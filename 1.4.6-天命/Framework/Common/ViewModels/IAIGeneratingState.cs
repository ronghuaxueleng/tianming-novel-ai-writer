using System.Windows.Input;

namespace TM.Framework.Common.ViewModels
{
    public interface IAIGeneratingState
    {
        bool IsAIGenerating { get; }

        bool IsBatchGenerating { get; }

        string BatchProgressText { get; }

        ICommand CancelBatchGenerationCommand { get; }
    }
}
