using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TM.Framework.Common.Helpers;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.SemanticKernel.Embedding;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public class LocalEmbeddingService
    {
        private ITextEmbedder? _embedder;
        private readonly ConcurrentDictionary<string, float[]> _chapterEmbeddings = new();
        private readonly ConcurrentBag<ChapterChunkEmbedding> _chunkEmbeddings = new();
        private readonly object _chunkEmbeddingsLock = new();
        private readonly ConcurrentDictionary<string, ChapterIndexMeta> _indexMeta = new();
        private readonly ConcurrentDictionary<string, HashSet<string>> _keywordIndex = new();
        private string _currentProjectName = string.Empty;
        private volatile bool _isInitialized;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        private readonly List<ConversationTurnEmbedding> _conversationTurns = new();
        private string? _currentConversationSessionId;

        private const double MinScoreThreshold = 0.3;
        private const int MaxParallelism = 4;
        private const int ChunkSize = 500;
        private const int ChunkOverlap = 50;
        private const int ConversationMaxContentLength = 800;

        public event EventHandler<string>? InitializationFailed;

        public bool IsAvailable => _isInitialized && _embedder != null;
        public int IndexedChapters => _chapterEmbeddings.Count;
        public int IndexedChunks => _chunkEmbeddings.Count;
        public int IndexedConversationTurns => _conversationTurns.Count;

        public bool IsChapterIndexed(string chapterId) => _indexMeta.ContainsKey(chapterId);

        public async Task InitializeAsync()
        {
            await _initLock.WaitAsync();
            try
            {
                var currentProject = StoragePathHelper.CurrentProjectName;

                if (_isInitialized && _currentProjectName == currentProject)
                    return;

                if (_currentProjectName != currentProject)
                {
                    ClearIndex();
                }

                try
                {
                    _embedder = CreateEmbedder();
                    _currentProjectName = currentProject;

                    if (await TryLoadIndexFromDiskAsync())
                    {
                        TM.App.Log($"[LocalEmbeddingService] 从磁盘加载索引: {_indexMeta.Count} 章节元数据");
                    }

                    await IncrementalIndexAsync();

                    _isInitialized = true;
                    TM.App.Log($"[LocalEmbeddingService] 初始化完成，索引 {_chapterEmbeddings.Count} 章节，{_chunkEmbeddings.Count} 分块");
                }
                catch (Exception ex)
                {
                    var errorMsg = $"本地向量服务初始化失败: {ex.Message}";
                    TM.App.Log($"[LocalEmbeddingService] {errorMsg}");
                    InitializationFailed?.Invoke(this, errorMsg);
                    _isInitialized = false;
                }
            }
            finally
            {
                _initLock.Release();
            }
        }

        private async Task IncrementalIndexAsync()
        {
            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
            if (!Directory.Exists(chaptersPath))
                return;

            var mdFiles = Directory.GetFiles(chaptersPath, "*.md", SearchOption.TopDirectoryOnly);
            var currentChapterIds = new ConcurrentBag<string>();
            var filesToIndex = new ConcurrentBag<(string chapterId, string filePath)>();

            Parallel.ForEach(mdFiles, new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism }, file =>
            {
                var chapterId = Path.GetFileNameWithoutExtension(file);
                currentChapterIds.Add(chapterId);

                var fileHash = ComputeFileHash(file);

                if (!_indexMeta.TryGetValue(chapterId, out var meta) || meta.FileHash != fileHash)
                {
                    filesToIndex.Add((chapterId, file));
                }
            });

            var indexTasks = filesToIndex.Select(async item =>
            {
                await IndexChapterAsync(item.chapterId, item.filePath);
            });
            await Task.WhenAll(indexTasks);

            var currentIds = currentChapterIds.ToHashSet();
            var deletedChapters = _indexMeta.Keys.Except(currentIds).ToList();
            foreach (var chapterId in deletedChapters)
            {
                RemoveChapterIndex(chapterId);
            }

            if (filesToIndex.Count > 0 || deletedChapters.Count > 0)
            {
                await SaveIndexToDiskAsync();
                TM.App.Log($"[LocalEmbeddingService] 增量更新: {filesToIndex.Count} 新增/修改，{deletedChapters.Count} 删除");
            }
        }

        public Task IndexChapterAsync(string chapterId, string? filePath = null)
        {
            if (_embedder == null)
                return Task.CompletedTask;

            try
            {
                filePath ??= Path.Combine(StoragePathHelper.GetProjectChaptersPath(), $"{chapterId}.md");
                if (!File.Exists(filePath))
                    return Task.CompletedTask;

                _chapterEmbeddings.TryRemove(chapterId, out _);
                PurgeChunksByChapterId(chapterId);

                var content = File.ReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(content))
                    return Task.CompletedTask;

                var chapterEmbedding = _embedder.Embed(content.Length > 2000 ? content.Substring(0, 2000) : content);
                _chapterEmbeddings[chapterId] = chapterEmbedding;

                var chunks = ChunkContent(chapterId, content);
                var newChunkEmbeddings = new List<ChapterChunkEmbedding>(chunks.Count);
                foreach (var chunk in chunks)
                {
                    var embedding = _embedder.Embed(chunk.Content);
                    newChunkEmbeddings.Add(new ChapterChunkEmbedding
                    {
                        ChapterId = chunk.ChapterId,
                        Position = chunk.Position,
                        Content = chunk.Content,
                        Embedding = embedding
                    });
                }

                lock (_chunkEmbeddingsLock)
                {
                    PurgeChunksByChapterId(chapterId);
                    foreach (var item in newChunkEmbeddings)
                    {
                        _chunkEmbeddings.Add(item);
                    }
                }

                BuildKeywordIndex(chapterId, content);

                _indexMeta[chapterId] = new ChapterIndexMeta
                {
                    ChapterId = chapterId,
                    FileHash = ComputeFileHash(filePath),
                    IndexTime = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LocalEmbeddingService] 索引章节失败 {chapterId}: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        public void ClearIndex()
        {
            _chapterEmbeddings.Clear();
            lock (_chunkEmbeddingsLock)
            {
                while (_chunkEmbeddings.TryTake(out _)) { }
            }
            _indexMeta.Clear();
            _keywordIndex.Clear();
            lock (_conversationTurns) { _conversationTurns.Clear(); }
            _currentConversationSessionId = null;
            _isInitialized = false;
            _embedder?.Dispose();
            _embedder = null;
        }

        private static ITextEmbedder CreateEmbedder()
        {
            var modelPath = StoragePathHelper.GetFilePath("Services", "Framework/AI/EmbeddingModels/bge-small-zh", "model.onnx");
            var vocabPath = StoragePathHelper.GetFilePath("Services", "Framework/AI/EmbeddingModels/bge-small-zh", "vocab.txt");

            return new OnnxTextEmbedder(modelPath, vocabPath);
        }

        public void RemoveChapterIndex(string chapterId)
        {
            _chapterEmbeddings.TryRemove(chapterId, out _);
            _indexMeta.TryRemove(chapterId, out _);
            foreach (var keywords in _keywordIndex.Values)
            {
                lock (keywords)
                {
                    keywords.Remove(chapterId);
                }
            }
            PurgeChunksByChapterId(chapterId);
        }

        public List<SearchResult> GetChunksByChapterId(string chapterId, int topK = 2)
        {
            var results = new List<SearchResult>();

            lock (_chunkEmbeddingsLock)
            {
                var chunks = _chunkEmbeddings
                    .Where(c => string.Equals(c.ChapterId, chapterId, StringComparison.Ordinal))
                    .OrderBy(c => c.Position)
                    .Take(topK);

                foreach (var chunk in chunks)
                {
                    results.Add(new SearchResult
                    {
                        ChapterId = chunk.ChapterId,
                        Position = chunk.Position,
                        Content = chunk.Content,
                        Score = 1.0f
                    });
                }
            }

            return results;
        }

        public async Task<List<SearchResult>> SearchAsync(string query, int topK = 5, double? minScore = null)
        {
            if (!_isInitialized)
                await InitializeAsync();

            if (_embedder == null || _chunkEmbeddings.IsEmpty)
                return new List<SearchResult>();

            var threshold = minScore ?? MinScoreThreshold;

            try
            {
                var candidateChapterIds = PrefilterByKeywords(query);

                var queryEmbedding = _embedder.Embed(query);

                IEnumerable<ChapterChunkEmbedding> searchScope = _chunkEmbeddings;

                if (candidateChapterIds.Count > 0 && candidateChapterIds.Count < _chapterEmbeddings.Count * 0.5)
                {
                    searchScope = _chunkEmbeddings.Where(c => candidateChapterIds.Contains(c.ChapterId));
                }

                var results = searchScope
                    .AsParallel()
                    .WithDegreeOfParallelism(MaxParallelism)
                    .Select(chunk => new SearchResult
                    {
                        ChapterId = chunk.ChapterId,
                        Position = chunk.Position,
                        Content = chunk.Content,
                        Score = OnnxTextEmbedder.CosineSimilarity(queryEmbedding, chunk.Embedding)
                    })
                    .Where(r => r.Score >= threshold)
                    .OrderByDescending(r => r.Score)
                    .Take(topK)
                    .ToList();

                return results;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LocalEmbeddingService] 搜索失败: {ex.Message}");
                return new List<SearchResult>();
            }
        }

        private HashSet<string> PrefilterByKeywords(string query)
        {
            var candidates = new HashSet<string>();
            var queryTerms = query.Split(new[] { ' ', '，', ',', '、', '。', '！', '？' }, 
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var term in queryTerms)
            {
                if (_keywordIndex.TryGetValue(term.ToLower(), out var chapterIds))
                {
                    if (candidates.Count == 0)
                        candidates.UnionWith(chapterIds);
                    else
                        candidates.IntersectWith(chapterIds);
                }
            }

            return candidates;
        }

        public async Task<List<string>> FindRelatedChaptersAsync(string query, int topK = 5)
        {
            if (!_isInitialized)
                await InitializeAsync();

            if (_embedder == null || _chapterEmbeddings.Count == 0)
                return new List<string>();

            try
            {
                var queryEmbedding = _embedder.Embed(query);

                return _chapterEmbeddings
                    .Select(kvp => new { ChapterId = kvp.Key, Score = OnnxTextEmbedder.CosineSimilarity(queryEmbedding, kvp.Value) })
                    .OrderByDescending(x => x.Score)
                    .Take(topK)
                    .Select(x => x.ChapterId)
                    .ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LocalEmbeddingService] 查找章节失败: {ex.Message}");
                return new List<string>();
            }
        }

        private static List<ChapterChunk> ChunkContent(string chapterId, string content)
        {
            var chunks = new List<ChapterChunk>();
            if (string.IsNullOrEmpty(content))
                return chunks;

            var paragraphs = content.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            var position = 0;
            var buffer = "";
            const int targetSize = 500;
            const int overlap = 50;

            foreach (var para in paragraphs)
            {
                var trimmed = para.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                buffer += (buffer.Length > 0 ? "\n" : "") + trimmed;

                if (buffer.Length >= targetSize)
                {
                    chunks.Add(new ChapterChunk
                    {
                        ChapterId = chapterId,
                        Position = position,
                        Content = buffer
                    });

                    var overlapStart = Math.Max(0, buffer.Length - overlap);
                    buffer = buffer.Substring(overlapStart);
                    position++;
                }
            }

            if (!string.IsNullOrEmpty(buffer))
            {
                chunks.Add(new ChapterChunk
                {
                    ChapterId = chapterId,
                    Position = position,
                    Content = buffer
                });
            }

            return chunks;
        }

        #region 索引持久化

        private string GetIndexPath() => Path.Combine(
            StoragePathHelper.GetCurrentProjectPath(), "VectorIndex", "local_embeddings.json");

        private async Task<bool> TryLoadIndexFromDiskAsync()
        {
            try
            {
                var indexPath = GetIndexPath();
                if (!File.Exists(indexPath))
                    return false;

                var json = await File.ReadAllTextAsync(indexPath);
                var data = JsonSerializer.Deserialize<IndexPersistenceData>(json);
                if (data == null)
                    return false;

                _indexMeta.Clear();
                foreach (var meta in data.ChapterMeta)
                {
                    _indexMeta[meta.ChapterId] = meta;
                }

                _keywordIndex.Clear();
                foreach (var kvp in data.KeywordIndex)
                {
                    _keywordIndex[kvp.Key] = new HashSet<string>(kvp.Value);
                }

                _chapterEmbeddings.Clear();
                if (data.ChapterEmbeddings != null)
                {
                    foreach (var kvp in data.ChapterEmbeddings)
                    {
                        _chapterEmbeddings[kvp.Key] = kvp.Value;
                    }
                }

                lock (_chunkEmbeddingsLock)
                {
                    while (_chunkEmbeddings.TryTake(out _)) { }
                    if (data.ChunkEmbeddings != null)
                    {
                        foreach (var chunk in data.ChunkEmbeddings)
                        {
                            _chunkEmbeddings.Add(chunk);
                        }
                    }
                }

                if (_indexMeta.Count > 0 && (_chapterEmbeddings.Count == 0 || _chunkEmbeddings.IsEmpty))
                {
                    TM.App.Log("[LocalEmbeddingService] 检测到旧索引文件缺少向量数据，将触发重建");
                    _indexMeta.Clear();
                    _keywordIndex.Clear();
                    _chapterEmbeddings.Clear();
                    lock (_chunkEmbeddingsLock)
                    {
                        while (_chunkEmbeddings.TryTake(out _)) { }
                    }
                    return false;
                }

                TM.App.Log($"[LocalEmbeddingService] 向量缓存已恢复: {_chapterEmbeddings.Count} 章节, {_chunkEmbeddings.Count} 分块");
                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LocalEmbeddingService] 加载索引失败: {ex.Message}");
                return false;
            }
        }

        private async Task SaveIndexToDiskAsync()
        {
            try
            {
                var indexPath = GetIndexPath();
                var dir = Path.GetDirectoryName(indexPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                List<ChapterChunkEmbedding> chunkSnapshot;
                lock (_chunkEmbeddingsLock)
                {
                    chunkSnapshot = _chunkEmbeddings.ToList();
                }

                var data = new IndexPersistenceData
                {
                    ChapterMeta = _indexMeta.Values.ToList(),
                    KeywordIndex = _keywordIndex.ToDictionary(
                        kvp => kvp.Key, 
                        kvp => kvp.Value.ToList()),
                    ChapterEmbeddings = _chapterEmbeddings.ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value),
                    ChunkEmbeddings = chunkSnapshot
                        .Where(c => _indexMeta.ContainsKey(c.ChapterId))
                        .ToList()
                };

                var json = JsonSerializer.Serialize(data, JsonHelper.Default);
                var tmpPath = indexPath + ".tmp";
                await File.WriteAllTextAsync(tmpPath, json);
                File.Move(tmpPath, indexPath, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LocalEmbeddingService] 保存索引失败: {ex.Message}");
            }
        }

        private void PurgeChunksByChapterId(string chapterId)
        {
            lock (_chunkEmbeddingsLock)
            {
                var kept = _chunkEmbeddings.Where(c => c.ChapterId != chapterId).ToList();
                while (_chunkEmbeddings.TryTake(out _)) { }
                foreach (var chunk in kept)
                {
                    _chunkEmbeddings.Add(chunk);
                }
            }
        }

        private static string ComputeFileHash(string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            return $"{fileInfo.Length}_{fileInfo.LastWriteTimeUtc.Ticks}";
        }

        private void BuildKeywordIndex(string chapterId, string content)
        {
            var words = ExtractKeywords(content);
            foreach (var word in words)
            {
                var key = word.ToLower();
                if (!_keywordIndex.ContainsKey(key))
                    _keywordIndex[key] = new HashSet<string>();
                _keywordIndex[key].Add(chapterId);
            }
        }

        private static HashSet<string> ExtractKeywords(string content)
        {
            var keywords = new HashSet<string>();
            if (string.IsNullOrEmpty(content))
                return keywords;

            var separators = new[] { ' ', '\n', '\r', '\t', ',', '.', '!', '?', ':', ';', '(', ')', '[', ']', '{', '}' };
            var words = content.Split(separators, StringSplitOptions.RemoveEmptyEntries);

            foreach (var word in words)
            {
                var trimmed = word.Trim();
                if (trimmed.Length >= 2)
                    keywords.Add(trimmed);
            }

            return keywords;
        }

        #endregion

        #region 对话向量索引

        private void EnsureEmbedderForConversation()
        {
            if (_embedder != null) return;

            try
            {
                _embedder = CreateEmbedder();
                if (string.IsNullOrEmpty(_currentProjectName))
                {
                    _currentProjectName = StoragePathHelper.CurrentProjectName;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LocalEmbeddingService] 对话 Embedder 初始化失败: {ex.Message}");
            }
        }

        public void SwitchConversationSession(string? sessionId, bool forceClear = false)
        {
            if (!forceClear && string.Equals(_currentConversationSessionId, sessionId, StringComparison.Ordinal))
                return;

            _currentConversationSessionId = sessionId;
            lock (_conversationTurns) { _conversationTurns.Clear(); }
            TM.App.Log($"[LocalEmbeddingService] 对话索引已切换: {sessionId ?? "null"}");
        }

        public void IndexConversationTurn(int turnIndex, string userText, string assistantText)
        {
            EnsureEmbedderForConversation();
            if (_embedder == null) return;

            try
            {
                var userTrunc = Truncate(userText, ConversationMaxContentLength);
                var assistTrunc = Truncate(assistantText, ConversationMaxContentLength);
                var combined = $"用户: {userTrunc}\n助手: {assistTrunc}";
                var embedding = _embedder.Embed(combined);

                lock (_conversationTurns)
                {
                    _conversationTurns.Add(new ConversationTurnEmbedding
                    {
                        TurnIndex = turnIndex,
                        UserText = userTrunc,
                        AssistantText = assistTrunc,
                        Embedding = embedding
                    });
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LocalEmbeddingService] 对话索引 turn {turnIndex} 失败: {ex.Message}");
            }
        }

        public List<ConversationSearchResult> SearchConversation(string query, int topK = 3, int excludeRecentTurns = 12, double? minScore = null)
        {
            EnsureEmbedderForConversation();
            if (_embedder == null) return new List<ConversationSearchResult>();

            var threshold = minScore ?? MinScoreThreshold;

            try
            {
                var queryEmbedding = _embedder.Embed(query);

                List<ConversationTurnEmbedding> snapshot;
                lock (_conversationTurns) { snapshot = _conversationTurns.ToList(); }

                if (snapshot.Count == 0)
                    return new List<ConversationSearchResult>();

                var maxTurnIndex = snapshot.Max(t => t.TurnIndex);
                var excludeFrom = maxTurnIndex - excludeRecentTurns + 1;

                var results = snapshot
                    .Where(t => t.TurnIndex < excludeFrom)
                    .Select(t => new ConversationSearchResult
                    {
                        TurnIndex = t.TurnIndex,
                        UserText = t.UserText,
                        AssistantText = t.AssistantText,
                        Score = OnnxTextEmbedder.CosineSimilarity(queryEmbedding, t.Embedding)
                    })
                    .Where(r => r.Score >= threshold)
                    .OrderByDescending(r => r.Score)
                    .Take(topK)
                    .ToList();

                return results;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LocalEmbeddingService] 对话搜索失败: {ex.Message}");
                return new List<ConversationSearchResult>();
            }
        }

        public void SaveConversationIndex(string filePath)
        {
            try
            {
                List<ConversationTurnRecord> records;
                lock (_conversationTurns)
                {
                    records = _conversationTurns.Select(t => new ConversationTurnRecord
                    {
                        TurnIndex = t.TurnIndex,
                        UserText = t.UserText,
                        AssistantText = t.AssistantText
                    }).ToList();
                }

                if (records.Count == 0) return;

                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(records, JsonHelper.Compact);
                var tmp = filePath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, filePath, overwrite: true);
                TM.App.Log($"[LocalEmbeddingService] 对话索引已保存: {records.Count} 轮 → {filePath}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LocalEmbeddingService] 对话索引保存失败: {ex.Message}");
            }
        }

        public void LoadConversationIndex(string filePath)
        {
            if (!File.Exists(filePath)) return;

            try
            {
                EnsureEmbedderForConversation();
                if (_embedder == null) return;

                var json = File.ReadAllText(filePath);
                var records = JsonSerializer.Deserialize<List<ConversationTurnRecord>>(json);
                if (records == null || records.Count == 0) return;

                lock (_conversationTurns)
                {
                    _conversationTurns.Clear();
                    foreach (var r in records)
                    {
                        var combined = $"用户: {r.UserText}\n助手: {r.AssistantText}";
                        var embedding = _embedder.Embed(combined);
                        _conversationTurns.Add(new ConversationTurnEmbedding
                        {
                            TurnIndex = r.TurnIndex,
                            UserText = r.UserText,
                            AssistantText = r.AssistantText,
                            Embedding = embedding
                        });
                    }
                }

                TM.App.Log($"[LocalEmbeddingService] 对话索引已加载: {records.Count} 轮 ← {filePath}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LocalEmbeddingService] 对话索引加载失败: {ex.Message}");
            }
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text ?? string.Empty;
            return text[..maxLength] + "…";
        }

        #endregion
    }

    internal class ChapterChunkEmbedding
    {
        [System.Text.Json.Serialization.JsonPropertyName("ChapterId")] public string ChapterId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Position")] public int Position { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Content")] public string Content { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Embedding")] public float[] Embedding { get; set; } = Array.Empty<float>();
    }

    internal class ChapterIndexMeta
    {
        [System.Text.Json.Serialization.JsonPropertyName("ChapterId")] public string ChapterId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("FileHash")] public string FileHash { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("IndexTime")] public DateTime IndexTime { get; set; }
    }

    internal class IndexPersistenceData
    {
        [System.Text.Json.Serialization.JsonPropertyName("ChapterMeta")] public List<ChapterIndexMeta> ChapterMeta { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("KeywordIndex")] public Dictionary<string, List<string>> KeywordIndex { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("ChapterEmbeddings")] public Dictionary<string, float[]> ChapterEmbeddings { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("ChunkEmbeddings")] public List<ChapterChunkEmbedding> ChunkEmbeddings { get; set; } = new();
    }

    public class ConversationTurnRecord
    {
        [System.Text.Json.Serialization.JsonPropertyName("TurnIndex")] public int TurnIndex { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("UserText")] public string UserText { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("AssistantText")] public string AssistantText { get; set; } = string.Empty;
    }

    internal class ConversationTurnEmbedding
    {
        [System.Text.Json.Serialization.JsonPropertyName("TurnIndex")] public int TurnIndex { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("UserText")] public string UserText { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("AssistantText")] public string AssistantText { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Embedding")] public float[] Embedding { get; set; } = Array.Empty<float>();
    }

    public class ConversationSearchResult
    {
        public int TurnIndex { get; set; }
        public string UserText { get; set; } = string.Empty;
        public string AssistantText { get; set; } = string.Empty;
        public double Score { get; set; }
    }
}
