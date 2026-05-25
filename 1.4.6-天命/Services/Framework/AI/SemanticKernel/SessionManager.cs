using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.UI.Workspace.Services;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public class SessionManager
    {

        private string _sessionsDir;
        private string? _currentSessionId;
        private readonly Dictionary<string, SessionInfo> _sessionIndex = new();
        private readonly SemaphoreSlim _writeSemaphore = new(1, 1);

        public SessionManager()
        {
            _sessionsDir = BuildSessionsDir();
            Directory.CreateDirectory(_sessionsDir);
            LoadSessionIndex();
            CleanupEmptySessions();

            StoragePathHelper.CurrentProjectChanged += OnProjectChanged;

            TM.App.Log($"[SessionManager] 初始化完成，会话目录: {_sessionsDir}");
        }

        private static string BuildSessionsDir()
            => Path.Combine(StoragePathHelper.GetCurrentProjectPath(), "Sessions");

        private void OnProjectChanged(string oldProject, string newProject)
        {
            _sessionsDir = BuildSessionsDir();
            Directory.CreateDirectory(_sessionsDir);
            _sessionIndex.Clear();
            _currentSessionId = null;
            LoadSessionIndex();
            TM.App.Log($"[SessionManager] 项目切换（{oldProject}→{newProject}），会话目录: {_sessionsDir}");
        }

        #region 会话管理

        public string CreateSession(string? title = null)
        {
            var sessionId = ShortIdGenerator.NewGuid().ToString("N")[..8];
            var info = new SessionInfo
            {
                Id = sessionId,
                Title = title ?? $"会话 {DateTime.Now:MM-dd HH:mm}",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                ContextChapterId = CurrentChapterTracker.CurrentChapterId
            };

            _sessionIndex[sessionId] = info;
            _currentSessionId = sessionId;

            SaveSessionIndex();
            SaveMessages(sessionId, Array.Empty<SerializedMessageRecord>());

            TM.App.Log($"[SessionManager] 创建会话: {sessionId}");
            return sessionId;
        }

        public void ResetCurrentSession()
        {
            _currentSessionId = null;
        }

        public ChatHistory SwitchSession(string sessionId)
        {
            if (!_sessionIndex.ContainsKey(sessionId))
            {
                TM.App.Log($"[SessionManager] 会话不存在: {sessionId}");
                return new ChatHistory();
            }

            _currentSessionId = sessionId;

            var records = LoadMessages(sessionId);
            return RebuildChatHistory(records);
        }

        public ChatHistory SwitchSessionWithRecords(string sessionId, List<SerializedMessageRecord> records)
        {
            if (!_sessionIndex.ContainsKey(sessionId))
            {
                TM.App.Log($"[SessionManager] 会话不存在: {sessionId}");
                return new ChatHistory();
            }

            _currentSessionId = sessionId;
            return RebuildChatHistory(records);
        }

        public string GetCurrentSessionId()
        {
            if (string.IsNullOrEmpty(_currentSessionId))
            {
                _currentSessionId = CreateSession();
            }
            return _currentSessionId;
        }

        public string? GetCurrentSessionIdOrNull()
        {
            return _currentSessionId;
        }

        public bool HasCurrentSession => !string.IsNullOrEmpty(_currentSessionId);

        public List<SessionInfo> GetAllSessions()
        {
            return _sessionIndex.Values
                .OrderByDescending(s => s.UpdatedAt)
                .ToList();
        }

        public void DeleteSession(string sessionId)
        {
            if (_sessionIndex.Remove(sessionId))
            {
                var messagesPath = GetMessagesFilePath(sessionId);
                if (File.Exists(messagesPath))
                {
                    File.Delete(messagesPath);
                }

                SaveSessionIndex();

                if (_currentSessionId == sessionId)
                {
                    _currentSessionId = _sessionIndex.Keys.FirstOrDefault();
                }

                TM.App.Log($"[SessionManager] 删除会话: {sessionId}");
            }
        }

        public void RenameSession(string sessionId, string newTitle)
        {
            if (_sessionIndex.TryGetValue(sessionId, out var info))
            {
                info.Title = newTitle;
                info.UpdatedAt = DateTime.Now;
                SaveSessionIndex();
            }
        }

        public void UpdateSessionMode(string sessionId, string mode)
        {
            if (_sessionIndex.TryGetValue(sessionId, out var info))
            {
                info.Mode = mode;
                SaveSessionIndex();
                TM.App.Log($"[SessionManager] 更新会话模式: {sessionId} -> {mode}");
            }
        }

        public string GetSessionMode(string sessionId)
        {
            if (_sessionIndex.TryGetValue(sessionId, out var info))
            {
                return info.Mode ?? "0";
            }
            return "0";
        }

        #endregion

        #region 三层消息存储（新架构）

        public void SaveMessages(string sessionId, IEnumerable<SerializedMessageRecord> messages)
        {
            try
            {
                var list = messages?.ToList() ?? new List<SerializedMessageRecord>();

                if (_sessionIndex.TryGetValue(sessionId, out var info))
                {
                    info.UpdatedAt = DateTime.Now;
                    info.MessageCount = list.Count;
                    if (CurrentChapterTracker.HasCurrentChapter)
                        info.ContextChapterId = CurrentChapterTracker.CurrentChapterId;
                }

                var msgJson = JsonSerializer.Serialize(list, JsonHelper.Default);
                var msgPath = GetMessagesFilePath(sessionId);
                var idxJson = JsonSerializer.Serialize(_sessionIndex.Values.ToList(), JsonHelper.Default);
                var idxPath = GetIndexFilePath();
                var count = list.Count;

                _ = Task.Run(async () =>
                {
                    await _writeSemaphore.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        var tmp = msgPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        File.WriteAllText(tmp, msgJson);
                        File.Move(tmp, msgPath, overwrite: true);

                        var idxTmp = idxPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        File.WriteAllText(idxTmp, idxJson);
                        File.Move(idxTmp, idxPath, overwrite: true);

                        TM.App.Log($"[SessionManager] 保存消息: {sessionId}, {count} 条");
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[SessionManager] 保存消息失败: {ex.Message}");
                    }
                    finally
                    {
                        _writeSemaphore.Release();
                    }
                });
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SessionManager] 保存消息失败: {ex.Message}");
            }
        }

        public List<SerializedMessageRecord> LoadMessages(string sessionId)
        {
            var filePath = GetMessagesFilePath(sessionId);
            if (!File.Exists(filePath))
            {
                return new List<SerializedMessageRecord>();
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var records = JsonSerializer.Deserialize<List<SerializedMessageRecord>>(json);
                TM.App.Log($"[SessionManager] 加载消息: {sessionId}, {records?.Count ?? 0} 条");
                return records ?? new List<SerializedMessageRecord>();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SessionManager] 加载消息失败: {ex.Message}");
                return new List<SerializedMessageRecord>();
            }
        }

        public async Task<List<SerializedMessageRecord>> LoadMessagesAsync(string sessionId)
        {
            var filePath = GetMessagesFilePath(sessionId);
            if (!File.Exists(filePath))
                return new List<SerializedMessageRecord>();

            return await Task.Run(() =>
            {
                try
                {
                    var json = File.ReadAllText(filePath);
                    var records = JsonSerializer.Deserialize<List<SerializedMessageRecord>>(json);
                    TM.App.Log($"[SessionManager] 加载消息: {sessionId}, {records?.Count ?? 0} 条");
                    return records ?? new List<SerializedMessageRecord>();
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[SessionManager] 加载消息失败: {ex.Message}");
                    return new List<SerializedMessageRecord>();
                }
            }).ConfigureAwait(false);
        }

        public void SaveCurrentMessages(IEnumerable<SerializedMessageRecord> messages)
        {
            var list = messages?.ToList() ?? new List<SerializedMessageRecord>();

            if (string.IsNullOrEmpty(_currentSessionId) && list.Count == 0)
            {
                return;
            }

            var sessionId = string.IsNullOrEmpty(_currentSessionId)
                ? CreateSession()
                : _currentSessionId;

            SaveMessages(sessionId, list);
        }

        public List<SerializedMessageRecord> LoadCurrentMessages()
        {
            if (string.IsNullOrEmpty(_currentSessionId))
            {
                return new List<SerializedMessageRecord>();
            }

            return LoadMessages(_currentSessionId);
        }

        public ChatHistory RebuildChatHistory(IEnumerable<SerializedMessageRecord> messages)
        {
            var history = new ChatHistory();
            foreach (var msg in messages)
            {
                var role = msg.Role.ToLower() switch
                {
                    "system" => AuthorRole.System,
                    "user" => AuthorRole.User,
                    "assistant" => AuthorRole.Assistant,
                    _ => AuthorRole.User
                };
                history.Add(new ChatMessageContent(role, msg.Summary));
            }
            return history;
        }

        private string GetMessagesFilePath(string sessionId)
        {
            return Path.Combine(_sessionsDir, $"{sessionId}.messages.json");
        }

        #endregion

        #region 私有方法

        private string GetIndexFilePath()
        {
            return Path.Combine(_sessionsDir, "_index.json");
        }

        private void LoadSessionIndex()
        {
            var indexPath = GetIndexFilePath();
            if (!File.Exists(indexPath)) return;

            try
            {
                var json = File.ReadAllText(indexPath);
                var sessions = JsonSerializer.Deserialize<List<SessionInfo>>(json);

                if (sessions != null)
                {
                    foreach (var s in sessions)
                    {
                        _sessionIndex[s.Id] = s;
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SessionManager] 加载索引失败: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task LoadSessionIndexAsync()
        {
            var indexPath = GetIndexFilePath();
            if (!File.Exists(indexPath)) return;

            try
            {
                var json = await File.ReadAllTextAsync(indexPath);
                var sessions = JsonSerializer.Deserialize<List<SessionInfo>>(json);

                if (sessions != null)
                {
                    foreach (var s in sessions)
                    {
                        _sessionIndex[s.Id] = s;
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SessionManager] 异步加载索引失败: {ex.Message}");
            }
        }

        public void ReloadIndex()
        {
            _sessionIndex.Clear();
            _currentSessionId = null;
            LoadSessionIndex();
            TM.App.Log("[SessionManager] 已重新加载会话索引");
        }

        private void SaveSessionIndex()
        {
            try
            {
                var json = JsonSerializer.Serialize(_sessionIndex.Values.ToList(), JsonHelper.Default);
                var path = GetIndexFilePath();

                _ = Task.Run(async () =>
                {
                    await _writeSemaphore.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        File.WriteAllText(tmp, json);
                        File.Move(tmp, path, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[SessionManager] 保存索引失败: {ex.Message}");
                    }
                    finally
                    {
                        _writeSemaphore.Release();
                    }
                });
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SessionManager] 保存索引失败: {ex.Message}");
            }
        }

        private void CleanupEmptySessions()
        {
            try
            {
                var toDelete = new List<string>();

                foreach (var kv in _sessionIndex)
                {
                    var id = kv.Key;
                    var info = kv.Value;

                    if (info.MessageCount > 0)
                    {
                        continue;
                    }

                    var path = GetMessagesFilePath(id);
                    if (!File.Exists(path))
                    {
                        toDelete.Add(id);
                        continue;
                    }

                    try
                    {
                        var json = File.ReadAllText(path);
                        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "[]")
                        {
                            toDelete.Add(id);
                        }
                    }
                    catch
                    {
                        toDelete.Add(id);
                    }
                }

                foreach (var id in toDelete)
                {
                    DeleteSession(id);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SessionManager] CleanupEmptySessions 失败: {ex.Message}");
            }
        }

        #endregion
    }

    public class SessionInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("Id")] public string Id { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Title")] public string Title { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("CreatedAt")] public DateTime CreatedAt { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("UpdatedAt")] public DateTime UpdatedAt { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("MessageCount")] public int MessageCount { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Mode")] public string Mode { get; set; } = "0";
        [System.Text.Json.Serialization.JsonPropertyName("ContextChapterId")] public string? ContextChapterId { get; set; }
    }

    public class SerializedMessageRecord
    {
        [System.Text.Json.Serialization.JsonPropertyName("MessageId")] public string MessageId { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Role")] public string Role { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Summary")] public string Summary { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Analysis")] public string? Analysis { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("DurationSeconds")] public double? DurationSeconds { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ChangesJson")] public string? ChangesJson { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ChangesDurationSeconds")] public double? ChangesDurationSeconds { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("PayloadType")] public int PayloadType { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("PayloadJson")] public string? PayloadJson { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("Timestamp")] public DateTime Timestamp { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ReferencesJson")] public string? ReferencesJson { get; set; }
    }
}
