using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using TM.Framework.Common.Controls;
using TM.Framework.Common.ViewModels;
using TM.Modules.Design.SmartParsing.ContentRefinery.Models;
using TM.Modules.Design.SmartParsing.ContentRefinery.Services;

namespace TM.Modules.Design.SmartParsing.ContentRefinery
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class ContentRefineryViewModel : INotifyPropertyChanged, IAIGeneratingState
    {
        private readonly ContentRefineryService _refineryService;
        private readonly RefineryHistoryService _historyService;
        private readonly RefineryWorkStateService _workStateService;
        private bool _suppressStateSave;
        private readonly System.Threading.Timer _saveDebounceTimer;
        private CancellationTokenSource? _cts;
        private string? _fullFileContent;

        public ContentRefineryViewModel()
        {
            _saveDebounceTimer = new System.Threading.Timer(_ =>
            {
                _ = SaveWorkStateAsync().ContinueWith(
                    t => TM.App.Log($"[ContentRefinery] 保存工作区状态异常: {t.Exception?.GetBaseException().Message}"),
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            }, null, Timeout.Infinite, Timeout.Infinite);
            _refineryService = ServiceLocator.Get<ContentRefineryService>();
            _historyService = ServiceLocator.Get<RefineryHistoryService>();
            _workStateService = ServiceLocator.Get<RefineryWorkStateService>();

            AddCommand = new RelayCommand(() => { }, () => false);
            SaveCommand = new RelayCommand(() => { }, () => false);
            DeleteCommand = new RelayCommand(() => { }, () => false);
            DeleteAllCommand = new RelayCommand(() => { }, () => false);

            SelectNodeCommand = new RelayCommand<object>(OnNodeSelected);
            TreeAfterActionCommand = new RelayCommand(() => { });
            AIGenerateCommand = new AsyncRelayCommand(ExecuteRefineAsync, CanExecuteRefine);
            CommitCommand = new AsyncRelayCommand(ExecuteCommitAsync, () => RefineryResults.Count > 0);
            ClearCommand = new RelayCommand(ExecuteClear, () => true);
            UploadFileCommand = new RelayCommand(ExecuteUploadFile);
            ClearHistoryCommand = new RelayCommand(ExecuteClearHistory, () => HistoryRecords.Count > 0);
            CancelRefineCommand = new RelayCommand(ExecuteCancelRefine, () => IsRefining);
            ToggleHistoryExpandCommand = new RelayCommand(param =>
            {
                if (param is TM.Modules.Design.SmartParsing.ContentRefinery.Models.RefineryHistoryRecord record)
                    record.IsExpanded = !record.IsExpanded;
            });

            InitializeTree();

            _ = InitContentAsync();
        }

        private ObservableCollection<TreeNodeItem> _treeData = new();
        public ObservableCollection<TreeNodeItem> TreeData
        {
            get => _treeData;
            set { _treeData = value; OnPropertyChanged(); }
        }

        private string _searchKeyword = string.Empty;
        public string SearchKeyword
        {
            get => _searchKeyword;
            set { _searchKeyword = value; OnPropertyChanged(); }
        }

        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set { _selectedTabIndex = value; OnPropertyChanged(); }
        }

        private RefineryModuleType? _selectedModuleType;
        public RefineryModuleType? SelectedModuleType
        {
            get => _selectedModuleType;
            set
            {
                _selectedModuleType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedModuleDisplayName));
                OnPropertyChanged(nameof(IsAIGenerateEnabled));
                ScheduleSaveWorkState();
            }
        }

        public string SelectedModuleDisplayName => _selectedModuleType.HasValue
            ? _selectedModuleType.Value switch
            {
                RefineryModuleType.CharacterRules => "角色规则",
                RefineryModuleType.PlotRules => "剧情规则",
                RefineryModuleType.WorldRules => "世界观规则",
                RefineryModuleType.FactionRules => "势力规则",
                RefineryModuleType.LocationRules => "位置规则",
                _ => string.Empty
            }
            : "未选择";

        private string _rawContent = string.Empty;
        public string RawContent
        {
            get => _rawContent;
            set
            {
                _rawContent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsAIGenerateEnabled));
                ScheduleSaveWorkState();
            }
        }

        private string _prerequisiteValue = string.Empty;
        public string PrerequisiteValue
        {
            get => _prerequisiteValue;
            set
            {
                _prerequisiteValue = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsAIGenerateEnabled));
                ScheduleSaveWorkState();
            }
        }

        private List<RefineryRequiredInput> _currentRequiredInputs = new();
        public List<RefineryRequiredInput> CurrentRequiredInputs
        {
            get => _currentRequiredInputs;
            set { _currentRequiredInputs = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRequiredInputs)); }
        }

        public bool HasRequiredInputs => CurrentRequiredInputs.Count > 0;

        private ObservableCollection<RefineryResult> _refineryResults = new();
        public ObservableCollection<RefineryResult> RefineryResults
        {
            get => _refineryResults;
            set { _refineryResults = value; OnPropertyChanged(); ScheduleSaveWorkState(); }
        }

        private ObservableCollection<RefineryHistoryRecord> _historyRecords = new();
        public ObservableCollection<RefineryHistoryRecord> HistoryRecords
        {
            get => _historyRecords;
            set { _historyRecords = value; OnPropertyChanged(); }
        }

        private bool _isRefining;
        public bool IsRefining
        {
            get => _isRefining;
            set { _isRefining = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsAIGenerating)); OnPropertyChanged(nameof(IsAIGenerateEnabled)); }
        }

        public bool IsAIGenerating => IsRefining;
        public bool IsBatchGenerating => false;
        public string BatchProgressText => string.Empty;
        public ICommand CancelBatchGenerationCommand => CancelRefineCommand;

        public bool IsAIGenerateEnabled => CanExecuteRefine();

        public bool IsContentTruncated => _fullFileContent != null && _fullFileContent.Length > _rawContent.Length;

        public ICommand AddCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand DeleteAllCommand { get; }
        public ICommand SelectNodeCommand { get; }
        public ICommand TreeAfterActionCommand { get; }
        public ICommand AIGenerateCommand { get; }
        public ICommand CommitCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand UploadFileCommand { get; }
        public ICommand ClearHistoryCommand { get; }
        public ICommand CancelRefineCommand { get; }
        public ICommand ToggleHistoryExpandCommand { get; }

        private void InitializeTree()
        {
            var nodes = _refineryService.BuildModuleSelectionTree();
            TreeData = new ObservableCollection<TreeNodeItem>(nodes);
        }

        private void OnNodeSelected(object? param)
        {
            if (param is TreeNodeItem node && node.Tag is RefineryModuleType moduleType)
            {
                SelectedModuleType = moduleType;

                var config = _refineryService.GetModuleConfig(moduleType);
                CurrentRequiredInputs = config?.RequiredInputs ?? new();
                PrerequisiteValue = string.Empty;
            }
        }

        private bool CanExecuteRefine()
        {
            if (IsRefining) return false;
            if (!_selectedModuleType.HasValue) return false;
            if (string.IsNullOrWhiteSpace(RawContent)) return false;

            if (HasRequiredInputs)
            {
                foreach (var input in CurrentRequiredInputs)
                {
                    if (input.IsRequired && input.Validator != null && !input.Validator(PrerequisiteValue))
                        return false;
                }
            }

            return true;
        }

        private async System.Threading.Tasks.Task ExecuteRefineAsync()
        {
            if (!_selectedModuleType.HasValue) return;

            var (isValid, missing) = _refineryService.ValidateDependencies(_selectedModuleType.Value);
            if (!isValid)
            {
                StandardDialog.ShowWarning(
                    $"请先提炼以下模块：\n\n{string.Join("\n", missing.Select(m => $"  • {m}"))}\n\n" +
                    "当前模块的部分字段引用了这些模块的数据，必须先有数据才能正确关联。",
                    "前置依赖未满足");
                return;
            }

            IsRefining = true;
            _cts = new CancellationTokenSource();

            try
            {
                Dictionary<string, string>? prerequisites = null;
                if (HasRequiredInputs && !string.IsNullOrWhiteSpace(PrerequisiteValue))
                {
                    prerequisites = new();
                    foreach (var input in CurrentRequiredInputs)
                        prerequisites[input.Key] = PrerequisiteValue;
                }

                var existingNames = _refineryService.GetExistingEntityNames(_selectedModuleType.Value);
                if (existingNames.Count > 0)
                {
                    GlobalToast.Info("AI提炼", $"目标模块已有 {existingNames.Count} 条数据，同名实体将自动跳过");
                }

                var effectiveContent = _fullFileContent ?? RawContent;
                var results = await _refineryService.RefineAsync(
                    effectiveContent, _selectedModuleType.Value, prerequisites, _cts.Token);

                if (results.Count > 0)
                {
                    RefineryResults = new ObservableCollection<RefineryResult>(results);
                    SelectedTabIndex = 1;
                    GlobalToast.Success("AI提炼", $"成功提炼 {results.Count} 条数据");
                }
                else
                {
                    if (existingNames.Count > 0)
                        GlobalToast.Warning("AI提炼", $"目标模块已有 {existingNames.Count} 条同名数据，AI跳过了全部重复实体。\n如需重新提取，请先删除已有数据。");
                    else
                        GlobalToast.Warning("AI提炼", "未提炼出有效数据，请检查输入内容");
                }
            }
            catch (OperationCanceledException)
            {
                GlobalToast.Info("AI提炼", "已取消提炼");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentRefinery] 提炼异常: {ex.Message}");
                GlobalToast.Error("AI提炼", $"提炼失败: {ex.Message}");
            }
            finally
            {
                IsRefining = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void ExecuteCancelRefine()
        {
            _cts?.Cancel();
        }

        private async System.Threading.Tasks.Task ExecuteCommitAsync()
        {
            if (!_selectedModuleType.HasValue || RefineryResults.Count == 0) return;

            var confirm = StandardDialog.ShowConfirm(
                $"即将把 {RefineryResults.Count(r => r.IsValid)} 条数据落盘到【{SelectedModuleDisplayName}】模块，是否继续？",
                "确认落盘");
            if (!confirm) return;

            try
            {
                await _refineryService.CommitAsync(RefineryResults.ToList(), _selectedModuleType.Value);

                var validResults = RefineryResults.Where(r => r.IsValid).ToList();
                var summaryLines = new System.Text.StringBuilder();
                for (int i = 0; i < validResults.Count; i++)
                {
                    var r = validResults[i];
                    summaryLines.AppendLine($"{i + 1}. {r.Name}");
                    foreach (var df in r.DisplayFields)
                        summaryLines.AppendLine($"   {df.Key}：{df.Value}");
                    if (i < validResults.Count - 1)
                        summaryLines.AppendLine();
                }

                var record = new RefineryHistoryRecord
                {
                    TargetModule = _selectedModuleType.Value,
                    RawContentPreview = RawContent.Length > 500 ? RawContent.Substring(0, 500) : RawContent,
                    ResultsSummary = summaryLines.ToString().TrimEnd(),
                    ResultCount = validResults.Count,
                    IsCommitted = true
                };
                _historyService.Add(record);
                RefreshHistory();

                GlobalToast.Success("落盘完成", $"已成功写入 {record.ResultCount} 条数据到【{SelectedModuleDisplayName}】");

                RefineryResults.Clear();
                OnPropertyChanged(nameof(RefineryResults));
                _ = SaveWorkStateAsync();
                SelectedTabIndex = 0;

                RefreshTargetModuleTree(_selectedModuleType.Value);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentRefinery] 落盘失败: {ex.Message}");
                GlobalToast.Error("落盘失败", ex.Message);
            }
        }

        private void RefreshTargetModuleTree(RefineryModuleType moduleType)
        {
            try
            {
                Type? viewType = moduleType switch
                {
                    RefineryModuleType.WorldRules => typeof(TM.Modules.Design.GlobalSettings.WorldRules.WorldRulesView),
                    RefineryModuleType.CharacterRules => typeof(TM.Modules.Design.Elements.CharacterRules.CharacterRulesView),
                    RefineryModuleType.FactionRules => typeof(TM.Modules.Design.Elements.FactionRules.FactionRulesView),
                    RefineryModuleType.LocationRules => typeof(TM.Modules.Design.Elements.LocationRules.LocationRulesView),
                    RefineryModuleType.PlotRules => typeof(TM.Modules.Design.Elements.PlotRules.PlotRulesView),
                    _ => null
                };
                if (viewType == null) return;

                var window = Application.Current.Windows.OfType<TM.Framework.UI.Windows.UnifiedWindow>()
                    .FirstOrDefault();
                if (window?.DataContext is not TM.Framework.UI.Windows.UnifiedWindowViewModel windowVm) return;

                var vm = windowVm.GetOrEnsureViewModel<object>(viewType);
                if (vm == null) return;
                var cmd = vm.GetType().GetProperty("RefreshCommand")?.GetValue(vm) as ICommand;
                cmd?.Execute(null);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentRefinery] 刷新目标模块树失败: {ex.Message}");
            }
        }

        private void ExecuteClear()
        {
            _suppressStateSave = true;
            _saveDebounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _fullFileContent = null;
            RawContent = string.Empty;
            RefineryResults.Clear();
            OnPropertyChanged(nameof(RefineryResults));
            OnPropertyChanged(nameof(IsContentTruncated));
            PrerequisiteValue = string.Empty;
            SelectedTabIndex = 0;
            _workStateService.Clear();
            _suppressStateSave = false;
        }

        private const int FileContentLimit = 80_000;

        private const int FileDisplayLimit = 50_000;

        private async void ExecuteUploadFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "所有支持格式|*.txt;*.md;*.docx|文本文件|*.txt;*.md|Word文档|*.docx|所有文件|*.*",
                Title = "选择要提炼的文件"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                    var fileName = dialog.FileName;

                    string content;
                    if (ext == ".docx")
                        content = await System.Threading.Tasks.Task.Run(() => ReadDocxAsText(fileName)).ConfigureAwait(true);
                    else
                        content = await File.ReadAllTextAsync(fileName).ConfigureAwait(true);

                    if (string.IsNullOrWhiteSpace(content))
                    {
                        GlobalToast.Warning("文件上传", "文件内容为空");
                        return;
                    }

                    if (content.Length > FileContentLimit)
                    {
                        _fullFileContent = content.Substring(0, FileContentLimit);
                        RawContent = content.Substring(0, FileDisplayLimit);
                        OnPropertyChanged(nameof(IsContentTruncated));
                        GlobalToast.Warning("文件上传",
                            $"文件过大（{content.Length:N0}字符），已截取前{FileContentLimit:N0}字符用于AI提炼\n" +
                            $"建议分章节上传以获得最佳效果");
                    }
                    else if (content.Length > FileDisplayLimit)
                    {
                        _fullFileContent = content;
                        RawContent = content.Substring(0, FileDisplayLimit);
                        OnPropertyChanged(nameof(IsContentTruncated));
                        GlobalToast.Info("文件上传",
                            $"已加载文件: {Path.GetFileName(fileName)}（{content.Length:N0}字符）\n" +
                            $"编辑区显示前{FileDisplayLimit:N0}字符，AI提炼将使用完整内容");
                    }
                    else
                    {
                        _fullFileContent = null;
                        RawContent = content;
                        OnPropertyChanged(nameof(IsContentTruncated));
                        GlobalToast.Success("文件上传", $"已加载文件: {Path.GetFileName(fileName)}");
                    }
                }
                catch (Exception ex)
                {
                    GlobalToast.Error("文件上传", $"读取文件失败: {ex.Message}");
                }
            }
        }

        private static string ReadDocxAsText(string filePath)
        {
            using var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(filePath, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null) return string.Empty;

            var sb = new System.Text.StringBuilder();
            foreach (var para in body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
            {
                sb.AppendLine(para.InnerText);
            }
            return sb.ToString().TrimEnd();
        }

        private void ExecuteClearHistory()
        {
            var confirm = StandardDialog.ShowConfirm("确定要清空所有提炼历史记录吗？", "清空历史");
            if (!confirm) return;

            _historyService.ClearAll();
            RefreshHistory();
            GlobalToast.Success("清空历史", "已清空所有提炼历史记录");
        }

        private async Task InitContentAsync()
        {
            try
            {
                var records = await Task.Run(() => _historyService.GetAll()).ConfigureAwait(true);
                var state = await _workStateService.LoadAsync().ConfigureAwait(true);

                HistoryRecords = new ObservableCollection<RefineryHistoryRecord>(records);

                if (state == null) return;

                _suppressStateSave = true;
                try
                {
                    if (state.SelectedModuleType.HasValue)
                    {
                        SelectedModuleType = state.SelectedModuleType;
                        var config = _refineryService.GetModuleConfig(state.SelectedModuleType.Value);
                        CurrentRequiredInputs = config?.RequiredInputs ?? new();
                    }
                    if (!string.IsNullOrEmpty(state.RawContent))
                        RawContent = state.RawContent;
                    if (!string.IsNullOrEmpty(state.PrerequisiteValue))
                        PrerequisiteValue = state.PrerequisiteValue;
                    if (state.PendingResults?.Count > 0)
                    {
                        RefineryResults = new ObservableCollection<RefineryResult>(state.PendingResults);
                        SelectedTabIndex = 1;
                    }
                    _suppressStateSave = false;
                }
                catch (Exception ex)
                {
                    _suppressStateSave = false;
                    TM.App.Log($"[ContentRefinery] 恢复工作区状态失败: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentRefinery] 初始化内容失败: {ex.Message}");
            }
        }

        private void RefreshHistory()
        {
            var records = _historyService.GetAll();
            HistoryRecords = new ObservableCollection<RefineryHistoryRecord>(records);
        }

        private void ScheduleSaveWorkState()
        {
            if (_suppressStateSave) return;
            _saveDebounceTimer.Change(800, Timeout.Infinite);
        }

        private async System.Threading.Tasks.Task SaveWorkStateAsync()
        {
            if (_suppressStateSave) return;
            try
            {
                List<RefineryResult> pendingSnapshot;
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                    pendingSnapshot = new List<RefineryResult>();
                else if (dispatcher.CheckAccess())
                    pendingSnapshot = _refineryResults.ToList();
                else
                    pendingSnapshot = await dispatcher.InvokeAsync(() => _refineryResults.ToList());

                var state = new RefineryWorkState
                {
                    SelectedModuleType = _selectedModuleType,
                    RawContent = _rawContent,
                    PrerequisiteValue = _prerequisiteValue,
                    PendingResults = pendingSnapshot
                };
                _workStateService.Save(state);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentRefinery] 保存工作区状态失败: {ex.Message}");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
