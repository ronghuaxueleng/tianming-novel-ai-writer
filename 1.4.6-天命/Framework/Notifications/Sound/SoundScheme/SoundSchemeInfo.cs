using System;
using System.Reflection;
using System.ComponentModel;

namespace TM.Framework.Notifications.Sound.SoundScheme
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class SoundSchemeInfo : INotifyPropertyChanged
    {
        private bool _isActive;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string SchemeId { get; set; } = string.Empty;

        public string SchemeName { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public bool IsBuiltIn { get; set; } = true;

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    OnPropertyChanged(nameof(IsActive));
                }
            }
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

