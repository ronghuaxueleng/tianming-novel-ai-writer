using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.Common.Models;
using TM.Framework.Common.ViewModels;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Models;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Services;
using TM.Modules.AIAssistant.PromptTools.VersionTesting.Models;
using TM.Modules.AIAssistant.PromptTools.VersionTesting.Services;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.Interfaces.AI;
using TM.Services.Framework.AI.Interfaces.Prompts;

namespace TM.Modules.AIAssistant.PromptTools.VersionTesting;

[Obfuscation(Exclude = true, ApplyToMembers = true)]
public class VersionTestingViewModel : DataManagementViewModelBase<TestVersionData, PromptCategory, VersionTestingService>, TM.Framework.Common.ViewModels.IAIGeneratingState
{
    private readonly IPromptRepository _promptRepository;
    private readonly IAITextGenerationService _aiTextGenerationService;
    private TestVersionData? _selectedVersion;
    private PromptTemplateData? _selectedPrompt;
    private List<PromptTemplateData> _promptCache = new();
    private CancellationTokenSource? _testCts;
    private AsyncRelayCommand _executeTestCommand = null!;
    private readonly RelayCommand _cancelTestCommand;

    public ObservableCollection<string> CategoryOptions { get; } = new();

    #region 分类A（版本对比左列）

    private PromptTemplateData? _selectedPromptA;
    public PromptTemplateData? SelectedPromptA
    {
        get => _selectedPromptA;
        set
        {
            if (_selectedPromptA != value)
            {
                _selectedPromptA = value;
                OnPropertyChanged();
                UpdatePromptADescription();
            }
        }
    }

    public List<string> QuickTemplateOptions => _quickTemplateOptions;

    public string? SelectedQuickTemplate
    {
        get => _selectedQuickTemplate;
        set
        {
            if (_selectedQuickTemplate != value)
            {
                _selectedQuickTemplate = value;
                OnPropertyChanged();

                if (!string.IsNullOrWhiteSpace(value))
                {
                    ApplyTemplate(value);
                }
            }
        }
    }

    private string _promptADescription = string.Empty;
    public string PromptADescription
    {
        get => _promptADescription;
        set { _promptADescription = value; OnPropertyChanged(); }
    }

    private bool _isCategoryADropdownOpen;
    public bool IsCategoryADropdownOpen
    {
        get => _isCategoryADropdownOpen;
        set { _isCategoryADropdownOpen = value; OnPropertyChanged(); }
    }

    private string _selectedCategoryAPath = string.Empty;
    public string SelectedCategoryAPath
    {
        get => _selectedCategoryAPath;
        set { _selectedCategoryAPath = value; OnPropertyChanged(); }
    }

    private string _selectedCategoryAIcon = string.Empty;
    public string SelectedCategoryAIcon
    {
        get => _selectedCategoryAIcon;
        set { _selectedCategoryAIcon = value; OnPropertyChanged(); }
    }

    public ObservableCollection<TreeNodeItem> CategoryATree { get; } = new();

    #endregion

    #region 分类B（版本对比右列）

    private PromptTemplateData? _selectedPromptB;
    public PromptTemplateData? SelectedPromptB
    {
        get => _selectedPromptB;
        set
        {
            if (_selectedPromptB != value)
            {
                _selectedPromptB = value;
                OnPropertyChanged();
                UpdatePromptBDescription();
            }
        }
    }

    private string _promptBDescription = string.Empty;
    public string PromptBDescription
    {
        get => _promptBDescription;
        set { _promptBDescription = value; OnPropertyChanged(); }
    }

    private bool _isCategoryBDropdownOpen;
    public bool IsCategoryBDropdownOpen
    {
        get => _isCategoryBDropdownOpen;
        set { _isCategoryBDropdownOpen = value; OnPropertyChanged(); }
    }

    private string _selectedCategoryBPath = string.Empty;
    public string SelectedCategoryBPath
    {
        get => _selectedCategoryBPath;
        set { _selectedCategoryBPath = value; OnPropertyChanged(); }
    }

    private string _selectedCategoryBIcon = string.Empty;
    public string SelectedCategoryBIcon
    {
        get => _selectedCategoryBIcon;
        set { _selectedCategoryBIcon = value; OnPropertyChanged(); }
    }

    public ObservableCollection<TreeNodeItem> CategoryBTree { get; } = new();

    #endregion

    #region 测试配置

    private string _testInput = string.Empty;
    public string TestInput
    {
        get => _testInput;
        set { _testInput = value; OnPropertyChanged(); }
    }

    #endregion

    #region 动态评分

    private double _creativityScore;
    public double CreativityScore
    {
        get => _creativityScore;
        set { _creativityScore = value; OnPropertyChanged(); UpdateTotalScore(); }
    }

    private double _coherenceScore;
    public double CoherenceScore
    {
        get => _coherenceScore;
        set { _coherenceScore = value; OnPropertyChanged(); UpdateTotalScore(); }
    }

    private double _logicScore;
    public double LogicScore
    {
        get => _logicScore;
        set { _logicScore = value; OnPropertyChanged(); UpdateTotalScore(); }
    }

    private double _emotionScore;
    public double EmotionScore
    {
        get => _emotionScore;
        set { _emotionScore = value; OnPropertyChanged(); UpdateTotalScore(); }
    }

    private double _totalScore;

    private readonly List<string> _quickTemplateOptions = new()
    {
        "修仙小说",
        "都市言情",
        "科幻冒险",
        "历史架空",
        "悬疑推理",
        "轻小说",
        "群像剧"
    };

    private string? _selectedQuickTemplate;
    public double TotalScore
    {
        get => _totalScore;
        set { _totalScore = value; OnPropertyChanged(); }
    }

    #endregion

    #region 输出内容

    private string _outputAContent = string.Empty;
    public string OutputAContent
    {
        get => _outputAContent;
        set { _outputAContent = value; OnPropertyChanged(); UpdateOutputAStats(); }
    }

    private string _outputAWordCount = "0";
    public string OutputAWordCount
    {
        get => _outputAWordCount;
        set { _outputAWordCount = value; OnPropertyChanged(); }
    }

    private string _outputADuration = "0s";
    public string OutputADuration
    {
        get => _outputADuration;
        set { _outputADuration = value; OnPropertyChanged(); }
    }

    private string _outputBContent = string.Empty;
    public string OutputBContent
    {
        get => _outputBContent;
        set { _outputBContent = value; OnPropertyChanged(); UpdateOutputBStats(); }
    }

    private string _outputBWordCount = "0";
    public string OutputBWordCount
    {
        get => _outputBWordCount;
        set { _outputBWordCount = value; OnPropertyChanged(); }
    }

    private string _outputBDuration = "0s";
    public string OutputBDuration
    {
        get => _outputBDuration;
        set { _outputBDuration = value; OnPropertyChanged(); }
    }

    #endregion

    public TestVersionData? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (_selectedVersion != value)
            {
                _selectedVersion = value;
                OnPropertyChanged();
                LoadFormFromVersion(_selectedVersion);
                UpdateAIGenerateButtonState();
            }
        }
    }

    public PromptTemplateData? SelectedPrompt
    {
        get => _selectedPrompt;
        private set
        {
            if (_selectedPrompt != value)
            {
                _selectedPrompt = value;
                OnPropertyChanged();

                if (value != null && !string.Equals(FormCategory, value.Category, StringComparison.Ordinal))
                {
                    FormCategory = value.Category;
                }
            }
        }
    }

    #region Tab 1: 基本信息与测试配置

    private string _formName = string.Empty;
    public string FormName
    {
        get => _formName;
        set { _formName = value; OnPropertyChanged(); }
    }

    private string _formCategory = string.Empty;
    public string FormCategory
    {
        get => _formCategory;
        set
        {
            if (_formCategory != value)
            {
                _formCategory = value;
                OnPropertyChanged();
                OnCategoryValueChanged(_formCategory);
            }
        }
    }

    private string _formVersionNumber = "1.0";
    public string FormVersionNumber
    {
        get => _formVersionNumber;
        set { _formVersionNumber = value; OnPropertyChanged(); }
    }

    private string _formDescription = string.Empty;
    public string FormDescription
    {
        get => _formDescription;
        set { _formDescription = value; OnPropertyChanged(); }
    }

    private string _formTestInput = string.Empty;
    public string FormTestInput
    {
        get => _formTestInput;
        set { _formTestInput = value; OnPropertyChanged(); }
    }

    private string _formExpectedOutput = string.Empty;
    public string FormExpectedOutput
    {
        get => _formExpectedOutput;
        set { _formExpectedOutput = value; OnPropertyChanged(); }
    }

    private string _formTestScenario = string.Empty;
    public string FormTestScenario
    {
        get => _formTestScenario;
        set { _formTestScenario = value; OnPropertyChanged(); }
    }

    #endregion

    #region Tab 2: 测试结果

    private string _formActualOutput = string.Empty;
    public string FormActualOutput
    {
        get => _formActualOutput;
        set { _formActualOutput = value; OnPropertyChanged(); }
    }

    private int _formRating;
    public int FormRating
    {
        get => _formRating;
        set { _formRating = value; OnPropertyChanged(); }
    }

    private string _formTestNotes = string.Empty;
    public string FormTestNotes
    {
        get => _formTestNotes;
        set { _formTestNotes = value; OnPropertyChanged(); }
    }

    private string _formTestStatus = "未测试";
    public string FormTestStatus
    {
        get => _formTestStatus;
        set { _formTestStatus = value; OnPropertyChanged(); }
    }

    private bool _isTestRunning;
    public bool IsTestRunning
    {
        get => _isTestRunning;
        set
        {
            _isTestRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TM.Framework.Common.ViewModels.IAIGeneratingState.IsAIGenerating));
            OnPropertyChanged(nameof(TM.Framework.Common.ViewModels.IAIGeneratingState.BatchProgressText));
            _executeTestCommand?.RaiseCanExecuteChanged();
            _cancelTestCommand?.RaiseCanExecuteChanged();
        }
    }

    bool TM.Framework.Common.ViewModels.IAIGeneratingState.IsAIGenerating => IsTestRunning;

    bool TM.Framework.Common.ViewModels.IAIGeneratingState.IsBatchGenerating => false;

    string TM.Framework.Common.ViewModels.IAIGeneratingState.BatchProgressText => IsTestRunning ? (FormTestStatus ?? "测试中...") : string.Empty;

    ICommand TM.Framework.Common.ViewModels.IAIGeneratingState.CancelBatchGenerationCommand => _cancelTestCommand;

    #endregion

    #region 命令

    public ICommand TreeNodeSelectedCommand { get; }
    public ICommand AddCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ExecuteTestCommand => _executeTestCommand;
    public ICommand CategoryASelectCommand { get; }
    public ICommand CategoryBSelectCommand { get; }
    public ICommand ApplyTemplateCommand { get; }

    #endregion

    public VersionTestingViewModel(IPromptRepository promptRepository, IAITextGenerationService aiTextGenerationService)
    {
        _promptRepository = promptRepository;
        _aiTextGenerationService = aiTextGenerationService;

        _cancelTestCommand = new RelayCommand(CancelTest, () => IsTestRunning);

        ReloadPromptCache();

        TreeNodeSelectedCommand = new RelayCommand(param => OnTreeNodeSelected(param as TreeNodeItem));
        AddCommand = new RelayCommand(param => BeginCreate(param as TreeNodeItem));
        SaveCommand = new RelayCommand(param => SaveCurrent(param as TreeNodeItem));
        DeleteCommand = new RelayCommand(param => DeleteCurrent(param as TreeNodeItem));
        _executeTestCommand = new AsyncRelayCommand(ExecuteTestAsync, () => !IsTestRunning);
        CategoryASelectCommand = new RelayCommand(param => HandleCategoryASelected(param as TreeNodeItem));
        CategoryBSelectCommand = new RelayCommand(param => HandleCategoryBSelected(param as TreeNodeItem));
        ApplyTemplateCommand = new RelayCommand(param => ApplyTemplate(param as string));

        ShowAIGenerateButton = false;

        try
        {
            RefreshTreeData();
        }
        catch (Exception ex)
        {
            TM.App.Log($"[VersionTestingViewModel] 初始化失败: {ex.Message}");
        }

        RefreshCategoryOptions();
        EnsureValidFormCategory();
        RefreshComparisonTrees();

        try
        {
            PromptService.TemplatesChanged += OnPromptTemplatesChanged;
        }
        catch (Exception ex)
        {
            TM.App.Log($"[VersionTestingViewModel] 订阅 PromptService.TemplatesChanged 失败: {ex.Message}");
        }
    }

    protected override string DefaultDataIcon => "🧪";

    protected override void OnTreeDataRefreshed()
    {
        if (_promptRepository == null)
            return;

        base.OnTreeDataRefreshed();
        ApplyPromptGroupingToTree();
    }

    protected override List<PromptCategory> GetAllCategoriesFromService()
    {
        if (_promptRepository == null)
            return new List<PromptCategory>();

        return _promptRepository.GetAllCategories().ToList();
    }

    private async Task ExecuteTestAsync()
    {
        if (string.IsNullOrWhiteSpace(FormTestInput))
        {
            GlobalToast.Warning("测试输入为空", "请先填写测试输入");
            return;
        }

        if (!await TM.Framework.Common.Services.ProtectionService.CheckFeatureAuthorizationAsync("writing.ai"))
        {
            GlobalToast.Warning("功能受限", "您的订阅计划不支持此功能，请升级订阅");
            return;
        }

        _testCts?.Cancel();
        _testCts?.Dispose();
        _testCts = new CancellationTokenSource();
        var ct = _testCts.Token;

        try
        {
            IsTestRunning = true;
            FormTestStatus = "测试中...";
            GlobalToast.Info("执行测试", "正在调用AI进行测试...");

            var aiResult = await _aiTextGenerationService.GenerateAsync(FormTestInput, ct);
            if (ct.IsCancellationRequested)
            {
                FormTestStatus = "已取消";
                return;
            }

            if (!aiResult.Success || string.IsNullOrWhiteSpace(aiResult.Content))
            {
                var message = string.IsNullOrWhiteSpace(aiResult.ErrorMessage)
                    ? "AI未返回有效内容，请检查模型配置后重试"
                    : aiResult.ErrorMessage;

                FormTestStatus = "测试失败";
                GlobalToast.Error("测试失败", message);
                TM.App.Log($"[VersionTestingViewModel] 测试失败: {message}");
                return;
            }

            FormActualOutput = aiResult.Content;
            FormTestStatus = "已完成";
            OnPropertyChanged(nameof(TM.Framework.Common.ViewModels.IAIGeneratingState.BatchProgressText));

            GlobalToast.Success("测试完成", "AI输出已填充到实际输出区域");
            TM.App.Log("[VersionTestingViewModel] 测试执行成功");
        }
        catch (OperationCanceledException)
        {
            FormTestStatus = "已取消";
            OnPropertyChanged(nameof(TM.Framework.Common.ViewModels.IAIGeneratingState.BatchProgressText));
            TM.App.Log("[VersionTestingViewModel] 测试已取消");
        }
        catch (Exception ex)
        {
            FormTestStatus = "测试失败";
            OnPropertyChanged(nameof(TM.Framework.Common.ViewModels.IAIGeneratingState.BatchProgressText));
            GlobalToast.Error("测试失败", ex.Message);
            TM.App.Log($"[VersionTestingViewModel] 测试失败: {ex.Message}");
        }
        finally
        {
            IsTestRunning = false;
            _testCts?.Dispose();
            _testCts = null;
        }
    }

    private void CancelTest()
    {
        try
        {
            _testCts?.Cancel();
        }
        catch (Exception ex)
        {
            TM.App.Log($"[VersionTestingViewModel] 取消测试失败: {ex.Message}");
        }
    }

    private void OnTreeNodeSelected(TreeNodeItem? node)
    {
        if (node?.Tag is PromptCategory category)
        {
            FormCategory = category.Name;
            SelectedVersion = null;
            SelectedPrompt = null;
            return;
        }

        if (node?.Tag is PromptTemplateData prompt)
        {
            SelectedPrompt = prompt;
            SelectedVersion = null;
            return;
        }

        if (node?.Tag is TestVersionData version)
        {
            SelectedVersion = version;
            SetSelectedPromptById(version.PromptId);
        }
        else
        {
            SelectedVersion = null;
        }
    }

    private void BeginCreate(TreeNodeItem? node = null)
    {
        SelectedVersion = null;
        ClearForm();
        if (node?.Tag is PromptCategory category)
        {
            FormCategory = category.Name;
            SelectedPrompt = null;
        }
        else if (node?.Tag is PromptTemplateData prompt)
        {
            SelectedPrompt = prompt;
        }
        GlobalToast.Info("新建测试版本", "请填写测试版本信息");
    }

    private void SaveCurrent(TreeNodeItem? node = null)
    {
        if (node?.Tag is TestVersionData versionFromNode)
        {
            SelectedVersion = versionFromNode;
        }

        if (string.IsNullOrWhiteSpace(FormName))
        {
            GlobalToast.Warning("保存失败", "版本名称不能为空");
            return;
        }

        if (string.IsNullOrWhiteSpace(FormCategory))
        {
            GlobalToast.Warning("保存失败", "请选择所属分类");
            return;
        }

        try
        {
            var version = SelectedVersion ?? new TestVersionData();
            FillDataFromForm(version);

            if (SelectedVersion == null)
            {
                Service.AddVersion(version);
                GlobalToast.Success("保存成功", $"已创建测试版本: {version.Name}");
                RefreshTreeData();
                FocusOnDataItem(version);
            }
            else
            {
                Service.UpdateVersion(version);
                GlobalToast.Success("保存成功", $"已更新测试版本: {version.Name}");
            }

            NotifyDataCollectionChanged();
            EnsureValidFormCategory();
            TM.App.Log($"[VersionTestingViewModel] 保存成功: {version.Name}");
        }
        catch (Exception ex)
        {
            GlobalToast.Error("保存失败", ex.Message);
            TM.App.Log($"[VersionTestingViewModel] 保存失败: {ex.Message}");
        }
    }

    private void DeleteCurrent(TreeNodeItem? node = null)
    {
        if (node?.Tag is TestVersionData versionFromNode)
        {
            SelectedVersion = versionFromNode;
        }

        if (SelectedVersion == null)
        {
            GlobalToast.Warning("删除失败", "请先选择要删除的测试版本");
            return;
        }

        try
        {
            Service.DeleteVersion(SelectedVersion.Id);
            GlobalToast.Success("删除成功", $"已删除测试版本: {SelectedVersion.Name}");
            RefreshTreeData();
            NotifyDataCollectionChanged();
            EnsureValidFormCategory();
            ClearForm();
            SelectedVersion = null;
        }
        catch (Exception ex)
        {
            GlobalToast.Error("删除失败", ex.Message);
        }
    }

    private void LoadFormFromVersion(TestVersionData? version)
    {
        if (version == null)
        {
            ClearForm();
            return;
        }

        FormName = version.Name;
        FormCategory = version.Category;
        FormVersionNumber = version.VersionNumber;
        FormDescription = version.Description;
        SetSelectedPromptById(version.PromptId);
        FormTestInput = version.TestInput;
        FormExpectedOutput = version.ExpectedOutput;
        FormTestScenario = version.TestScenario;
        FormActualOutput = version.ActualOutput;
        FormRating = version.Rating;
        FormTestNotes = version.TestNotes;
        FormTestStatus = version.TestStatus;
    }

    private void ClearForm()
    {
        FormName = string.Empty;
        FormVersionNumber = "1.0";
        FormDescription = string.Empty;
        SelectedPrompt = null;
        FormTestInput = string.Empty;
        FormExpectedOutput = string.Empty;
        FormTestScenario = string.Empty;
        FormActualOutput = string.Empty;
        FormRating = 0;
        FormTestNotes = string.Empty;
        FormTestStatus = "未测试";
    }

    private void FillDataFromForm(TestVersionData version)
    {
        version.Name = FormName;
        version.Category = FormCategory;
        version.PromptId = SelectedPrompt?.Id ?? version.PromptId;
        version.VersionNumber = FormVersionNumber;
        version.Description = FormDescription;
        version.TestInput = FormTestInput;
        version.ExpectedOutput = FormExpectedOutput;
        version.TestScenario = FormTestScenario;
        version.ActualOutput = FormActualOutput;
        version.Rating = FormRating;
        version.TestNotes = FormTestNotes;
        version.TestStatus = FormTestStatus;
        version.ModifiedTime = DateTime.Now;
    }

    protected override bool MatchesSearchKeyword(TestVersionData data, string keyword)
    {
        return data.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               data.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               data.VersionNumber.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               data.TestInput.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               data.ExpectedOutput.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               data.TestScenario.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               data.ActualOutput.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               data.TestNotes.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    protected override List<TestVersionData> GetAllDataItems()
    {
        return Service.GetAllVersions();
    }

    protected override string GetDataCategory(TestVersionData data)
    {
        return data.Category;
    }

    protected override TreeNodeItem ConvertToTreeNode(TestVersionData data)
    {
        string statusIcon = data.TestStatus switch
        {
            "通过" => "✅",
            "失败" => "❌",
            "测试中..." => "⏳",
            _ => "🧪"
        };

        return new TreeNodeItem
        {
            Name = $"{statusIcon} {data.Name} (v{data.VersionNumber})",
            Icon = DefaultDataIcon,
            Level = 2,
            Tag = data
        };
    }

    protected override string? GetCurrentCategoryValue()
    {
        return FormCategory;
    }

    protected override void ApplyCategorySelection(string categoryName)
    {
        FormCategory = categoryName;
    }

    protected override int ClearAllDataItems()
    {
        return Service.ClearAllVersions();
    }

    protected override void OnAfterDeleteAll(int deletedCount)
    {
        SelectedVersion = null;
        ClearForm();
        EnsureValidFormCategory();
    }

    private void ReloadPromptCache()
    {
        try
        {
            _promptCache = _promptRepository.GetAllTemplates().ToList();
            TM.App.Log($"[VersionTestingViewModel] 读取提示词缓存成功，共 {_promptCache.Count} 个提示词");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[VersionTestingViewModel] 读取提示词缓存失败: {ex.Message}");
            _promptCache = new List<PromptTemplateData>();
        }
    }

    private void OnPromptTemplatesChanged(object? sender, EventArgs e)
    {
        try
        {
            TM.App.Log("[VersionTestingViewModel] 检测到提示词模板变更，开始同步版本测试视图数据");
            ReloadPromptCache();
            RefreshCategoryOptions();
            EnsureValidFormCategory();
            RefreshComparisonTrees();
        }
        catch (Exception ex)
        {
            TM.App.Log($"[VersionTestingViewModel] 同步提示词模板变更失败: {ex.Message}");
        }
    }

    private void SetSelectedPromptById(string? promptId)
    {
        if (string.IsNullOrWhiteSpace(promptId))
        {
            SelectedPrompt = null;
            return;
        }

        var prompt = _promptCache.FirstOrDefault(p => string.Equals(p.Id, promptId, StringComparison.Ordinal));
        SelectedPrompt = prompt;
    }

    private void ApplyPromptGroupingToTree()
    {
        foreach (var root in TreeData.ToList())
        {
            GroupCategoryNode(root);
        }
    }

    private void GroupCategoryNode(TreeNodeItem node)
    {
        if (node.Tag is PromptCategory category)
        {
            foreach (var child in node.Children.Where(child => child.Tag is PromptCategory).ToList())
            {
                GroupCategoryNode(child);
            }

            var versionNodes = node.Children.Where(child => child.Tag is TestVersionData).ToList();
            foreach (var versionNode in versionNodes)
            {
                node.Children.Remove(versionNode);
            }

            var existingPromptNodes = node.Children.Where(child => child.Tag is PromptTemplateData).ToList();
            foreach (var promptNode in existingPromptNodes)
            {
                node.Children.Remove(promptNode);
            }

            var prompts = _promptCache
                .Where(p => string.Equals(p.Category, category.Name, StringComparison.Ordinal))
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ToList();

            if (prompts.Count > 0)
            {
                foreach (var prompt in prompts)
                {
                    var promptNode = new TreeNodeItem
                    {
                        Name = prompt.Name,
                        Icon = "📝",
                        Level = node.Level + 1,
                        Tag = prompt,
                        ShowChildCount = true
                    };

                    var relatedVersions = versionNodes
                        .Where(v => v.Tag is TestVersionData data && string.Equals(data.PromptId, prompt.Id, StringComparison.Ordinal))
                        .ToList();

                    foreach (var versionNode in relatedVersions)
                    {
                        versionNode.Level = promptNode.Level + 1;
                        promptNode.Children.Add(versionNode);
                        versionNodes.Remove(versionNode);
                    }

                    node.Children.Add(promptNode);
                }
            }

            foreach (var leftover in versionNodes)
            {
                leftover.Level = node.Level + 1;
                node.Children.Add(leftover);
            }
        }
    }

    private void RefreshCategoryOptions()
    {
        try
        {
            var categories = _promptRepository.GetAllCategories().ToList();
            categories = categories
                .OrderBy(c => c.Level)
                .ThenBy(c => c.Order)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .ToList();

            var seen = new HashSet<string>(StringComparer.Ordinal);
            CategoryOptions.Clear();
            foreach (var category in categories)
            {
                if (seen.Add(category.Name))
                {
                    CategoryOptions.Add(category.Name);
                }
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[VersionTestingViewModel] 刷新分类选项失败: {ex.Message}");
        }
    }

    private void EnsureValidFormCategory()
    {
        if (CategoryOptions.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(FormCategory))
            {
                _formCategory = string.Empty;
                OnPropertyChanged(nameof(FormCategory));
                OnCategoryValueChanged(FormCategory);
            }
            return;
        }

        var normalized = AlignSelection(FormCategory, CategoryOptions);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            FormCategory = CategoryOptions[0];
        }
        else if (!string.Equals(FormCategory, normalized, StringComparison.Ordinal))
        {
            FormCategory = normalized;
        }
        else
        {
            OnCategoryValueChanged(FormCategory);
        }
    }

    private void RefreshComparisonTrees()
    {
        try
        {
            var categories = _promptRepository.GetAllCategories().ToList();
            TM.App.Log($"[VersionTestingViewModel] 获取分类数据，共 {categories.Count} 个分类");
            TM.App.Log($"[VersionTestingViewModel] 当前提示词缓存数量: {_promptCache.Count}");

            var categoryTree = BuildPromptCategoryTree(categories, _promptCache);
            TM.App.Log($"[VersionTestingViewModel] 构建树形数据完成，顶级节点数: {categoryTree.Count}");

            CategoryATree.Clear();
            CategoryBTree.Clear();

            foreach (var node in categoryTree)
            {
                CategoryATree.Add(node);
                CategoryBTree.Add(CloneTreeNode(node));
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[VersionTestingViewModel] 刷新对比树形数据失败: {ex.Message}");
            TM.App.Log($"[VersionTestingViewModel] 异常堆栈: {ex.StackTrace}");
        }
    }

    private List<TreeNodeItem> BuildPromptCategoryTree(List<PromptCategory> categories, List<PromptTemplateData> prompts)
    {
        var result = new List<TreeNodeItem>();
        var topLevelCategories = categories
            .Where(c => string.IsNullOrWhiteSpace(c.ParentCategory))
            .OrderBy(c => c.Order)
            .ThenBy(c => c.Name)
            .ToList();

        foreach (var cat in topLevelCategories)
        {
            var node = CreateCategoryNodeWithPrompts(cat, categories, prompts);
            result.Add(node);
        }

        return result;
    }

    private TreeNodeItem CreateCategoryNodeWithPrompts(PromptCategory category, List<PromptCategory> allCategories, List<PromptTemplateData> prompts)
    {
        var node = new TreeNodeItem
        {
            Name = category.Name,
            Icon = category.Icon,
            Level = category.Level,
            Tag = category,
            ShowChildCount = true
        };

        var children = allCategories
            .Where(c => string.Equals(c.ParentCategory, category.Name, StringComparison.Ordinal))
            .OrderBy(c => c.Order)
            .ThenBy(c => c.Name)
            .ToList();

        foreach (var child in children)
        {
            var childNode = CreateCategoryNodeWithPrompts(child, allCategories, prompts);
            node.Children.Add(childNode);
        }

        var categoryPrompts = prompts
            .Where(p => string.Equals(p.Category, category.Name, StringComparison.Ordinal))
            .OrderBy(p => p.Name)
            .ToList();

        foreach (var prompt in categoryPrompts)
        {
            var promptNode = new TreeNodeItem
            {
                Name = prompt.Name,
                Icon = "📝",
                Level = category.Level + 1,
                Tag = prompt,
                ShowChildCount = false
            };
            node.Children.Add(promptNode);
        }

        return node;
    }

    private TreeNodeItem CloneTreeNode(TreeNodeItem source)
    {
        var clone = new TreeNodeItem
        {
            Name = source.Name,
            Icon = source.Icon,
            Level = source.Level,
            Tag = source.Tag,
            ShowChildCount = source.ShowChildCount
        };

        foreach (var child in source.Children)
        {
            clone.Children.Add(CloneTreeNode(child));
        }

        return clone;
    }

    private void HandleCategoryASelected(TreeNodeItem? node)
    {
        if (node?.Tag is PromptTemplateData prompt)
        {
            SelectedPromptA = prompt;
            IsCategoryADropdownOpen = false;

            string categoryPath = BuildCategoryPath(prompt.Category);
            SelectedCategoryAPath = string.IsNullOrWhiteSpace(categoryPath)
                ? prompt.Name
                : $"{categoryPath} > {prompt.Name}";
            SelectedCategoryAIcon = "📝";

            TM.App.Log($"[VersionTestingViewModel] 分类A选择提示词: {prompt.Name}, 路径: {SelectedCategoryAPath}");
        }
    }

    private void HandleCategoryBSelected(TreeNodeItem? node)
    {
        if (node?.Tag is PromptTemplateData prompt)
        {
            SelectedPromptB = prompt;
            IsCategoryBDropdownOpen = false;

            string categoryPath = BuildCategoryPath(prompt.Category);
            SelectedCategoryBPath = string.IsNullOrWhiteSpace(categoryPath)
                ? prompt.Name
                : $"{categoryPath} > {prompt.Name}";
            SelectedCategoryBIcon = "📝";

            TM.App.Log($"[VersionTestingViewModel] 分类B选择提示词: {prompt.Name}, 路径: {SelectedCategoryBPath}");
        }
    }

    private void UpdatePromptADescription()
    {
        if (SelectedPromptA != null)
        {
            PromptADescription = string.IsNullOrWhiteSpace(SelectedPromptA.Tags)
                ? "暂无标签"
                : SelectedPromptA.Tags;
        }
        else
        {
            PromptADescription = string.Empty;
        }
    }

    private void UpdatePromptBDescription()
    {
        if (SelectedPromptB != null)
        {
            PromptBDescription = string.IsNullOrWhiteSpace(SelectedPromptB.Tags)
                ? "暂无标签"
                : SelectedPromptB.Tags;
        }
        else
        {
            PromptBDescription = string.Empty;
        }
    }

    private string BuildCategoryPath(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
            return string.Empty;

        var categories = _promptRepository.GetAllCategories();
        var category = categories.FirstOrDefault(c => string.Equals(c.Name, categoryName, StringComparison.Ordinal));

        if (category == null)
            return categoryName;

        var path = new List<string>();
        var current = category;

        while (current != null)
        {
            path.Insert(0, current.Name);

            if (string.IsNullOrWhiteSpace(current.ParentCategory))
                break;

            current = categories.FirstOrDefault(c => string.Equals(c.Name, current.ParentCategory, StringComparison.Ordinal));
        }

        return string.Join(" > ", path);
    }

    private void ApplyTemplate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
            return;

        var templates = new Dictionary<string, string>
        {
            ["修仙小说"] = "请根据以下要求构建一部修仙小说：\n1. 主角从普通人开始修炼\n2. 包含完整的修炼体系设定\n3. 情节跌宕起伏，节奏紧凑\n4. 字数要求：3500字左右",
            ["都市言情"] = "请根据以下要求创作都市言情小说：\n1. 现代都市背景\n2. 男女主角相遇相识过程\n3. 情感细腻，描写真实\n4. 字数要求：2000字左右",
            ["科幻冒险"] = "请根据以下要求创作科幻冒险故事：\n1. 未来科技背景设定\n2. 主角团队探索未知星域\n3. 科技元素与冒险情节结合\n4. 字数要求：3500字左右",
            ["历史架空"] = "请根据以下要求创作历史架空小说：\n1. 架空历史朝代背景\n2. 主角穿越或重生设定\n3. 历史细节合理，情节引人入胜\n4. 字数要求：3500字左右"
        };

        if (templates.TryGetValue(template, out var content))
        {
            TestInput = content;
            TM.App.Log($"[VersionTestingViewModel] 应用模板: {template}");
        }
    }

    private void UpdateTotalScore()
    {
        TotalScore = (CreativityScore + CoherenceScore + LogicScore + EmotionScore) / 4.0;
    }

    private void UpdateOutputAStats()
    {
        OutputAWordCount = string.IsNullOrWhiteSpace(OutputAContent)
            ? "0"
            : OutputAContent.Length.ToString();
    }

    private void UpdateOutputBStats()
    {
        OutputBWordCount = string.IsNullOrWhiteSpace(OutputBContent)
            ? "0"
            : OutputBContent.Length.ToString();
    }
}
