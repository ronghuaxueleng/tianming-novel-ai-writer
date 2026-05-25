using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Media;
using TM.Framework.Appearance.IntelligentGeneration.GenerationHistory;
using TM.Framework.Appearance.ThemeManagement;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.Storage;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.Common.ViewModels;
using TM.Services.Framework.AI.Core;

namespace TM.Framework.Appearance.IntelligentGeneration.AIColorScheme
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class AIColorSchemeViewModel : INotifyPropertyChanged, IAIGeneratingState
    {
        private readonly AIService _aiService;
        private readonly ThemeManager _themeManager;
        private readonly GenerationHistorySettings _historySettings;
        private readonly AIColorSchemeSettings _settings;

        private string _keywords = string.Empty;
        private string _selectedColorHarmony = "互补色";
        private string _selectedThemeType = "浅色主题";
        private string _selectedEmotion = "无";
        private string _selectedScene = "通用";
        private ObservableCollection<ColorSchemeCard> _generatedSchemes = new();
        private bool _isGenerating;
        private System.Threading.CancellationTokenSource? _generationCts;
        private readonly RelayCommand _cancelGenerationCommand;

        public List<string> ColorHarmonies { get; } = new() { "互补色", "类似色", "三角色", "分裂互补色", "四色", "单色" };
        public List<string> ThemeTypes { get; } = new() { "浅色主题", "暗色主题" };
        public List<string> Emotions { get; } = new() { "无", "平静", "活力", "温暖", "清新", "神秘", "专业", "可爱" };
        public List<string> Scenes { get; } = new() { "通用", "写作创作", "数据分析", "商务办公", "休闲娱乐", "科技感" };

        public string Keywords
        {
            get => _keywords;
            set { if (_keywords == value) return; _keywords = value; OnPropertyChanged(nameof(Keywords)); }
        }
        public string SelectedColorHarmony
        {
            get => _selectedColorHarmony;
            set { if (_selectedColorHarmony == value) return; _selectedColorHarmony = value; OnPropertyChanged(nameof(SelectedColorHarmony)); }
        }
        public string SelectedThemeType
        {
            get => _selectedThemeType;
            set { if (_selectedThemeType == value) return; _selectedThemeType = value; OnPropertyChanged(nameof(SelectedThemeType)); }
        }
        public string SelectedEmotion
        {
            get => _selectedEmotion;
            set { if (_selectedEmotion == value) return; _selectedEmotion = value; OnPropertyChanged(nameof(SelectedEmotion)); }
        }
        public string SelectedScene
        {
            get => _selectedScene;
            set { if (_selectedScene == value) return; _selectedScene = value; OnPropertyChanged(nameof(SelectedScene)); }
        }
        public ObservableCollection<ColorSchemeCard> GeneratedSchemes
        {
            get => _generatedSchemes;
            set { if (_generatedSchemes == value) return; _generatedSchemes = value; OnPropertyChanged(nameof(GeneratedSchemes)); }
        }
        public bool IsGenerating
        {
            get => _isGenerating;
            set
            {
                if (_isGenerating == value) return;
                _isGenerating = value;
                OnPropertyChanged(nameof(IsGenerating));
                OnPropertyChanged(nameof(IsAIGenerating));
                _cancelGenerationCommand.RaiseCanExecuteChanged();
            }
        }

        public bool IsAIGenerating => IsGenerating;

        public bool IsBatchGenerating => false;

        public string BatchProgressText => IsGenerating ? "正在生成..." : string.Empty;

        public ICommand CancelBatchGenerationCommand => _cancelGenerationCommand;

        public ICommand GenerateSchemesCommand { get; }
        public ICommand ClearSchemesCommand { get; }

        public AIColorSchemeViewModel(
            AIService aiService,
            ThemeManager themeManager,
            GenerationHistorySettings historySettings,
            AIColorSchemeSettings settings)
        {
            _aiService = aiService;
            _themeManager = themeManager;
            _historySettings = historySettings;
            _settings = settings;

            try
            {
                var data = _settings.LoadData();
                var cfg = data?.UserConfig;
                if (cfg != null)
                {
                    if (!string.IsNullOrWhiteSpace(cfg.LastKeywords)) Keywords = cfg.LastKeywords;
                    if (!string.IsNullOrWhiteSpace(cfg.LastColorHarmony) && ColorHarmonies.Contains(cfg.LastColorHarmony))
                        SelectedColorHarmony = cfg.LastColorHarmony;
                    if (!string.IsNullOrWhiteSpace(cfg.LastThemeType) && ThemeTypes.Contains(cfg.LastThemeType))
                        SelectedThemeType = cfg.LastThemeType;
                    if (!string.IsNullOrWhiteSpace(cfg.LastEmotion) && Emotions.Contains(cfg.LastEmotion))
                        SelectedEmotion = cfg.LastEmotion;
                    if (!string.IsNullOrWhiteSpace(cfg.LastScene) && Scenes.Contains(cfg.LastScene))
                        SelectedScene = cfg.LastScene;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AIColorScheme] 加载用户配置失败: {ex.Message}");
            }

            _cancelGenerationCommand = new RelayCommand(CancelGeneration, () => IsGenerating);
            GenerateSchemesCommand = new AsyncRelayCommand(GenerateSchemesAsync);
            ClearSchemesCommand = new RelayCommand(ClearSchemes);
        }

        private async System.Threading.Tasks.Task GenerateSchemesAsync()
        {
            if (IsGenerating) return;
            IsGenerating = true;

            _generationCts?.Cancel();
            _generationCts?.Dispose();
            _generationCts = new System.Threading.CancellationTokenSource();
            var ct = _generationCts.Token;

            try
            {
                try
                {
                    _settings.UpdateUserConfig(new AIColorSchemeUserConfig
                    {
                        LastColorHarmony = SelectedColorHarmony,
                        LastThemeType = SelectedThemeType,
                        LastEmotion = SelectedEmotion,
                        LastScene = SelectedScene,
                        LastKeywords = Keywords ?? string.Empty
                    });
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[AIColorScheme] 保存用户配置失败: {ex.Message}");
                }

                var prompt = BuildPrompt();
                var result = await _aiService.GenerateAsync(prompt, ct);
                if (!result.Success)
                {
                    GlobalToast.Error("生成失败", result.ErrorMessage);
                    TM.App.Log($"[AIColorScheme] 生成失败: {result.ErrorMessage}");
                    return;
                }
                var cards = ParseResponse(result.Content);
                if (cards == null || cards.Count == 0)
                {
                    GlobalToast.Warning("解析失败", "AI返回内容无法解析为配色方案，请重试");
                    TM.App.Log($"[AIColorScheme] 解析失败，AI响应: {result.Content}");
                    return;
                }
                foreach (var card in cards)
                {
                    var c = card;
                    c.ApplyCommand = new AsyncRelayCommand(() => ApplySchemeAsync(c));
                    c.SaveCommand = new AsyncRelayCommand(() => SaveSchemeAsync(c));
                    GeneratedSchemes.Add(c);

                    try
                    {
                        _historySettings.AddRecord(new HistoryRecordData
                        {
                            Type = "AI配色",
                            Name = c.SchemeName,
                            Timestamp = DateTime.Now,
                            PrimaryColor = c.PrimaryHex,
                            SecondaryColor = c.SecondaryHex,
                            AccentColor = c.AccentHex,
                            BackgroundColor = c.BackgroundHex,
                            TextColor = c.TextHex,
                            Harmony = SelectedColorHarmony,
                            Keywords = Keywords ?? string.Empty,
                            IsFavorite = false
                        });
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[AIColorScheme] 写入生成历史失败: {ex.Message}");
                    }
                }
                GlobalToast.Success("生成完成", $"已生成 {cards.Count} 套配色方案");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("生成异常", ex.Message);
                TM.App.Log($"[AIColorScheme] 生成异常: {ex.Message}");
            }
            finally
            {
                _generationCts?.Dispose();
                _generationCts = null;
                IsGenerating = false;
            }
        }

        private void CancelGeneration()
        {
            try
            {
                _generationCts?.Cancel();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AIColorScheme] 取消生成失败: {ex.Message}");
            }
        }

        private void ClearSchemes()
        {
            GeneratedSchemes.Clear();
            GlobalToast.Info("已清除", "所有配色方案已清除");
        }

        private string BuildPrompt()
        {
            var isDark = SelectedThemeType == "暗色主题";
            var bgConstraint = isDark
                ? "BackgroundColor必须为深色（如#1A1A2E、#0D1117等，亮度<30%），TextColor必须为浅色（如#E8E8E8、#F0F0F0等，亮度>70%）"
                : "BackgroundColor必须为浅色（如#F8F9FA、#FFFFFF等，亮度>85%），TextColor必须为深色（如#1F2937、#212529等，亮度<30%）";
            var keywordsText = string.IsNullOrWhiteSpace(Keywords) ? "随机（富有美感）" : Keywords.Trim();
            var emotionText = SelectedEmotion == "无" ? "不限" : SelectedEmotion;

            var sb = new StringBuilder();
            sb.AppendLine("<role>你是专业UI配色设计师。根据以下参数生成3套完整的应用程序主题配色方案。</role>");
            sb.AppendLine();
            sb.AppendLine("<params>");
            sb.AppendLine($"- 关键词/描述：{keywordsText}");
            sb.AppendLine($"- 色彩和谐规则：{SelectedColorHarmony}");
            sb.AppendLine($"- 主题类型：{SelectedThemeType}");
            sb.AppendLine($"- 情感色彩：{emotionText}");
            sb.AppendLine($"- 使用场景：{SelectedScene}");
            sb.AppendLine("</params>");
            sb.AppendLine();
            sb.AppendLine("<output_format note=\"只输出JSON数组，不要任何额外文字、代码块标记或Markdown\">");
            sb.AppendLine("[");
            sb.AppendLine("  {");
            sb.AppendLine("    \"SchemeName\": \"方案名称（简洁中文，不超过8字）\",");
            sb.AppendLine("    \"PrimaryColor\": \"#RRGGBB\",");
            sb.AppendLine("    \"SecondaryColor\": \"#RRGGBB\",");
            sb.AppendLine("    \"AccentColor\": \"#RRGGBB\",");
            sb.AppendLine("    \"BackgroundColor\": \"#RRGGBB\",");
            sb.AppendLine("    \"TextColor\": \"#RRGGBB\"");
            sb.AppendLine("  }");
            sb.AppendLine("]");
            sb.AppendLine("</output_format>");
            sb.AppendLine();
            sb.AppendLine("<hard_constraints>");
            sb.AppendLine("1. 所有色值必须是#RRGGBB格式（6位十六进制）");
            sb.AppendLine($"2. {bgConstraint}");
            sb.AppendLine($"3. 严格遵循「{SelectedColorHarmony}」色彩和谐规则，主色/辅色/强调色之间关系符合该规则");
            sb.AppendLine("4. PrimaryColor与BackgroundColor文字对比度必须≥4.5:1");
            sb.AppendLine("5. 3套方案之间颜色要有明显区分度，不能雷同");
            sb.AppendLine("6. 数组长度必须严格等于3");
            sb.AppendLine("</hard_constraints>");
            return sb.ToString();
        }

        private List<ColorSchemeCard>? ParseResponse(string content)
        {
            try
            {
                var json = ExtractJsonArray(content);
                if (string.IsNullOrWhiteSpace(json)) return null;

                var items = JsonSerializer.Deserialize<List<JsonElement>>(json);
                if (items == null) return null;

                var result = new List<ColorSchemeCard>();
                foreach (var item in items)
                {
                    var primary = ParseColor(GetString(item, "PrimaryColor"));
                    var background = ParseColor(GetString(item, "BackgroundColor"));
                    var text = ParseColor(GetString(item, "TextColor"));
                    var card = new ColorSchemeCard
                    {
                        SchemeName = GetString(item, "SchemeName"),
                        PrimaryColor = primary,
                        SecondaryColor = ParseColor(GetString(item, "SecondaryColor")),
                        AccentColor = ParseColor(GetString(item, "AccentColor")),
                        BackgroundColor = background,
                        TextColor = text,
                        Harmony = SelectedColorHarmony,
                        ThemeType = SelectedThemeType,
                        Emotion = SelectedEmotion,
                        Scene = SelectedScene,
                        Score = ComputeScore(primary, background, text)
                    };
                    if (string.IsNullOrWhiteSpace(card.SchemeName))
                        card.SchemeName = $"配色方案{result.Count + 1}";
                    result.Add(card);
                }
                return result;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AIColorScheme] JSON解析异常: {ex.Message}");
                return null;
            }
        }

        private static string ExtractJsonArray(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return string.Empty;
            var start = content.IndexOf('[');
            var end = content.LastIndexOf(']');
            if (start < 0 || end <= start) return string.Empty;
            return content.Substring(start, end - start + 1);
        }

        private static string GetString(JsonElement el, string key)
        {
            if (el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? string.Empty;
            return string.Empty;
        }

        private static Color ParseColor(string hex)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) return Colors.Gray;
                hex = hex.Trim();
                if (!hex.StartsWith("#")) hex = "#" + hex;
                if (hex.Length == 7)
                    return Color.FromRgb(
                        Convert.ToByte(hex.Substring(1, 2), 16),
                        Convert.ToByte(hex.Substring(3, 2), 16),
                        Convert.ToByte(hex.Substring(5, 2), 16));
            }
            catch { }
            return Colors.Gray;
        }

        private static int ComputeScore(Color primary, Color background, Color text)
        {
            var contrast = GetContrastRatio(text, background);
            var score = 0;
            if (contrast >= 7.0) score += 50;
            else if (contrast >= 4.5) score += 35;
            else if (contrast >= 3.0) score += 20;
            var primaryContrast = GetContrastRatio(primary, background);
            if (primaryContrast >= 4.5) score += 30;
            else if (primaryContrast >= 3.0) score += 15;
            var saturation = GetSaturation(primary);
            if (saturation > 0.3 && saturation < 0.9) score += 20;
            else if (saturation >= 0.1) score += 10;
            return Math.Min(score, 100);
        }

        private static double GetContrastRatio(Color c1, Color c2)
        {
            var l1 = GetRelativeLuminance(c1);
            var l2 = GetRelativeLuminance(c2);
            var lighter = Math.Max(l1, l2);
            var darker = Math.Min(l1, l2);
            return (lighter + 0.05) / (darker + 0.05);
        }

        private static double GetRelativeLuminance(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            r = r <= 0.03928 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
            g = g <= 0.03928 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
            b = b <= 0.03928 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);
            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

        private static double GetSaturation(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            if (max == 0) return 0;
            return (max - min) / max;
        }

        private async System.Threading.Tasks.Task ApplySchemeAsync(ColorSchemeCard card)
        {
            try
            {
                var themeName = $"AI_{SanitizeName(card.SchemeName)}_{DateTime.Now:HHmmss}";
                var xaml = BuildThemeXaml(themeName, card);
                await WriteThemeFileAsync(themeName, xaml);
                _themeManager.ApplyThemeFromFile($"{themeName}Theme.xaml");
                GlobalToast.Success("已应用", $"配色方案「{card.SchemeName}」已应用为当前主题");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("应用失败", ex.Message);
                TM.App.Log($"[AIColorScheme] 应用主题失败: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task SaveSchemeAsync(ColorSchemeCard card)
        {
            try
            {
                var themeName = $"AI_{SanitizeName(card.SchemeName)}_{DateTime.Now:HHmmss}";
                var xaml = BuildThemeXaml(themeName, card);
                await WriteThemeFileAsync(themeName, xaml);
                GlobalToast.Success("已保存", $"配色方案「{card.SchemeName}」已加入主题库，可在主题切换中选用");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("保存失败", ex.Message);
                TM.App.Log($"[AIColorScheme] 保存主题失败: {ex.Message}");
            }
        }

        private static string SanitizeName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (var ch in name)
                sb.Append(Array.IndexOf(invalid, ch) < 0 ? ch : '_');
            return sb.ToString();
        }

        private static async System.Threading.Tasks.Task WriteThemeFileAsync(string themeName, string xaml)
        {
            var themesPath = StoragePathHelper.GetFrameworkStoragePath("Appearance/ThemeManagement/Themes");
            StoragePathHelper.EnsureDirectoryExists(themesPath);
            var filePath = Path.Combine(themesPath, $"{themeName}Theme.xaml");
            var tmp = filePath + ".tmp";
            await File.WriteAllTextAsync(tmp, xaml, System.Text.Encoding.UTF8);
            File.Move(tmp, filePath, overwrite: true);
        }

        private static string BuildThemeXaml(string themeName, ColorSchemeCard card)
        {
            var isDark = card.ThemeType == "暗色主题";
            var p = card.PrimaryColor;
            var s = card.SecondaryColor;
            var bg = card.BackgroundColor;
            var txt = card.TextColor;

            string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            string Lighter(Color c, double f) => $"#{(byte)(c.R + (255 - c.R) * f):X2}{(byte)(c.G + (255 - c.G) * f):X2}{(byte)(c.B + (255 - c.B) * f):X2}";
            string Darker(Color c, double f) => $"#{(byte)(c.R * (1 - f)):X2}{(byte)(c.G * (1 - f)):X2}{(byte)(c.B * (1 - f)):X2}";

            var sb = new StringBuilder();
            sb.AppendLine("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"");
            sb.AppendLine("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">");
            sb.AppendLine($"    <!-- {themeName} - AI配色生成 -->");
            if (isDark)
            {
                sb.AppendLine($"    <SolidColorBrush x:Key=\"UnifiedBackground\" Color=\"{Hex(bg)}\"/>");
                sb.AppendLine($"    <SolidColorBrush x:Key=\"ContentBackground\" Color=\"{Lighter(bg, 0.08)}\"/>");
                sb.AppendLine($"    <SolidColorBrush x:Key=\"Surface\" Color=\"{Lighter(bg, 0.04)}\"/>");
                sb.AppendLine($"    <SolidColorBrush x:Key=\"ContentHighlight\" Color=\"{Lighter(bg, 0.12)}\"/>");
                sb.AppendLine($"    <SolidColorBrush x:Key=\"WindowBorder\" Color=\"{Lighter(bg, 0.2)}\"/>");
                sb.AppendLine($"    <SolidColorBrush x:Key=\"BorderBrush\" Color=\"{Lighter(bg, 0.25)}\"/>");
                sb.AppendLine($"    <SolidColorBrush x:Key=\"TextPrimary\" Color=\"{Hex(txt)}\"/>");
                sb.AppendLine("    <SolidColorBrush x:Key=\"TextSecondary\" Color=\"#adb5bd\"/>");
                sb.AppendLine("    <SolidColorBrush x:Key=\"TextTertiary\" Color=\"#6c757d\"/>");
                sb.AppendLine("    <SolidColorBrush x:Key=\"TextDisabled\" Color=\"#495057\"/>");
            }
            else
            {
                sb.AppendLine($"    <SolidColorBrush x:Key=\"UnifiedBackground\" Color=\"{Hex(bg)}\"/>");
                sb.AppendLine($"    <SolidColorBrush x:Key=\"ContentBackground\" Color=\"#ffffff\"/>");
                sb.AppendLine($"    <SolidColorBrush x:Key=\"Surface\" Color=\"{Darker(bg, 0.03)}\"/>");
                sb.AppendLine($"    <SolidColorBrush x:Key=\"ContentHighlight\" Color=\"{Darker(bg, 0.06)}\"/>");
                sb.AppendLine($"    <SolidColorBrush x:Key=\"WindowBorder\" Color=\"{Darker(bg, 0.12)}\"/>");
                sb.AppendLine($"    <SolidColorBrush x:Key=\"BorderBrush\" Color=\"{Darker(bg, 0.16)}\"/>");
                sb.AppendLine($"    <SolidColorBrush x:Key=\"TextPrimary\" Color=\"{Hex(txt)}\"/>");
                sb.AppendLine("    <SolidColorBrush x:Key=\"TextSecondary\" Color=\"#6c757d\"/>");
                sb.AppendLine("    <SolidColorBrush x:Key=\"TextTertiary\" Color=\"#adb5bd\"/>");
                sb.AppendLine("    <SolidColorBrush x:Key=\"TextDisabled\" Color=\"#dee2e6\"/>");
            }
            sb.AppendLine($"    <SolidColorBrush x:Key=\"HoverBackground\" Color=\"{Lighter(p, 0.2)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"ActiveBackground\" Color=\"{Lighter(p, 0.3)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"SelectedBackground\" Color=\"{Hex(p)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"PrimaryColor\" Color=\"{Hex(p)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"PrimaryHover\" Color=\"{Lighter(p, 0.15)}\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"PrimaryActive\" Color=\"{Darker(p, 0.15)}\"/>");
            sb.AppendLine("    <SolidColorBrush x:Key=\"SuccessColor\" Color=\"#34D399\"/>");
            sb.AppendLine("    <SolidColorBrush x:Key=\"WarningColor\" Color=\"#FBBF24\"/>");
            sb.AppendLine("    <SolidColorBrush x:Key=\"DangerColor\" Color=\"#EF4444\"/>");
            sb.AppendLine("    <SolidColorBrush x:Key=\"DangerHover\" Color=\"#DC2626\"/>");
            sb.AppendLine($"    <SolidColorBrush x:Key=\"InfoColor\" Color=\"{Hex(s)}\"/>");
            sb.AppendLine("</ResourceDictionary>");
            return sb.ToString();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class ColorSchemeCard
    {
        public string SchemeName { get; set; } = string.Empty;
        public Color PrimaryColor { get; set; }
        public Color SecondaryColor { get; set; }
        public Color AccentColor { get; set; }
        public Color BackgroundColor { get; set; }
        public Color TextColor { get; set; }
        public string Harmony { get; set; } = string.Empty;
        public string ThemeType { get; set; } = string.Empty;
        public string Emotion { get; set; } = "无";
        public string Scene { get; set; } = "通用";
        public int Score { get; set; } = 0;

        public SolidColorBrush PrimaryBrush => new(PrimaryColor);
        public SolidColorBrush SecondaryBrush => new(SecondaryColor);
        public SolidColorBrush AccentBrush => new(AccentColor);
        public SolidColorBrush BackgroundBrush => new(BackgroundColor);
        public SolidColorBrush TextBrush => new(TextColor);

        public string PrimaryHex => $"#{PrimaryColor.R:X2}{PrimaryColor.G:X2}{PrimaryColor.B:X2}";
        public string SecondaryHex => $"#{SecondaryColor.R:X2}{SecondaryColor.G:X2}{SecondaryColor.B:X2}";
        public string AccentHex => $"#{AccentColor.R:X2}{AccentColor.G:X2}{AccentColor.B:X2}";
        public string BackgroundHex => $"#{BackgroundColor.R:X2}{BackgroundColor.G:X2}{BackgroundColor.B:X2}";
        public string TextHex => $"#{TextColor.R:X2}{TextColor.G:X2}{TextColor.B:X2}";

        public string ScoreText => $"{Score}分";
        public string ScoreRating => Score >= 80 ? "优秀" : Score >= 60 ? "良好" : "一般";
        public string ScoreIcon => Score >= 80 ? "⭐" : Score >= 60 ? "✨" : "💡";

        public ICommand? ApplyCommand { get; set; }
        public ICommand? SaveCommand { get; set; }
    }
}
