using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
        private readonly object _lock = new();
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private int _presetsVersion;

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

            _ = System.Threading.Tasks.Task.Run(async () => await LoadCustomPresetsAsync().ConfigureAwait(false));
        }

        private async System.Threading.Tasks.Task LoadCustomPresetsAsync()
        {
            var loadVersion = Volatile.Read(ref _presetsVersion);
            try
            {
                if (File.Exists(_presetsFilePath))
                {
                    await using var stream = File.OpenRead(_presetsFilePath);
                    var loaded = await JsonSerializer.DeserializeAsync<List<FontPreset>>(stream) ?? new List<FontPreset>();

                    lock (_lock)
                    {
                        if (loadVersion == Volatile.Read(ref _presetsVersion))
                        {
                            _customPresets = loaded;
                        }
                        else
                        {
                            var existingNames = new HashSet<string>(_customPresets.Select(p => p.Name));
                            foreach (var p in loaded)
                            {
                                if (!string.IsNullOrWhiteSpace(p.Name) && !existingNames.Contains(p.Name))
                                    _customPresets.Add(p);
                            }
                        }

                        TM.App.Log($"[FontPresetService] 异步加载 {_customPresets.Count} 个自定义预设");
                    }
                }
                else
                {
                    if (loadVersion != Volatile.Read(ref _presetsVersion))
                        return;
                    lock (_lock)
                        _customPresets = new List<FontPreset>();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontPresetService] 异步加载自定义预设失败: {ex.Message}");
                if (loadVersion != Volatile.Read(ref _presetsVersion))
                    return;
                lock (_lock)
                    _customPresets = new List<FontPreset>();
            }
        }

        private async System.Threading.Tasks.Task SaveCustomPresetsAsync()
        {
            int saveVersion;
            List<FontPreset> snapshot;
            lock (_lock)
            {
                saveVersion = _presetsVersion;
                snapshot = new List<FontPreset>(_customPresets);
            }

            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (saveVersion != Volatile.Read(ref _presetsVersion))
                    return;
                string? directory = Path.GetDirectoryName(_presetsFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    TM.Framework.Common.Helpers.Storage.StoragePathHelper.EnsureDirectoryExists(directory);
                }

                var tmpFpsA = _presetsFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await using (var stream = File.Create(tmpFpsA))
                {
                    await JsonSerializer.SerializeAsync(stream, snapshot, JsonHelper.Default);
                }
                File.Move(tmpFpsA, _presetsFilePath, overwrite: true);
                TM.App.Log("[FontPresetService] 自定义预设已异步保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontPresetService] 异步保存自定义预设失败: {ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        }

        public List<FontPreset> GetAllPresets()
        {
            var allPresets = new List<FontPreset>();
            allPresets.AddRange(_builtInPresets);
            lock (_lock)
                allPresets.AddRange(_customPresets);
            return allPresets;
        }

        public List<FontPreset> GetBuiltInPresets()
        {
            return new List<FontPreset>(_builtInPresets);
        }

        public List<FontPreset> GetCustomPresets()
        {
            lock (_lock)
                return new List<FontPreset>(_customPresets);
        }

        public void SaveAsPreset(string name, string description, FontSettings settings)
        {
            try
            {
                bool created;
                lock (_lock)
                {
                    Interlocked.Increment(ref _presetsVersion);
                    var existing = _customPresets.FirstOrDefault(p => p.Name == name);
                    if (existing != null)
                    {
                        existing.Description = description;
                        existing.FontFamily = settings.FontFamily;
                        existing.FontSize = settings.FontSize;
                        existing.FontWeight = settings.FontWeight;
                        existing.LineHeight = settings.LineHeight;
                        existing.LetterSpacing = settings.LetterSpacing;
                        created = false;
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
                        created = true;
                    }
                }

                TM.App.Log(created ? $"[FontPresetService] 创建预设: {name}" : $"[FontPresetService] 更新预设: {name}");

                _ = SaveCustomPresetsAsync();
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
                bool deleted;
                lock (_lock)
                {
                    var preset = _customPresets.FirstOrDefault(p => p.Name == name);
                    deleted = preset != null && _customPresets.Remove(preset);
                    if (deleted)
                        Interlocked.Increment(ref _presetsVersion);
                }

                if (deleted)
                {
                    _ = SaveCustomPresetsAsync();
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

        public async System.Threading.Tasks.Task ImportPresetsAsync(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    string json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
                    var importedPresets = JsonSerializer.Deserialize<List<FontPreset>>(json);
                    if (importedPresets != null && importedPresets.Count > 0)
                    {
                        int added;
                        lock (_lock)
                        {
                            Interlocked.Increment(ref _presetsVersion);
                            var existing = new HashSet<string>(_customPresets.Select(p => p.Name));
                            added = 0;
                            foreach (var preset in importedPresets)
                            {
                                preset.IsBuiltIn = false;
                                if (string.IsNullOrWhiteSpace(preset.Name))
                                    continue;
                                if (existing.Add(preset.Name))
                                {
                                    _customPresets.Add(preset);
                                    added++;
                                }
                            }
                        }
                        _ = SaveCustomPresetsAsync();
                        TM.App.Log($"[FontPresetService] 导入 {added} 个预设");
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[FontPresetService] 导入预设失败: {ex.Message}");
                throw;
            }
        }

        public async System.Threading.Tasks.Task ExportPresetsAsync(string filePath, List<FontPreset> presets)
        {
            try
            {
                string json = JsonSerializer.Serialize(presets, JsonHelper.Default);
                var tmpFpE = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmpFpE, json).ConfigureAwait(false);
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

