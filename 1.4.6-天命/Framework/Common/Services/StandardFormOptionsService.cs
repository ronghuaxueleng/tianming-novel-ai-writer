using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TM.Framework.Common.Helpers;

namespace TM.Framework.Common.Services
{
    public class StandardFormOptionsService
    {
        private readonly string _optionsPath;
        private StandardFormOptions? _cachedOptions;

        public StandardFormOptionsService()
        {
            _optionsPath = StoragePathHelper.GetFilePath(
                "Framework",
                "Common",
                "standard_form_options.json"
            );

        }

        public StandardFormOptions GetOptions()
        {
            if (_cachedOptions != null)
            {
                return _cachedOptions;
            }

            try
            {
                if (!File.Exists(_optionsPath))
                {
                    TM.App.Log("[StandardFormOptionsService] 配置文件不存在，返回默认配置");
                    _cachedOptions = GetDefaultOptions();
                    return _cachedOptions;
                }

                var json = File.ReadAllText(_optionsPath);
                _cachedOptions = JsonSerializer.Deserialize<StandardFormOptions>(json) ?? GetDefaultOptions();

                TM.App.Log($"[StandardFormOptionsService] 加载配置成功");
                return _cachedOptions;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[StandardFormOptionsService] 加载配置失败: {ex.Message}，使用默认配置");
                _cachedOptions = GetDefaultOptions();
                return _cachedOptions;
            }
        }

        public void SaveOptions(StandardFormOptions options)
        {
            try
            {
                var jsonOptions = JsonHelper.CnDefault;
                var json = JsonSerializer.Serialize(options, jsonOptions);
                var tmpSf = _optionsPath + ".tmp";
                File.WriteAllText(tmpSf, json);
                File.Move(tmpSf, _optionsPath, overwrite: true);

                _cachedOptions = options;
                TM.App.Log($"[StandardFormOptionsService] 配置已保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[StandardFormOptionsService] 保存配置失败: {ex.Message}");
                throw;
            }
        }

        public async System.Threading.Tasks.Task SaveOptionsAsync(StandardFormOptions options)
        {
            try
            {
                var jsonOptions = JsonHelper.CnDefault;
                var json = JsonSerializer.Serialize(options, jsonOptions);
                var tmpSfa = _optionsPath + ".tmp";
                await File.WriteAllTextAsync(tmpSfa, json);
                File.Move(tmpSfa, _optionsPath, overwrite: true);

                _cachedOptions = options;
                TM.App.Log($"[StandardFormOptionsService] 配置已异步保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[StandardFormOptionsService] 异步保存配置失败: {ex.Message}");
                throw;
            }
        }

        public void ClearCache()
        {
            _cachedOptions = null;
            TM.App.Log("[StandardFormOptionsService] 缓存已清除");
        }

        private StandardFormOptions GetDefaultOptions()
        {
            return new StandardFormOptions
            {
                PriorityOptions = new List<string> { "极高", "高", "中", "低", "极低" },
                ScopeOptions = new List<string> { "全局", "卷级", "章节级", "场景级", "人物级" },
                NovelGenreOptions = new List<string> 
                { 
                    "玄幻修仙", "都市言情", "科幻未来", "历史架空",
                    "武侠仙侠", "悬疑推理", "游戏竞技", "奇幻魔法",
                    "灵异恐怖", "轻小说", "二次元", "其他"
                },
                DefaultPriority = "中",
                DefaultScope = "全局",
                DefaultAIWeight = 50
            };
        }
    }

    public class StandardFormOptions
    {
        [System.Text.Json.Serialization.JsonPropertyName("PriorityOptions")] public List<string> PriorityOptions { get; set; } = new List<string>();
        [System.Text.Json.Serialization.JsonPropertyName("ScopeOptions")] public List<string> ScopeOptions { get; set; } = new List<string>();
        [System.Text.Json.Serialization.JsonPropertyName("NovelGenreOptions")] public List<string> NovelGenreOptions { get; set; } = new List<string>();
        [System.Text.Json.Serialization.JsonPropertyName("DefaultPriority")] public string DefaultPriority { get; set; } = "中";
        [System.Text.Json.Serialization.JsonPropertyName("DefaultScope")] public string DefaultScope { get; set; } = "全局";
        [System.Text.Json.Serialization.JsonPropertyName("DefaultAIWeight")] public int DefaultAIWeight { get; set; } = 50;
    }
}

