using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TM.Framework.Common.Models;
using TM.Services.Framework.AI.Core;

namespace TM.Framework.Common.ViewModels
{
    public abstract partial class DataManagementViewModelBase<TData, TCategory, TService>
    {
        protected virtual async System.Threading.Tasks.Task ExecuteBatchAIGenerateAsync(BatchGenerationConfig config)
        {
            var vmName = GetType().Name;
            var isSingleMode = config.TotalCount == 1 && config.BatchSize == 1;

            TM.App.Log($"[{vmName}] 开始AI生成: 分类={config.CategoryName}, 总数={config.TotalCount}, 单批={config.BatchSize}, 单类模式={isSingleMode}");

            _lastBatchStoppedBySlotExhausted = false;
            _lastBatchKeyExhausted = false;
            _lastBatchWasCancelled = false;

            var activeConfig = _aiService.GetActiveConfiguration();
            if (activeConfig == null)
            {
                _lastBatchStoppedBySlotExhausted = true;
                _lastBatchKeyExhausted = true;
                if (!_isPipelineExecution)
                    GlobalToast.Error("未配置AI模型", "当前没有激活的AI模型，请前往\"智能助手 > 模型管理\"完成配置后重试。");
                TM.App.Log($"[{vmName}] AI生成阻断：未激活任何模型配置");
                return;
            }

            _batchCancellationTokenSource?.Dispose();
            _batchCancellationTokenSource = new System.Threading.CancellationTokenSource();
            var cancellationToken = _batchCancellationTokenSource.Token;

            var aiConfig = GetAIGenerationConfig();
            if (aiConfig != null)
            {
                await PrepareReferenceDataForAIGenerationAsync(aiConfig, isBatch: true, categoryName: config.CategoryName, cancellationToken);
            }

            int totalGenerated = 0;
            int totalFailed = 0;
            var estimatedBatches = config.EstimatedBatches;
            bool wasCancelled = false;

            _sessionDbNamesCache = new HashSet<string>(
                GetExistingNamesForDedup()
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => EntityNameNormalizeHelper.NormalizeBatchEntityName(n))
                    .Where(n => !string.IsNullOrWhiteSpace(n)),
                StringComparer.OrdinalIgnoreCase);
            if (_sessionDbNamesCache.Count > 0)
                TM.App.Log($"[{vmName}] DB存在名称缓存: {_sessionDbNamesCache.Count} 条（用于跨会话去重兜底）");

            var maxBatches = estimatedBatches + Math.Max(3, estimatedBatches / 3);

            var previousLatencyMode = System.Runtime.GCSettings.LatencyMode;
            try { System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency; }
            catch { }

            IsBatchGenerating = true;
            IsBatchCancelRequested = false;
            BatchProgressText = $"正在生成... (0/{estimatedBatches} 批)";
            _versionTrackingService.SuppressDownstreamToast = true;
            _skChatService.RegisterWorkspaceBatch(() =>
            {
                CancelBatchGeneration();
            });

            try
            {
                var moduleName = GetModuleNameForVersionTracking();
                Dictionary<string, int>? versionSnapshot = null;
                if (!string.IsNullOrEmpty(moduleName))
                {
                    versionSnapshot = _versionTrackingService.GetDependencySnapshot(moduleName);
                    TM.App.Log($"[{vmName}] dep snapshot: {moduleName}");
                }
                string? lastBatchError = null;
                int consecutiveFailures = 0;
                int totalFailedBatches = 0;
                var maxConsecutiveFailures = Math.Max(3, estimatedBatches / 4);
                var maxTotalFailedBatches = Math.Max(5, estimatedBatches / 2);
                bool consecutiveFailureStop = false;
                for (int batchIndex = 0; batchIndex < maxBatches; batchIndex++)
                {
                    if (IsBatchCancelRequested || cancellationToken.IsCancellationRequested)
                    {
                        wasCancelled = true;
                        _batchCancellationTokenSource?.Cancel();
                        TM.App.Log($"[{vmName}] 批量生成已取消，已完成 {batchIndex}/{maxBatches} 批");
                        break;
                    }

                    var remaining = config.TotalCount - totalGenerated;
                    if (remaining <= 0) break;
                    var batchCount = Math.Min(config.BatchSize, remaining);

                    var _batchNum = batchIndex + 1;
                    BatchProgressText = _batchNum > estimatedBatches
                        ? $"正在生成... (第{_batchNum}批/预计{estimatedBatches}批 · 去重补偿)"
                        : $"正在生成... ({_batchNum}/{estimatedBatches} 批)";

                    try
                    {
                        var batchResult = await GenerateBatchAsync(config.CategoryName, batchCount, cancellationToken);

                        cancellationToken.ThrowIfCancellationRequested();
                        var batchSlotCount = batchResult?.Count ?? 0;
                        if (_lastBatchKeyExhausted && batchSlotCount == 0)
                        {
                            OnBatchGenerationFailed(batchCount);
                            totalFailed += batchCount;
                            lastBatchError = "AI密钥或模型配置不可用";
                            _lastBatchStoppedBySlotExhausted = true;
                            TM.App.Log($"[{vmName}] AI密钥或模型配置不可用，终止后续批量生成");
                            break;
                        }

                        if (batchResult != null && batchResult.Count > 0)
                        {
                            int skippedByCoherence = 0;
                            batchResult = FilterBatchEntitiesByCoherence(batchResult, out skippedByCoherence);

                            if (skippedByCoherence > 0)
                            {
                                totalFailed += skippedByCoherence;
                                if (!_isPipelineExecution)
                                    GlobalToast.Warning("检测到硬冲突", $"已跳过 {skippedByCoherence} 个存在硬冲突的实体（未落库）");
                                else
                                    TM.App.Log($"[{vmName}] Pipeline: 检测到硬冲突，已跳过 {skippedByCoherence} 个实体");
                            }

                            var range = GetCurrentBatchRange();
                            if (range.HasValue)
                            {
                                batchResult = ValidateAndNormalizeGeneratedEntities(range.Value, batchResult);
                            }

                            NormalizeBatchEntityNames(batchResult);

                            if (IsNameDedupEnabled() && _batchGeneratedNames.Count > 0)
                            {
                                var beforeDedup = batchResult.Count;
                                var nameSet = new HashSet<string>(_batchGeneratedNames, StringComparer.OrdinalIgnoreCase);
                                batchResult = batchResult.Where(e =>
                                {
                                    if (e.TryGetValue("Name", out var n) && n is string ns && !string.IsNullOrWhiteSpace(ns))
                                    {
                                        if (nameSet.Contains(ns)) return false;
                                        if (TryStripNumericSuffix(ns, out var baseName) && nameSet.Contains(baseName))
                                            return false;
                                        return true;
                                    }
                                    return true;
                                }).ToList();
                                var skippedCross = beforeDedup - batchResult.Count;
                                if (skippedCross > 0)
                                    TM.App.Log($"[{vmName}] 跨批次去重: 跳过 {skippedCross} 个重名实体");
                            }

                            if (IsNameDedupEnabled() && _sessionDbNamesCache?.Count > 0)
                            {
                                var beforeDbDedup = batchResult.Count;
                                batchResult = batchResult.Where(e =>
                                {
                                    if (e.TryGetValue("Name", out var n) && n is string ns && !string.IsNullOrWhiteSpace(ns))
                                    {
                                        if (_sessionDbNamesCache.Contains(ns)) return false;
                                        if (TryStripNumericSuffix(ns, out var baseName) && _sessionDbNamesCache.Contains(baseName))
                                            return false;
                                        return true;
                                    }
                                    return true;
                                }).ToList();
                                var skippedDb = beforeDbDedup - batchResult.Count;
                                if (skippedDb > 0)
                                    TM.App.Log($"[{vmName}] DB存在名称过滤: 跳过 {skippedDb} 个已存在实体");
                            }

                            if (IsNameDedupEnabled())
                            {
                                var beforeIntra = batchResult.Count;
                                var seenInBatch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                batchResult = batchResult.Where(e =>
                                {
                                    if (e.TryGetValue("Name", out var n) && n is string ns && !string.IsNullOrWhiteSpace(ns))
                                        return seenInBatch.Add(ns);
                                    return true;
                                }).ToList();
                                var skippedIntra = beforeIntra - batchResult.Count;
                                if (skippedIntra > 0)
                                    TM.App.Log($"[{vmName}] 批内去重: 跳过 {skippedIntra} 个同批次重名实体");
                            }

                            var savedEntities = await SaveBatchEntitiesAsync(batchResult, config.CategoryName, versionSnapshot);

                            foreach (var entity in savedEntities)
                            {
                                if (entity.TryGetValue("Name", out var nameObj) && nameObj is string eName && !string.IsNullOrWhiteSpace(eName))
                                    _batchGeneratedNames.Add(eName);
                            }
                            AppendBatchIndexEntries(savedEntities, GetAIGenerationConfig());

                            cancellationToken.ThrowIfCancellationRequested();

                            totalGenerated += savedEntities.Count;
                            _pipelineLastGeneratedThisRunCount = totalGenerated;
                            _pipelineLastTotalCount = _pipelineProgressTargetCount > 0 ? _pipelineProgressTargetCount : config.TotalCount;
                            _pipelineLastGeneratedCount = Math.Min(_pipelineProgressBaseCount + totalGenerated, _pipelineLastTotalCount);
                            _pipelineBatchProgress?.Report($"__PIPELINE_PROGRESS__|{_pipelineLastGeneratedCount}|{_pipelineLastTotalCount}");
                            if (savedEntities.Count > 0)
                            {
                                consecutiveFailures = 0;
                                TM.App.Log($"[{vmName}] 第 {batchIndex + 1} 批完成: 生成={batchResult.Count}, 落库={savedEntities.Count}");
                                ScheduleImmediateRefreshTreeData();
                            }
                            else
                            {
                                TM.App.Log($"[{vmName}] 第 {batchIndex + 1} 批：AI返回 {batchSlotCount} 条但去重/校验后全部过滤，连续失败计数保持 {consecutiveFailures}");
                            }
                        }
                        else
                        {
                            OnBatchGenerationFailed(batchCount);
                            totalFailed += batchCount;
                            consecutiveFailures++;
                            totalFailedBatches++;
                            TM.App.Log($"[{vmName}] 第 {batchIndex + 1} 批失败: AI未返回有效数据（连续={consecutiveFailures}, 累计={totalFailedBatches}/{maxTotalFailedBatches}）");

                            if (consecutiveFailures >= maxConsecutiveFailures || totalFailedBatches >= maxTotalFailedBatches)
                            {
                                consecutiveFailureStop = true;
                                _lastBatchStoppedBySlotExhausted = true;
                                var stopReason = consecutiveFailures >= maxConsecutiveFailures
                                    ? $"连续失败 {consecutiveFailures} 次"
                                    : $"累计失败 {totalFailedBatches} 批";
                                BatchProgressText = $"{stopReason}，已停止";
                                TM.App.Log($"[{vmName}] 失败超限（连续={consecutiveFailures}/{maxConsecutiveFailures}, 累计={totalFailedBatches}/{maxTotalFailedBatches}），提前终止");
                                var emptyHint = totalGenerated > 0
                                    ? $"{stopReason}，已生成 {totalGenerated} 个，请检查后重试"
                                    : $"{stopReason}，请检查网络或模型配置后重试";
                                if (!_isPipelineExecution)
                                    GlobalToast.Warning("AI 请求失败", emptyHint);
                                break;
                            }
                        }

                        if (RequiresBatchSlotCompletion && batchSlotCount < batchCount)
                        {
                            if (!_lastBatchKeyExhausted && !_isPipelineExecution)
                                GlobalToast.Warning("批次未完成 · 已停止",
                                    $"重试次数已用完，当前批次完成 {batchSlotCount}/{batchCount}。\n" +
                                    $"已累计生成 {totalGenerated} 个，剩余可重新执行批量生成继续补全。");
                            TM.App.Log($"[{vmName}] 严格批次屏障：当前批未补齐（{batchSlotCount}/{batchCount}），终止后续批次，已累计 {totalGenerated}");
                            _lastBatchStoppedBySlotExhausted = true;
                            break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        wasCancelled = true;
                        TM.App.Log($"[{vmName}] 第 {batchIndex + 1} 批被取消");
                        break;
                    }
                    catch (Exception ex)
                    {
                        OnBatchGenerationFailed(batchCount);
                        totalFailed += batchCount;
                        lastBatchError = ex.Message;
                        consecutiveFailures++;
                        totalFailedBatches++;
                        TM.App.Log($"[{vmName}] 第 {batchIndex + 1} 批异常: {ex.Message}（连续={consecutiveFailures}, 累计={totalFailedBatches}/{maxTotalFailedBatches}）");

                        if (consecutiveFailures >= maxConsecutiveFailures || totalFailedBatches >= maxTotalFailedBatches)
                        {
                            consecutiveFailureStop = true;
                            _lastBatchStoppedBySlotExhausted = true;
                            var stopReason = consecutiveFailures >= maxConsecutiveFailures
                                ? $"连续失败 {consecutiveFailures} 次"
                                : $"累计失败 {totalFailedBatches} 批";
                            BatchProgressText = $"{stopReason}，已停止";
                            TM.App.Log($"[{vmName}] 失败超限（连续={consecutiveFailures}/{maxConsecutiveFailures}, 累计={totalFailedBatches}/{maxTotalFailedBatches}），提前终止");
                            var hint = totalGenerated > 0
                                ? $"{stopReason}，已生成 {totalGenerated} 个，请检查后重试"
                                : $"{stopReason}，请检查网络或模型配置后重试";
                            if (!_isPipelineExecution)
                                GlobalToast.Warning("AI 请求失败", hint);
                            break;
                        }
                    }
                }

                ScheduleImmediateRefreshTreeData();

                if (consecutiveFailureStop) return;

                if (wasCancelled)
                {
                    if (!_isPipelineExecution)
                        GlobalToast.Info("已取消", $"已生成 {totalGenerated} / 目标 {config.TotalCount}");
                    TM.App.Log($"[{vmName}] 批量AI生成已取消: 成功={totalGenerated}, 失败={totalFailed}");
                    return;
                }

                if (_lastBatchStoppedBySlotExhausted)
                {
                    TM.App.Log($"[{vmName}] 批量AI生成因槽位重试耗尽而停止: 成功={totalGenerated}, 失败={totalFailed}");
                    return;
                }

                if (!_isPipelineExecution)
                {
                    if (totalFailed == 0 && totalGenerated > 0)
                    {
                        GlobalToast.Success("生成完成", $"成功生成 {totalGenerated} 个实体");
                    }
                    else if (totalGenerated > 0 && totalFailed > 0)
                    {
                        GlobalToast.Info("部分成功", $"成功 {totalGenerated} 个，失败 {totalFailed} 个");
                    }
                    else if (totalGenerated == 0)
                    {
                        var errorHint = !string.IsNullOrWhiteSpace(lastBatchError)
                            ? $"AI调用失败：{TrimForToast(lastBatchError)}"
                            : "未能生成任何实体，请检查AI模型配置或提示词模板";
                        GlobalToast.Error("生成失败", errorHint);
                    }
                }

                TM.App.Log($"[{vmName}] 批量AI生成完成: 成功={totalGenerated}, 失败={totalFailed}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{vmName}] 批量AI生成异常: {ex.Message}");
                ScheduleImmediateRefreshTreeData();
                if (!_isPipelineExecution)
                    GlobalToast.Error("生成失败", $"生成失败：{TrimForToast(ex.Message)}");
                else
                    throw;
            }
            finally
            {
                IsBatchGenerating = false;
                _lastBatchWasCancelled = wasCancelled;
                IsBatchCancelRequested = false;
                BatchProgressText = string.Empty;
                _skChatService.UnregisterWorkspaceBatch();

                try
                {
                    _panelCommunicationService.PublishRefreshChapterList();
                }
                catch { }

                _batchCancellationTokenSource?.Dispose();
                _batchCancellationTokenSource = null;

                _versionTrackingService.SuppressDownstreamToast = false;
                _versionTrackingService.FlushPendingDownstreamNotifications(showToast: !_isPipelineExecution);

                try { System.Runtime.GCSettings.LatencyMode = previousLatencyMode; }
                catch { }
            }
        }

        protected virtual async System.Threading.Tasks.Task<List<Dictionary<string, object>>> GenerateBatchAsync(
            string categoryName, int count, System.Threading.CancellationToken cancellationToken)
        {
            var vmName = GetType().Name;

            if (IsBatchCancelRequested)
                throw new OperationCanceledException(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var config = GetAIGenerationConfig();
            var range = GetNextGenerationRange(categoryName, count);
            _currentBatchRange = range;

            var businessSessionKey = $"{vmName}_{categoryName}";
            _batchSessionHasHistory = _aiService.HasDirtyBusinessSession(businessSessionKey);

            var maxAttempts = RequiresBatchSlotCompletion ? 2 : 1;
            var final = new List<Dictionary<string, object>>();

            Func<System.Threading.Tasks.Task<string>>? initialContextProvider = null;
            if (!string.IsNullOrEmpty(_cachedBatchContextText))
            {
                var ctxSnapshot = _cachedBatchContextText;
                initialContextProvider = () => System.Threading.Tasks.Task.FromResult(ctxSnapshot);
            }
            else if (config?.ContextProvider != null)
            {
                initialContextProvider = config.ContextProvider;
            }

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsBatchCancelRequested) throw new OperationCanceledException(cancellationToken);

                var needed = count - final.Count;
                if (needed <= 0) break;

                if (range.HasValue)
                {
                    var start = range.Value.Start + final.Count;
                    var end = Math.Min(range.Value.End, start + needed - 1);
                    _currentBatchRange = new GenerationRange(start, end);
                }

                var prompt = await BuildBatchGenerationPromptAsync(categoryName, needed, cancellationToken);
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    TM.App.Log($"[{vmName}] 批量生成提示词为空，请检查 GetAIGenerationConfig/BatchFieldKeyMap/提示词模板配置");
                    break;
                }

                var batchProgress = new Progress<string>(msg =>
                {
                    BatchProgressText = $"{BatchProgressText?.Split('|')[0]?.Trim()} | {msg}";
                    _pipelineBatchProgress?.Report(msg);
                });

                var aiTask = _aiService.GenerateInBusinessSessionAsync(businessSessionKey, initialContextProvider, prompt, batchProgress, cancellationToken);
                _ = aiTask.ContinueWith(
                    t => { _ = t.Exception; },
                    System.Threading.CancellationToken.None,
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted,
                    System.Threading.Tasks.TaskScheduler.Default);
                var cancelTask = System.Threading.Tasks.Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
                var completed = await System.Threading.Tasks.Task.WhenAny(aiTask, cancelTask);
                if (!ReferenceEquals(completed, aiTask))
                    throw new OperationCanceledException(cancellationToken);

                var aiResult = await aiTask;
                if (!aiResult.Success || string.IsNullOrWhiteSpace(aiResult.Content))
                {
                    var errMsg = !string.IsNullOrWhiteSpace(aiResult.ErrorMessage)
                        ? aiResult.ErrorMessage
                        : (aiResult.Success ? "AI返回成功但正文为空（可能被中转站吞内容或推理模型仅产出思考）" : "AI未返回有效内容");
                    TM.App.Log($"[{vmName}] AI调用失败(attempt={attempt}): {errMsg}");
                    if (errMsg.Contains("所有密钥不可用", StringComparison.Ordinal)
                        || errMsg.Contains("没有激活的AI模型", StringComparison.Ordinal)
                        || errMsg.Contains("没有激活的 AI 模型", StringComparison.Ordinal)
                        || errMsg.Contains("没有可用密钥", StringComparison.Ordinal))
                    {
                        _lastBatchKeyExhausted = true;
                        break;
                    }
                    if (final.Count > 0)
                    {
                        TM.App.Log($"[{vmName}] 已累积 {final.Count} 条有效结果，中止重试并返回部分结果");
                        break;
                    }

                    if (RequiresBatchSlotCompletion && attempt < maxAttempts)
                    {
                        continue;
                    }

                    break;
                }

                var parsed = ParseBatchJsonResult(aiResult.Content);
                if (parsed.Count == 0)
                {
                    TM.App.Log($"[{vmName}] attempt={attempt}: AI未返回有效JSON，继续重试");
                    continue;
                }

                if (parsed.Count > needed) parsed = parsed.Take(needed).ToList();

                if (attempt < maxAttempts && IsBatchMissingFieldsTooHigh(parsed))
                {
                    TM.App.Log($"[{vmName}] attempt={attempt}: 字段缺失过多（>= {MissingFieldRetryThreshold:P0}），丢弃本次结果并重试");
                    continue;
                }

                EnsureRequiredFields(parsed);
                final.AddRange(parsed);

                if (final.Count >= count) break;

                if (attempt < maxAttempts)
                {
                    TM.App.Log($"[{vmName}] attempt={attempt}: 完成 {final.Count}/{count}，继续补齐剩余 {count - final.Count} 个槽位");
                    OnBatchRetrySlotTrimmed(parsed.Count);
                }
            }

            _currentBatchRange = range;

            if (final.Count != count)
                TM.App.Log($"[{vmName}] 批量生成部分完成：目标={count}，实际={final.Count}，{(RequiresBatchSlotCompletion ? "已耗尽重试次数" : "外层将补齐剩余")}");

            return final;
        }

        protected virtual bool RequiresBatchSlotCompletion => false;

        protected virtual void OnBatchRetrySlotTrimmed(int filledSoFar) { }

        private bool IsBatchMissingFieldsTooHigh(List<Dictionary<string, object>> entities)
        {
            if (entities == null || entities.Count == 0)
            {
                return true;
            }

            var config = GetAIGenerationConfig();
            if (config == null || config.OutputFields.Count == 0)
            {
                return false;
            }

            var required = config.OutputFields.Keys
                .Concat(new[] { "Name" })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            double sumRatio = 0;
            int counted = 0;

            foreach (var entity in entities)
            {
                if (entity == null)
                {
                    continue;
                }

                sumRatio += GetMissingRatio(entity, required);
                counted++;
            }

            if (counted == 0)
            {
                return true;
            }

            var avg = sumRatio / counted;
            return avg >= MissingFieldRetryThreshold;
        }

        private static double GetMissingRatio(Dictionary<string, object> entity, IReadOnlyList<string> requiredKeys)
        {
            if (requiredKeys.Count == 0)
            {
                return 0;
            }

            int missing = 0;
            foreach (var key in requiredKeys)
            {
                if (!entity.TryGetValue(key, out var v) || v == null)
                {
                    missing++;
                    continue;
                }

                if (v is string s && string.IsNullOrWhiteSpace(s))
                {
                    missing++;
                }
            }

            return (double)missing / requiredKeys.Count;
        }

        private void EnsureRequiredFields(List<Dictionary<string, object>> entities)
        {
            if (entities == null || entities.Count == 0)
            {
                return;
            }

            var config = GetAIGenerationConfig();
            if (config == null || config.OutputFields.Count == 0)
            {
                return;
            }

            var required = config.OutputFields.Keys
                .Concat(new[] { "Name" })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var entity in entities)
            {
                if (entity == null)
                {
                    continue;
                }

                foreach (var key in required)
                {
                    if (!entity.ContainsKey(key))
                    {
                        if (key.EndsWith("Number", StringComparison.OrdinalIgnoreCase) ||
                            key.EndsWith("Count", StringComparison.OrdinalIgnoreCase) ||
                            key.EndsWith("Index", StringComparison.OrdinalIgnoreCase) ||
                            key.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
                        {
                            entity[key] = 0;
                        }
                        else if (key.StartsWith("Is", StringComparison.OrdinalIgnoreCase) ||
                                 key.StartsWith("Has", StringComparison.OrdinalIgnoreCase) ||
                                 key.EndsWith("Enabled", StringComparison.OrdinalIgnoreCase))
                        {
                            entity[key] = false;
                        }
                        else if (key.EndsWith("List", StringComparison.OrdinalIgnoreCase) ||
                                 key.EndsWith("Tags", StringComparison.OrdinalIgnoreCase) ||
                                 key.EndsWith("Items", StringComparison.OrdinalIgnoreCase))
                        {
                            entity[key] = new List<string>();
                        }
                        else
                        {
                            entity[key] = string.Empty;
                        }
                    }
                }
            }
        }

        private static void NormalizeBatchEntityNames(List<Dictionary<string, object>> entities)
        {
            if (entities == null) return;
            foreach (var entity in entities)
            {
                if (entity != null && entity.TryGetValue("Name", out var nameObj) && nameObj is string nameStr)
                {
                    var cleaned = EntityNameNormalizeHelper.NormalizeBatchEntityName(nameStr);
                    if (!string.IsNullOrWhiteSpace(cleaned))
                        entity["Name"] = cleaned;
                }
            }
        }

        private static bool TryStripNumericSuffix(string name, out string baseName)
        {
            baseName = name;
            if (string.IsNullOrWhiteSpace(name)) return false;
            var t = name.Trim();
            var idx = t.LastIndexOf('_');
            if (idx <= 0 || idx >= t.Length - 1) return false;
            var suffix = t.Substring(idx + 1);
            if (!suffix.All(char.IsDigit)) return false;
            var stripped = t.Substring(0, idx).TrimEnd();
            if (string.IsNullOrWhiteSpace(stripped)) return false;
            baseName = stripped;
            return true;
        }

        protected virtual async System.Threading.Tasks.Task<string> BuildBatchGenerationPromptAsync(
            string categoryName,
            int count,
            System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsBatchCancelRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            var repo = GetPromptRepository();
            var config = GetAIGenerationConfig();

            var previousInfo = GetPreviousBatchInfo(categoryName);
            var previousInfoText = previousInfo?.ToContextString() ?? string.Empty;

            if (repo == null || config == null || string.IsNullOrWhiteSpace(config.Category))
            {
                GlobalToast.Warning("未接入新业务", "当前页面未提供AIGenerationConfig或提示词分类，已禁用旧业务批量生成fallback");
                return string.Empty;
            }

            if (config.BatchFieldKeyMap == null || config.BatchFieldKeyMap.Count == 0)
            {
                GlobalToast.Warning("未接入新业务", "当前页面未配置BatchFieldKeyMap，已禁用旧业务批量字段推导fallback");
                return string.Empty;
            }

            _cachedBatchContextText = string.Empty;
            string contextText = string.Empty;
            if (config.ContextProvider != null)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    contextText = await config.ContextProvider();
                    _cachedBatchContextText = contextText;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[{GetType().Name}] BuildBatchGenerationPromptAsync: 获取上下文失败 - {ex.Message}");
                    contextText = string.Empty;
                }
            }

            var templates = repo.GetTemplatesByCategory(config.Category);
            var enabled = templates.Where(t => t.IsEnabled).ToList();
            var candidates = enabled.Count > 0 ? enabled : templates.ToList();
            var singleTemplate = candidates
                .Where(t => !string.IsNullOrWhiteSpace(t.SystemPrompt))
                .OrderByDescending(t => t.IsDefault)
                .ThenByDescending(t => t.IsBuiltIn)
                .ThenByDescending(t => t.IsEnabled)
                .FirstOrDefault();

            if (singleTemplate == null || string.IsNullOrWhiteSpace(singleTemplate.SystemPrompt))
            {
                GlobalToast.Warning("提示词缺失", $"请先在「提示词管理」中配置「{config.Category}」分类的模板");
                return string.Empty;
            }

            var variableNames = SplitTemplateVariables(singleTemplate.Variables);
            var derived = BuildDerivedBatchPrompt(singleTemplate.SystemPrompt, variableNames, categoryName, count, contextText, previousInfoText,
                CoherenceEnabledCategories.Contains(config.Category));
            return derived;
        }

        protected virtual GenerationRange? GetNextGenerationRange(string categoryName, int requestedCount)
        {
            var config = GetAIGenerationConfig();
            if (config == null || string.IsNullOrWhiteSpace(config.SequenceFieldName) || config.GetCurrentMaxSequence == null)
            {
                return null;
            }

            var max = config.GetCurrentMaxSequence(categoryName);
            var start = Math.Max(1, max + 1);
            var end = Math.Max(start, start + Math.Max(1, requestedCount) - 1);
            return new GenerationRange(start, end);
        }

        protected virtual string ApplyGenerationRangeToPrompt(string prompt, GenerationRange range)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return prompt;
            }

            var config = GetAIGenerationConfig();
            var seqField = config?.SequenceFieldName;
            var appendix = new StringBuilder();
            appendix.AppendLine();
            appendix.AppendLine("<batch_range_constraint mandatory=\"true\">");
            appendix.AppendLine($"本批次续写范围：{range.Start}-{range.End}");
            if (!string.IsNullOrWhiteSpace(seqField))
            {
                appendix.AppendLine($"要求：{seqField} 必须严格落在 {range.Start}-{range.End} 且不重复");
            }
            appendix.AppendLine("</batch_range_constraint>");

            return prompt + appendix;
        }

        protected virtual List<Dictionary<string, object>> ValidateAndNormalizeGeneratedEntities(
            GenerationRange range,
            List<Dictionary<string, object>> entities)
        {
            if (entities == null || entities.Count == 0)
            {
                return entities ?? new List<Dictionary<string, object>>();
            }

            var config = GetAIGenerationConfig();
            var seqField = config?.SequenceFieldName;
            if (string.IsNullOrWhiteSpace(seqField))
            {
                return entities;
            }

            var used = new HashSet<int>();
            int next = range.Start;

            foreach (var entity in entities)
            {
                if (entity == null)
                {
                    continue;
                }

                int candidate = 0;
                try
                {
                    var reader = new TM.Framework.Common.Services.BatchEntityReader(entity);
                    candidate = reader.GetInt(seqField);
                }
                catch
                {
                    candidate = 0;
                }

                bool ok = candidate >= range.Start && candidate <= range.End && !used.Contains(candidate);
                if (!ok)
                {
                    while (next <= range.End && used.Contains(next))
                    {
                        next++;
                    }

                    candidate = next <= range.End ? next : range.End;
                }

                used.Add(candidate);
                entity[seqField] = candidate;
                if (candidate == next)
                {
                    next++;
                }
            }

            return entities;
        }

        private string BuildDerivedBatchPrompt(
            string singlePrompt,
            IReadOnlyList<string> variableNames,
            string categoryName,
            int count,
            string contextText,
            string previousInfoText,
            bool enableCoherence)
        {
            var config = GetAIGenerationConfig();
            List<string> fieldNames;

            if (config?.BatchFieldKeyMap != null && config.OutputFields.Count > 0)
            {
                fieldNames = config.OutputFields.Keys.ToList();
                if (!fieldNames.Contains("Name", StringComparer.OrdinalIgnoreCase))
                {
                    fieldNames.Insert(0, "Name");
                }
            }
            else
            {
                throw new InvalidOperationException("未配置 BatchFieldKeyMap，已禁用旧业务批量字段推导 fallback");
            }
            if (enableCoherence)
            {
                if (!fieldNames.Contains(BatchCoherenceConflictKey, StringComparer.Ordinal))
                    fieldNames = fieldNames.Concat(new[] { BatchCoherenceConflictKey }).ToList();
                if (!fieldNames.Contains(BatchCoherenceConflictPointKey, StringComparer.Ordinal))
                    fieldNames = fieldNames.Concat(new[] { BatchCoherenceConflictPointKey }).ToList();
            }
            var fieldsText = string.Join(", ", fieldNames.Select(f => $"\"{f}\""));

            var normalizedSinglePrompt = Helpers.AI.SystemPromptTrimHelper.Trim(singlePrompt, config?.ActiveModuleHint);
            var variableValues = new Dictionary<string, string>(StringComparer.Ordinal);
            if (config?.InputVariables != null)
            {
                foreach (var (varName, getValue) in config.InputVariables)
                {
                    if (string.IsNullOrWhiteSpace(varName))
                    {
                        continue;
                    }

                    try
                    {
                        variableValues[varName] = getValue?.Invoke() ?? string.Empty;
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[{GetType().Name}] BuildDerivedBatchPrompt: 获取输入变量失败 - {varName}: {ex.Message}");
                        variableValues[varName] = string.Empty;
                    }
                }
            }

            foreach (var varName in variableNames)
            {
                if (string.IsNullOrWhiteSpace(varName))
                {
                    continue;
                }

                if (variableValues.TryGetValue(varName, out var variableValue) && !string.IsNullOrWhiteSpace(variableValue))
                {
                    normalizedSinglePrompt = normalizedSinglePrompt.Replace($"{{{varName}}}", variableValue);
                }
                else
                {
                    normalizedSinglePrompt = normalizedSinglePrompt.Replace($"{{{varName}}}", $"<自动生成:{varName}>");
                }
            }
            normalizedSinglePrompt = normalizedSinglePrompt.Replace("{上下文数据}", string.Empty);

            var sb = new StringBuilder();
            sb.AppendLine("<batch_task>");
            sb.AppendLine("你将执行批量生成任务。");
            sb.AppendLine($"目标分类：{categoryName}");
            sb.AppendLine($"本批次生成数量：{count}");

            sb.AppendLine();
            sb.AppendLine("<mandate critical=\"true\">");
            sb.AppendLine($"输出协议：仅输出JSON数组，长度严格等于 {count}，每项必须包含字段 {fieldsText}。禁止Markdown/代码块/额外文字。");
            sb.AppendLine("</mandate>");

            if (!_batchSessionHasHistory && !string.IsNullOrWhiteSpace(previousInfoText))
            {
                sb.AppendLine();
                sb.AppendLine("<previous_batch_info>");
                sb.AppendLine(previousInfoText);
                sb.AppendLine("</previous_batch_info>");
            }

            if (!_batchSessionHasHistory)
            {
                if (!string.IsNullOrWhiteSpace(contextText))
                {
                    sb.AppendLine();
                    sb.AppendLine("<context_reference>");
                    sb.AppendLine("完整背景设定已在 system message 中提供，请直接参考。");
                    sb.AppendLine("</context_reference>");
                }

                if (_batchGeneratedIndex.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("<generated_index note=\"已生成条目核心属性摘要，用于保持内容分布一致性，勿重复\">");
                    sb.AppendLine(string.Join("\n", _batchGeneratedIndex));
                    sb.AppendLine("</generated_index>");
                }
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("<session_continuation>");
                sb.AppendLine("完整背景设定已在会话初始化时提供，此处不重复。");
                if (_batchGeneratedIndex.Count > 0)
                {
                    sb.AppendLine("<generated_index note=\"已生成条目核心属性摘要，用于保持内容分布一致性，勿重复\">");
                    sb.AppendLine(string.Join("\n", _batchGeneratedIndex));
                    sb.AppendLine("</generated_index>");
                }
                else
                {
                    sb.AppendLine("<session_note>本地分布摘要已重置，请直接参考对话历史中已生成的内容保持风格与分布一致性。</session_note>");
                    if (_batchGeneratedNames.Count > 0)
                        sb.AppendLine($"本会话内仍追踪的名称（勿重复）：{string.Join("、", _batchGeneratedNames)}");
                }
                sb.AppendLine("</session_continuation>");
            }

            sb.AppendLine();
            sb.AppendLine("<single_gen_spec note=\"仅用于理解内容要求；输出格式以output_requirements为准\">");
            sb.AppendLine(normalizedSinglePrompt);
            if (variableNames.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("说明：单次规范中的 <自动生成:...> 代表原本的模板变量占位符。批量生成时，这些值不由外部输入提供，请为每个对象自行生成合理内容。");
            }
            sb.AppendLine("</single_gen_spec>");

            sb.AppendLine();
            sb.AppendLine("<output_requirements mandatory=\"true\">");
            sb.AppendLine("1. 只输出一个有效的JSON数组（不要Markdown、不要代码块、不要额外解释文本）。");
            sb.AppendLine($"2. 数组长度必须严格等于 {count}。");
            sb.AppendLine("3. 每个对象必须至少包含以下字段：");
            sb.AppendLine(fieldsText);
            if (_batchGeneratedNames.Count > 0)
            {
                sb.AppendLine($"4. Name 字段必须有区分度，且严禁与以下已生成的 Name 重复：{string.Join("、", _batchGeneratedNames)}");
            }
            else
            {
                sb.AppendLine("4. Name 字段必须有区分度，避免重复。");
            }
            sb.AppendLine("5. 所有字段值必须是字符串，不要用数组或嵌套对象。多项内容请在字符串内换行。");
            sb.AppendLine("</output_requirements>");

            if (enableCoherence)
            {
                sb.AppendLine();
                sb.AppendLine("<coherence_requirements mandatory=\"true\">");
                sb.AppendLine($"- {BatchCoherenceConflictKey}: \"是\" 或 \"否\"（仅当你确认存在硬冲突时才填\"是\"）");
                sb.AppendLine($"- {BatchCoherenceConflictPointKey}: 若 {BatchCoherenceConflictKey}=\"是\"，必须填写具体冲突点；否则填\"无\"");
                sb.AppendLine("</coherence_requirements>");
            }

            sb.AppendLine();
            sb.AppendLine("<output_example note=\"仅示意字段结构，内容请按单次规范生成\">");
            sb.AppendLine("[");
            sb.AppendLine("  {");
            for (int i = 0; i < fieldNames.Count; i++)
            {
                var field = fieldNames[i];
                var comma = i == fieldNames.Count - 1 ? string.Empty : ",";
                sb.AppendLine($"    \"{field}\": \"...\"{comma}");
            }
            sb.AppendLine("  }");
            sb.AppendLine("]");
            sb.AppendLine("</output_example>");
            sb.AppendLine();
            sb.AppendLine($"<final_count_check>输出的 JSON 数组对象数量必须精确等于 {count}，不能多也不能少。</final_count_check>");
            sb.AppendLine("</batch_task>");

            var prompt = sb.ToString();
            if (_currentBatchRange.HasValue)
            {
                prompt = ApplyGenerationRangeToPrompt(prompt, _currentBatchRange.Value);
            }

            return prompt;
        }

        protected virtual ModuleNormalizationConfig? GetNormalizationConfig() => null;

        protected virtual System.Threading.Tasks.Task ResolveEntityReferencesBeforeSaveAsync()
            => System.Threading.Tasks.Task.CompletedTask;

        protected string NormalizeFieldValue(string fieldName, string rawValue)
        {
            var config = GetNormalizationConfig();
            if (config == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return rawValue;
            }

            var rule = config.Rules.FirstOrDefault(r => string.Equals(r.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));
            if (rule == null)
            {
                return rawValue;
            }

            var candidates = GetNormalizationCandidates(rule);
            if (candidates.Count == 0)
            {
                if (rule.Type == NormalizationType.DynamicList)
                {
                    return rawValue;
                }

                return rule.AllowEmpty ? string.Empty : rule.DefaultValue;
            }

            var normalized = EntityNameNormalizeHelper.FilterToCandidate(rawValue, candidates);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }

            if (rule.LogWarning && !string.IsNullOrWhiteSpace(rawValue))
            {
                TM.App.Log($"[{config.ModuleName}] 字段 {fieldName} 归一化失败（未知实体已丢弃）。原始值: {rawValue}");
            }

            return rule.AllowEmpty ? string.Empty : rule.DefaultValue;
        }

        private static List<string> GetNormalizationCandidates(FieldNormalizationRule rule)
        {
            List<string> candidates = rule.Type switch
            {
                NormalizationType.StaticOptions => rule.StaticOptions?.ToList() ?? new List<string>(),
                NormalizationType.DynamicList => rule.DynamicOptionsProvider?.Invoke() ?? new List<string>(),
                _ => new List<string>()
            };

            return candidates
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<Dictionary<string, object>> FilterBatchEntitiesByCoherence(
            List<Dictionary<string, object>> entities,
            out int skippedByCoherence)
        {
            skippedByCoherence = 0;
            if (entities == null)
                return new List<Dictionary<string, object>>();
            if (entities.Count == 0)
                return entities;

            var filtered = new List<Dictionary<string, object>>(entities.Count);

            foreach (var entity in entities)
            {
                if (entity == null)
                {
                    continue;
                }

                if (!entity.TryGetValue(BatchCoherenceConflictKey, out var conflictObj))
                {
                    filtered.Add(entity);
                    continue;
                }

                bool isConflict = conflictObj is bool b
                    ? b
                    : string.Equals(conflictObj?.ToString()?.Trim(), "是", StringComparison.Ordinal);

                if (!isConflict)
                {
                    filtered.Add(entity);
                    continue;
                }

                if (!entity.TryGetValue(BatchCoherenceConflictPointKey, out var pointObj))
                {
                    filtered.Add(entity);
                    continue;
                }

                var point = pointObj?.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(point) || string.Equals(point, "无", StringComparison.Ordinal))
                {
                    filtered.Add(entity);
                    continue;
                }

                if (!IsHighConfidenceConflictPoint(point))
                {
                    filtered.Add(entity);
                    continue;
                }

                skippedByCoherence++;
            }

            return filtered;
        }

        private static bool IsHighConfidenceConflictPoint(string point)
        {
            if (string.IsNullOrWhiteSpace(point))
                return false;

            var domainKeywords = new[]
            {
                "世界", "规则", "设定", "角色", "能力", "关系", "时间", "时间线", "剧情", "冲突", "伏笔"
            };

            var conflictKeywords = new[]
            {
                "冲突", "矛盾", "违背", "不一致"
            };

            return conflictKeywords.Any(k => point.Contains(k, StringComparison.Ordinal))
                   && domainKeywords.Any(k => point.Contains(k, StringComparison.Ordinal));
        }

        private static IReadOnlyList<string> SplitTemplateVariables(string? variables)
        {
            if (string.IsNullOrWhiteSpace(variables))
            {
                return Array.Empty<string>();
            }

            return variables
                .Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        protected virtual PreviousBatchInfo? GetPreviousBatchInfo(string categoryName)
        {
            return null;
        }

        protected virtual IEnumerable<string> GetExistingNamesForDedup() => Enumerable.Empty<string>();

        protected virtual bool IsNameDedupEnabled() => true;

        protected virtual int GetDefaultTotalCount() => GetDefaultBatchSize();

        protected int GetDefaultBatchSize()
        {
            try
            {
                var aiService = _cachedAIService ??= TM.Framework.Common.Services.ServiceLocator.Get<AIService>();
                var tier = aiService.GetActiveConfiguration()?.BatchTier ?? "64K";
                return tier switch
                {
                    "32K" => GetBaseBatchSize(),
                    "128K" => GetBatchSize128K(),
                    _ => GetBatchSize64K()
                };
            }
            catch
            {
                return GetBatchSize64K();
            }
        }

        protected virtual int GetBaseBatchSize() => 10;

        protected virtual int GetBatchSize64K() => GetBaseBatchSize();

        protected virtual int GetBatchSize128K() => GetBaseBatchSize();

        protected virtual void OnBatchGenerationFailed(int failedCount) { }

        protected virtual System.Threading.Tasks.Task<List<Dictionary<string, object>>> SaveBatchEntitiesAsync(
            List<Dictionary<string, object>> entities,
            string categoryName,
            Dictionary<string, int>? versionSnapshot)
        {
            TM.App.Log($"[{GetType().Name}] SaveBatchEntitiesAsync: 子类未重写，无法保存 {entities.Count} 个实体");
            return System.Threading.Tasks.Task.FromResult(new List<Dictionary<string, object>>());
        }

    }
}

