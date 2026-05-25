using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TM.Framework.Common.Controls;
using TM.Framework.Common.ViewModels;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Models;
using TM.Modules.AIAssistant.PromptTools.VersionTesting.Models;
using TM.Modules.AIAssistant.PromptTools.VersionTesting.Services;

namespace TM.Modules.AIAssistant.PromptTools.VersionTesting;

public partial class VersionTestingViewModel : DataManagementViewModelBase<TestVersionData, PromptCategory, VersionTestingService>, TM.Framework.Common.ViewModels.IAIGeneratingState, IDisposable
{
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

    private System.Windows.Media.ImageSource? _selectedCategoryAIcon;
    public System.Windows.Media.ImageSource? SelectedCategoryAIcon
    {
        get => _selectedCategoryAIcon;
        set { _selectedCategoryAIcon = value; OnPropertyChanged(); }
    }

    public ObservableCollection<TreeNodeItem> CategoryATree { get; } = new TM.Framework.Common.ViewModels.RangeObservableCollection<TreeNodeItem>();

    #endregion
}
