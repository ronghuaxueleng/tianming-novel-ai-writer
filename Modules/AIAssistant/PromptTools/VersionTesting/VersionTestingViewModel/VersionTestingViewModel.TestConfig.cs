using System;
using TM.Framework.Common.ViewModels;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Models;
using TM.Modules.AIAssistant.PromptTools.VersionTesting.Models;
using TM.Modules.AIAssistant.PromptTools.VersionTesting.Services;

namespace TM.Modules.AIAssistant.PromptTools.VersionTesting;

public partial class VersionTestingViewModel : DataManagementViewModelBase<TestVersionData, PromptCategory, VersionTestingService>, TM.Framework.Common.ViewModels.IAIGeneratingState, IDisposable
{
    #region 测试配置

    private string _testInput = string.Empty;
    public string TestInput
    {
        get => _testInput;
        set { _testInput = value; OnPropertyChanged(); }
    }

    #endregion
}
