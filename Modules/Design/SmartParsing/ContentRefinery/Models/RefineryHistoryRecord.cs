using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TM.Modules.Design.SmartParsing.ContentRefinery.Models
{
    public class RefineryHistoryRecord : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }
        public string Id { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public string RawContentPreview { get; set; } = string.Empty;

        public string ResultsSummary { get; set; } = string.Empty;

        public RefineryModuleType TargetModule { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string TargetModuleDisplayName => TargetModule switch
        {
            RefineryModuleType.WorldRules => "世界观规则",
            RefineryModuleType.CharacterRules => "角色规则",
            RefineryModuleType.FactionRules => "势力规则",
            RefineryModuleType.LocationRules => "位置规则",
            RefineryModuleType.PlotRules => "剧情规则",
            _ => TargetModule.ToString()
        };

        public string TargetCategoryName { get; set; } = string.Empty;

        public int ResultCount { get; set; }

        public bool IsCommitted { get; set; }
    }
}
