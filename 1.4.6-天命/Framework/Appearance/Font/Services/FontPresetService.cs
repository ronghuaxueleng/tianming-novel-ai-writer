using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TM.Framework.Common.Helpers;
using TM.Framework.Appearance.Font.Models;

namespace TM.Framework.Appearance.Font.Services
{
    public class FontPreset
    {
        [System.Text.Json.Serialization.JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("FontFamily")] public string FontFamily { get; set; } = "Microsoft YaHei UI";
        [System.Text.Json.Serialization.JsonPropertyName("FontSize")] public double FontSize { get; set; } = 14;
        [System.Text.Json.Serialization.JsonPropertyName("FontWeight")] public string FontWeight { get; set; } = "Normal";
        [System.Text.Json.Serialization.JsonPropertyName("LineHeight")] public double LineHeight { get; set; } = 1.5;
        [System.Text.Json.Serialization.JsonPropertyName("LetterSpacing")] public double LetterSpacing { get; set; } = 0;
        [System.Text.Json.Serialization.JsonPropertyName("IsBuiltIn")] public bool IsBuiltIn { get; set; } = false;
    }

    public class FontPresetService
    {
        private readonly string _presetsFilePath;
        private List<FontPreset> _customPresets = new();
        private readonly List<FontPreset> _builtInPresets;

        public FontPresetService()
        {
            _presetsFilePath = TM.Framework.Common.Helpers.Storage.StoragePathHelper.GetFilePath(
                "Framework",
                "Appearance/Font",
                "presets.json"
            );

            _builtInPresets = new List<FontPreset>
            {
                new FontPreset
                {
                    Name = "办公场景",
                    Description = "适合日常办公和文档编辑",
                    FontFamily = "Microsoft YaHei UI",
                    FontSize = 14,
                    FontWeight = "Normal",
                    LineHeight = 1.5,
                    LetterSpacing = 0,
                    IsBuiltIn = true
                },
                new FontPreset
                {
                    Name = "阅读场景",
                    Description = "适合长时间阅读,减轻眼睛疲劳",
                    FontFamily = "宋体",
                    FontSize = 16,
                    FontWeight = "Light",
                    LineHeight = 1.8,
                    LetterSpacing = 0.2,
                    IsBuiltIn = true
                },
                new FontPreset
                {
                    Name = "设计场景",
                    Description = "适合UI设计和创作工作",
                    FontFamily = "苹方",
                    FontSize = 13,
                    FontWeight = "Medium",
                    LineHeight = 1.6,
                    LetterSpacing = 0,
                    IsBuiltIn = true
                },
                new FontPreset
                {
                    Name = "极简场景",
                    Description = "简洁明快,适合专注工作",
                    FontFamily = "Segoe UI",
                    FontSize = 12,
                    FontWeight = "Light",
                    LineHeight = 1.4,
                    LetterSpacing = 0,
                    IsBuiltIn = true
                },
                new FontPreset
                {
                    Name = "演示场景",
                    Description = "适合屏幕演示和投影展示",
                    FontFamily = "Microsoft YaHei UI",
                    FontSize = 18,
                    FontWeight = "SemiBold",
                    LineHeight = 1.6,
                    LetterSpacing = 0.3,
                    IsBuiltIn = true
                }
            };

            LoadCustomPresets();
        }

        private void LoadCustomPresets()
        {
            try
            {
                if (File.Exists(_presetsFilePath))
                {
                    string json = File.ReadAllText(_presetsFilePath);
                    _customPresets = JsonSerializer.Deserialize<List<FontPreset>>(json) ?? new List<FontPreset>();
                    TM.App.Log($"[FontPresetService] 成功加载 {_customPresets.Count} 个自定义预设");
                }
                else
                {
                    _customPresets = new List<FontPreset>();
                    TM.App.Log("[FontPresetService] 自定义预设文件不存在，创建空列表");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontPresetService] 加载自定义预设失败: {ex.Message}");
                _customPresets = new List<FontPreset>();
            }
        }

        private void SaveCustomPresets()
        {
            try
            {
                string? directory = Path.GetDirectoryName(_presetsFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    TM.Framework.Common.Helpers.Storage.StoragePathHelper.EnsureDirectoryExists(directory);
                }

                string json = JsonSerializer.Serialize(_customPresets, JsonHelper.Default);
                var tmpFps = _presetsFilePath + ".tmp";
                File.WriteAllText(tmpFps, json);
                File.Move(tmpFps, _presetsFilePath, overwrite: true);
                TM.App.Log("[FontPresetService] 自定义预设已保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontPresetService] 保存自定义预设失败: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task LoadCustomPresetsAsync()
        {
            try
            {
                if (File.Exists(_presetsFilePath))
                {
                    string json = await File.ReadAllTextAsync(_presetsFilePath);
                    _customPresets = JsonSerializer.Deserialize<List<FontPreset>>(json) ?? new List<FontPreset>();
                    TM.App.Log($"[FontPresetService] 异步加载 {_customPresets.Count} 个自定义预设");
                }
                else
                {
                    _customPresets = new List<FontPreset>();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontPresetService] 异步加载自定义预设失败: {ex.Message}");
                _customPresets = new List<FontPreset>();
            }
        }

        private async System.Threading.Tasks.Task SaveCustomPresetsAsync()
        {
            try
            {
                string? directory = Path.GetDirectoryName(_presetsFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    TM.Framework.Common.Helpers.Storage.StoragePathHelper.EnsureDirectoryExists(directory);
                }

                string json = JsonSerializer.Serialize(_customPresets, JsonHelper.Default);
                var tmpFpsA = _presetsFilePath + ".tmp";
                await File.WriteAllTextAsync(tmpFpsA, json);
                File.Move(tmpFpsA, _presetsFilePath, overwrite: true);
                TM.App.Log("[FontPresetService] 自定义预设已异步保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontPresetService] 异步保存自定义预设失败: {ex.Message}");
            }
        }

        public List<FontPreset> GetAllPresets()
        {
            var allPresets = new List<FontPreset>();
            allPresets.AddRange(_builtInPresets);
            allPresets.AddRange(_customPresets);
            return allPresets;
        }

        public List<FontPreset> GetBuiltInPresets()
        {
            return new List<FontPreset>(_builtInPresets);
        }

        public List<FontPreset> GetCustomPresets()
        {
            return new List<FontPreset>(_customPresets);
        }

        public void SaveAsPreset(string name, string description, FontSettings settings)
        {
            try
            {
                var existing = _customPresets.FirstOrDefault(p => p.Name == name);
                if (existing != null)
                {
                    existing.Description = description;
                    existing.FontFamily = settings.FontFamily;
                    existing.FontSize = settings.FontSize;
                    existing.FontWeight = settings.FontWeight;
                    existing.LineHeight = settings.LineHeight;
                    existing.LetterSpacing = settings.LetterSpacing;
                    TM.App.Log($"[FontPresetService] 更新预设: {name}");
                }
                else
                {
                    var newPreset = new FontPreset
                    {
                        Name = name,
                        Description = description,
                        FontFamily = settings.FontFamily,
                        FontSize = settings.FontSize,
                        FontWeight = settings.FontWeight,
                        LineHeight = settings.LineHeight,
                        LetterSpacing = settings.LetterSpacing,
                        IsBuiltIn = false
                    };
                    _customPresets.Add(newPreset);
                    TM.App.Log($"[FontPresetService] 创建预设: {name}");
                }

                SaveCustomPresets();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontPresetService] 保存预设失败: {ex.Message}");
                throw;
            }
        }

        public void DeletePreset(string name)
        {
            try
            {
                var preset = _customPresets.FirstOrDefault(p => p.Name == name);
                if (preset != null)
                {
                    _customPresets.Remove(preset);
                    SaveCustomPresets();
                    TM.App.Log($"[FontPresetService] 删除预设: {name}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontPresetService] 删除预设失败: {ex.Message}");
                throw;
            }
        }

        public void ApplyPreset(FontPreset preset, FontSettings settings)
        {
            settings.FontFamily = preset.FontFamily;
            settings.FontSize = preset.FontSize;
            settings.FontWeight = preset.FontWeight;
            settings.LineHeight = preset.LineHeight;
            settings.LetterSpacing = preset.LetterSpacing;
        }

        public void ImportPresets(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var importedPresets = JsonSerializer.Deserialize<List<FontPreset>>(json);
                    if (importedPresets != null && importedPresets.Count > 0)
                    {
                        foreach (var preset in importedPresets)
                        {
                            preset.IsBuiltIn = false;
                            if (!_customPresets.Any(p => p.Name == preset.Name))
                            {
                                _customPresets.Add(preset);
                            }
                        }
                        SaveCustomPresets();
                        TM.App.Log($"[FontPresetService] 导入 {importedPresets.Count} 个预设");
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontPresetService] 导入预设失败: {ex.Message}");
                throw;
            }
        }

        public void ExportPresets(string filePath, List<FontPreset> presets)
        {
            try
            {
                string json = JsonSerializer.Serialize(presets, JsonHelper.Default);
                var tmpFpE = filePath + ".tmp";
                File.WriteAllText(tmpFpE, json);
                File.Move(tmpFpE, filePath, overwrite: true);
                TM.App.Log($"[FontPresetService] 导出 {presets.Count} 个预设到 {filePath}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontPresetService] 导出预设失败: {ex.Message}");
                throw;
            }
        }
    }
}

