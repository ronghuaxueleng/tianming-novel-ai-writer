using System;
using System.Collections.ObjectModel;
using TM.Framework.Common.Controls;
using TM.Framework.Common.ViewModels;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Models;
using TM.Modules.AIAssistant.PromptTools.VersionTesting.Models;
using TM.Modules.AIAssistant.PromptTools.VersionTesting.Services;

namespace TM.Modules.AIAssistant.PromptTools.VersionTesting;

public partial class VersionTestingViewModel : DataManagementViewModelBase<TestVersionData, PromptCategory, VersionTestingService>, TM.Framework.Common.ViewModels.IAIGeneratingState, IDisposable
{
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

    private System.Windows.Media.ImageSource? _selectedCategoryBIcon;
    public System.Windows.Media.ImageSource? SelectedCategoryBIcon
    {
        get => _selectedCategoryBIcon;
        set { _selectedCategoryBIcon = value; OnPropertyChanged(); }
    }

    public ObservableCollection<TreeNodeItem> CategoryBTree { get; } = new TM.Framework.Common.ViewModels.RangeObservableCollection<TreeNodeItem>();

    #endregion
}
