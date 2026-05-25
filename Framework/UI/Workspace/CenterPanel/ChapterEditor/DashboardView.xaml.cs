using System;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TM.Framework.UI.Workspace.Services;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Services.Modules.ProjectData.Implementations;

namespace TM.Framework.UI.Workspace.CenterPanel.ChapterEditor
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class DashboardView : UserControl
    {
        private static readonly string[] _weekDays = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };

        public event Action<string>? ModuleSelected;

        private readonly PanelCommunicationService _comm;
        private readonly GeneratedContentService _contentService;
        private readonly GenerationGate _generationGate;
        private readonly VolumeDesignService _volumeDesignService;
        private readonly DispatcherTimer _clockTimer;
        private System.Threading.CancellationTokenSource? _dashboardDebounceCtx;

        public DashboardView()
        {
            InitializeComponent();

            _comm = ServiceLocator.Get<PanelCommunicationService>();
            _contentService = ServiceLocator.Get<GeneratedContentService>();
            _generationGate = ServiceLocator.Get<GenerationGate>();
            _volumeDesignService = ServiceLocator.Get<VolumeDesignService>();

            TM.Framework.Common.Helpers.UI.AppIconLoader.Load(AppIconBorder, 56, FallbackIconImage, "DashboardView");

            _clockTimer = new DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _clockTimer.Tick += (s, e) => UpdateDateTime();
            _clockTimer.Start();

            UpdateDateTime();

            _ = LoadStatisticsAsync();

            _comm.RefreshChapterListRequested += OnRefreshRequested;
            _volumeDesignService.DataChanged += OnVolumeDesignDataChanged;

            this.Unloaded += (s, e) =>
            {
                _clockTimer.Stop();
                _comm.RefreshChapterListRequested -= OnRefreshRequested;
                _volumeDesignService.DataChanged -= OnVolumeDesignDataChanged;
            };
            this.Loaded += (s, e) =>
            {
                if (!_clockTimer.IsEnabled)
                    _clockTimer.Start();
                _ = LoadStatisticsAsync();
            };
        }

        private void OnRefreshRequested() => DebounceLoadStatistics();

        private void OnVolumeDesignDataChanged(object? sender, EventArgs e) => DebounceLoadStatistics();

        private void DebounceLoadStatistics()
        {
            _dashboardDebounceCtx?.Cancel();
            var cts = _dashboardDebounceCtx = new System.Threading.CancellationTokenSource();
            var token = cts.Token;
            _ = Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(150, token).ConfigureAwait(true);
                    if (!token.IsCancellationRequested)
                        await LoadStatisticsAsync();
                }
                catch (OperationCanceledException) { }
            });
        }

        private void UpdateDateTime()
        {
            var now = DateTime.Now;
            DateText.Text = $"{now:yyyy年M月d日} {_weekDays[(int)now.DayOfWeek]}";
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
                await _volumeDesignService.InitializeAsync();
                var volumeDesigns = _volumeDesignService.GetAllVolumeDesigns()
                    .ToList();

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
                GlobalToast.Error("打开失败", $"打开失败：{ex.Message}");
            }
        }
    }
}
