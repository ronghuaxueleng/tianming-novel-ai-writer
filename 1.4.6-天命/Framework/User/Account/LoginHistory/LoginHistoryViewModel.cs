using System;
using System.Reflection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.User.Account.LoginHistory;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Services;

namespace TM.Framework.User.Account.LoginHistory
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class LoginHistoryViewModel : INotifyPropertyChanged
    {
        private readonly LoginHistoryService _historyService;

        #region 属性

        private ObservableCollection<LoginHistoryModel> _loginRecords;
        public ObservableCollection<LoginHistoryModel> LoginRecords
        {
            get => _loginRecords;
            set
            {
                _loginRecords = value;
                OnPropertyChanged();
                BuildLoginTree();
            }
        }

        public ObservableCollection<TreeNodeItem> LoginTree { get; } = new();

        private DateTime? _filterStartDate;
        public DateTime? FilterStartDate
        {
            get => _filterStartDate;
            set
            {
                _filterStartDate = value;
                OnPropertyChanged();
            }
        }

        private DateTime? _filterEndDate;
        public DateTime? FilterEndDate
        {
            get => _filterEndDate;
            set
            {
                _filterEndDate = value;
                OnPropertyChanged();
            }
        }

        private string? _selectedDeviceType;
        public string? SelectedDeviceType
        {
            get => _selectedDeviceType;
            set
            {
                _selectedDeviceType = value;
                OnPropertyChanged();
            }
        }

        private int _totalRecords;
        public int TotalRecords
        {
            get => _totalRecords;
            set
            {
                _totalRecords = value;
                OnPropertyChanged();
            }
        }

        private int _abnormalRecords;
        public int AbnormalRecords
        {
            get => _abnormalRecords;
            set
            {
                _abnormalRecords = value;
                OnPropertyChanged();
            }
        }

        private string _lastLoginTime = string.Empty;
        public string LastLoginTime
        {
            get => _lastLoginTime;
            set
            {
                _lastLoginTime = value;
                OnPropertyChanged();
            }
        }

        private int _securityScore;
        public int SecurityScore
        {
            get => _securityScore;
            set
            {
                _securityScore = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SecurityScoreColor));
                OnPropertyChanged(nameof(SecurityScoreText));
            }
        }

        public string SecurityScoreColor => SecurityScore >= 80 ? "#4CAF50" : 
                                           SecurityScore >= 60 ? "#FFA726" :
                                           SecurityScore >= 40 ? "#FF7043" : "#F44336";

        public string SecurityScoreText => SecurityScore >= 80 ? "安全" :
                                          SecurityScore >= 60 ? "一般" :
                                          SecurityScore >= 40 ? "风险" : "危险";

        private int _highRiskCount;
        public int HighRiskCount
        {
            get => _highRiskCount;
            set
            {
                _highRiskCount = value;
                OnPropertyChanged();
            }
        }

        private int _activeSessionsCount;
        public int ActiveSessionsCount
        {
            get => _activeSessionsCount;
            set
            {
                _activeSessionsCount = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<LoginHistoryModel> _activeSessions;
        public ObservableCollection<LoginHistoryModel> ActiveSessions
        {
            get => _activeSessions;
            set
            {
                _activeSessions = value;
                OnPropertyChanged();
            }
        }

        private bool _showOnlyAbnormal;
        public bool ShowOnlyAbnormal
        {
            get => _showOnlyAbnormal;
            set
            {
                _showOnlyAbnormal = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<string> DeviceTypes { get; set; }

        #endregion

        #region 命令

        public ICommand RefreshCommand { get; }
        public ICommand FilterCommand { get; }
        public ICommand ClearHistoryCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand ViewDetailsCommand { get; }
        public ICommand EndSessionCommand { get; }
        public ICommand ShowAnalyticsCommand { get; }
        public ICommand SelectLoginCommand { get; }

        #endregion

        public LoginHistoryViewModel(LoginHistoryService historyService)
        {
            _historyService = historyService;
            _loginRecords = new ObservableCollection<LoginHistoryModel>();
            _activeSessions = new ObservableCollection<LoginHistoryModel>();
            DeviceTypes = new ObservableCollection<string> { "全部", "Windows PC", "移动设备", "其他" };

            RefreshCommand = new RelayCommand(LoadLoginRecords);
            FilterCommand = new RelayCommand(ApplyFilter);
            ClearHistoryCommand = new RelayCommand(ClearHistory);
            ExportCommand = new RelayCommand(ExportHistory);
            ViewDetailsCommand = new RelayCommand<LoginHistoryModel>(ViewDetails);
            EndSessionCommand = new RelayCommand<string>(EndSession);
            ShowAnalyticsCommand = new RelayCommand(ShowAnalytics);
            SelectLoginCommand = new TM.Framework.Common.Helpers.MVVM.RelayCommand(SelectLoginFromTree);

            LoadLoginRecords();
            LoadActiveSessions();
        }

        private void LoadActiveSessions()
        {
            try
            {
                var sessions = _historyService.GetActiveSessions();
                ActiveSessions.Clear();

                foreach (var session in sessions)
                {
                    ActiveSessions.Add(new LoginHistoryModel
                    {
                        Id = session.Id,
                        LoginTime = session.LoginTime,
                        IpAddress = session.IpAddress,
                        DeviceType = session.DeviceType,
                        DeviceName = session.DeviceName,
                        Location = session.Location,
                        Browser = session.Browser,
                        OperatingSystem = session.OperatingSystem,
                        IsSuccess = session.IsSuccess,
                        IsAbnormal = session.IsAbnormal,
                        SessionId = session.SessionId,
                        LogoutTime = session.LogoutTime,
                        SessionDuration = session.SessionDuration,
                        RiskLevel = session.RiskLevel,
                        RiskReason = session.RiskReason
                    });
                }

                ActiveSessionsCount = ActiveSessions.Count;
                TM.App.Log($"[LoginHistoryViewModel] 加载了 {ActiveSessionsCount} 个活动会话");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginHistoryViewModel] 加载活动会话失败: {ex.Message}");
            }
        }

        private void LoadLoginRecords()
        {
            _ = LoadLoginRecordsAsync();
        }

        private async Task LoadLoginRecordsAsync()
        {
            try
            {
                var records = await _historyService.GetAllRecordsFromServerAsync();
                LoginRecords.Clear();

                foreach (var record in records)
                {
                    if (ShowOnlyAbnormal && !record.IsAbnormal)
                        continue;

                    LoginRecords.Add(new LoginHistoryModel
                    {
                        Id = record.Id,
                        LoginTime = record.LoginTime,
                        IpAddress = record.IpAddress,
                        DeviceType = record.DeviceType,
                        DeviceName = record.DeviceName,
                        Location = record.Location,
                        Browser = record.Browser,
                        OperatingSystem = record.OperatingSystem,
                        IsSuccess = record.IsSuccess,
                        IsAbnormal = record.IsAbnormal,
                        SessionId = record.SessionId,
                        LogoutTime = record.LogoutTime,
                        SessionDuration = record.SessionDuration,
                        RiskLevel = record.RiskLevel,
                        RiskReason = record.RiskReason
                    });
                }

                UpdateStatistics();
                LoadActiveSessions();
                TM.App.Log($"[LoginHistoryViewModel] 加载了 {LoginRecords.Count} 条登录记录");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginHistoryViewModel] 加载登录记录失败: {ex.Message}");
                GlobalToast.Error("加载失败", ex.Message);
            }
        }

        private void ApplyFilter()
        {
            try
            {
                var records = _historyService.GetFilteredRecords(
                    FilterStartDate,
                    FilterEndDate,
                    SelectedDeviceType == "全部" ? null : SelectedDeviceType
                );

                LoginRecords.Clear();
                foreach (var record in records)
                {
                    if (ShowOnlyAbnormal && !record.IsAbnormal)
                        continue;

                    LoginRecords.Add(new LoginHistoryModel
                    {
                        Id = record.Id,
                        LoginTime = record.LoginTime,
                        IpAddress = record.IpAddress,
                        DeviceType = record.DeviceType,
                        DeviceName = record.DeviceName,
                        Location = record.Location,
                        Browser = record.Browser,
                        OperatingSystem = record.OperatingSystem,
                        IsSuccess = record.IsSuccess,
                        IsAbnormal = record.IsAbnormal,
                        SessionId = record.SessionId,
                        LogoutTime = record.LogoutTime,
                        SessionDuration = record.SessionDuration,
                        RiskLevel = record.RiskLevel,
                        RiskReason = record.RiskReason
                    });
                }

                UpdateStatistics();
                GlobalToast.Info("筛选结果", $"找到 {LoginRecords.Count} 条记录");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginHistoryViewModel] 筛选失败: {ex.Message}");
                GlobalToast.Error("筛选失败", ex.Message);
            }
        }

        private void ClearHistory()
        {
            try
            {
                var result = StandardDialog.ShowConfirm("确定要清空所有登录历史记录吗？此操作不可撤销。", "清空历史");
                if (result == true)
                {
                    _historyService.ClearHistory();
                    LoginRecords.Clear();
                    UpdateStatistics();

                    TM.App.Log("[LoginHistoryViewModel] 历史记录已清空");
                    GlobalToast.Success("清空历史", "历史记录已清空");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginHistoryViewModel] 清空历史失败: {ex.Message}");
                GlobalToast.Error("清空失败", ex.Message);
            }
        }

        private void ExportHistory()
        {
            try
            {
                var exportPath = _historyService.ExportHistory();
                if (!string.IsNullOrEmpty(exportPath))
                {
                    GlobalToast.Success("导出成功", $"已导出到: {exportPath}");
                }
                else
                {
                    GlobalToast.Error("导出失败", "导出历史记录失败");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginHistoryViewModel] 导出失败: {ex.Message}");
                GlobalToast.Error("导出失败", ex.Message);
            }
        }

        private void UpdateStatistics()
        {
            TotalRecords = LoginRecords.Count;
            AbnormalRecords = LoginRecords.Count(r => r.IsAbnormal);
            HighRiskCount = LoginRecords.Count(r => r.RiskLevel >= LoginRiskLevel.High);

            var lastLogin = LoginRecords.FirstOrDefault();
            LastLoginTime = lastLogin?.LoginTimeDisplay ?? "无记录";

            var stats = _historyService.GetStatistics();
            SecurityScore = stats.SecurityScore;
        }

        private void ViewDetails(LoginHistoryModel? record)
        {
            if (record == null) return;

            try
            {
                var details = $"登录详情\n\n" +
                             $"时间: {record.LoginTimeDisplay}\n" +
                             $"IP地址: {record.IpAddress}\n" +
                             $"位置: {record.Location}\n" +
                             $"设备: {record.DeviceName} ({record.DeviceType})\n" +
                             $"操作系统: {record.OperatingSystem}\n" +
                             $"浏览器: {record.Browser}\n" +
                             $"会话状态: {record.SessionStatus}\n" +
                             $"会话时长: {record.SessionDurationDisplay}\n" +
                             $"风险等级: {record.StatusText}\n" +
                             $"风险原因: {record.RiskReason}";

                StandardDialog.ShowInfo("登录详情", details);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginHistoryViewModel] 查看详情失败: {ex.Message}");
                GlobalToast.Error("查看详情", $"操作失败: {ex.Message}");
            }
        }

        private void EndSession(string? sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;

            try
            {
                var result = StandardDialog.ShowConfirm("结束会话", "确定要强制结束此会话吗？");
                if (result == true)
                {
                    _historyService.EndSession(sessionId);
                    LoadActiveSessions();
                    LoadLoginRecords();
                    GlobalToast.Success("结束会话", "会话已强制结束");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginHistoryViewModel] 结束会话失败: {ex.Message}");
                GlobalToast.Error("结束会话", $"操作失败: {ex.Message}");
            }
        }

        private void ShowAnalytics()
        {
            try
            {
                var stats = _historyService.GetStatistics();

                var analytics = $"登录统计分析\n\n" +
                               $"总登录次数: {stats.TotalLogins}\n" +
                               $"成功登录: {stats.SuccessfulLogins}\n" +
                               $"失败登录: {stats.FailedLogins}\n" +
                               $"异常登录: {stats.AbnormalLogins}\n" +
                               $"唯一IP数: {stats.UniqueIPs}\n" +
                               $"唯一设备数: {stats.UniqueDevices}\n" +
                               $"安全评分: {stats.SecurityScore}/100";

                if (stats.LocationDistribution.Count > 0)
                {
                    analytics += "\n\n地理位置分布:";
                    foreach (var loc in stats.LocationDistribution.OrderByDescending(kv => kv.Value).Take(5))
                    {
                        analytics += $"\n{loc.Key}: {loc.Value}次";
                    }
                }

                StandardDialog.ShowInfo("统计分析", analytics);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LoginHistoryViewModel] 显示分析失败: {ex.Message}");
                GlobalToast.Error("统计分析", $"操作失败: {ex.Message}");
            }
        }

        private void BuildLoginTree()
        {
            LoginTree.Clear();
            foreach (var record in LoginRecords)
            {
                string icon = record.IsAbnormal ? "⚠️" : (record.SessionStatus == "活动中" ? "🟢" : "✅");
                LoginTree.Add(new TreeNodeItem
                {
                    Name = $"{record.LoginTime:yyyy-MM-dd HH:mm} - {record.Location}",
                    Icon = icon,
                    Tag = record,
                    IsExpanded = false,
                    ShowChildCount = false
                });
            }
            OnPropertyChanged(nameof(LoginTree));
        }

        private void SelectLoginFromTree(object? parameter)
        {
            if (parameter is TreeNodeItem node)
            {
                foreach (var item in LoginTree)
                {
                    item.IsSelected = false;
                }
                node.IsSelected = true;
            }
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

