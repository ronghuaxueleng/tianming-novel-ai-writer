using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TM.Framework.UI.Workspace.Services;
using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Parsing;
using TM.Services.Framework.AI.SemanticKernel.Plugins;

namespace TM.Framework.UI.Workspace.RightPanel.Conversation
{
    public partial class SKConversationViewModel
    {
        #region Edit 预览确认快捷面板

        private static readonly string[] WorkspacePreviewFunctions = new[]
        {
            "ReplaceInFile", "MultiReplaceInFile", "PreviewWriteFile", "PreviewDeleteFile", "PreviewRenameFile"
        };

        private void CheckEditPreviewActions(ExecutionEvent evt)
        {
            if (evt.Mode != ChatMode.Edit) return;
            if (evt.EventType != ExecutionEventType.ToolCallCompleted) return;
            if (string.IsNullOrEmpty(evt.Detail)) return;

            var funcName = evt.FunctionName ?? string.Empty;
            var isDataEditPreview = string.Equals(funcName, "PreviewChange", StringComparison.OrdinalIgnoreCase);
            var isWorkspacePreview = WorkspacePreviewFunctions.Any(f => string.Equals(funcName, f, StringComparison.OrdinalIgnoreCase));

            if (!isDataEditPreview && !isWorkspacePreview) return;

            try
            {
                using var doc = JsonDocument.Parse(evt.Detail);
                if (doc.RootElement.TryGetProperty("previewId", out var pidEl))
                {
                    var previewId = pidEl.GetString();
                    if (!string.IsNullOrEmpty(previewId))
                    {
                        if (isDataEditPreview)
                            _pendingPreviewId = previewId;
                        else
                        {
                            if (!_pendingFilePreviewIds.Contains(previewId))
                                _pendingFilePreviewIds.Add(previewId);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private void ShowEditPreviewPanelIfPending()
        {
            var previewId = _pendingPreviewId;
            if (!string.IsNullOrEmpty(previewId))
            {
                if (PendingChangeStore.GetPreview(previewId) != null)
                {
                    _hasEditPreviewActions = true;
                    OnPropertyChanged(nameof(HasEditPreviewActions));
                }
                else
                {
                    _pendingPreviewId = null;
                }
            }

            if (_pendingFilePreviewIds.Count > 0)
            {
                foreach (var filePreviewId in _pendingFilePreviewIds.ToList())
                {
                    var entry = FilePreviewStore.GetPreview(filePreviewId);
                    if (entry == null) continue;

                    try
                    {
                        var oldText = entry.OriginalContent ?? string.Empty;
                        var newText = entry.NewContent ?? string.Empty;
                        var displayPath = entry.RelativePath;

                        if (entry.OperationType == FileOperationType.Rename)
                        {
                            oldText = $"[文件路径] {entry.RelativePath}";
                            newText = $"[文件路径] {entry.NewRelativePath}";
                            displayPath = $"{entry.RelativePath} → {entry.NewRelativePath}";
                        }
                        else if (entry.OperationType == FileOperationType.Delete)
                        {
                            newText = $"（文件将被删除）";
                        }

                        var comm = ServiceLocator.Get<PanelCommunicationService>();
                        comm.PublishShowFileDiff(filePreviewId, displayPath, oldText, newText);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[QuickPanel] 发布文件 Diff 视图异常: {ex.Message}");
                    }
                }
            }

            OnPropertyChanged(nameof(HasSuggestedActions));
        }

        private async System.Threading.Tasks.Task ExecuteEditConfirmAsync()
        {
            var previewId = _pendingPreviewId;
            if (string.IsNullOrEmpty(previewId)) return;

            _hasEditPreviewActions = false;
            _pendingPreviewId = null;
            OnPropertyChanged(nameof(HasEditPreviewActions));
            OnPropertyChanged(nameof(HasSuggestedActions));

            try
            {
                var plugin = new DataEditPlugin();
                var result = await plugin.ConfirmChange(previewId);

                var msg = UIMessageItem.CreateAssistantPlaceholder();
                msg.Content = result;
                msg.IsThinking = false;
                msg.FinishStreaming();
                Messages.Add(msg);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EditQuickPanel] 确认变更异常: {ex.Message}");
                GlobalToast.Error("确认变更失败", ex.Message);
            }
        }

        private async System.Threading.Tasks.Task ExecuteEditCancelAsync()
        {
            var previewId = _pendingPreviewId;
            if (string.IsNullOrEmpty(previewId)) return;

            _hasEditPreviewActions = false;
            _pendingPreviewId = null;
            OnPropertyChanged(nameof(HasEditPreviewActions));
            OnPropertyChanged(nameof(HasSuggestedActions));

            try
            {
                var plugin = new DataEditPlugin();
                var result = await plugin.RollbackChange(previewId);

                var msg = UIMessageItem.CreateAssistantPlaceholder();
                msg.Content = result;
                msg.IsThinking = false;
                msg.FinishStreaming();
                Messages.Add(msg);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EditQuickPanel] 取消变更异常: {ex.Message}");
                GlobalToast.Error("取消变更失败", ex.Message);
            }
        }

        #endregion

        #region 快捷面板逻辑

        private void RefreshSuggestedActions()
        {
            if (string.IsNullOrWhiteSpace(_lastSentUserText))
            {
                OnPropertyChanged(nameof(HasSuggestedActions));
                return;
            }

            if (!IsExplicitChapterGenerationRequest(_lastSentUserText)
                && !ChapterDirectiveParser.HasContinueDirective(_lastSentUserText)
                && !ChapterDirectiveParser.HasRewriteDirective(_lastSentUserText))
            {
                OnPropertyChanged(nameof(HasSuggestedActions));
                return;
            }

            if (_lastExecutedMode == ChatMode.Plan)
            {
                var range = ChapterParserHelper.ParseChapterRange(_lastSentUserText);
                int nextStart = -1;
                if (range.HasValue && range.Value.end > 0)
                {
                    nextStart = range.Value.end + 1;
                }
                else
                {
                    var ranges = ChapterParserHelper.ParseChapterRanges(_lastSentUserText);
                    if (ranges != null && ranges.Count > 0)
                        nextStart = ranges.Max(r => r.end) + 1;
                }

                if (nextStart < 1)
                {
                    var singleCh = ChapterParserHelper.ExtractChapterNumber(_lastSentUserText);
                    if (singleCh > 0) nextStart = singleCh + 1;
                }

                if (nextStart < 1)
                {
                    var lastId = CurrentChapterTracker.CurrentChapterId;
                    if (!string.IsNullOrEmpty(lastId))
                    {
                        var (_, lastCh) = ChapterParserHelper.ParseChapterIdOrDefault(lastId);
                        if (lastCh > 0) nextStart = lastCh + 1;
                    }
                }

                if (nextStart < 1) nextStart = 1;

                var volNum = ChapterParserHelper.ExtractVolumeNumber(_lastSentUserText);
                _planContinueDisplayPrefix = volNum > 0 ? $"生成第{volNum}卷第" : "生成第";
                _planContinueStartNum = nextStart;
                _hasPlanContinueAction = true;
                OnPropertyChanged(nameof(HasPlanContinueAction));
                OnPropertyChanged(nameof(PlanContinueDisplayPrefix));
                OnPropertyChanged(nameof(PlanContinueStartNum));
            }
            else if (_lastExecutedMode == ChatMode.Agent)
            {
                var lastChapterId = CurrentChapterTracker.CurrentChapterId;
                if (string.IsNullOrEmpty(lastChapterId))
                {
                    OnPropertyChanged(nameof(HasSuggestedActions));
                    return;
                }

                var (lastVol, lastChNum) = ChapterParserHelper.ParseChapterIdOrDefault(lastChapterId);
                var nextChNum = lastChNum > 0 ? lastChNum + 1 : -1;

                _agentContinueLabel = nextChNum > 0
                    ? (lastVol > 0 ? $"▶ 继续生成第{lastVol}卷第{nextChNum}章" : $"▶ 继续生成第{nextChNum}章")
                    : string.Empty;
                _agentContinueText = nextChNum > 0
                    ? (lastVol > 0 ? $"生成第{lastVol}卷第{nextChNum}章" : $"生成第{nextChNum}章")
                    : string.Empty;

                _agentRewriteLabel = $"↺ 重写 {lastChapterId}";
                _agentRewriteText = $"@重写:{lastChapterId}";

                _hasAgentActions = true;
                OnPropertyChanged(nameof(HasAgentActions));
                OnPropertyChanged(nameof(HasAgentContinue));
                OnPropertyChanged(nameof(AgentContinueLabel));
                OnPropertyChanged(nameof(AgentRewriteLabel));
            }

            OnPropertyChanged(nameof(HasSuggestedActions));
        }

        #endregion
    }
}
