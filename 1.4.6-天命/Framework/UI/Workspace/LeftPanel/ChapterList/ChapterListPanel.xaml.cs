using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Controls.Dialogs;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Services;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Services.Framework.AI.SemanticKernel.Plugins;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Generated;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;
using TM.Framework.UI.Workspace.Services;

namespace TM.Framework.UI.Workspace.LeftPanel.ChapterList
{
    public partial class ChapterListPanel : UserControl
    {
        private readonly IGeneratedContentService _contentService;
        private readonly VolumeDesignService _volumeDesignService;
        private readonly ChapterListViewModel _viewModel;

        private UIStateCache? _uiStateCache;
        private UIStateCache UiStateCache => _uiStateCache ??= ServiceLocator.Get<UIStateCache>();
        private PanelCommunicationService? _panelComm;
        private PanelCommunicationService PanelComm => _panelComm ??= ServiceLocator.Get<PanelCommunicationService>();

        public event EventHandler<ChapterInfo>? ChapterSelected;

        public event EventHandler<string>? ChapterDeleted;

        public event EventHandler<NewChapterEventArgs>? NewChapterRequested;

        public ChapterListPanel()
        {
            InitializeComponent();
            _contentService = ServiceLocator.Get<IGeneratedContentService>();
            _volumeDesignService = ServiceLocator.Get<VolumeDesignService>();
            _viewModel = new ChapterListViewModel(PanelComm);
            _viewModel.ChapterSelected += (s, chapter) => ChapterSelected?.Invoke(this, chapter);
            _viewModel.ChapterDeleted += (s, chapterId) => ChapterDeleted?.Invoke(this, chapterId);
            _viewModel.NewChapterRequested += (s, args) => NewChapterRequested?.Invoke(this, args);
            _viewModel.SetRefreshCallback(LoadChaptersAsync);
            DataContext = _viewModel;

            var uiCache = UiStateCache;
            if (uiCache.IsWarmedUp)
            {
                _viewModel.ShowEmptyGuide = !uiCache.HasChaptersOrVolumes;
            }

            PanelComm.NewChapterFromHomepageRequested += OnNewChapterFromHomepage;

            _volumeDesignService.DataChanged += OnVolumeDesignDataChanged;
            Unloaded += OnUnloaded;

            _ = LoadChaptersAsync();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _volumeDesignService.DataChanged -= OnVolumeDesignDataChanged;
            Unloaded -= OnUnloaded;
        }

        private void OnVolumeDesignDataChanged(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => _ = LoadChaptersAsync()));
                return;
            }

            _ = LoadChaptersAsync();
        }

        private void OnNewChapterFromHomepage()
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_viewModel.AddChapterCommand.CanExecute(null))
                {
                    _viewModel.AddChapterCommand.Execute(null);
                }
            });
        }

        public async Task LoadChaptersAsync()
        {
            try
            {
                await _volumeDesignService.InitializeAsync();

                var volumeDesigns = _volumeDesignService.GetAllVolumeDesigns()
                    .Where(v => v.VolumeNumber > 0)
                    .OrderBy(v => v.VolumeNumber)
                    .ToList();

                var volumes = volumeDesigns.Select(MapToVolumeInfo).ToList();

                var chapters = await _contentService.GetGeneratedChaptersAsync();

                _viewModel.ShowEmptyGuide = volumes.Count == 0 && chapters.Count == 0;

                _viewModel.BuildChapterTree(volumes, chapters);

                var totalWords = chapters.Sum(c => c.WordCount);
                StatsText.Text = $"共 {chapters.Count()} 章 / {totalWords:N0} 字";

                TM.App.Log($"[ChapterListPanel] 加载了 {volumes.Count} 个分类, {chapters.Count()} 个章节");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ChapterListPanel] 加载章节失败: {ex.Message}");
            }
        }

        private static VolumeInfo MapToVolumeInfo(VolumeDesignData data)
        {
            var name = data.VolumeNumber > 0
                ? $"第{data.VolumeNumber}卷 {data.VolumeTitle}".Trim()
                : data.Name;

            if (string.IsNullOrWhiteSpace(name) && data.VolumeNumber > 0)
            {
                name = $"第{data.VolumeNumber}卷";
            }

            return new VolumeInfo
            {
                Id = $"vol{data.VolumeNumber}",
                Name = name,
                Icon = "📚",
                Number = data.VolumeNumber,
                Order = data.VolumeNumber
            };
        }

        public void SelectChapter(string chapterId)
        {
            if (string.IsNullOrEmpty(chapterId))
                return;

            var chapter = _viewModel.FindChapterById(chapterId);
            if (chapter != null)
            {
                ChapterSelected?.Invoke(this, chapter);
                TM.App.Log($"[ChapterListPanel] 选中章节: {chapterId}");
            }
        }

        private void OnQuickAction_NewCategory(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                GlobalToast.Info("提示", "卷分类来自分卷设计（只读），请在分卷设计中管理卷分类");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("操作失败", ex.Message);
                TM.App.Log($"[ChapterListPanel] 提示跳转分卷设计失败: {ex.Message}");
            }
        }

        private void OnOpenChapterFolder_Click(object sender, RoutedEventArgs e)
        {
            var path = TM.Framework.Common.Helpers.Storage.StoragePathHelper.GetProjectChaptersPath();
            if (System.IO.Directory.Exists(path))
                System.Diagnostics.Process.Start("explorer.exe", path);
            else
                GlobalToast.Warning("目录不存在", "章节目录尚未创建，请先生成章节内容");
        }

        private void OnQuickAction_Refresh(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_viewModel.RefreshCommand.CanExecute(null))
            {
                _viewModel.RefreshCommand.Execute(null);
            }
        }

        private void OnQuickAction_StartCreation(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                PanelComm.PublishFunctionNavigationRequested(
                    "Design",
                    "智能拆书",
                    typeof(TM.Modules.Design.SmartParsing.BookAnalysis.BookAnalysisView));
                TM.App.Log("[ChapterListPanel] 开始创作：跳转智能拆书-拆书分析");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("操作失败", ex.Message);
                TM.App.Log($"[ChapterListPanel] 开始创作跳转失败: {ex.Message}");
            }
        }
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ChapterListViewModel : INotifyPropertyChanged
    {
        private readonly IGeneratedContentService _contentService;
        private readonly VolumeDesignService _volumeDesignService;
        private readonly PanelCommunicationService _panelComm;
        private Func<Task>? _refreshCallback;
        private bool _showEmptyGuide;

        public ObservableCollection<TreeNodeItem> ChapterTree { get; } = new();

        public ICommand SelectChapterCommand { get; }
        public ICommand AddCategoryCommand { get; }
        public ICommand DeleteCategoryCommand { get; }
        public ICommand DeleteAllCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand AddChapterCommand { get; }

        public bool ShowEmptyGuide
        {
            get => _showEmptyGuide;
            set
            {
                if (_showEmptyGuide != value)
                {
                    _showEmptyGuide = value;
                    OnPropertyChanged();
                }
            }
        }

        public event EventHandler<ChapterInfo>? ChapterSelected;

        public event EventHandler<string>? ChapterDeleted;

        #pragma warning disable CS0067
        public event EventHandler<NewChapterEventArgs>? NewChapterRequested;
        #pragma warning restore CS0067

        public ChapterListViewModel(PanelCommunicationService panelComm)
        {
            _panelComm = panelComm;
            _contentService = ServiceLocator.Get<IGeneratedContentService>();
            _volumeDesignService = ServiceLocator.Get<VolumeDesignService>();
            SelectChapterCommand = new RelayCommand(OnSelectChapter);
            AddCategoryCommand = new AsyncRelayCommand(OnAddCategoryAsync);
            DeleteCategoryCommand = new AsyncRelayCommand(OnDeleteCategoryAsync);
            DeleteAllCommand = new AsyncRelayCommand(OnDeleteAllAsync);
            RefreshCommand = new AsyncRelayCommand(OnRefreshAsync);
            AddChapterCommand = new AsyncRelayCommand(OnAddChapterAsync);
        }

        public void SetRefreshCallback(Func<Task> callback)
        {
            _refreshCallback = callback;
        }

        private async Task OnRefreshAsync()
        {
            if (_refreshCallback != null)
            {
                await _refreshCallback();
                GlobalToast.Success("刷新成功", "章节列表已更新");
            }
        }

        private async Task OnAddCategoryAsync(object? param)
        {
            try
            {
                GlobalToast.Info("提示", "卷分类来自分卷设计（只读），请在分卷设计中管理卷分类");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("操作失败", ex.Message);
                TM.App.Log($"[ChapterListPanel] 提示跳转分卷设计失败: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        private async Task OnDeleteCategoryAsync(object? param)
        {
            if (param is not TreeNodeItem node)
            {
                GlobalToast.Warning("未选择", "请先选择要删除的项目");
                return;
            }

            if (node.Tag is VolumeInfo volume)
            {
                GlobalToast.Info("提示", "卷分类来自分卷设计（只读），请在分卷设计中管理卷分类");
                return;
            }
            else if (node.Tag is ChapterInfo chapter)
            {
                if (!StandardDialog.ShowConfirm($"确定要删除「{chapter.Title}」吗？", "确认删除"))
                    return;

                var chapterId = chapter.Id;
                var deleted = await _contentService.DeleteChapterAsync(chapterId);

                if (!deleted && _contentService.ChapterExists(chapterId))
                {
                    GlobalToast.Error("删除失败", $"章节文件无法删除（可能被占用），已中止级联清理");
                    TM.App.Log($"[ChapterListPanel] 章节文件删除失败且仍存在: {chapterId}");
                    return;
                }

                ChapterDeleted?.Invoke(this, chapterId);

                if (string.Equals(CurrentChapterTracker.CurrentChapterId, chapterId, StringComparison.OrdinalIgnoreCase))
                {
                    CurrentChapterTracker.Clear();
                }

                if (_refreshCallback != null)
                    await _refreshCallback();

                _panelComm.PublishRefreshChapterList();

                GlobalToast.Success("删除成功", $"已删除章节：{chapter.Title}");
            }
            else
            {
                GlobalToast.Warning("无法删除", "该节点不可删除");
            }
        }

        private async Task OnDeleteAllAsync()
        {
            try
            {
                var chapters = await _contentService.GetGeneratedChaptersAsync();

                if (chapters.Count == 0)
                {
                    GlobalToast.Info("暂无内容", "当前没有可删除的章节");
                    return;
                }

                var chapterCount = chapters.Count;
                if (!StandardDialog.ShowConfirm(
                    $"确定要删除所有章节内容吗？\n\n将删除：{chapterCount} 个章节\n\n此操作不可撤销！", 
                    "⚠️ 全部删除"))
                    return;

                if (!StandardDialog.ShowConfirm(
                    "⚠️ 最终确认 ⚠️\n\n这将永久删除所有章节内容！\n\n请再次确认是否继续？",
                    "危险操作"))
                    return;

                var deletedChapters = 0;
                var failedChapters = 0;

                CurrentChapterTracker.Clear();
                foreach (var chapter in chapters)
                {
                    var deleted = await _contentService.DeleteChapterAsync(chapter.Id);

                    if (!deleted && _contentService.ChapterExists(chapter.Id))
                    {
                        failedChapters++;
                        TM.App.Log($"[ChapterListPanel] 章节文件删除失败且仍存在，跳过级联: {chapter.Id}");
                        continue;
                    }

                    ChapterDeleted?.Invoke(this, chapter.Id);
                    deletedChapters++;
                }

                if (_refreshCallback != null)
                    await _refreshCallback();

                _panelComm.PublishRefreshChapterList();

                if (failedChapters > 0)
                {
                    GlobalToast.Warning("部分删除", $"已删除 {deletedChapters} 个章节，{failedChapters} 个文件删除失败（可能被占用）");
                }
                else
                {
                    GlobalToast.Success("清空成功", $"已删除 {deletedChapters} 个章节");
                }
                TM.App.Log($"[ChapterListPanel] 全部删除完成: 成功={deletedChapters}, 失败={failedChapters}");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("删除失败", ex.Message);
                TM.App.Log($"[ChapterListPanel] 全部删除失败: {ex.Message}");
            }
        }

        public void BuildChapterTree(IList<VolumeInfo> volumes, IList<ChapterInfo> chapters)
        {
            ChapterTree.Clear();

            foreach (var volume in volumes.OrderBy(v => v.Order))
            {
                var volumeNode = new TreeNodeItem
                {
                    Name = volume.Name,
                    Icon = volume.Icon,
                    Tag = volume,
                    Level = 1,
                    IsExpanded = true,
                    ShowChildCount = true
                };

                var volumeChapters = chapters
                    .Where(c => c.Id.StartsWith(volume.Id + "_"))
                    .OrderBy(c => c.ChapterNumber);

                foreach (var chapter in volumeChapters)
                {
                    volumeNode.Children.Add(new TreeNodeItem
                    {
                        Name = "    " + chapter.Title,
                        Icon = "📄",
                        Tag = chapter,
                        Level = 2,
                        ShowChildCount = false,
                        ShowLevelIndicator = false,
                        ShowIcon = false
                    });
                }

                ChapterTree.Add(volumeNode);
            }

            var categorizedChapterIds = volumes
                .SelectMany(v => chapters.Where(c => c.Id.StartsWith(v.Id + "_")).Select(c => c.Id))
                .ToHashSet();

            var uncategorizedChapters = chapters
                .Where(c => !categorizedChapterIds.Contains(c.Id))
                .OrderBy(c => c.VolumeNumber)
                .ThenBy(c => c.ChapterNumber);

            if (uncategorizedChapters.Any())
            {
                var uncategorizedNode = new TreeNodeItem
                {
                    Name = "未归类",
                    Icon = "📄",
                    Level = 1,
                    IsExpanded = true,
                    ShowChildCount = true
                };

                foreach (var chapter in uncategorizedChapters)
                {
                    uncategorizedNode.Children.Add(new TreeNodeItem
                    {
                        Name = "    " + chapter.Title,
                        Icon = "📄",
                        Tag = chapter,
                        Level = 2,
                        ShowChildCount = false,
                        ShowLevelIndicator = false,
                        ShowIcon = false
                    });
                }

                ChapterTree.Add(uncategorizedNode);
            }
        }

        private void OnSelectChapter(object? param)
        {
            if (param is TreeNodeItem node && node.Tag is ChapterInfo chapter)
            {
                TM.App.Log($"[ChapterListPanel] 选择章节: {chapter.Id}");

                CurrentChapterTracker.SetCurrentChapter(chapter.Id, chapter.Title);

                ChapterSelected?.Invoke(this, chapter);
            }
        }

        public ChapterInfo? FindChapterById(string chapterId)
        {
            foreach (var root in ChapterTree)
            {
                var chapter = FindChapterInNode(root, chapterId);
                if (chapter != null)
                    return chapter;
            }
            return null;
        }

        private ChapterInfo? FindChapterInNode(TreeNodeItem node, string chapterId)
        {
            if (node.Tag is ChapterInfo chapter && chapter.Id == chapterId)
                return chapter;

            foreach (var child in node.Children)
            {
                var found = FindChapterInNode(child, chapterId);
                if (found != null)
                    return found;
            }

            return null;
        }

        #region 新建章级

        private async Task OnAddChapterAsync()
        {
            try
            {
                var chapters = await _contentService.GetGeneratedChaptersAsync();

                var baseChapterNumber = 0;
                if (CurrentChapterTracker.HasCurrentChapter)
                {
                    var parsed = ChapterParserHelper.ParseChapterId(CurrentChapterTracker.CurrentChapterId);
                    if (parsed.HasValue)
                        baseChapterNumber = parsed.Value.chapterNumber;
                }

                if (baseChapterNumber <= 0 && chapters.Count > 0)
                    baseChapterNumber = chapters.Max(c => c.ChapterNumber);

                var targetChapterNumber = baseChapterNumber > 0 ? baseChapterNumber + 1 : 1;
                var (_, chapterNumber, chapterId) = await ResolveNewChapterIdAsync(targetChapterNumber);
                if (string.IsNullOrWhiteSpace(chapterId))
                    return;

                if (_contentService.ChapterExists(chapterId))
                {
                    GlobalToast.Warning("已存在", $"章节 {chapterId} 已存在");
                    return;
                }

                var chapterTitle = $"第{chapterNumber}章：";
                var initialContent = string.Empty;

                TM.App.Log($"[ChapterListPanel] 新建章节（仿写路径）: {chapterId}, 标题: {chapterTitle}");

                var writer = new WriterPlugin();
                var saved = await writer.SaveExternalChapterAsync(
                    System.Threading.CancellationToken.None,
                    chapterTitle,
                    initialContent,
                    chapterId);

                if (_refreshCallback != null)
                    await _refreshCallback();

                _panelComm.PublishChapterSelected(saved.ChapterId, saved.Title, saved.DisplayContent);

                TM.App.Log($"[ChapterListPanel] 新建章节完成: {saved.ChapterId}");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("新建失败", ex.Message);
                TM.App.Log($"[ChapterListPanel] 新建章节失败: {ex.Message}");
            }
        }

        private async Task<(int volumeNumber, int chapterNumber, string chapterId)> ResolveNewChapterIdAsync(int suggestedChapterNumber)
        {
            var auto = await TryResolveVolumeNumberForChapterAsync(suggestedChapterNumber);
            if (auto.success)
            {
                var autoId = ChapterParserHelper.BuildChapterId(auto.volumeNumber, suggestedChapterNumber);
                return (auto.volumeNumber, suggestedChapterNumber, autoId);
            }

            var input = StandardDialog.ShowInput(
                $"无法根据分卷范围推导第{suggestedChapterNumber}章所属卷，请输入目标章节（如：第2卷第3章 或 vol2_ch3）：",
                "新建章节",
                $"第{suggestedChapterNumber}章");

            if (string.IsNullOrWhiteSpace(input))
            {
                return (0, 0, string.Empty);
            }

            var trimmed = input.Trim();

            var parsedId = ChapterParserHelper.ParseChapterId(trimmed);
            if (parsedId.HasValue)
            {
                var id = ChapterParserHelper.BuildChapterId(parsedId.Value.volumeNumber, parsedId.Value.chapterNumber);
                return (parsedId.Value.volumeNumber, parsedId.Value.chapterNumber, id);
            }

            var (vol, ch) = ChapterParserHelper.ParseFromNaturalLanguage(trimmed);
            if (vol.HasValue && ch.HasValue)
            {
                var id = ChapterParserHelper.BuildChapterId(vol.Value, ch.Value);
                return (vol.Value, ch.Value, id);
            }

            if (ch.HasValue)
            {
                var resolved = await TryResolveVolumeNumberForChapterAsync(ch.Value);
                if (resolved.success)
                {
                    var id = ChapterParserHelper.BuildChapterId(resolved.volumeNumber, ch.Value);
                    return (resolved.volumeNumber, ch.Value, id);
                }

                StandardDialog.ShowWarning(resolved.errorMessage ?? "无法推导卷号，请明确卷号。", "无法新建");
                return (0, 0, string.Empty);
            }

            StandardDialog.ShowWarning("无法识别章节格式，请输入如：第2卷第3章 或 vol2_ch3。", "无法新建");
            return (0, 0, string.Empty);
        }

        private async Task<(bool success, int volumeNumber, string? errorMessage)> TryResolveVolumeNumberForChapterAsync(int chapterNumber)
        {
            if (chapterNumber <= 0)
            {
                return (false, 0, "章节号无效");
            }

            await _volumeDesignService.InitializeAsync();
            var designs = _volumeDesignService.GetAllVolumeDesigns();

            var matches = designs
                .Where(v => v.VolumeNumber > 0)
                .Where(v => v.StartChapter > 0 && v.EndChapter > 0)
                .Where(v => chapterNumber >= v.StartChapter && chapterNumber <= v.EndChapter)
                .ToList();

            if (matches.Count == 1)
            {
                return (true, matches[0].VolumeNumber, null);
            }

            if (matches.Count == 0)
            {
                return (false, 0, $"未找到包含第{chapterNumber}章的分卷范围，请在分卷设计中配置章节范围或明确卷号。 ");
            }

            var hint = string.Join("，", matches.Select(m => $"第{m.VolumeNumber}卷"));
            return (false, 0, $"多个分卷范围命中第{chapterNumber}章：{hint}，请明确卷号。 ");
        }

        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class NewChapterEventArgs : EventArgs
    {
        public int VolumeNumber { get; set; }
        public int ChapterNumber { get; set; }
        public string ChapterId { get; set; } = string.Empty;
        public string ChapterTitle { get; set; } = string.Empty;
        public string InitialContent { get; set; } = string.Empty;
    }
}
