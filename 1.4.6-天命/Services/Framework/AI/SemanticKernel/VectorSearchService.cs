using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.KernelMemory;
using TM.Services.Framework.AI.Core;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public class VectorSearchService
    {
        private IKernelMemory? _memory;
        private readonly LocalEmbeddingService _localEmbedding = new();
        private volatile bool _isInitialized;
        private string _currentProjectName = string.Empty;
        private readonly HashSet<string> _indexedChapters = new();
        private readonly object _indexedChaptersLock = new();
        private int _initEpoch;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        private const string CollectionName = "chapters";

        private SearchMode _currentMode = SearchMode.None;

        public VectorSearchService() { }

        public bool IsAvailable => _isInitialized;
        public SearchMode CurrentMode => _currentMode;

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

                var epoch = Volatile.Read(ref _initEpoch);

                _currentProjectName = currentProject;

                await TryLoadIndexedChaptersAsync();
                if (epoch != Volatile.Read(ref _initEpoch))
                    return;

                try
                {
                    await _localEmbedding.InitializeAsync();
                    if (_localEmbedding.IsAvailable)
                    {
                        _currentMode = SearchMode.LocalEmbedding;
                        TM.App.Log("[VectorSearchService] 本地向量搜索已启用");
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[VectorSearchService] 本地向量初始化失败: {ex.Message}");
                }

                if (epoch != Volatile.Read(ref _initEpoch))
                    return;

                try
                {
                    var config = GetOpenAIConfig();
                    if (config != null)
                    {
                        _memory = new KernelMemoryBuilder()
                            .WithOpenAIDefaults(config.ApiKey)
                            .Build<MemoryServerless>();

                        var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
                        if (Directory.Exists(chaptersPath))
                        {
                            var mdFiles = Directory.GetFiles(chaptersPath, "*.md", SearchOption.TopDirectoryOnly);
                            foreach (var file in mdFiles)
                            {
                                if (epoch != Volatile.Read(ref _initEpoch))
                                    return;

                                var chapterId = Path.GetFileNameWithoutExtension(file);
                                var alreadyIndexed = false;
                                lock (_indexedChaptersLock)
                                {
                                    alreadyIndexed = _indexedChapters.Contains(chapterId);
                                }
                                if (alreadyIndexed) continue;

                                await IndexChapterWithKernelMemoryAsync(chapterId, file);
                            }
                        }

                        _currentMode = SearchMode.Hybrid;
                        int indexedCount;
                        lock (_indexedChaptersLock) { indexedCount = _indexedChapters.Count; }
                        TM.App.Log($"[VectorSearchService] 混合搜索模式已启用（本地+OpenAI），索引 {indexedCount} 章节");
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[VectorSearchService] OpenAI向量初始化失败: {ex.Message}，使用本地向量");
                }

                if (epoch != Volatile.Read(ref _initEpoch))
                    return;

                if (_currentMode == SearchMode.None)
                {
                    await InitializeFallbackAsync();
                    if (epoch != Volatile.Read(ref _initEpoch))
                        return;

                    _currentMode = SearchMode.Keyword;
                    TM.App.Log("[VectorSearchService] keyword mode");
                }

                _isInitialized = true;
            }
            finally
            {
                _initLock.Release();
            }
        }

        public async Task IndexChapterAsync(string chapterId, string? filePath = null)
        {
            if (!_isInitialized)
                await InitializeAsync();

            filePath ??= Path.Combine(StoragePathHelper.GetProjectChaptersPath(), $"{chapterId}.md");

            if (_currentMode == SearchMode.Keyword)
            {
                await InvalidateFallbackCacheAsync(chapterId);
                return;
            }

            var localOk = false;
            var kmOk = false;

            if (_localEmbedding.IsAvailable)
            {
                _localEmbedding.RemoveChapterIndex(chapterId);
                await _localEmbedding.IndexChapterAsync(chapterId, filePath);
                localOk = _localEmbedding.IsChapterIndexed(chapterId);
            }

            kmOk = await IndexChapterWithKernelMemoryAsync(chapterId, filePath);

            if (!localOk && !kmOk)
                throw new InvalidOperationException($"[VectorSearchService] 向量索引失败: {chapterId}");
        }

        private async Task<bool> IndexChapterWithKernelMemoryAsync(string chapterId, string filePath)
        {
            var memory = _memory;
            if (memory == null)
                return false;

            try
            {
                if (!File.Exists(filePath))
                    return false;

                var existed = false;
                lock (_indexedChaptersLock)
                {
                    existed = _indexedChapters.Contains(chapterId);
                }

                if (existed)
                {
                    await memory.DeleteDocumentAsync(chapterId);
                }

                var content = await File.ReadAllTextAsync(filePath);
                await memory.ImportTextAsync(content, chapterId, tags: new TagCollection
                {
                    { "chapterId", chapterId },
                    { "project", _currentProjectName }
                });

                lock (_indexedChaptersLock)
                {
                    _indexedChapters.Add(chapterId);
                }
                await SaveIndexedChaptersToDiskAsync().ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[VectorSearchService] Kernel Memory索引失败 {chapterId}: {ex.Message}");
                return false;
            }
        }

        public async Task RemoveChapterAsync(string chapterId)
        {
            lock (_indexedChaptersLock)
            {
                _indexedChapters.Remove(chapterId);
            }
            _localEmbedding.RemoveChapterIndex(chapterId);
            await InvalidateFallbackCacheAsync(chapterId);

            var memory = _memory;
            if (memory != null)
            {
                try
                {
                    await memory.DeleteDocumentAsync(chapterId);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[VectorSearchService] Kernel Memory删除 {chapterId} 失败（非致命）: {ex.Message}");
                }
            }

            TM.App.Log($"[VectorSearchService] 已移除章节索引: {chapterId}");

            await SaveIndexedChaptersToDiskAsync().ConfigureAwait(false);
        }

        private string GetIndexedChaptersPath() => System.IO.Path.Combine(
            StoragePathHelper.GetCurrentProjectPath(), "VectorIndex", "indexed_chapters.json");

        private async Task TryLoadIndexedChaptersAsync()
        {
            try
            {
                var path = GetIndexedChaptersPath();
                if (!File.Exists(path)) return;
                var json = await File.ReadAllTextAsync(path);
                var ids = JsonSerializer.Deserialize<List<string>>(json);
                if (ids == null) return;

                int count;
                lock (_indexedChaptersLock)
                {
                    foreach (var id in ids) _indexedChapters.Add(id);
                    count = _indexedChapters.Count;
                }
                TM.App.Log($"[VectorSearchService] 已恢复 {count} 个 KernelMemory 索引记录");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[VectorSearchService] 加载索引记录失败（非致命）: {ex.Message}");
            }
        }

        private async Task SaveIndexedChaptersToDiskAsync()
        {
            try
            {
                var path = GetIndexedChaptersPath();
                var dir = Path.GetDirectoryName(path)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                List<string> snapshot;
                lock (_indexedChaptersLock)
                {
                    snapshot = _indexedChapters.ToList();
                }
                var json = JsonSerializer.Serialize(snapshot);
                var tmpPath = path + ".tmp";
                await File.WriteAllTextAsync(tmpPath, json);
                File.Move(tmpPath, path, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[VectorSearchService] 保存索引记录失败（非致命）: {ex.Message}");
            }
        }

        public void ClearIndex()
        {
            lock (_indexedChaptersLock)
            {
                _indexedChapters.Clear();
            }
            _memory = null;
            _isInitialized = false;
            _ = Task.Run(async () =>
            {
                await _fallbackCacheSemaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    _fallbackChunkCache.Clear();
                    _fallbackLru.Clear();
                    _fallbackLruNodes.Clear();
                }
                finally
                {
                    _fallbackCacheSemaphore.Release();
                }
            });
            _localEmbedding.ClearIndex();
            _currentMode = SearchMode.None;
            Interlocked.Increment(ref _initEpoch);
            TM.App.Log("[VectorSearchService] 索引已清空");
        }

        public async Task<List<SearchResult>> SearchAsync(string query, int topK = 5)
        {
            if (!_isInitialized)
                await InitializeAsync();

            var results = new List<SearchResult>();

            switch (_currentMode)
            {
                case SearchMode.Hybrid:
                    var localResults = await _localEmbedding.SearchAsync(query, topK);
                    if (localResults.Count >= topK)
                    {
                        results = localResults;
                    }
                    else
                    {
                        var openaiResults = await SearchWithKernelMemoryAsync(query, topK);
                        results = MergeAndDeduplicate(localResults, openaiResults, topK);
                    }
                    break;

                case SearchMode.LocalEmbedding:
                    results = await _localEmbedding.SearchAsync(query, topK);
                    break;

                case SearchMode.Keyword:
                default:
                    results = await SearchWithFallbackAsync(query, topK);
                    break;
            }

            return results;
        }

        private static List<SearchResult> MergeAndDeduplicate(
            List<SearchResult> results1, 
            List<SearchResult> results2, 
            int topK)
        {
            var merged = new Dictionary<string, SearchResult>();

            foreach (var r in results1.Concat(results2))
            {
                var key = $"{r.ChapterId}_{r.Position}";
                if (!merged.ContainsKey(key) || merged[key].Score < r.Score)
                {
                    merged[key] = r;
                }
            }

            return merged.Values
                .OrderByDescending(r => r.Score)
                .Take(topK)
                .ToList();
        }

        public async Task<List<SearchResult>> SearchByChapterAsync(string chapterId, int topK = 2)
        {
            if (!_isInitialized)
                await InitializeAsync();

            var chunks = _localEmbedding.GetChunksByChapterId(chapterId, topK);
            if (chunks.Count > 0)
                return chunks;

            var fallback = await GetFallbackChunksAsync(chapterId, topK);
            return fallback;
        }

        private async Task<List<SearchResult>> GetFallbackChunksAsync(string chapterId, int topK)
        {
            var results = new List<SearchResult>();
            try
            {
                var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
                var filePath = Path.Combine(chaptersPath, $"{chapterId}.md");
                if (!File.Exists(filePath)) return results;

                var content = await File.ReadAllTextAsync(filePath);
                if (string.IsNullOrWhiteSpace(content)) return results;

                var chunks = ChunkContent(chapterId, content);
                foreach (var chunk in chunks.Take(topK))
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
            catch (Exception ex)
            {
                TM.App.Log($"[VectorSearchService] fallback read err: {ex.Message}");
            }
            return results;
        }

        public async Task<List<string>> FindRelatedChaptersAsync(string query, int topK = 5)
        {
            var results = await SearchAsync(query, topK * 2);
            return results
                .Select(r => r.ChapterId)
                .Distinct()
                .Take(topK)
                .ToList();
        }

        #region KM

        private async Task<List<SearchResult>> SearchWithKernelMemoryAsync(string query, int topK)
        {
            var results = new List<SearchResult>();

            try
            {
                var memory = _memory;
                if (memory == null)
                    return results;

                var searchResult = await memory.SearchAsync(query, limit: topK);

                foreach (var citation in searchResult.Results)
                {
                    var chapterId = citation.DocumentId;

                    foreach (var partition in citation.Partitions)
                    {
                        results.Add(new SearchResult
                        {
                            ChapterId = chapterId,
                            Position = partition.PartitionNumber,
                            Content = partition.Text,
                            Score = partition.Relevance
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[VectorSearchService] search err: {ex.Message}");
            }

            return results.OrderByDescending(r => r.Score).Take(topK).ToList();
        }

        private static UserConfiguration? GetOpenAIConfig()
        {
            var configs = ServiceLocator.Get<AIService>().GetAllConfigurations();
            return configs.FirstOrDefault(c => 
                c.ProviderId.Equals("openai", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(c.ApiKey));
        }

        #endregion

        #region TextMatch

        private const int FallbackChapterCacheSize = 12;
        private readonly Dictionary<string, List<ChapterChunk>> _fallbackChunkCache = new();
        private readonly LinkedList<string> _fallbackLru = new();
        private readonly Dictionary<string, LinkedListNode<string>> _fallbackLruNodes = new();
        private readonly SemaphoreSlim _fallbackCacheSemaphore = new(1, 1);

        private Task InitializeFallbackAsync()
        {
            _fallbackChunkCache.Clear();
            _fallbackLru.Clear();
            _fallbackLruNodes.Clear();
            _currentProjectName = StoragePathHelper.CurrentProjectName;

            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
            if (!Directory.Exists(chaptersPath))
            {
                return Task.CompletedTask;
            }

            var mdFileCount = Directory.GetFiles(chaptersPath, "*.md", SearchOption.TopDirectoryOnly).Length;
            TM.App.Log($"[VectorSearchService] fallback init: {mdFileCount} files");

            return Task.CompletedTask;
        }

        private async Task<List<SearchResult>> SearchWithFallbackAsync(string query, int topK)
        {
            var chaptersPath = StoragePathHelper.GetProjectChaptersPath();
            if (!Directory.Exists(chaptersPath))
                return new List<SearchResult>();

            if (topK <= 0)
                return new List<SearchResult>();

            var queryTerms = query.Split(new[] { ' ', '，', ',', '、' }, StringSplitOptions.RemoveEmptyEntries);
            if (queryTerms.Length == 0)
                return new List<SearchResult>();

            var mdFiles = Directory.GetFiles(chaptersPath, "*.md", SearchOption.TopDirectoryOnly);
            var topResults = new List<SearchResult>(topK);

            foreach (var file in mdFiles)
            {
                var chapterId = Path.GetFileNameWithoutExtension(file);
                var chunks = await GetOrLoadFallbackChunksAsync(chapterId, file);
                if (chunks.Count == 0)
                    continue;

                foreach (var chunk in chunks)
                {
                    var score = CalculateRelevance(queryTerms, chunk.Content);
                    if (score <= 0)
                        continue;

                    if (topResults.Count == topK && score <= topResults[topResults.Count - 1].Score)
                        continue;

                    var result = new SearchResult
                    {
                        ChapterId = chunk.ChapterId,
                        Position = chunk.Position,
                        Content = chunk.Content,
                        Score = score

                    };

                    var insertIndex = 0;
                    while (insertIndex < topResults.Count && topResults[insertIndex].Score >= result.Score)
                        insertIndex++;

                    topResults.Insert(insertIndex, result);
                    if (topResults.Count > topK)
                        topResults.RemoveAt(topResults.Count - 1);
                }
            }

            return topResults;
        }

        private async Task<List<ChapterChunk>> GetOrLoadFallbackChunksAsync(string chapterId, string filePath)
        {
            await _fallbackCacheSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_fallbackChunkCache.TryGetValue(chapterId, out var cached))
                {
                    TouchFallbackLru(chapterId);
                    return cached;
                }
            }
            finally
            {
                _fallbackCacheSemaphore.Release();
            }

            if (!File.Exists(filePath))
                return new List<ChapterChunk>();

            var content = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            var chunks = ChunkContent(chapterId, content);

            await _fallbackCacheSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_fallbackChunkCache.TryGetValue(chapterId, out var cached))
                {
                    TouchFallbackLru(chapterId);
                    return cached;
                }

                _fallbackChunkCache[chapterId] = chunks;
                TouchFallbackLru(chapterId);

                while (_fallbackLru.Count > FallbackChapterCacheSize)
                {
                    var toRemove = _fallbackLru.First?.Value;
                    if (string.IsNullOrEmpty(toRemove))
                        break;

                    _fallbackLru.RemoveFirst();
                    _fallbackLruNodes.Remove(toRemove);
                    _fallbackChunkCache.Remove(toRemove);
                }

                return chunks;
            }
            finally
            {
                _fallbackCacheSemaphore.Release();
            }
        }

        private void TouchFallbackLru(string chapterId)
        {
            if (_fallbackLruNodes.TryGetValue(chapterId, out var node))
            {
                _fallbackLru.Remove(node);
            }

            var newNode = _fallbackLru.AddLast(chapterId);
            _fallbackLruNodes[chapterId] = newNode;
        }

        private async Task InvalidateFallbackCacheAsync(string chapterId)
        {
            await _fallbackCacheSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                _fallbackChunkCache.Remove(chapterId);
                if (_fallbackLruNodes.TryGetValue(chapterId, out var node))
                {
                    _fallbackLru.Remove(node);
                    _fallbackLruNodes.Remove(chapterId);
                }
            }
            finally
            {
                _fallbackCacheSemaphore.Release();
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

        private static double CalculateRelevance(string[] queryTerms, string content)
        {
            if (queryTerms.Length == 0 || string.IsNullOrEmpty(content))
                return 0;

            var matchCount = queryTerms.Count(term =>
                content.Contains(term, StringComparison.OrdinalIgnoreCase));

            return (double)matchCount / queryTerms.Length;
        }

        #endregion

        #region ConversationSearch

        public void SwitchConversationSession(string? sessionId)
        {
            _localEmbedding.SwitchConversationSession(sessionId);
        }

        public void SwitchConversationSession(string? sessionId, bool forceClear)
        {
            _localEmbedding.SwitchConversationSession(sessionId, forceClear);
        }

        public void IndexConversationTurn(int turnIndex, string userText, string assistantText)
        {
            _localEmbedding.IndexConversationTurn(turnIndex, userText, assistantText);
        }

        public List<ConversationSearchResult> SearchConversation(string query, int topK = 3, int excludeRecentTurns = 12)
        {
                        switch (_currentMode)
            {
                case SearchMode.Hybrid:
                case SearchMode.LocalEmbedding:
                                        return _localEmbedding.SearchConversation(query, topK, excludeRecentTurns);

                case SearchMode.Keyword:
                default:
                    return new List<ConversationSearchResult>();
            }
        }

        public int IndexedConversationTurns => _localEmbedding.IndexedConversationTurns;

        public void SaveConversationIndex(string filePath) => _localEmbedding.SaveConversationIndex(filePath);

        public void LoadConversationIndex(string filePath) => _localEmbedding.LoadConversationIndex(filePath);

        public static string FormatRecalledContext(List<ConversationSearchResult> results)
        {
            if (results.Count == 0) return string.Empty;

            var lines = new System.Text.StringBuilder();
            lines.AppendLine("<context_block source=\"semantic_recall\">");
            foreach (var r in results)
            {
                lines.AppendLine($"[轮次 {r.TurnIndex + 1}，相关度 {r.Score:F2}]");
                lines.AppendLine($"  用户: {r.UserText}");
                lines.AppendLine($"  助手: {r.AssistantText}");
                lines.AppendLine();
            }
            lines.AppendLine("</context_block>");

            return lines.ToString();
        }

        #endregion
    }

    public class ChapterChunk
    {
        public string ChapterId { get; set; } = string.Empty;
        public int Position { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    public class SearchResult
    {
        public string ChapterId { get; set; } = string.Empty;
        public int Position { get; set; }
        public string Content { get; set; } = string.Empty;
        public double Score { get; set; }
    }

    public enum SearchMode
    {
        None,
        Keyword,
        LocalEmbedding,
        Hybrid
    }
}
