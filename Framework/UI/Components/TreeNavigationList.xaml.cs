using System;
using System.Reflection;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Threading.Tasks;
using TM.Framework.Common.Controls;
using TM.Framework.Common.ViewModels;

namespace TM.Framework.UI.Components
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class TreeNavigationList : UserControl
    {
        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();
        private readonly RangeObservableCollection<TreeNodeItem> _fileTreeItems = new();
        private TreeNodeItem? _rootNode;
        private TreeNodeItem? _lastContextNode;
        private readonly ICommand _nodeSelectedCommand;
        private readonly ICommand _fileNodeDoubleClickCommand;
        private readonly Dictionary<string, TreeNodeItem> _nodeIndex = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<TreeNodeItem, TreeNodeItem?> _parentMap = new();
        private readonly Dictionary<string, bool> _nodeExpansionState = new(StringComparer.OrdinalIgnoreCase);
        private string? _expansionStateFilePath;

        private readonly object _saveExpansionLock = new();
        private System.Threading.CancellationTokenSource? _saveExpansionCts;
        private bool _isInitializingTree;

        public static readonly RoutedUICommand CreateFileCommand = new("新建文件", nameof(CreateFileCommand), typeof(TreeNavigationList));
        public static readonly RoutedUICommand CreateFolderCommand = new("新建文件夹", nameof(CreateFolderCommand), typeof(TreeNavigationList));
        public static readonly RoutedUICommand RenameCommand = new("重命名", nameof(RenameCommand), typeof(TreeNavigationList));
        public static readonly RoutedUICommand DeleteCommand = new("删除", nameof(DeleteCommand), typeof(TreeNavigationList));
        public static readonly RoutedUICommand RevealInExplorerCommand = new("在资源管理器中打开", nameof(RevealInExplorerCommand), typeof(TreeNavigationList));
        public static readonly RoutedUICommand CopyCommand = new(
            "复制",
            nameof(CopyCommand),
            typeof(TreeNavigationList),
            new InputGestureCollection { new KeyGesture(Key.C, ModifierKeys.Control) });
        public static readonly RoutedUICommand PasteCommand = new(
            "粘贴",
            nameof(PasteCommand),
            typeof(TreeNavigationList),
            new InputGestureCollection { new KeyGesture(Key.V, ModifierKeys.Control) });
        public static readonly RoutedUICommand RefreshCommand = new("刷新", nameof(RefreshCommand), typeof(TreeNavigationList));

        public event EventHandler<FileNodeOpenRequestedEventArgs>? FileNodeOpenRequested;

        public TreeNavigationList()
        {
            InitializeComponent();

            CommandBindings.Add(new CommandBinding(CreateFileCommand, OnCreateFileCommandExecuted, OnCreateFileCommandCanExecute));
            CommandBindings.Add(new CommandBinding(CreateFolderCommand, OnCreateFolderCommandExecuted, OnCreateFolderCommandCanExecute));
            CommandBindings.Add(new CommandBinding(RenameCommand, OnRenameCommandExecuted, OnRenameCommandCanExecute));
            CommandBindings.Add(new CommandBinding(DeleteCommand, OnDeleteCommandExecuted, OnDeleteCommandCanExecute));
            CommandBindings.Add(new CommandBinding(RevealInExplorerCommand, OnRevealCommandExecuted, OnRevealCommandCanExecute));
            CommandBindings.Add(new CommandBinding(CopyCommand, OnCopyCommandExecuted, OnCopyCommandCanExecute));
            CommandBindings.Add(new CommandBinding(PasteCommand, OnPasteCommandExecuted, OnPasteCommandCanExecute));
            CommandBindings.Add(new CommandBinding(RefreshCommand, OnRefreshCommandExecuted));

            _nodeSelectedCommand = new RelayCommand(param =>
            {
                if (param is TreeNodeItem item)
                {
                    _lastContextNode = item;
                    HandleFileNodeSelection(item);
                }
            });
            _fileNodeDoubleClickCommand = new RelayCommand(HandleFileNodeDoubleClick);

            FileTreeView.ItemsSource = _fileTreeItems;
            FileTreeView.ParentClickMode = ParentNodeClickMode.Toggle;
            FileTreeView.ParentNodeClickCommand = _nodeSelectedCommand;
            FileTreeView.ChildNodeClickCommand = _nodeSelectedCommand;
            FileTreeView.NodeDoubleClickCommand = _fileNodeDoubleClickCommand;
            FileTreeView.AddHandler(UIElement.PreviewMouseRightButtonDownEvent, new MouseButtonEventHandler(OnFileTreePreviewMouseRightButtonDown), true);

            InitializeExpansionState();
        }

        #region 文件树管理

        private async Task LoadFileTree(string? pathToSelect = null)
        {
            try
            {
                pathToSelect ??= GetCurrentNodePath();

                string storageRoot = StoragePathHelper.GetStorageRoot();
                string projectsRoot = Path.Combine(storageRoot, "Projects");

                if (!Directory.Exists(projectsRoot))
                {
                    await Task.Run(async () =>
                    {
                        Directory.CreateDirectory(projectsRoot);
                        await CreateDefaultProjectStructureAsync(projectsRoot);
                    });
                    App.Log($"[文件树] 创建默认项目结构: {projectsRoot}");
                }

                _isInitializingTree = true;
                foreach (var oldNode in _nodeIndex.Values)
                    oldNode.PropertyChanged -= OnTreeNodePropertyChanged;
                _nodeIndex.Clear();
                _parentMap.Clear();

                var root = projectsRoot;
                var scanned = await Task.Run(() => ScanFileSystem(root, FileNodeType.Root));
                _rootNode = await BuildTreeNodeBatchedAsync(scanned, 0, null);

                _fileTreeItems.ReplaceAll(_rootNode.Children);

                _lastContextNode = _rootNode;
                _isInitializingTree = false;

                if (!string.IsNullOrWhiteSpace(pathToSelect))
                    ApplySelection(pathToSelect);

                SaveExpansionState(force: true);
                App.Log($"[文件树] 加载完成，共{GetNodeCount(_rootNode)}个节点");
            }
            catch (Exception ex)
            {
                App.Log($"[文件树] 加载失败: {ex.Message}");
                GlobalToast.Error("加载失败", $"加载失败：{ex.Message}");
            }
            finally
            {
                _isInitializingTree = false;
            }
        }

        private void OnTreeNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isInitializingTree || !string.Equals(e.PropertyName, nameof(TreeNodeItem.IsExpanded), StringComparison.Ordinal))
            {
                return;
            }

            if (sender is TreeNodeItem node && node.Tag is FileNodeInfo info && info.Type != FileNodeType.File)
            {
                UpdateExpansionState(info.FullPath, node.IsExpanded, immediateSave: false);
                SaveExpansionState();
            }
        }

        private async Task CreateDefaultProjectStructureAsync(string projectsRoot)
        {
            string defaultProject = Path.Combine(projectsRoot, "默认项目");
            Directory.CreateDirectory(defaultProject);

            Directory.CreateDirectory(Path.Combine(defaultProject, "大纲"));
            Directory.CreateDirectory(Path.Combine(defaultProject, "角色"));
            Directory.CreateDirectory(Path.Combine(defaultProject, "设定"));
            Directory.CreateDirectory(Path.Combine(defaultProject, "素材"));
            Directory.CreateDirectory(Path.Combine(defaultProject, "章节"));

            await File.WriteAllTextAsync(
                Path.Combine(defaultProject, "README.md"),
                "# 默认项目\n\n这是一个默认项目，你可以在这里管理你的创作文件。").ConfigureAwait(false);
        }

        private sealed class FileEntry
        {
            public string Path { get; set; } = "";
            public FileNodeType Type { get; set; }
            public List<FileEntry> Children { get; } = new();
        }

        private static FileEntry ScanFileSystem(string path, FileNodeType type)
        {
            var entry = new FileEntry { Path = path, Type = type };
            if (type == FileNodeType.File) return entry;
            try
            {
                foreach (var dir in Directory.GetDirectories(path))
                    entry.Children.Add(ScanFileSystem(dir, FileNodeType.Folder));
                foreach (var file in Directory.GetFiles(path))
                    entry.Children.Add(new FileEntry { Path = file, Type = FileNodeType.File });
            }
            catch (Exception ex)
            {
                App.Log($"[文件树] 扫描目录失败 {path}: {ex.Message}");
            }
            return entry;
        }

        private TreeNodeItem BuildTreeNode(FileEntry entry, int level, TreeNodeItem? parent)
        {
            var normalizedPath = NormalizePath(entry.Path);
            var name = Path.GetFileName(normalizedPath);
            if (string.IsNullOrEmpty(name))
                name = entry.Type == FileNodeType.Root ? "Projects" : normalizedPath;

            var node = new TreeNodeItem
            {
                Name = name,
                Icon = GetNodeIcon(entry.Type, entry.Path, level),
                Level = level,
                IsExpanded = level <= 1,
                ShowChildCount = entry.Type != FileNodeType.File,
                IsFileSystemNode = true,
                Tag = new FileNodeInfo { FullPath = normalizedPath, Type = entry.Type }
            };

            RegisterNode(node, parent);

            foreach (var child in entry.Children)
                node.Children.Add(BuildTreeNode(child, level + 1, node));

            return node;
        }

        private async Task<TreeNodeItem> BuildTreeNodeBatchedAsync(FileEntry rootEntry, int rootLevel, TreeNodeItem? rootParent)
        {
            var rootNode = CreateSingleTreeNode(rootEntry, rootLevel, rootParent);

            var queue = new Queue<(FileEntry entry, int level, TreeNodeItem parent)>();
            foreach (var child in rootEntry.Children)
                queue.Enqueue((child, rootLevel + 1, rootNode));

            int batchCounter = 0;
            const int BatchSize = 50;

            while (queue.Count > 0)
            {
                var (entry, level, parent) = queue.Dequeue();
                var node = CreateSingleTreeNode(entry, level, parent);
                parent.Children.Add(node);

                foreach (var child in entry.Children)
                    queue.Enqueue((child, level + 1, node));

                if (++batchCounter >= BatchSize)
                {
                    batchCounter = 0;
                    await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                }
            }

            return rootNode;
        }

        private TreeNodeItem CreateSingleTreeNode(FileEntry entry, int level, TreeNodeItem? parent)
        {
            var normalizedPath = NormalizePath(entry.Path);
            var name = Path.GetFileName(normalizedPath);
            if (string.IsNullOrEmpty(name))
                name = entry.Type == FileNodeType.Root ? "Projects" : normalizedPath;

            var node = new TreeNodeItem
            {
                Name = name,
                Icon = GetNodeIcon(entry.Type, entry.Path, level),
                Level = level,
                IsExpanded = level <= 1,
                ShowChildCount = entry.Type != FileNodeType.File,
                IsFileSystemNode = true,
                Tag = new FileNodeInfo { FullPath = normalizedPath, Type = entry.Type }
            };

            RegisterNode(node, parent);
            return node;
        }

        private void HandleFileNodeDoubleClick(object? parameter)
        {
            if (parameter is not TreeNodeItem item)
            {
                return;
            }

            var info = GetNodeInfo(item);
            if (info == null)
            {
                return;
            }

            _lastContextNode = item;

            switch (info.Type)
            {
                case FileNodeType.File:
                    RequestOpenFile(item, info);
                    break;

                case FileNodeType.Folder:
                case FileNodeType.Root:
                    item.IsExpanded = !item.IsExpanded;
                    break;
            }
        }

        private int GetNodeCount(TreeNodeItem node)
        {
            int count = 1;
            foreach (var child in node.Children)
            {
                count += GetNodeCount(child);
            }
            return count;
        }

        #endregion

        #region 右键菜单命令
        private void OnCreateFileCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = TryResolveCommandContext(e.Parameter, requireDirectory: true, allowRoot: true, out _, out _);
        }

        private void OnCreateFileCommandExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (TryResolveCommandContext(e.Parameter, requireDirectory: true, allowRoot: true, out var node, out _))
            {
                ExecuteCreateFile(node);
                e.Handled = true;
            }
        }

        private void OnCreateFolderCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = TryResolveCommandContext(e.Parameter, requireDirectory: true, allowRoot: true, out _, out _);
        }

        private void OnCreateFolderCommandExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (TryResolveCommandContext(e.Parameter, requireDirectory: true, allowRoot: true, out var node, out _))
            {
                ExecuteCreateFolder(node);
                e.Handled = true;
            }
        }

        private void OnRenameCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = TryResolveCommandContext(e.Parameter, requireDirectory: false, allowRoot: false, out _, out _);
        }

        private void OnRenameCommandExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (TryResolveCommandContext(e.Parameter, requireDirectory: false, allowRoot: false, out var node, out _))
            {
                ExecuteRename(node).SafeFireAndForget(ex => TM.App.Log($"[TreeNavigationList] {ex.Message}"));
                e.Handled = true;
            }
        }

        private void OnDeleteCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = TryResolveCommandContext(e.Parameter, requireDirectory: false, allowRoot: false, out _, out _);
        }

        private void OnDeleteCommandExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (TryResolveCommandContext(e.Parameter, requireDirectory: false, allowRoot: false, out var node, out _))
            {
                ExecuteDelete(node).SafeFireAndForget(ex => TM.App.Log($"[TreeNavigationList] {ex.Message}"));
                e.Handled = true;
            }
        }

        private void OnCopyCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = TryResolveCommandContext(e.Parameter, requireDirectory: false, allowRoot: true, out _, out _);
        }

        private void OnCopyCommandExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (TryResolveCommandContext(e.Parameter, requireDirectory: false, allowRoot: true, out var node, out _))
            {
                ExecuteCopy(node);
                e.Handled = true;
            }
        }

        private void OnRevealCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = TryResolveCommandContext(e.Parameter, requireDirectory: false, allowRoot: true, out _, out _);
        }

        private void OnRevealCommandExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (TryResolveCommandContext(e.Parameter, requireDirectory: false, allowRoot: true, out var node, out _))
            {
                ExecuteReveal(node);
                e.Handled = true;
            }
        }

        private void OnPasteCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = ClipboardHasFileDropList() &&
                           TryResolveCommandContext(e.Parameter, requireDirectory: true, allowRoot: true, out _, out _);
        }

        private void OnPasteCommandExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (TryResolveCommandContext(e.Parameter, requireDirectory: true, allowRoot: true, out var node, out var info))
            {
                ExecutePaste(node, info).SafeFireAndForget(ex => TM.App.Log($"[TreeNavigationList] {ex.Message}"));
                e.Handled = true;
            }
        }

        private void OnRefreshCommandExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            LoadFileTree(GetCurrentNodePath()).SafeFireAndForget(ex => TM.App.Log($"[TreeNavigationList] {ex.Message}"));
            App.Log("[文件树] 右键刷新");
            e.Handled = true;
        }

        private void ExecuteCreateFile(TreeNodeItem node)
        {
            try
            {
                var defaultName = $"新建文件_{DateTime.Now:yyyyMMdd_HHmmss}.md";
                var input = StandardDialog.ShowInput("请输入文件名称", "新建文件", defaultName);

                if (string.IsNullOrWhiteSpace(input))
                {
                    return;
                }

                var fileName = NormalizeFileName(input.Trim(), true, Path.GetExtension(defaultName));
                if (!IsValidFileName(fileName))
                {
                    GlobalToast.Warning("名称无效", "文件名包含非法字符");
                    return;
                }

                var info = GetNodeInfo(node);
                if (info == null)
                {
                    return;
                }

                var directory = info.FullPath;
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var targetPath = Path.Combine(directory, fileName);
                if (File.Exists(targetPath))
                {
                    GlobalToast.Warning("创建失败", "同名文件已存在");
                    return;
                }

                var header = Path.GetFileNameWithoutExtension(fileName);
                var content = $"# {header}\n\n";
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    await File.WriteAllTextAsync(targetPath, content).ConfigureAwait(false);
                }).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        GlobalToast.Error("创建失败", t.Exception?.GetBaseException().Message ?? "未知错误");
                        return;
                    }

                    GlobalToast.Success("创建成功", $"文件已创建: {fileName}");
                    App.Log($"[文件树] 右键新建文件: {targetPath}");
                    LoadFileTree(targetPath).SafeFireAndForget(ex => TM.App.Log($"[TreeNavigationList] {ex.Message}"));
                }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
            }
            catch (Exception ex)
            {
                App.Log($"[文件树] 新建文件失败: {ex.Message}");
                GlobalToast.Error("创建失败", $"创建失败：{ex.Message}");
            }
        }

        private void OnContextMenuOpened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu)
            {
                return;
            }

            if (_lastContextNode == null && _rootNode != null)
            {
                _lastContextNode = _rootNode;
            }

            menu.DataContext = _lastContextNode;
            App.Log($"[文件树] ContextMenu打开 DataContext={DescribeCandidate(menu.DataContext)}");

            if (menu.DataContext is TreeNodeItem node && node.Tag is FileNodeInfo info)
            {
                menu.IsEnabled = true;
                UpdateMenuCommandParameters(menu, node, info);
            }
            else
            {
                menu.IsEnabled = false;
            }

            System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                System.Windows.Input.CommandManager.InvalidateRequerySuggested,
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private void UpdateMenuCommandParameters(ContextMenu menu, TreeNodeItem node, FileNodeInfo info)
        {
            foreach (var item in menu.Items)
            {
                if (item is MenuItem menuItem)
                {
                    menuItem.CommandParameter = node;

                    if (Equals(menuItem.Command, PasteCommand) && info.Type == FileNodeType.File)
                    {
                        if (_parentMap.TryGetValue(node, out var parent) && parent != null)
                        {
                            menuItem.CommandParameter = parent;
                        }
                    }
                }
            }
        }

        private void ExecuteCreateFolder(TreeNodeItem node)
        {
            try
            {
                var defaultName = $"新建文件夹_{DateTime.Now:yyyyMMdd_HHmmss}";
                var input = StandardDialog.ShowInput("请输入文件夹名称", "新建文件夹", defaultName);

                if (string.IsNullOrWhiteSpace(input))
                {
                    return;
                }

                var folderName = NormalizeFileName(input.Trim(), false, string.Empty);
                if (!IsValidFileName(folderName))
                {
                    GlobalToast.Warning("名称无效", "文件夹名称包含非法字符");
                    return;
                }

                var info = GetNodeInfo(node);
                if (info == null)
                {
                    return;
                }

                var targetDirectory = Path.Combine(info.FullPath, folderName);
                if (Directory.Exists(targetDirectory))
                {
                    GlobalToast.Warning("创建失败", "同名文件夹已存在");
                    return;
                }

                _ = Task.Run(() =>
                {
                    Directory.CreateDirectory(targetDirectory);
                }).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        GlobalToast.Error("创建失败", t.Exception?.GetBaseException().Message ?? "未知错误");
                        return;
                    }

                    GlobalToast.Success("创建成功", $"文件夹已创建: {folderName}");
                    App.Log($"[文件树] 右键新建文件夹: {targetDirectory}");
                    LoadFileTree(targetDirectory).SafeFireAndForget(ex => TM.App.Log($"[TreeNavigationList] {ex.Message}"));
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            catch (Exception ex)
            {
                App.Log($"[文件树] 新建文件夹失败: {ex.Message}");
                GlobalToast.Error("创建失败", $"创建失败：{ex.Message}");
            }
        }

        #endregion
    }
}

