using System;
using System.Reflection;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TM.Framework.Common.Constants;
using TM.Framework.Common.Services;
using TM.Framework.UI.Workspace.Services;

namespace TM.Framework.UI.Workspace.CenterPanel.ChapterEditor
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class HomepageView : UserControl
    {
        public event Action<string>? ModuleSelected;

        private readonly string _clickCountFile;
        private readonly Dictionary<string, int> _clickCounts;
        private readonly PanelCommunicationService _comm;

        public HomepageView()
        {
            InitializeComponent();

            _comm = ServiceLocator.Get<PanelCommunicationService>();

            LoadAppIcon();

            _clickCountFile = StoragePathHelper.GetFilePath(
                "Framework",
                "UI/Workspace/CenterPanel/ChapterEditor",
                "homepage_click_counts.json");

            _clickCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            ApplyClickRanking();

            var filePath = _clickCountFile;
            _ = System.Threading.Tasks.Task.Run(() => LoadClickCounts(filePath))
                .ContinueWith(t =>
                {
                    if (!t.IsCompletedSuccessfully) return;
                    foreach (var kv in t.Result)
                        _clickCounts[kv.Key] = kv.Value;
                    ApplyClickRanking();
                }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
        }

        private static Dictionary<string, int> LoadClickCounts(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                }

                var json = File.ReadAllText(filePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
                return data != null
                    ? new Dictionary<string, int>(data, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[HomepageView] 加载点击次数失败: {ex.Message}");
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private async System.Threading.Tasks.Task SaveClickCountsAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_clickCounts);
                await File.WriteAllTextAsync(_clickCountFile, json);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[HomepageView] 异步保存点击次数失败: {ex.Message}");
            }
        }

        private void RecordClick(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            _clickCounts[key] = _clickCounts.TryGetValue(key, out var count) ? count + 1 : 1;

            _ = SaveClickCountsAsync();
        }

        private void ApplyClickRanking()
        {
            BuildQuickButtons("Design", DesignQuickGrid);
            BuildQuickButtons("Generate", GenerateQuickGrid);
            BuildQuickButtons("Validate", ValidateQuickGrid);
            BuildQuickButtons("SmartAssistant", SmartAssistantQuickGrid);
        }

        private void BuildQuickButtons(string moduleName, Panel? panel)
        {
            try
            {
                if (panel == null)
                {
                    return;
                }

                var candidates = GetQuickCandidates(moduleName);
                if (candidates.Count == 0)
                {
                    return;
                }

                var ranked = candidates
                    .Select(c => new
                    {
                        c.Tag,
                        c.Title,
                        Count = _clickCounts.TryGetValue(c.Tag, out var cnt) ? cnt : 0
                    })
                    .OrderByDescending(x => x.Count)
                    .ThenBy(x => x.Title)
                    .ToList();

                var selected = ranked
                    .Where(x => x.Count > 0)
                    .Select(x => x.Tag)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(4)
                    .ToList();

                if (selected.Count < 4)
                {
                    foreach (var fallback in candidates.Select(c => c.Tag))
                    {
                        if (selected.Count >= 4)
                        {
                            break;
                        }

                        if (!selected.Contains(fallback, StringComparer.OrdinalIgnoreCase))
                        {
                            selected.Add(fallback);
                        }
                    }
                }

                panel.Children.Clear();
                foreach (var tag in selected)
                {
                    var title = candidates.First(c => c.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)).Title;
                    panel.Children.Add(CreateQuickButton(title, tag));
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[HomepageView] 应用点击排行失败: {ex.Message}");
            }
        }

        private sealed record QuickCandidate(string Tag, string Title);

        private List<QuickCandidate> GetQuickCandidates(string moduleName)
        {
            var module = NavigationDefinitions.GetModuleByName(moduleName);
            if (module == null)
            {
                return new List<QuickCandidate>();
            }

            var primary = module.SubModules
                .Where(sm => sm.Functions.Length > 0)
                .Select(sm => new QuickCandidate(
                    $"{moduleName}/{sm.Name}/{sm.Functions[0].Name}",
                    sm.Functions[0].Name))
                .ToList();

            var rest = module.SubModules
                .SelectMany(sm => sm.Functions.Skip(1).Select(f => new QuickCandidate(
                    $"{moduleName}/{sm.Name}/{f.Name}",
                    f.Name)))
                .ToList();

            return primary
                .Concat(rest)
                .DistinctBy(c => c.Tag, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private System.Windows.Controls.Button CreateQuickButton(string title, string tag)
        {
            var button = new System.Windows.Controls.Button
            {
                Content = title,
                Tag = tag,
                Margin = new Thickness(0, 0, 2, 2),
                Padding = new Thickness(12, 6, 12, 6),
                FontSize = 12,
                Width = 96,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };

            button.Click += SubModule_Click;

            return button;
        }

        private void LoadAppIcon()
        {
            try
            {
                var iconPath = StoragePathHelper.GetFrameworkPath("UI/Icons/app.ico");
                if (!File.Exists(iconPath))
                {
                    AppIconBorder.Background = null;
                    AppIconBorder.Visibility = Visibility.Collapsed;
                    FallbackIconTextBlock.Visibility = Visibility.Visible;
                    return;
                }

                var decoder = new IconBitmapDecoder(new Uri(iconPath, UriKind.Absolute),
                    BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

                var target = 64;
                var best = decoder.Frames
                    .OrderBy(f => Math.Abs(f.PixelWidth - target))
                    .FirstOrDefault();

                var source = best ?? decoder.Frames.FirstOrDefault();
                if (source == null)
                {
                    AppIconBorder.Background = null;
                    AppIconBorder.Visibility = Visibility.Collapsed;
                    FallbackIconTextBlock.Visibility = Visibility.Visible;
                    return;
                }

                var brush = new ImageBrush(source) { Stretch = Stretch.UniformToFill };
                if (brush.CanFreeze) brush.Freeze();

                AppIconBorder.Background = brush;
                AppIconBorder.Visibility = Visibility.Visible;
                FallbackIconTextBlock.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[HomepageView] 加载应用图标失败: {ex.Message}");
                AppIconBorder.Background = null;
                AppIconBorder.Visibility = Visibility.Collapsed;
                FallbackIconTextBlock.Visibility = Visibility.Visible;
            }
        }

        private void ModuleCard_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.BorderBrush = (Brush)FindResource("PrimaryColor");
                border.BorderThickness = new Thickness(2);

                if (border.Effect is DropShadowEffect effect)
                {
                    effect.BlurRadius = 16;
                    effect.Opacity = 0.2;
                }
            }
        }

        private void ModuleCard_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.BorderBrush = (Brush)FindResource("BorderBrush");
                border.BorderThickness = new Thickness(1);

                if (border.Effect is DropShadowEffect effect)
                {
                    effect.BlurRadius = 8;
                    effect.Opacity = 0.1;
                }
            }
        }

        private void ModuleCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is string moduleName)
            {
                ModuleSelected?.Invoke(moduleName);
                TM.App.Log($"[HomepageView] 用户点击模块: {moduleName}");
            }
        }

        private void SubModule_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tag)
            {
                RecordClick(tag);

                Dispatcher.BeginInvoke(new Action(ApplyClickRanking), DispatcherPriority.Background);

                var parts = tag.Split('/');
                if (parts.Length == 2)
                {
                    var moduleName = parts[0];
                    var subModuleName = parts[1];

                    var viewType = NavigationDefinitions.GetFunctionViewType(moduleName, subModuleName);

                    if (viewType != null)
                    {
                        _comm.PublishFunctionNavigationRequested(moduleName, subModuleName, viewType);
                        TM.App.Log($"[HomepageView] 导航到功能: {moduleName}/{subModuleName} -> {viewType.FullName}");
                    }
                    else
                    {
                        ModuleSelected?.Invoke(moduleName);
                        TM.App.Log($"[HomepageView] 未找到功能视图，跳转到模块: {moduleName}");
                    }
                }
                else if (parts.Length == 3)
                {
                    var moduleName = parts[0];
                    var subModuleName = parts[1];
                    var functionName = parts[2];

                    var viewType = NavigationDefinitions.GetFunctionViewType(moduleName, subModuleName, functionName);

                    if (viewType != null)
                    {
                        _comm.PublishFunctionNavigationRequested(moduleName, subModuleName, viewType);
                        TM.App.Log($"[HomepageView] 导航到功能: {moduleName}/{subModuleName}/{functionName} -> {viewType.FullName}");
                    }
                    else
                    {
                        ModuleSelected?.Invoke(moduleName);
                        TM.App.Log($"[HomepageView] 未找到功能视图，跳转到模块: {moduleName}");
                    }
                }
            }
        }
    }
}
