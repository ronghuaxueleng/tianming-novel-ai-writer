using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Input;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Framework.UI.Workspace.Services;
using TM.Services.Framework.AI.SemanticKernel;

namespace TM.Framework.UI.Workspace.CenterPanel.ChapterEditor
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "controlflow", Exclude = true, ApplyToMembers = true)]
    public class PlanViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly SKChatService _chatService;
        private readonly PanelCommunicationService _comm;

        private bool _disposed;

        public PlanViewModel(PanelCommunicationService comm, SKChatService chatService)
        {
            _chatService = chatService;
            _comm = comm;

            ModifyOrCancelCommand = new RelayCommand(OnModifyOrCancel);
            StartOrSaveCommand = new RelayCommand(OnStartOrSave);

            EventsView = CollectionViewSource.GetDefaultView(Events);
            if (EventsView != null)
            {
                EventsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ExecutionEvent.StepIndex)));
            }

            ExecutionEventHub.Published += OnExecutionEvent;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ExecutionEventHub.Published -= OnExecutionEvent;
        }

        public ObservableCollection<ExecutionEvent> Events { get; } = new();

        public ICommand ModifyOrCancelCommand { get; }

        public ICommand StartOrSaveCommand { get; }

        public ICollectionView EventsView { get; } = null!;

        #region 编辑模式

        private bool _isEditMode;
        public bool IsEditMode
        {
            get => _isEditMode;
            set
            {
                if (_isEditMode != value)
                {
                    _isEditMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LeftButtonText));
                    OnPropertyChanged(nameof(RightButtonText));
                }
            }
        }

        public string LeftButtonText => IsEditMode ? "取消修改" : "修改计划";

        public string RightButtonText => IsEditMode ? "保存计划" : "开始执行";

        private string _planContent = string.Empty;
        public string PlanContent
        {
            get => _planContent;
            set
            {
                if (_planContent != value)
                {
                    _planContent = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _originalPlanContent = string.Empty;

        #endregion

        private string _runSummaryText = "尚未执行";
        public string RunSummaryText
        {
            get => _runSummaryText;
            set { if (_runSummaryText != value) { _runSummaryText = value; OnPropertyChanged(); } }
        }

        private string _errorSummaryText = string.Empty;
        public string ErrorSummaryText
        {
            get => _errorSummaryText;
            set { if (_errorSummaryText != value) { _errorSummaryText = value; OnPropertyChanged(); } }
        }

        private bool _hasFailures;
        public bool HasFailures
        {
            get => _hasFailures;
            set { if (_hasFailures != value) { _hasFailures = value; OnPropertyChanged(); } }
        }

        private ExecutionEvent? _selectedEvent;
        public ExecutionEvent? SelectedEvent
        {
            get => _selectedEvent;
            set
            {
                if (_selectedEvent != value)
                {
                    _selectedEvent = value;
                    OnPropertyChanged();

                    if (value != null)
                    {
                        _comm.PublishHighlightExecution(value.RunId, value.Id);

                        if (!IsEditMode && !string.IsNullOrWhiteSpace(value.Detail))
                        {
                            PlanContent = value.Detail;
                        }
                    }
                }
            }
        }

        #region 命令处理

        private void OnModifyOrCancel()
        {
            if (IsEditMode)
            {
                CancelEdit();
            }
            else
            {
                EnterEditMode();
            }
        }

        private void OnStartOrSave()
        {
            if (IsEditMode)
            {
                SaveChanges();
            }
            else
            {
                StartPlanExecution();
            }
        }

        private void EnterEditMode()
        {
            var stepEvents = Events
                .Where(e => e.EventType == ExecutionEventType.PlanStepStarted && e.StepIndex != null)
                .OrderBy(e => e.StepIndex!.Value)
                .ToList();

            if (stepEvents.Count == 0)
            {
                GlobalToast.Warning("无计划", "没有可编辑的计划步骤");
                return;
            }

            var sb = new StringBuilder();
            foreach (var evt in stepEvents)
            {
                sb.AppendLine($"步骤{evt.StepIndex}. {evt.Title}");
                if (!string.IsNullOrWhiteSpace(evt.Detail))
                {
                    sb.AppendLine(evt.Detail);
                }
                sb.AppendLine();
            }

            PlanContent = sb.ToString().TrimEnd();
            _originalPlanContent = PlanContent;
            IsEditMode = true;
            TM.App.Log("[PlanViewModel] 进入编辑模式");
        }

        private void CancelEdit()
        {
            PlanContent = _originalPlanContent;
            IsEditMode = false;
            TM.App.Log("[PlanViewModel] 取消编辑，已恢复原始内容");
        }

        private void SaveChanges()
        {
            if (PlanContent == _originalPlanContent)
            {
                IsEditMode = false;
                GlobalToast.Info("无修改", "计划内容未变更");
                return;
            }

            var parsedSteps = ParseEditedPlanContent(PlanContent);
            if (parsedSteps.Count == 0)
            {
                GlobalToast.Warning("解析失败", "无法识别计划步骤，请保持「步骤N. 标题」的格式");
                return;
            }

            var runId = Events.FirstOrDefault()?.RunId ?? Guid.Empty;

            var oldStepEvents = Events
                .Where(e => e.EventType == ExecutionEventType.PlanStepStarted)
                .ToList();
            foreach (var old in oldStepEvents)
            {
                Events.Remove(old);
            }

            var insertIndex = Events.Count > 0 && Events[0].EventType == ExecutionEventType.RunStarted ? 1 : 0;
            foreach (var step in parsedSteps)
            {
                Events.Insert(insertIndex++, new ExecutionEvent
                {
                    RunId = runId,
                    Mode = ChatMode.Plan,
                    EventType = ExecutionEventType.PlanStepStarted,
                    StepIndex = step.Index,
                    Title = step.Title,
                    Detail = step.Detail
                });
            }

            _originalPlanContent = PlanContent;
            IsEditMode = false;
            RecalculateSummary();
            GlobalToast.Success("已保存", $"计划已修改为 {parsedSteps.Count} 个步骤，点击「开始执行」执行");
            TM.App.Log($"[PlanViewModel] 保存修改后的计划：{parsedSteps.Count} 个步骤");
        }

        private static List<ParsedStep> ParseEditedPlanContent(string content)
        {
            var result = new List<ParsedStep>();
            if (string.IsNullOrWhiteSpace(content)) return result;

            var lines = content.Split('\n');
            var stepPattern = new Regex(
                @"^步骤\s*(\d+)[\.、：:\s]+(.+)$",
                RegexOptions.Compiled);

            int currentIndex = 0;
            string currentTitle = "";
            var currentDetail = new StringBuilder();

            foreach (var rawLine in lines)
            {
                var trimmed = rawLine.TrimEnd();
                var match = stepPattern.Match(trimmed);

                if (match.Success)
                {
                    if (currentIndex > 0)
                    {
                        result.Add(new ParsedStep(currentIndex, currentTitle, currentDetail.ToString().Trim()));
                    }

                    currentIndex = int.Parse(match.Groups[1].Value);
                    currentTitle = match.Groups[2].Value.Trim();
                    currentDetail.Clear();
                }
                else if (currentIndex > 0)
                {
                    currentDetail.AppendLine(rawLine);
                }
            }

            if (currentIndex > 0)
            {
                result.Add(new ParsedStep(currentIndex, currentTitle, currentDetail.ToString().Trim()));
            }

            return result;
        }

        private sealed record ParsedStep(int Index, string Title, string Detail);

        private void StartPlanExecution()
        {
            var stepEvents = Events
                .Where(e => e.EventType == ExecutionEventType.PlanStepStarted && e.StepIndex != null)
                .OrderBy(e => e.StepIndex!.Value)
                .ToList();

            if (stepEvents.Count == 0)
            {
                GlobalToast.Warning("无计划", "请先生成计划");
                return;
            }

            var steps = stepEvents
                .Select(e => (
                    Index: e.StepIndex!.Value,
                    Title: e.Title ?? $"步骤 {e.StepIndex}",
                    Detail: e.Detail ?? ""
                ))
                .ToList();

            TM.App.Log($"[PlanViewModel] 从 Events 提取 {steps.Count} 个步骤，开始执行");

            PlanModeFilter.IsEnabled = true;

            _comm.PublishStartPlanExecution(steps);

            _comm.PublishShowPlanViewChanged(false);
        }

        #endregion

        private void OnExecutionEvent(ExecutionEvent evt)
        {
            if (evt.RunId != _chatService.LastRunId)
            {
                return;
            }

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (evt.EventType == ExecutionEventType.RunStarted)
                {
                    Events.Clear();
                }

                var showTypes = new[]
                {
                    ExecutionEventType.PlanStepStarted,
                    ExecutionEventType.PlanStepCompleted,
                    ExecutionEventType.ToolCallStarted,
                    ExecutionEventType.ToolCallCompleted,
                    ExecutionEventType.ToolCallFailed
                };
                if (!showTypes.Contains(evt.EventType))
                {
                    return;
                }

                Events.Add(evt);
                RecalculateSummary();
            });
        }

        private void RecalculateSummary()
        {
            if (Events.Count == 0)
            {
                RunSummaryText = "尚未执行";
                ErrorSummaryText = string.Empty;
                HasFailures = false;
                return;
            }

            var allStepGroups = Events
                .Where(e => e.StepIndex != null)
                .GroupBy(e => e.StepIndex!.Value)
                .ToList();

            var executedGroups = allStepGroups
                .Where(g => g.Any(ev => ev.Succeeded != null))
                .ToList();

            var failedStepGroups = executedGroups
                .Where(g => g.Any(ev => ev.Succeeded == false))
                .ToList();
            var failedSteps = failedStepGroups.Count;

            HasFailures = failedSteps > 0;

            if (allStepGroups.Count == 0)
            {
                RunSummaryText = "尚未生成计划步骤";
            }
            else if (executedGroups.Count == 0)
            {
                RunSummaryText = $"共 {allStepGroups.Count} 个步骤，等待执行";
            }
            else
            {
                RunSummaryText = $"共 {allStepGroups.Count} 步，已完成 {executedGroups.Count - failedSteps}，失败 {failedSteps}";
            }

            if (failedSteps > 0)
            {
                var failedTitles = failedStepGroups
                    .Select(g => g.LastOrDefault(ev => !string.IsNullOrWhiteSpace(ev.Title))?.Title
                                 ?? $"步骤 {g.Key}")
                    .Take(3)
                    .ToList();

                var more = failedSteps > failedTitles.Count ? " ..." : string.Empty;
                ErrorSummaryText = "失败步骤: " + string.Join(" / ", failedTitles) + more;
            }
            else
            {
                ErrorSummaryText = string.Empty;
            }
        }

        private void UpdateFilter()
        {
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
