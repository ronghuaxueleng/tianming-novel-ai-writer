using System;
using System.Collections.Generic;
using TM.Framework.Common.ViewModels;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Models;
using TM.Modules.AIAssistant.PromptTools.VersionTesting.Models;
using TM.Modules.AIAssistant.PromptTools.VersionTesting.Services;

namespace TM.Modules.AIAssistant.PromptTools.VersionTesting;

public partial class VersionTestingViewModel : DataManagementViewModelBase<TestVersionData, PromptCategory, VersionTestingService>, TM.Framework.Common.ViewModels.IAIGeneratingState, IDisposable
{
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
}
