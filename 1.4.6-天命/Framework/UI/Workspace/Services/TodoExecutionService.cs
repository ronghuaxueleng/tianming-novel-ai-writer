using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Services;
using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Services.Framework.AI.SemanticKernel;

namespace TM.Framework.UI.Workspace.Services
{
    public sealed class TodoExecutionService
    {
        private readonly object _lock = new();
        private bool _isRunning;
        private Guid _currentRunId = Guid.Empty;
        private CancellationTokenSource? _cts;
        private Task? _runnerTask;

        public int MaxRetries { get; set; } = 1;

        private const int BaseRetryDelayMs = 2000;

        public TodoExecutionService() { }

        public bool IsRunning
        {
            get { lock (_lock) { return _isRunning; } }
        }

        public Guid CurrentRunId
        {
            get { lock (_lock) { return _currentRunId; } }
        }

        public Guid StartSequentialRun(ChatMode mode, IReadOnlyList<TodoExecutionTask> tasks, Guid? runId = null)
        {
            lock (_lock)
            {
                if (_isRunning)
                {
                    if (_runnerTask == null || _runnerTask.IsCompleted)
                    {
                        TM.App.Log("[TodoExecutionService] 检测到遗留运行态，已自动复位");
                        _isRunning = false;
                        _currentRunId = Guid.Empty;
                        _cts?.Dispose();
                        _cts = null;
                        _runnerTask = null;
                    }
                    else
                    {
                        TM.App.Log("[TodoExecutionService] 已有任务在执行");
                        return Guid.Empty;
                    }
                }
                _isRunning = true;
                var effectiveRunId = runId.HasValue && runId.Value != Guid.Empty
                    ? runId.Value
                    : ShortIdGenerator.NewGuid();
                _currentRunId = effectiveRunId;
                _cts = new CancellationTokenSource();
            }

            var currentRunId = _currentRunId;
            var ct = _cts.Token;

            _runnerTask = Task.Run(async () => await ExecuteAsync(currentRunId, mode, tasks, ct));

            var shortRunId = currentRunId.ToString("N").Substring(0, 8).ToUpper();
            TM.App.Log($"[TodoExecutionService] 启动 任务执行#{shortRunId}, 任务数={tasks.Count}");
            return currentRunId;
        }

        public void CancelCurrentRun()
        {
            lock (_lock)
            {
                if (_isRunning && _cts != null)
                {
                    _cts.Cancel();
                    TM.App.Log("[TodoExecutionService] 已请求取消");
                }
            }
        }

        private async Task ExecuteAsync(Guid runId, ChatMode mode, IReadOnlyList<TodoExecutionTask> tasks, CancellationToken ct)
        {
            try
            {
                Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = mode,
                    EventType = ExecutionEventType.RunStarted,
                    Title = $"开始执行 {tasks.Count} 个步骤"
                });

                for (int i = 0; i < tasks.Count; i++)
                {
                    var task = tasks[i];
                    var stepIndex = task.StepIndex > 0 ? task.StepIndex : (i + 1);
                    Publish(new ExecutionEvent
                    {
                        RunId = runId,
                        Mode = mode,
                        EventType = ExecutionEventType.ToolCallStarted,
                        StepIndex = stepIndex,
                        Title = task.Title,
                        Detail = "等待执行",
                        PluginName = task.PluginName,
                        FunctionName = task.FunctionName
                    });
                }

                int completed = 0, failed = 0;

                for (int i = 0; i < tasks.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var task = tasks[i];
                    var stepIndex = task.StepIndex > 0 ? task.StepIndex : (i + 1);

                    Publish(new ExecutionEvent
                    {
                        RunId = runId,
                        Mode = mode,
                        EventType = ExecutionEventType.ToolCallStarted,
                        StepIndex = stepIndex,
                        Title = task.Title,
                        Detail = task.Detail,
                        PluginName = task.PluginName,
                        FunctionName = task.FunctionName
                    });

                    var stepSucceeded = false;
                    for (int attempt = 0; attempt <= MaxRetries; attempt++)
                    {
                        ct.ThrowIfCancellationRequested();

                        try
                        {
                            if (attempt > 0)
                            {
                                var delay = BaseRetryDelayMs * attempt;
                                TM.App.Log($"[TodoExecutionService] 步骤 {stepIndex} 第{attempt}次重试，等待{delay}ms");
                                Publish(new ExecutionEvent
                                {
                                    RunId = runId,
                                    Mode = mode,
                                    EventType = ExecutionEventType.ToolCallStarted,
                                    StepIndex = stepIndex,
                                    Title = task.Title,
                                    Detail = $"重试中（第{attempt}次）",
                                    PluginName = task.PluginName,
                                    FunctionName = task.FunctionName
                                });
                                await Task.Delay(delay, ct);
                            }

                            string? result;
                            result = await task.ExecuteAsync!(ct);

                            Publish(new ExecutionEvent
                            {
                                RunId = runId,
                                Mode = mode,
                                EventType = ExecutionEventType.ToolCallCompleted,
                                StepIndex = stepIndex,
                                Title = task.Title,
                                Detail = result ?? "完成",
                                PluginName = task.PluginName,
                                FunctionName = task.FunctionName,
                                Succeeded = true
                            });
                            completed++;
                            stepSucceeded = true;
                            break;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex) when (attempt < MaxRetries && !ex.Message.Contains("已达到最大重写次数"))
                        {
                            TM.App.Log($"[TodoExecutionService] 步骤 {stepIndex} 第{attempt + 1}次尝试失败，将重试: {ex.Message}");
                        }
                        catch (Exception ex)
                        {
                            Publish(new ExecutionEvent
                            {
                                RunId = runId,
                                Mode = mode,
                                EventType = ExecutionEventType.ToolCallFailed,
                                StepIndex = stepIndex,
                                Title = task.Title,
                                Detail = ex.Message,
                                PluginName = task.PluginName,
                                FunctionName = task.FunctionName,
                                Succeeded = false
                            });
                            failed++;
                            TM.App.Log($"[TodoExecutionService] 步骤 {stepIndex} 经{MaxRetries + 1}次尝试仍失败，中断后续执行: {ex.Message}");
                        }
                    }

                    if (!stepSucceeded && failed > 0)
                        break;
                }

                ct.ThrowIfCancellationRequested();

                Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = mode,
                    EventType = ExecutionEventType.RunCompleted,
                    Title = $"完成：{completed} 成功，{failed} 失败",
                    Succeeded = failed == 0
                });
            }
            catch (OperationCanceledException)
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
            catch (Exception ex)
            {
                Publish(new ExecutionEvent
                {
                    RunId = runId,
                    Mode = mode,
                    EventType = ExecutionEventType.RunFailed,
                    Title = $"失败: {ex.Message}",
                    Succeeded = false
                });
            }
            finally
            {
                lock (_lock)
                {
                    _isRunning = false;
                    _currentRunId = Guid.Empty;
                    _cts?.Dispose();
                    _cts = null;
                    _runnerTask = null;
                }
            }
        }

        private static void Publish(ExecutionEvent evt)
        {
            ExecutionEventHub.Publish(evt);
        }
    }

    public class TodoExecutionTask
    {
        public int StepIndex { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string PluginName { get; set; } = string.Empty;
        public string FunctionName { get; set; } = string.Empty;
        public Func<CancellationToken, Task<string?>>? ExecuteAsync { get; set; }
    }
}
