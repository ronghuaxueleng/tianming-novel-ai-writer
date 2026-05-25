using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TM.Framework.Common.Helpers.MVVM;

namespace TM.Framework.Appearance.ThemeManagement.ThemeDesign
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ThemeDesignViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private Stack<ThemeSnapshot> _undoStack = new();
        private Stack<ThemeSnapshot> _redoStack = new();
        private const int MaxHistorySize = 50;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

            if (propertyName != null && (propertyName.Contains("Background") || propertyName.Contains("Text")))
            {
                SaveToHistory();
                UpdateContrastWarnings();
            }
        }

        private string _themeName = "自定义主题";
        public string ThemeName
        {
            get => _themeName;
            set { _themeName = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _topBarBackground = new(Color.FromRgb(30, 30, 30));
        public SolidColorBrush TopBarBackground
        {
            get => _topBarBackground;
            set { _topBarBackground = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _topBarText = new(Color.FromRgb(255, 255, 255));
        public SolidColorBrush TopBarText
        {
            get => _topBarText;
            set { _topBarText = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _leftBarBackground = new(Color.FromRgb(25, 25, 25));
        public SolidColorBrush LeftBarBackground
        {
            get => _leftBarBackground;
            set { _leftBarBackground = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _leftBarIconColor = new(Color.FromRgb(100, 150, 250));
        public SolidColorBrush LeftBarIconColor
        {
            get => _leftBarIconColor;
            set { _leftBarIconColor = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _leftWorkspaceBackground = new(Color.FromRgb(35, 35, 35));
        public SolidColorBrush LeftWorkspaceBackground
        {
            get => _leftWorkspaceBackground;
            set { _leftWorkspaceBackground = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _leftWorkspaceText = new(Color.FromRgb(255, 255, 255));
        public SolidColorBrush LeftWorkspaceText
        {
            get => _leftWorkspaceText;
            set { _leftWorkspaceText = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _leftWorkspaceBorder = new(Color.FromRgb(60, 60, 60));
        public SolidColorBrush LeftWorkspaceBorder
        {
            get => _leftWorkspaceBorder;
            set { _leftWorkspaceBorder = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _centerWorkspaceBackground = new(Color.FromRgb(40, 40, 40));
        public SolidColorBrush CenterWorkspaceBackground
        {
            get => _centerWorkspaceBackground;
            set { _centerWorkspaceBackground = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _centerWorkspaceText = new(Color.FromRgb(255, 255, 255));
        public SolidColorBrush CenterWorkspaceText
        {
            get => _centerWorkspaceText;
            set { _centerWorkspaceText = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _centerWorkspaceBorder = new(Color.FromRgb(60, 60, 60));
        public SolidColorBrush CenterWorkspaceBorder
        {
            get => _centerWorkspaceBorder;
            set { _centerWorkspaceBorder = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _rightWorkspaceBackground = new(Color.FromRgb(35, 35, 35));
        public SolidColorBrush RightWorkspaceBackground
        {
            get => _rightWorkspaceBackground;
            set { _rightWorkspaceBackground = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _rightWorkspaceText = new(Color.FromRgb(255, 255, 255));
        public SolidColorBrush RightWorkspaceText
        {
            get => _rightWorkspaceText;
            set { _rightWorkspaceText = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _rightWorkspaceBorder = new(Color.FromRgb(60, 60, 60));
        public SolidColorBrush RightWorkspaceBorder
        {
            get => _rightWorkspaceBorder;
            set { _rightWorkspaceBorder = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _bottomBarBackground = new(Color.FromRgb(20, 20, 20));
        public SolidColorBrush BottomBarBackground
        {
            get => _bottomBarBackground;
            set { _bottomBarBackground = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _bottomBarText = new(Color.FromRgb(255, 255, 255));
        public SolidColorBrush BottomBarText
        {
            get => _bottomBarText;
            set { _bottomBarText = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _primaryButtonColor = new(Color.FromRgb(96, 165, 250));
        public SolidColorBrush PrimaryButtonColor
        {
            get => _primaryButtonColor;
            set { _primaryButtonColor = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _primaryButtonHover = new(Color.FromRgb(59, 130, 246));
        public SolidColorBrush PrimaryButtonHover
        {
            get => _primaryButtonHover;
            set { _primaryButtonHover = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _dangerButtonColor = new(Color.FromRgb(248, 113, 113));
        public SolidColorBrush DangerButtonColor
        {
            get => _dangerButtonColor;
            set { _dangerButtonColor = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _dangerButtonHover = new(Color.FromRgb(239, 68, 68));
        public SolidColorBrush DangerButtonHover
        {
            get => _dangerButtonHover;
            set { _dangerButtonHover = value; OnPropertyChanged(); }
        }

        private ObservableCollection<ContrastWarning> _contrastWarnings = new();
        public ObservableCollection<ContrastWarning> ContrastWarnings
        {
            get => _contrastWarnings;
            set { _contrastWarnings = value; OnPropertyChanged(); }
        }

        private string _contrastSummary = "✅ 所有配色对比度良好";
        public string ContrastSummary
        {
            get => _contrastSummary;
            set { _contrastSummary = value; OnPropertyChanged(); }
        }

        private bool _canUndo = false;
        public bool CanUndo
        {
            get => _canUndo;
            set { _canUndo = value; OnPropertyChanged(); }
        }

        private bool _canRedo = false;
        public bool CanRedo
        {
            get => _canRedo;
            set { _canRedo = value; OnPropertyChanged(); }
        }

        private string _historyInfo = "无历史记录";
        public string HistoryInfo
        {
            get => _historyInfo;
            set { _historyInfo = value; OnPropertyChanged(); }
        }

        public ICommand ApplyThemeCommand { get; }
        public ICommand SaveThemeCommand { get; }
        public ICommand ResetThemeCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }

        private readonly ThemeManager _themeManager;

        public ThemeDesignViewModel(ThemeManager themeManager)
        {
            _themeManager = themeManager;
            ApplyThemeCommand = new RelayCommand(ApplyTheme);
            SaveThemeCommand = new RelayCommand(SaveTheme);
            ResetThemeCommand = new RelayCommand(ResetTheme);
            UndoCommand = new RelayCommand(Undo, () => CanUndo);
            RedoCommand = new RelayCommand(Redo, () => CanRedo);

            UpdateContrastWarnings();
        }

        private async void ApplyTheme()
        {
            var savedFileName = await SaveThemeInternalAsync(showSavedToast: false);
            if (string.IsNullOrWhiteSpace(savedFileName))
                return;

            try
            {
                _themeManager.ApplyThemeFromFile(savedFileName);
                ToastNotification.ShowSuccess("应用成功", $"主题「{ThemeName}」已保存并全局应用");
            }
            catch (Exception ex)
            {
                StandardDialog.ShowError(
                    $"应用主题失败：{ex.Message}",
                    "错误",
                    null
                );
            }
        }

        private async void SaveTheme()
        {
            await SaveThemeInternalAsync(showSavedToast: true);
        }

        private async System.Threading.Tasks.Task<string?> SaveThemeInternalAsync(bool showSavedToast)
        {
            if (string.IsNullOrWhiteSpace(ThemeName))
            {
                StandardDialog.ShowWarning("请输入主题名称！", "提示", null);
                return null;
            }

            try
            {
                var fileName = string.Join("", ThemeName.Split(Path.GetInvalidFileNameChars())) + "Theme.xaml";

                var filePath = StoragePathHelper.GetFilePath(
                    "Framework",
                    "Appearance/ThemeManagement/Themes",
                    fileName
                );

                TM.App.Log($"[ThemeDesign] 保存主题到: {filePath}");

                if (File.Exists(filePath))
                {
                    var result = StandardDialog.ShowConfirm(
                        $"主题 \"{ThemeName}\" 已存在，是否覆盖？",
                        "确认覆盖",
                        null
                    );

                    if (!result)
                        return null;
                }

                var xamlContent = GenerateThemeXaml();
                var tmpTdv = filePath + ".tmp";
                await File.WriteAllTextAsync(tmpTdv, xamlContent, Encoding.UTF8);
                File.Move(tmpTdv, filePath, overwrite: true);

                if (showSavedToast)
                    ToastNotification.ShowSuccess("保存成功", $"主题「{ThemeName}」已保存，请在主题选择中刷新查看");

                return fileName;
            }
            catch (Exception ex)
            {
                StandardDialog.ShowError(
                    $"保存主题失败：{ex.Message}",
                    "错误",
                    null
                );
                return null;
            }
        }

        private string GenerateThemeXaml()
        {
            var sb = new StringBuilder();
            sb.AppendLine("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"");
            sb.AppendLine("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">");
            sb.AppendLine("    ");
            sb.AppendLine($"    <!-- {ThemeName} -->");
            sb.AppendLine("    ");
            sb.AppendLine("    <!-- 背景颜色 -->");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"UnifiedBackground\" Color=\"{GetColorHex(TopBarBackground)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ContentBackground\" Color=\"{GetColorHex(CenterWorkspaceBackground)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"Surface\" Color=\"{GetColorHex(LeftWorkspaceBackground)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ContentHighlight\" Color=\"{GetColorHex(LeftWorkspaceBackground)}\"/>");
            sb.AppendLine("    ");
            sb.AppendLine("    <!-- 边框颜色 -->");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"WindowBorder\" Color=\"{GetColorHex(LeftWorkspaceBorder)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"BorderBrush\" Color=\"{GetColorHex(CenterWorkspaceBorder)}\"/>");
            sb.AppendLine("    ");
            sb.AppendLine("    <!-- 文字颜色 -->");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"TextPrimary\" Color=\"{GetColorHex(CenterWorkspaceText)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"TextSecondary\" Color=\"{GetColorHex(TopBarText)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"TextTertiary\" Color=\"{GetColorHex(BottomBarText)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"TextDisabled\" Color=\"{GetDarkerColor(CenterWorkspaceText, 0.5)}\"/>");
            sb.AppendLine("    ");
            sb.AppendLine("    <!-- 交互状态颜色 -->");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"HoverBackground\" Color=\"{GetLighterColor(LeftBarBackground, 0.2)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ActiveBackground\" Color=\"{GetLighterColor(LeftBarBackground, 0.3)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"SelectedBackground\" Color=\"{GetColorHex(LeftBarIconColor)}\"/>");
            sb.AppendLine("    ");
            sb.AppendLine("    <!-- 主题色 -->");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"PrimaryColor\" Color=\"{GetColorHex(PrimaryButtonColor)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"PrimaryHover\" Color=\"{GetColorHex(PrimaryButtonHover)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"PrimaryActive\" Color=\"{GetDarkerColor(PrimaryButtonHover, 0.2)}\"/>");
            sb.AppendLine("    ");
            sb.AppendLine("    <!-- 功能色 -->");
            sb.AppendLine("    <SolidColorBrush x:Key=\"SuccessColor\" Color=\"#34D399\"/>");
            sb.AppendLine("    <SolidColorBrush x:Key=\"WarningColor\" Color=\"#FBBF24\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"DangerColor\" Color=\"{GetColorHex(DangerButtonColor)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"DangerHover\" Color=\"{GetColorHex(DangerButtonHover)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"InfoColor\" Color=\"{GetColorHex(PrimaryButtonColor)}\"/>");
            sb.AppendLine("    ");
            sb.AppendLine("</ResourceDictionary>");

            return sb.ToString();
        }

        private string GetColorHex(SolidColorBrush brush)
        {
            var color = brush.Color;
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private string GetLighterColor(SolidColorBrush brush, double factor)
        {
            var color = brush.Color;
            var r = Math.Min(255, (int)(color.R + (255 - color.R) * factor));
            var g = Math.Min(255, (int)(color.G + (255 - color.G) * factor));
            var b = Math.Min(255, (int)(color.B + (255 - color.B) * factor));
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        private string GetDarkerColor(SolidColorBrush brush, double factor)
        {
            var color = brush.Color;
            var r = (int)(color.R * (1 - factor));
            var g = (int)(color.G * (1 - factor));
            var b = (int)(color.B * (1 - factor));
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        #region 对比度检查

        private void UpdateContrastWarnings()
        {
            var warnings = new List<ContrastWarning>();

            CheckContrast(warnings, "顶栏", TopBarText, TopBarBackground);
            CheckContrast(warnings, "左工作区", LeftWorkspaceText, LeftWorkspaceBackground);
            CheckContrast(warnings, "中心工作区", CenterWorkspaceText, CenterWorkspaceBackground);
            CheckContrast(warnings, "右工作区", RightWorkspaceText, RightWorkspaceBackground);
            CheckContrast(warnings, "底栏", BottomBarText, BottomBarBackground);

            ContrastWarnings.Clear();
            foreach (var warning in warnings)
            {
                ContrastWarnings.Add(warning);
            }

            if (warnings.Count == 0)
            {
                ContrastSummary = "✅ 所有配色对比度良好";
            }
            else
            {
                var criticalCount = warnings.Count(w => w.Ratio < 3.0);
                var warningCount = warnings.Count(w => w.Ratio >= 3.0 && w.Ratio < 4.5);
                ContrastSummary = $"⚠️ {criticalCount}个严重问题，{warningCount}个警告";
            }
        }

        private void CheckContrast(List<ContrastWarning> warnings, string area, SolidColorBrush text, SolidColorBrush background)
        {
            var ratio = CalculateContrastRatio(text.Color, background.Color);

            if (ratio < 4.5)
            {
                warnings.Add(new ContrastWarning
                {
                    Area = area,
                    Ratio = ratio,
                    Level = ratio < 3.0 ? "严重" : "警告",
                    Recommendation = ratio < 3.0 ? "对比度过低，文字可能无法阅读" : "对比度偏低，建议调整"
                });
            }
        }

        private double CalculateContrastRatio(Color color1, Color color2)
        {
            var l1 = GetRelativeLuminance(color1);
            var l2 = GetRelativeLuminance(color2);

            var lighter = Math.Max(l1, l2);
            var darker = Math.Min(l1, l2);

            return (lighter + 0.05) / (darker + 0.05);
        }

        private double GetRelativeLuminance(Color color)
        {
            var r = GetSRGB(color.R / 255.0);
            var g = GetSRGB(color.G / 255.0);
            var b = GetSRGB(color.B / 255.0);

            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

        private double GetSRGB(double channel)
        {
            return channel <= 0.03928 
                ? channel / 12.92 
                : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        #endregion

        #region 编辑历史

        private void SaveToHistory()
        {
            var snapshot = new ThemeSnapshot
            {
                ThemeName = ThemeName,
                TopBarBackground = CloneBrush(TopBarBackground),
                TopBarText = CloneBrush(TopBarText),
                LeftBarBackground = CloneBrush(LeftBarBackground),
                LeftBarIconColor = CloneBrush(LeftBarIconColor),
                LeftWorkspaceBackground = CloneBrush(LeftWorkspaceBackground),
                LeftWorkspaceText = CloneBrush(LeftWorkspaceText),
                LeftWorkspaceBorder = CloneBrush(LeftWorkspaceBorder),
                CenterWorkspaceBackground = CloneBrush(CenterWorkspaceBackground),
                CenterWorkspaceText = CloneBrush(CenterWorkspaceText),
                CenterWorkspaceBorder = CloneBrush(CenterWorkspaceBorder),
                RightWorkspaceBackground = CloneBrush(RightWorkspaceBackground),
                RightWorkspaceText = CloneBrush(RightWorkspaceText),
                RightWorkspaceBorder = CloneBrush(RightWorkspaceBorder),
                BottomBarBackground = CloneBrush(BottomBarBackground),
                BottomBarText = CloneBrush(BottomBarText),
                PrimaryButtonColor = CloneBrush(PrimaryButtonColor),
                PrimaryButtonHover = CloneBrush(PrimaryButtonHover),
                DangerButtonColor = CloneBrush(DangerButtonColor),
                DangerButtonHover = CloneBrush(DangerButtonHover)
            };

            _undoStack.Push(snapshot);

            if (_undoStack.Count > MaxHistorySize)
            {
                var temp = new Stack<ThemeSnapshot>(_undoStack.Reverse().Skip(_undoStack.Count - MaxHistorySize));
                _undoStack = new Stack<ThemeSnapshot>(temp.Reverse());
            }

            _redoStack.Clear();
            UpdateHistoryState();
        }

        private void Undo()
        {
            if (_undoStack.Count == 0) return;

            var currentSnapshot = CreateCurrentSnapshot();
            _redoStack.Push(currentSnapshot);

            var snapshot = _undoStack.Pop();
            RestoreSnapshot(snapshot);

            UpdateHistoryState();
        }

        private void Redo()
        {
            if (_redoStack.Count == 0) return;

            var currentSnapshot = CreateCurrentSnapshot();
            _undoStack.Push(currentSnapshot);

            var snapshot = _redoStack.Pop();
            RestoreSnapshot(snapshot);

            UpdateHistoryState();
        }

        private ThemeSnapshot CreateCurrentSnapshot()
        {
            return new ThemeSnapshot
            {
                ThemeName = ThemeName,
                TopBarBackground = CloneBrush(TopBarBackground),
                TopBarText = CloneBrush(TopBarText),
                LeftBarBackground = CloneBrush(LeftBarBackground),
                LeftBarIconColor = CloneBrush(LeftBarIconColor),
                LeftWorkspaceBackground = CloneBrush(LeftWorkspaceBackground),
                LeftWorkspaceText = CloneBrush(LeftWorkspaceText),
                LeftWorkspaceBorder = CloneBrush(LeftWorkspaceBorder),
                CenterWorkspaceBackground = CloneBrush(CenterWorkspaceBackground),
                CenterWorkspaceText = CloneBrush(CenterWorkspaceText),
                CenterWorkspaceBorder = CloneBrush(CenterWorkspaceBorder),
                RightWorkspaceBackground = CloneBrush(RightWorkspaceBackground),
                RightWorkspaceText = CloneBrush(RightWorkspaceText),
                RightWorkspaceBorder = CloneBrush(RightWorkspaceBorder),
                BottomBarBackground = CloneBrush(BottomBarBackground),
                BottomBarText = CloneBrush(BottomBarText),
                PrimaryButtonColor = CloneBrush(PrimaryButtonColor),
                PrimaryButtonHover = CloneBrush(PrimaryButtonHover),
                DangerButtonColor = CloneBrush(DangerButtonColor),
                DangerButtonHover = CloneBrush(DangerButtonHover)
            };
        }

        private void RestoreSnapshot(ThemeSnapshot snapshot)
        {
            ThemeName = snapshot.ThemeName;
            TopBarBackground = snapshot.TopBarBackground;
            TopBarText = snapshot.TopBarText;
            LeftBarBackground = snapshot.LeftBarBackground;
            LeftBarIconColor = snapshot.LeftBarIconColor;
            LeftWorkspaceBackground = snapshot.LeftWorkspaceBackground;
            LeftWorkspaceText = snapshot.LeftWorkspaceText;
            LeftWorkspaceBorder = snapshot.LeftWorkspaceBorder;
            CenterWorkspaceBackground = snapshot.CenterWorkspaceBackground;
            CenterWorkspaceText = snapshot.CenterWorkspaceText;
            CenterWorkspaceBorder = snapshot.CenterWorkspaceBorder;
            RightWorkspaceBackground = snapshot.RightWorkspaceBackground;
            RightWorkspaceText = snapshot.RightWorkspaceText;
            RightWorkspaceBorder = snapshot.RightWorkspaceBorder;
            BottomBarBackground = snapshot.BottomBarBackground;
            BottomBarText = snapshot.BottomBarText;
            PrimaryButtonColor = snapshot.PrimaryButtonColor;
            PrimaryButtonHover = snapshot.PrimaryButtonHover;
            DangerButtonColor = snapshot.DangerButtonColor;
            DangerButtonHover = snapshot.DangerButtonHover;
        }

        private void UpdateHistoryState()
        {
            CanUndo = _undoStack.Count > 0;
            CanRedo = _redoStack.Count > 0;
            HistoryInfo = $"可撤销: {_undoStack.Count} | 可重做: {_redoStack.Count}";

            CommandManager.InvalidateRequerySuggested();
        }

        private SolidColorBrush CloneBrush(SolidColorBrush brush)
        {
            return new SolidColorBrush(brush.Color);
        }

        #endregion

        private void ResetTheme()
        {
            TopBarBackground = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            TopBarText = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            LeftBarBackground = new SolidColorBrush(Color.FromRgb(25, 25, 25));
            LeftBarIconColor = new SolidColorBrush(Color.FromRgb(100, 150, 250));
            LeftWorkspaceBackground = new SolidColorBrush(Color.FromRgb(35, 35, 35));
            LeftWorkspaceText = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            LeftWorkspaceBorder = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            CenterWorkspaceBackground = new SolidColorBrush(Color.FromRgb(40, 40, 40));
            CenterWorkspaceText = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            CenterWorkspaceBorder = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            RightWorkspaceBackground = new SolidColorBrush(Color.FromRgb(35, 35, 35));
            RightWorkspaceText = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            RightWorkspaceBorder = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            BottomBarBackground = new SolidColorBrush(Color.FromRgb(20, 20, 20));
            BottomBarText = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            PrimaryButtonColor = new SolidColorBrush(Color.FromRgb(96, 165, 250));
            PrimaryButtonHover = new SolidColorBrush(Color.FromRgb(59, 130, 246));
            DangerButtonColor = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            DangerButtonHover = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            ThemeName = "自定义主题";
        }
    }

    public class ContrastWarning
    {
        public string Area { get; set; } = "";
        public double Ratio { get; set; }
        public string Level { get; set; } = "";
        public string Recommendation { get; set; } = "";

        public string DisplayRatio => $"{Ratio:F2}:1";
        public string Icon => Level == "严重" ? "❌" : "⚠️";
    }

    public class ThemeSnapshot
    {
        public string ThemeName { get; set; } = "";
        public SolidColorBrush TopBarBackground { get; set; } = new(Colors.Black);
        public SolidColorBrush TopBarText { get; set; } = new(Colors.White);
        public SolidColorBrush LeftBarBackground { get; set; } = new(Colors.Black);
        public SolidColorBrush LeftBarIconColor { get; set; } = new(Colors.White);
        public SolidColorBrush LeftWorkspaceBackground { get; set; } = new(Colors.Black);
        public SolidColorBrush LeftWorkspaceText { get; set; } = new(Colors.White);
        public SolidColorBrush LeftWorkspaceBorder { get; set; } = new(Colors.Gray);
        public SolidColorBrush CenterWorkspaceBackground { get; set; } = new(Colors.Black);
        public SolidColorBrush CenterWorkspaceText { get; set; } = new(Colors.White);
        public SolidColorBrush CenterWorkspaceBorder { get; set; } = new(Colors.Gray);
        public SolidColorBrush RightWorkspaceBackground { get; set; } = new(Colors.Black);
        public SolidColorBrush RightWorkspaceText { get; set; } = new(Colors.White);
        public SolidColorBrush RightWorkspaceBorder { get; set; } = new(Colors.Gray);
        public SolidColorBrush BottomBarBackground { get; set; } = new(Colors.Black);
        public SolidColorBrush BottomBarText { get; set; } = new(Colors.White);
        public SolidColorBrush PrimaryButtonColor { get; set; } = new(Colors.Blue);
        public SolidColorBrush PrimaryButtonHover { get; set; } = new(Colors.DarkBlue);
        public SolidColorBrush DangerButtonColor { get; set; } = new(Colors.Red);
        public SolidColorBrush DangerButtonHover { get; set; } = new(Colors.DarkRed);
    }
}

