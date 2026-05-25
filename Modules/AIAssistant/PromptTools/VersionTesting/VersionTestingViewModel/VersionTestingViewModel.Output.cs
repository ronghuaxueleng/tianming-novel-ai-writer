using System;
using TM.Framework.Common.ViewModels;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Models;
using TM.Modules.AIAssistant.PromptTools.VersionTesting.Models;
using TM.Modules.AIAssistant.PromptTools.VersionTesting.Services;

namespace TM.Modules.AIAssistant.PromptTools.VersionTesting;

public partial class VersionTestingViewModel : DataManagementViewModelBase<TestVersionData, PromptCategory, VersionTestingService>, TM.Framework.Common.ViewModels.IAIGeneratingState, IDisposable
{
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
}
