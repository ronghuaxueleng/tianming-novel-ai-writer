using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using TM.Framework.Common.ViewModels;

namespace TM.Modules.Design.Templates.OneClickGenerate.OneClickDirect
{
    public partial class OneClickGenerateViewModel : INotifyPropertyChanged, IAIGeneratingState
    {
        #region 属性

        public ObservableCollection<PipelineStepViewModel> PipelineSteps { get; } = new();
        public IEnumerable<PipelineStepViewModel> DesignSteps => PipelineSteps.Where(s => s.StepIndex <= 6);
        public IEnumerable<PipelineStepViewModel> GenerateSteps => PipelineSteps.Where(s => s.StepIndex > 6);
        public ObservableCollection<LogEntry> LogEntries { get; } = new();

        private string _overallProgressText = "等待开始...";
        public string OverallProgressText
        {
            get => _overallProgressText;
            set { _overallProgressText = value; OnPropertyChanged(); OnPropertyChanged(nameof(BatchProgressText)); }
        }

        private int _overallProgressPercent;
        public int OverallProgressPercent
        {
            get => _overallProgressPercent;
            set { _overallProgressPercent = value; OnPropertyChanged(); }
        }

        private bool _isPipelineRunning;
        public bool IsPipelineRunning
        {
            get => _isPipelineRunning;
            set
            {
                _isPipelineRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsParameterEditable));
                OnPropertyChanged(nameof(IsAIGenerating));
            }
        }

        public bool IsAIGenerating => IsPipelineRunning;
        public bool IsBatchGenerating => false;
        public string BatchProgressText => OverallProgressText;
        public ICommand CancelBatchGenerationCommand => CancelPipelineCommand;

        public bool IsParameterEditable => !IsPipelineRunning;

        public string StartButtonText
            => PipelineSteps.Any(s => s.Status == StepStatus.Completed || s.Status == StepStatus.Failed || s.Status == StepStatus.Cancelled)
                ? "继续生成"
                : "开始生成";

        private static bool _suppressFailureDialogThisRun;

        private const string SuppressPipelineFailureDialogKey = "OneClickGenerate.SuppressFailureDialog";

        #endregion
    }
}
