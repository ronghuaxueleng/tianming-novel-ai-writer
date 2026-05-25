using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using TM.Framework.Common.Controls;

namespace TM.Framework.UI.Components
{
    public partial class TreeNavigationList
    {
        private async Task ExecuteRename(TreeNodeItem node)
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
                LoadFileTree(targetPath).SafeFireAndForget(ex => TM.App.Log($"[TreeNavigationList] {ex.Message}"));
            }
            catch (Exception ex)
            {
                App.Log($"[文件树] 重命名失败: {ex.Message}");
                GlobalToast.Error("重命名失败", $"重命名失败：{ex.Message}");
            }
        }

        private async Task ExecuteDelete(TreeNodeItem node)
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
                LoadFileTree(parentDirectory).SafeFireAndForget(ex => TM.App.Log($"[TreeNavigationList] {ex.Message}"));
            }
            catch (Exception ex)
            {
                App.Log($"[文件树] 删除失败: {ex.Message}");
                GlobalToast.Error("删除失败", $"删除失败：{ex.Message}");
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
                GlobalToast.Error("复制失败", $"复制失败：{ex.Message}");
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
                GlobalToast.Error("打开失败", $"打开失败：{ex.Message}");
            }
        }

        private async Task ExecutePaste(TreeNodeItem node, FileNodeInfo targetInfo)
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

                var sources = Clipboard.GetFileDropList().Cast<string>().ToList();

                var pastedCount = 0;
                bool hasError = false;
                string? lastErrorMessage = null;
                string? lastCreatedPath = null;

                await Task.Run(async () =>
                {
                    Directory.CreateDirectory(targetDirectory);
                    foreach (var source in sources)
                    {
                        try
                        {
                            if (File.Exists(source))
                            {
                                var destination = EnsureUniquePath(Path.Combine(targetDirectory, Path.GetFileName(source)), false);
                                await using var src = File.OpenRead(source);
                                await using var dst = File.Create(destination);
                                await src.CopyToAsync(dst).ConfigureAwait(false);
                                lastCreatedPath = destination;
                                pastedCount++;
                            }
                            else if (Directory.Exists(source))
                            {
                                var destinationFolder = EnsureUniquePath(Path.Combine(targetDirectory, Path.GetFileName(source)), true);
                                await CopyDirectoryRecursiveAsync(source, destinationFolder).ConfigureAwait(false);
                                lastCreatedPath = destinationFolder;
                                pastedCount++;
                            }
                        }
                        catch (Exception innerEx)
                        {
                            hasError = true;
                            lastErrorMessage = innerEx.Message;
                            App.Log($"[文件树] 粘贴单项失败: {innerEx.Message}");
                        }
                    }
                });

                if (pastedCount > 0)
                {
                    if (hasError)
                    {
                        GlobalToast.Warning("粘贴完成", $"已粘贴 {pastedCount} 个项目，部分失败：{lastErrorMessage ?? "未知原因"}");
                    }
                    else
                    {
                        GlobalToast.Success("粘贴成功", $"已粘贴 {pastedCount} 个项目");
                    }

                    LoadFileTree(lastCreatedPath ?? targetDirectory).SafeFireAndForget(ex => TM.App.Log($"[TreeNavigationList] {ex.Message}"));
                }
                else
                {
                    GlobalToast.Warning("粘贴失败", "没有成功粘贴的项目");
                }
            }
            catch (Exception ex)
            {
                App.Log($"[文件树] 粘贴失败: {ex.Message}");
                GlobalToast.Error("粘贴失败", $"粘贴失败：{ex.Message}");
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

        private class FileNodeInfo
        {
            public string FullPath { get; set; } = string.Empty;
            public FileNodeType Type { get; set; }
        }

        private static ImageSource? GetNodeIcon(FileNodeType type, string path, int level)
        {
            return type switch
            {
                FileNodeType.Root => IconHelper.TryGet("Icon.FolderOpen"),
                FileNodeType.Folder => level == 1 ? IconHelper.TryGet("Icon.FolderOpen") : IconHelper.TryGet("Icon.Folder"),
                FileNodeType.File => GetFileIconByExtension(path),
                _ => IconHelper.TryGet("Icon.Document")
            };
        }

        private static ImageSource? GetFileIconByExtension(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".md" => IconHelper.TryGet("Icon.Document"),
                ".txt" => IconHelper.TryGet("Icon.Document"),
                ".json" => IconHelper.TryGet("Icon.Code"),
                ".xml" => IconHelper.TryGet("Icon.Code"),
                ".png" or ".jpg" or ".jpeg" or ".gif" => IconHelper.TryGet("Icon.Image"),
                _ => IconHelper.TryGet("Icon.Document")
            };
        }

        private static FileNodeInfo? GetNodeInfo(TreeNodeItem node) => node.Tag as FileNodeInfo;

        private void InitializeExpansionState()
        {
            EnsureExpansionStateFile();
            var filePath = _expansionStateFilePath;
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    if (!File.Exists(filePath)) return (Dictionary<string, bool>?)null;
                    var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
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
                LoadFileTree().SafeFireAndForget(ex => TM.App.Log($"[TreeNavigationList] {ex.Message}"));
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

                System.Threading.CancellationToken token;
                lock (_saveExpansionLock)
                {
                    _saveExpansionCts?.Cancel();
                    _saveExpansionCts = new System.Threading.CancellationTokenSource();
                    token = _saveExpansionCts.Token;
                }

                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        await System.Threading.Tasks.Task.Delay(200, token).ConfigureAwait(false);
                        var tmp = dest + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        await using (var stream = File.Create(tmp))
                        {
                            await JsonSerializer.SerializeAsync(stream, snapshot, JsonHelper.Default).ConfigureAwait(false);
                        }
                        File.Move(tmp, dest, overwrite: true);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        App.Log($"[文件树] 保存展开状态失败: {ex.Message}");
                    }
                });
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

        private async Task CopyDirectoryRecursiveAsync(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            foreach (var file in Directory.GetFiles(source))
            {
                var destFile = EnsureUniquePath(Path.Combine(destination, Path.GetFileName(file)), false);
                await using var src = File.OpenRead(file);
                await using var dst = File.Create(destFile);
                await src.CopyToAsync(dst).ConfigureAwait(false);
            }

            foreach (var dir in Directory.GetDirectories(source))
            {
                var destDir = EnsureUniquePath(Path.Combine(destination, Path.GetFileName(dir)), true);
                await CopyDirectoryRecursiveAsync(dir, destDir).ConfigureAwait(false);
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
                Icon = string.Empty
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

