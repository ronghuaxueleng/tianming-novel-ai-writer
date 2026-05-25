using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.ViewModels;

namespace TM.Modules.Design.Templates.OneClickGenerate.OneClickDirect
{
    public partial class OneClickGenerateViewModel : INotifyPropertyChanged, IAIGeneratingState
    {
        #region 管线执行

        private async Task ExecutePipelineAsync()
        {
            IsPipelineRunning = true;
            OverallProgressPercent = 0;
            OverallProgressText = "准备开始...";

            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _pipelineForceRefreshedSteps.Clear();

            var mainVm = GetActiveMainWindowViewModel();
            if (mainVm == null)
            {
                AddLog("Icon.Forbidden", "无法获取主窗口 ViewModel，管线中止");
                IsPipelineRunning = false;
                StandardDialog.ShowError("无法获取主窗口 ViewModel，管线无法启动，请重启应用后重试。", "管线启动失败");
                return;
            }

            var totalSteps = PipelineSteps.Count;
            AddLog("Icon.Rocket", $"管线启动: 共 {totalSteps} 个步骤");

            var templateStep = PipelineSteps.FirstOrDefault(s => s.StepIndex == 1);
            if (templateStep != null)
            {
                var gcField = templateStep.ExtraFields.FirstOrDefault(f => f.Key == "GoldenChapter");
                if (gcField != null)
                    TM.Framework.UI.Workspace.Services.Spec.GoldenChapterConfig.Save(gcField.Value == "黄金三章");

                var genreField = templateStep.ExtraFields.FirstOrDefault(f => f.Key == "Genre");
                if (genreField != null && !string.IsNullOrWhiteSpace(genreField.Value))
                {
                    try
                    {
                        var promptRepo = ServiceLocator.TryGet<TM.Services.Framework.AI.Interfaces.Prompts.IPromptRepository>();
                        var specLoader = ServiceLocator.TryGet<TM.Framework.UI.Workspace.Services.Spec.SpecLoader>();
                        if (promptRepo != null && specLoader != null)
                        {
                            var specTemplate = promptRepo.GetTemplatesByCategory(genreField.Value)?
                                .Where(t => t.IsEnabled && !string.IsNullOrWhiteSpace(t.SystemPrompt))
                                .OrderByDescending(t => t.IsDefault)
                                .ThenByDescending(t => t.IsBuiltIn)
                                .FirstOrDefault();
                            if (specTemplate != null)
                            {
                                var spec = TM.Framework.Common.Helpers.AI.SpecTemplateParser.Parse(specTemplate.SystemPrompt, specTemplate.Name);
                                await specLoader.SaveProjectSpecAsync(spec);
                                specLoader.InvalidateCache();
                                AddLog("Icon.CheckCircle", $"已同步 Spec 题材: {genreField.Value} → {specTemplate.Name}");
                            }
                            else
                            {
                                AddLog("Icon.Warning", $"未找到题材 '{genreField.Value}' 对应的 Spec 模板，跳过同步");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[OneClickGenerate] Spec 题材同步失败: {ex.Message}");
                    }
                }
            }

            foreach (var preStep in PipelineSteps)
            {
                if (preStep.Status == StepStatus.Completed)
                    continue;

                if ((preStep.Status == StepStatus.Failed || preStep.Status == StepStatus.Cancelled)
                    && preStep.GeneratedCount > 0)
                    continue;

                preStep.TotalCount = preStep.ShowCountInput ? preStep.Count : 0;
                preStep.GeneratedCount = 0;
            }
            UpdateOverallProgress();

            try
            {
                for (int i = 0; i < totalSteps; i++)
                {
                    if (ct.IsCancellationRequested) break;

                    var step = PipelineSteps[i];
                    var stepNum = i + 1;

                    if (step.Status == StepStatus.Completed)
                    {
                        if (step.TotalCount > 0 && step.GeneratedCount < step.TotalCount)
                        {
                            step.Status = StepStatus.Failed;
                            AddLog("Icon.Warning", $"#{stepNum} {step.DisplayName}: 已完成状态与进度不一致（{step.GeneratedCount}/{step.TotalCount}），继续补齐");
                        }
                        else
                        {
                            AddLog("Icon.ChevronRight", $"#{stepNum} {step.DisplayName}: 已完成，跳过");
                            continue;
                        }
                    }

                    if (step.ShowCountInput && step.Count <= 0)
                    {
                        step.Status = StepStatus.Skipped;
                        AddLog("Icon.ChevronRight", $"跳过 #{stepNum} {step.DisplayName}: 数量=0");
                        SavePipelineState();
                        continue;
                    }

                    if (i > 0)
                    {
                        var prevFailed = PipelineSteps.Take(i).FirstOrDefault(s =>
                            s.Status == StepStatus.Failed || s.Status == StepStatus.Cancelled);
                        if (prevFailed != null)
                        {
                            for (int j = i; j < totalSteps; j++)
                            {
                                if (PipelineSteps[j].Status == StepStatus.Pending)
                                {
                                    PipelineSteps[j].Status = StepStatus.Skipped;
                                    AddLog("Icon.Forbidden", $"#{j + 1} {PipelineSteps[j].DisplayName}: 前置步骤『{prevFailed.DisplayName}』未完成，已跳过");
                                }
                            }
                            SavePipelineState();
                            break;
                        }
                    }

                    OverallProgressText = $"[{stepNum}/{totalSteps}] {step.DisplayName} | 加载模块...";
                    var isResumeStatus = step.Status == StepStatus.Failed || step.Status == StepStatus.Cancelled;
                    var isResuming = isResumeStatus && step.GeneratedCount > 0;
                    var isAutoExpandResuming = step.AutoExpandCategories && isResumeStatus;
                    step.Status = StepStatus.Running;
                    if (!isResuming)
                    {
                        step.TotalCount = step.ShowCountInput ? step.Count : 0;
                        step.GeneratedCount = 0;
                    }
                    UpdateOverallProgress();

                    AddLog("Icon.Refresh", $"开始 #{stepNum} {step.DisplayName}...");

                    var target = mainVm.GetOrEnsureViewModel<IPipelineBatchTarget>(step.Definition.ViewType);
                    if (target == null)
                    {
                        step.Status = StepStatus.Failed;
                        var vmErrMsg = $"模块『{step.DisplayName}』加载失败，无法获取 ViewModel。";
                        AddLog("Icon.Forbidden", $"#{stepNum} {step.DisplayName}: 无法获取 ViewModel，管线中止");
                        SavePipelineState();
                        ShowPipelineFailureDialog(vmErrMsg, $"管线中止 · #{stepNum} {step.DisplayName}");
                        break;
                    }

                    OverallProgressText = $"[{stepNum}/{totalSteps}] {step.DisplayName} | 准备数据...";

                    if (step.AutoExpandCategories)
                    {
                        try
                        {
                            var vds = ServiceLocator.Get<Modules.Generate.Elements.VolumeDesign.Services.VolumeDesignService>();
                            await vds.InitializeAsync();
                        }
                        catch (Exception ex)
                        {
                            TM.App.Log($"[OneClickGenerate] VolumeDesignService 预初始化异常: {ex.Message}");
                        }

                        var allCategories = target.GetCategoryNames();
                        if (allCategories.Count == 0)
                        {
                            AddLog("Icon.ChevronRight", $"#{stepNum} {step.DisplayName}: 暂无分类（前序步骤未生成），已跳过");
                            step.Status = StepStatus.Skipped;
                            SavePipelineState();
                            continue;
                        }

                        AddLog("Icon.Clipboard", $"#{stepNum} {step.DisplayName}: 共 {allCategories.Count} 个分类，逐卷生成");
                        step.TotalCount = allCategories.Count * 100;
                        bool stepFailed = false;
                        string? stepFailedReason = null;

                        for (int ci = 0; ci < allCategories.Count; ci++)
                        {
                            if (ct.IsCancellationRequested) break;
                            var catName = allCategories[ci];

                            bool backfillAborted = false;
                            int backfillRound = 0;
                            const int maxBackfillRounds = 3;
                            while (!ct.IsCancellationRequested)
                            {
                                List<string> incomplete;
                                try { incomplete = await target.GetIncompletePrerequisiteCategoriesAsync(catName); }
                                catch { incomplete = new List<string>(); }
                                if (incomplete.Count == 0) break;

                                backfillRound++;
                                if (backfillRound > maxBackfillRounds)
                                {
                                    var volList2 = string.Join("、", incomplete);
                                    AddLog("Icon.Forbidden", $"#{stepNum} {step.DisplayName}·{catName}: 前序补全已重试 {maxBackfillRounds} 轮仍未完成（{volList2}），中止");
                                    if (string.IsNullOrWhiteSpace(stepFailedReason))
                                        stepFailedReason = $"前序补全重试超限（{maxBackfillRounds}轮），仍未完成：{volList2}";
                                    backfillAborted = true;
                                    break;
                                }

                                var volList = string.Join("、", incomplete);
                                AddLog("Icon.Warning", $"#{stepNum} {step.DisplayName}·{catName}: 前序分卷未完成（{volList}），自动补全（第{backfillRound}/{maxBackfillRounds}轮）");

                                bool anyBfFailed = false;
                                foreach (var bfCat in incomplete)
                                {
                                    if (ct.IsCancellationRequested) break;
                                    AddLog("Icon.Refresh", $"#{stepNum} {step.DisplayName} → 补全 {bfCat}");
                                    OverallProgressText = $"[{stepNum}/{totalSteps}] {step.DisplayName} 补全 {bfCat}";

                                    var bfRequest = new PipelineBatchRequest
                                    {
                                        CategoryName = bfCat,
                                        Count = 1,
                                        PrefilledFields = step.BuildPrefilledFields(),
                                        IsResumeMode = isAutoExpandResuming,
                                    };
                                    var bfProgress = new Progress<string>(msg =>
                                    {
                                        if (TryParsePipelineProgressMessage(msg, out _, out _)) return;
                                        OverallProgressText = $"[{stepNum}/{totalSteps}] {step.DisplayName} 补全 {bfCat} | {msg}";
                                    });

                                    var bfResult = await target.ExecutePipelineBatchAsync(bfRequest, bfProgress, ct);
                                    if (ct.IsCancellationRequested) break;

                                    if (!bfResult.Success)
                                    {
                                        var errMsg = bfResult.ErrorMessage ?? string.Empty;
                                        var isKeyError = errMsg.Contains("授权", StringComparison.Ordinal)
                                                      || errMsg.Contains("功能受限", StringComparison.Ordinal)
                                                      || errMsg.Contains("所有密钥不可用", StringComparison.Ordinal)
                                                      || errMsg.Contains("API", StringComparison.OrdinalIgnoreCase)
                                                      || errMsg.Contains("key", StringComparison.OrdinalIgnoreCase);
                                        if (isKeyError)
                                        {
                                            AddLog("Icon.Forbidden", $"#{stepNum} 补全 {bfCat} 失败（Key/授权错误），管线中止: {errMsg}");
                                            if (string.IsNullOrWhiteSpace(stepFailedReason))
                                                stepFailedReason = errMsg;
                                            backfillAborted = true;
                                            stepFailed = true;
                                            break;
                                        }
                                        AddLog("Icon.Warning", $"#{stepNum} 补全 {bfCat} 未完全成功: {errMsg}，下轮重试");
                                        if (string.IsNullOrWhiteSpace(stepFailedReason))
                                            stepFailedReason = errMsg;
                                        anyBfFailed = true;
                                        continue;
                                    }

                                    AddLog("Icon.CheckCircle", $"#{stepNum} 补全 {bfCat}: 完成 ({bfResult.Duration.TotalSeconds:F1}s)");
                                    target.ConfirmAndEndAISessionForPipeline();
                                    _pipelineForceRefreshedSteps.Add(step.StepIndex);
                                    AddLog("Icon.Save", $"#{stepNum} 补全 {bfCat}: 已保存");
                                    await Task.Delay(200, ct);
                                }

                                if (backfillAborted || ct.IsCancellationRequested) break;
                                if (anyBfFailed && !incomplete.Any(c => !backfillAborted))
                                {
                                    stepFailed = true;
                                    break;
                                }
                            }

                            if (backfillAborted) { stepFailed = true; break; }
                            if (ct.IsCancellationRequested) break;

                            OverallProgressText = $"[{stepNum}/{totalSteps}] {step.DisplayName} ({ci + 1}/{allCategories.Count}) {catName}";
                            AddLog("Icon.Refresh", $"#{stepNum} {step.DisplayName} → {catName}");

                            var catRequest = new PipelineBatchRequest
                            {
                                CategoryName = catName,
                                Count = 1,
                                PrefilledFields = step.BuildPrefilledFields(),
                                IsResumeMode = isAutoExpandResuming,
                            };
                            var catIndex = ci;
                            var catTotal = allCategories.Count;
                            var catProgress = new Progress<string>(msg =>
                            {
                                if (TryParsePipelineProgressMessage(msg, out var gen, out var tot))
                                {
                                    step.TotalCount = catTotal * 100;
                                    step.GeneratedCount = catIndex * 100 + (tot > 0 ? (int)(gen * 100.0 / tot) : 0);
                                    UpdateOverallProgress();
                                    return;
                                }
                                OverallProgressText = $"[{stepNum}/{totalSteps}] {step.DisplayName}·{catName} | {msg}";
                            });

                            var catResult = await target.ExecutePipelineBatchAsync(catRequest, catProgress, ct);

                            if (ct.IsCancellationRequested) break;

                            if (!catResult.Success)
                            {
                                var catErrMsg = catResult.ErrorMessage ?? string.Empty;
                                var isCatKeyError = catErrMsg.Contains("所有密钥不可用", StringComparison.Ordinal)
                                                || catErrMsg.Contains("授权", StringComparison.Ordinal)
                                                || catErrMsg.Contains("功能受限", StringComparison.Ordinal);
                                if (isCatKeyError)
                                {
                                    AddLog("Icon.Forbidden", $"#{stepNum} {step.DisplayName}·{catName}: API密钥不可用，管线已中止");
                                    GlobalToast.Error("一键生成已停止", "所有API密钥均不可用，请在「模型管理」中检查密钥配置后重试。");
                                    if (string.IsNullOrWhiteSpace(stepFailedReason))
                                        stepFailedReason = catErrMsg;
                                    stepFailed = true;
                                    break;
                                }
                                stepFailed = true;
                                AddLog("Icon.Forbidden", $"#{stepNum} {step.DisplayName}·{catName}: {catErrMsg}，自动跳过");
                                if (string.IsNullOrWhiteSpace(stepFailedReason))
                                    stepFailedReason = catErrMsg;
                                continue;
                            }

                            if (catResult.GeneratedCount == 0 && catResult.Duration == TimeSpan.Zero)
                            {
                                AddLog("Icon.ChevronRight", $"#{stepNum} {step.DisplayName}·{catName}: 已全部完成，跳过");
                                step.GeneratedCount = (ci + 1) * 100;
                                step.TotalCount = allCategories.Count * 100;
                                UpdateOverallProgress();
                                continue;
                            }

                            AddLog("Icon.CheckCircle", $"#{stepNum} {step.DisplayName}·{catName}: 完成 ({catResult.Duration.TotalSeconds:F1}s)");

                            var catSaveOk = target.ConfirmAndEndAISessionForPipeline();
                            if (catSaveOk)
                            {
                                _pipelineForceRefreshedSteps.Add(step.StepIndex);
                                AddLog("Icon.Save", $"#{stepNum} {step.DisplayName}·{catName}: 已保存");
                            }
                            else
                                AddLog("Icon.ChevronRight", $"#{stepNum} {step.DisplayName}·{catName}: 用户取消保存");

                            step.GeneratedCount = (ci + 1) * 100;
                            step.TotalCount = allCategories.Count * 100;
                            UpdateOverallProgress();
                            await Task.Delay(200, ct);
                        }

                        if (ct.IsCancellationRequested) { step.Status = StepStatus.Cancelled; SavePipelineState(); break; }
                        if (!stepFailed && step.GeneratedCount == 0 && step.TotalCount > 0)
                            stepFailed = true;
                        step.Status = stepFailed ? StepStatus.Failed : StepStatus.Completed;
                        RefreshRemainingStepCategories(i + 1, mainVm);
                        SavePipelineState();
                        if (stepFailed)
                        {
                            AddLog("Icon.Forbidden", $"#{stepNum} {step.DisplayName}: 部分分类失败，管线中止");
                            ShowPipelineFailureDialog(
                                $"步骤『{step.DisplayName}』部分分类生成失败，管线已中止。" +
                                (string.IsNullOrWhiteSpace(stepFailedReason) ? "" : $"\n\n失败原因：\n{stepFailedReason}") +
                                "\n\n修复后可点击「继续生成」。",
                                $"管线中止 · #{stepNum} {step.DisplayName}");
                            break;
                        }
                        continue;
                    }

                    var request = new PipelineBatchRequest
                    {
                        CategoryName = step.CategoryName,
                        Count = step.Count,
                        PrefilledFields = step.BuildPrefilledFields(),
                        IsResumeMode = false,
                    };

                    var progress = new Progress<string>(msg =>
                    {
                        if (TryParsePipelineProgressMessage(msg, out var generated, out var total))
                        {
                            step.TotalCount = Math.Max(0, total);
                            step.GeneratedCount = Math.Max(0, generated);
                            UpdateOverallProgress();
                            return;
                        }
                        OverallProgressText = $"[{stepNum}/{totalSteps}] {step.DisplayName} | {msg}";
                    });
                    var result = await target.ExecutePipelineBatchAsync(request, progress, ct);

                    if (ct.IsCancellationRequested)
                    {
                        step.Status = StepStatus.Cancelled;
                        AddLog("Icon.Warning", $"#{stepNum} {step.DisplayName}: 已取消");
                        SavePipelineState();
                        break;
                    }

                    if (!result.Success)
                    {
                        step.Status = StepStatus.Failed;
                        AddLog("Icon.Forbidden", $"#{stepNum} {step.DisplayName}: {result.ErrorMessage}，管线中止");
                        SavePipelineState();
                        ShowPipelineFailureDialog(
                            $"步骤『{step.DisplayName}』生成失败：\n\n{result.ErrorMessage}\n\n修复后可点击「继续生成」从断点恢复。",
                            $"管线中止 · #{stepNum} {step.DisplayName}");
                        break;
                    }

                    if (result.GeneratedCount == 0 && result.Duration == TimeSpan.Zero)
                    {
                        if (step.TotalCount <= 0)
                            step.TotalCount = 1;
                        step.GeneratedCount = step.TotalCount;
                        UpdateOverallProgress();
                        AddLog("Icon.ChevronRight", $"#{stepNum} {step.DisplayName}: 已全部完成，跳过");
                        step.Status = StepStatus.Completed;
                        RefreshRemainingStepCategories(i + 1, mainVm);
                        SavePipelineState();
                        continue;
                    }

                    if (result.GeneratedCount > 0)
                    {
                        if (step.TotalCount <= 0 || result.GeneratedCount > step.TotalCount)
                            step.TotalCount = result.GeneratedCount;
                        step.GeneratedCount = result.GeneratedCount;
                    }
                    else
                    {
                        if (step.TotalCount <= 0)
                            step.TotalCount = step.ShowCountInput ? step.Count : 1;
                        step.GeneratedCount = step.TotalCount;
                    }
                    UpdateOverallProgress();
                    AddLog("Icon.CheckCircle", $"#{stepNum} {step.DisplayName}: 生成完成 (耗时 {result.Duration.TotalSeconds:F1}s)");

                    var saveOk = target.ConfirmAndEndAISessionForPipeline();
                    if (saveOk)
                    {
                        _pipelineForceRefreshedSteps.Add(step.StepIndex);
                        AddLog("Icon.Save", $"#{stepNum} {step.DisplayName}: 已保存");
                    }
                    else
                        AddLog("Icon.ChevronRight", $"#{stepNum} {step.DisplayName}: 用户取消保存");

                    step.Status = StepStatus.Completed;
                    RefreshRemainingStepCategories(i + 1, mainVm);
                    SavePipelineState();

                    await Task.Delay(200, ct);
                }
            }
            catch (OperationCanceledException)
            {
                AddLog("Icon.Warning", "管线已取消");
            }
            catch (Exception ex)
            {
                AddLog("Icon.Forbidden", $"管线异常: {ex.Message}");
                TM.App.Log($"[OneClickGenerate] Pipeline exception: {ex}");
                try { SavePipelineState(); } catch { }
                StandardDialog.ShowError($"管线执行中发生未知异常：\n\n{ex.Message}", "管线异常中止");
            }
            finally
            {
                var completedCount = PipelineSteps.Count(s => s.Status == StepStatus.Completed);
                var failedCount = PipelineSteps.Count(s => s.Status == StepStatus.Failed);
                var cancelledCount = PipelineSteps.Count(s => s.Status == StepStatus.Cancelled);
                var runningStep = PipelineSteps.FirstOrDefault(s => s.Status == StepStatus.Running);
                if (runningStep != null) runningStep.Status = StepStatus.Cancelled;
                SavePipelineState();
                UpdateOverallProgress();
                OverallProgressText = $"完成 {completedCount}/{totalSteps} 个模块";
                IsPipelineRunning = false;
                AddLog("Icon.CheckCircle", OverallProgressText);

                foreach (var cached in _stepTargetCache)
                {
                    if (_pipelineForceRefreshedSteps.Contains(cached.Key))
                        continue;

                    try { cached.Value.ForceRefreshTreeData(); }
                    catch (Exception ex) { TM.App.Log($"[OneClickGenerate] 刷新模块树失败: {ex.Message}"); }
                }

                try
                {
                    var panelComm = ServiceLocator.TryGet<TM.Framework.UI.Workspace.Services.PanelCommunicationService>();
                    panelComm?.PublishRefreshChapterList();
                }
                catch { }

                if (cancelledCount > 0 || ct.IsCancellationRequested)
                    GlobalToast.Warning("一键生成已取消", $"已完成 {completedCount}/{totalSteps} 个模块");
                else if (failedCount > 0)
                    GlobalToast.Warning("一键生成部分完成", $"完成 {completedCount}/{totalSteps} 个模块，{failedCount} 个失败");
                else if (completedCount == totalSteps)
                    GlobalToast.Success("一键生成全部完成", $"共 {totalSteps} 个模块已全部生成完毕");
                else
                    GlobalToast.Success("一键生成完成", $"已完成 {completedCount}/{totalSteps} 个模块");
            }
        }

        private static bool TryParsePipelineProgressMessage(string message, out int generatedCount, out int totalCount)
        {
            generatedCount = 0;
            totalCount = 0;

            const string prefix = "__PIPELINE_PROGRESS__|";
            if (string.IsNullOrWhiteSpace(message) || !message.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            var payload = message.Substring(prefix.Length).Split('|');
            if (payload.Length != 2)
                return false;

            return int.TryParse(payload[0], out generatedCount)
                && int.TryParse(payload[1], out totalCount);
        }

        private void UpdateOverallProgress()
        {
            var total = PipelineSteps.Count;
            if (total <= 0) { OverallProgressPercent = 0; return; }
            double progress = 0;
            foreach (var s in PipelineSteps)
            {
                if (s.Status == StepStatus.Completed || s.Status == StepStatus.Skipped)
                    progress += 1.0;
                else if (s.Status == StepStatus.Running && s.TotalCount > 0)
                    progress += (double)s.GeneratedCount / s.TotalCount;
            }
            OverallProgressPercent = (int)Math.Round(Math.Clamp(progress / total * 100d, 0d, 100d));
        }

        private void CancelPipeline()
        {
            _cts?.Cancel();
            AddLog("Icon.Warning", "正在取消...");
        }

        private void ShowPipelineFailureDialog(string message, string title)
        {
            if (_suppressFailureDialogThisRun)
            {
                GlobalToast.Warning(title, message.Length > 80 ? message[..80] + "..." : message);
                return;
            }

            try
            {
                var settings = ServiceLocator.Get<TM.Services.Framework.Settings.SettingsManager>();
                if (settings.Get(SuppressPipelineFailureDialogKey, false))
                {
                    _suppressFailureDialogThisRun = true;
                    GlobalToast.Warning(title, message.Length > 80 ? message[..80] + "..." : message);
                    return;
                }
            }
            catch { }

            var dialog = new StandardDialog();
            StandardDialog.EnsureOwnerAndTopmost(dialog, null);
            dialog.SetTitle(title);
            dialog.SetIcon("Icon.Forbidden");

            var fg = dialog.TryFindResource("TextPrimary") as System.Windows.Media.Brush;
            var panel = new System.Windows.Controls.StackPanel();

            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = message,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                FontSize = 14,
                MaxWidth = 520,
                Foreground = fg
            });

            var cbThisRun = new System.Windows.Controls.CheckBox
            {
                Content = "本次不再提示（重启后恢复）",
                Margin = new System.Windows.Thickness(0, 12, 0, 0),
                Foreground = fg
            };
            panel.Children.Add(cbThisRun);

            var cbRemember = new System.Windows.Controls.CheckBox
            {
                Content = "记住选择（下次启动也不再提示）",
                Margin = new System.Windows.Thickness(0, 6, 0, 0),
                Foreground = fg
            };
            panel.Children.Add(cbRemember);

            dialog.SetContent(new System.Windows.Controls.ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
                MaxHeight = 360
            });

            dialog.AddButton("确定", () => { dialog.Close(); }, true);
            dialog.ShowDialog();

            if (cbRemember.IsChecked == true)
            {
                try { ServiceLocator.Get<TM.Services.Framework.Settings.SettingsManager>().Set(SuppressPipelineFailureDialogKey, true); }
                catch { }
            }
            if (cbRemember.IsChecked == true || cbThisRun.IsChecked == true)
            {
                _suppressFailureDialogThisRun = true;
            }
        }

        #endregion
    }
}
