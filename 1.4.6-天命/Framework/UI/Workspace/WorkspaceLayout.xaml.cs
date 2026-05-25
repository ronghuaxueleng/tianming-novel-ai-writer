using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using TM.Framework.Common.Services;
using TM.Services.Framework.Settings;
using TM.Framework.UI.Workspace.Services;
using TM.Framework.UI.Workspace.RightPanel.Conversation;

namespace TM.Framework.UI.Workspace
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class WorkspaceLayout : UserControl
    {
        private readonly SettingsManager _settings;
        private readonly ChapterFileWatcher _fileWatcher;
        private readonly PanelCommunicationService _comm;
        private SKConversationViewModel? _conversationVm;
        private bool _isInitialized = false;

        private bool _isLeftWorkVisible = true;
        private bool _isRightWorkVisible = true;

        private GridLength _leftWorkOriginalWidth = new GridLength(1, GridUnitType.Star);
        private GridLength _rightWorkOriginalWidth = new GridLength(1, GridUnitType.Star);

        public WorkspaceLayout()
        {
            InitializeComponent();

            _comm = ServiceLocator.Get<PanelCommunicationService>();

            _conversationVm = ServiceLocator.Get<SKConversationViewModel>();
            RightPanel.DataContext = _conversationVm;

            _settings = ServiceLocator.Get<SettingsManager>();

            _fileWatcher = new ChapterFileWatcher(Dispatcher);
            _fileWatcher.FilesChanged += (s, e) =>
            {
                _comm.PublishRefreshChapterList();
                TM.App.Log("[WorkspaceLayout] 检测到文件变化，已发布刷新事件");
            };

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_isInitialized) return;

            RestoreProportions();

            _isInitialized = true;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _fileWatcher?.Dispose();

            _conversationVm?.Dispose();
            _conversationVm = null;
        }

        public void LoadGeneratedContent(string chapterId, string title, string content)
        {
            _comm.PublishContentGenerated(chapterId, title, content);
            TM.App.Log($"[WorkspaceLayout] 已发布生成内容: {chapterId}");
        }

        public void RefreshChapterList()
        {
            _comm.PublishRefreshChapterList();
        }

    }
}
