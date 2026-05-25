using System;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.UI.Workspace.RightPanel.Modes;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public enum ExecutionEventType
    {
        RunStarted,
        RunCompleted,
        RunFailed,
        UserMessage,
        AssistantMessage,
        ToolCallStarted,
        ToolCallCompleted,
        ToolCallFailed,
        PlanStepStarted,
        PlanStepCompleted,
        Info
    }

    public class ExecutionEvent
    {
        public Guid Id { get; set; } = ShortIdGenerator.NewGuid();

        public Guid RunId { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;

        public ChatMode Mode { get; set; } = ChatMode.Ask;

        public ExecutionEventType EventType { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? Detail { get; set; }

        public string? PluginName { get; set; }

        public string? FunctionName { get; set; }

        public int? StepIndex { get; set; }

        public bool? Succeeded { get; set; }
    }

    public static class ExecutionEventHub
    {
        public static event Action<ExecutionEvent>? Published;

        public static void Publish(ExecutionEvent evt)
        {
            Published?.Invoke(evt);
        }

        #region 便捷发布方法

        public static Guid NewRunId() => ShortIdGenerator.NewGuid();

        public static void PublishRunStarted(Guid runId, ChatMode mode, string title)
        {
            Publish(new ExecutionEvent
            {
                RunId = runId,
                Mode = mode,
                EventType = ExecutionEventType.RunStarted,
                Title = title
            });
        }

        public static void PublishRunCompleted(Guid runId, ChatMode mode, int completedCount, int failedCount)
        {
            Publish(new ExecutionEvent
            {
                RunId = runId,
                Mode = mode,
                EventType = ExecutionEventType.RunCompleted,
                Title = $"完成：{completedCount} 成功，{failedCount} 失败",
                Succeeded = failedCount == 0
            });
        }

        public static void PublishRunFailed(Guid runId, ChatMode mode, string reason)
        {
            Publish(new ExecutionEvent
            {
                RunId = runId,
                Mode = mode,
                EventType = ExecutionEventType.RunFailed,
                Title = reason,
                Succeeded = false
            });
        }

        public static void PublishRunCancelled(Guid runId, ChatMode mode)
        {
            Publish(new ExecutionEvent
            {
                RunId = runId,
                Mode = mode,
                EventType = ExecutionEventType.RunFailed,
                Title = "已取消",
                Succeeded = false
            });
        }

        public static void PublishStepStarted(Guid runId, ChatMode mode, int stepIndex, string title, string? detail = null)
        {
            Publish(new ExecutionEvent
            {
                RunId = runId,
                Mode = mode,
                EventType = ExecutionEventType.ToolCallStarted,
                StepIndex = stepIndex,
                Title = title,
                Detail = detail ?? title
            });
        }

        public static void PublishStepCompleted(Guid runId, ChatMode mode, int stepIndex, string title, string? result = null)
        {
            Publish(new ExecutionEvent
            {
                RunId = runId,
                Mode = mode,
                EventType = ExecutionEventType.ToolCallCompleted,
                StepIndex = stepIndex,
                Title = title,
                Detail = result ?? "完成",
                Succeeded = true
            });
        }

        public static void PublishStepFailed(Guid runId, ChatMode mode, int stepIndex, string title, string errorMessage)
        {
            Publish(new ExecutionEvent
            {
                RunId = runId,
                Mode = mode,
                EventType = ExecutionEventType.ToolCallFailed,
                StepIndex = stepIndex,
                Title = title,
                Detail = errorMessage,
                Succeeded = false
            });
        }

        public static void AssociateRunIdToMessages(UIMessageItem? userMessage, UIMessageItem? assistantMessage, Guid runId)
        {
            if (userMessage != null)
                userMessage.RunId = runId;
            if (assistantMessage != null)
                assistantMessage.RunId = runId;
        }

        #endregion
    }
}
