using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using TM.Framework.Common.ViewModels;

namespace TM.Modules.Design.Templates.OneClickGenerate.OneClickDirect
{
    public partial class OneClickGenerateViewModel : INotifyPropertyChanged, IAIGeneratingState
    {
        #region 断点续传持久化

        private static string GetPipelineStatePath()
            => StoragePathHelper.GetFilePath("Modules", "Design/Templates/OneClickGenerate/OneClickDirect", "pipeline_state.json");

        private void SavePipelineState()
        {
            try
            {
                var state = PipelineSteps.Select(s => new PipelineStepState
                {
                    StepIndex = s.StepIndex,
                    Status = s.Status.ToString(),
                    GeneratedCount = s.GeneratedCount,
                    TotalCount = s.TotalCount,
                    CategoryName = s.CategoryName,
                    Count = s.Count,
                    ExtraFields = s.ExtraFields.ToDictionary(f => f.Key, f => f.Value),
                }).ToList();

                var path = GetPipelineStatePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(state));
                OnPropertyChanged(nameof(StartButtonText));
            }
            catch (Exception ex)
            {
                TM.App.Log($"[OneClickGenerate] 保存管线状态失败: {ex.Message}");
            }
        }

        private async void LoadPipelineState()
        {
            try
            {
                var path = GetPipelineStatePath();
                if (!File.Exists(path)) return;

                var states = await Task.Run(async () =>
                {
                    var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                    return JsonSerializer.Deserialize<List<PipelineStepState>>(json);
                }).ConfigureAwait(true);
                if (states == null) return;

                var map = states.ToDictionary(s => s.StepIndex);
                foreach (var step in PipelineSteps)
                {
                    if (!map.TryGetValue(step.StepIndex, out var saved)) continue;

                    if (Enum.TryParse<StepStatus>(saved.Status, out var status))
                    {
                        step.Status = status == StepStatus.Running ? StepStatus.Cancelled : status;
                    }

                    if (!string.IsNullOrWhiteSpace(saved.CategoryName))
                        step.CategoryName = saved.CategoryName;

                    step.TotalCount = saved.TotalCount;
                    step.GeneratedCount = saved.GeneratedCount;
                    step.Count = saved.Count;

                    foreach (var field in step.ExtraFields)
                        if (saved.ExtraFields != null && saved.ExtraFields.TryGetValue(field.Key, out var val))
                            field.Value = val ?? string.Empty;
                }
                UpdateOverallProgress();
                OnPropertyChanged(nameof(StartButtonText));
                TM.App.Log($"[OneClickGenerate] 已恢复管线状态: {states.Count(s => s.Status == nameof(StepStatus.Completed))} 步已完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[OneClickGenerate] 加载管线状态失败: {ex.Message}");
            }
        }

        private static void ClearPipelineState()
        {
            try
            {
                var path = GetPipelineStatePath();
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[OneClickGenerate] 清除管线状态失败: {ex.Message}");
            }
        }

        #endregion

        private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
        {
            if (target is TM.Framework.Common.ViewModels.RangeObservableCollection<T> range)
            {
                range.ReplaceAll(items is IList<T> list ? list : items.ToList());
                return;
            }

            target.Clear();
            foreach (var item in items)
            {
                target.Add(item);
            }
        }

        private void AddLog(string iconKey, string message)
        {
            TM.App.Log($"[OneClickGenerate] [{iconKey}] {message}");
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                LogEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Icon = TM.Framework.Common.Helpers.IconHelper.TryGet(iconKey),
                    Message = message,
                });
            });
        }

    }
}
