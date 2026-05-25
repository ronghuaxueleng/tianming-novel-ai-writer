using System;
using TM.Framework.Common.ViewModels;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Models;
using TM.Modules.AIAssistant.PromptTools.VersionTesting.Models;
using TM.Modules.AIAssistant.PromptTools.VersionTesting.Services;

namespace TM.Modules.AIAssistant.PromptTools.VersionTesting;

public partial class VersionTestingViewModel : DataManagementViewModelBase<TestVersionData, PromptCategory, VersionTestingService>, TM.Framework.Common.ViewModels.IAIGeneratingState, IDisposable
{
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
}
