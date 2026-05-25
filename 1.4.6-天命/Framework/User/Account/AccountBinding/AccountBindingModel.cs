using System;
using System.Reflection;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using TM.Framework.User.Account.AccountBinding;

namespace TM.Framework.User.Account.AccountBinding
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class AccountBindingModel : INotifyPropertyChanged
    {
        private bool _isBound;
        private string _accountId = string.Empty;
        private string _nickname = string.Empty;
        private string _email = string.Empty;
        private string _avatarUrl = string.Empty;
        private DateTime? _bindTime;
        private DateTime? _lastSyncTime;
        private DateTime? _lastUseTime;
        private SyncStatus _syncStatus = SyncStatus.None;
        private List<string> _permissions = new();

        public PlatformType Platform { get; set; }
        public string PlatformName { get; set; } = string.Empty;
        public string PlatformIcon { get; set; } = string.Empty;
        public ImageSource? LogoImage { get; set; }

        public bool IsBound
        {
            get => _isBound;
            set
            {
                _isBound = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusDisplay));
                OnPropertyChanged(nameof(StatusColor));
            }
        }

        public string AccountId
        {
            get => _accountId;
            set
            {
                _accountId = value;
                OnPropertyChanged();
            }
        }

        public string Nickname
        {
            get => _nickname;
            set
            {
                _nickname = value;
                OnPropertyChanged();
            }
        }

        public string Email
        {
            get => _email;
            set
            {
                _email = value;
                OnPropertyChanged();
            }
        }

        public string AvatarUrl
        {
            get => _avatarUrl;
            set
            {
                _avatarUrl = value;
                OnPropertyChanged();
            }
        }

        public DateTime? BindTime
        {
            get => _bindTime;
            set
            {
                _bindTime = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BindTimeDisplay));
            }
        }

        public DateTime? LastSyncTime
        {
            get => _lastSyncTime;
            set
            {
                _lastSyncTime = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LastSyncDisplay));
            }
        }

        public DateTime? LastUseTime
        {
            get => _lastUseTime;
            set
            {
                _lastUseTime = value;
                OnPropertyChanged();
            }
        }

        public SyncStatus SyncStatus
        {
            get => _syncStatus;
            set
            {
                _syncStatus = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SyncStatusDisplay));
            }
        }

        public List<string> Permissions
        {
            get => _permissions;
            set
            {
                _permissions = value;
                OnPropertyChanged();
            }
        }

        public string BindTimeDisplay => BindTime?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;
        public string LastSyncDisplay => LastSyncTime?.ToString("MM-dd HH:mm") ?? "未同步";
        public string StatusDisplay => IsBound ? Nickname : "未绑定";
        public string StatusColor => IsBound ? "#4CAF50" : "#F57C00";
        public string SyncStatusDisplay => SyncStatus switch
        {
            SyncStatus.Synced => "✓",
            SyncStatus.Syncing => "⟳",
            SyncStatus.Failed => "✗",
            SyncStatus.Outdated => "⚠",
            _ => ""
        };

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}

