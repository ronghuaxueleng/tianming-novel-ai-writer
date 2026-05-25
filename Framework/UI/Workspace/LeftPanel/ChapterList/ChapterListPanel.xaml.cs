using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Modules.Generate.Elements.Chapter.Services;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Services.Framework.AI.SemanticKernel.Plugins;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Generated;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterPlanning;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;
using TM.Framework.UI.Workspace.Services;
using TM.Framework.Common.ViewModels;

namespace TM.Framework.UI.Workspace.LeftPanel.ChapterList
{
    public partial class ChapterListPanel : UserControl
    {
        private readonly IGeneratedContentService _contentService;
        private readonly ChapterService _chapterService;
        private readonly VolumeDesignService _volumeDesignService;
        private readonly ChapterListViewModel _viewModel;
        private bool _hasLoggedFirstLoad;

        private UIStateCache? _uiStateCache;
        private UIStateCache UiStateCache => _uiStateCache ??= ServiceLocator.Get<UIStateCache>();
        private PanelCommunicationService? _panelComm;
        private PanelCommunicationService PanelComm => _panelComm ??= ServiceLocator.Get<PanelCommunicationService>();

        public event EventHandler<ChapterInfo>? ChapterSelected;

        public event EventHandler<string>? ChapterDeleted;

        public ChapterListPanel()
        {
            InitializeComponent();
            _contentService = ServiceLocator.Get<IGeneratedContentService>();
            _chapterService = ServiceLocator.Get<ChapterService>();
            _volumeDesignService = ServiceLocator.Get<VolumeDesignService>();
            _viewModel = new ChapterListViewModel(PanelComm);
            _viewModel.ChapterSelected += (s, chapter) => ChapterSelected?.Invoke(this, chapter);
            _viewModel.ChapterDeleted += (s, chapterId) => ChapterDeleted?.Invoke(this, chapterId);
            _viewModel.SetRefreshCallback(LoadChaptersAsync);
            DataContext = _viewModel;

            var uiCache = UiStateCache;
            if (uiCache.IsWarmedUp)
            {
                _viewModel.ShowEmptyGuide = !uiCache.HasChaptersOrVolumes;
            }

            PanelComm.NewChapterFromHomepageRequested += OnNewChapterFromHomepage;

            _volumeDesignService.DataChanged += OnVolumeDesignDataChanged;
            _chapterService.DataChanged += OnVolumeDesignDataChanged;
            Unloaded += OnUnloaded;

            _ = LoadChaptersAsync();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _volumeDesignService.DataChanged -= OnVolumeDesignDataChanged;
            _chapterService.DataChanged -= OnVolumeDesignDataChanged;
            PanelComm.NewChapterFromHomepageRequested -= OnNewChapterFromHomepage;
            Unloaded -= OnUnloaded;
        }

        private TM.Services.Framework.AI.SemanticKernel.SKChatService? _skChatService;

        private void OnVolumeDesignDataChanged(object? sender, EventArgs e)
        {
            try
            {
                _skChatService ??= ServiceLocator.Get<TM.Services.Framework.AI.SemanticKernel.SKChatService>();
                if (_skChatService.IsWorkspaceBatchGenerating)
                    return;
            }
            catch { }

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
                await System.Threading.Tasks.Task.WhenAll(
                    _volumeDesignService.InitializeAsync(),
                    _chapterService.InitializeAsync());

                var volumeDesigns = _volumeDesignService.GetAllVolumeDesigns()
                    .Where(v => v.VolumeNumber > 0)
                    .OrderBy(v => v.VolumeNumber)
                    .ToList();

                var subscribedVolumes = volumeDesigns.Select(MapToVolumeInfo).ToList();
                var subscribedVolumeNumbers = subscribedVolumes.Select(v => v.Number).ToHashSet();
                var rewriteVolumes = _chapterService.GetRewriteCategories()
                    .Select(MapToRewriteVolumeInfo)
                    .Where(v => v.Number > 0)
                    .Where(v => !subscribedVolumeNumbers.Contains(v.Number))
                    .OrderBy(v => v.Order)
                    .ToList();
                var volumes = subscribedVolumes.Concat(rewriteVolumes).ToList();

                var chapters = await _contentService.GetGeneratedChaptersAsync();

                _viewModel.ShowEmptyGuide = volumes.Count == 0 && chapters.Count == 0;
                _viewModel.BuildChapterTree(volumes, chapters);
                UiStateCache.SetChapterState(volumes.Count, chapters.Count());

                var totalWords = chapters.Sum(c => c.WordCount);
                StatsText.Text = $"共 {chapters.Count()} 章 / {totalWords:N0} 字";

                if (!_hasLoggedFirstLoad)
                {
                    _hasLoggedFirstLoad = true;
                    TM.App.Log($"[ChapterListPanel] 加载了 {volumes.Count} 个分类, {chapters.Count()} 个章节");
                }
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
                name = $"第{data.VolumeNumber}卷";

            var isAutoCreated = string.IsNullOrEmpty(data.VolumeTitle)
                             && data.StartChapter == 0
                             && data.EndChapter == 0;

            return new VolumeInfo
            {
                Id = isAutoCreated ? data.Id : $"vol{data.VolumeNumber}",
                Name = name,
                Icon = isAutoCreated ? "Icon.Document" : "Icon.Books",
                Number = data.VolumeNumber,
                Order = data.VolumeNumber,
                Source = isAutoCreated ? "rewrite_vds" : "volume_design",
                IsReadOnly = !isAutoCreated
            };
        }

        private static VolumeInfo MapToRewriteVolumeInfo(ChapterCategory category)
        {
            var volumeNumber = TryExtractVolumeNumber(category.Name);
            return new VolumeInfo
            {
                Id = category.Id,
                Name = category.Name,
                Icon = string.IsNullOrWhiteSpace(category.Icon) ? "Icon.Document" : category.Icon,
                Number = volumeNumber,
                Order = volumeNumber > 0 ? volumeNumber : category.Order,
                Source = "rewrite",
                IsReadOnly = false
            };
        }

        private static readonly System.Text.RegularExpressions.Regex _volNumRegex =
            new(@"第\s*(\d+)\s*卷", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static int TryExtractVolumeNumber(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return 0;

            var match = _volNumRegex.Match(name);
            return match.Success && int.TryParse(match.Groups[1].Value, out var volumeNumber)
                ? volumeNumber
                : 0;
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
            var confirmed = StandardDialog.ShowConfirm(
                "当前创建的分类仅用于润色测试使用。\n\n正式创作时会在生成章节时自动同步分卷结构，无需手动构建分类。\n\n是否继续创建？",
                "创建分类提示",
                Window.GetWindow(this));
            if (!confirmed)
                return;

            if (_viewModel.AddCategoryCommand.CanExecute(null))
                _viewModel.AddCategoryCommand.Execute(null);
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
                GlobalToast.Error("操作失败", $"操作失败：{ex.Message}");
                TM.App.Log($"[ChapterListPanel] 开始创作跳转失败: {ex.Message}");
            }
        }
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class ChapterListViewModel : INotifyPropertyChanged
    {
        private readonly IGeneratedContentService _contentService;
        private readonly ChapterService _chapterService;
        private readonly VolumeDesignService _volumeDesignService;
        private readonly PanelCommunicationService _panelComm;
        private Func<Task>? _refreshCallback;
        private bool _showEmptyGuide;

        private const int LazyLoadThreshold = 200;
        private IList<ChapterInfo> _allChapters = Array.Empty<ChapterInfo>();
        private readonly Dictionary<TreeNodeItem, (IList<ChapterInfo> Chapters, string OriginalName)> _lazyVolumeChapters = new();
        private static readonly object _placeholderTag = new();
        private static readonly System.Text.RegularExpressions.Regex _volNumRegex =
            new(@"第\s*(\d+)\s*卷", System.Text.RegularExpressions.RegexOptions.Compiled);

        public RangeObservableCollection<TreeNodeItem> ChapterTree { get; } = new();

        public ICommand SelectChapterCommand { get; }
        public ICommand AddCategoryCommand { get; }
        public ICommand DeleteCategoryCommand { get; }
        public ICommand DeleteAllCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand AddChapterCommand { get; }
        public ICommand AddChapterWithConfirmCommand { get; }

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

        public ChapterListViewModel(PanelCommunicationService panelComm)
        {
            _panelComm = panelComm;
            _contentService = ServiceLocator.Get<IGeneratedContentService>();
            _chapterService = ServiceLocator.Get<ChapterService>();
            _volumeDesignService = ServiceLocator.Get<VolumeDesignService>();
            SelectChapterCommand = new RelayCommand(OnSelectChapter);
            AddCategoryCommand = new AsyncRelayCommand(OnAddCategoryAsync);
            DeleteCategoryCommand = new AsyncRelayCommand(OnDeleteCategoryAsync);
            DeleteAllCommand = new AsyncRelayCommand(OnDeleteAllAsync);
            RefreshCommand = new AsyncRelayCommand(OnRefreshAsync);
            AddChapterCommand = new AsyncRelayCommand(OnAddChapterAsync);
            AddChapterWithConfirmCommand = new AsyncRelayCommand(OnAddChapterWithConfirmAsync);
        }

        private async Task OnAddChapterWithConfirmAsync()
        {
            var confirmed = StandardDialog.ShowConfirm(
                "当前新建的章节仅用于润色测试使用。\n\n正式创作时会在生成章节时自动创建，无需手动新建。\n\n是否继续新建？",
                "新建章节提示");
            if (!confirmed)
                return;

            await OnAddChapterAsync();
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
                await _chapterService.InitializeAsync();

                const string categoryName = "第1卷";
                var existing = _chapterService.GetRewriteCategories();
                if (existing.Any(c => string.Equals(c.Name, categoryName, StringComparison.Ordinal)))
                {
                    GlobalToast.Warning("分类已存在", "第1卷仿写分类已存在，可直接新建章节");
                    return;
                }

                var category = new ChapterCategory
                {
                    Name = categoryName,
                    Icon = "Icon.Document",
                    Order = 1,
                    IsEnabled = true
                };

                var added = await _chapterService.AddRewriteCategoryAsync(category);
                if (!added)
                {
                    GlobalToast.Warning("分类已存在", "第1卷已在订阅分卷中存在，可直接新建章节");
                    return;
                }

                TM.App.Log($"[ChapterListPanel] 仿写路径新建第1卷分类（ChapterService）");

                if (_refreshCallback != null)
                    await _refreshCallback();

                GlobalToast.Success("分类已创建", "第1卷已添加，可开始新建章节");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("创建失败", $"创建失败：{ex.Message}");
                TM.App.Log($"[ChapterListPanel] 新建仿写分类失败: {ex.Message}");
            }
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
                if (volume.IsReadOnly)
                {
                    GlobalToast.Info("提示", "该卷来自分卷设计（只读），请在分卷设计中管理");
                    return;
                }

                if (!StandardDialog.ShowConfirm($"确定要删除仿写分类「{volume.Name}」吗？\n\n该分类下的章节内容将一并删除。", "确认删除"))
                    return;

                try
                {
                    var chapters = await _contentService.GetGeneratedChaptersAsync();
                    var volumeChapters = chapters.Where(c => c.VolumeNumber == volume.Number).ToList();
                    var deletedCount = 0;
                    foreach (var ch in volumeChapters)
                    {
                        var ok = await _contentService.DeleteChapterAsync(ch.Id);
                        if (ok || !_contentService.ChapterExists(ch.Id))
                        {
                            ChapterDeleted?.Invoke(this, ch.Id);
                            deletedCount++;
                        }
                    }
                    if (deletedCount > 0)
                        TM.App.Log($"[ChapterListPanel] 级联删除: {volume.Name} 下 {deletedCount} 个章节已删除");

                    if (volume.Source == "rewrite_vds")
                    {
                        _volumeDesignService.DeleteVolumeDesign(volume.Id);
                        TM.App.Log($"[ChapterListPanel] 仿写自动分卷已从 VolumeDesignService 删除: {volume.Name} ({volume.Id})");
                    }
                    else
                    {
                        await _chapterService.DeleteRewriteCategoryAsync(volume.Name);
                        TM.App.Log($"[ChapterListPanel] 仿写分类已删除: {volume.Name}");
                    }

                    CurrentChapterTracker.Clear();

                    if (_refreshCallback != null)
                        await _refreshCallback();

                    _panelComm.PublishRefreshChapterList();
                    GlobalToast.Success("分类已删除", $"「{volume.Name}」及 {deletedCount} 个章节已删除");
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ChapterListPanel] 删除失败: {ex.Message}");
                    GlobalToast.Error("删除失败", $"删除失败：{ex.Message}");
                }
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

                var rewriteCategories = _chapterService.GetRewriteCategories().ToList();
                var subscribedVolumes = _volumeDesignService.GetAllVolumeDesigns()
                    .Where(v => v.VolumeNumber > 0)
                    .ToList();

                var chapterCount = chapters.Count;
                var subscribedNote = subscribedVolumes.Count > 0
                    ? $"\n\n注意：{subscribedVolumes.Count} 个订阅分卷（来自「分卷设计」）不会被删除，如需删除请前往【分卷设计】管理。"
                    : string.Empty;
                if (!StandardDialog.ShowConfirm(
                    $"确定要删除所有章节内容吗？\n\n将删除：{chapterCount} 个章节{(rewriteCategories.Count > 0 ? $"、{rewriteCategories.Count} 个手动分类" : "")}{subscribedNote}\n\n此操作不可撤销！",
                    "全部删除"))
                    return;

                if (!StandardDialog.ShowConfirm(
                    "最终确认\n\n这将永久删除所有章节内容！\n\n请再次确认是否继续？",
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

                foreach (var cat in rewriteCategories)
                {
                    try
                    {
                        await _chapterService.DeleteRewriteCategoryAsync(cat.Name);
                        TM.App.Log($"[ChapterListPanel] 全部删除：已清理仿写分类 {cat.Name}");
                    }
                    catch (Exception catEx)
                    {
                        TM.App.Log($"[ChapterListPanel] 清理仿写分类失败: {cat.Name} - {catEx.Message}");
                    }
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
                    GlobalToast.Success("清空成功", $"已删除 {deletedChapters} 个章节" + (rewriteCategories.Count > 0 ? $"、{rewriteCategories.Count} 个分类" : ""));
                }
                TM.App.Log($"[ChapterListPanel] 全部删除完成: 章节成功={deletedChapters}, 失败={failedChapters}, 分类={rewriteCategories.Count}");
            }
            catch (Exception ex)
            {
                GlobalToast.Error("删除失败", $"删除失败：{ex.Message}");
                TM.App.Log($"[ChapterListPanel] 全部删除失败: {ex.Message}");
            }
        }

        public void BuildChapterTree(IList<VolumeInfo> volumes, IList<ChapterInfo> chapters)
        {
            foreach (var kv in _lazyVolumeChapters)
                kv.Key.PropertyChanged -= OnVolumeNodePropertyChanged;
            _lazyVolumeChapters.Clear();

            _allChapters = chapters;

            var useLazyLoad = chapters.Count > LazyLoadThreshold;
            var nodes = new List<TreeNodeItem>();

            foreach (var volume in volumes.OrderBy(v => v.Order))
            {
                var volumeChapters = ((volume.Source == "rewrite" || volume.Source == "rewrite_vds")
                    ? chapters.Where(c => c.VolumeNumber == volume.Number).OrderBy(c => c.ChapterNumber)
                    : chapters.Where(c => c.Id.StartsWith(volume.Id + "_")).OrderBy(c => c.ChapterNumber))
                    .ToList();

                var volumeNode = new TreeNodeItem
                {
                    Name = useLazyLoad && volumeChapters.Count > 0
                        ? $"{volume.Name} ({volumeChapters.Count})"
                        : volume.Name,
                    Icon = IconHelper.TryGet(volume.Icon),
                    Tag = volume,
                    Level = 1,
                    IsExpanded = !useLazyLoad,
                    ShowChildCount = !useLazyLoad
                };

                if (useLazyLoad && volumeChapters.Count > 0)
                {
                    volumeNode.Children.Add(new TreeNodeItem { Tag = _placeholderTag });
                    _lazyVolumeChapters[volumeNode] = (volumeChapters, volume.Name);
                    volumeNode.PropertyChanged += OnVolumeNodePropertyChanged;
                }
                else
                {
                    foreach (var chapter in volumeChapters)
                        volumeNode.Children.Add(CreateChapterNode(chapter));
                }

                nodes.Add(volumeNode);
            }

            var categorizedChapterIds = volumes
                .SelectMany(v => (v.Source == "rewrite" || v.Source == "rewrite_vds")
                    ? chapters.Where(c => c.VolumeNumber == v.Number).Select(c => c.Id)
                    : chapters.Where(c => c.Id.StartsWith(v.Id + "_")).Select(c => c.Id))
                .ToHashSet();

            var uncategorizedChapters = chapters
                .Where(c => !categorizedChapterIds.Contains(c.Id))
                .OrderBy(c => c.VolumeNumber)
                .ThenBy(c => c.ChapterNumber)
                .ToList();

            if (uncategorizedChapters.Count > 0)
            {
                var uncategorizedNode = new TreeNodeItem
                {
                    Name = useLazyLoad
                        ? $"未归类 ({uncategorizedChapters.Count})"
                        : "未归类",
                    Icon = IconHelper.Get("Icon.Document"),
                    Level = 1,
                    IsExpanded = !useLazyLoad,
                    ShowChildCount = !useLazyLoad
                };

                if (useLazyLoad)
                {
                    uncategorizedNode.Children.Add(new TreeNodeItem { Tag = _placeholderTag });
                    _lazyVolumeChapters[uncategorizedNode] = (uncategorizedChapters, "未归类");
                    uncategorizedNode.PropertyChanged += OnVolumeNodePropertyChanged;
                }
                else
                {
                    foreach (var chapter in uncategorizedChapters)
                        uncategorizedNode.Children.Add(CreateChapterNode(chapter));
                }

                nodes.Add(uncategorizedNode);
            }

            ChapterTree.ReplaceAll(nodes);
        }

        private static TreeNodeItem CreateChapterNode(ChapterInfo chapter)
        {
            return new TreeNodeItem
            {
                Name = "    " + chapter.Title,
                Icon = IconHelper.Get("Icon.Document"),
                Tag = chapter,
                Level = 2,
                ShowChildCount = false,
                ShowLevelIndicator = false,
                ShowIcon = false
            };
        }

        private void OnVolumeNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(TreeNodeItem.IsExpanded)) return;
            if (sender is not TreeNodeItem volumeNode || !volumeNode.IsExpanded) return;
            if (!_lazyVolumeChapters.TryGetValue(volumeNode, out var entry)) return;

            volumeNode.PropertyChanged -= OnVolumeNodePropertyChanged;
            _lazyVolumeChapters.Remove(volumeNode);

            var chapterNodes = entry.Chapters.Select(CreateChapterNode).ToList();
            volumeNode.Children.ReplaceAll(chapterNodes);

            volumeNode.Name = entry.OriginalName;
            volumeNode.ShowChildCount = true;
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
            return _allChapters.FirstOrDefault(c => c.Id == chapterId);
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

                var writer = ServiceLocator.Get<WriterPlugin>();
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
                GlobalToast.Error("新建失败", $"新建失败：{ex.Message}");
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
                "请输入目标章节",
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
            var designs = _volumeDesignService.GetAllVolumeDesigns()
                .ToList();

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
                var allVolumes = designs.Where(v => v.VolumeNumber > 0).ToList();
                if (allVolumes.Count == 1)
                    return (true, allVolumes[0].VolumeNumber, null);

                var rewriteCategories = _chapterService.GetRewriteCategories();
                if (rewriteCategories.Count == 1)
                {
                    var rewriteVol = TryExtractVolumeNumberFromName(rewriteCategories[0].Name);
                    if (rewriteVol > 0)
                        return (true, rewriteVol, null);
                }

                return (false, 0, $"未找到包含第{chapterNumber}章的分卷范围，请在分卷设计中配置章节范围或明确卷号。 ");
            }

            var hint = string.Join("，", matches.Select(m => $"第{m.VolumeNumber}卷"));
            return (false, 0, $"多个分卷范围命中第{chapterNumber}章：{hint}，请明确卷号。 ");
        }

        private static int TryExtractVolumeNumberFromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return 0;
            var m = _volNumRegex.Match(name);
            return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
        }

        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

}
