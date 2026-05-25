using System;
using System.Reflection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TM.Framework.Common.Helpers.MVVM;

namespace TM.Framework.Notifications.NotificationManagement.NotificationHistory
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class NotificationHistoryViewModel : INotifyPropertyChanged
    {
        private readonly NotificationHistorySettings _settings;
        private ObservableCollection<NotificationRecord> _records;

        public NotificationHistoryViewModel(NotificationHistorySettings settings)
        {
            _settings = settings;
            _records = new ObservableCollection<NotificationRecord>();

            ClearAllCommand = new RelayCommand(ClearAll);
            DeleteCommand = new RelayCommand<NotificationRecord>(DeleteRecord);
            MarkReadCommand = new RelayCommand<NotificationRecord>(MarkAsRead);

            LoadRecords();
        }
        private string _searchKeyword = string.Empty;
        private string _selectedFilter = "全部";
        private int _totalCount;
        private int _unreadCount;

        public ObservableCollection<NotificationRecord> Records
        {
            get => _records;
            set { _records = value; OnPropertyChanged(); }
        }

        public string SearchKeyword
        {
            get => _searchKeyword;
            set { _searchKeyword = value; OnPropertyChanged(); FilterRecords(); }
        }

        public string SelectedFilter
        {
            get => _selectedFilter;
            set { _selectedFilter = value; OnPropertyChanged(); FilterRecords(); }
        }

        public int TotalCount
        {
            get => _totalCount;
            set { _totalCount = value; OnPropertyChanged(); }
        }

        public int UnreadCount
        {
            get => _unreadCount;
            set { _unreadCount = value; OnPropertyChanged(); }
        }

        public ICommand ClearAllCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand MarkReadCommand { get; }

        private void LoadRecords()
        {
            var historyData = _settings.GetRecords();
            foreach (var data in historyData)
            {
                Records.Add(new NotificationRecord
                {
                    Id = data.Id,
                    Title = data.Title,
                    Content = data.Content,
                    Time = data.Time,
                    Type = data.Type,
                    IsRead = data.IsRead
                });
            }

            UpdateStatistics();
            TM.App.Log($"[NotificationHistory] 加载了 {Records.Count} 条通知历史");
        }

        private void FilterRecords()
        {
            UpdateStatistics();
        }

        private void UpdateStatistics()
        {
            TotalCount = Records.Count;
            UnreadCount = Records.Count(r => !r.IsRead);
        }

        private void ClearAll()
        {
            var result = StandardDialog.ShowConfirm("确定要清空所有通知历史记录吗？", "此操作不可恢复。");
            if (result == true)
            {
                _settings.ClearAll();
                Records.Clear();
                UpdateStatistics();
                GlobalToast.Success("清空历史", "已清空所有通知历史");
                TM.App.Log("[NotificationHistory] 清空所有通知历史");
            }
        }

        private void DeleteRecord(NotificationRecord? record)
        {
            if (record == null) return;

            _settings.DeleteRecord(record.Id);
            Records.Remove(record);
            UpdateStatistics();
            GlobalToast.Success("删除通知", "已删除该条通知记录");
            TM.App.Log($"[NotificationHistory] 删除通知: {record.Title}");
        }

        private void MarkAsRead(NotificationRecord? record)
        {
            if (record == null) return;

            _settings.MarkAsRead(record.Id);
            record.IsRead = true;
            UpdateStatistics();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class NotificationRecord : INotifyPropertyChanged
    {
        private bool _isRead;

        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime Time { get; set; }
        public string Type { get; set; } = string.Empty;

        public bool IsRead
        {
            get => _isRead;
            set { _isRead = value; OnPropertyChanged(); }
        }

        public string TimeDisplay => Time.ToString("yyyy-MM-dd HH:mm");

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
