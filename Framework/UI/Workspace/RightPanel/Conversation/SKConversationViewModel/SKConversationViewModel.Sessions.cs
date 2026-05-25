using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TM.Framework.UI.Workspace.Services;
using TM.Framework.UI.Workspace.RightPanel.Dialogs;
using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Helpers;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Models;

namespace TM.Framework.UI.Workspace.RightPanel.Conversation
{
    public partial class SKConversationViewModel
    {
        #region 会话管理

        private void NewSession()
        {
            if (_todoExecutionService.IsRunning)
            {
                _todoExecutionService.CancelCurrentRun();
                TM.App.Log("[SKConversationViewModel] 新会话：检测到执行仍在运行，已自动取消");
            }
            _chatService.BeginDraftSession();
            Messages.Clear();
            _hasPlanContinueAction = false;
            _hasAgentActions = false;
            ClearBlueprintSession();
            OnPropertyChanged(nameof(HasPlanContinueAction));
            OnPropertyChanged(nameof(HasAgentActions));
            OnPropertyChanged(nameof(HasAgentContinue));
            OnPropertyChanged(nameof(HasSuggestedActions));
            _currentSessionId = null;
            HasDraftConversation = true;
            SessionTitle = "新会话";

            TM.App.Log("[SKConversationViewModel] 已进入新会话草稿态");
            RefreshContextUsage();
        }

        private void ClearHistory()
        {
            _chatService.DeleteCurrentSession();
            Messages.Clear();
            _hasPlanContinueAction = false;
            _hasAgentActions = false;
            ClearBlueprintSession();
            OnPropertyChanged(nameof(HasPlanContinueAction));
            OnPropertyChanged(nameof(HasAgentActions));
            OnPropertyChanged(nameof(HasAgentContinue));
            OnPropertyChanged(nameof(HasSuggestedActions));
            _currentSessionId = null;
            HasDraftConversation = false;
            SessionTitle = "新会话";
            RefreshContextUsage();
        }

        public void EnterDraftConversation()
        {
            if (!HasDraftConversation)
            {
                HasDraftConversation = true;
            }
        }

        public async System.Threading.Tasks.Task SwitchSessionAsync(string sessionId)
        {
            if (_todoExecutionService.IsRunning)
            {
                _todoExecutionService.CancelCurrentRun();
                TM.App.Log("[SKConversationViewModel] 切换会话：检测到执行仍在运行，已自动取消");
            }

            await _chatService.SwitchSessionAsync(sessionId);

            _hasPlanContinueAction = false;
            _hasAgentActions = false;
            OnPropertyChanged(nameof(HasPlanContinueAction));
            OnPropertyChanged(nameof(HasAgentActions));
            OnPropertyChanged(nameof(HasAgentContinue));
            OnPropertyChanged(nameof(HasSuggestedActions));

            await LoadHistoryMessagesAsync();
            RefreshContextUsage();

            _currentSessionId = sessionId;
            HasDraftConversation = false;

            var sessions = _chatService.Sessions.GetAllSessions();
            var session = sessions.Find(s => s.Id == sessionId);
            if (session != null)
            {
                SessionTitle = session.Title;

                if (!string.IsNullOrEmpty(session.ContextChapterId))
                {
                    CurrentChapterTracker.SetCurrentChapter(session.ContextChapterId);
                    TM.App.Log($"[SKConversationViewModel] 切换会话：章节上下文恢复为 {session.ContextChapterId}");
                }
            }

            LoadSessionMode();
        }

        private void LoadSessionMode()
        {
            if (string.IsNullOrEmpty(_currentSessionId))
                return;

            var modeStr = _chatService.Sessions.GetSessionMode(_currentSessionId);
            ChatMode mode;
            if (int.TryParse(modeStr, out var modeInt) && Enum.IsDefined(typeof(ChatMode), modeInt))
                mode = (ChatMode)modeInt;
            else if (Enum.TryParse<ChatMode>(modeStr, out mode)) { }
            else
                return;

            _currentMode = mode;
            _chatService.CurrentMode = mode;
            OnPropertyChanged(nameof(CurrentMode));
            MonitorTitle = GetModeDisplayName(mode);
            TM.App.Log($"[SKConversationViewModel] 恢复会话模式: {mode}");
        }

        private void ShowHistory()
        {
            try
            {
                var dialog = new SessionHistoryDialog();
                StandardDialog.EnsureOwnerAndTopmost(dialog, null);

                var result = dialog.ShowDialog();
                if (result == true && !string.IsNullOrEmpty(dialog.SelectedSessionId))
                {
                    var sessionId = dialog.SelectedSessionId!;
                    SwitchSessionAsync(sessionId).SafeFireAndForget(ex =>
                    {
                        TM.App.Log($"[SKConversationViewModel] 切换会话失败: {ex.Message}");
                        GlobalToast.Error("切换失败", "切换会话失败，请重试");
                    });
                }
                else
                {
                    OnPropertyChanged(nameof(Messages));
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] 显示会话历史失败: {ex.Message}");
                StandardDialog.ShowError($"打开失败：{ex.Message}", "历史会话打开失败");
            }
        }

        public void RenameCurrentSession(string newTitle)
        {
            newTitle = newTitle?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(newTitle))
            {
                newTitle = $"会话 {DateTime.Now:MM-dd HH:mm}";
            }

            if (string.IsNullOrEmpty(_currentSessionId))
            {
                SessionTitle = newTitle;
                return;
            }

            try
            {
                _chatService.Sessions.RenameSession(_currentSessionId, newTitle);
                SessionTitle = newTitle;
                TM.App.Log($"[SKConversationViewModel] 会话重命名: {_currentSessionId} -> {newTitle}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] 会话重命名失败: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task RestoreLastSessionAsync()
        {
            try
            {
                await _chatService.Sessions.InitializationTask.ConfigureAwait(true);

                _currentSessionId = _chatService.Sessions.GetCurrentSessionIdOrNull();

                if (!string.IsNullOrEmpty(_currentSessionId))
                {
                    LoadSessionMode();

                    var initSession = _chatService.Sessions.GetAllSessions()
                        .Find(s => s.Id == _currentSessionId);
                    if (initSession != null)
                    {
                        SessionTitle = initSession.Title;

                        if (!string.IsNullOrEmpty(initSession.ContextChapterId))
                        {
                            CurrentChapterTracker.SetCurrentChapter(initSession.ContextChapterId);
                            TM.App.Log($"[SKConversationViewModel] 启动：章节上下文恢复为 {initSession.ContextChapterId}");
                        }
                    }

                    await LoadHistoryMessagesAsync();
                    RefreshContextUsage();
                    TM.App.Log($"[SKConversationViewModel] 启动：已恢复会话 {_currentSessionId}，{Messages.Count} 条消息");
                }
                else
                {
                    TM.App.Log("[SKConversationViewModel] 启动：无历史会话");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationViewModel] 启动恢复会话失败: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task LoadHistoryMessagesAsync()
        {
            var records = await _chatService.LoadMessagesAsync().ConfigureAwait(true);

            var items = new List<UIMessageItem>(records.Count);
            foreach (var record in records)
            {
                if (record.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                    continue;

                var item = UIMessageItem.FromSerializedRecord(record);
                items.Add(item);

                if (item.IsAssistant)
                {
                    var payload = item.RestorePayload();
                    if (payload is PlanPayload planPayload && planPayload.Steps.Count > 0)
                    {
                        _cachedPlanSteps = PlanPayloadPublisher.PublishAndCache(planPayload);
                    }
                }
            }

            Messages.ReplaceAll(items);

            _chatService.RebuildHistoryFromMessages(Messages);
            TM.App.Log($"[SKConversationViewModel] 加载 {Messages.Count} 条历史消息（三层架构）");
        }

        public System.Collections.Generic.List<SessionInfo> GetRecentSessions()
        {
            return _chatService.Sessions.GetAllSessions();
        }

        private static string FormatTokenCount(int count)
        {
            if (count >= 1_000_000) return $"{count / 1_000_000.0:F1}M";
            if (count >= 1_000) return $"{count / 1_000.0:F1}k";
            return count.ToString();
        }

        public async void RefreshContextUsage()
        {
            try
            {
                var inputSnapshot = InputText;
                var (tokens, contextWindow, percent) = await Task.Run(() => _chatService.GetContextUsage(inputSnapshot));
                _cachedContextTokens = tokens;
                _cachedContextWindow = contextWindow;
                _cachedContextPercent = percent;
                OnPropertyChanged(nameof(ContextUsageDetailLine1));
                OnPropertyChanged(nameof(ContextUsageStatusText));
                OnPropertyChanged(nameof(ContextUsageColor));
                OnPropertyChanged(nameof(IsSessionCompressed));
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKConversationVM] RefreshContextUsage 失败: {ex.Message}");
            }
        }

        #endregion
    }
}
