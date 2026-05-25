using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public class PlanModeFilter : IFunctionInvocationFilter
    {
        private static readonly object _lock = new();
        private static readonly Dictionary<Guid, int> _runStepCounters = new();
        public static event Func<FunctionInvocationContext, Task<bool>>? OnFunctionConfirmation;

        public static bool IsEnabled { get; set; } = false;

        private static int GetNextStepIndex(Guid runId)
        {
            lock (_lock)
            {
                if (!_runStepCounters.TryGetValue(runId, out var current))
                {
                    current = 0;
                }

                current++;
                _runStepCounters[runId] = current;
                return current;
            }
        }

        public static void ResetRun(Guid runId)
        {
            lock (_lock)
            {
                _runStepCounters.Remove(runId);
            }
        }

        public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
        {
            TM.App.Log($"[PlanModeFilter] 拦截函数调用: {context.Function.Name}");

            var chatService = ServiceLocator.Get<SKChatService>();
            var runId = chatService.LastRunId;
            var mode = chatService.CurrentMode;

            var stepIndex = GetNextStepIndex(runId);

            ExecutionEventHub.Publish(new ExecutionEvent
            {
                RunId = runId,
                Mode = mode,
                EventType = ExecutionEventType.ToolCallStarted,
                PluginName = context.Function.PluginName,
                FunctionName = context.Function.Name,
                StepIndex = stepIndex,
                Title = $"调用 {context.Function.PluginName}.{context.Function.Name}",
                Detail = context.Arguments?.ToString()
            });

            bool confirmed = true;
            if (IsEnabled && OnFunctionConfirmation != null)
            {
                try
                {
                    confirmed = await OnFunctionConfirmation(context);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[PlanModeFilter] 确认回调异常: {ex.Message}");
                    confirmed = false;
                }
            }

            if (confirmed)
            {
                TM.App.Log($"[PlanModeFilter] 用户确认执行: {context.Function.Name}");
                try
                {
                    await next(context);

                    ExecutionEventHub.Publish(new ExecutionEvent
                    {
                        RunId = runId,
                        Mode = mode,
                        EventType = ExecutionEventType.ToolCallCompleted,
                        PluginName = context.Function.PluginName,
                        FunctionName = context.Function.Name,
                        StepIndex = stepIndex,
                        Title = $"完成 {context.Function.PluginName}.{context.Function.Name}",
                        Detail = context.Result?.ToString(),
                        Succeeded = true
                    });
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[PlanModeFilter] 工具调用异常: {ex.Message}");
                    ExecutionEventHub.Publish(new ExecutionEvent
                    {
                        RunId = runId,
                        Mode = mode,
                        EventType = ExecutionEventType.ToolCallFailed,
                        PluginName = context.Function.PluginName,
                        FunctionName = context.Function.Name,
                        StepIndex = stepIndex,
                        Title = $"失败 {context.Function.PluginName}.{context.Function.Name}",
                        Detail = ex.Message,
                        Succeeded = false
                    });
                    throw;
                }
            }
            else
            {
                TM.App.Log($"[PlanModeFilter] 用户取消执行: {context.Function.Name}");
                context.Result = new FunctionResult(context.Function, "[用户取消了此操作]");

                ExecutionEventHub.Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = mode,
                    EventType = ExecutionEventType.ToolCallFailed,
                    PluginName = context.Function.PluginName,
                    FunctionName = context.Function.Name,
                    StepIndex = stepIndex,
                    Title = $"取消 {context.Function.PluginName}.{context.Function.Name}",
                    Detail = "[用户取消了此操作]",
                    Succeeded = false
                });
            }
        }
    }
}
