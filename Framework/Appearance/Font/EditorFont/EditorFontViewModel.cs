using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using TM.Framework.Appearance.Font.Models;
using TM.Framework.Appearance.Font.Services;

namespace TM.Framework.Appearance.Font.EditorFont
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class EditorFontViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<string> _availableFonts = new();
        private FontSettings _currentSettings;
        private string _selectedFontFamily;
        private double _selectedFontSize;
        private string _selectedFontWeight;
        private double _selectedLineHeight;
        private double _selectedLetterSpacing;
        private string _searchText = string.Empty;
        private System.Windows.Threading.DispatcherTimer? _fontSearchTimer;

        private bool _showMonospaceOnly;
        private bool _enableLigatures;
        private ObservableCollection<string> _supportedLigatures = new();
        private string _ligaturePreviewText = string.Empty;

        private List<string> _cachedAllFonts = new();
        private TM.Framework.Common.Controls.TreeNodeItem? _selectedFontNode;

        private readonly MonospaceFontDetector _monoDetector;
        private readonly LigatureDetector _ligatureDetector;

        private readonly CodeSampleProvider _codeSampleProvider;
        private CodeLanguage _selectedLanguage = CodeLanguage.CSharp;
        private string _currentCode = string.Empty;

        private readonly EditorFontPresetService _presetService;
        private EditorFontPreset? _selectedPreset;

        private readonly OpenTypeFeaturesService _openTypeService;
        private ObservableCollection<OpenTypeFeature> _openTypeFeatures = new();

        private readonly CharWidthAnalyzer _widthAnalyzer;
        private CharWidthReport? _widthReport;

        private readonly FontPerformanceAnalyzer _performanceAnalyzer;
        private PerformanceReport? _performanceReport;
        private System.Threading.CancellationTokenSource? _perfTestCts;
        private bool _isRunningWidthAnalysis;

        private readonly ScenePresetService _scenePresetService;
        private ScenePreset? _selectedScene;

        public ObservableCollection<string> AvailableFonts
        {
            get => _availableFonts;
            set
            {
                if (_availableFonts != value)
                {
                    _availableFonts = value;
                    OnPropertyChanged(nameof(AvailableFonts));
                }
            }
        }

        public TM.Framework.Common.ViewModels.RangeObservableCollection<TM.Framework.Common.Controls.TreeNodeItem> FontTree { get; } = new();

        public ObservableCollection<string> FontWeightOptions { get; } = new()
        {
            "Thin", "ExtraLight", "Light", "Normal", "Medium", "SemiBold", "Bold", "ExtraBold", "Black"
        };

        public string SelectedFontFamily
        {
            get => _selectedFontFamily;
            set
            {
                if (_selectedFontFamily != value)
                {
                    _selectedFontFamily = value;
                    _currentSettings.FontFamily = value;
                    OnPropertyChanged(nameof(SelectedFontFamily));

                    var capturedFont = value;
                    _ = System.Threading.Tasks.Task.Run(() =>
                    {
                        try
                        {
                            var ligatures = _ligatureDetector.GetSupportedLigatures(capturedFont);
                            var ligaturePreview = ligatures.Count > 0
                                ? _ligatureDetector.GenerateLigaturePreviewText(ligatures)
                                : "此字体不支持编程连字";
                            var features = _openTypeService.GetSupportedFeatures(capturedFont);
                            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                            {
                                SupportedLigatures = new System.Collections.ObjectModel.ObservableCollection<string>(ligatures);
                                LigaturePreviewText = ligaturePreview;
                                OpenTypeFeatures = new System.Collections.ObjectModel.ObservableCollection<OpenTypeFeature>(features);
                            });
                        }
                        catch (System.Exception ex)
                        {
                            TM.App.Log($"[EditorFont] 后台解析字体特性失败({capturedFont}): {ex.Message}");
                        }
                    });
                }
            }
        }

        public double SelectedFontSize
        {
            get => _selectedFontSize;
            set
            {
                if (_selectedFontSize != value)
                {
                    _selectedFontSize = value;
                    _currentSettings.FontSize = value;
                    OnPropertyChanged(nameof(SelectedFontSize));
                }
            }
        }

        public string SelectedFontWeight
        {
            get => _selectedFontWeight;
            set
            {
                if (_selectedFontWeight != value)
                {
                    _selectedFontWeight = value;
                    _currentSettings.FontWeight = value;
                    OnPropertyChanged(nameof(SelectedFontWeight));
                }
            }
        }

        public double SelectedLineHeight
        {
            get => _selectedLineHeight;
            set
            {
                if (_selectedLineHeight != value)
                {
                    _selectedLineHeight = value;
                    _currentSettings.LineHeight = value;
                    OnPropertyChanged(nameof(SelectedLineHeight));
                }
            }
        }

        public double SelectedLetterSpacing
        {
            get => _selectedLetterSpacing;
            set
            {
                if (_selectedLetterSpacing != value)
                {
                    _selectedLetterSpacing = value;
                    _currentSettings.LetterSpacing = value;
                    OnPropertyChanged(nameof(SelectedLetterSpacing));
                }
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged(nameof(SearchText));
                    ScheduleFontSearch();
                }
            }
        }

        public bool ShowMonospaceOnly
        {
            get => _showMonospaceOnly;
            set
            {
                if (_showMonospaceOnly != value)
                {
                    _showMonospaceOnly = value;
                    OnPropertyChanged(nameof(ShowMonospaceOnly));
                    ScheduleFontSearch();
                }
            }
        }

        public bool EnableLigatures
        {
            get => _enableLigatures;
            set
            {
                if (_enableLigatures != value)
                {
                    _enableLigatures = value;
                    _currentSettings.EnableLigatures = value;
                    OnPropertyChanged(nameof(EnableLigatures));
                }
            }
        }

        public ObservableCollection<string> SupportedLigatures
        {
            get => _supportedLigatures;
            set
            {
                if (_supportedLigatures != value)
                {
                    _supportedLigatures = value;
                    OnPropertyChanged(nameof(SupportedLigatures));
                }
            }
        }

        public string LigaturePreviewText
        {
            get => _ligaturePreviewText;
            set
            {
                if (_ligaturePreviewText != value)
                {
                    _ligaturePreviewText = value;
                    OnPropertyChanged(nameof(LigaturePreviewText));
                }
            }
        }

        public ObservableCollection<CodeLanguage> SupportedLanguages { get; } = new();

        public ObservableCollection<EditorFontPreset> EditorPresets { get; } = new();

        public EditorFontPreset? SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                if (_selectedPreset != value)
                {
                    _selectedPreset = value;
                    OnPropertyChanged(nameof(SelectedPreset));
                }
            }
        }

        public ObservableCollection<OpenTypeFeature> OpenTypeFeatures
        {
            get => _openTypeFeatures;
            set
            {
                if (_openTypeFeatures != value)
                {
                    _openTypeFeatures = value;
                    OnPropertyChanged(nameof(OpenTypeFeatures));
                }
            }
        }

        public CodeLanguage SelectedLanguage
        {
            get => _selectedLanguage;
            set
            {
                if (_selectedLanguage != value)
                {
                    _selectedLanguage = value;
                    OnPropertyChanged(nameof(SelectedLanguage));
                    LoadCodeSample();
                }
            }
        }

        public string CurrentCode
        {
            get => _currentCode;
            set
            {
                if (_currentCode != value)
                {
                    _currentCode = value;
                    OnPropertyChanged(nameof(CurrentCode));
                }
            }
        }

        public FontSettings CurrentSettings => _currentSettings;

        public string CodePreviewText => @"// C# 代码示例
using System;
using System.Collections.Generic;

namespace Example
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine(""Hello, World!"");
            var numbers = new List<int> { 1, 2, 3, 4, 5 };
            foreach (var num in numbers)
            {
                Console.WriteLine($""Number: {num}"");
            }
        }
    }
}";

        public string JsonPreviewText => @"{
  ""name"": ""配置示例"",
  ""version"": ""1.0.0"",
  ""settings"": {
    ""fontFamily"": ""Consolas"",
    ""fontSize"": 13,
    ""lineHeight"": 1.6
  },
  ""features"": [
    ""语法高亮"",
    ""代码折叠"",
    ""智能提示""
  ]
}";

        public CharWidthReport? WidthReport
        {
            get => _widthReport;
            set
            {
                if (_widthReport != value)
                {
                    _widthReport = value;
                    OnPropertyChanged(nameof(WidthReport));
                }
            }
        }

        public PerformanceReport? PerformanceReport
        {
            get => _performanceReport;
            set
            {
                if (_performanceReport != value)
                {
                    _performanceReport = value;
                    OnPropertyChanged(nameof(PerformanceReport));
                }
            }
        }

        public ObservableCollection<ScenePreset> ScenePresets { get; } = new();

        public ScenePreset? SelectedScene
        {
            get => _selectedScene;
            set
            {
                if (_selectedScene != value)
                {
                    _selectedScene = value;
                    OnPropertyChanged(nameof(SelectedScene));
                }
            }
        }

        public ObservableCollection<string> TabSymbolOptions { get; } = new() { "→", "⇥", "⟶", "»" };
        public ObservableCollection<string> SpaceSymbolOptions { get; } = new() { "·", "•", "␣", "◦" };

        public ICommand ApplyCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand ResetCodeSampleCommand { get; }
        public ICommand ApplyPresetCommand { get; }
        public ICommand ToggleFeatureCommand { get; }
        public ICommand RunWidthAnalysisCommand { get; }
        public ICommand RunPerformanceTestCommand { get; }
        public ICommand ApplySceneCommand { get; }
        public ICommand SelectFontCommand { get; }

        public EditorFontViewModel(
            MonospaceFontDetector monoDetector,
            LigatureDetector ligatureDetector,
            CodeSampleProvider codeSampleProvider,
            EditorFontPresetService presetService,
            OpenTypeFeaturesService openTypeService,
            CharWidthAnalyzer widthAnalyzer,
            FontPerformanceAnalyzer performanceAnalyzer,
            ScenePresetService scenePresetService)
        {
            TM.App.Log("[EditorFont] ViewModel初始化");

            _monoDetector = monoDetector;
            _ligatureDetector = ligatureDetector;
            _codeSampleProvider = codeSampleProvider;
            _presetService = presetService;
            _openTypeService = openTypeService;
            _widthAnalyzer = widthAnalyzer;
            _performanceAnalyzer = performanceAnalyzer;
            _scenePresetService = scenePresetService;

            var config = FontManager.LoadConfiguration();
            _currentSettings = config.EditorFont.Clone();

            _selectedFontFamily = _currentSettings.FontFamily;
            _selectedFontSize = _currentSettings.FontSize;
            _selectedFontWeight = _currentSettings.FontWeight;
            _selectedLineHeight = _currentSettings.LineHeight;
            _selectedLetterSpacing = _currentSettings.LetterSpacing;
            _enableLigatures = _currentSettings.EnableLigatures;

            var languages = _codeSampleProvider.GetSupportedLanguages();
            foreach (var lang in languages)
            {
                SupportedLanguages.Add(lang);
            }

            LoadCodeSample();

            var presets = _presetService.GetBuiltInPresets();
            foreach (var preset in presets)
            {
                EditorPresets.Add(preset);
            }

            var scenes = _scenePresetService.GetAllPresets();
            foreach (var scene in scenes)
            {
                ScenePresets.Add(scene);
            }

            _monoDetector.InitializeDpi();
            var initFontFamily = _selectedFontFamily;
            AsyncSettingsLoader.RunOrDefer(() =>
            {
                var fonts = FontManager.GetSystemFonts();
                var ligatures = _ligatureDetector.GetSupportedLigatures(initFontFamily);
                var ligaturePreview = ligatures.Count > 0
                    ? _ligatureDetector.GenerateLigaturePreviewText(ligatures)
                    : "此字体不支持编程连字";
                var features = _openTypeService.GetSupportedFeatures(initFontFamily);
                var treeNodes = BuildFontTreeNodes(fonts.Select(f => (f, _monoDetector.IsMonospace(f))));
                return () =>
                {
                    _cachedAllFonts = fonts;
                    AvailableFonts = new System.Collections.ObjectModel.ObservableCollection<string>(fonts);
                    SupportedLigatures = new System.Collections.ObjectModel.ObservableCollection<string>(ligatures);
                    LigaturePreviewText = ligaturePreview;
                    OpenTypeFeatures = new System.Collections.ObjectModel.ObservableCollection<OpenTypeFeature>(features);
                    FontTree.ReplaceAll(treeNodes);
                };
            }, "EditorFont");

            ApplyCommand = new RelayCommand(ApplySettings);
            SaveCommand = new RelayCommand(SaveSettings);
            ResetCommand = new RelayCommand(ResetSettings);
            ResetCodeSampleCommand = new RelayCommand(ResetCodeSample);
            ApplyPresetCommand = new RelayCommand(ApplyPreset);
            ToggleFeatureCommand = new RelayCommand<OpenTypeFeature>(ToggleFeature);
            RunWidthAnalysisCommand = new RelayCommand(RunWidthAnalysis);
            RunPerformanceTestCommand = new RelayCommand(RunPerformanceTest);
            ApplySceneCommand = new RelayCommand<ScenePreset>(ApplyScene);
            SelectFontCommand = new TM.Framework.Common.Helpers.MVVM.RelayCommand(SelectFontFromTree);
        }

        private void FilterFonts()
        {
            if (_cachedAllFonts.Count == 0) return;
            var allFonts = new List<string>(_cachedAllFonts);

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                allFonts = allFonts.Where(f => f.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            bool monoOnly = ShowMonospaceOnly;
            var snapshot = allFonts
                .Select(f => (f, isMono: _monoDetector.IsMonospace(f)))
                .Where(t => !monoOnly || t.isMono)
                .ToList();
            _ = System.Threading.Tasks.Task.Run(() => BuildFontTreeNodes(snapshot))
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                        {
                            TM.App.Log($"[EditorFont] 字体树重建失败: {t.Exception?.GetBaseException().Message}");
                            return;
                        }
                        FontTree.ReplaceAll(t.Result);
                    }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
            TM.App.Log($"[EditorFont] 过滤字体: 显示 {allFonts.Count} 个 (等宽only={ShowMonospaceOnly})");
        }

        private void ScheduleFontSearch()
        {
            if (_fontSearchTimer == null)
            {
                _fontSearchTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(300)
                };
                _fontSearchTimer.Tick += (_, _) => { _fontSearchTimer.Stop(); FilterFonts(); };
            }
            _fontSearchTimer.Stop();
            _fontSearchTimer.Start();
        }

        private System.Collections.Generic.List<TM.Framework.Common.Controls.TreeNodeItem> BuildFontTreeNodes(
            System.Collections.Generic.IEnumerable<(string font, bool isMono)> fonts)
        {
            var newItems = new System.Collections.Generic.List<TM.Framework.Common.Controls.TreeNodeItem>();
            foreach (var (font, isMono) in fonts)
            {
                var icon = isMono ? IconHelper.TryGet("Icon.Monitor") : IconHelper.TryGet("Icon.Font");
                newItems.Add(new TM.Framework.Common.Controls.TreeNodeItem
                {
                    Name = font,
                    Icon = icon,
                    Tag = font,
                    IsExpanded = false,
                    ShowChildCount = false
                });
            }
            return newItems;
        }

        private void SelectFontFromTree(object? parameter)
        {
            if (parameter is TM.Framework.Common.Controls.TreeNodeItem node && node.Tag is string fontName)
            {
                if (_selectedFontNode != null)
                    _selectedFontNode.IsSelected = false;
                node.IsSelected = true;
                _selectedFontNode = node;
                SelectedFontFamily = fontName;
            }
        }

        private void LoadCodeSample()
        {
            try
            {
                var sample = _codeSampleProvider.GetSample(SelectedLanguage);
                CurrentCode = sample.Code;
                TM.App.Log($"[EditorFont] 已加载 {sample.DisplayName} 代码示例");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EditorFont] 加载代码示例失败: {ex.Message}");
                CurrentCode = "// 加载失败";
            }
        }

        private void ResetCodeSample()
        {
            try
            {
                LoadCodeSample();
                GlobalToast.Info("重置成功", $"已恢复 {SelectedLanguage} 示例代码");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EditorFont] 重置代码示例失败: {ex.Message}");
                StandardDialog.ShowError($"重置失败\n\n错误详情：{ex.Message}", "重置失败");
            }
        }

        private void ApplyPreset()
        {
            try
            {
                if (SelectedPreset == null)
                {
                    GlobalToast.Warning("未选择预设", "请先选择一个预设");
                    return;
                }

                if (!SelectedPreset.IsInstalled)
                {
                    var result = StandardDialog.ShowConfirm(
                        $"字体 {SelectedPreset.Settings.FontFamily} 未安装。\n\n是否继续应用预设？（可能无法显示）",
                        "字体未安装"
                    );

                    if (!result)
                    {
                        return;
                    }
                }

                var preset = SelectedPreset.Settings.Clone();
                SelectedFontFamily = preset.FontFamily;
                SelectedFontSize = preset.FontSize;
                SelectedFontWeight = preset.FontWeight;
                SelectedLineHeight = preset.LineHeight;
                SelectedLetterSpacing = preset.LetterSpacing;
                EnableLigatures = preset.EnableLigatures;

                GlobalToast.Success("应用成功", $"已应用预设：{SelectedPreset.Name}");
                TM.App.Log($"[EditorFont] 应用预设: {SelectedPreset.Name}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EditorFont] 应用预设失败: {ex.Message}");
                StandardDialog.ShowError($"应用预设失败\n\n错误详情：{ex.Message}", "应用失败");
            }
        }

        private void ToggleFeature(OpenTypeFeature? feature)
        {
            try
            {
                if (feature == null) return;

                feature.IsEnabled = !feature.IsEnabled;
                OnPropertyChanged(nameof(OpenTypeFeatures));

                TM.App.Log($"[EditorFont] OpenType特性 {feature.Tag}: {(feature.IsEnabled ? "启用" : "禁用")}");
                GlobalToast.Info(
                    $"特性 {feature.Name}",
                    feature.IsEnabled ? "已启用" : "已禁用"
                );
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EditorFont] 切换特性失败: {ex.Message}");
            }
        }

        private async void RunWidthAnalysis()
        {
            if (_isRunningWidthAnalysis) return;
            try
            {
                _isRunningWidthAnalysis = true;
                if (string.IsNullOrWhiteSpace(SelectedFontFamily))
                {
                    GlobalToast.Warning("未选择字体", "请先选择一个字体");
                    return;
                }

                TM.App.Log($"[EditorFont] 开始分析字符宽度: {SelectedFontFamily}");

                var family = SelectedFontFamily;
                var size = SelectedFontSize;
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(System.Windows.Application.Current.MainWindow).PixelsPerDip;

                var report = await System.Threading.Tasks.Task.Run(() => _widthAnalyzer.AnalyzeFont(family, size, dpi));
                WidthReport = report;

                var resultIcon = report.OverallResult switch
                {
                    WidthCheckResult.Pass => "✓",
                    WidthCheckResult.Warning => "[!]",
                    WidthCheckResult.Fail => "✗",
                    _ => "?"
                };

                GlobalToast.Info(
                    $"{resultIcon} 分析完成",
                    report.Summary
                );

                TM.App.Log($"[EditorFont] 分析完成: {report.Summary}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EditorFont] 分析失败: {ex.Message}");
                StandardDialog.ShowError($"分析失败\n\n错误详情：{ex.Message}", "分析失败");
            }
            finally
            {
                _isRunningWidthAnalysis = false;
            }
        }

        private void ApplySettings()
        {
            try
            {
                FontManager.ApplyEditorFont(_currentSettings);
                TM.App.Log($"[EditorFont] 字体设置已应用");
                GlobalToast.Success("应用成功", "编辑器字体设置已生效");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EditorFont] 应用字体失败: {ex.Message}");
                StandardDialog.ShowError($"应用字体失败\n\n错误详情：{ex.Message}", "应用失败");
            }
        }

        private void SaveSettings()
        {
            try
            {
                var config = FontManager.LoadConfiguration();
                config.EditorFont = _currentSettings.Clone();
                FontManager.SaveConfiguration(config);
                FontManager.ApplyEditorFont(_currentSettings);
                TM.App.Log($"[EditorFont] 字体设置已保存");
                GlobalToast.Success("保存成功", "编辑器字体设置已保存并应用");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EditorFont] 保存字体失败: {ex.Message}");
                StandardDialog.ShowError($"保存字体失败\n\n错误详情：{ex.Message}", "保存失败");
            }
        }

        private void ResetSettings()
        {
            try
            {
                var defaultConfig = FontConfiguration.GetDefault();
                _currentSettings = defaultConfig.EditorFont.Clone();

                SelectedFontFamily = _currentSettings.FontFamily;
                SelectedFontSize = _currentSettings.FontSize;
                SelectedFontWeight = _currentSettings.FontWeight;
                SelectedLineHeight = _currentSettings.LineHeight;
                SelectedLetterSpacing = _currentSettings.LetterSpacing;

                TM.App.Log($"[EditorFont] 字体设置已重置为默认值");
                GlobalToast.Info("重置成功", "编辑器字体设置已恢复默认");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EditorFont] 重置字体失败: {ex.Message}");
                StandardDialog.ShowError($"重置字体失败\n\n错误详情：{ex.Message}", "重置失败");
            }
        }

        private async void RunPerformanceTest()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(SelectedFontFamily))
                {
                    GlobalToast.Warning("未选择字体", "请先选择一个字体");
                    return;
                }

                _perfTestCts?.Cancel();
                _perfTestCts?.Dispose();
                _perfTestCts = new System.Threading.CancellationTokenSource();
                var ct = _perfTestCts.Token;

                TM.App.Log($"[EditorFont] 开始性能测试: {SelectedFontFamily}");

                var family = SelectedFontFamily;
                var size = SelectedFontSize;
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(System.Windows.Application.Current.MainWindow).PixelsPerDip;

                var report = await System.Threading.Tasks.Task.Run(() => _performanceAnalyzer.AnalyzePerformance(family, size, pixelsPerDip: dpi, ct: ct), ct);
                ct.ThrowIfCancellationRequested();
                PerformanceReport = report;

                var ratingIcon = report.Rating switch
                {
                    PerformanceRating.Excellent => "SSD",
                    PerformanceRating.Good => "✓",
                    PerformanceRating.Fair => "[!]",
                    PerformanceRating.Poor => "✗",
                    _ => "?"
                };

                GlobalToast.Info(
                    $"{ratingIcon} 测试完成",
                    report.Summary
                );

                TM.App.Log($"[EditorFont] 性能测试完成: {report.Summary}");
            }
            catch (OperationCanceledException)
            {
                TM.App.Log("[EditorFont] 性能测试已取消");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EditorFont] 性能测试失败: {ex.Message}");
                StandardDialog.ShowError($"性能测试失败\n\n错误详情：{ex.Message}", "测试失败");
            }
        }

        private void ApplyScene(ScenePreset? scene)
        {
            try
            {
                if (scene == null)
                {
                    GlobalToast.Warning("未选择场景", "请先选择一个使用场景");
                    return;
                }

                foreach (var preset in ScenePresets)
                {
                    preset.IsSelected = (preset == scene);
                }

                SelectedScene = scene;

                var settings = scene.Settings;

                SelectedFontFamily = settings.FontFamily;
                SelectedFontSize = settings.FontSize;
                SelectedFontWeight = settings.FontWeight;
                SelectedLineHeight = settings.LineHeight;
                SelectedLetterSpacing = settings.LetterSpacing;
                EnableLigatures = settings.EnableLigatures;
                CurrentSettings.VisualizeWhitespace = settings.VisualizeWhitespace;
                CurrentSettings.ShowZeroWidthChars = settings.ShowZeroWidthChars;
                CurrentSettings.TabSymbol = settings.TabSymbol;
                CurrentSettings.SpaceSymbol = settings.SpaceSymbol;

                _currentSettings.FontFamily = settings.FontFamily;
                _currentSettings.FontSize = settings.FontSize;
                _currentSettings.FontWeight = settings.FontWeight;
                _currentSettings.LineHeight = settings.LineHeight;
                _currentSettings.LetterSpacing = settings.LetterSpacing;
                _currentSettings.EnableLigatures = settings.EnableLigatures;
                _currentSettings.VisualizeWhitespace = settings.VisualizeWhitespace;
                _currentSettings.ShowZeroWidthChars = settings.ShowZeroWidthChars;
                _currentSettings.TabSymbol = settings.TabSymbol;
                _currentSettings.SpaceSymbol = settings.SpaceSymbol;

                TM.App.Log($"[EditorFont] 应用场景预设: {scene.Name}");
                GlobalToast.Success(
                    $"场景已应用: {scene.Icon} {scene.Name}",
                    scene.Description
                );
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EditorFont] 应用场景失败: {ex.Message}");
                StandardDialog.ShowError($"应用场景失败\n\n错误详情：{ex.Message}", "应用失败");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
