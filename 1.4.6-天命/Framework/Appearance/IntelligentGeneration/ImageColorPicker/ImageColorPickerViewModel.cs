using System;
using System.Reflection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TM.Framework.Appearance.ThemeManagement;
using TM.Framework.Common.Services;

namespace TM.Framework.Appearance.IntelligentGeneration.ImageColorPicker
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ImageColorPickerViewModel : INotifyPropertyChanged
    {
        private readonly ThemeManager _themeManager;
        private BitmapImage? _currentImage;
        private string _imageInfo = "未上传图片";
        private string _analysisText = "上传图片后，这里会显示图片分析信息";
        private bool _hasImage;

        public BitmapImage? CurrentImage
        {
            get => _currentImage;
            set
            {
                _currentImage = value;
                OnPropertyChanged(nameof(CurrentImage));
            }
        }

        public string ImageInfo
        {
            get => _imageInfo;
            set
            {
                _imageInfo = value;
                OnPropertyChanged(nameof(ImageInfo));
            }
        }

        public string AnalysisText
        {
            get => _analysisText;
            set
            {
                _analysisText = value;
                OnPropertyChanged(nameof(AnalysisText));
            }
        }

        public bool HasImage
        {
            get => _hasImage;
            set
            {
                _hasImage = value;
                OnPropertyChanged(nameof(HasImage));
            }
        }

        public ObservableCollection<ExtractedColorCard> ExtractedColors { get; } = new();

        public RelayCommand UploadImageCommand { get; }
        public RelayCommand ClearRecordsCommand { get; }
        public RelayCommand GenerateThemeCommand { get; }
        public RelayCommand AddThemeCommand { get; }

        private ThemeColors? _generatedTheme;
        private string _generatedThemeName = string.Empty;

        public ImageColorPickerViewModel(ThemeManager themeManager)
        {
            _themeManager = themeManager;
            UploadImageCommand = new RelayCommand(UploadImage);
            ClearRecordsCommand = new RelayCommand(ClearRecords);
            GenerateThemeCommand = new RelayCommand(GenerateThemeAuto);
            AddThemeCommand = new RelayCommand(AddTheme);
        }

        private void UploadImage()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择图片",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.webp|所有文件|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(dialog.FileName);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();

                    CurrentImage = bitmap;
                    HasImage = true;

                    var fileInfo = new FileInfo(dialog.FileName);
                    var fileSize = fileInfo.Length / 1024.0;
                    ImageInfo = $"{fileInfo.Name} | {bitmap.PixelWidth}×{bitmap.PixelHeight} | {fileSize:F1} KB";

                    ExtractColors(bitmap);

                    AnalyzeImage(bitmap);

                    TM.App.Log($"[ImageColorPicker] 图片加载成功: {dialog.FileName}");
                }
                catch (Exception ex)
                {
                    StandardDialog.ShowError(
                        $"图片加载失败：{ex.Message}",
                        "错误",
                        null
                    );
                    TM.App.Log($"[ImageColorPicker] 图片加载失败: {ex.Message}");
                }
            }
        }

        private void ExtractColors(BitmapImage bitmap)
        {
            try
            {
                ExtractedColors.Clear();

                var colors = ColorExtractor.ExtractPalette(bitmap, 5);

                for (int i = 0; i < colors.Count; i++)
                {
                    var color = colors[i];
                    var colorCard = new ExtractedColorCard
                    {
                        Color = color,
                        HexColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}",
                        RgbColor = $"RGB({color.R},{color.G},{color.B})",
                        Index = i + 1,
                        ImagePath = null
                    };

                    colorCard.GenerateCommand = new RelayCommand(() => GenerateTheme(colorCard));

                    ExtractedColors.Add(colorCard);
                }

                TM.App.Log($"[ImageColorPicker] 颜色提取成功: {colors.Count} 个颜色");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ImageColorPicker] 颜色提取失败: {ex.Message}");
            }
        }

        private void AnalyzeImage(BitmapImage bitmap)
        {
            try
            {
                var analysis = ImageAnalyzer.Analyze(bitmap);

                var sb = new StringBuilder();
                sb.AppendLine("📊 图片分析结果");
                sb.AppendLine();
                sb.AppendLine($"• 平均亮度：{analysis.AvgBrightness:F1} / 255");
                sb.AppendLine($"• 图片类型：{(analysis.IsDark ? "暗色图片" : "亮色图片")}");
                sb.AppendLine($"• 建议主题：{analysis.ThemeType.ToUpper()} 主题");
                sb.AppendLine($"• 文字颜色：{analysis.TextColor}");
                sb.AppendLine();
                sb.AppendLine("💡 建议：");
                sb.AppendLine(analysis.Notes);

                AnalysisText = sb.ToString();

                TM.App.Log($"[ImageColorPicker] 图片分析完成");
            }
            catch (Exception ex)
            {
                AnalysisText = "图片分析失败";
                TM.App.Log($"[ImageColorPicker] 图片分析失败: {ex.Message}");
            }
        }

        private async void GenerateTheme(ExtractedColorCard colorCard)
        {
            try
            {
            if (CurrentImage == null)
            {
                ToastNotification.ShowWarning("请先上传图片", "");
                return;
            }

                var timestamp = DateTime.Now.ToString("HHmmss");
                var themeName = $"ImageColor_{colorCard.Index}_{timestamp}";

                var themeColors = GenerateThemeColors(colorCard.Color, CurrentImage);

                await SaveThemeAsync(themeName, themeColors);

                _themeManager.ApplyThemeFromFile($"{themeName}Theme.xaml");

                ToastNotification.ShowSuccess("生成成功", $"主题「图片取色 {colorCard.Index} - {timestamp}」已创建并应用");

                TM.App.Log($"[ImageColorPicker] 主题生成成功: {themeName}");
            }
            catch (Exception ex)
            {
                StandardDialog.ShowError(
                    $"生成主题失败：{ex.Message}",
                    "错误",
                    null
                );
                TM.App.Log($"[ImageColorPicker] 主题生成失败: {ex.Message}");
            }
        }

        private ThemeColors GenerateThemeColors(Color primaryColor, BitmapImage bitmap)
        {
            var palette = ColorExtractor.ExtractPalette(bitmap, 5);
            var analysis = ImageAnalyzer.Analyze(bitmap);

            Color secondaryColor = palette.OrderByDescending(c =>
                Math.Sqrt(
                    Math.Pow(c.R - primaryColor.R, 2) +
                    Math.Pow(c.G - primaryColor.G, 2) +
                    Math.Pow(c.B - primaryColor.B, 2)
                )
            ).FirstOrDefault();

            return new ThemeColors
            {
                Primary = primaryColor,
                Secondary = secondaryColor,
                IsDarkTheme = analysis.IsDark,
                TextColor = analysis.IsDark ? Colors.White : Color.FromRgb(33, 37, 41)
            };
        }

        private async System.Threading.Tasks.Task SaveThemeAsync(string themeName, ThemeColors colors)
        {
            var themesPath = StoragePathHelper.GetFrameworkStoragePath("Appearance/ThemeManagement/Themes");
            StoragePathHelper.EnsureDirectoryExists(themesPath);

            var filePath = Path.Combine(themesPath, $"{themeName}Theme.xaml");

            var xamlContent = GenerateThemeXaml(themeName, colors);
            var tmpIcp = filePath + ".tmp";
            await File.WriteAllTextAsync(tmpIcp, xamlContent, Encoding.UTF8);
            File.Move(tmpIcp, filePath, overwrite: true);

            TM.App.Log($"[ImageColorPicker] 主题异步保存成功: {filePath}");
        }

        private string GenerateThemeXaml(string themeName, ThemeColors colors)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"");
            sb.AppendLine("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">");
            sb.AppendLine("    ");
            sb.AppendLine($"    <!-- {themeName} - 从图片生成 -->");
            sb.AppendLine("    ");

            if (colors.IsDarkTheme)
            {
                sb.AppendLine("    <!-- 背景颜色 -->");
                sb.AppendLine("    <SolidColorBrush x:Key=\"UnifiedBackground\" Color=\"#1a1a1a\"/>");
                sb.AppendLine("    <SolidColorBrush x:Key=\"ContentBackground\" Color=\"#2d2d2d\"/>");
                sb.AppendLine("    <SolidColorBrush x:Key=\"Surface\" Color=\"#252525\"/>");
                sb.AppendLine("    <SolidColorBrush x:Key=\"ContentHighlight\" Color=\"#2d2d2d\"/>");
                sb.AppendLine("    ");
                sb.AppendLine("    <!-- 边框颜色 -->");
                sb.AppendLine("    <SolidColorBrush x:Key=\"WindowBorder\" Color=\"#3a3a3a\"/>");
                sb.AppendLine("    <SolidColorBrush x:Key=\"BorderBrush\" Color=\"#3a3a3a\"/>");
                sb.AppendLine("    ");
                sb.AppendLine("    <!-- 文字颜色 -->");
                sb.AppendLine($"    <SolidColorBrush x:Key=\"TextPrimary\" Color=\"{GetColorHex(colors.TextColor)}\"/>");
                sb.AppendLine("    <SolidColorBrush x:Key=\"TextSecondary\" Color=\"#adb5bd\"/>");
                sb.AppendLine("    <SolidColorBrush x:Key=\"TextTertiary\" Color=\"#6c757d\"/>");
                sb.AppendLine("    <SolidColorBrush x:Key=\"TextDisabled\" Color=\"#495057\"/>");
            }
            else
            {
                sb.AppendLine("    <!-- 背景颜色 -->");
                sb.AppendLine("    <SolidColorBrush x:Key=\"UnifiedBackground\" Color=\"#f8f9fa\"/>");
                sb.AppendLine("    <SolidColorBrush x:Key=\"ContentBackground\" Color=\"#ffffff\"/>");
                sb.AppendLine("    <SolidColorBrush x:Key=\"Surface\" Color=\"#f1f3f5\"/>");
                sb.AppendLine("    <SolidColorBrush x:Key=\"ContentHighlight\" Color=\"#e9ecef\"/>");
                sb.AppendLine("    ");
                sb.AppendLine("    <!-- 边框颜色 -->");
                sb.AppendLine("    <SolidColorBrush x:Key=\"WindowBorder\" Color=\"#dee2e6\"/>");
                sb.AppendLine("    <SolidColorBrush x:Key=\"BorderBrush\" Color=\"#ced4da\"/>");
                sb.AppendLine("    ");
                sb.AppendLine("    <!-- 文字颜色 -->");
                sb.AppendLine($"    <SolidColorBrush x:Key=\"TextPrimary\" Color=\"{GetColorHex(colors.TextColor)}\"/>");
                sb.AppendLine("    <SolidColorBrush x:Key=\"TextSecondary\" Color=\"#6c757d\"/>");
                sb.AppendLine("    <SolidColorBrush x:Key=\"TextTertiary\" Color=\"#adb5bd\"/>");
                sb.AppendLine("    <SolidColorBrush x:Key=\"TextDisabled\" Color=\"#dee2e6\"/>");
            }

            sb.AppendLine("    ");
            sb.AppendLine("    <!-- 交互状态颜色 -->");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"HoverBackground\" Color=\"{GetLighterColor(colors.Primary, 0.2)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ActiveBackground\" Color=\"{GetLighterColor(colors.Primary, 0.3)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"SelectedBackground\" Color=\"{GetColorHex(colors.Primary)}\"/>");
            sb.AppendLine("    ");
            sb.AppendLine("    <!-- 主题色 -->");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"PrimaryColor\" Color=\"{GetColorHex(colors.Primary)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"PrimaryHover\" Color=\"{GetLighterColor(colors.Primary, 0.15)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"PrimaryActive\" Color=\"{GetDarkerColor(colors.Primary, 0.15)}\"/>");
            sb.AppendLine("    ");
            sb.AppendLine("    <!-- 功能色 -->");
            sb.AppendLine("    <SolidColorBrush x:Key=\"SuccessColor\" Color=\"#34D399\"/>");
            sb.AppendLine("    <SolidColorBrush x:Key=\"WarningColor\" Color=\"#FBBF24\"/>");
            sb.AppendLine("    <SolidColorBrush x:Key=\"DangerColor\" Color=\"#EF4444\"/>");
            sb.AppendLine("    <SolidColorBrush x:Key=\"DangerHover\" Color=\"#DC2626\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"InfoColor\" Color=\"{GetColorHex(colors.Secondary)}\"/>");
            sb.AppendLine("    ");
            sb.AppendLine("</ResourceDictionary>");

            return sb.ToString();
        }

        private string GetColorHex(Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private string GetLighterColor(Color color, double factor)
        {
            var r = (byte)(color.R + (255 - color.R) * factor);
            var g = (byte)(color.G + (255 - color.G) * factor);
            var b = (byte)(color.B + (255 - color.B) * factor);
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        private string GetDarkerColor(Color color, double factor)
        {
            var r = (byte)(color.R * (1 - factor));
            var g = (byte)(color.G * (1 - factor));
            var b = (byte)(color.B * (1 - factor));
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        private void ClearRecords()
        {
            try
            {
                ExtractedColors.Clear();
                AnalysisText = "上传图片后，这里会显示图片分析信息";
                CurrentImage = null;
                HasImage = false;
                ImageInfo = "未上传图片";
                _generatedTheme = null;
                _generatedThemeName = string.Empty;

                TM.App.Log($"[ImageColorPicker] 已清除所有记录");

                ToastNotification.ShowSuccess("清除成功", "所有记录已移除");
            }
            catch (Exception ex)
            {
                StandardDialog.ShowError(
                    $"清除记录失败：{ex.Message}",
                    "错误",
                    null
                );
                TM.App.Log($"[ImageColorPicker] 清除记录失败: {ex.Message}");
            }
        }

        private void GenerateThemeAuto()
        {
            try
            {
            if (ExtractedColors.Count == 0 || CurrentImage == null)
            {
                ToastNotification.ShowWarning("请先提取颜色", "请先上传图片并提取颜色");
                return;
            }

                var primaryColor = ExtractedColors[0].Color;

                _generatedTheme = GenerateThemeColors(primaryColor, CurrentImage);

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _generatedThemeName = $"AutoTheme_{timestamp}";

                TM.App.Log($"[ImageColorPicker] 主题生成成功: {_generatedThemeName}");

                ToastNotification.ShowSuccess("生成成功", $"主题「{_generatedThemeName}」已生成，可点击加入主题库");
            }
            catch (Exception ex)
            {
                StandardDialog.ShowError(
                    $"生成主题失败：{ex.Message}",
                    "错误",
                    null
                );
                TM.App.Log($"[ImageColorPicker] 生成主题失败: {ex.Message}");
            }
        }

        private async void AddTheme()
        {
            try
            {
            if (_generatedTheme == null || string.IsNullOrEmpty(_generatedThemeName))
            {
                ToastNotification.ShowWarning("请先生成主题", "点击「生成主题」按钮后才能加入主题库");
                return;
            }

                await SaveThemeAsync(_generatedThemeName, _generatedTheme);

                _themeManager.ApplyThemeFromFile($"{_generatedThemeName}Theme.xaml");

                ToastNotification.ShowSuccess("加入成功", $"主题「{_generatedThemeName}」已加入主题库并应用");

                TM.App.Log($"[ImageColorPicker] 主题已加入: {_generatedThemeName}");
            }
            catch (Exception ex)
            {
                StandardDialog.ShowError(
                    $"加入主题失败：{ex.Message}",
                    "错误",
                    null
                );
                TM.App.Log($"[ImageColorPicker] 加入主题失败: {ex.Message}");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ExtractedColorCard
    {
        public Color Color { get; set; }
        public string HexColor { get; set; } = string.Empty;
        public string RgbColor { get; set; } = string.Empty;
        public int Index { get; set; }
        public string? ImagePath { get; set; }
        public RelayCommand? GenerateCommand { get; set; }

        public SolidColorBrush ColorBrush => new SolidColorBrush(Color);
        public SolidColorBrush ButtonBackground => new SolidColorBrush(Color);
        public SolidColorBrush ButtonForeground
        {
            get
            {
                var brightness = (Color.R + Color.G + Color.B) / 3.0;
                return new SolidColorBrush(brightness < 128 ? Colors.White : Colors.Black);
            }
        }
    }

    public class ThemeColors
    {
        public Color Primary { get; set; }
        public Color Secondary { get; set; }
        public bool IsDarkTheme { get; set; }
        public Color TextColor { get; set; }
    }
}
