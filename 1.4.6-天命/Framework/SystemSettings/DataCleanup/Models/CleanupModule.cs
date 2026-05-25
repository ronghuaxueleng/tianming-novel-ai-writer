using System.Collections.Generic;
using System.Reflection;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TM.Framework.SystemSettings.DataCleanup.Models
{
    [System.Reflection.Obfuscation(Exclude = true)]
    public enum RiskLevel
    {
        Low,
        Medium,
        High
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class CleanupModule : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _isSelected;
        private bool _isExpanded = true;

        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        public string Icon { get; set; } = "";

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                    if (Items != null)
                    {
                        foreach (var item in Items)
                        {
                            item.IsSelected = value;
                        }
                    }
                }
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); } }
        }

        public bool IsDangerous { get; set; }

        public string Description { get; set; } = "";

        public string Layer { get; set; } = "";

        public List<CleanupItem> Items { get; set; } = new();

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class CleanupItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _isSelected;

        public string Name { get; set; } = "";

        public string FilePath { get; set; } = "";

        public bool IsDirectory { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        public RiskLevel RiskLevel { get; set; } = RiskLevel.Low;

        public string WarningMessage { get; set; } = "";

        public CleanupMethod CleanupMethod { get; set; } = CleanupMethod.ClearContent;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum CleanupMethod
    {
        ClearContent,
        DeleteFile,
        ClearDirectory,
        DeleteNonBuiltIn,
        ClearProjectCategories,
        ClearModelCategoriesKeepLevel1,
        ClearProjectVolumesAndChapters,
        ClearProjectConfigData,
        ClearProjectHistory
    }
}
