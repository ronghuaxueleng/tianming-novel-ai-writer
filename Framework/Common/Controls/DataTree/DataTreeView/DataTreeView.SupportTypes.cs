using System;
using System.Collections.Generic;
using System.Reflection;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TM.Framework.Common.ViewModels;

namespace TM.Framework.Common.Controls
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class TreeNodeItem : INotifyPropertyChanged
    {
        private static readonly Color LevelNeutralColor = Color.FromRgb(0x6F, 0x74, 0x88);
        private static readonly Color LevelBlueBaseColor = Color.FromRgb(0x3B, 0x7D, 0xE2);
        private static readonly Color LevelBluePulseColor = Color.FromRgb(0x00, 0xB4, 0xD8);
        private static readonly Color LevelGreenBaseColor = Color.FromRgb(0x10, 0xB9, 0x81);
        private static readonly Color LevelGreenPulseColor = Color.FromRgb(0x8C, 0xF8, 0xBC);
        private static readonly TimeSpan BluePulseDuration = TimeSpan.FromSeconds(1.6);
        private static readonly TimeSpan GreenPulseDuration = TimeSpan.FromSeconds(1.2);

        private static readonly TimeSpan NameBluePulseDuration = TimeSpan.FromSeconds(1.6);
        private static readonly TimeSpan NameGreenPulseDuration = TimeSpan.FromSeconds(1.9);

        private static readonly ColorAnimation _levelGreenAnimation = CreateFrozenColorAnim(LevelGreenPulseColor, GreenPulseDuration);
        private static readonly ColorAnimation _levelBlueAnimation = CreateFrozenColorAnim(LevelBluePulseColor, BluePulseDuration);
        private static ColorAnimation CreateFrozenColorAnim(Color to, TimeSpan dur)
        {
            var a = new ColorAnimation { To = to, Duration = new Duration(dur), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            a.Freeze();
            return a;
        }

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (!_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[TreeNodeItem] {key}: {ex.Message}");
        }
        private static readonly TimeSpan NameGrayPulseDuration = TimeSpan.FromSeconds(1.6);

        private static LinearGradientBrush? _nameBlueFlowBrush;
        private static TranslateTransform? _nameBlueFlowTransform;
        private static LinearGradientBrush? _nameGreenFlowBrush;
        private static TranslateTransform? _nameGreenFlowTransform;
        private static LinearGradientBrush? _nameGrayFlowBrush;
        private static TranslateTransform? _nameGrayFlowTransform;
        private static Brush? _cachedTextPrimaryBrush;
        private static readonly SolidColorBrush FrozenNeutralBrush = CreateFrozenBrush(LevelNeutralColor);
        private static SolidColorBrush CreateFrozenBrush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        private bool _isExpanded;
        private bool _isSelected;
        private bool _isSelectionFocus;
        private bool _isDragging;
        private bool _isDragOver;
        private bool _isSiblingBranch;
        private Brush _nameBrush;
        private string _name = string.Empty;
        private ImageSource? _icon;
        private ImageSource? _logoImage = null;
        private bool _showChildCount = true;
        private bool _isFileSystemNode = false;
        private int _level = 1;

        private enum LevelBrushState { Neutral, Blue, Green }
        private LevelBrushState _levelBrushState = LevelBrushState.Neutral;

        private SolidColorBrush? _levelBrush;

        public TreeNodeItem()
        {
            _nameBrush = FrozenNeutralBrush;
            UpdateNameBrush();
        }

        public SolidColorBrush LevelBrush
        {
            get => _levelBrush ??= new SolidColorBrush(LevelNeutralColor);
        }

        public Brush NameBrush
        {
            get => _nameBrush;
            private set
            {
                if (!ReferenceEquals(_nameBrush, value))
                {
                    _nameBrush = value;
                    OnPropertyChanged();
                }
            }
        }

        private static Color TryGetResourceColor(string key, Color fallback)
        {
            try
            {
                if (Application.Current?.TryFindResource(key) is SolidColorBrush brush)
                {
                    return brush.Color;
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(TryGetResourceColor), ex);
                return fallback;
            }

            return fallback;
        }

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        public ImageSource? Icon
        {
            get => _icon;
            set
            {
                if (!ReferenceEquals(_icon, value))
                {
                    _icon = value;
                    OnPropertyChanged();
                }
            }
        }

        public ImageSource? LogoImage
        {
            get => _logoImage;
            set
            {
                if (_logoImage != value)
                {
                    _logoImage = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasLogoImage));
                }
            }
        }

        public bool HasLogoImage => _logoImage != null;

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                    UpdateLevelBrush();
                    UpdateNameBrush();
                }
            }
        }

        public bool IsSelectionFocus
        {
            get => _isSelectionFocus;
            set
            {
                if (_isSelectionFocus != value)
                {
                    _isSelectionFocus = value;
                    OnPropertyChanged();
                    UpdateLevelBrush();
                    UpdateNameBrush();
                }
            }
        }

        public bool IsDragging
        {
            get => _isDragging;
            set
            {
                if (_isDragging != value)
                {
                    _isDragging = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsDragOver
        {
            get => _isDragOver;
            set
            {
                if (_isDragOver != value)
                {
                    _isDragOver = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSiblingBranch
        {
            get => _isSiblingBranch;
            set
            {
                if (_isSiblingBranch != value)
                {
                    _isSiblingBranch = value;
                    OnPropertyChanged();
                    UpdateNameBrush();
                }
            }
        }

        public bool ShowChildCount
        {
            get => _showChildCount;
            set
            {
                if (_showChildCount != value)
                {
                    _showChildCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsFileSystemNode
        {
            get => _isFileSystemNode;
            set
            {
                if (_isFileSystemNode != value)
                {
                    _isFileSystemNode = value;
                    OnPropertyChanged();
                }
            }
        }

        public int Level
        {
            get => _level;
            set
            {
                if (_level != value)
                {
                    _level = value;
                    OnPropertyChanged();
                    UpdateLevelBrush();
                    UpdateNameBrush();
                }
            }
        }

        private void UpdateNameBrush()
        {
            if (IsSelectionFocus)
            {
                NameBrush = GetOrCreateNameGreenFlowBrush();
                return;
            }

            if (IsSelected)
            {
                NameBrush = GetOrCreateNameBlueFlowBrush();
                return;
            }

            if (IsSiblingBranch)
            {
                NameBrush = GetOrCreateNameGrayFlowBrush();
                return;
            }

            if (_cachedTextPrimaryBrush == null
                && System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                _cachedTextPrimaryBrush = TryGetResourceBrush("TextPrimary", Brushes.Black);
            }
            NameBrush = _cachedTextPrimaryBrush ?? FrozenNeutralBrush;
        }

        internal static void InvalidateStaticBrushCache()
        {
            _cachedTextPrimaryBrush = null;
            _nameBlueFlowBrush = null;
            _nameBlueFlowTransform = null;
            _nameGreenFlowBrush = null;
            _nameGreenFlowTransform = null;
            _nameGrayFlowBrush = null;
            _nameGrayFlowTransform = null;
        }

        private static Brush TryGetResourceBrush(string key, Brush fallback)
        {
            try
            {
                if (Application.Current?.TryFindResource(key) is Brush brush)
                {
                    return brush;
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(TryGetResourceBrush), ex);
                return fallback;
            }

            return fallback;
        }

        private static LinearGradientBrush GetOrCreateNameBlueFlowBrush()
        {
            if (_nameBlueFlowBrush != null)
            {
                return _nameBlueFlowBrush;
            }

            var baseColor = TryGetResourceColor("PrimaryColor", LevelBlueBaseColor);
            var pulseColor = LevelBluePulseColor;
            var brighterPulseColor = GetBrighterPulseColor(pulseColor);

            _nameBlueFlowTransform = new TranslateTransform();
            _nameBlueFlowBrush = CreateHorizontalFlowBrush(baseColor, pulseColor, brighterPulseColor, _nameBlueFlowTransform);
            StartFlowAnimation(_nameBlueFlowTransform, NameBluePulseDuration);

            return _nameBlueFlowBrush;
        }

        private static LinearGradientBrush GetOrCreateNameGreenFlowBrush()
        {
            if (_nameGreenFlowBrush != null)
            {
                return _nameGreenFlowBrush;
            }

            var baseColor = TryGetResourceColor("SuccessColor", LevelGreenBaseColor);
            var pulseColor = LevelGreenPulseColor;
            var brighterPulseColor = GetBrighterPulseColor(pulseColor);

            _nameGreenFlowTransform = new TranslateTransform();
            _nameGreenFlowBrush = CreateHorizontalFlowBrush(baseColor, pulseColor, brighterPulseColor, _nameGreenFlowTransform);
            StartFlowAnimation(_nameGreenFlowTransform, NameGreenPulseDuration);

            return _nameGreenFlowBrush;
        }

        private static LinearGradientBrush GetOrCreateNameGrayFlowBrush()
        {
            if (_nameGrayFlowBrush != null)
            {
                return _nameGrayFlowBrush;
            }

            var baseColor = TryGetResourceColor("TextTertiary", LevelNeutralColor);
            var pulseColor = TryGetResourceColor("TextSecondary", LevelNeutralColor);
            var brighterPulseColor = GetBrighterPulseColor(pulseColor);

            _nameGrayFlowTransform = new TranslateTransform();
            _nameGrayFlowBrush = CreateHorizontalFlowBrush(baseColor, pulseColor, brighterPulseColor, _nameGrayFlowTransform);
            StartFlowAnimation(_nameGrayFlowTransform, NameGrayPulseDuration);

            return _nameGrayFlowBrush;
        }

        private static LinearGradientBrush CreateHorizontalFlowBrush(Color baseColor, Color pulseColor, Color brighterPulseColor, TranslateTransform transform)
        {
            var brush = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.RelativeToBoundingBox,
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                SpreadMethod = GradientSpreadMethod.Repeat,
                RelativeTransform = transform
            };

            brush.GradientStops.Add(new GradientStop(baseColor, 0));
            brush.GradientStops.Add(new GradientStop(baseColor, 0.40));
            brush.GradientStops.Add(new GradientStop(pulseColor, 0.47));
            brush.GradientStops.Add(new GradientStop(brighterPulseColor, 0.5));
            brush.GradientStops.Add(new GradientStop(pulseColor, 0.53));
            brush.GradientStops.Add(new GradientStop(baseColor, 0.60));
            brush.GradientStops.Add(new GradientStop(baseColor, 1));

            return brush;
        }

        private static void StartFlowAnimation(TranslateTransform transform, TimeSpan duration)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);

            var realDuration = duration <= TimeSpan.Zero
                ? TimeSpan.FromSeconds(2)
                : duration;

            var animation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(realDuration),
                RepeatBehavior = RepeatBehavior.Forever
            };
            animation.Freeze();

            transform.BeginAnimation(TranslateTransform.XProperty, animation);
        }

        private static Color GetBrighterPulseColor(Color pulseColor)
        {
            static byte Boost(byte value)
            {
                const int delta = 45;
                return (byte)Math.Min(byte.MaxValue, value + delta);
            }

            return Color.FromRgb(Boost(pulseColor.R), Boost(pulseColor.G), Boost(pulseColor.B));
        }

        private string? _statusBadge;

        public string? StatusBadge
        {
            get => _statusBadge;
            set
            {
                if (_statusBadge != value)
                {
                    _statusBadge = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasStatusBadge));
                }
            }
        }

        public bool HasStatusBadge => !string.IsNullOrEmpty(_statusBadge);

        private bool _showLevelIndicator = true;

        public bool ShowLevelIndicator
        {
            get => _showLevelIndicator;
            set
            {
                if (_showLevelIndicator != value)
                {
                    _showLevelIndicator = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _showIcon = true;

        public bool ShowIcon
        {
            get => _showIcon && !HasLogoImage;
            set
            {
                if (_showIcon != value)
                {
                    _showIcon = value;
                    OnPropertyChanged();
                }
            }
        }

        public RangeObservableCollection<TreeNodeItem> Children { get; set; } = new();

        public object? Tag { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void UpdateLevelBrush()
        {
            if (_levelBrush == null)
            {
                return;
            }

            if (IsSelectionFocus)
            {
                if (_levelBrushState == LevelBrushState.Green) return;
                _levelBrushState = LevelBrushState.Green;
                LevelBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                LevelBrush.Color = LevelGreenBaseColor;
                LevelBrush.BeginAnimation(SolidColorBrush.ColorProperty, _levelGreenAnimation);
            }
            else if (IsSelected)
            {
                if (_levelBrushState == LevelBrushState.Blue) return;
                _levelBrushState = LevelBrushState.Blue;
                LevelBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                LevelBrush.Color = LevelBlueBaseColor;
                LevelBrush.BeginAnimation(SolidColorBrush.ColorProperty, _levelBlueAnimation);
            }
            else
            {
                if (_levelBrushState == LevelBrushState.Neutral) return;
                _levelBrushState = LevelBrushState.Neutral;
                LevelBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                LevelBrush.Color = LevelNeutralColor;
            }
        }
    }

    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class TreeNodeCountVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length != 3 ||
                values[0] is not bool showChildCount ||
                values[1] is not bool isFileSystemNode ||
                values[2] is not int childrenCount)
            {
                return Visibility.Collapsed;
            }

            if (!showChildCount)
            {
                return Visibility.Collapsed;
            }

            if (isFileSystemNode)
            {
                return Visibility.Visible;
            }

            return childrenCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class TagIsDeletableVisibilityConverter : IValueConverter
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.PropertyInfo?> _isBuiltInPropCache = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                object? tag = value;

                if (value is TreeNodeItem node)
                {
                    tag = node.Tag;
                }

                if (tag == null)
                {
                    return Visibility.Collapsed;
                }

                if (tag is TM.Framework.Common.Models.ICategory cat)
                {
                    return !cat.IsBuiltIn ? Visibility.Visible : Visibility.Collapsed;
                }

                if (tag is TM.Framework.Common.Models.IDataItem dataItem)
                {
                    if (string.IsNullOrWhiteSpace(dataItem.Id))
                        return Visibility.Collapsed;

                    var tagType = tag.GetType();
                    var isBuiltInProp = _isBuiltInPropCache.GetOrAdd(tagType, t =>
                    {
                        var prop = t.GetProperty("IsBuiltIn");
                        return prop?.PropertyType == typeof(bool) ? prop : null;
                    });
                    if (isBuiltInProp != null)
                    {
                        var isBuiltIn = (bool)(isBuiltInProp.GetValue(tag) ?? false);
                        return isBuiltIn ? Visibility.Collapsed : Visibility.Visible;
                    }

                    return TM.Framework.Common.Helpers.Id.ShortIdGenerator.IsLikelyId(dataItem.Id)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }

                return Visibility.Collapsed;
            }
            catch
            {
                return Visibility.Collapsed;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class TreeNodeTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? ParentTemplate { get; set; }
        public DataTemplate? LeafTemplate { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            if (item is TreeNodeItem node && node.Level >= 3)
                return LeafTemplate;
            return ParentTemplate;
        }
    }
}
