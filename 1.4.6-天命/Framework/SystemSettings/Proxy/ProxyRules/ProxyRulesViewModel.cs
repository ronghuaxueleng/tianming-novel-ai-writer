using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using TM.Framework.Common.Controls.Dialogs;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.SystemSettings.Proxy.Services;
using TM.Framework.Common.Services;

namespace TM.Framework.SystemSettings.Proxy.ProxyRules
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ProxyRulesViewModel : INotifyPropertyChanged
    {
        private readonly string _settingsFile;
        private ProxyRulesSettings _settings = new();

        private string _searchKeyword = string.Empty;
        private ProxyRule? _selectedRule;
        private readonly ProxyRuleService _ruleService;

        public ObservableCollection<ProxyRule> Rules { get; } = new();
        public ObservableCollection<RuleType> RuleTypes { get; } = new();
        public ObservableCollection<ProxyAction> ProxyActions { get; } = new();

        public ObservableCollection<RuleMatchHistory> MatchHistory { get; } = new();
        public ObservableCollection<RuleUsageStatistics> Statistics { get; } = new();
        public ObservableCollection<RuleEffectiveness> Effectiveness { get; } = new();
        public ObservableCollection<RuleRecommendation> Recommendations { get; } = new();
        public RuleConflictAnalysis? CurrentConflictAnalysis { get; private set; }

        public string SearchKeyword
        {
            get => _searchKeyword;
            set { _searchKeyword = value; OnPropertyChanged(nameof(SearchKeyword)); FilterRules(); }
        }

        public ProxyRule? SelectedRule
        {
            get => _selectedRule;
            set { _selectedRule = value; OnPropertyChanged(nameof(SelectedRule)); OnPropertyChanged(nameof(HasSelectedRule)); }
        }

        public bool HasSelectedRule => SelectedRule != null;

        public ICommand AddRuleCommand { get; }
        public ICommand EditRuleCommand { get; }
        public ICommand DeleteRuleCommand { get; }
        public ICommand ToggleRuleCommand { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand LoadTemplateCommand { get; }

        public ICommand ViewMatchHistoryCommand { get; }
        public ICommand AnalyzeEffectivenessCommand { get; }
        public ICommand DetectConflictsCommand { get; }
        public ICommand GenerateRecommendationsCommand { get; }
        public ICommand OptimizeRulesCommand { get; }
        public ICommand ExportRuleReportCommand { get; }

        public ProxyRulesViewModel(ProxyRuleService ruleService)
        {
            _ruleService = ruleService;
            _settingsFile = StoragePathHelper.GetFilePath("Framework", "Network/Proxy/ProxyRules", "rules_settings.json");

            AddRuleCommand = new RelayCommand(AddRule);
            EditRuleCommand = new RelayCommand(EditRule);
            DeleteRuleCommand = new RelayCommand<ProxyRule>(DeleteRule);
            ToggleRuleCommand = new RelayCommand<ProxyRule>(ToggleRule);
            MoveUpCommand = new RelayCommand<ProxyRule>(MoveUp);
            MoveDownCommand = new RelayCommand<ProxyRule>(MoveDown);
            ImportCommand = new RelayCommand(Import);
            ExportCommand = new RelayCommand(Export);
            LoadTemplateCommand = new RelayCommand<string>(LoadTemplate);

            ViewMatchHistoryCommand = new RelayCommand(ViewMatchHistory);
            AnalyzeEffectivenessCommand = new RelayCommand(AnalyzeEffectiveness);
            DetectConflictsCommand = new RelayCommand(DetectConflicts);
            GenerateRecommendationsCommand = new RelayCommand(GenerateRecommendations);
            OptimizeRulesCommand = new RelayCommand(OptimizeRules);
            ExportRuleReportCommand = new RelayCommand(ExportRuleReport);

            InitializeEnums();
            AsyncSettingsLoader.LoadOrDefer<ProxyRulesSettings>(_settingsFile, s =>
            {
                _settings = s;
                MatchHistory.Clear();
                foreach (var item in _settings.MatchHistory.OrderByDescending(h => h.Timestamp).Take(100))
                    MatchHistory.Add(item);
                Statistics.Clear();
                foreach (var stat in _settings.UsageStatistics.OrderByDescending(st => st.TotalMatches).Take(20))
                    Statistics.Add(stat);
                Effectiveness.Clear();
                foreach (var eff in _settings.EffectivenessData)
                    Effectiveness.Add(eff);
                LoadRules();
            }, "ProxyRules");
            TM.App.Log("[ProxyRulesViewModel] 初始化完成");
        }

        private void InitializeEnums()
        {
            foreach (RuleType type in Enum.GetValues(typeof(RuleType)))
            {
                RuleTypes.Add(type);
            }

            foreach (ProxyAction action in Enum.GetValues(typeof(ProxyAction)))
            {
                ProxyActions.Add(action);
            }
        }

        private void LoadRules()
        {
            try
            {
                Rules.Clear();
                var rules = _ruleService.GetRules();
                foreach (var rule in rules)
                {
                    Rules.Add(rule);
                }
            }
            catch (Exception ex)
            {
                GlobalToast.Error("加载失败", $"加载规则失败: {ex.Message}");
            }
        }

        private void FilterRules()
        {
            if (string.IsNullOrWhiteSpace(_searchKeyword))
            {
                LoadRules();
                return;
            }

            Rules.Clear();
            var rules = _ruleService.GetRules()
                .Where(r => r.Pattern.Contains(_searchKeyword, StringComparison.OrdinalIgnoreCase) ||
                            r.Description.Contains(_searchKeyword, StringComparison.OrdinalIgnoreCase));

            foreach (var rule in rules)
            {
                Rules.Add(rule);
            }
        }

        private void AddRule()
        {
            var dialog = new RuleEditDialog();
            StandardDialog.EnsureOwnerAndTopmost(dialog, null);
            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                _ruleService.AddRule(dialog.Result);
                LoadRules();
                GlobalToast.Success("添加成功", "规则已添加");
            }
        }

        private void EditRule()
        {
            if (SelectedRule == null) return;

            var dialog = new RuleEditDialog(SelectedRule);
            StandardDialog.EnsureOwnerAndTopmost(dialog, null);
            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                _ruleService.UpdateRule(dialog.Result);
                LoadRules();
                GlobalToast.Success("更新成功", "规则已更新");
            }
        }

        private void DeleteRule(ProxyRule? rule)
        {
            if (rule == null) return;

            if (StandardDialog.ShowConfirm($"确定要删除规则 '{rule.Pattern}' 吗？", "确认删除"))
            {
                _ruleService.DeleteRule(rule.Id);
                LoadRules();
                GlobalToast.Success("删除成功", "规则已删除");
            }
        }

        private void ToggleRule(ProxyRule? rule)
        {
            if (rule == null) return;

            rule.Enabled = !rule.Enabled;
            _ruleService.ToggleRule(rule.Id, rule.Enabled);
            OnPropertyChanged(nameof(Rules));
        }

        private void MoveUp(ProxyRule? rule)
        {
            if (rule == null) return;

            _ruleService.MovePriority(rule.Id, true);
            LoadRules();
        }

        private void MoveDown(ProxyRule? rule)
        {
            if (rule == null) return;

            _ruleService.MovePriority(rule.Id, false);
            LoadRules();
        }

        private void Import()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON文件|*.json|所有文件|*.*",
                Title = "导入规则"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _ruleService.ImportRules(dialog.FileName, append: true);
                    LoadRules();
                    GlobalToast.Success("导入成功", "规则已导入");
                }
                catch (Exception ex)
                {
                    StandardDialog.ShowError("导入失败", $"导入规则失败: {ex.Message}");
                }
            }
        }

        private void Export()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JSON文件|*.json",
                FileName = $"proxy_rules_{DateTime.Now:yyyyMMdd}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _ruleService.ExportRules(dialog.FileName);
                    GlobalToast.Success("导出成功", $"已保存到: {dialog.FileName}");
                }
                catch (Exception ex)
                {
                    StandardDialog.ShowError("导出失败", $"导出规则失败: {ex.Message}");
                }
            }
        }

        private void LoadTemplate(string? templateName)
        {
            if (string.IsNullOrEmpty(templateName)) return;

            if (StandardDialog.ShowConfirm($"确定要加载 {templateName} 模板吗？将添加预设规则。", "确认加载"))
            {
                _ruleService.LoadPresetTemplate(templateName);
                LoadRules();
                GlobalToast.Success("加载成功", "预设规则已加载");
            }
        }

        private async void SaveSettings()
        {
            try
            {
                _settings.LastUpdated = DateTime.Now;

                var json = JsonSerializer.Serialize(_settings, JsonHelper.CnDefault);
                var tmpPrv = _settingsFile + ".tmp";
                await File.WriteAllTextAsync(tmpPrv, json);
                File.Move(tmpPrv, _settingsFile, overwrite: true);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ProxyRules] 保存设置失败: {ex.Message}");
            }
        }

        private void ViewMatchHistory()
        {
            if (!MatchHistory.Any())
            {
                GlobalToast.Info("无历史记录", "暂无规则匹配历史");
                return;
            }

            GlobalToast.Info("匹配历史", $"共有 {MatchHistory.Count} 条匹配记录");
        }

        private void AnalyzeEffectiveness()
        {
            try
            {
                Effectiveness.Clear();

                var rules = _ruleService.GetRules();
                foreach (var rule in rules)
                {
                    var stat = _settings.UsageStatistics.FirstOrDefault(s => s.RuleId == rule.Id);
                    if (stat == null) continue;

                    var effectiveness = new RuleEffectiveness
                    {
                        RuleId = rule.Id,
                        RuleName = rule.Pattern,
                        HitRate = stat.TotalMatches > 0 ? (stat.SuccessfulMatches * 100.0 / stat.TotalMatches) : 0,
                        Accuracy = stat.SuccessRate,
                        ImpactScore = CalculateImpactScore(stat)
                    };

                    if (effectiveness.HitRate > 80 && effectiveness.Accuracy > 80)
                        effectiveness.Level = EffectivenessLevel.Excellent;
                    else if (effectiveness.HitRate > 60 && effectiveness.Accuracy > 60)
                        effectiveness.Level = EffectivenessLevel.Good;
                    else if (effectiveness.HitRate > 40)
                        effectiveness.Level = EffectivenessLevel.Fair;
                    else
                        effectiveness.Level = EffectivenessLevel.Poor;

                    if (effectiveness.Level == EffectivenessLevel.Poor)
                    {
                        effectiveness.OptimizationSuggestions.Add("考虑调整规则模式以提高匹配率");
                    }
                    if (effectiveness.Accuracy < 70)
                    {
                        effectiveness.OptimizationSuggestions.Add("检查规则动作是否合适");
                    }

                    effectiveness.Summary = $"{effectiveness.Level} - 命中率: {effectiveness.HitRate:F1}%, 准确率: {effectiveness.Accuracy:F1}%";

                    Effectiveness.Add(effectiveness);
                    _settings.EffectivenessData.Add(effectiveness);
                }

                SaveSettings();
                GlobalToast.Success("分析完成", $"已分析 {Effectiveness.Count} 条规则");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("分析失败", $"效果分析失败: {ex.Message}");
            }
        }

        private int CalculateImpactScore(RuleUsageStatistics stat)
        {
            int score = 0;

            if (stat.TotalMatches > 1000) score += 40;
            else if (stat.TotalMatches > 100) score += 20;
            else if (stat.TotalMatches > 10) score += 10;

            score += (int)(stat.SuccessRate * 0.6);

            return Math.Min(100, score);
        }

        private void DetectConflicts()
        {
            try
            {
                var rules = _ruleService.GetRules().Where(r => r.Enabled).ToList();
                var conflicts = new List<ConflictingRulePair>();

                for (int i = 0; i < rules.Count - 1; i++)
                {
                    for (int j = i + 1; j < rules.Count; j++)
                    {
                        var rule1 = rules[i];
                        var rule2 = rules[j];

                        if (IsPatternOverlap(rule1.Pattern, rule2.Pattern))
                        {
                            conflicts.Add(new ConflictingRulePair
                            {
                                Rule1 = rule1,
                                Rule2 = rule2,
                                Type = ConflictType.PatternOverlap,
                                Severity = ConflictSeverity.Medium,
                                Reason = "规则模式存在重叠",
                                Resolution = "考虑合并规则或调整优先级"
                            });
                        }

                        if (rule1.Pattern == rule2.Pattern && rule1.Action != rule2.Action)
                        {
                            conflicts.Add(new ConflictingRulePair
                            {
                                Rule1 = rule1,
                                Rule2 = rule2,
                                Type = ConflictType.ActionConflict,
                                Severity = ConflictSeverity.High,
                                Reason = "相同模式但动作不同",
                                Resolution = "删除或禁用其中一条规则"
                            });
                        }
                    }
                }

                CurrentConflictAnalysis = new RuleConflictAnalysis
                {
                    Conflicts = conflicts,
                    Summary = conflicts.Any() ? $"发现 {conflicts.Count} 个潜在冲突" : "未发现规则冲突"
                };

                OnPropertyChanged(nameof(CurrentConflictAnalysis));

                if (conflicts.Any())
                {
                    GlobalToast.Warning("发现冲突", $"检测到 {conflicts.Count} 个规则冲突");
                }
                else
                {
                    GlobalToast.Success("检测完成", "未发现规则冲突");
                }
            }
            catch (Exception ex)
            {
                GlobalToast.Error("检测失败", $"冲突检测失败: {ex.Message}");
            }
        }

        private bool IsPatternOverlap(string pattern1, string pattern2)
        {
            return pattern1.Contains(pattern2) || pattern2.Contains(pattern1);
        }

        private void GenerateRecommendations()
        {
            try
            {
                Recommendations.Clear();

                var rules = _ruleService.GetRules();

                var lowUsageRules = _settings.UsageStatistics.Where(s => s.TotalMatches < 5).ToList();
                if (lowUsageRules.Any())
                {
                    Recommendations.Add(new RuleRecommendation
                    {
                        Title = "移除低使用规则",
                        Description = $"发现 {lowUsageRules.Count} 条很少使用的规则",
                        Reason = "这些规则很少被匹配，可能不再需要",
                        Type = RecommendationType.RuleRemoval,
                        Priority = 2,
                        ConfidenceScore = 0.75
                    });
                }

                var poorRules = Effectiveness.Where(e => e.Level == EffectivenessLevel.Poor).ToList();
                if (poorRules.Any())
                {
                    Recommendations.Add(new RuleRecommendation
                    {
                        Title = "优化低效规则",
                        Description = $"发现 {poorRules.Count} 条效果较差的规则",
                        Reason = "这些规则的命中率或准确率较低",
                        Type = RecommendationType.RuleOptimization,
                        Priority = 1,
                        ConfidenceScore = 0.85,
                        Benefits = new List<string> { "提高规则效率", "减少误匹配", "优化性能" }
                    });
                }

                var similarRules = FindSimilarRules(rules);
                if (similarRules > 0)
                {
                    Recommendations.Add(new RuleRecommendation
                    {
                        Title = "合并相似规则",
                        Description = "发现多条模式相似的规则可以合并",
                        Reason = "简化规则集，提高管理效率",
                        Type = RecommendationType.RuleConsolidation,
                        Priority = 3,
                        ConfidenceScore = 0.7
                    });
                }

                GlobalToast.Success("推荐生成", $"已生成 {Recommendations.Count} 条优化建议");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("生成失败", $"生成推荐失败: {ex.Message}");
            }
        }

        private int FindSimilarRules(List<ProxyRule> rules)
        {
            int count = 0;
            for (int i = 0; i < rules.Count - 1; i++)
            {
                for (int j = i + 1; j < rules.Count; j++)
                {
                    if (IsSimilarPattern(rules[i].Pattern, rules[j].Pattern))
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        private bool IsSimilarPattern(string pattern1, string pattern2)
        {
            var words1 = pattern1.Split('.');
            var words2 = pattern2.Split('.');
            int common = words1.Intersect(words2).Count();
            return common > words1.Length / 2 || common > words2.Length / 2;
        }

        private void OptimizeRules()
        {
            try
            {
                var rules = _ruleService.GetRules();
                int optimized = 0;

                foreach (var rule in rules)
                {
                    var stat = _settings.UsageStatistics.FirstOrDefault(s => s.RuleId == rule.Id);
                    if (stat != null && stat.TotalMatches < 3 && rule.Enabled)
                    {
                        _ruleService.ToggleRule(rule.Id, false);
                        optimized++;
                    }
                }

                LoadRules();
                GlobalToast.Success("优化完成", $"已优化 {optimized} 条规则");
                TM.App.Log($"[ProxyRules] 规则优化完成: {optimized}条");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("优化失败", $"规则优化失败: {ex.Message}");
            }
        }

        private async void ExportRuleReport()
        {
            try
            {
                var rules = _ruleService.GetRules();

                var report = new RuleReport
                {
                    GeneratedTime = DateTime.Now,
                    TotalRules = rules.Count,
                    EnabledRules = rules.Count(r => r.Enabled),
                    DisabledRules = rules.Count(r => !r.Enabled),
                    TopRules = Statistics.Take(5).ToList(),
                    LowEfficiencyRules = Effectiveness.Where(e => e.Level == EffectivenessLevel.Poor).ToList(),
                    ConflictAnalysis = CurrentConflictAnalysis ?? new RuleConflictAnalysis(),
                    Recommendations = Recommendations.ToList(),
                    Summary = $"规则集报告 - 生成于 {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    HealthScore = CalculateRuleHealthScore()
                };

                var dialog = new SaveFileDialog
                {
                    Filter = "JSON文件|*.json",
                    FileName = $"proxy_rules_report_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    var json = JsonSerializer.Serialize(report, JsonHelper.CnDefault);
                    var filePath = dialog.FileName;
                    await Task.Run(() =>
                    {
                        var tmp = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        File.WriteAllText(tmp, json);
                        File.Move(tmp, filePath, overwrite: true);
                    });

                    GlobalToast.Success("导出成功", $"报告已保存到: {filePath}");
                    TM.App.Log($"[ProxyRules] 报告已导出: {filePath}");
                }
            }
            catch (Exception ex)
            {
                GlobalToast.Error("导出失败", $"导出报告失败: {ex.Message}");
            }
        }

        private int CalculateRuleHealthScore()
        {
            int score = 100;

            var rules = _ruleService.GetRules();

            if (rules.Any())
            {
                var enabledRatio = rules.Count(r => r.Enabled) * 100.0 / rules.Count;
                if (enabledRatio < 50) score -= 20;
            }

            if (CurrentConflictAnalysis != null && CurrentConflictAnalysis.TotalConflicts > 0)
            {
                score -= CurrentConflictAnalysis.TotalConflicts * 10;
            }

            var poorCount = Effectiveness.Count(e => e.Level == EffectivenessLevel.Poor);
            score -= poorCount * 5;

            return Math.Max(0, score);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RuleEditDialog : Window
    {
        public ProxyRule? Result { get; private set; }

        public RuleEditDialog(ProxyRule? existingRule = null)
        {
            Title = existingRule == null ? "添加规则" : "编辑规则";
            Width = 500;
            Height = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            Result = existingRule ?? new ProxyRule();
        }
    }
}

