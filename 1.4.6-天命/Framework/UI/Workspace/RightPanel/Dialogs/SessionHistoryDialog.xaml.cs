using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using TM.Services.Framework.AI.SemanticKernel;

namespace TM.Framework.UI.Workspace.RightPanel.Dialogs
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class SessionHistoryDialog : Window, INotifyPropertyChanged
    {
        private readonly SessionManager _sessionManager;
        private readonly UIStateCache _uiStateCache;

        private static SessionManager GetSessionManager() => ServiceLocator.Get<SessionManager>();
        private static UIStateCache GetUIStateCache() => ServiceLocator.Get<UIStateCache>();

        private ObservableCollection<SessionInfo> _sessions = new();
        private ObservableCollection<SessionInfo> _filteredSessions = new();
        private SessionInfo? _selectedSession;
        private string _searchKeyword = string.Empty;

        public ObservableCollection<SessionInfo> Sessions
        {
            get => _sessions;
            set
            {
                _sessions = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        public ObservableCollection<SessionInfo> FilteredSessions
        {
            get => _filteredSessions;
            set
            {
                _filteredSessions = value;
                OnPropertyChanged();
            }
        }

        public SessionInfo? SelectedSession
        {
            get => _selectedSession;
            set
            {
                _selectedSession = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedSession));
            }
        }

        public string SearchKeyword
        {
            get => _searchKeyword;
            set
            {
                _searchKeyword = value;
                OnPropertyChanged();
                _ = ApplyFilterAsync();
            }
        }

        public bool HasSelectedSession => SelectedSession != null;

        public string? SelectedSessionId { get; private set; }

        public List<string> DeletedSessionIds { get; } = new();

        public SessionHistoryDialog()
        {
            _sessionManager = GetSessionManager();
            _uiStateCache = GetUIStateCache();
            InitializeComponent();
            DataContext = this;
            LoadSessions();
        }

        private void LoadSessions()
        {
            try
            {
                var all = _sessionManager.GetAllSessions();
                Sessions = new ObservableCollection<SessionInfo>(all);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SessionHistoryDialog] 加载会话失败: {ex.Message}");
            }
        }

        private void ApplyFilter()
        {
            _ = ApplyFilterAsync();
        }

        private async System.Threading.Tasks.Task ApplyFilterAsync()
        {
            if (Sessions == null || Sessions.Count == 0)
            {
                FilteredSessions = new ObservableCollection<SessionInfo>();
                return;
            }

            if (string.IsNullOrWhiteSpace(SearchKeyword))
            {
                FilteredSessions = new ObservableCollection<SessionInfo>(Sessions);
                return;
            }

            var keyword = SearchKeyword.Trim();
            if (keyword.Length == 0)
            {
                FilteredSessions = new ObservableCollection<SessionInfo>(Sessions);
                return;
            }

            var lowered = keyword.ToLowerInvariant();
            var snapshot = Sessions.ToList();
            var sessionManager = _sessionManager;

            var result = await System.Threading.Tasks.Task.Run(() =>
            {
                var matched = new List<SessionInfo>();
                foreach (var session in snapshot)
                {
                    if (!string.IsNullOrEmpty(session.Title) &&
                        session.Title.ToLowerInvariant().Contains(lowered))
                    {
                        matched.Add(session);
                        continue;
                    }

                    try
                    {
                        var records = sessionManager.LoadMessages(session.Id);
                        if (records.Any(r =>
                                !string.IsNullOrEmpty(r.Summary) &&
                                r.Summary.ToLowerInvariant().Contains(lowered)))
                        {
                            matched.Add(session);
                        }
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[SessionHistoryDialog] 搜索会话失败: {ex.Message}");
                    }
                }
                return matched;
            }).ConfigureAwait(true);

            FilteredSessions = new ObservableCollection<SessionInfo>(result);
        }

        private void OnDeleteSession(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string sessionId)
            {
                return;
            }

            var session = Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null)
            {
                return;
            }

            var confirm = StandardDialog.ShowConfirm(
                $"确定要删除会话\"{session.Title}\"吗？\n\n此操作不可恢复。",
                "删除会话");

            if (!confirm)
            {
                return;
            }

            try
            {
                _sessionManager.DeleteSession(sessionId);
                DeletedSessionIds.Add(sessionId);

                Sessions.Remove(session);
                ApplyFilter();

                if (SelectedSession?.Id == sessionId)
                {
                    SelectedSession = null;
                }

                _uiStateCache.SetSessionState(Sessions.Count);

                GlobalToast.Success("删除成功", $"已删除会话\"{session.Title}\"");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("删除失败", ex.Message);
            }
        }

        private void OnDeleteAll(object sender, RoutedEventArgs e)
        {
            if (Sessions.Count == 0)
            {
                GlobalToast.Warning("提示", "没有可删除的会话");
                return;
            }

            var confirm = StandardDialog.ShowConfirm(
                $"确定要删除全部 {Sessions.Count} 个会话吗？\n\n此操作不可恢复。",
                "全选删除");

            if (!confirm)
            {
                return;
            }

            try
            {
                var ids = Sessions.Select(s => s.Id).ToList();
                foreach (var id in ids)
                {
                    _sessionManager.DeleteSession(id);
                    DeletedSessionIds.Add(id);
                }

                Sessions.Clear();
                FilteredSessions.Clear();
                SelectedSession = null;

                _uiStateCache.SetSessionState(0);

                GlobalToast.Success("删除成功", $"已删除全部 {ids.Count} 个会话");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("删除失败", ex.Message);
            }
        }

        private void OnConfirm(object sender, RoutedEventArgs e)
        {
            if (SelectedSession != null)
            {
                SelectedSessionId = SelectedSession.Id;
                DialogResult = true;
                Close();
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
