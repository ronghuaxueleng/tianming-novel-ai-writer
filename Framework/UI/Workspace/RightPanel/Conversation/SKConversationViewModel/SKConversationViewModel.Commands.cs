using System.Windows.Input;

namespace TM.Framework.UI.Workspace.RightPanel.Conversation
{
    public partial class SKConversationViewModel
    {
        #region 命令

        public ICommand SendCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand NewSessionCommand { get; }
        public ICommand ClearHistoryCommand { get; }
        public ICommand ClearSessionCommand { get; }
        public ICommand ShowHistoryCommand { get; }

        public ICommand CopyMessageCommand { get; }
        public ICommand DeleteMessageCommand { get; }
        public ICommand DeleteUserWithAssistantCommand { get; }
        public ICommand RecallToInputCommand { get; }
        public ICommand RegenerateAssistantMessageCommand { get; }
        public ICommand RegenerateFromUserMessageCommand { get; }
        public ICommand RegenerateFromHereCommand { get; }
        public ICommand ToggleStarCommand { get; }
        public ICommand ExportMessageCommand { get; }
        public ICommand ShowStarredMessagesCommand { get; }
        public ICommand EditUserMessageCommand { get; }
        public ICommand SwitchModelAnswerCommand { get; }
        public ICommand TranslateMessageCommand { get; }
        public ICommand ToggleMultiSelectCommand { get; }

        #endregion

        #region 快捷面板命令

        public ICommand QuickFillInputCommand { get; }
        public ICommand QuickSendCommand { get; }
        public ICommand SendPlanContinueCommand { get; }
        public ICommand AgentContinueCommand { get; }
        public ICommand AgentRewriteCommand { get; }

        public ICommand EditConfirmCommand { get; }
        public ICommand EditCancelCommand { get; }

        private ICommand? _blueprintContinueCommand;
        public ICommand BlueprintContinueCommand => _blueprintContinueCommand ??= new AsyncRelayCommand(async _ =>
        {
            if (!string.IsNullOrEmpty(_blueprintSessionId))
                await GenerateBlueprintChapterAsync();
        });

        private ICommand? _blueprintEndCommand;
        public ICommand BlueprintEndCommand => _blueprintEndCommand ??= new RelayCommand(_ =>
        {
            ClearBlueprintSession();
            GlobalToast.Info("已停止生成", "蓝图逐章生成已停止，已生成章节完整保留");
        });

        #endregion
    }
}
