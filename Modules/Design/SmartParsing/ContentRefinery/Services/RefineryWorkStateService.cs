using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Modules.Design.SmartParsing.ContentRefinery.Models;

namespace TM.Modules.Design.SmartParsing.ContentRefinery.Services
{
    public class RefineryWorkStateService
    {
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private int _saveVersion;

        private static string StatePath =>
            StoragePathHelper.GetFilePath("Modules", "Design/SmartParsing/ContentRefinery", "work_state.json");

        public async Task<RefineryWorkState?> LoadAsync()
        {
            try
            {
                var path = StatePath;
                if (!File.Exists(path)) return null;
                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                return JsonSerializer.Deserialize<RefineryWorkState>(json);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentRefinery] 异步加载工作区状态失败: {ex.Message}");
                return null;
            }
        }

        public void Save(RefineryWorkState state)
        {
            var json = JsonSerializer.Serialize(state, _jsonOptions);
            var saveVersion = Interlocked.Increment(ref _saveVersion);
            _ = SaveCoreAsync(json, saveVersion);
        }

        private async Task SaveCoreAsync(string json, int saveVersion)
        {
            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (saveVersion != Volatile.Read(ref _saveVersion))
                    return;
                var path = StatePath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
                File.Move(tmp, path, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentRefinery] 保存工作区状态失败: {ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        }

        public void Clear()
        {
            try
            {
                var path = StatePath;
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentRefinery] 清空工作区状态失败: {ex.Message}");
            }
        }
    }
}
