using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.ViewModels;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Models;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Services;
using TM.Framework.Common.Helpers.AI;
using TM.Services.Framework.AI.Interfaces.AI;

namespace TM.Modules.AIAssistant.PromptTools.PromptManagement;

[Obfuscation(Exclude = true, ApplyToMembers = true)]
[Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
public partial class PromptManagementViewModel : DataManagementViewModelBase<PromptTemplateData, PromptCategory, PromptService>, IDisposable
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
            .MaxBy(t => t.IsDefault)
            ?? templates.MaxBy(t => t.IsDefault)
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

    private readonly IPromptGenerationService _promptService;
    private readonly IAITextGenerationService _aiTextGenerationService;

    private bool _isSubscribedTemplatesChanged;

    public ICommand SelectNodeCommand { get; }
    public new ICommand TreeAfterActionCommand { get; }

    protected override string NewItemTypeName => "模板";

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

    public override void Dispose()
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
        base.Dispose();
    }

}
