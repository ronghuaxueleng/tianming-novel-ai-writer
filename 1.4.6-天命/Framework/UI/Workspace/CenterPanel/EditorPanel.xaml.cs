using System;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TM.Framework.Common.Services;
using TM.Framework.UI.Workspace.CenterPanel.ChapterEditor;
using TM.Framework.UI.Workspace.CenterPanel.Controls;
using TM.Framework.UI.Workspace.Services;
using TM.Services.Modules.ProjectData.Implementations;

namespace TM.Framework.UI.Workspace.CenterPanel
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class EditorPanel : UserControl
    {
        private readonly EditorTabManager _tabManager;
        private readonly PlanViewModel _planViewModel;
        private readonly DiffViewerViewModel _diffViewModel = new();
        private readonly PanelCommunicationService _comm;
        private readonly GenerationGate _generationGate;
        private readonly GeneratedContentService _contentService;
        private readonly UIStateCache _uiStateCache;

        public EditorPanel()
        {
            InitializeComponent();

            _comm = ServiceLocator.Get<PanelCommunicationService>();
            _generationGate = ServiceLocator.Get<GenerationGate>();
            _contentService = ServiceLocator.Get<GeneratedContentService>();
            _uiStateCache = ServiceLocator.Get<UIStateCache>();

            _planViewModel = ServiceLocator.Get<PlanViewModel>();

            _tabManager = new EditorTabManager();
            EditorTabBar.BindTabManager(_tabManager);

            InitializeHomeView();

            HomepagePanel.ModuleSelected += OnHomepageModuleSelected;
            DashboardPanel.ModuleSelected += OnHomepageModuleSelected;

            PlanViewPanel.DataContext = _planViewModel;

            BindDiffViewerViewModel(_diffViewModel);

            _diffViewModel.Accepted += (chapterId, paragraphIndex, modifiedContent) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (chapterId == ChapterEditor.CurrentChapterId)
                    {
                        ChapterEditor.ApplyInlineDiff(_diffViewModel.OriginalContent, _diffViewModel.ModifiedContent);
                    }

                    DiffViewerPanel.Visibility = Visibility.Collapsed;
                });
            };

            _diffViewModel.Rejected += () =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    DiffViewerPanel.Visibility = Visibility.Collapsed;
                });
            };

            _comm.ChapterSelected += (id, title, content) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    CurrentChapterTracker.SetCurrentChapter(id, title);
                    var protocol = _generationGate.ValidateChangesProtocol(content);
                    var displayContent = protocol.ContentWithoutChanges ?? content;
                    if (!_tabManager.OpenTab(id, title, displayContent, displayContent))
                    {
                        TM.Framework.Common.Controls.Dialogs.StandardDialog.ShowWarning(
                            "标签已满", "最多只能打开6个标签，请先关闭一些标签。");
                    }
                });
            };

            _comm.ChapterDeleted += (chapterId) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    var tab = _tabManager.Tabs.FirstOrDefault(t => t.Id == chapterId);
                    if (tab != null)
                    {
                        _tabManager.CloseTab(tab);
                    }
                });
            };

            _comm.NewChapterRequested += (chapterId, chapterTitle, initialContent, isNew) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    CurrentChapterTracker.SetCurrentChapter(chapterId, chapterTitle);
                    var displayTitle = "新章节（未保存）";
                    if (!_tabManager.OpenTab(chapterId, displayTitle, initialContent, initialContent))
                    {
                        TM.Framework.Common.Controls.Dialogs.StandardDialog.ShowWarning(
                            "标签已满", "最多只能打开6个标签，请先关闭一些标签。");
                    }
                    else
                    {
                        var tab = _tabManager.Tabs.FirstOrDefault(t => t.Id == chapterId);
                        if (tab != null && isNew)
                        {
                            tab.IsNew = true;
                        }
                        TM.App.Log($"[EditorPanel] 新建章节标签页: {chapterId}");
                    }
                });
            };

            _comm.ChapterNavigationRequested += async (chapterId) =>
            {
                try
                {
                    var genService = _contentService;
                    var chapters = await genService.GetGeneratedChaptersAsync();
                    var chapter = chapters?.FirstOrDefault(c => c.Id == chapterId);

                    if (chapter != null)
                    {
                        var content = await genService.GetChapterAsync(chapterId) ?? "";
                        await Dispatcher.InvokeAsync(() =>
                        {
                            var protocol = _generationGate.ValidateChangesProtocol(content);
                            var displayContent = protocol.ContentWithoutChanges ?? content;
                            CurrentChapterTracker.SetCurrentChapter(chapter.Id ?? chapterId, chapter.Title ?? chapterId);
                            _tabManager.OpenTab(chapter.Id ?? chapterId, chapter.Title ?? chapterId, displayContent, displayContent);
                        });
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[EditorPanel] 导航到章节失败: {ex.Message}");
                }
            };

            _comm.ContentGenerated += (id, title, content) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    var protocol = _generationGate.ValidateChangesProtocol(content);
                    var displayContent = protocol.ContentWithoutChanges ?? content;
                    CurrentChapterTracker.SetCurrentChapter(id, title);
                    if (!_tabManager.OpenTab(id, title, displayContent, displayContent))
                    {
                        ChapterEditor.LoadNewContent(id, title, displayContent);
                    }
                });
            };

            _comm.ShowPlanViewChanged += (show) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (show)
                    {
                        _tabManager.OpenTab("plan", "执行计划", "", "", EditorTabContentType.Plan);
                    }
                    else
                    {
                        var planTab = _tabManager.Tabs.FirstOrDefault(t => t.Id == "plan");
                        if (planTab != null)
                        {
                            _tabManager.CloseTab(planTab);
                        }
                    }
                });
            };

            _comm.ShowDiffRequested += (id, original, modified) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    _diffViewModel.SetDiff(id, -1, original, modified);
                    DiffViewerPanel.Visibility = Visibility.Visible;
                });
            };

            _tabManager.ActiveTabChanged += (s, tab) =>
            {
                if (tab != null)
                {
                    var showHome = tab.IsHomepage;
                    UpdateHomeViewVisibility(showHome);

                    switch (tab.ContentType)
                    {
                        case EditorTabContentType.Plan:
                            ChapterEditor.Visibility = Visibility.Collapsed;
                            PlanViewPanel.Visibility = Visibility.Visible;
                            break;

                        case EditorTabContentType.Chapter:
                            ChapterEditor.Visibility = Visibility.Visible;
                            ChapterEditor.LoadTabContent(tab.Id, tab.Content, tab.OriginalContent);
                            PlanViewPanel.Visibility = Visibility.Collapsed;
                            break;

                        case EditorTabContentType.Homepage:
                            ChapterEditor.Visibility = Visibility.Collapsed;
                            PlanViewPanel.Visibility = Visibility.Collapsed;
                            break;

                        default:
                            ChapterEditor.Visibility = Visibility.Visible;
                            ChapterEditor.Clear();
                            PlanViewPanel.Visibility = Visibility.Collapsed;
                            break;
                    }
                }
                else
                {
                    UpdateHomeViewVisibility(true);
                    ChapterEditor.Visibility = Visibility.Collapsed;
                    PlanViewPanel.Visibility = Visibility.Collapsed;
                }
            };

            ChapterEditor.ContentModified += (s, args) =>
            {
                _tabManager.UpdateTabContent(args.Id, args.Content);
            };

            ChapterEditor.ChapterSaved += (chapterId, persisted) =>
            {
                Dispatcher.InvokeAsync(() => _tabManager.MarkTabSaved(chapterId, persisted));
            };

            _tabManager.AllTabsClosed += () =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    ChapterEditor.SwitchToEditMode();
                });
            };

            _tabManager.TabClosing = (tab) =>
            {
                if (tab.Id == "plan")
                {
                    return true;
                }

                if (tab.IsModified)
                {
                    return TM.Framework.Common.Controls.Dialogs.StandardDialog.ShowConfirm(
                        $"\"{tab.Title}\" 有未保存的修改，确定要关闭吗？", "未保存的更改");
                }
                return true;
            };

            _comm.RefreshChapterListRequested += OnRefreshChapterListRequested;

            try
            {
                var projectManager = ServiceLocator.Get<ProjectManager>();
                projectManager.ProjectSwitched += _ =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        var chapterTabs = _tabManager.Tabs
                            .Where(t => t.ContentType == EditorTabContentType.Chapter)
                            .ToList();
                        foreach (var tab in chapterTabs)
                            _tabManager.CloseTab(tab);
                    });
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EditorPanel] 订阅项目切换事件失败: {ex.Message}");
            }

            this.Unloaded += (s, e) =>
            {
                _comm.RefreshChapterListRequested -= OnRefreshChapterListRequested;
                _planViewModel.Dispose();
            };
        }

        private void OnRefreshChapterListRequested()
        {
            _ = OnRefreshChapterListRequestedAsync();
        }

        private async Task OnRefreshChapterListRequestedAsync()
        {
            try
            {
                var contentService = _contentService;
                var chapters = await contentService.GetGeneratedChaptersAsync();
                var volumeService = ServiceLocator.Get<TM.Modules.Generate.Elements.VolumeDesign.Services.VolumeDesignService>();
                await volumeService.InitializeAsync();
                var volumes = volumeService.GetAllVolumeDesigns();

                _uiStateCache.SetChapterState(volumes.Count, chapters.Count);

                await Dispatcher.InvokeAsync(() =>
                {
                    var hasActiveTabs = _tabManager.Tabs.Any(t => !t.IsHomepage);
                    if (!hasActiveTabs)
                    {
                        UpdateHomeViewVisibility(true);
                    }
                });

                TM.App.Log($"[EditorPanel] 刷新章节列表状态: 卷={volumes.Count}, 章={chapters.Count}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EditorPanel] 刷新章节列表状态失败: {ex.Message}");
            }
        }

        public void BindDiffViewerViewModel(object diffViewModel)
        {
            DiffViewerPanel.DataContext = diffViewModel;
        }

        private void OnHomepageModuleSelected(string moduleName)
        {
            try
            {
                _comm.PublishModuleNavigationRequested(moduleName);
                TM.App.Log($"[EditorPanel] 请求导航到模块: {moduleName}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EditorPanel] 模块导航失败: {ex.Message}");
            }
        }

        private void InitializeHomeView()
        {
            var uiCache = _uiStateCache;
            if (uiCache.IsWarmedUp)
            {
                if (uiCache.HasChaptersOrVolumes)
                {
                    HomepagePanel.Visibility = Visibility.Collapsed;
                    DashboardPanel.Visibility = Visibility.Visible;
                }
                else
                {
                    HomepagePanel.Visibility = Visibility.Visible;
                    DashboardPanel.Visibility = Visibility.Collapsed;
                }
                TM.App.Log($"[EditorPanel] 使用预缓存状态初始化: 显示{(uiCache.HasChaptersOrVolumes ? "仪表盘" : "引导")}");
            }
            else
            {
                HomepagePanel.Visibility = Visibility.Visible;
                DashboardPanel.Visibility = Visibility.Collapsed;
                TM.App.Log("[EditorPanel] 预缓存未就绪，默认显示引导");
            }
        }

        private void UpdateHomeViewVisibility(bool showHome)
        {
            if (!showHome)
            {
                HomepagePanel.Visibility = Visibility.Collapsed;
                DashboardPanel.Visibility = Visibility.Collapsed;
                return;
            }

            if (_uiStateCache.HasChaptersOrVolumes)
            {
                HomepagePanel.Visibility = Visibility.Collapsed;
                DashboardPanel.Visibility = Visibility.Visible;
            }
            else
            {
                HomepagePanel.Visibility = Visibility.Visible;
                DashboardPanel.Visibility = Visibility.Collapsed;
            }
        }
    }
}
