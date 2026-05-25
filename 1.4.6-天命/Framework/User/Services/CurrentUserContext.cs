using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using TM.Framework.Common.Services;
using TM.Framework.User.Profile.BasicInfo;

namespace TM.Framework.User.Services
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public sealed class CurrentUserContext : INotifyPropertyChanged
    {
        private string _username = string.Empty;
        private string _displayName = string.Empty;
        private string _avatarPath = string.Empty;
        private readonly BasicInfoSettings _basicInfoSettings;

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? UserChanged;

        public CurrentUserContext()
        {
            _basicInfoSettings = ServiceLocator.Get<BasicInfoSettings>();
            Refresh();
        }

        public string Username
        {
            get => _username;
            private set
            {
                if (_username != value)
                {
                    _username = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DisplayName
        {
            get => _displayName;
            private set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayTitle));
                }
            }
        }

        public string AvatarPath
        {
            get => _avatarPath;
            private set
            {
                if (_avatarPath != value)
                {
                    _avatarPath = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DisplayTitle => string.IsNullOrWhiteSpace(DisplayName) ? "用户" : DisplayName;

        public void Refresh()
        {
            try
            {
                var settings = _basicInfoSettings;
                settings.LoadSettings();

                var oldUsername = Username;
                var oldDisplayName = DisplayName;
                var oldAvatarPath = AvatarPath;

                Username = settings.Username;
                DisplayName = settings.DisplayName;
                AvatarPath = settings.AvatarPath;

                if (!string.Equals(oldUsername, Username, StringComparison.Ordinal) ||
                    !string.Equals(oldDisplayName, DisplayName, StringComparison.Ordinal) ||
                    !string.Equals(oldAvatarPath, AvatarPath, StringComparison.Ordinal))
                {
                    UserChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CurrentUserContext] 刷新用户信息失败: {ex.Message}");
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
