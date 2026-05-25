using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace TM.Framework.UI.Workspace.Services.Spec
{
    public class SpecLoader
    {
        public event EventHandler? SpecSaved;

        public SpecLoader()
        {
            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) => InvalidateCache();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SpecLoader] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        #region 缓存
        private CreativeSpec? _cache;
        private DateTime _cacheTime;
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);

        public void InvalidateCache() => _cache = null;
        #endregion

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public CreativeSpec? LoadProjectSpecSync()
        {
            if (_cache != null && DateTime.Now - _cacheTime < _cacheExpiration)
                return _cache;

            try
            {
                var projectPath = StoragePathHelper.GetCurrentProjectPath();
                var path = Path.Combine(projectPath, "Config", "project_spec.json");
                if (!File.Exists(path))
                {
                    _cache = CreateDefaultProjectSpec();
                    _cacheTime = DateTime.Now;
                    return _cache;
                }

                var json = File.ReadAllText(path);
                _cache = JsonSerializer.Deserialize<CreativeSpec>(json, JsonOptions);
                _cacheTime = DateTime.Now;
                return _cache;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SpecLoader] 同步加载项目Spec失败: {ex.Message}");
                return CreateDefaultProjectSpec();
            }
        }

        public async Task<CreativeSpec?> LoadProjectSpecAsync()
        {
            if (_cache != null && DateTime.Now - _cacheTime < _cacheExpiration)
            {
                return _cache;
            }

            try
            {
                var projectPath = StoragePathHelper.GetCurrentProjectPath();
                var configDir = Path.Combine(projectPath, "Config");
                var path = Path.Combine(configDir, "project_spec.json");

                if (!File.Exists(path))
                {
                    _cache = CreateDefaultProjectSpec();
                    _cacheTime = DateTime.Now;
                    return _cache;
                }

                var json = await File.ReadAllTextAsync(path);
                _cache = JsonSerializer.Deserialize<CreativeSpec>(json, JsonOptions);
                _cacheTime = DateTime.Now;

                TM.App.Log("[SpecLoader] 加载项目Spec成功");
                return _cache;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SpecLoader] 加载项目Spec失败: {ex.Message}");
                return CreateDefaultProjectSpec();
            }
        }

        public async Task SaveProjectSpecAsync(CreativeSpec spec)
        {
            try
            {
                var projectPath = StoragePathHelper.GetCurrentProjectPath();
                var configDir = Path.Combine(projectPath, "Config");
                var path = Path.Combine(configDir, "project_spec.json");

                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                var json = JsonSerializer.Serialize(spec, JsonOptions);
                var tmpSpec = path + ".tmp";
                await File.WriteAllTextAsync(tmpSpec, json);
                File.Move(tmpSpec, path, overwrite: true);

                _cache = spec;
                _cacheTime = DateTime.Now;

                TM.App.Log("[SpecLoader] 保存项目Spec成功");
                SpecSaved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SpecLoader] 保存项目Spec失败: {ex.Message}");
                throw;
            }
        }

        private static CreativeSpec CreateDefaultProjectSpec()
        {
            return new CreativeSpec
            {
                WritingStyle = "流畅自然",
                Pov = "第三人称限知",
                Tone = "平衡",
                TargetWordCount = 3500,
                ParagraphLength = 200,
                DialogueRatio = 0.3,
                PolishMode = 1
            };
        }
    }
}
