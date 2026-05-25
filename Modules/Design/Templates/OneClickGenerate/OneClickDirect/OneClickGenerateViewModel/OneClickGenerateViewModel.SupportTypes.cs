using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TM.Modules.Design.Templates.OneClickGenerate.OneClickDirect
{
    internal class PipelineStepState
    {
        public int StepIndex { get; set; }
        public string Status { get; set; } = "Pending";
        public int GeneratedCount { get; set; }
        public int TotalCount { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public int Count { get; set; }
        public Dictionary<string, string> ExtraFields { get; set; } = new();
    }

    #region 辅助模型

    public enum StepStatus { Pending, Running, Completed, Failed, Skipped, Cancelled }

    public record PipelineStepDefinition(
        int StepIndex,
        string DisplayName,
        ImageSource? Icon,
        Type ViewType,
        bool HasExtraFields,
        ExtraFieldDef[] ExtraFieldDefs,
        bool HideCount = false,
        string TitleColorGroup = "Template",
        bool AutoExpandCategories = false,
        string CategoryHint = "",
        bool HideCategory = false,
        int RequiredPreviousStepIndex = 0);

    public record ExtraFieldDef(string Key, string Label, bool IsRequired, bool IsDropdown = false);

    public class PipelineStepViewModel : INotifyPropertyChanged
    {
        public PipelineStepDefinition Definition { get; }

        public int StepIndex => Definition.StepIndex;
        public string DisplayName => Definition.DisplayName;
        public ImageSource? Icon => Definition.Icon;
        public string TitleColorGroup => Definition.TitleColorGroup;
        public bool AutoExpandCategories => Definition.AutoExpandCategories;
        public string CategoryHint => Definition.CategoryHint;

        private bool _showCountInput = true;
        public bool ShowCountInput
        {
            get => _showCountInput;
            set { _showCountInput = value; OnPropertyChanged(); }
        }

        private bool _showCategoryInput = true;
        public bool ShowCategoryInput
        {
            get => _showCategoryInput;
            set { _showCategoryInput = value; OnPropertyChanged(); }
        }

        public string DynamicCategoryHint
        {
            get
            {
                if (!AutoExpandCategories) return Definition.CategoryHint;
                var count = AvailableCategories.Count;
                if (count == 0) return "等待前序步骤完成后，将自动展开全量生成";
                return $"{Definition.CategoryHint}（共 {count} 卷）";
            }
        }

        public ObservableCollection<string> AvailableCategories { get; } = new TM.Framework.Common.ViewModels.RangeObservableCollection<string>();

        private string _categoryName = string.Empty;
        public string CategoryName
        {
            get => _categoryName;
            set { _categoryName = value; OnPropertyChanged(); }
        }

        private int _count;
        public int Count
        {
            get => _count;
            set { _count = Math.Max(0, value); OnPropertyChanged(); }
        }

        public ObservableCollection<ExtraFieldViewModel> ExtraFields { get; } = new();

        private StepStatus _status = StepStatus.Pending;
        public StepStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusIcon)); OnPropertyChanged(nameof(IsRunning)); }
        }

        public bool IsRunning => Status == StepStatus.Running;

        public ImageSource? StatusIcon => Status switch
        {
            StepStatus.Pending => IconHelper.TryGet("Icon.Clock"),
            StepStatus.Running => IconHelper.TryGet("Icon.Refresh"),
            StepStatus.Completed => IconHelper.TryGet("Icon.CheckCircle"),
            StepStatus.Failed => IconHelper.TryGet("Icon.Forbidden"),
            StepStatus.Skipped => IconHelper.TryGet("Icon.ChevronRight"),
            StepStatus.Cancelled => IconHelper.TryGet("Icon.Warning"),
            _ => IconHelper.TryGet("Icon.Clock"),
        };

        private int _totalCount;
        public int TotalCount
        {
            get => _totalCount;
            set { _totalCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercent)); OnPropertyChanged(nameof(ProgressText)); }
        }

        private int _generatedCount;
        public int GeneratedCount
        {
            get => _generatedCount;
            set { _generatedCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercent)); OnPropertyChanged(nameof(ProgressText)); }
        }

        public int ProgressPercent => TotalCount > 0 ? (int)((double)GeneratedCount / TotalCount * 100) : 0;
        public string ProgressText => AutoExpandCategories && TotalCount > 0
            ? $"{GeneratedCount / 100} / {TotalCount / 100} 卷"
            : $"{GeneratedCount} / {TotalCount}";

        public PipelineStepViewModel(PipelineStepDefinition def)
        {
            Definition = def;
            _categoryName = string.Empty;

            foreach (var f in def.ExtraFieldDefs)
            {
                ExtraFields.Add(new ExtraFieldViewModel(f));
            }

            AvailableCategories.CollectionChanged += (_, __) => OnPropertyChanged(nameof(DynamicCategoryHint));
        }

        public Dictionary<string, string> BuildPrefilledFields()
        {
            var dict = new Dictionary<string, string>();
            foreach (var field in ExtraFields)
            {
                if (!string.IsNullOrWhiteSpace(field.Value))
                    dict[field.Key] = field.Value;
            }
            return dict;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ExtraFieldViewModel : INotifyPropertyChanged
    {
        public string Key { get; }
        public string Label { get; }
        public bool IsRequired { get; }
        public bool IsDropdown { get; }
        public string RequiredMark => IsRequired ? " *" : "";

        public ObservableCollection<string> AvailableOptions { get; } = new TM.Framework.Common.ViewModels.RangeObservableCollection<string>();
        public bool HasOptions => AvailableOptions.Count > 0;

        private string _value = string.Empty;
        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); }
        }

        public ExtraFieldViewModel(ExtraFieldDef def)
        {
            Key = def.Key;
            Label = def.Label;
            IsRequired = def.IsRequired;
            IsDropdown = def.IsDropdown;
        }

        public void RefreshHasOptions() => OnPropertyChanged(nameof(HasOptions));

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public ImageSource? Icon { get; set; }
        public string Message { get; set; } = "";
    }

    [ValueConversion(typeof(int), typeof(string))]
    public class ZeroToEmptyStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is int i ? i.ToString() : "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => int.TryParse(value as string, out var n) ? (object)Math.Max(0, n) : 0;
    }

    #endregion
}
