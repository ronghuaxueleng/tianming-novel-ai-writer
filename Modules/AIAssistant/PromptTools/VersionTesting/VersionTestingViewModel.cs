using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using TM.Framework.Common.ViewModels;
using TM.Modules.AIAssistant.PromptTools.PromptManagement.Models;
using TM.Modules.AIAssistant.PromptTools.VersionTesting.Models;
using TM.Modules.AIAssistant.PromptTools.VersionTesting.Services;
using TM.Services.Framework.AI.Interfaces.AI;
using TM.Services.Framework.AI.Interfaces.Prompts;

namespace TM.Modules.AIAssistant.PromptTools.VersionTesting;

[Obfuscation(Exclude = true, ApplyToMembers = true)]
[Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
public partial class VersionTestingViewModel : DataManagementViewModelBase<TestVersionData, PromptCategory, VersionTestingService>, TM.Framework.Common.ViewModels.IAIGeneratingState, IDisposable
{
    private readonly IPromptRepository _promptRepository;
    private readonly IAITextGenerationService _aiTextGenerationService;
    private TestVersionData? _selectedVersion;
    private PromptTemplateData? _selectedPrompt;
    private List<PromptTemplateData> _promptCache = new();
    private CancellationTokenSource? _testCts;
    private AsyncRelayCommand _executeTestCommand = null!;
    private readonly RelayCommand _cancelTestCommand;

    public ObservableCollection<string> CategoryOptions { get; } = new TM.Framework.Common.ViewModels.RangeObservableCollection<string>();

    public TestVersionData? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (_selectedVersion != value)
            {
                _selectedVersion = value;
                OnPropertyChanged();
                LoadFormFromVersion(_selectedVersion);
                UpdateAIGenerateButtonState();
            }
        }
    }

    public PromptTemplateData? SelectedPrompt
    {
        get => _selectedPrompt;
        private set
        {
            if (_selectedPrompt != value)
            {
                _selectedPrompt = value;
                OnPropertyChanged();

                if (value != null && !string.Equals(FormCategory, value.Category, StringComparison.Ordinal))
                {
                    FormCategory = value.Category;
                }
            }
        }
    }

}
