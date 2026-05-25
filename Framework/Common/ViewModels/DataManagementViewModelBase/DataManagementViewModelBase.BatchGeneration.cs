using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Models;
using TM.Services.Framework.AI.Interfaces.Prompts;

namespace TM.Framework.Common.ViewModels
{
    public abstract partial class DataManagementViewModelBase<TData, TCategory, TService>
    {
        #region IPipelineBatchTarget 实现

        public string PipelineModuleName => GetType().Name;

        public List<string> GetCategoryNames()
        {
            try
            {
                return GetAllCategories()
                    .Where(c => c.IsEnabled)
                    .Select(c => c.Name)
                    .ToList();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] GetCategoryNames 异常: {ex.Message}");
                return new List<string>();
            }
        }

        public bool IsPipelineSingleMode => !SupportsBatch(null!) || IsBatchGenerationDisabledForCurrentModule();

        public int GetPipelineDefaultCount() => GetDefaultTotalCount();

        public virtual Dictionary<string, string> GetPrefilledFieldDefaults(string categoryName)
        {
            return new Dictionary<string, string>();
        }

        public virtual Dictionary<string, List<string>> GetExtraFieldOptions()
        {
            return new Dictionary<string, List<string>>();
        }

        public virtual System.Threading.Tasks.Task<List<string>> GetIncompletePrerequisiteCategoriesAsync(string categoryName)
        {
            return System.Threading.Tasks.Task.FromResult(new List<string>());
        }

        public bool ConfirmAndEndAISessionForPipeline()
        {
            EndBusinessSessionAndResetBatchNames();
            ForceRefreshTreeData();
            return true;
        }

        public async System.Threading.Tasks.Task<PipelineBatchResult> ExecutePipelineBatchAsync(
            PipelineBatchRequest request,
            IProgress<string>? progress,
            System.Threading.CancellationToken cancellationToken)
        {
            var vmName = GetType().Name;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            _isPipelineExecution = true;
            _isPipelineResume = request.IsResumeMode;
            IsAIGenerating = true;

            try
            {
                var node = FindCategoryNodeByName(request.CategoryName);
                if (node == null)
                {
                    var msg = $"未找到分类节点: {request.CategoryName}";
                    TM.App.Log($"[{vmName}] Pipeline: {msg}");
                    return new PipelineBatchResult { Success = false, ErrorMessage = msg };
                }
                SetSelectedCategoryNodeForBatch(node);
                if (!string.Equals(GetCurrentCategoryValue(), request.CategoryName, StringComparison.Ordinal))
                {
                    _suppressCategorySelectionSync = true;
                    try { ApplyCategorySelection(request.CategoryName); }
                    finally { _suppressCategorySelectionSync = false; }
                }

                if (request.PrefilledFields != null && request.PrefilledFields.Count > 0)
                    ApplyPrefilledFields(request.PrefilledFields);

                bool? authResult = null;
                for (int authRetry = 0; authRetry < 3; authRetry++)
                {
                    authResult = await ProtectionService.CheckFeatureAuthorizationAsync(AIFeatureId);
                    if (authResult != null) break;
                    TM.App.Log($"[{vmName}] Pipeline: 功能授权检查未响应，第{authRetry + 1}次重试...");
                    await System.Threading.Tasks.Task.Delay(1000, cancellationToken);
                }
                if (authResult == null)
                    return new PipelineBatchResult { Success = false, ErrorMessage = "网络异常，无法验证功能授权（已重试3次）" };
                if (authResult == false)
                    return new PipelineBatchResult { Success = false, ErrorMessage = "功能受限，订阅计划不支持此功能" };

                BatchGenerationConfig? config;
                var isPreCalculatedModule = IsPipelinePreCalculatedModule();
                var desiredTotalCount = 0;
                var existingBeforeCount = 0;
                if (isPreCalculatedModule)
                {
                    config = await ShowBatchGenerationDialogAsync(request.CategoryName, singleMode: false);
                    if (config == null)
                    {
                        return new PipelineBatchResult { Success = false, ErrorMessage = "批量配置被取消或预计算失败" };
                    }
                    if (config.TotalCount <= 0)
                    {
                        TM.App.Log($"[{vmName}] Pipeline: 分类 '{request.CategoryName}' 已全部完成，跳过生成");
                        return new PipelineBatchResult { Success = true, GeneratedCount = 0, Duration = TimeSpan.Zero };
                    }
                }
                else
                {
                    var isSingleMode = !SupportsBatch(node) || IsBatchGenerationDisabledForCurrentModule();
                    var batchSize = isSingleMode ? 1 : GetDefaultBatchSize();
                    desiredTotalCount = isSingleMode ? 1 : Math.Max(1, request.Count);
                    existingBeforeCount = CountPipelineExistingItems(request.CategoryName);
                    var remainingCount = Math.Max(0, desiredTotalCount - existingBeforeCount);
                    if (remainingCount <= 0)
                    {
                        TM.App.Log($"[{vmName}] Pipeline: 分类 '{request.CategoryName}' 已达目标 {existingBeforeCount}/{desiredTotalCount}，跳过生成");
                        return new PipelineBatchResult { Success = true, GeneratedCount = 0, Duration = TimeSpan.Zero };
                    }
                    config = new BatchGenerationConfig
                    {
                        CategoryName = request.CategoryName,
                        TotalCount = remainingCount,
                        BatchSize = Math.Max(1, Math.Min(batchSize, remainingCount)),
                    };
                }

                TM.App.Log($"[{vmName}] Pipeline: 开始批量生成, 分类={request.CategoryName}, 总数={config.TotalCount}, 单批={config.BatchSize}");

                _pipelineBatchProgress = progress;
                _pipelineProgressBaseCount = isPreCalculatedModule ? 0 : existingBeforeCount;
                _pipelineProgressTargetCount = isPreCalculatedModule ? config.TotalCount : desiredTotalCount;
                _pipelineLastGeneratedCount = _pipelineProgressBaseCount;
                _pipelineLastGeneratedThisRunCount = 0;
                _pipelineLastTotalCount = _pipelineProgressTargetCount;
                _pipelineBatchProgress?.Report($"__PIPELINE_PROGRESS__|{_pipelineProgressBaseCount}|{_pipelineProgressTargetCount}");

                SetSelectedCategoryNodeForBatch(node);
                if (!string.Equals(GetCurrentCategoryValue(), request.CategoryName, StringComparison.Ordinal))
                {
                    _suppressCategorySelectionSync = true;
                    try { ApplyCategorySelection(request.CategoryName); }
                    finally { _suppressCategorySelectionSync = false; }
                }

                using var pipelineCancelReg = cancellationToken.Register(() => CancelBatchGeneration());
                await ExecuteBatchAIGenerateAsync(config);

                sw.Stop();
                TM.App.Log($"[{vmName}] Pipeline: 批量生成完成, 耗时={sw.ElapsedMilliseconds}ms");
                if (_lastBatchStoppedBySlotExhausted)
                {
                    var remainingAfterStop = Math.Max(0, _pipelineProgressTargetCount - _pipelineLastGeneratedCount);
                    var stopErr = _lastBatchKeyExhausted
                        ? "AI密钥或模型配置不可用，请在模型管理中检查API密钥和激活模型"
                        : $"批次重试耗尽，当前完成 {_pipelineLastGeneratedCount}/{_pipelineProgressTargetCount}，剩余 {remainingAfterStop} 个未生成";
                    return new PipelineBatchResult
                    {
                        Success = false,
                        GeneratedCount = _pipelineLastGeneratedCount,
                        Duration = sw.Elapsed,
                        ErrorMessage = stopErr,
                    };
                }

                if (_lastBatchWasCancelled)
                {
                    return new PipelineBatchResult
                    {
                        Success = false,
                        GeneratedCount = _pipelineLastGeneratedCount,
                        Duration = sw.Elapsed,
                        ErrorMessage = $"批量生成被取消，当前完成 {_pipelineLastGeneratedCount}/{_pipelineProgressTargetCount}",
                    };
                }

                if (_pipelineLastGeneratedThisRunCount == 0 && config.TotalCount > 0)
                {
                    return new PipelineBatchResult
                    {
                        Success = false,
                        GeneratedCount = _pipelineLastGeneratedCount,
                        Duration = sw.Elapsed,
                        ErrorMessage = "未生成任何内容，请检查AI模型配置或网络连接",
                    };
                }

                if (_pipelineLastGeneratedCount < _pipelineProgressTargetCount)
                {
                    var missingCount = _pipelineProgressTargetCount - _pipelineLastGeneratedCount;
                    return new PipelineBatchResult
                    {
                        Success = false,
                        GeneratedCount = _pipelineLastGeneratedCount,
                        Duration = sw.Elapsed,
                        ErrorMessage = $"目标未完成：目标 {_pipelineProgressTargetCount}，当前 {_pipelineLastGeneratedCount}，缺失 {missingCount}。请检查AI输出、网络或模型稳定性后继续生成",
                    };
                }

                return new PipelineBatchResult
                {
                    Success = true,
                    GeneratedCount = _pipelineLastGeneratedCount,
                    Duration = sw.Elapsed,
                };
            }
            catch (OperationCanceledException)
            {
                TM.App.Log($"[{vmName}] Pipeline: 批量生成已取消");
                return new PipelineBatchResult { Success = false, GeneratedCount = _pipelineLastGeneratedCount, ErrorMessage = "已取消" };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{vmName}] Pipeline: 批量生成异常 - {ex.Message}");
                return new PipelineBatchResult { Success = false, GeneratedCount = _pipelineLastGeneratedCount, ErrorMessage = ex.Message };
            }
            finally
            {
                _pipelineBatchProgress = null;
                _pipelineLastGeneratedThisRunCount = 0;
                _pipelineProgressBaseCount = 0;
                _pipelineProgressTargetCount = 0;
                _isPipelineExecution = false;
                _isPipelineResume = false;
                IsAIGenerating = false;
            }
        }

        private int CountPipelineExistingItems(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName)) return 0;

            var allData = GetAllDataItems();
            var categoryId = GetAllCategoriesFromService()
                .FirstOrDefault(c => string.Equals(c.Name, categoryName, StringComparison.Ordinal))?.Id;

            if (!string.IsNullOrWhiteSpace(categoryId))
            {
                return allData.Count(d =>
                    GetDataIsEnabled(d) &&
                    (!string.IsNullOrWhiteSpace(d.CategoryId) && string.Equals(d.CategoryId, categoryId, StringComparison.Ordinal)) ||
                    (GetDataIsEnabled(d) && string.IsNullOrWhiteSpace(d.CategoryId) && string.Equals(GetDataCategory(d), categoryName, StringComparison.Ordinal)));
            }

            return allData.Count(d => GetDataIsEnabled(d) && string.Equals(GetDataCategory(d), categoryName, StringComparison.Ordinal));
        }

        protected virtual void ApplyPrefilledFields(Dictionary<string, string> fields)
        {
        }

        private TreeNodeItem? FindCategoryNodeByName(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName)) return null;
            var found = FindCategoryNodeRecursive(TreeData, categoryName);
            if (found != null) return found;

            var allCats = GetAllCategories();
            var cat = allCats?.FirstOrDefault(c =>
                string.Equals(c.Name, categoryName, StringComparison.OrdinalIgnoreCase));
            if (cat == null) return null;

            return new TreeNodeItem
            {
                Name = cat.Name,
                Tag = cat,
                Icon = IconHelper.TryGet(cat.Icon) ?? IconHelper.TryGet("Icon.Folder"),
            };
        }

        private TreeNodeItem? FindCategoryNodeRecursive(IEnumerable<TreeNodeItem> nodes, string name)
        {
            foreach (var node in nodes)
            {
                if (node.Tag is TCategory && string.Equals(node.Name, name, StringComparison.OrdinalIgnoreCase))
                    return node;
                var found = FindCategoryNodeRecursive(node.Children, name);
                if (found != null) return found;
            }
            return null;
        }

        #endregion

        private void AppendBatchIndexEntries(List<Dictionary<string, object>> entities, AIGenerationConfig? config)
        {
            if (config?.BatchIndexFields == null || config.BatchIndexFields.Count == 0) return;
            foreach (var entity in entities)
            {
                var parts = config.BatchIndexFields
                    .Select(f => entity.TryGetValue(f, out var v) ? v?.ToString()?.Trim() ?? string.Empty : string.Empty)
                    .ToList();
                if (parts.Any(p => !string.IsNullOrWhiteSpace(p)))
                {
                    _batchGeneratedIndex.Add(string.Join("|", parts));
                    if (_batchGeneratedIndex.Count > 30)
                        _batchGeneratedIndex.RemoveAt(0);
                }
            }
        }

        private System.Threading.CancellationTokenSource? _singleCancellationTokenSource;

        private bool _isBatchGenerating;
        protected override System.Windows.Threading.DispatcherPriority RefreshDispatcherPriority =>
            _isBatchGenerating
                ? System.Windows.Threading.DispatcherPriority.ApplicationIdle
                : System.Windows.Threading.DispatcherPriority.Background;

        public bool IsBatchGenerating
        {
            get => _isBatchGenerating;
            protected set
            {
                if (_isBatchGenerating != value)
                {
                    _isBatchGenerating = value;
                    OnPropertyChanged();
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                        System.Windows.Input.CommandManager.InvalidateRequerySuggested,
                        System.Windows.Threading.DispatcherPriority.Background);
                    try
                    {
                        MemoryOptimizationService.SetGeneratingState(value);
                        if (value)
                            ServiceLocator.Get<MemoryOptimizationService>().NotifyUserActivity();
                    }
                    catch { }
                }
            }
        }

        private string _batchProgressText = string.Empty;
        public string BatchProgressText
        {
            get => _batchProgressText;
            protected set
            {
                if (_batchProgressText != value)
                {
                    _batchProgressText = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _aiGenerateButtonText = "AI生成";
        public string AIGenerateButtonText
        {
            get => _aiGenerateButtonText;
            set
            {
                if (_aiGenerateButtonText != value)
                {
                    _aiGenerateButtonText = value;
                    OnPropertyChanged();
                }
            }
        }

        protected virtual bool SupportsBatch(TreeNodeItem categoryNode)
        {
            return true;
        }

        public void SetSelectedCategoryNodeForBatch(TreeNodeItem? categoryNode)
        {
            _selectedCategoryNodeForBatch = categoryNode;

            if (_selectedCategoryNodeForBatch != null && _selectedCategoryNodeForBatch.Tag is ICategory)
            {
                IsBatchModeActive = true;
                var isSingleMode = !SupportsBatch(_selectedCategoryNodeForBatch) || IsBatchGenerationDisabledForCurrentModule();
                AIGenerateButtonText = isSingleMode ? "AI单次" : "AI批量";
            }
            else
            {
                IsBatchModeActive = false;
                AIGenerateButtonText = "AI生成";
            }

            System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                System.Windows.Input.CommandManager.InvalidateRequerySuggested,
                System.Windows.Threading.DispatcherPriority.Background);
        }

        protected virtual bool IsBatchGenerationDisabledForCurrentModule()
        {
            return false;
        }

        public void CancelBatchGeneration()
        {
            _batchCancellationTokenSource?.Cancel();
            _singleCancellationTokenSource?.Cancel();
            IsBatchCancelRequested = true;
            BatchProgressText = "正在取消...";
            try
            {
                _skChatService.CancelCurrentRequest();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetType().Name}] 取消当前请求失败: {ex.Message}");
            }
            TM.App.Log($"[{GetType().Name}] 批量生成已请求取消");
        }

        protected override bool CanExecuteAIGenerate()
        {
            if (IsBatchModeActive && _selectedCategoryNodeForBatch != null)
            {
                return base.CanExecuteAIGenerate() && GetAIGenerationConfig() != null;
            }

            return base.CanExecuteAIGenerate() && GetAIGenerationConfig() != null;
        }

        private static readonly HashSet<string> CoherenceEnabledCategories = new(StringComparer.Ordinal)
        {
            "场景规划",
            "细节设计",
            "要素整合"
        };

        private bool _hasCoherenceHardConflict;
        public bool HasCoherenceHardConflict
        {
            get => _hasCoherenceHardConflict;
            private set
            {
                if (_hasCoherenceHardConflict != value)
                {
                    _hasCoherenceHardConflict = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _coherenceConflictMessage = string.Empty;
        public string CoherenceConflictMessage
        {
            get => _coherenceConflictMessage;
            private set
            {
                if (_coherenceConflictMessage != value)
                {
                    _coherenceConflictMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _coherenceConflictScopeId = string.Empty;

        private string GetCurrentCoherenceScopeId()
        {
            if (IsCreateMode)
            {
                return "CreateMode";
            }

            var editingData = GetCurrentEditingDataObject();
            if (editingData is IDataItem dataItem)
            {
                return dataItem.Id ?? string.Empty;
            }

            return string.Empty;
        }

        private void ResetCoherenceState()
        {
            HasCoherenceHardConflict = false;
            CoherenceConflictMessage = string.Empty;
            _coherenceConflictScopeId = string.Empty;
        }

        private static string BuildCoherencePromptAppendix()
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("<coherence_check mandatory=\"true\">");
            sb.AppendLine("在完成主要输出内容之后，必须追加以下区块：");
            sb.AppendLine("<new_facts>");
            sb.AppendLine("- 若没有新增事实，写：- 无");
            sb.AppendLine("</new_facts>");
            sb.AppendLine("<consistency_check>");
            sb.AppendLine("- 与既定设定是否冲突: 是/否");
            sb.AppendLine("- 若是，冲突点: ...");
            sb.AppendLine("- 若否，如何确保不冲突: ...");
            sb.AppendLine("</consistency_check>");
            sb.AppendLine("<missing_info>");
            sb.AppendLine("- 若无需补充，写：- 无");
            sb.AppendLine("</missing_info>");
            sb.AppendLine("</coherence_check>");
            sb.AppendLine();
            return sb.ToString();
        }

        private const string BatchCoherenceConflictKey = "CoherenceSelfCheckConflict";
        private const string BatchCoherenceConflictPointKey = "CoherenceConflictPoint";

        private void EvaluateCoherenceC0(string aiText)
        {
            ResetCoherenceState();

            _coherenceConflictScopeId = GetCurrentCoherenceScopeId();

            if (string.IsNullOrWhiteSpace(aiText))
            {
                return;
            }

            var lines = aiText.Split('\n');

            bool conflict = false;
            string conflictPoint = string.Empty;

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0)
                    continue;

                if (line.Contains("与既定设定是否冲突", StringComparison.Ordinal))
                {
                    if (YesPatternRegex.IsMatch(line))
                    {
                        conflict = true;
                    }
                }

                if (line.Contains("冲突点", StringComparison.Ordinal))
                {
                    var idx = line.IndexOf(':');
                    if (idx < 0) idx = line.IndexOf('：');
                    if (idx >= 0 && idx + 1 < line.Length)
                    {
                        var v = line[(idx + 1)..].Trim();
                        if (!string.IsNullOrWhiteSpace(v) && !string.Equals(v, "无", StringComparison.Ordinal))
                        {
                            conflictPoint = v;
                        }
                    }
                }
            }

            if (conflict)
            {
                HasCoherenceHardConflict = true;
                CoherenceConflictMessage = string.IsNullOrWhiteSpace(conflictPoint)
                    ? "检测到硬冲突（连贯性自检标记为是）"
                    : $"检测到硬冲突：{conflictPoint}";
            }
        }

        protected virtual AIGenerationConfig? GetAIGenerationConfig() => null;

        protected virtual IPromptRepository? GetPromptRepository() => null;

        protected virtual System.Threading.Tasks.Task PrepareReferenceDataForAIGenerationAsync(
            AIGenerationConfig config,
            bool isBatch,
            string? categoryName,
            System.Threading.CancellationToken cancellationToken)
        {
            return System.Threading.Tasks.Task.CompletedTask;
        }

        protected static async System.Threading.Tasks.Task EnsureServiceInitializedAsync(object? service)
        {
            if (service is TM.Framework.Common.Services.IAsyncInitializable initializable)
            {
                await initializable.InitializeAsync().ConfigureAwait(false);
            }
        }

        protected static string FilterToCandidateOrRaw(string value, IEnumerable<string> candidates)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var list = candidates?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
            if (list.Count == 0) return value;
            return EntityNameNormalizeHelper.NormalizeSingle(value, list, EntityMatchMode.Strict);
        }

        protected static string FilterToCandidatesOrRaw(string value, IEnumerable<string> candidates, string separator = "、")
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var list = candidates?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
            if (list.Count == 0) return value;
            return EntityNameNormalizeHelper.NormalizeMultiple(value, list, EntityMatchMode.Strict, separator);
        }

        protected override async System.Threading.Tasks.Task ExecuteAIGenerateAsync()
        {
            if (_skChatService.IsMainConversationGenerating)
            {
                var confirmed = StandardDialog.ShowConfirm(
                    "主界面对话正在生成，继续需要中断主界面对话，是否继续？",
                    "互斥提醒");
                if (!confirmed)
                    return;

                _skChatService.CancelCurrentRequest();
                TM.App.Log($"[{GetType().Name}] 用户确认中断主界面对话，工作台 AI 继续执行");
            }

            if (IsBatchModeActive && _selectedCategoryNodeForBatch != null)
            {
                var isSingleMode = !SupportsBatch(_selectedCategoryNodeForBatch) || IsBatchGenerationDisabledForCurrentModule();
                TM.App.Log($"[{GetType().Name}] 进入分类AI生成模式，分类={_selectedCategoryNodeForBatch.Name}, 单类模式={isSingleMode}");
                await ExecuteBatchAIGenerateEntryAsync(isSingleMode);
                return;
            }

            var config = GetAIGenerationConfig();
            if (config != null)
            {
                var authResult = await ProtectionService.CheckFeatureAuthorizationAsync(AIFeatureId);
                if (authResult == null)
                {
                    GlobalToast.Warning("网络异常", "无法验证功能授权，请检查网络后重试");
                    return;
                }
                if (authResult == false)
                {
                    GlobalToast.Warning("功能受限", "您的订阅计划不支持此功能，请升级订阅");
                    return;
                }

                IsAIGenerating = true;

                await ExecuteAIGenerateWithConfigAsync(config);
            }
            else
            {
                GlobalToast.Warning("未接入新业务", "当前页面未提供AIGenerationConfig，已禁用旧业务AI生成链路");
            }
        }

        private async System.Threading.Tasks.Task ExecuteBatchAIGenerateEntryAsync(bool singleMode = false)
        {
            if (_selectedCategoryNodeForBatch == null)
            {
                GlobalToast.Warning("提示", "请先选择一个分类节点");
                return;
            }

            var categoryName = _selectedCategoryNodeForBatch.Name;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var config = await ShowBatchGenerationDialogAsync(categoryName, singleMode);
            if (config == null)
            {
                TM.App.Log($"[{GetType().Name}] 用户取消生成");
                return;
            }
            TM.App.Log($"[{GetType().Name}] 配置弹窗完成: {sw.ElapsedMilliseconds}ms");

            IsAIGenerating = true;

            var authResult = await ProtectionService.CheckFeatureAuthorizationAsync(AIFeatureId);
            if (authResult == null)
            {
                GlobalToast.Warning("网络异常", "无法验证功能授权，请检查网络后重试");
                return;
            }
            if (authResult == false)
            {
                GlobalToast.Warning("功能受限", "您的订阅计划不支持此功能，请升级订阅");
                return;
            }

            await ExecuteBatchAIGenerateAsync(config);
        }

        protected virtual System.Threading.Tasks.Task<BatchGenerationConfig?> ShowBatchGenerationDialogAsync(string categoryName, bool singleMode = false)
        {
            if (_isPipelineExecution)
            {
                return System.Threading.Tasks.Task.FromResult<BatchGenerationConfig?>(new BatchGenerationConfig
                {
                    CategoryName = categoryName,
                    TotalCount = singleMode ? 1 : GetDefaultTotalCount(),
                    BatchSize = singleMode ? 1 : GetDefaultBatchSize(),
                });
            }
            var config = BatchGenerationDialog.Show(categoryName, singleMode ? 1 : 0, singleMode ? 1 : GetDefaultBatchSize(), null, singleMode);
            return System.Threading.Tasks.Task.FromResult(config);
        }

    }
}

