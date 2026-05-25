using System;
using System.Linq;
using TM.Framework.UI.Workspace.RightPanel.Controls;
using TM.Services.Framework.AI.SemanticKernel;

namespace TM.Framework.UI.Workspace.RightPanel.Conversation
{
    public partial class SKConversationViewModel
    {
        #region 运行事件监控

        private void OnExecutionEvent(ExecutionEvent evt)
        {
            CheckEditPreviewActions(evt);

            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                var rt = evt.RunType;

                if (rt != RunType.Chat)
                {
                    if (rt == RunType.Task
                        && evt.EventType == ExecutionEventType.ToolCallStarted
                        && evt.RunId == _chatService.LastRunId
                        && !ShowTodoOverlay
                        && !IsReadOnlyToolCall(evt))
                    {
                        ShowTodoOverlay = true;
                    }

                    TodoPanelViewModel.OnExecutionEvent(evt);
                }

                if (evt.RunId == _chatService.LastRunId)
                {
                    if (evt.EventType == ExecutionEventType.RunStarted)
                    {
                        RunEvents.Clear();
                    }
                    RunEvents.Add(evt);
                    UpdateMonitorState(evt);
                }

                if (evt.RunId == _todoExecutionService.CurrentRunId)
                {
                    UpdateMonitorState(evt);
                }
            });
        }

        private void UpdateMonitorState(ExecutionEvent lastEvent)
        {
            var isExecutionEngineEvent = lastEvent.RunId != Guid.Empty
                && lastEvent.RunId == _todoExecutionService.CurrentRunId;

            switch (lastEvent.EventType)
            {
                case ExecutionEventType.RunStarted:
                    MonitorTitle = GetModeDisplayName(lastEvent.Mode);
                    MonitorSubTitle = isExecutionEngineEvent ? "执行中" : "运行";
                    _lastRunStatus = "Running";
                    break;
                case ExecutionEventType.RunCompleted:
                    MonitorSubTitle = isExecutionEngineEvent ? "结束" : "结束";
                    _lastRunStatus = "Completed";
                    break;
                case ExecutionEventType.RunFailed:
                    MonitorSubTitle = isExecutionEngineEvent ? "失败" : "失败";
                    _lastRunStatus = "Failed";
                    break;
            }

            OnPropertyChanged(nameof(CurrentModeActiveColor));
        }

        private void OnHighlightExecutionRequested(Guid runId, Guid? eventId)
        {
            if (runId != _chatService.LastRunId)
            {
                return;
            }

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                ExecutionEvent? targetEvent = null;
                if (eventId.HasValue && eventId.Value != Guid.Empty)
                {
                    targetEvent = RunEvents.FirstOrDefault(e => e.Id == eventId.Value);
                }

                targetEvent ??= RunEvents.FirstOrDefault(e => e.RunId == runId);

                if (targetEvent != null)
                {
                    SelectedRunEvent = targetEvent;
                }

                if (TodoPanelViewModel.Steps.Any())
                {
                    TodoStepViewModel? step = null;

                    if (eventId.HasValue && eventId.Value != Guid.Empty)
                    {
                        step = TodoPanelViewModel.Steps.FirstOrDefault(s => s.EventId == eventId.Value);
                    }

                    step ??= TodoPanelViewModel.Steps.FirstOrDefault(s => s.RunId == runId);

                    if (step != null)
                    {
                        TodoPanelViewModel.SelectedStep = step;
                    }
                }
            });
        }

        private void HighlightMessagesForRun(Guid runId)
        {
            if (runId == Guid.Empty || Messages.Count == 0)
            {
                return;
            }

            var target = Messages.FirstOrDefault(m => m.RunId == runId && m.IsAssistant)
                         ?? Messages.FirstOrDefault(m => m.RunId == runId && m.IsUser);

            if (target != null)
            {
                SelectedMessage = target;
            }
        }

        private static bool IsReadOnlyToolCall(ExecutionEvent evt)
        {
            if (evt.EventType != ExecutionEventType.ToolCallStarted
                && evt.EventType != ExecutionEventType.ToolCallCompleted
                && evt.EventType != ExecutionEventType.ToolCallFailed)
            {
                return false;
            }

            var plugin = evt.PluginName ?? string.Empty;
            var func = evt.FunctionName ?? string.Empty;

            if (string.Equals(plugin, "DataLookup", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(plugin, "System", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(func, "GetProjectInfo", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(func, "GetCurrentTime", StringComparison.OrdinalIgnoreCase);
            }

            if (string.Equals(plugin, "Workspace", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(func, "ListDirectory", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(func, "SearchFiles", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(func, "GrepInFiles", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(func, "ReadFileLines", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        #endregion
    }
}
