using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Framework.User.Account.Login.Bootstrap
{
    public class BootstrapManager
    {
        private readonly List<TaskBatch> _batches = new();
        private readonly List<IBootstrapTask> _allTasks = new();

        public event EventHandler<BootstrapProgressEventArgs>? ProgressChanged;

        public void AddTask(IBootstrapTask task)
        {
            _allTasks.Add(task);

            if (_batches.Count == 0 || _batches[^1].IsSealed)
            {
                _batches.Add(new TaskBatch());
            }
            _batches[^1].Tasks.Add(task);
        }

        public void AddParallelBatch(params IBootstrapTask[] tasks)
        {
            if (tasks.Length == 0) return;

            var batch = new TaskBatch { IsParallel = true };
            batch.Tasks.AddRange(tasks);
            batch.IsSealed = true;
            _batches.Add(batch);
            _allTasks.AddRange(tasks);
        }

        public void SealCurrentBatch()
        {
            if (_batches.Count > 0)
            {
                _batches[^1].IsSealed = true;
            }
        }

        public async Task ExecuteAllAsync()
        {
            TM.App.Log($"[BootstrapManager] 开始执行启动任务，共 {_allTasks.Count} 个任务，{_batches.Count} 个批次");

            int completedCount = 0;

            foreach (var batch in _batches)
            {
                if (batch.Tasks.Count == 0) continue;

                if (batch.IsParallel && batch.Tasks.Count > 1)
                {
                    TM.App.Log($"[BootstrapManager] 并行执行批次: {string.Join(", ", batch.Tasks.Select(t => t.Name))}");

                    ReportProgress(completedCount, batch.Tasks[0]);

                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var tasks = batch.Tasks.Select(async task =>
                    {
                        try
                        {
                            var taskWithTimeout = task.ExecuteAsync();
                            var completedTask = await Task.WhenAny(taskWithTimeout, Task.Delay(25000, cts.Token));

                            if (completedTask == taskWithTimeout)
                            {
                                await taskWithTimeout;
                                TM.App.Log($"[BootstrapManager] 任务完成: {task.Name}");
                            }
                            else
                            {
                                TM.App.Log($"[BootstrapManager] 任务超时: {task.Name}（已跳过）");
                            }
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        catch (Exception ex)
                        {
                            TM.App.Log($"[BootstrapManager] 任务失败: {task.Name} - {ex.Message}");
                        }
                    }).ToArray();

                    try
                    {
                        await Task.WhenAll(tasks);
                    }
                    finally
                    {
                        cts.Cancel();
                        cts.Dispose();
                    }

                    completedCount += batch.Tasks.Count;

                    ReportProgress(completedCount, batch.Tasks[^1]);
                }
                else
                {
                    foreach (var task in batch.Tasks)
                    {
                        ReportProgress(completedCount, task);
                        await Task.Yield();

                        TM.App.Log($"[BootstrapManager] [{completedCount + 1}/{_allTasks.Count}] 执行任务: {task.Name}");

                        try
                        {
                            await task.ExecuteAsync();
                            TM.App.Log($"[BootstrapManager] 任务完成: {task.Name}");
                        }
                        catch (Exception ex)
                        {
                            TM.App.Log($"[BootstrapManager] 任务失败: {task.Name} - {ex.Message}");
                        }

                        completedCount++;
                        ReportProgress(completedCount, task);
                    }
                }
            }

            TM.App.Log("[BootstrapManager] 所有启动任务执行完成");
        }

        private void ReportProgress(int completed, IBootstrapTask currentTask)
        {
            try
            {
                var args = new BootstrapProgressEventArgs
                {
                    CurrentTaskIndex = completed,
                    CompletedTasks = completed,
                    TotalTasks = _allTasks.Count,
                    CurrentTaskName = currentTask.Name,
                    CurrentTaskDescription = currentTask.Description
                };
                ProgressChanged?.Invoke(this, args);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[BootstrapManager] ProgressChanged通知失败: {ex.Message}");
            }
        }

        public void Clear()
        {
            _batches.Clear();
            _allTasks.Clear();
        }

        private class TaskBatch
        {
            public List<IBootstrapTask> Tasks { get; } = new();
            public bool IsParallel { get; set; }
            public bool IsSealed { get; set; }
        }
    }
}
