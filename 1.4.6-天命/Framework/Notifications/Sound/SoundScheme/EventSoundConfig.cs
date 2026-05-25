using System;
using System.Reflection;
using System.ComponentModel;

namespace TM.Framework.Notifications.Sound.SoundScheme
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class EventSoundConfig : INotifyPropertyChanged
    {
        private string _selectedSound = "默认提示音";

        public event PropertyChangedEventHandler? PropertyChanged;

        public string EventName { get; set; } = string.Empty;

        public string SelectedSound
        {
            get => _selectedSound;
            set
            {
                if (_selectedSound != value)
                {
                    _selectedSound = value;
                    OnPropertyChanged(nameof(SelectedSound));
                }
            }
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

