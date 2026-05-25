using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using TM.Framework.Common.Helpers;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class WorkScopeService : IWorkScopeService
    {

        private const string WorkScopeFileName = "work_scope.json";
        private string? _currentSourceBookId;
        private DateTime? _lastUpdated;
        private string _loadedForProjectName = string.Empty;

        public event EventHandler<string?>? ScopeChanged;

        public WorkScopeService() 
        {
            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) =>
                {
                    CurrentSourceBookId = null;
                    _lastUpdated = null;
                    _loadedForProjectName = string.Empty;
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorkScopeService] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        public string? CurrentSourceBookId 
        { 
            get => _currentSourceBookId; 
            private set 
            { 
                if (_currentSourceBookId != value)
                {
                    var oldValue = _currentSourceBookId;
                    _currentSourceBookId = value;
                    _lastUpdated = DateTime.Now;
                    ScopeChanged?.Invoke(this, value);

                    TM.App.Log($"[WorkScopeService] Scope切换: {oldValue ?? "null"} -> {value ?? "null"}");
                }
            }
        }

        public DateTime? LastUpdated => _lastUpdated;

        public async Task InitializeAsync()
        {
            await LoadAsync().ConfigureAwait(false);

            if (string.IsNullOrEmpty(_currentSourceBookId))
            {
                var inferred = await InferScopeFromCreativeMaterialsAsync().ConfigureAwait(false);
                if (!string.IsNullOrEmpty(inferred))
                {
                    CurrentSourceBookId = inferred;
                    await SaveAsync().ConfigureAwait(false);
                    TM.App.Log($"[WorkScopeService] 自动推断 Scope: {inferred}");
                }
            }
        }

        public async Task<string?> GetCurrentScopeAsync()
        {
            if (_loadedForProjectName != StoragePathHelper.CurrentProjectName)
            {
                _currentSourceBookId = null;
                _lastUpdated = null;
                _loadedForProjectName = string.Empty;
            }

            if (_currentSourceBookId == null)
            {
                await LoadAsync().ConfigureAwait(false);
            }
            return _currentSourceBookId;
        }

        public async Task SetCurrentScopeAsync(string? sourceBookId)
        {
            CurrentSourceBookId = sourceBookId;
            await SaveAsync().ConfigureAwait(false);
        }

        public async Task ClearScopeAsync()
        {
            CurrentSourceBookId = null;
            await SaveAsync().ConfigureAwait(false);
        }

        private async Task LoadAsync()
        {
            try
            {
                var configPath = StoragePathHelper.GetProjectConfigPath();
                var filePath = Path.Combine(configPath, WorkScopeFileName);

                if (!File.Exists(filePath))
                {
                    TM.App.Log($"[WorkScopeService] work_scope.json 不存在，使用默认值");
                    return;
                }

                var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                var data = JsonSerializer.Deserialize<WorkScopeData>(json);

                if (data != null)
                {
                    _currentSourceBookId = string.IsNullOrEmpty(data.CurrentSourceBookId) ? null : data.CurrentSourceBookId;
                    _lastUpdated = data.UpdatedTime;
                    _loadedForProjectName = StoragePathHelper.CurrentProjectName;

                    TM.App.Log($"[WorkScopeService] 加载成功: CurrentSourceBookId={_currentSourceBookId ?? "null"}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorkScopeService] 加载失败: {ex.Message}");
            }
        }

        private async Task SaveAsync()
        {
            try
            {
                var configPath = StoragePathHelper.GetProjectConfigPath();
                Directory.CreateDirectory(configPath);

                var filePath = Path.Combine(configPath, WorkScopeFileName);

                var data = new WorkScopeData
                {
                    CurrentSourceBookId = _currentSourceBookId,
                    UpdatedTime = _lastUpdated ?? DateTime.Now
                };

                var json = JsonSerializer.Serialize(data, JsonHelper.Default);

                var tmp = filePath + ".tmp";
                await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
                File.Move(tmp, filePath, overwrite: true);

                TM.App.Log($"[WorkScopeService] 保存成功: CurrentSourceBookId={_currentSourceBookId ?? "null"}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorkScopeService] 保存失败: {ex.Message}");
                throw;
            }
        }

        private async Task<string?> InferScopeFromCreativeMaterialsAsync()
        {
            try
            {
                var materialsPath = Path.Combine(
                    StoragePathHelper.GetStorageRoot(),
                    "Modules", "Design", "Templates", "CreativeMaterials", "creative_materials.json");

                if (!File.Exists(materialsPath))
                    return null;

                var json = await File.ReadAllTextAsync(materialsPath).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var isEnabled = item.TryGetProperty("IsEnabled", out var enabledProp) && enabledProp.GetBoolean();
                    if (!isEnabled) continue;

                    if (item.TryGetProperty("SourceBookId", out var sourceBookIdProp) &&
                        sourceBookIdProp.ValueKind == JsonValueKind.String)
                    {
                        var sourceBookId = sourceBookIdProp.GetString();
                        if (!string.IsNullOrEmpty(sourceBookId))
                            return sourceBookId;
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorkScopeService] 推断 Scope 失败: {ex.Message}");
            }
            return null;
        }

        private class WorkScopeData
        {
            [System.Text.Json.Serialization.JsonPropertyName("CurrentSourceBookId")] public string? CurrentSourceBookId { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("UpdatedTime")] public DateTime UpdatedTime { get; set; }
        }
    }
}
