using System.ComponentModel;
using System.Reflection;

namespace TM.Framework.Common.Controls.DataManagement
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class TabItemData : INotifyPropertyChanged
    {
        private string? _header;
        private object? _icon;
        private bool _isSelected;
        private bool _isEnabled = true;
        private object? _content;

        public string? Header
        {
            get => _header;
            set { if (_header != value) { _header = value; OnPropertyChanged(nameof(Header)); } }
        }

        public object? Icon
        {
            get => _icon;
            set { if (_icon != value) { _icon = value; OnPropertyChanged(nameof(Icon)); } }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set { if (_isEnabled != value) { _isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); } }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); } }
        }

        public object? Content
        {
            get => _content;
            set { if (_content != value) { _content = value; OnPropertyChanged(nameof(Content)); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
