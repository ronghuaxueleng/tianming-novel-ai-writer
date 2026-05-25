using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Controls.Dialogs;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Helpers.AI;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.Common.ViewModels;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Models;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Services;
using TM.Services.Framework.AI.Interfaces.AI;

namespace TM.Modules.AIAssistant.PromptTools.PromptManagement;

[Obfuscation(Exclude = true, ApplyToMembers = true)]
public class PromptManagementViewModel : DataManagementViewModelBase<PromptTemplateData, PromptCategory, PromptService>, IDisposable
{
    private static readonly HashSet<string> UnifiedMutexCategories = new(StringComparer.Ordinal)
    {
        "拆书分析师",
        "素材设计师",
        "小说设计师",
        "小说创作者",
        "业务提示词"
    };

    private static bool IsValidateTemplateCategory(string? categoryName, PromptService service)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return false;
        }

        var templates = service.GetTemplatesByCategory(categoryName);
        return templates.Any(t => t.IsBuiltIn && t.Id.StartsWith("tpl-validate-", StringComparison.Ordinal));
    }

    private bool IsAutoFallbackCategory(string? categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return false;
        }

        if (IsUnifiedMutexCategory(categoryName))
        {
            return true;
        }

        return IsValidateTemplateCategory(categoryName, Service);
    }

    private void EnsureBuiltInDefaultEnabledIfNone(string categoryName)
    {
        if (!IsAutoFallbackCategory(categoryName))
        {
            return;
        }

        var templates = Service.GetTemplatesByCategory(categoryName).ToList();
        if (templates.Count == 0)
        {
            return;
        }

        if (templates.Any(t => t.IsEnabled))
        {
            return;
        }

        var selected = templates
            .Where(t => t.IsBuiltIn)
            .OrderByDescending(t => t.IsDefault)
            .FirstOrDefault()
            ?? templates.OrderByDescending(t => t.IsDefault).FirstOrDefault()
            ?? templates.FirstOrDefault();

        if (selected == null)
        {
            return;
        }

        if (IsUnifiedMutexCategory(categoryName))
        {
            EnforceUnifiedCategoryMutex(categoryName, selected.Id);
        }
        else
        {
            selected.IsEnabled = true;
        }

        Service.UpdateData(selected);
    }

    private string _formName = string.Empty;
    public string FormName
    {
        get => _formName;
        set { _formName = value; OnPropertyChanged(); }
    }

    private string _formIcon = "📝";
    public string FormIcon
    {
        get => _formIcon;
        set { _formIcon = value; OnPropertyChanged(); }
    }

    private string _formStatus = "未启用";
    public string FormStatus
    {
        get => _formStatus;
        set { _formStatus = value; OnPropertyChanged(); }
    }

    private string _formCategory = string.Empty;
    private bool _suppressCategoryValueChanged;
    public string FormCategory
    {
        get => _formCategory;
        set
        {
            if (_formCategory != value)
            {
                _formCategory = value;
                OnPropertyChanged();

                if (!_suppressCategoryValueChanged)
                {
                    OnCategoryValueChanged(_formCategory);
                }
            }
        }
    }

    private string _formSystemPrompt = string.Empty;
    public string FormSystemPrompt
    {
        get => _formSystemPrompt;
        set { _formSystemPrompt = value; OnPropertyChanged(); }
    }

    private string _formUserTemplate = string.Empty;
    public string FormUserTemplate
    {
        get => _formUserTemplate;
        set { _formUserTemplate = value; OnPropertyChanged(); }
    }

    private string _formVariables = string.Empty;
    public string FormVariables
    {
        get => _formVariables;
        set { _formVariables = value; OnPropertyChanged(); }
    }

    private string _formTags = string.Empty;
    public string FormTags
    {
        get => _formTags;
        set { _formTags = value; OnPropertyChanged(); }
    }

    private string _formDescription = string.Empty;
    public string FormDescription
    {
        get => _formDescription;
        set { _formDescription = value; OnPropertyChanged(); }
    }

    private bool _formIsBuiltIn = false;
    public bool FormIsBuiltIn
    {
        get => _formIsBuiltIn;
        set { _formIsBuiltIn = value; OnPropertyChanged(); }
    }

    private bool _formIsDefault = false;
    public bool FormIsDefault
    {
        get => _formIsDefault;
        set { _formIsDefault = value; OnPropertyChanged(); }
    }

    private readonly IPromptGenerationService _promptService;
    private readonly IAITextGenerationService _aiTextGenerationService;

    private bool _isSubscribedTemplatesChanged;

    public ICommand SelectNodeCommand { get; }
    public new ICommand TreeAfterActionCommand { get; }

    protected override string NewItemTypeName => "模板";

    private ICommand? _addCommand;
    public ICommand AddCommand => _addCommand ??= new RelayCommand(_ =>
    {
        try
        {
            _currentEditingData = null;
            _currentEditingCategory = null;
            ResetForm();
            ExecuteAddWithCreateMode();
        }
        catch (Exception ex)
        {
            TM.App.Log($"[PromptManagement] 新建失败: {ex.Message}");
            GlobalToast.Error("新建失败", ex.Message);
        }
    });

    private ICommand? _saveCommand;
    public ICommand SaveCommand => _saveCommand ??= new RelayCommand(_ =>
    {
        try
        {
            ExecuteSaveWithCreateEditMode(
                validateForm: ValidateFormCore,
                createCategoryCore: CreateCategoryCore,
                createDataCore: CreateDataCore,
                hasEditingCategory: () => _currentEditingCategory != null,
                hasEditingData: () => _currentEditingData != null,
                updateCategoryCore: UpdateCategoryCore,
                updateDataCore: UpdateDataCore);
        }
        catch (Exception ex)
        {
            TM.App.Log($"[PromptManagement] 保存失败: {ex.Message}");
            GlobalToast.Error("保存失败", ex.Message);
        }
    });

    private ICommand? _deleteCommand;
    public ICommand DeleteCommand => _deleteCommand ??= new RelayCommand(_ =>
    {
        try
        {
            if (_currentEditingCategory != null)
            {
                var allCategoriesToDelete = new List<string>();
                CollectCategoryAndChildren(_currentEditingCategory.Name, allCategoriesToDelete);

                if (ContainsProtectedCategories(allCategoriesToDelete))
                {
                    GlobalToast.Warning("禁止删除", "1-3级分类不可删除（含联动删除）。");
                    return;
                }

                if (ContainsBuiltInTemplates(allCategoriesToDelete, out var builtInCount))
                {
                    GlobalToast.Warning("禁止删除", $"该分类（含子分类）下包含 {builtInCount} 个内置模板，禁止删除（避免误删JSON业务提示词）。");
                    return;
                }

                var result = StandardDialog.ShowConfirm(
                    $"确定要删除分类「{_currentEditingCategory.Name}」吗？\n\n注意：该分类及其{allCategoriesToDelete.Count - 1}个子分类下的所有提示词模板也会被删除！",
                    "确认删除"
                );
                if (!result) return;

                int totalDataDeleted = 0;

                foreach (var categoryName in allCategoriesToDelete)
                {
                    var dataInCategory = Service.GetAllData()
                        .Where(d => d.Category == categoryName)
                        .ToList();

                    foreach (var data in dataInCategory)
                    {
                        if (data.IsBuiltIn)
                        {
                            continue;
                        }
                        Service.DeleteData(data.Id);
                        totalDataDeleted++;
                    }

                    Service.DeleteCategory(categoryName);
                }

                GlobalToast.Success("删除成功", 
                    $"已删除 {allCategoriesToDelete.Count} 个分类及其 {totalDataDeleted} 个提示词模板");

                _currentEditingCategory = null;
                ResetForm();
                RefreshTreeData();
            }
            else if (_currentEditingData != null)
            {
                if (IsBuiltInTemplate(_currentEditingData))
                {
                    GlobalToast.Warning("禁止删除", "内置模板不可删除。");
                    return;
                }

                var result = StandardDialog.ShowConfirm($"确定要删除提示词模板「{_currentEditingData.Name}」吗？", "确认删除");
                if (!result) return;

                Service.DeleteData(_currentEditingData.Id);
                GlobalToast.Success("删除成功", $"提示词模板「{_currentEditingData.Name}」已删除");

                var deletedCategory = _currentEditingData.Category;

                _currentEditingData = null;
                ResetForm();
                RefreshTreeData();
                EnsureBuiltInDefaultEnabledIfNone(deletedCategory);
            }
            else
            {
                GlobalToast.Warning("删除失败", "请先选择要删除的分类或提示词模板");
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[PromptManagement] 删除失败: {ex.Message}");
            GlobalToast.Error("删除失败", ex.Message);
        }
    });

    public PromptManagementViewModel(IPromptGenerationService promptService, IAITextGenerationService aiTextGenerationService)
    {
        _promptService = promptService;
        _aiTextGenerationService = aiTextGenerationService;

        SelectNodeCommand = new RelayCommand(param => OnNodeDoubleClick(param as TreeNodeItem));
        TreeAfterActionCommand = new RelayCommand(_ => { });

        TrySubscribeTemplatesChanged();
    }

    private void TrySubscribeTemplatesChanged()
    {
        if (_isSubscribedTemplatesChanged)
        {
            return;
        }

        try
        {
            PromptService.TemplatesChanged += OnPromptTemplatesChanged;
            _isSubscribedTemplatesChanged = true;
        }
        catch (Exception ex)
        {
            TM.App.Log($"[PromptManagement] 订阅 PromptService.TemplatesChanged 失败: {ex.Message}");
        }
    }

    private void OnPromptTemplatesChanged(object? sender, EventArgs e)
    {
        try
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                RefreshTreeData();
            });
        }
        catch (Exception ex)
        {
            TM.App.Log($"[PromptManagement] 刷新模板树失败: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_isSubscribedTemplatesChanged)
        {
            try
            {
                PromptService.TemplatesChanged -= OnPromptTemplatesChanged;
            }
            catch { }
            _isSubscribedTemplatesChanged = false;
        }
    }

    protected override void UpdateAIGenerateButtonState(bool hasSelection = false)
    {
        IsAIGenerateEnabled = hasSelection && _currentEditingData != null;
    }

    protected override bool CanExecuteAIGenerate() => _currentEditingData != null;

    private bool ContainsProtectedCategories(IEnumerable<string> categoryNames)
    {
        if (categoryNames == null) return false;

        var categories = Service.GetAllCategories();
        var lookup = categories
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var name in categoryNames)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (lookup.TryGetValue(name, out var category) && IsProtectedCategory(category))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsProtectedCategory(PromptCategory category)
    {
        if (category.IsBuiltIn && category.Level <= 3)
        {
            return true;
        }

        return false;
    }

    private bool ContainsBuiltInTemplates(IEnumerable<string> categoryNames, out int builtInCount)
    {
        builtInCount = 0;
        if (categoryNames == null) return false;

        var set = new HashSet<string>(categoryNames.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.Ordinal);
        if (set.Count == 0) return false;

        builtInCount = Service.GetAllData().Count(d => IsBuiltInTemplate(d) && set.Contains(d.Category));
        return builtInCount > 0;
    }

    private static bool IsBuiltInTemplate(PromptTemplateData data)
    {
        return data.IsBuiltIn;
    }

    protected override async System.Threading.Tasks.Task ExecuteAIGenerateAsync()
    {
        var skChat = ServiceLocator.Get<TM.Services.Framework.AI.SemanticKernel.SKChatService>();
        if (skChat.IsMainConversationGenerating)
        {
            var confirmed = StandardDialog.ShowConfirm(
                "主界面对话正在生成，继续需要中断主界面对话，是否继续？",
                "互斥提醒");
            if (!confirmed)
                return;
            skChat.CancelCurrentRequest();
        }

        try
        {
            var metadata = TM.Framework.Common.Helpers.AI.ModuleMetadataRegistry.GetMetadata(FormCategory);

            var (rootCategory, subCategory) = ResolvePromptCategories(FormCategory);

            var context = new PromptGenerationContext
            {
                PromptRootCategory = rootCategory,
                PromptSubCategory = subCategory,
                ModuleKey = $"PromptTools.{FormCategory}",
                ModuleDisplayName = FormCategory ?? "提示词管理",
                TemplateName = FormName,
                ExtraRequirement = FormDescription,
                FieldNames = new[] { "Prompt正文", "说明" },
                OutputFieldNames = metadata?.OutputFields,
                InputVariableNames = metadata?.InputVariables,
                ModuleType = metadata?.ModuleType,
                Description = metadata?.Description
            };

            var result = await _promptService.GenerateModulePromptAsync(context);
            if (!result.Success || string.IsNullOrWhiteSpace(result.Content))
            {
                var message = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "AI未返回有效的提示词内容"
                    : result.ErrorMessage;
                GlobalToast.Error("生成失败", message);
                return;
            }

            FormSystemPrompt = result.Content;

            if (metadata?.InputVariables != null && metadata.InputVariables.Length > 0)
            {
                FormVariables = string.Join(",", metadata.InputVariables);
            }

            if (!string.IsNullOrWhiteSpace(result.Description))
            {
                FormDescription = result.Description;
            }
            else if (!string.IsNullOrWhiteSpace(metadata?.Description))
            {
                FormDescription = metadata.Description;
            }
            else
            {
                var fieldsToGenerate = new Dictionary<string, string>
                {
                    { "说明", "用一句话（不超过50字）描述该模板的用途和适用场景" }
                };

                var generatedFields = await GenerateFieldsAsync(result.Content, FormName, fieldsToGenerate);

                if (generatedFields.TryGetValue("说明", out var desc))
                {
                    FormDescription = desc;
                }
            }

            var metaInfo = metadata != null ? "（已应用业务元数据）" : "";
            GlobalToast.Success("生成完成", $"已为当前模板生成Prompt正文{metaInfo}");
        }
        catch (System.Exception ex)
        {
            TM.App.Log($"[PromptManagement] AI生成提示词失败: {ex.Message}");
            GlobalToast.Error("生成失败", ex.Message);
        }
    }

    private (string? RootCategory, string? SubCategory) ResolvePromptCategories(string? categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
            return (null, null);

        var categories = Service.GetAllCategories();
        var lookup = categories
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        if (!lookup.TryGetValue(categoryName, out var current))
            return (null, categoryName);

        var sub = current.Level == 1 ? null : current.Name;
        var node = current;
        while (!string.IsNullOrWhiteSpace(node.ParentCategory) && lookup.TryGetValue(node.ParentCategory, out var parent))
        {
            node = parent;
        }

        return (node.Name, sub ?? categoryName);
    }

    private async System.Threading.Tasks.Task<Dictionary<string, string>> GenerateFieldsAsync(
        string generatedContent, 
        string? templateName, 
        Dictionary<string, string> fieldDescriptions)
    {
        var result = new Dictionary<string, string>();
        if (fieldDescriptions == null || fieldDescriptions.Count == 0)
            return result;

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("根据以下已生成的内容，补全相关字段。");
            sb.AppendLine();
            sb.AppendLine($"<template_name>{templateName ?? "未命名"}</template_name>");
            sb.AppendLine();
            sb.AppendLine("<generated_content>");
            sb.AppendLine(generatedContent);
            sb.AppendLine("</generated_content>");
            sb.AppendLine();
            sb.AppendLine("<fields_to_fill>");
            foreach (var kv in fieldDescriptions)
            {
                sb.AppendLine($"- {kv.Key}：{kv.Value}");
            }
            sb.AppendLine("</fields_to_fill>");
            sb.AppendLine();
            sb.AppendLine("<output_rules>");
            sb.AppendLine("请以JSON格式输出，只包含字段名和对应的值，不要任何额外解释。示例：");
            sb.AppendLine("{");
            foreach (var kv in fieldDescriptions)
            {
                sb.AppendLine($"  \"{kv.Key}\": \"生成的内容\",");
            }
            sb.AppendLine("}");
            sb.AppendLine("</output_rules>");

            var aiResult = await _aiTextGenerationService.GenerateAsync(sb.ToString());
            if (aiResult.Success && !string.IsNullOrWhiteSpace(aiResult.Content))
            {
                var json = aiResult.Content.Trim();
                var jsonStart = json.IndexOf('{');
                var jsonEnd = json.LastIndexOf('}');
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    json = json.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    foreach (var kv in fieldDescriptions)
                    {
                        if (doc.RootElement.TryGetProperty(kv.Key, out var prop) &&
                            prop.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            var val = prop.GetString();
                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                result[kv.Key] = val;
                            }
                        }
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            TM.App.Log($"[PromptManagement] 补全字段失败: {ex.Message}");
        }
        return result;
    }

    protected override string DefaultDataIcon => "📝";

    protected override PromptTemplateData? CreateNewData(string? categoryName = null)
    {
        return new PromptTemplateData
        {
            Id = ShortIdGenerator.New("D"),
            Category = categoryName ?? string.Empty,
            Icon = DefaultDataIcon,
            IsEnabled = false,
            IsBuiltIn = false,
            CreatedTime = DateTime.Now,
            ModifiedTime = DateTime.Now
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
        var deletable = Service.GetAllTemplates()
            .Where(t => !IsBuiltInTemplate(t))
            .ToList();

        foreach (var t in deletable)
        {
            Service.DeleteData(t.Id);
        }

        return deletable.Count;
    }

    protected override List<PromptCategory> GetAllCategoriesFromService()
    {
        return Service.GetAllCategories();
    }

    protected override List<PromptTemplateData> GetAllDataItems()
    {
        return Service.GetAllTemplates().ToList();
    }

    protected override string GetDataCategory(PromptTemplateData data)
    {
        return data.Category;
    }

    protected override TreeNodeItem ConvertToTreeNode(PromptTemplateData data)
    {
        return new TreeNodeItem
        {
            Name = data.Name,
            Icon = data.Icon,
            Tag = data,
            ShowChildCount = false
        };
    }

    protected override bool MatchesSearchKeyword(PromptTemplateData data, string keyword)
    {
        return data.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               data.SystemPrompt.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               data.UserTemplate.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               data.Tags.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private bool ValidateFormCore()
    {
        if (string.IsNullOrWhiteSpace(FormName))
        {
            GlobalToast.Warning("保存失败", "请输入名称");
            return false;
        }

        if (!IsCreateMode && _currentEditingCategory == null && _currentEditingData == null)
        {
            GlobalToast.Warning("保存失败", "请先新建，或在左侧选择要编辑的分类或提示词模板");
            return false;
        }

        return true;
    }

    private void CreateCategoryCore()
    {
        var parentCategoryName = "";
        var level = 1;

        if (!string.IsNullOrWhiteSpace(FormCategory))
        {
            parentCategoryName = FormCategory;
            var parentCategory = Service.GetAllCategories().FirstOrDefault(c => c.Name == parentCategoryName);
            level = parentCategory != null ? parentCategory.Level + 1 : 1;
        }

        var categoryIcon = GetCategoryIconForSave(FormIcon);

        var newCategory = new PromptCategory
        {
            Id = ShortIdGenerator.New("C"),
            Name = FormName,
            Icon = categoryIcon,
            ParentCategory = parentCategoryName,
            Level = level,
            Order = Service.GetAllCategories().Count + 1,
            IsBuiltIn = false
        };

        if (!Service.AddCategory(newCategory))
        {
            GlobalToast.Warning("创建失败", "分类名已存在，请改名");
            return;
        }

        string levelDesc = level == 1 ? "一级分类" : $"{level}级分类";
        GlobalToast.Success("保存成功", $"{levelDesc}「{newCategory.Name}」已创建");

        _currentEditingCategory = null;
        _currentEditingData = null;
        ResetForm();
        RefreshTreeData();
    }

    private void CreateDataCore()
    {
        if (string.IsNullOrWhiteSpace(FormCategory))
        {
            GlobalToast.Warning("保存失败", "请选择所属分类");
            return;
        }

        var newData = CreateNewData(FormCategory);
        if (newData == null) return;

        UpdateDataFromForm(newData);
        if (newData.IsEnabled)
        {
            EnforceUnifiedCategoryMutex(newData.Category, newData.Id);
        }
        Service.AddData(newData);
        _currentEditingData = newData;
        GlobalToast.Success("保存成功", $"提示词模板「{newData.Name}」已创建");
        RefreshTreeData();
        FocusOnDataItem(newData);
    }

    private void UpdateCategoryCore()
    {
        if (_currentEditingCategory == null)
            return;

        var oldName = _currentEditingCategory.Name;
        _currentEditingCategory.Name = FormName;
        _currentEditingCategory.Icon = GetCategoryIconForSave(FormIcon);
        if (!Service.UpdateCategory(_currentEditingCategory))
        {
            _currentEditingCategory.Name = oldName;
            GlobalToast.Warning("保存失败", "分类名已存在，请改名");
            return;
        }
        GlobalToast.Success("保存成功", $"分类「{_currentEditingCategory.Name}」已更新");
    }

    private void UpdateDataCore()
    {
        if (_currentEditingData == null)
            return;

        if (IsBuiltInTemplate(_currentEditingData))
        {
            GlobalToast.Warning("禁止修改", "内置模板不可修改。");
            return;
        }

        UpdateDataFromForm(_currentEditingData);
        if (_currentEditingData.IsEnabled)
        {
            EnforceUnifiedCategoryMutex(_currentEditingData.Category, _currentEditingData.Id);
        }
        Service.UpdateData(_currentEditingData);
        GlobalToast.Success("保存成功", $"提示词模板「{_currentEditingData.Name}」已更新");
    }

    protected override void OnDataEnabledChanged(PromptTemplateData data, bool isEnabled)
    {
        base.OnDataEnabledChanged(data, isEnabled);

        if (!isEnabled)
        {
            if (IsAutoFallbackCategory(data.Category))
            {
                EnsureBuiltInDefaultEnabledIfNone(data.Category);
            }
            return;
        }

        if (!IsUnifiedMutexCategory(data.Category))
        {
            return;
        }

        EnforceUnifiedCategoryMutex(data.Category, data.Id);
        Service.UpdateData(data);

        if (_currentEditingData?.Id == data.Id)
        {
            FormStatus = "已启用";
        }
    }

    protected override void ExecuteBulkToggle()
    {
        try
        {
            var serviceBase = Service as TM.Framework.Common.Services.ModuleServiceBase<PromptCategory, PromptTemplateData>;
            if (serviceBase == null) return;

            var selectedRoot = TryGetBulkToggleRootCategory();

            List<string> names;
            bool allEnabled;

            if (selectedRoot != null && selectedRoot.Level == 1)
            {
                names = CollectCategoryAndChildrenNames(selectedRoot.Name);
                if (names.Count == 0) { GlobalToast.Warning("提示", "未找到可操作的分类"); return; }
                allEnabled = IsAllEnabledInCategories(names);
            }
            else
            {
                var categories = Service.GetAllCategories();
                names = categories.Select(c => c.Name).ToList();
                if (names.Count == 0) { GlobalToast.Warning("提示", "暂无分类数据"); return; }
                allEnabled = IsAllEnabledInCategories(names);
            }

            var newEnabled = !allEnabled;

            if (newEnabled && !CheckBulkEnableScopeWarning(names))
            {
                return;
            }

            var updatedCategories = serviceBase.SetCategoriesEnabled(names, newEnabled);
            var updatedData = serviceBase.SetDataEnabledByCategories(names, newEnabled);

            if (newEnabled)
            {
                ApplyUnifiedBuiltInDefaults(names);
            }
            else
            {
                foreach (var category in names)
                {
                    EnsureBuiltInDefaultEnabledIfNone(category);
                }
            }

            RefreshTreeAndCategorySelection();
            UpdateBulkToggleState();

            GlobalToast.Success(newEnabled ? "已启用" : "已禁用", $"分类:{updatedCategories}，条目:{updatedData}");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[PromptManagement] 一键启用/禁用失败: {ex.Message}");
            GlobalToast.Error("操作失败", ex.Message);
        }
    }

    private static bool IsUnifiedMutexCategory(string? categoryName)
        => !string.IsNullOrWhiteSpace(categoryName) && UnifiedMutexCategories.Contains(categoryName);

    private void EnforceUnifiedCategoryMutex(string categoryName, string enabledTemplateId)
    {
        if (!IsUnifiedMutexCategory(categoryName))
        {
            return;
        }

        foreach (var t in Service.GetTemplatesByCategory(categoryName))
        {
            t.IsEnabled = string.Equals(t.Id, enabledTemplateId, StringComparison.Ordinal);
        }
    }

    private void ApplyUnifiedBuiltInDefaults(IEnumerable<string> categoryNames)
    {
        var set = new HashSet<string>(categoryNames.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.Ordinal);

        foreach (var category in UnifiedMutexCategories)
        {
            if (!set.Contains(category))
            {
                continue;
            }

            var templates = Service.GetTemplatesByCategory(category).ToList();
            if (templates.Count == 0)
            {
                continue;
            }

            var selected = templates
                .Where(t => t.IsBuiltIn)
                .OrderByDescending(t => t.IsDefault)
                .FirstOrDefault()
                ?? templates.OrderByDescending(t => t.IsDefault).FirstOrDefault()
                ?? templates.FirstOrDefault();

            if (selected == null)
            {
                continue;
            }

            EnforceUnifiedCategoryMutex(category, selected.Id);
            Service.UpdateData(selected);
        }
    }

    private bool IsAllEnabledInCategories(List<string> categoryNames)
    {
        var set = new HashSet<string>(categoryNames.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.Ordinal);

        var categories = Service.GetAllCategories();
        var data = GetAllDataItems();

        var allCategoriesEnabled = categories.Where(c => set.Contains(c.Name)).All(c => c.IsEnabled);
        var allDataEnabled = data.Where(d => set.Contains(GetDataCategory(d))).All(d => GetDataIsEnabled(d));

        return allCategoriesEnabled && allDataEnabled;
    }

    private PromptCategory? TryGetBulkToggleRootCategory()
    {
        return GetBulkToggleCurrentCategory();
    }

    private void CollectCategoryAndChildren(string categoryName, List<string> result)
    {
        result.Add(categoryName);

        var childCategories = Service.GetAllCategories()
            .Where(c => c.ParentCategory == categoryName)
            .ToList();

        foreach (var child in childCategories)
        {
            CollectCategoryAndChildren(child.Name, result);
        }
    }

    private new void OnNodeDoubleClick(TreeNodeItem? node)
    {
        if (node == null) return;

        try
        {
            if (node.Tag is PromptCategory category)
            {
                _currentEditingCategory = category;
                _currentEditingData = null;
                LoadCategoryToForm(category);
                EnterEditMode();
            }
            else if (node.Tag is PromptTemplateData data)
            {
                _currentEditingData = data;
                _currentEditingCategory = null;
                LoadDataToForm(data);
                EnterEditMode();
                OnDataItemLoaded();
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[PromptManagement] 加载节点失败: {ex.Message}");
            GlobalToast.Error("加载失败", ex.Message);
        }
    }

    private void LoadCategoryToForm(PromptCategory category)
    {
        FormName = category.Name;
        FormIcon = category.Icon;
        FormStatus = "分类";

        _suppressCategoryValueChanged = true;
        try
        {
            FormCategory = category.ParentCategory ?? string.Empty;
        }
        finally
        {
            _suppressCategoryValueChanged = false;
        }

        SyncCategorySelectionDisplay(FormCategory);

        FormSystemPrompt = string.Empty;
        FormUserTemplate = string.Empty;
        FormVariables = string.Empty;
        FormTags = string.Empty;
        FormDescription = string.Empty;
        FormIsBuiltIn = false;
        FormIsDefault = false;
    }

    private void LoadDataToForm(PromptTemplateData data)
    {
        FormName = data.Name;
        FormIcon = data.Icon;
        FormStatus = data.IsEnabled ? "已启用" : "已禁用";

        _suppressCategoryValueChanged = true;
        try
        {
            FormCategory = data.Category;
        }
        finally
        {
            _suppressCategoryValueChanged = false;
        }

        SyncCategorySelectionDisplay(FormCategory);

        FormSystemPrompt = data.SystemPrompt;
        FormUserTemplate = data.UserTemplate;
        FormVariables = data.Variables;
        FormTags = data.Tags;
        FormDescription = data.Description;
        FormIsBuiltIn = data.IsBuiltIn;
        FormIsDefault = data.IsDefault;
    }

    private void SyncCategorySelectionDisplay(string? categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            SelectedCategoryTreePath = "主页导航";
            SelectedCategoryTreeIcon = "🏠";
            return;
        }

        var category = Service.GetAllCategories().FirstOrDefault(c => string.Equals(c.Name, categoryName, StringComparison.Ordinal));
        SelectedCategoryTreeIcon = string.IsNullOrWhiteSpace(category?.Icon) ? "📁" : category!.Icon;

        var chain = BuildCategoryChain(categoryName);
        SelectedCategoryTreePath = chain.Count == 0
            ? $"主页导航 > {categoryName}"
            : $"主页导航 > {string.Join(" > ", chain)}";
    }

    private List<string> BuildCategoryChain(string categoryName)
    {
        var categories = Service.GetAllCategories();
        var lookup = categories
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var result = new List<string>();
        var current = categoryName;

        while (!string.IsNullOrWhiteSpace(current) && lookup.TryGetValue(current, out var cat))
        {
            result.Add(cat.Name);
            current = cat.ParentCategory ?? string.Empty;
        }

        result.Reverse();
        return result;
    }

    private void UpdateDataFromForm(PromptTemplateData data)
    {
        data.Name = FormName;
        data.Icon = GetDataIconForSave(FormIcon);
        data.Category = FormCategory;
        data.IsEnabled = FormStatus == "已启用";
        data.ModifiedTime = DateTime.Now;

        data.SystemPrompt = FormSystemPrompt;
        data.UserTemplate = FormUserTemplate;
        data.Variables = FormVariables;
        data.Tags = FormTags;
        data.Description = FormDescription;
        data.IsDefault = FormIsDefault;
    }

    private void ResetForm()
    {
        FormName = string.Empty;
        FormIcon = "📝";
        FormStatus = "已启用";
        FormCategory = string.Empty;

        FormSystemPrompt = string.Empty;
        FormUserTemplate = string.Empty;
        FormVariables = string.Empty;
        FormTags = string.Empty;
        FormDescription = string.Empty;
        FormIsBuiltIn = false;
        FormIsDefault = false;
    }
}
