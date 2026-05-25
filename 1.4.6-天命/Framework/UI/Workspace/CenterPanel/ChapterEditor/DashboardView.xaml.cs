using System;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TM.Framework.Common.Services;
using TM.Framework.UI.Workspace.Services;
using TM.Services.Modules.ProjectData.Implementations;

namespace TM.Framework.UI.Workspace.CenterPanel.ChapterEditor
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class DashboardView : UserControl
    {
        public event Action<string>? ModuleSelected;

        private readonly PanelCommunicationService _comm;
        private readonly GeneratedContentService _contentService;
        private readonly GenerationGate _generationGate;
        private readonly DispatcherTimer _clockTimer;

        public DashboardView()
        {
            InitializeComponent();

            _comm = ServiceLocator.Get<PanelCommunicationService>();
            _contentService = ServiceLocator.Get<GeneratedContentService>();
            _generationGate = ServiceLocator.Get<GenerationGate>();

            LoadAppIcon();

            _clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _clockTimer.Tick += (s, e) => UpdateDateTime();
            _clockTimer.Start();

            UpdateDateTime();

            _ = LoadStatisticsAsync();

            _comm.RefreshChapterListRequested += OnRefreshRequested;

            this.Unloaded += (s, e) => 
            {
                _clockTimer.Stop();
                _comm.RefreshChapterListRequested -= OnRefreshRequested;
            };
            this.Loaded += (s, e) => 
            {
                if (!_clockTimer.IsEnabled)
                    _clockTimer.Start();
                _ = LoadStatisticsAsync();
            };
        }

        private void OnRefreshRequested()
        {
            _ = Dispatcher.InvokeAsync(() => _ = LoadStatisticsAsync());
        }

        private void UpdateDateTime()
        {
            var now = DateTime.Now;
            var weekDays = new[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
            DateText.Text = $"{now:yyyy年M月d日} {weekDays[(int)now.DayOfWeek]}";
            TimeText.Text = now.ToString("HH:mm:ss");

            var hour = now.Hour;
            WelcomeText.Text = hour switch
            {
                >= 5 and < 12 => "早上好",
                >= 12 and < 14 => "中午好",
                >= 14 and < 18 => "下午好",
                >= 18 and < 22 => "晚上好",
                _ => "夜深了"
            };
        }

        private async Task LoadStatisticsAsync()
        {
            try
            {
                var contentService = _contentService;

                var chapters = await contentService.GetGeneratedChaptersAsync();
                var volumeService = ServiceLocator.Get<TM.Modules.Generate.Elements.VolumeDesign.Services.VolumeDesignService>();
                await volumeService.InitializeAsync();
                var volumeDesigns = volumeService.GetAllVolumeDesigns();

                ChapterCountText.Text = chapters.Count.ToString();

                var totalWords = chapters.Sum(c => c.WordCount);
                WordCountText.Text = FormatNumber(totalWords);

                var recentChapter = chapters
                    .OrderByDescending(c => c.ModifiedTime)
                    .FirstOrDefault();

                if (recentChapter != null)
                {
                    var volumeNumber = ChapterParserHelper.ParseChapterId(recentChapter.Id)?.volumeNumber ?? 0;
                    var volume = volumeDesigns.FirstOrDefault(v => v.VolumeNumber == volumeNumber);
                    var volumeName = volumeNumber > 0
                        ? $"第{volumeNumber}卷 {volume?.VolumeTitle}".Trim()
                        : volume?.Name;

                    CurrentVolumeText.Text = string.IsNullOrWhiteSpace(volumeName)
                        ? "--"
                        : (volumeName.Length > 4 ? volumeName.Substring(0, 4) : volumeName);
                }
                else
                {
                    CurrentVolumeText.Text = "--";
                }

                if (recentChapter != null)
                {
                    RecentEditText.Text = recentChapter.Title;
                }
                else
                {
                    RecentEditText.Text = "暂无编辑记录";
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DashboardView] 加载统计数据失败: {ex.Message}");
            }
        }

        private static string FormatNumber(int number)
        {
            return number switch
            {
                >= 10000 => $"{number / 10000.0:F1}万",
                >= 1000 => $"{number / 1000.0:F1}K",
                _ => number.ToString()
            };
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

                var target = 56;
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
                TM.App.Log($"[DashboardView] 加载应用图标失败: {ex.Message}");
                AppIconBorder.Background = null;
                AppIconBorder.Visibility = Visibility.Collapsed;
                FallbackIconTextBlock.Visibility = Visibility.Visible;
            }
        }

        private void QuickAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string action)
                return;

            switch (action)
            {
                case "ContinueWriting":
                    _ = OpenRecentChapterAsync();
                    break;

                case "NewChapter":
                    _comm.PublishNewChapterFromHomepage();
                    break;

                case "Design":
                    ModuleSelected?.Invoke("Design");
                    break;

                case "SmartAssistant":
                    ModuleSelected?.Invoke("SmartAssistant");
                    break;
            }

            TM.App.Log($"[DashboardView] 快捷操作: {action}");
        }

        private async Task OpenRecentChapterAsync()
        {
            try
            {
                var contentService = _contentService;
                var chapters = await contentService.GetGeneratedChaptersAsync();

                var recentChapter = chapters
                    .OrderByDescending(c => c.ModifiedTime)
                    .FirstOrDefault();

                if (recentChapter != null)
                {
                    var content = await contentService.GetChapterAsync(recentChapter.Id) ?? "";
                    var protocol = _generationGate.ValidateChangesProtocol(content);
                    var displayContent = protocol.ContentWithoutChanges ?? content;
                    _comm.PublishChapterSelected(recentChapter.Id, recentChapter.Title, displayContent);
                }
                else
                {
                    GlobalToast.Info("暂无章节", "请先创建一个章节");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DashboardView] 继续写作失败: {ex.Message}");
                GlobalToast.Error("打开失败", ex.Message);
            }
        }
    }
}
