using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Emoji.Wpf;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Helpers;

namespace TM.Framework.UI.Components
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class TreeNavigationList : UserControl
    {
        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();
        private readonly ObservableCollection<TreeNodeItem> _fileTreeItems = new();
        private TreeNodeItem? _rootNode;
        private TreeNodeItem? _lastContextNode;
        private readonly ICommand _nodeSelectedCommand;
        private readonly ICommand _fileNodeDoubleClickCommand;
        private readonly Dictionary<string, TreeNodeItem> _nodeIndex = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<TreeNodeItem, TreeNodeItem?> _parentMap = new();
        private readonly Dictionary<string, bool> _nodeExpansionState = new(StringComparer.OrdinalIgnoreCase);
        private string? _expansionStateFilePath;

        private readonly object _saveExpansionLock = new();
        private int _saveExpansionVersion;
        private System.Threading.Tasks.Task _saveExpansionTask = System.Threading.Tasks.Task.CompletedTask;
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

        private async void LoadFileTree(string? pathToSelect = null)
        {
            try
            {
                pathToSelect ??= GetCurrentNodePath();

                string storageRoot = StoragePathHelper.GetStorageRoot();
                string projectsRoot = Path.Combine(storageRoot, "Projects");

                if (!Directory.Exists(projectsRoot))
                {
                    await Task.Run(() =>
                    {
                        Directory.CreateDirectory(projectsRoot);
                        CreateDefaultProjectStructure(projectsRoot);
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
                _rootNode = BuildTreeNode(scanned, 0, null);

                _fileTreeItems.Clear();
                foreach (var child in _rootNode.Children)
                    _fileTreeItems.Add(child);

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
                GlobalToast.Error("加载失败", ex.Message);
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

        private void CreateDefaultProjectStructure(string projectsRoot)
        {
            string defaultProject = Path.Combine(projectsRoot, "默认项目");
            Directory.CreateDirectory(defaultProject);

            Directory.CreateDirectory(Path.Combine(defaultProject, "大纲"));
            Directory.CreateDirectory(Path.Combine(defaultProject, "角色"));
            Directory.CreateDirectory(Path.Combine(defaultProject, "设定"));
            Directory.CreateDirectory(Path.Combine(defaultProject, "素材"));
            Directory.CreateDirectory(Path.Combine(defaultProject, "章节"));

            File.WriteAllText(
                Path.Combine(defaultProject, "README.md"),
                "# 默认项目\n\n这是一个默认项目，你可以在这里管理你的创作文件。");
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

        private TreeNodeItem CreateTreeNode(string path, FileNodeType type, int level, TreeNodeItem? parent = null)
        {
            var scanned = ScanFileSystem(path, type);
            return BuildTreeNode(scanned, level, parent);
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

        private void OnNewFile(object sender, RoutedEventArgs e)
        {
            try
            {
                string fileName = $"新建文件_{DateTime.Now:yyyyMMdd_HHmmss}.md";
                string storageRoot = StoragePathHelper.GetStorageRoot();
                string projectsRoot = Path.Combine(storageRoot, "Projects", "默认项目");
                string filePath = Path.Combine(projectsRoot, fileName);

                var content = $"# {fileName}\n\n";
                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    File.WriteAllText(filePath, content);
                }).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        GlobalToast.Error("创建失败", t.Exception?.GetBaseException().Message ?? "未知错误");
                        return;
                    }

                    LoadFileTree(filePath);
                    GlobalToast.Success("创建成功", $"文件已创建: {fileName}");
                    App.Log($"[文件树] 创建文件: {filePath}");
                }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
            }
            catch (Exception ex)
            {
                App.Log($"[文件树] 创建文件失败: {ex.Message}");
                GlobalToast.Error("创建失败", ex.Message);
            }
        }

        private void OnNewFolder(object sender, RoutedEventArgs e)
        {
            try
            {
                string folderName = $"新建文件夹_{DateTime.Now:yyyyMMdd_HHmmss}";
                string storageRoot = StoragePathHelper.GetStorageRoot();
                string projectsRoot = Path.Combine(storageRoot, "Projects", "默认项目");
                string folderPath = Path.Combine(projectsRoot, folderName);

                _ = Task.Run(() =>
                {
                    Directory.CreateDirectory(folderPath);
                }).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        GlobalToast.Error("创建失败", t.Exception?.GetBaseException().Message ?? "未知错误");
                        return;
                    }

                    LoadFileTree(folderPath);
                    GlobalToast.Success("创建成功", $"文件夹已创建: {folderName}");
                    App.Log($"[文件树] 创建文件夹: {folderPath}");
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            catch (Exception ex)
            {
                App.Log($"[文件树] 创建文件夹失败: {ex.Message}");
                GlobalToast.Error("创建失败", ex.Message);
            }
        }

        private void OnRefreshFileTree(object sender, RoutedEventArgs e)
        {
            LoadFileTree(GetCurrentNodePath());
            App.Log("[文件树] 手动刷新");
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
                ExecuteRename(node);
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
                ExecuteDelete(node);
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
                ExecutePaste(node, info);
                e.Handled = true;
            }
        }

        private void OnRefreshCommandExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            LoadFileTree(GetCurrentNodePath());
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
                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    File.WriteAllText(targetPath, content);
                }).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        GlobalToast.Error("创建失败", t.Exception?.GetBaseException().Message ?? "未知错误");
                        return;
                    }

                    GlobalToast.Success("创建成功", $"文件已创建: {fileName}");
                    App.Log($"[文件树] 右键新建文件: {targetPath}");
                    LoadFileTree(targetPath);
                }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
            }
            catch (Exception ex)
            {
                App.Log($"[文件树] 新建文件失败: {ex.Message}");
                GlobalToast.Error("创建失败", ex.Message);
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

            CommandManager.InvalidateRequerySuggested();
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
                    LoadFileTree(targetDirectory);
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            catch (Exception ex)
            {
                App.Log($"[文件树] 新建文件夹失败: {ex.Message}");
                GlobalToast.Error("创建失败", ex.Message);
            }
        }

        private async void ExecuteRename(TreeNodeItem node)
        {
            try
            {
                var info = GetNodeInfo(node);
                if (info == null)
                {
                    return;
                }

                var parentDirectory = Path.GetDirectoryName(info.FullPath);
                if (string.IsNullOrEmpty(parentDirectory))
                {
                    GlobalToast.Warning("重命名失败", "无法获取上级目录");
                    return;
                }

                var originalExtension = info.Type == FileNodeType.File ? Path.GetExtension(info.FullPath) : string.Empty;
                var input = StandardDialog.ShowInput("请输入新名称", "重命名", node.Name);

                if (string.IsNullOrWhiteSpace(input))
                {
                    return;
                }

                var newNameRaw = input.Trim();
                var newName = info.Type == FileNodeType.File
                    ? NormalizeFileName(newNameRaw, true, originalExtension)
                    : NormalizeFileName(newNameRaw, false, string.Empty);

                if (!IsValidFileName(newName))
                {
                    GlobalToast.Warning("重命名失败", "名称包含非法字符");
                    return;
                }

                if (string.Equals(node.Name, newName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var targetPath = Path.Combine(parentDirectory, newName);

                if (info.Type == FileNodeType.File && File.Exists(targetPath))
                {
                    GlobalToast.Warning("重命名失败", "已存在同名文件");
                    return;
                }

                if (info.Type == FileNodeType.Folder && Directory.Exists(targetPath))
                {
                    GlobalToast.Warning("重命名失败", "已存在同名文件夹");
                    return;
                }

                var srcPath = info.FullPath;
                var isFile = info.Type == FileNodeType.File;
                await Task.Run(() =>
                {
                    if (isFile)
                        File.Move(srcPath, targetPath);
                    else
                        Directory.Move(srcPath, targetPath);
                });

                GlobalToast.Success("重命名成功", $"已重命名为: {newName}");
                App.Log($"[文件树] 右键重命名: {srcPath} -> {targetPath}");
                LoadFileTree(targetPath);
            }
            catch (Exception ex)
            {
                App.Log($"[文件树] 重命名失败: {ex.Message}");
                GlobalToast.Error("重命名失败", ex.Message);
            }
        }

        private async void ExecuteDelete(TreeNodeItem node)
        {
            try
            {
                var info = GetNodeInfo(node);
                if (info == null)
                {
                    return;
                }

                var confirm = StandardDialog.ShowConfirm($"确定要删除\"{node.Name}\"吗？此操作不可撤销。", "删除确认");
                if (!confirm)
                {
                    return;
                }

                var parentDirectory = Path.GetDirectoryName(info.FullPath);

                var delPath = info.FullPath;
                var isFile = info.Type == FileNodeType.File;
                await Task.Run(() =>
                {
                    if (isFile)
                        File.Delete(delPath);
                    else
                        Directory.Delete(delPath, true);
                });

                GlobalToast.Success("删除成功", $"已删除: {node.Name}");
                App.Log($"[文件树] 右键删除: {delPath}");
                LoadFileTree(parentDirectory);
            }
            catch (Exception ex)
            {
                App.Log($"[文件树] 删除失败: {ex.Message}");
                GlobalToast.Error("删除失败", ex.Message);
            }
        }

        private void ExecuteCopy(TreeNodeItem node)
        {
            try
            {
                var info = GetNodeInfo(node);
                if (info == null)
                {
                    return;
                }

                var files = new StringCollection { info.FullPath };
                Clipboard.SetFileDropList(files);

                GlobalToast.Success("复制成功", info.Type == FileNodeType.File
                    ? $"已复制文件: {node.Name}"
                    : $"已复制文件夹: {node.Name}");

                App.Log($"[文件树] 复制: {info.FullPath}");
            }
            catch (Exception ex)
            {
                App.Log($"[文件树] 复制失败: {ex.Message}");
                GlobalToast.Error("复制失败", ex.Message);
            }
        }

        private void ExecuteReveal(TreeNodeItem node)
        {
            try
            {
                var info = GetNodeInfo(node);
                if (info == null)
                {
                    return;
                }

                var target = info.FullPath;
                var psi = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = true,
                    Arguments = info.Type == FileNodeType.File
                        ? $"/select,\"{target}\""
                        : $"\"{target}\""
                };

                Process.Start(psi);
                App.Log($"[文件树] 资源管理器打开: {target}");
            }
            catch (Exception ex)
            {
                App.Log($"[文件树] 打开资源管理器失败: {ex.Message}");
                GlobalToast.Error("打开失败", ex.Message);
            }
        }

        private async void ExecutePaste(TreeNodeItem node, FileNodeInfo targetInfo)
        {
            try
            {
                var targetDirectory = targetInfo.Type == FileNodeType.File
                    ? Path.GetDirectoryName(targetInfo.FullPath)
                    : targetInfo.FullPath;

                if (string.IsNullOrWhiteSpace(targetDirectory))
                {
                    GlobalToast.Warning("粘贴失败", "无法确定目标目录");
                    return;
                }

                var sources = GetClipboardFilePaths();
                if (sources.Count == 0)
                {
                    GlobalToast.Warning("粘贴失败", "剪贴板中没有可粘贴的文件或文件夹");
                    return;
                }

                var pastedCount = 0;
                bool hasError = false;
                string? lastCreatedPath = null;

                await Task.Run(() =>
                {
                    Directory.CreateDirectory(targetDirectory);
                    foreach (var source in sources)
                    {
                        try
                        {
                            if (File.Exists(source))
                            {
                                var destination = EnsureUniquePath(Path.Combine(targetDirectory, Path.GetFileName(source)), false);
                                File.Copy(source, destination, overwrite: false);
                                lastCreatedPath = destination;
                                pastedCount++;
                            }
                            else if (Directory.Exists(source))
                            {
                                var destinationFolder = EnsureUniquePath(Path.Combine(targetDirectory, Path.GetFileName(source)), true);
                                CopyDirectoryRecursive(source, destinationFolder);
                                lastCreatedPath = destinationFolder;
                                pastedCount++;
                            }
                        }
                        catch (Exception innerEx)
                        {
                            hasError = true;
                            App.Log($"[文件树] 粘贴单项失败: {innerEx.Message}");
                        }
                    }
                });

                if (pastedCount > 0)
                {
                    if (hasError)
                    {
                        GlobalToast.Warning("粘贴完成", $"已粘贴 {pastedCount} 个项目，部分失败请查看日志");
                    }
                    else
                    {
                        GlobalToast.Success("粘贴成功", $"已粘贴 {pastedCount} 个项目");
                    }

                    LoadFileTree(lastCreatedPath ?? targetDirectory);
                }
                else
                {
                    GlobalToast.Warning("粘贴失败", "没有成功粘贴的项目");
                }
            }
            catch (Exception ex)
            {
                App.Log($"[文件树] 粘贴失败: {ex.Message}");
                GlobalToast.Error("粘贴失败", ex.Message);
            }
        }

        private static bool IsValidFileName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && name.IndexOfAny(InvalidFileNameChars) < 0;
        }

        private static string NormalizeFileName(string name, bool ensureExtension, string defaultExtension)
        {
            if (ensureExtension)
            {
                var currentExt = Path.GetExtension(name);
                if (string.IsNullOrWhiteSpace(currentExt))
                {
                    if (!string.IsNullOrWhiteSpace(defaultExtension))
                    {
                        return name + defaultExtension;
                    }

                    return name + ".md";
                }
            }

            return name;
        }
        #endregion

        private class FileNodeInfo
        {
            public string FullPath { get; set; } = string.Empty;
            public FileNodeType Type { get; set; }
        }

        private static string GetNodeIcon(FileNodeType type, string path, int level)
        {
            return type switch
            {
                FileNodeType.Root => "",
                FileNodeType.Folder => level == 1 ? "" : "",
                FileNodeType.File => GetFileIconByExtension(path),
                _ => ""
            };
        }

        private static string GetFileIconByExtension(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".md" => "",
                ".txt" => "",
                ".json" => "",
                ".xml" => "",
                ".png" or ".jpg" or ".jpeg" or ".gif" => "",
                _ => ""
            };
        }

        private static bool TryGetNodeInfo(object? parameter, out TreeNodeItem node, out FileNodeInfo info)
        {
            node = default!;
            info = default!;

            if (parameter is TreeNodeItem item && item.Tag is FileNodeInfo meta)
            {
                node = item;
                info = meta;
                return true;
            }

            return false;
        }

        private static FileNodeInfo? GetNodeInfo(TreeNodeItem node) => node.Tag as FileNodeInfo;

        private void InitializeExpansionState()
        {
            EnsureExpansionStateFile();
            var filePath = _expansionStateFilePath;
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(filePath)) return (Dictionary<string, bool>?)null;
                    var json = File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
                }
                catch (Exception ex)
                {
                    App.Log($"[文件树] 读取展开状态失败: {ex.Message}");
                    return (Dictionary<string, bool>?)null;
                }
            }).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && t.Result != null)
                {
                    _nodeExpansionState.Clear();
                    foreach (var kv in t.Result)
                        _nodeExpansionState[kv.Key] = kv.Value;
                }
                LoadFileTree();
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void EnsureExpansionStateFile()
        {
            if (!string.IsNullOrEmpty(_expansionStateFilePath))
            {
                return;
            }

            _expansionStateFilePath = StoragePathHelper.GetFilePath(
                "Framework",
                "UI/Components/TreeNavigationList",
                "file_tree_state.json");
        }

        private void RegisterNode(TreeNodeItem node, TreeNodeItem? parent)
        {
            _parentMap[node] = parent;
            node.PropertyChanged += OnTreeNodePropertyChanged;

            if (node.Tag is not FileNodeInfo info)
                return;

            var normalized = NormalizePath(info.FullPath);
            _nodeIndex[normalized] = node;

            if (info.Type != FileNodeType.File)
            {
                if (_nodeExpansionState.TryGetValue(normalized, out var expanded))
                {
                    node.IsExpanded = expanded;
                }
                else
                {
                    _nodeExpansionState[normalized] = node.IsExpanded;
                }
            }
        }

        private void SaveExpansionState(bool force = false)
        {
            if (_isInitializingTree && !force) return;

            EnsureExpansionStateFile();
            if (string.IsNullOrEmpty(_expansionStateFilePath)) return;

            try
            {
                var dest = _expansionStateFilePath;

                var snapshot = new Dictionary<string, bool>(_nodeExpansionState, _nodeExpansionState.Comparer);
                lock (_saveExpansionLock)
                {
                    var version = ++_saveExpansionVersion;
                    _saveExpansionTask = _saveExpansionTask.ContinueWith(async _ =>
                    {
                        try
                        {
                            await Task.Delay(200).ConfigureAwait(false);
                            bool shouldWrite;
                            lock (_saveExpansionLock)
                            {
                                shouldWrite = (version == _saveExpansionVersion);
                            }
                            if (!shouldWrite) return;

                            var tmp = dest + "." + Guid.NewGuid().ToString("N") + ".tmp";
                            var json = JsonSerializer.Serialize(snapshot, JsonHelper.Default);
                            await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
                            File.Move(tmp, dest, overwrite: true);
                        }
                        catch (Exception ex)
                        {
                            App.Log($"[文件树] 保存展开状态失败: {ex.Message}");
                        }
                    }, TaskScheduler.Default).Unwrap();
                }
            }
            catch (Exception ex)
            {
                App.Log($"[文件树] 序列化展开状态失败: {ex.Message}");
            }
        }

        private void UpdateExpansionState(string path, bool isExpanded, bool immediateSave = true)
        {
            var normalized = NormalizePath(path);
            _nodeExpansionState[normalized] = isExpanded;
            if (immediateSave)
                SaveExpansionState();
        }

        private void ApplySelection(string targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return;
            }

            var normalized = NormalizePath(targetPath);
            if (!_nodeIndex.TryGetValue(normalized, out var node))
            {
                var directory = Path.GetDirectoryName(normalized);
                if (string.IsNullOrWhiteSpace(directory) || !_nodeIndex.TryGetValue(directory, out node))
                {
                    return;
                }
            }

            ExpandAncestors(node);
            ClearAllSelections();

            var current = node;
            while (current != null)
            {
                current.IsSelected = true;
                if (!_parentMap.TryGetValue(current, out current))
                {
                    break;
                }
            }

            _lastContextNode = node;
        }

        private void ExpandAncestors(TreeNodeItem node)
        {
            var current = node;
            while (current != null)
            {
                if (current.Tag is FileNodeInfo info && info.Type != FileNodeType.File)
                {
                    if (!current.IsExpanded)
                    {
                        current.IsExpanded = true;
                    }
                    UpdateExpansionState(info.FullPath, current.IsExpanded, immediateSave: false);
                }

                if (!_parentMap.TryGetValue(current, out var parent) || parent == null)
                {
                    break;
                }

                current = parent;
            }

            SaveExpansionState();
        }

        private void ClearAllSelections()
        {
            if (_rootNode == null)
            {
                return;
            }

            ClearSelectionRecursive(_rootNode);
        }

        private void ClearSelectionRecursive(TreeNodeItem node)
        {
            if (node.IsSelected)
            {
                node.IsSelected = false;
            }
            foreach (var child in node.Children)
            {
                ClearSelectionRecursive(child);
            }
        }

        private string? GetCurrentNodePath()
        {
            if (_lastContextNode != null)
            {
                var info = GetNodeInfo(_lastContextNode);
                if (info != null)
                {
                    return info.FullPath;
                }
            }

            if (_rootNode != null)
            {
                var rootInfo = GetNodeInfo(_rootNode);
                return rootInfo?.FullPath;
            }

            return null;
        }

        private string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            var fullPath = Path.GetFullPath(path);

            var root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root) && string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath;
            }

            return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private bool ClipboardHasFileDropList()
        {
            try
            {
                return Clipboard.ContainsFileDropList();
            }
            catch (Exception ex)
            {
                App.Log($"[文件树] 检查剪贴板失败: {ex.Message}");
                return false;
            }
        }

        private List<string> GetClipboardFilePaths()
        {
            var paths = new List<string>();

            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    var items = Clipboard.GetFileDropList();
                    foreach (string? entry in items)
                    {
                        if (string.IsNullOrWhiteSpace(entry))
                        {
                            continue;
                        }

                        var normalized = NormalizePath(entry);
                        if (File.Exists(normalized) || Directory.Exists(normalized))
                        {
                            paths.Add(normalized);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log($"[文件树] 读取剪贴板失败: {ex.Message}");
            }

            return paths;
        }

        private void CopyDirectoryRecursive(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            foreach (var file in Directory.GetFiles(source))
            {
                var destFile = EnsureUniquePath(Path.Combine(destination, Path.GetFileName(file)), false);
                File.Copy(file, destFile, overwrite: false);
            }

            foreach (var dir in Directory.GetDirectories(source))
            {
                var destDir = EnsureUniquePath(Path.Combine(destination, Path.GetFileName(dir)), true);
                CopyDirectoryRecursive(dir, destDir);
            }
        }

        private string EnsureUniquePath(string destinationPath, bool isDirectory)
        {
            var directory = Path.GetDirectoryName(destinationPath);
            var originalName = Path.GetFileName(destinationPath);

            string baseName;
            string extension;

            if (isDirectory)
            {
                baseName = originalName;
                extension = string.Empty;
            }
            else
            {
                baseName = Path.GetFileNameWithoutExtension(destinationPath);
                extension = Path.GetExtension(destinationPath);
            }

            var candidate = destinationPath;
            var index = 1;

            while ((isDirectory && Directory.Exists(candidate)) || (!isDirectory && File.Exists(candidate)))
            {
                var newName = string.IsNullOrEmpty(baseName) ? originalName : $"{baseName} ({index})";
                candidate = Path.Combine(directory ?? string.Empty, isDirectory ? newName : newName + extension);
                index++;
            }

            return candidate;
        }

        private bool TryResolveCommandContext(object? parameter, bool requireDirectory, bool allowRoot, out TreeNodeItem node, out FileNodeInfo info)
        {
            if (TryResolveCommandCandidate(parameter, requireDirectory, allowRoot, out node, out info))
            {
                _lastContextNode = node;
                return true;
            }

            if (_lastContextNode != null && TryResolveCommandCandidate(_lastContextNode, requireDirectory, allowRoot, out node, out info))
            {
                _lastContextNode = node;
                return true;
            }

            if (_rootNode != null && TryResolveCommandCandidate(_rootNode, requireDirectory, allowRoot, out node, out info))
            {
                _lastContextNode = node;
                return true;
            }

            App.Log($"[文件树] 命令上下文解析失败: parameter={DescribeCandidate(parameter)}, requireDirectory={requireDirectory}, allowRoot={allowRoot}, lastContext={DescribeCandidate(_lastContextNode)}, root={DescribeCandidate(_rootNode)}");

            node = default!;
            info = default!;
            return false;
        }

        private bool TryResolveCommandCandidate(object? candidate, bool requireDirectory, bool allowRoot, out TreeNodeItem node, out FileNodeInfo info)
        {
            if (candidate is TreeNodeItem item && item.Tag is FileNodeInfo meta)
            {
                if (!allowRoot && meta.Type == FileNodeType.Root)
                {
                    node = default!;
                    info = default!;
                    return false;
                }

                if (requireDirectory && meta.Type == FileNodeType.File)
                {
                    if (_parentMap.TryGetValue(item, out var parent) && parent != null && parent.Tag is FileNodeInfo parentInfo)
                    {
                        node = parent;
                        info = parentInfo;
                        return true;
                    }

                    node = default!;
                    info = default!;
                    return false;
                }

                node = item;
                info = meta;
                return true;
            }

            node = default!;
            info = default!;
            return false;
        }

        private string DescribeCandidate(object? candidate)
        {
            if (candidate is TreeNodeItem item && item.Tag is FileNodeInfo info)
            {
                return $"{item.Name}({info.Type})";
            }

            return candidate == null ? "null" : candidate.GetType().Name;
        }

        private void HandleFileNodeSelection(TreeNodeItem item)
        {
            var info = GetNodeInfo(item);
            if (info == null)
            {
                return;
            }

            if (info.Type == FileNodeType.File)
            {
                RequestOpenFile(item, info);
            }
        }

        private void RequestOpenFile(TreeNodeItem item, FileNodeInfo info)
        {
            FileNodeOpenRequested?.Invoke(this, new FileNodeOpenRequestedEventArgs
            {
                FileName = item.Name,
                FullPath = info.FullPath,
                Icon = item.Icon
            });
        }

        private void OnFileTreePreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                var node = FindDataContext<TreeNodeItem>(source);
                _lastContextNode = node ?? _rootNode;
                App.Log($"[文件树] 右键命中: {DescribeCandidate(node)} -> LastContext={DescribeCandidate(_lastContextNode)}");
            }

            FileTreeView.Focus();
        }

        private static T? FindDataContext<T>(DependencyObject? source) where T : class
        {
            while (source != null)
            {
                if (source is FrameworkElement fe && fe.DataContext is T t1)
                {
                    return t1;
                }

                if (source is FrameworkContentElement fce && fce.DataContext is T t2)
                {
                    return t2;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class FileNodeOpenRequestedEventArgs : EventArgs
    {
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
    }

    public enum FileNodeType
    {
        Root,
        Folder,
        File
    }
}
