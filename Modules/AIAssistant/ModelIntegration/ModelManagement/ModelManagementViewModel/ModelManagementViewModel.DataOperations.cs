using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Helpers.Id;
using TM.Modules.AIAssistant.ModelIntegration.ModelManagement.Models;

namespace TM.Modules.AIAssistant.ModelIntegration.ModelManagement;

public partial class ModelManagementViewModel
{

    protected override string DefaultDataIcon => "Icon.Robot";

    protected override UserConfigurationData? CreateNewData(string? categoryName = null)
    {
        return new UserConfigurationData
        {
            Id = ShortIdGenerator.New("D"),
            Name = "新模型配置",
            Icon = "Icon.Robot",
            Category = categoryName ?? "",
            IsEnabled = false,
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
        var count = Service.ClearAllConfigurations();
        try
        {
            foreach (var cfg in _aiConfigurationService.GetAllConfigurations().ToList())
            {
                _aiConfigurationService.DeleteConfiguration(cfg.Id);
            }
        }
        catch (Exception ex)
        {
            LogScoped($"[ModelManagement] 全部删除同步清理对话配置失败: {ex.Message}");
        }
        return count;
    }

    protected override void OnAfterDeleteAll(int deletedCount)
    {
        base.OnAfterDeleteAll(deletedCount);
        _ = Task.Run(async () =>
        {
            try
            {
                await Service.SyncProvidersFromCategoriesAsync().ConfigureAwait(false);
                await _aiLibraryService.ReloadLibraryAsync().ConfigureAwait(false);

                try
                {
                    var validIds = new HashSet<string>(
                        _aiLibraryService.GetAllProviders().Select(p => p.Id),
                        StringComparer.OrdinalIgnoreCase);
                    var liveDataKeys = new HashSet<string>(
                        Service.GetAllData()
                            .Where(d => !string.IsNullOrWhiteSpace(d.CategoryId)
                                     && !string.IsNullOrWhiteSpace(d.ModelName))
                            .Select(d => $"{d.CategoryId}::{d.ModelName}"),
                        StringComparer.OrdinalIgnoreCase);
                    foreach (var orphan in _aiConfigurationService.GetAllConfigurations()
                        .Where(c => !validIds.Contains(c.ProviderId)
                                 || !liveDataKeys.Contains($"{c.ProviderId}::{c.ModelId}"))
                        .ToList())
                    {
                        _aiConfigurationService.DeleteConfiguration(orphan.Id);
                        LogScoped($"[ModelManagement] 孤儿配置已清理: {orphan.Name}({orphan.ModelId})");
                    }
                }
                catch (Exception ex) { LogScoped($"[ModelManagement] 孤儿配置清理失败: {ex.Message}"); }
            }
            catch (Exception ex) { LogScoped($"[ModelManagement] 同步供应商失败: {ex.Message}"); }
        });
        _currentEditingData = null;
        _currentEditingCategory = null;
        ResetForm();
        OnPropertyChanged(nameof(IsGlobalParametersAvailable));
        OnPropertyChanged(nameof(IsTab1Enabled));
        OnPropertyChanged(nameof(IsApiConfigEditable));
        OnPropertyChanged(nameof(IsApiActionEnabled));
        OnPropertyChanged(nameof(IsTab2Enabled));
    }

    protected override List<AIProviderCategory> GetAllCategoriesFromService()
    {
        return Service.GetAllCategories();
    }

    private Dictionary<string, AIProviderCategory>? _level2CategoryCache;

    protected override void OnTreeDataRefreshed()
    {
        base.OnTreeDataRefreshed();
        _level2CategoryCache = null;
        CleanupCategorySelectionTree();
    }

    protected override List<UserConfigurationData> GetAllDataItems()
    {
        return Service.GetAllData();
    }

    protected override string GetDataCategory(UserConfigurationData data)
    {
        return data.Category;
    }

    protected override TreeNodeItem ConvertToTreeNode(UserConfigurationData data)
    {
        _level2CategoryCache ??= Service.GetAllCategories()
            .Where(c => c.Level == 2)
            .GroupBy(c => c.Name)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _level2CategoryCache.TryGetValue(data.Category ?? "", out var provider);

        System.Windows.Media.ImageSource? logoImage = null;
        System.Windows.Media.ImageSource? icon = IconHelper.TryGet(DefaultDataIcon);

        if (!string.IsNullOrWhiteSpace(data.Icon))
        {
            if (data.Icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                logoImage = TM.Framework.Common.Helpers.AI.ProviderLogoHelper.GetLogo(data.Icon, DefaultDataIcon);
                icon = IconHelper.TryGet(DefaultDataIcon);
            }
            else
            {
                icon = IconHelper.TryGet(data.Icon);
            }
        }

        if (logoImage == null && !string.IsNullOrWhiteSpace(data.ModelName))
        {
            var modelLogoPath = TM.Framework.Common.Helpers.AI.ProviderLogoHelper.GetLogoFileName(data.ModelName);
            if (!string.IsNullOrEmpty(modelLogoPath))
            {
                logoImage = TM.Framework.Common.Helpers.AI.ProviderLogoHelper.GetLogo(modelLogoPath, DefaultDataIcon);
            }
        }

        if (logoImage == null && provider != null)
        {
            logoImage = GetCategoryLogoImage(provider);
        }

        return new TreeNodeItem
        {
            Name = data.Name,
            Icon = icon,
            Tag = data,
            ShowChildCount = false,
            LogoImage = logoImage
        };
    }

    protected override string[] GetSearchAdditionalFields(UserConfigurationData data)
    {
        return new[] { data.ModelName, data.Description, data.ProviderName };
    }

    protected override string NewItemTypeName => "模型配置";

    private ICommand? _addCommand;
    public ICommand AddCommand => _addCommand ??= new RelayCommand(_ =>
    {
        try
        {
            _currentEditingData = null;
            _currentEditingCategory = null;
            ResetForm();
            ExecuteAddWithCreateMode();
            RefreshFormStateProperties();
        }
        catch (Exception ex)
        {
            LogScoped($"[ModelManagement] 新建失败: {ex.Message}");
            GlobalToast.Error("新建失败", $"新建失败：{ex.Message}");
        }
    });

    private ICommand? _saveCommand;
    public ICommand SaveCommand => _saveCommand ??= new AsyncRelayCommand(async () =>
    {
        try
        {
            switch (CurrentTabIndex)
            {
                case 2:
                    Service.SaveParameterProfilesFromUI(ParameterProfiles);
                    LoadParameterProfilesForUI();
                    GlobalToast.Success("保存成功", "参数模板已更新");
                    return;

                case 3:
                    SaveWritingSettings();
                    GlobalToast.Success("保存成功", "写作配置已保存");
                    return;

                default:
                    await Service.EnsureInitializedAsync().ConfigureAwait(true);
                    ExecuteSaveWithCreateEditMode(
                        validateForm: ValidateFormCore,
                        createCategoryCore: CreateCategoryCore,
                        createDataCore: CreateDataCore,
                        hasEditingCategory: () => _currentEditingCategory != null,
                        hasEditingData: () => _currentEditingData != null,
                        updateCategoryCore: UpdateCategoryCore,
                        updateDataCore: UpdateDataCore);
                    return;
            }
        }
        catch (Exception ex)
        {
            LogScoped($"[ModelManagement] 保存失败: {ex.Message}");
            GlobalToast.Error("保存失败", $"保存失败：{ex.Message}");
        }
    });

    private ICommand? _deleteCommand;
    public ICommand DeleteCommand => _deleteCommand ??= new RelayCommand(param =>
    {
        try
        {
            if (param is TreeNodeItem node)
            {
                if (node.Tag is AIProviderCategory category)
                {
                    _currentEditingCategory = category;
                    _currentEditingData = null;
                }
                else if (node.Tag is UserConfigurationData data)
                {
                    _currentEditingData = data;
                    _currentEditingCategory = null;
                }
            }

            if (_currentEditingCategory != null)
            {
                if (_currentEditingCategory.IsBuiltIn)
                {
                    GlobalToast.Warning("无法删除", $"「{_currentEditingCategory.Name}」是系统内置分类，不可删除");
                    return;
                }

                var childCount = CollectCategoryAndChildrenNames(_currentEditingCategory.Name).Count - 1;

                var result = StandardDialog.ShowConfirm(
                    $"确定要删除分类「{_currentEditingCategory.Name}」吗？\n\n注意：该分类及其{childCount}个子分类下的所有模型配置也会被删除！",
                    "确认删除"
                );
                if (!result) return;

                try
                {
                    var allNames = new HashSet<string>(
                        CollectCategoryAndChildrenNames(_currentEditingCategory.Name),
                        StringComparer.OrdinalIgnoreCase);

                    var providerIdSet = new HashSet<string>(
                        Service.GetAllData()
                            .Where(d => allNames.Contains(d.Category) || allNames.Contains(d.ProviderName ?? string.Empty))
                            .Select(d => d.CategoryId)
                            .Where(id => !string.IsNullOrWhiteSpace(id)),
                        StringComparer.OrdinalIgnoreCase);

                    foreach (var p in _aiLibraryService.GetAllProviders().Where(p => allNames.Contains(p.Name)))
                        providerIdSet.Add(p.Id);

                    foreach (var cfg in _aiConfigurationService.GetAllConfigurations()
                        .Where(c => providerIdSet.Contains(c.ProviderId))
                        .ToList())
                    {
                        _aiConfigurationService.DeleteConfiguration(cfg.Id);
                    }
                }
                catch (Exception ex)
                {
                    LogScoped($"[ModelManagement] 删除分类同步清理对话配置失败: {ex.Message}");
                }

                var (catDeleted, dataDeleted) = Service.CascadeDeleteCategory(_currentEditingCategory.Name);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Service.SyncProvidersFromCategoriesAsync().ConfigureAwait(false);
                        await _aiLibraryService.ReloadLibraryAsync().ConfigureAwait(false);

                        try
                        {
                            var validIds = new HashSet<string>(
                                _aiLibraryService.GetAllProviders().Select(p => p.Id),
                                StringComparer.OrdinalIgnoreCase);
                            var liveDataKeys = new HashSet<string>(
                                Service.GetAllData()
                                    .Where(d => !string.IsNullOrWhiteSpace(d.CategoryId)
                                             && !string.IsNullOrWhiteSpace(d.ModelName))
                                    .Select(d => $"{d.CategoryId}::{d.ModelName}"),
                                StringComparer.OrdinalIgnoreCase);
                            foreach (var orphan in _aiConfigurationService.GetAllConfigurations()
                                .Where(c => !validIds.Contains(c.ProviderId)
                                         || !liveDataKeys.Contains($"{c.ProviderId}::{c.ModelId}"))
                                .ToList())
                            {
                                _aiConfigurationService.DeleteConfiguration(orphan.Id);
                                LogScoped($"[ModelManagement] 孤儿配置已清理: {orphan.Name}({orphan.ModelId})");
                            }
                        }
                        catch (Exception ex) { LogScoped($"[ModelManagement] 孤儿配置清理失败: {ex.Message}"); }
                    }
                    catch (Exception ex) { LogScoped($"[ModelManagement] 同步供应商失败: {ex.Message}"); }
                });

                GlobalToast.Success("删除成功", $"已删除 {catDeleted} 个分类及其 {dataDeleted} 个模型配置");

                _currentEditingCategory = null;
                _currentEditingData = null;
                ResetForm();
                RefreshTreeAndCategorySelection();

                RefreshFormStateProperties();
            }
            else if (_currentEditingData != null)
            {
                var result = StandardDialog.ShowConfirm($"确定要删除模型配置「{_currentEditingData.Name}」吗？", "确认删除");
                if (!result) return;

                try
                {
                    var providers = _aiLibraryService.GetAllProviders();
                    var provider = providers.FirstOrDefault(p =>
                        string.Equals(p.Name, _currentEditingData.Category, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.Name, _currentEditingData.ProviderName, StringComparison.OrdinalIgnoreCase));

                    if (provider != null)
                    {
                        var models = _aiLibraryService.GetModelsByProvider(provider.Id);
                        var modelName = (_currentEditingData.ModelName ?? string.Empty).Trim();

                        if (!string.IsNullOrWhiteSpace(modelName))
                        {
                            TM.Services.Framework.AI.Core.AIModel? model = null;
                            if (models.Count > 0)
                            {
                                model = models.FirstOrDefault(m =>
                                    string.Equals(m.Id, modelName, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(m.Name, modelName, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(m.DisplayName, modelName, StringComparison.OrdinalIgnoreCase));
                            }

                            var modelId = model?.Id ?? modelName;

                            var configToDelete = _aiConfigurationService.GetAllConfigurations()
                                .FirstOrDefault(c =>
                                    string.Equals(c.ProviderId, provider.Id, StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(c.ModelId, modelId, StringComparison.OrdinalIgnoreCase));

                            if (configToDelete != null)
                            {
                                _aiConfigurationService.DeleteConfiguration(configToDelete.Id);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogScoped($"[ModelManagement] 删除模型配置同步清理对话配置失败: {ex.Message}");
                }

                Service.DeleteConfiguration(_currentEditingData.Id);
                GlobalToast.Success("删除成功", $"模型配置「{_currentEditingData.Name}」已删除");

                _currentEditingData = null;
                ResetForm();
                RefreshTreeAndCategorySelection();

                OnPropertyChanged(nameof(IsGlobalParametersAvailable));
                OnPropertyChanged(nameof(IsTab1Enabled));
                OnPropertyChanged(nameof(IsApiConfigEditable));
                OnPropertyChanged(nameof(IsApiActionEnabled));
                OnPropertyChanged(nameof(IsTab2Enabled));
                OnPropertyChanged(nameof(FormReasoningEffortEnabled));
                OnPropertyChanged(nameof(FormThinkingParamsEnabled));
            }
            else
            {
                GlobalToast.Warning("删除失败", "请先选择要删除的分类或模型配置");
            }
        }
        catch (Exception ex)
        {
            LogScoped($"[ModelManagement] 删除失败: {ex.Message}");
            GlobalToast.Error("删除失败", $"删除失败：{ex.Message}");
        }
    });

    private ICommand? _selectNodeCommand;
    public ICommand SelectNodeCommand => _selectNodeCommand ??= new RelayCommand(param =>
    {
        try
        {
            if (param is TreeNodeItem node)
            {
                if (node.Tag is UserConfigurationData data)
                {
                    _currentEditingData = data;
                    _currentEditingCategory = null;
                    LoadDataToForm(data);
                    OnDataItemLoaded();
                    RefreshFormStateProperties();
                }
                else if (node.Tag is AIProviderCategory category)
                {
                    _currentEditingCategory = category;
                    _currentEditingData = null;
                    LoadCategoryToForm(category);
                    EnterEditMode();
                    RefreshFormStateProperties();
                }
            }
        }
        catch (Exception ex)
        {
            LogScoped($"[ModelManagement] 选择节点失败: {ex.Message}");
            GlobalToast.Error("选择失败", $"选择失败：{ex.Message}");
        }
    });

    private bool ValidateFormCore()
    {
        if (string.IsNullOrWhiteSpace(FormName))
        {
            GlobalToast.Warning("保存失败", "请输入名称");
            return false;
        }

        if (!IsCreateMode && _currentEditingCategory == null && _currentEditingData == null)
        {
            GlobalToast.Warning("保存失败", "请先新建，或在左侧选择要编辑的分类或模型配置");
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

        var logoFileName = TM.Framework.Common.Helpers.AI.ProviderLogoHelper.GetLogoFileName(FormName);

        var newCategory = new AIProviderCategory
        {
            Id = ShortIdGenerator.New("C"),
            Name = FormName,
            Icon = categoryIcon,
            LogoPath = logoFileName,
            ParentCategory = parentCategoryName,
            Level = level,
            Order = Service.GetAllCategories().Count + 1,
            Description = FormDescription,
            ApiEndpoint = level == 2 ? FormApiEndpoint : null,
            ApiKey = level == 2 ? FormApiKey : null
        };

        if (!Service.AddCategory(newCategory))
        {
            GlobalToast.Warning("创建失败", "分类名已存在，请改名");
            return;
        }

        _ = Task.Run(async () =>
        {
            try { await Service.SyncProvidersFromCategoriesAsync().ConfigureAwait(false); await _aiLibraryService.ReloadLibraryAsync().ConfigureAwait(false); }
            catch (Exception ex) { LogScoped($"[ModelManagement] 同步供应商失败: {ex.Message}"); }
        });

        string levelDesc = level == 1 ? "一级分类" : $"{level}级分类";
        GlobalToast.Success("保存成功", $"{levelDesc}「{newCategory.Name}」已创建");

        _currentEditingCategory = null;
        _currentEditingData = null;
        ResetForm();

        OnPropertyChanged(nameof(IsGlobalParametersAvailable));

        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            OnPropertyChanged(nameof(IsTab1Enabled));
            OnPropertyChanged(nameof(IsApiConfigEditable));
            OnPropertyChanged(nameof(IsApiActionEnabled));
            OnPropertyChanged(nameof(IsTab2Enabled));
            OnPropertyChanged(nameof(FormReasoningEffortEnabled));
            OnPropertyChanged(nameof(FormThinkingParamsEnabled));
        });
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
        Service.AddConfiguration(newData);
        _currentEditingData = newData;
        GlobalToast.Success("保存成功", $"模型配置「{newData.Name}」已创建");
        _ = SyncToAIServiceAndActivateAsync(newData);
        _ = Task.Run(async () =>
        {
            try { await _aiLibraryService.ReloadLibraryAsync().ConfigureAwait(false); }
            catch (Exception ex) { LogScoped($"[ModelManagement] 重载模型库失败: {ex.Message}"); }
        });

        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            OnPropertyChanged(nameof(IsTab1Enabled));
            OnPropertyChanged(nameof(IsApiConfigEditable));
            OnPropertyChanged(nameof(IsApiActionEnabled));
            OnPropertyChanged(nameof(IsTab2Enabled));
            OnPropertyChanged(nameof(FormReasoningEffortEnabled));
            OnPropertyChanged(nameof(FormThinkingParamsEnabled));
        });
    }

    private void UpdateCategoryCore()
    {
        if (_currentEditingCategory == null)
            return;

        if (_currentEditingCategory.IsBuiltIn && _currentEditingCategory.Level == 2)
        {
            GlobalToast.Warning("无法修改", $"「{_currentEditingCategory.Name}」是系统内置供应商，不可修改");
            return;
        }

        var oldName = _currentEditingCategory.Name;
        _currentEditingCategory.Name = FormName;
        _currentEditingCategory.Icon = GetCategoryIconForSave(FormIcon);
        _currentEditingCategory.Description = FormDescription;

        if (_currentEditingCategory.Level == 2)
        {
            _currentEditingCategory.ApiEndpoint = FormApiEndpoint;
            _currentEditingCategory.ApiKey = FormApiKey;
        }

        if (!Service.UpdateCategory(_currentEditingCategory))
        {
            _currentEditingCategory.Name = oldName;
            GlobalToast.Warning("保存失败", "分类名已存在，请改名");
            return;
        }

        if (_currentEditingCategory.Level == 2
            && !string.Equals(oldName, _currentEditingCategory.Name, StringComparison.Ordinal))
        {
            var models = Service.GetModelsForProvider(_currentEditingCategory).ToList();
            if (models.Count > 0)
            {
                foreach (var m in models)
                {
                    m.Category = _currentEditingCategory.Name;
                    m.ProviderName = _currentEditingCategory.Name;
                    m.CategoryId = _currentEditingCategory.Id;
                }
                Service.SaveModelsForProvider(_currentEditingCategory, models);
                LogScoped($"[ModelManagement] 供应商改名 '{oldName}' → '{_currentEditingCategory.Name}'，同步 {models.Count} 个模型的 Category 字段");
            }
        }

        Service.SyncKeyPoolsToRotationService();
        _ = Task.Run(async () =>
        {
            try { await Service.SyncProvidersFromCategoriesAsync().ConfigureAwait(false); await _aiLibraryService.ReloadLibraryAsync().ConfigureAwait(false); }
            catch (Exception ex) { LogScoped($"[ModelManagement] 同步供应商失败: {ex.Message}"); }
        });
        GlobalToast.Success("保存成功", $"分类「{_currentEditingCategory.Name}」已更新");
    }

}

