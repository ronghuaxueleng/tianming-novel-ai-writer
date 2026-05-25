using System;
using System.Windows.Input;
using TM.Framework.Common.ViewModels;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Models;
using TM.Modules.AIAssistant.PromptTools.VersionTesting.Models;
using TM.Modules.AIAssistant.PromptTools.VersionTesting.Services;

namespace TM.Modules.AIAssistant.PromptTools.VersionTesting;

public partial class VersionTestingViewModel : DataManagementViewModelBase<TestVersionData, PromptCategory, VersionTestingService>, TM.Framework.Common.ViewModels.IAIGeneratingState, IDisposable
{
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
}
