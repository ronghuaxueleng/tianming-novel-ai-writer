using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Services.Framework.AI.SemanticKernel;

namespace TM.Framework.UI.Workspace.RightPanel.Controls
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class TodoPanelViewModel : INotifyPropertyChanged, IDisposable
    {
        private Guid _currentRunId;
        private string _progressText = "等待执行";
        private string _statusMessage = "空闲中";
        private string _currentStatusTitle = "空闲中";
        private ImageSource? _titleIcon;
        private TodoStepViewModel? _selectedStep;
        private bool _canBackToPlan;
        private ChatMode _currentMode;

        private readonly DispatcherTimer _runningTimer;
        private DateTime? _runningStartedAt;

        public ObservableCollection<TodoStepViewModel> Steps { get; } = new();

        public TodoPanelViewModel()
        {
            _runningTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _runningTimer.Tick += (_, _) => RefreshRunningElapsed();
        }

        public void Dispose()
        {
            _runningTimer.Stop();
            GC.SuppressFinalize(this);
        }

        public bool CanBackToPlan
        {
            get => _canBackToPlan;
            set { if (_canBackToPlan != value) { _canBackToPlan = value; OnPropertyChanged(); } }
        }

        public string CurrentStatusTitle
        {
            get => _currentStatusTitle;
            set { if (_currentStatusTitle != value) { _currentStatusTitle = value; OnPropertyChanged(); } }
        }

        public ImageSource? TitleIcon
        {
            get => _titleIcon;
            set { if (_titleIcon != value) { _titleIcon = value; OnPropertyChanged(); } }
        }

        public string ProgressText
        {
            get => _progressText;
            set { if (_progressText != value) { _progressText = value; OnPropertyChanged(); } }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { if (_statusMessage != value) { _statusMessage = value; OnPropertyChanged(); } }
        }

        public TodoStepViewModel? SelectedStep
        {
            get => _selectedStep;
            set
            {
                if (_selectedStep != value)
                {
                    _selectedStep = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectedStepMeta));
                }
            }
        }

        public string SelectedStepMeta
        {
            get
            {
                if (SelectedStep == null)
                {
                    return string.Empty;
                }

                var parts = new System.Collections.Generic.List<string>();

                if (SelectedStep.Timestamp != default)
                {
                    parts.Add(SelectedStep.Timestamp.ToString("HH:mm:ss"));
                }

                if (!string.IsNullOrWhiteSpace(SelectedStep.ToolName))
                {
                    parts.Add(SelectedStep.ToolName);
                }

                parts.Add(SelectedStep.EventType.ToString());

                return string.Join(" · ", parts);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void OnExecutionEvent(ExecutionEvent evt)
        {
            if (evt.RunType == TM.Services.Framework.AI.SemanticKernel.RunType.Chat)
            {
                return;
            }

            if (evt.Mode != ChatMode.Plan && evt.Mode != ChatMode.Agent && evt.Mode != ChatMode.Edit)
            {
                return;
            }

            if (evt.EventType == ExecutionEventType.RunStarted)
            {
                _currentRunId = evt.RunId;
                _currentMode = evt.Mode;
                Steps.Clear();
                StatusMessage = "执行中...";
                SetTitleStatus("执行中", "Icon.Refresh");
                ProgressText = "0 步";
                CanBackToPlan = evt.Mode == ChatMode.Plan;

                _runningStartedAt = null;
                _runningTimer.Stop();
            }

            if (_currentRunId == Guid.Empty || evt.RunId != _currentRunId)
            {
                return;
            }

            switch (evt.EventType)
            {
                case ExecutionEventType.ToolCallStarted:
                case ExecutionEventType.ToolCallCompleted:
                case ExecutionEventType.ToolCallFailed:
                    HandleStepEvent(evt);
                    break;
                case ExecutionEventType.RunCompleted:
                    StatusMessage = "执行完成";
                    SetTitleStatus("执行完成", "Icon.CheckCircle");
                    foreach (var step in Steps.Where(s => s.IsRunning))
                    {
                        step.IsRunning = false;
                        step.Succeeded = true;
                        step.StatusIcon = IconHelper.TryGet("Icon.CheckCircle");
                        step.StatusText = "完成";
                        step.StepBackground = Brushes.Transparent;
                    }

                    _runningStartedAt = null;
                    _runningTimer.Stop();
                    break;
                case ExecutionEventType.RunFailed:
                    StatusMessage = evt.IsPolishFatal
                        ? "润色失败，已终止所有任务"
                        : "执行失败";
                    SetTitleStatus("执行失败", "Icon.Error");

                    if (evt.IsPolishFatal)
                    {
                        Steps.Clear();
                        SelectedStep = null;
                        CanBackToPlan = false;
                    }

                    _runningStartedAt = null;
                    _runningTimer.Stop();
                    break;
            }

            RecalculateProgress();
        }

        private void HandleStepEvent(ExecutionEvent evt)
        {
            if (evt.StepIndex == null && string.IsNullOrEmpty(evt.Title) && string.IsNullOrEmpty(evt.FunctionName))
            {
                return;
            }

            TodoStepViewModel? step = null;

            if (evt.StepIndex != null)
            {
                step = Steps.FirstOrDefault(s => s.StepIndex == evt.StepIndex);
            }

            if (step == null
                && evt.EventType == ExecutionEventType.ToolCallStarted
                && Steps.Count > 0
                && !string.IsNullOrEmpty(evt.FunctionName))
            {
                var last = Steps[Steps.Count - 1];
                if (string.Equals(last.PluginName, evt.PluginName, StringComparison.Ordinal)
                    && string.Equals(last.FunctionName, evt.FunctionName, StringComparison.Ordinal))
                {
                    last.RepeatCount += 1;
                    last.RunId = evt.RunId;
                    last.EventId = evt.Id;
                    last.EventType = evt.EventType;
                    last.Timestamp = evt.Timestamp;
                    if (!string.IsNullOrWhiteSpace(evt.Detail))
                    {
                        last.Detail = evt.Detail;
                    }
                    last.ToolName = BuildDisplayName(last, evt.Title);
                    step = last;
                }
            }

            if (step == null)
            {
                var stepNum = evt.StepIndex ?? (Steps.Count + 1);
                var title = !string.IsNullOrEmpty(evt.Title) ? evt.Title : BuildToolName(evt);
                var displayName = $"{stepNum}. {title}";
                step = new TodoStepViewModel
                {
                    StepIndex = evt.StepIndex,
                    PluginName = evt.PluginName,
                    FunctionName = evt.FunctionName,
                    RunId = evt.RunId,
                    EventId = evt.Id,
                    EventType = evt.EventType,
                    Timestamp = evt.Timestamp,
                    ToolName = displayName,
                    Description = evt.Title,
                    StatusIcon = IconHelper.TryGet("Icon.Clock"),
                    StatusText = "等待执行",
                    IsRunning = false,
                    Succeeded = null,
                    Detail = evt.Detail ?? evt.Title
                };
                Steps.Add(step);
            }

            step.EventId = evt.Id;
            step.EventType = evt.EventType;
            step.Timestamp = evt.Timestamp;

            if (!string.IsNullOrWhiteSpace(evt.Detail))
            {
                step.Detail = evt.Detail;
            }

            switch (evt.EventType)
            {
                case ExecutionEventType.ToolCallStarted:
                case ExecutionEventType.PlanStepStarted:
                    bool isInitialState = evt.Detail == "等待执行";
                    if (isInitialState)
                    {
                        step.IsRunning = false;
                        step.Succeeded = null;
                        step.StatusIcon = IconHelper.TryGet("Icon.Clock");
                        step.StatusText = "等待";
                        step.StepBackground = Brushes.Transparent;
                    }
                    else
                    {
                        foreach (var prevStep in Steps.Where(s => s.IsRunning && s != step))
                        {
                            prevStep.IsRunning = false;
                            prevStep.Succeeded = true;
                            prevStep.StatusIcon = IconHelper.TryGet("Icon.CheckCircle");
                            prevStep.StatusText = "完成";
                            prevStep.StepBackground = Brushes.Transparent;
                        }
                        step.IsRunning = true;
                        step.Succeeded = null;
                        step.StatusIcon = IconHelper.TryGet("Icon.Clock");
                        step.StatusText = BuildRunningStatusText(null);
                        step.StepBackground = Brushes.Transparent;
                        SetTitleStatus("执行中", "Icon.Refresh");

                        _runningStartedAt = DateTime.Now;
                        if (!_runningTimer.IsEnabled)
                        {
                            _runningTimer.Start();
                        }
                        RefreshRunningElapsed();
                    }
                    break;
                case ExecutionEventType.ToolCallCompleted:
                case ExecutionEventType.PlanStepCompleted:
                    step.IsRunning = false;
                    step.Succeeded = true;
                    step.StatusIcon = IconHelper.TryGet("Icon.CheckCircle");
                    step.StatusText = "完成";
                    step.StepBackground = Brushes.Transparent;
                    UpdateTitleFromSteps();

                    if (!Steps.Any(s => s.IsRunning))
                    {
                        _runningStartedAt = null;
                        _runningTimer.Stop();
                    }
                    break;
                case ExecutionEventType.ToolCallFailed:
                    step.IsRunning = false;
                    step.Succeeded = false;
                    step.StatusIcon = IconHelper.TryGet("Icon.Forbidden");
                    step.StatusText = "失败";
                    step.StepBackground = Brushes.Transparent;
                    SetTitleStatus("执行失败", "Icon.Error");

                    if (!Steps.Any(s => s.IsRunning))
                    {
                        _runningStartedAt = null;
                        _runningTimer.Stop();
                    }
                    break;
            }
        }

        private void RefreshRunningElapsed()
        {
            var step = Steps.FirstOrDefault(s => s.IsRunning);
            if (step == null)
            {
                _runningStartedAt = null;
                _runningTimer.Stop();
                return;
            }

            if (!_runningStartedAt.HasValue)
            {
                _runningStartedAt = step.Timestamp == default ? DateTime.Now : step.Timestamp;
            }

            var elapsed = DateTime.Now - _runningStartedAt.Value;
            var seconds = Math.Max(1, (int)elapsed.TotalSeconds);
            step.StatusText = $"执行中（已运行 {seconds} 秒）";
            StatusMessage = $"{step.ToolName} · {step.StatusText}";
        }

        private void UpdateTitleFromSteps()
        {
            if (Steps.Any(s => s.Succeeded == false))
            {
                SetTitleStatus("执行失败", "Icon.Error");
                return;
            }

            if (Steps.Any(s => s.IsRunning))
            {
                SetTitleStatus("执行中", "Icon.Refresh");
                return;
            }

            if (Steps.Count > 0 && Steps.All(s => s.Succeeded == true))
            {
                SetTitleStatus("执行完成", "Icon.CheckCircle");
                return;
            }

            SetTitleStatus("空闲中", "Icon.Clock");
        }

        private void SetTitleStatus(string title, string iconKey)
        {
            CurrentStatusTitle = title;
            TitleIcon = IconHelper.TryGet(iconKey);
        }

        private void RecalculateProgress()
        {
            if (Steps.Count == 0)
            {
                ProgressText = "0 步";
                return;
            }

            var completed = Steps.Count(s => s.Succeeded == true);
            ProgressText = $"{completed}/{Steps.Count} 步完成";
        }

        private static string BuildToolName(ExecutionEvent evt)
        {
            if (!string.IsNullOrEmpty(evt.PluginName) && !string.IsNullOrEmpty(evt.FunctionName))
            {
                return $"{evt.PluginName}/{evt.FunctionName}";
            }

            if (!string.IsNullOrEmpty(evt.FunctionName))
            {
                return evt.FunctionName!;
            }

            return evt.Title;
        }

        private static string BuildDisplayName(TodoStepViewModel step, string? fallbackTitle)
        {
            var title = !string.IsNullOrWhiteSpace(step.Description)
                ? step.Description
                : (!string.IsNullOrWhiteSpace(fallbackTitle) ? fallbackTitle! : step.FunctionName ?? string.Empty);

            var stepNum = step.StepIndex;
            var prefix = stepNum.HasValue ? $"{stepNum.Value}. " : string.Empty;
            var suffix = step.RepeatCount > 1 ? $" ×{step.RepeatCount}" : string.Empty;
            return $"{prefix}{title}{suffix}";
        }

        private static string BuildRunningStatusText(string? detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                return "执行中";
            }

            var d = detail.Trim();
            if (d.Contains("已运行", StringComparison.OrdinalIgnoreCase) || d.StartsWith("执行中", StringComparison.OrdinalIgnoreCase))
            {
                return d;
            }

            return "执行中";
        }

    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class TodoStepViewModel : INotifyPropertyChanged
    {
        private string _toolName = string.Empty;
        private string _description = string.Empty;
        private string _statusText = string.Empty;
        private ImageSource? _statusIcon;
        private bool _isRunning;
        private Brush _stepBackground = Brushes.Transparent;
        private bool? _succeeded;
        private string _detail = string.Empty;

        public int? StepIndex { get; set; }
        public string? PluginName { get; set; }
        public string? FunctionName { get; set; }

        public Guid RunId { get; set; }

        public Guid EventId { get; set; }

        public ExecutionEventType EventType { get; set; }

        public DateTime Timestamp { get; set; }

        public string ToolName
        {
            get => _toolName;
            set { if (_toolName != value) { _toolName = value; OnPropertyChanged(); } }
        }

        private int _repeatCount = 1;
        public int RepeatCount
        {
            get => _repeatCount;
            set { if (_repeatCount != value) { _repeatCount = value; OnPropertyChanged(); } }
        }

        public string Description
        {
            get => _description;
            set { if (_description != value) { _description = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasDescription)); } }
        }

        public bool HasDescription => !string.IsNullOrWhiteSpace(_description);

        public string Detail
        {
            get => _detail;
            set { if (_detail != value) { _detail = value; OnPropertyChanged(); } }
        }

        public string StatusText
        {
            get => _statusText;
            set { if (_statusText != value) { _statusText = value; OnPropertyChanged(); } }
        }

        public ImageSource? StatusIcon
        {
            get => _statusIcon;
            set { if (_statusIcon != value) { _statusIcon = value; OnPropertyChanged(); } }
        }

        public bool IsRunning
        {
            get => _isRunning;
            set { if (_isRunning != value) { _isRunning = value; OnPropertyChanged(); } }
        }

        public Brush StepBackground
        {
            get => _stepBackground;
            set { if (_stepBackground != value) { _stepBackground = value; OnPropertyChanged(); } }
        }

        public bool? Succeeded
        {
            get => _succeeded;
            set { if (_succeeded != value) { _succeeded = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
