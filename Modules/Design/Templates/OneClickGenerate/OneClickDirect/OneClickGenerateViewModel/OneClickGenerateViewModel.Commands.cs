using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using TM.Framework.Common.ViewModels;
using TM.Framework.UI.Windows;
using TM.Services.Framework.SystemIntegration;
using TM.Framework.UI.Workspace.Services;

namespace TM.Modules.Design.Templates.OneClickGenerate.OneClickDirect
{
    public partial class OneClickGenerateViewModel : INotifyPropertyChanged, IAIGeneratingState
    {
        #region 命令

        public ICommand StartPipelineCommand { get; }
        public ICommand CancelPipelineCommand { get; }
        public ICommand ResetPipelineCommand { get; }

        #endregion

        private CancellationTokenSource? _cts;

        public ICommand LoadCategoriesCommand { get; }

        public OneClickGenerateViewModel()
        {
            foreach (var def in StepDefinitions)
            {
                PipelineSteps.Add(new PipelineStepViewModel(def));
            }

            StartPipelineCommand = new AsyncRelayCommand(ExecutePipelineAsync, CanStartPipeline);
            CancelPipelineCommand = new RelayCommand(CancelPipeline, () => IsPipelineRunning);
            ResetPipelineCommand = new RelayCommand(_ => ResetPipeline(), () => !IsPipelineRunning);
            LoadCategoriesCommand = new RelayCommand(LoadAllCategories);

            foreach (var step in PipelineSteps)
            {
                var capturedStep = step;
                step.PropertyChanged += (_, e) =>
                {
                    RaiseCanStartChanged();
                    if (e.PropertyName == nameof(PipelineStepViewModel.CategoryName))
                        OnStepCategoryChanged(capturedStep);
                };
                foreach (var field in step.ExtraFields)
                    field.PropertyChanged += (_, __) => RaiseCanStartChanged();
            }
        }

        private readonly Dictionary<int, IPipelineBatchTarget> _stepTargetCache = new();
        private readonly HashSet<int> _pipelineForceRefreshedSteps = new();
        private bool _pipelineStateLoaded;

        private UnifiedWindowViewModel? GetActiveMainWindowViewModel()
        {
            var app = System.Windows.Application.Current;
            if (app?.MainWindow?.DataContext is UnifiedWindowViewModel mainWindowVm)
                return mainWindowVm;

            return app?.Windows
                .OfType<UnifiedWindow>()
                .FirstOrDefault(w => w.IsLoaded)
                ?.DataContext as UnifiedWindowViewModel;
        }

        private void LoadAllCategories()
        {
            var mainVm = GetActiveMainWindowViewModel();
            if (mainVm == null) return;

            foreach (var step in PipelineSteps)
            {
                try
                {
                    var target = mainVm.GetOrEnsureViewModel<IPipelineBatchTarget>(step.Definition.ViewType);
                    if (target == null) continue;

                    _stepTargetCache[step.StepIndex] = target;

                    var isSingle = target.IsPipelineSingleMode;
                    var hideCount = step.Definition.HideCount;
                    step.ShowCountInput = !isSingle && !hideCount;
                    step.ShowCategoryInput = !step.AutoExpandCategories && !step.Definition.HideCategory;

                    var names = target.GetCategoryNames();
                    ReplaceCollection(step.AvailableCategories, names);

                    if (names.Count > 0
                        && (string.IsNullOrWhiteSpace(step.CategoryName) || !names.Contains(step.CategoryName)))
                    {
                        step.CategoryName = names[0];
                    }

                    var fieldOptions = target.GetExtraFieldOptions();
                    foreach (var field in step.ExtraFields)
                    {
                        if (fieldOptions.TryGetValue(field.Key, out var options) && options.Count > 0)
                        {
                            ReplaceCollection(field.AvailableOptions, options);
                        }
                        else
                        {
                            field.AvailableOptions.Clear();
                        }
                        field.RefreshHasOptions();
                    }

                    if (!string.IsNullOrWhiteSpace(step.CategoryName))
                        ApplyPrefilledDefaults(step, target);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[OneClickGenerate] 加载 {step.DisplayName} 分类失败: {ex.Message}");
                }
            }
            if (!_pipelineStateLoaded)
            {
                _pipelineStateLoaded = true;
                LoadPipelineState();
            }
        }

        private void OnStepCategoryChanged(PipelineStepViewModel step)
        {
            if (string.IsNullOrWhiteSpace(step.CategoryName)) return;
            if (!_stepTargetCache.TryGetValue(step.StepIndex, out var target)) return;
            ApplyPrefilledDefaults(step, target);
        }

        private void ApplyPrefilledDefaults(PipelineStepViewModel step, IPipelineBatchTarget target)
        {
            try
            {
                var defaults = target.GetPrefilledFieldDefaults(step.CategoryName);
                foreach (var field in step.ExtraFields)
                {
                    if (defaults.TryGetValue(field.Key, out var val) && !string.IsNullOrWhiteSpace(val))
                        field.Value = val;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[OneClickGenerate] 预填字段加载失败 {step.DisplayName}: {ex.Message}");
            }
        }

        private void RefreshRemainingStepCategories(int fromIndex, UnifiedWindowViewModel mainVm)
        {
            for (int j = fromIndex; j < PipelineSteps.Count; j++)
            {
                var nextStep = PipelineSteps[j];
                try
                {
                    var nextTarget = mainVm.GetOrEnsureViewModel<IPipelineBatchTarget>(nextStep.Definition.ViewType);
                    if (nextTarget == null) continue;

                    _stepTargetCache[nextStep.StepIndex] = nextTarget;
                    var names = nextTarget.GetCategoryNames();
                    ReplaceCollection(nextStep.AvailableCategories, names);

                    if (names.Count > 0
                        && (string.IsNullOrWhiteSpace(nextStep.CategoryName) || !names.Contains(nextStep.CategoryName)))
                    {
                        nextStep.CategoryName = names[0];
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[OneClickGenerate] 刷新 {nextStep.DisplayName} 分类失败: {ex.Message}");
                }
            }
        }

        private bool CanStartPipeline()
        {
            if (IsPipelineRunning) return false;

            foreach (var step in PipelineSteps)
            {
                if (step.Status == StepStatus.Completed || step.Status == StepStatus.Skipped)
                    continue;

                if (step.ShowCountInput && step.Count <= 0)
                    continue;

                if (step.ShowCategoryInput && string.IsNullOrWhiteSpace(step.CategoryName))
                    return false;

                foreach (var field in step.ExtraFields)
                {
                    if (field.IsRequired && string.IsNullOrWhiteSpace(field.Value))
                        return false;
                }
            }
            return true;
        }

        private void RaiseCanStartChanged()
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                System.Windows.Input.CommandManager.InvalidateRequerySuggested,
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private void ResetPipeline()
        {
            bool includeBookAnalysis = StandardDialog.ShowConfirm(
                "是否同时清理【拆书分析】数据？\n\n" +
                "• 确认 → 拆书 + 设计 + 创作（全量清理）\n" +
                "• 取消 → 仅设计 + 创作（保留拆书数据）",
                "选择清理范围");

            var scopeText = includeBookAnalysis
                ? "拆书 + 设计 + 创作（全量）"
                : "仅设计 + 创作（保留拆书）";
            if (!StandardDialog.ShowConfirm(
                $"即将清理：{scopeText}\n\n此操作不可恢复，确定要重置吗？",
                "确认刷新重置"))
                return;

            try
            {
                var (success, clearedCount, details) = BusinessCleanupService.Execute(includeBookAnalysis);

                foreach (var step in PipelineSteps)
                {
                    step.Status = StepStatus.Pending;
                    step.TotalCount = 0;
                    step.GeneratedCount = 0;
                    step.Count = 0;
                    foreach (var field in step.ExtraFields)
                        field.Value = string.Empty;
                }
                ClearPipelineState();
                OnPropertyChanged(nameof(StartButtonText));

                OverallProgressPercent = 0;
                OverallProgressText = "等待开始...";
                LogEntries.Clear();
                _pipelineForceRefreshedSteps.Clear();
                _stepTargetCache.Clear();
                _pipelineStateLoaded = false;

                try
                {
                    ServiceLocator.TryGet<PanelCommunicationService>()?.PublishBusinessDataCleared();
                }
                catch { }

                LoadAllCategories();

                var firstError = details.Find(d => d.Contains("失败(", StringComparison.Ordinal));
                var msg = success
                    ? (clearedCount > 0 ? $"已清除 {clearedCount} 条业务数据，状态已重置" : "当前无业务数据，状态已重置")
                    : (string.IsNullOrWhiteSpace(firstError)
                        ? "清除过程中遇到问题，状态已部分重置"
                        : $"清除过程中遇到问题，状态已部分重置：{firstError}");

                if (success)
                    GlobalToast.Success("刷新重置完成", msg);
                else
                    GlobalToast.Warning("刷新重置部分完成", msg);

                TM.App.Log($"[OneClickGenerate] 刷新重置完成，清除 {clearedCount} 条数据");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[OneClickGenerate] 刷新重置异常: {ex.Message}");
                GlobalToast.Error("重置失败", $"重置失败：{ex.Message}");
            }
        }

    }
}
