using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.MVVM;
using TM.Services.Framework.Settings;

namespace TM.Framework.SystemSettings.Logging.LogLevel
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class LogLevelViewModel : INotifyPropertyChanged
    {
        private LogLevelSettings _settings = null!;
        private readonly string _settingsFilePath = null!;
        private readonly string _historyFilePath = null!;
        private readonly string _statisticsFilePath = null!;
        private readonly LogManager _logManager;
        private LevelStatistics _statistics = null!;
        private System.Windows.Threading.DispatcherTimer _statsTimer = null!;

        public event PropertyChangedEventHandler? PropertyChanged;

        public LogLevelViewModel(LogManager logManager)
        {
            _logManager = logManager;
            _settings = new LogLevelSettings();
            _settingsFilePath = StoragePathHelper.GetFilePath(
                "Framework",
                "SystemSettings/Logging/LogLevel",
                "settings.json"
            );
            _historyFilePath = StoragePathHelper.GetFilePath(
                "Framework",
                "SystemSettings/Logging/LogLevel",
                "change_history.json"
            );
            _statisticsFilePath = StoragePathHelper.GetFilePath(
                "Framework",
                "SystemSettings/Logging/LogLevel",
                "statistics.json"
            );

            LogLevels = new List<LogLevelEnum>
            {
                LogLevelEnum.Trace,
                LogLevelEnum.Debug,
                LogLevelEnum.Info,
                LogLevelEnum.Warning,
                LogLevelEnum.Error,
                LogLevelEnum.Fatal
            };

            ModuleLevels = new ObservableCollection<ModuleLevelItem>();

            ChangeHistory = new ObservableCollection<LevelChangeRecord>();
            LevelStatisticsItems = new ObservableCollection<LevelStatisticsItem>();

            PresetNames = new List<string>(LevelPreset.StandardPresets.Keys);

            _statistics = new LevelStatistics();
            AsyncSettingsLoader.LoadOrDefer<LogLevelSettings>(_settingsFilePath, s =>
            {
                _settings = s;
                OnPropertyChanged(nameof(GlobalLevel));
                OnPropertyChanged(nameof(MinimumLevel));
                LoadModuleLevels();
            }, "LogLevel");

            AsyncSettingsLoader.LoadOrDefer<List<LevelChangeRecord>>(_historyFilePath, history =>
            {
                ChangeHistory.Clear();
                foreach (var record in history.Take(100))
                {
                    ChangeHistory.Add(record);
                }
            }, "LogLevel.History");
            AsyncSettingsLoader.LoadOrDefer<LevelStatistics>(_statisticsFilePath, s =>
            {
                _statistics = s;
                RefreshStatistics();
            }, "LogLevel.Stats");

            SaveCommand = new RelayCommand(SaveSettings);
            ResetCommand = new RelayCommand(ResetSettings);
            AddModuleCommand = new RelayCommand(AddModule);
            RemoveModuleCommand = new RelayCommand(RemoveModule);
            ApplyPresetCommand = new RelayCommand(param => ApplyPreset((param as string)!));
            ViewHistoryCommand = new RelayCommand(ViewHistory);
            ClearHistoryCommand = new RelayCommand(ClearHistory);
            RefreshStatsCommand = new RelayCommand(RefreshStatistics);
            ResetStatsCommand = new RelayCommand(ResetStatistics);

            _statsTimer = new System.Windows.Threading.DispatcherTimer();
            _statsTimer.Interval = TimeSpan.FromSeconds(5);
            _statsTimer.Tick += (s, e) => SimulateLogGeneration();
            if (TM.App.IsDebugMode)
                _statsTimer.Start();

            TM.App.Log($"[LogLevel] ViewModel初始化完成");
        }

        public LogLevelEnum GlobalLevel
        {
            get => _settings.GlobalLevel;
            set 
            { 
                if (_settings.GlobalLevel != value)
                {
                    var oldLevel = _settings.GlobalLevel;
                    _settings.GlobalLevel = value;
                    RecordLevelChange("Global", oldLevel, value, "手动调整");
                    OnPropertyChanged(nameof(GlobalLevel));
                    TM.App.Log($"[LogLevel] 全局级别已调整: {oldLevel} -> {value}");
                    GlobalToast.Info("级别已更新", $"全局日志级别已调整为 {value}");
                }
            }
        }

        public LogLevelEnum MinimumLevel
        {
            get => _settings.MinimumLevel;
            set { _settings.MinimumLevel = value; OnPropertyChanged(nameof(MinimumLevel)); }
        }

        public List<LogLevelEnum> LogLevels { get; } = null!;

        public ObservableCollection<ModuleLevelItem> ModuleLevels { get; } = null!;

        public ObservableCollection<LevelChangeRecord> ChangeHistory { get; } = null!;

        public ObservableCollection<LevelStatisticsItem> LevelStatisticsItems { get; } = null!;

        public List<string> PresetNames { get; } = null!;

        public ICommand SaveCommand { get; } = null!;
        public ICommand ResetCommand { get; } = null!;
        public ICommand AddModuleCommand { get; } = null!;
        public ICommand RemoveModuleCommand { get; } = null!;
        public ICommand ApplyPresetCommand { get; } = null!;
        public ICommand ViewHistoryCommand { get; } = null!;
        public ICommand ClearHistoryCommand { get; } = null!;
        public ICommand RefreshStatsCommand { get; } = null!;
        public ICommand ResetStatsCommand { get; } = null!;

        public string GetLevelColor(LogLevelEnum level)
        {
            return _settings.LevelColors.ContainsKey(level) ? _settings.LevelColors[level] : "#FFFFFF";
        }

        private void LoadModuleLevels()
        {
            ModuleLevels.Clear();
            foreach (var kvp in _settings.ModuleLevels)
            {
                ModuleLevels.Add(new ModuleLevelItem
                {
                    ModuleName = kvp.Key,
                    Level = kvp.Value
                });
            }
        }

        private async void SaveSettings()
        {
            try
            {
                _settings.ModuleLevels.Clear();
                foreach (var item in ModuleLevels)
                {
                    if (!string.IsNullOrWhiteSpace(item.ModuleName))
                    {
                        _settings.ModuleLevels[item.ModuleName] = item.Level;
                    }
                }

                var directory = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_settings, JsonHelper.Default);
                var tmpLls = _settingsFilePath + ".tmp";
                await File.WriteAllTextAsync(tmpLls, json);
                File.Move(tmpLls, _settingsFilePath, overwrite: true);

                _logManager.Reload();

                TM.App.Log($"[LogLevel] 保存设置成功");
                GlobalToast.Success("保存成功", "日志级别设置已保存");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LogLevel] 保存设置失败: {ex.Message}");
                GlobalToast.Error("保存失败", $"无法保存日志级别设置: {ex.Message}");
            }
        }

        private void ResetSettings()
        {
            var result = StandardDialog.ShowConfirm(
                "是否将所有日志级别设置恢复为默认值？",
                "确认重置"
            );

            if (result)
            {
                _settings = new LogLevelSettings();
                OnPropertyChanged(nameof(GlobalLevel));
                OnPropertyChanged(nameof(MinimumLevel));
                LoadModuleLevels();
                SaveSettings();
                TM.App.Log($"[LogLevel] 重置设置成功");
                GlobalToast.Info("已重置", "日志级别设置已恢复为默认值");
            }
        }

        private void AddModule()
        {
            var moduleName = StandardDialog.ShowInput("请输入模块名称", "添加模块");
            if (!string.IsNullOrWhiteSpace(moduleName))
            {
                if (ModuleLevels.Any(m => m.ModuleName == moduleName))
                {
                    GlobalToast.Warning("模块已存在", $"模块 '{moduleName}' 已经配置过日志级别");
                    return;
                }

                ModuleLevels.Add(new ModuleLevelItem
                {
                    ModuleName = moduleName,
                    Level = LogLevelEnum.Info
                });

                TM.App.Log($"[LogLevel] 添加模块: {moduleName}");
                GlobalToast.Success("添加成功", $"已添加模块 '{moduleName}'");
            }
        }

        private void RemoveModule()
        {
            if (ModuleLevels.Count > 0)
            {
                var lastModule = ModuleLevels.Last();
                var result = StandardDialog.ShowConfirm(
                    $"是否删除模块 '{lastModule.ModuleName}' 的日志级别配置？",
                    "确认删除"
                );

                if (result)
                {
                    ModuleLevels.Remove(lastModule);
                    TM.App.Log($"[LogLevel] 删除模块: {lastModule.ModuleName}");
                    GlobalToast.Success("删除成功", $"已删除模块 '{lastModule.ModuleName}'");
                }
            }
            else
            {
                GlobalToast.Warning("无可删除项", "没有配置任何模块级别");
            }
        }

        private void RecordLevelChange(string target, LogLevelEnum oldLevel, LogLevelEnum newLevel, string reason)
        {
            var record = new LevelChangeRecord
            {
                Target = target,
                OldLevel = oldLevel,
                NewLevel = newLevel,
                Reason = reason
            };

            ChangeHistory.Insert(0, record);

            while (ChangeHistory.Count > 100)
            {
                ChangeHistory.RemoveAt(ChangeHistory.Count - 1);
            }

            SaveChangeHistory();
        }

        private async void SaveChangeHistory()
        {
            try
            {
                var directory = Path.GetDirectoryName(_historyFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(ChangeHistory.ToList(), JsonHelper.Default);
                var tmpLlh = _historyFilePath + ".tmp";
                await File.WriteAllTextAsync(tmpLlh, json);
                File.Move(tmpLlh, _historyFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LogLevel] 保存变更历史失败: {ex.Message}");
            }
        }

        private void ViewHistory()
        {
            if (ChangeHistory.Count == 0)
            {
                GlobalToast.Info("无历史记录", "还没有任何级别变更记录");
                return;
            }

            var historyText = string.Join("\n", ChangeHistory.Take(20).Select(r =>
                $"[{r.Timestamp:MM-dd HH:mm}] {r.Target}: {r.OldLevel} → {r.NewLevel} ({r.User})"));

            StandardDialog.ShowInfo(historyText, $"级别变更历史（最近{Math.Min(20, ChangeHistory.Count)}条）");
        }

        private void ClearHistory()
        {
            var result = StandardDialog.ShowConfirm(
                "是否清空所有级别变更历史记录？",
                "确认清空"
            );

            if (result)
            {
                ChangeHistory.Clear();
                SaveChangeHistory();
                TM.App.Log($"[LogLevel] 清空变更历史成功");
                GlobalToast.Success("清空成功", "级别变更历史已清空");
            }
        }

        private async void SaveStatistics()
        {
            try
            {
                var directory = Path.GetDirectoryName(_statisticsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_statistics, JsonHelper.Default);
                var tmpLlst = _statisticsFilePath + ".tmp";
                await File.WriteAllTextAsync(tmpLlst, json);
                File.Move(tmpLlst, _statisticsFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LogLevel] 保存统计信息失败: {ex.Message}");
            }
        }

        private void RefreshStatistics()
        {
            try
            {
                LevelStatisticsItems.Clear();

                foreach (var level in LogLevels)
                {
                    var count = _statistics.LevelCounts.ContainsKey(level) ? _statistics.LevelCounts[level] : 0;
                    var percentage = _statistics.GetLevelPercentage(level);

                    LevelStatisticsItems.Add(new LevelStatisticsItem
                    {
                        Level = level,
                        Count = count,
                        Percentage = percentage,
                        Color = GetLevelColor(level)
                    });
                }

                TM.App.Log($"[LogLevel] 刷新统计信息: 总计 {_statistics.TotalLogs} 条日志");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LogLevel] 刷新统计信息失败: {ex.Message}");
            }
        }

        private void ResetStatistics()
        {
            var result = StandardDialog.ShowConfirm(
                "是否重置所有级别统计信息？",
                "确认重置"
            );

            if (result)
            {
                _statistics = new LevelStatistics();
                SaveStatistics();
                RefreshStatistics();
                TM.App.Log($"[LogLevel] 重置统计信息成功");
                GlobalToast.Success("重置成功", "级别统计信息已重置");
            }
        }

        private void SimulateLogGeneration()
        {
            try
            {
                var random = new Random();
                var level = (LogLevelEnum)random.Next(0, 6);
                _statistics.IncrementLevel(level);

                if (_statistics.TotalLogs % 10 == 0)
                {
                    SaveStatistics();
                    RefreshStatistics();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LogLevel] 模拟日志生成失败: {ex.Message}");
            }
        }

        private void ApplyPreset(string presetKey)
        {
            if (string.IsNullOrEmpty(presetKey) || !LevelPreset.StandardPresets.ContainsKey(presetKey))
                return;

            try
            {
                var preset = LevelPreset.StandardPresets[presetKey];

                TM.App.Log($"[LogLevel] 应用预设方案: {preset.Name}");

                var oldGlobalLevel = GlobalLevel;
                _settings.GlobalLevel = preset.GlobalLevel;
                _settings.MinimumLevel = preset.MinimumLevel;
                _settings.ModuleLevels.Clear();
                foreach (var kvp in preset.ModuleLevels)
                {
                    _settings.ModuleLevels[kvp.Key] = kvp.Value;
                }

                RecordLevelChange($"预设:{preset.Name}", oldGlobalLevel, preset.GlobalLevel, $"应用{preset.Name}预设");

                OnPropertyChanged(nameof(GlobalLevel));
                OnPropertyChanged(nameof(MinimumLevel));
                LoadModuleLevels();

                SaveSettings();

                GlobalToast.Success("预设已应用", $"{preset.Name}: {preset.Description}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LogLevel] 应用预设失败: {ex.Message}");
                GlobalToast.Error("应用失败", $"无法应用预设方案: {ex.Message}");
            }
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class LevelStatisticsItem
    {
        public LogLevelEnum Level { get; set; }
        public int Count { get; set; }
        public double Percentage { get; set; }
        public string Color { get; set; } = string.Empty;
    }
}

