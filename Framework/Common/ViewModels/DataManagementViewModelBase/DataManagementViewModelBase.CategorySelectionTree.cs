using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Models;

namespace TM.Framework.Common.ViewModels
{
    public abstract partial class DataManagementViewModelBase<TData, TCategory, TService>
    {
        private void RebuildCategorySelectionTree()
        {
            void Rebuild()
            {
                _categoryNodeIndex.Clear();
                _categorySelectionParentMap.Clear();
                _categorySelectionPathCache.Clear();
                _categoryLookup = new Dictionary<string, TCategory>(StringComparer.Ordinal);

                List<TCategory> categories;
                try
                {
                    categories = GetAllCategoriesFromService() ?? new List<TCategory>();
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[{GetType().Name}] 构建分类下拉树失败: {ex.Message}");
                    categories = new List<TCategory>();
                }

                var validCategories = categories.Count > 0 ? FilterCategories(categories) : new List<TCategory>();

                if (validCategories.Count > 0)
                {
                    _categoryLookup = validCategories
                        .GroupBy(c => c.Name, StringComparer.Ordinal)
                        .Select(g => g.First())
                        .ToDictionary(c => c.Name, c => c, StringComparer.Ordinal);
                }

                var topLevel = validCategories
                    .Where(c => string.IsNullOrWhiteSpace(c.ParentCategory))
                    .OrderBy(c => c.Order)
                    .ThenBy(c => c.Name, StringComparer.Ordinal)
                    .ToList();

                var newTree = new List<TreeNodeItem>();

                var homepageNode = new TreeNodeItem
                {
                    Name = "主页导航",
                    Icon = IconHelper.TryGet("Icon.Home"),
                    Level = 0,
                    Tag = null,
                    ShowChildCount = false
                };
                _categorySelectionParentMap[homepageNode] = null;
                _categorySelectionPathCache[homepageNode] = homepageNode.Name;

                foreach (var category in topLevel)
                {
                    var mirrorNode = CreateCategorySelectionNode(category, validCategories, 1, homepageNode, homepageNode.Name);
                    homepageNode.Children.Add(mirrorNode);
                }

                newTree.Add(homepageNode);

                CategorySelectionTree.ReplaceAll(newTree);

                UpdateCategorySelectionDisplayCore(GetCurrentCategoryValue());
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(Rebuild);
            }
            else
            {
                Rebuild();
            }
        }

        private static List<TCategory> FilterCategories(List<TCategory> categories)
        {
            var unique = categories
                .GroupBy(c => c.Name, StringComparer.Ordinal)
                .Select(g => g.First())
                .ToDictionary(c => c.Name, c => c, StringComparer.Ordinal);

            return unique
                .Values
                .Where(c => string.IsNullOrWhiteSpace(c.ParentCategory) || unique.ContainsKey(c.ParentCategory))
                .OrderBy(c => c.Level)
                .ThenBy(c => c.Order)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .ToList();
        }

        private TreeNodeItem CreateCategorySelectionNode(TCategory category, List<TCategory> allCategories, int level, TreeNodeItem parentNode, string parentPath)
        {
            var node = new TreeNodeItem
            {
                Name = category.Name,
                Icon = IconHelper.TryGet(category.Icon) ?? IconHelper.TryGet("Icon.Folder"),
                Level = level,
                Tag = category,
                ShowChildCount = false
            };

            _categoryNodeIndex[category.Name] = node;
            _categorySelectionParentMap[node] = parentNode;
            var currentPath = string.Concat(parentPath, " > ", node.Name);
            _categorySelectionPathCache[node] = currentPath;

            var children = allCategories
                .Where(c => string.Equals(c.ParentCategory, category.Name, StringComparison.Ordinal))
                .OrderBy(c => c.Order)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .ToList();

            foreach (var child in children)
            {
                node.Children.Add(CreateCategorySelectionNode(child, allCategories, level + 1, node, currentPath));
            }

            return node;
        }

        private void HandleCategoryTreeNodeSelected(TreeNodeItem? node)
        {
            if (node == null)
            {
                return;
            }

            if (node.Tag == null && node.Name == "主页导航")
            {
                TM.App.Log($"[{GetType().Name}] 选中主页导航，用于创建顶级分类");

                SelectedCategoryTreePath = "主页导航";
                SelectedCategoryTreeIcon = IconHelper.TryGet("Icon.Home");
                IsCategoryTreeDropdownOpen = false;
                return;
            }

            _suppressCategorySelectionSync = true;
            try
            {
                ApplyCategorySelection(node.Name);
            }
            finally
            {
                _suppressCategorySelectionSync = false;
            }

            var fullPath = BuildNodePathFromTree(node);
            SelectedCategoryTreePath = fullPath;

            if (node.Tag is ICategory category)
            {
                SelectedCategoryTreeIcon = IconHelper.TryGet(category.Icon) ?? IconHelper.TryGet("Icon.Folder");
            }
            else
            {
                SelectedCategoryTreeIcon = IconHelper.TryGet("Icon.Folder");
            }

            IsCategoryTreeDropdownOpen = false;
        }

        private string BuildNodePathFromTree(TreeNodeItem targetNode)
        {
            if (_categorySelectionPathCache.TryGetValue(targetNode, out var cachedPath))
            {
                return cachedPath;
            }

            var path = new List<TreeNodeItem>();

            foreach (var root in CategorySelectionTree)
            {
                if (FindNodePathInTree(root, targetNode, path))
                {
                    return string.Join(" > ", path.Select(n => n.Name));
                }
                path.Clear();
            }

            return targetNode.Name;
        }

        private bool FindNodePathInTree(TreeNodeItem current, TreeNodeItem target, List<TreeNodeItem> path)
        {
            path.Add(current);

            if (current == target)
            {
                return true;
            }

            foreach (var child in current.Children)
            {
                if (FindNodePathInTree(child, target, path))
                {
                    return true;
                }
            }

            path.Remove(current);
            return false;
        }

        private void UpdateCategorySelectionDisplayCore(string? categoryName)
        {
            foreach (var prevNode in _lastCategorySelectionPath)
            {
                prevNode.IsSelected = false;
            }
            _lastCategorySelectionPath.Clear();

            if (string.IsNullOrWhiteSpace(categoryName))
            {
                SelectedCategoryTreePath = string.Empty;
                SelectedCategoryTreeIcon = null;
                return;
            }

            if (_categoryNodeIndex.TryGetValue(categoryName, out var node))
            {
                node.IsSelected = true;
                SelectedCategoryTreePath = BuildNodePath(node);
                if (node.Tag is ICategory category)
                {
                    SelectedCategoryTreeIcon = IconHelper.TryGet(category.Icon) ?? IconHelper.TryGet("Icon.Folder");
                }
                else
                {
                    SelectedCategoryTreeIcon = IconHelper.TryGet("Icon.Folder");
                }

                _lastCategorySelectionPath.Add(node);
            }
            else
            {
                SelectedCategoryTreePath = categoryName;
                if (_categoryLookup.TryGetValue(categoryName, out var category))
                {
                    SelectedCategoryTreeIcon = IconHelper.TryGet(category.Icon) ?? IconHelper.TryGet("Icon.Folder");
                }
                else
                {
                    SelectedCategoryTreeIcon = IconHelper.TryGet("Icon.Folder");
                }
            }
        }

        private void ClearSelectionState(TreeNodeItem node)
        {
            node.IsSelected = false;
            foreach (var child in node.Children)
            {
                ClearSelectionState(child);
            }
        }

        private string BuildNodePath(TreeNodeItem node)
        {
            if (_categorySelectionPathCache.TryGetValue(node, out var cachedPath))
            {
                return cachedPath;
            }

            if (_categorySelectionParentMap.Count > 0 && _categorySelectionParentMap.ContainsKey(node))
            {
                var parts = new List<string>();
                var current = node;
                while (true)
                {
                    parts.Add(current.Name);
                    if (!_categorySelectionParentMap.TryGetValue(current, out var parent) || parent == null)
                    {
                        break;
                    }

                    current = parent;
                }

                parts.Reverse();
                return string.Join(" > ", parts);
            }

            var stack = new Stack<string>();
            var cursor = node;

            while (cursor != null)
            {
                stack.Push(cursor.Name);

                if (cursor.Tag is not ICategory category || string.IsNullOrWhiteSpace(category.ParentCategory))
                {
                    TreeNodeItem? parentNode = null;
                    bool found = false;

                    foreach (var rootNode in CategorySelectionTree)
                    {
                        if (FindParentNode(rootNode, cursor, out parentNode))
                        {
                            cursor = parentNode;
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        break;
                    }
                }
                else
                {
                    if (!_categoryNodeIndex.TryGetValue(category.ParentCategory, out cursor))
                    {
                        stack.Push(category.ParentCategory);
                        break;
                    }
                }
            }

            return string.Join(" > ", stack);
        }

        private bool FindParentNode(TreeNodeItem searchRoot, TreeNodeItem targetNode, out TreeNodeItem? parentNode)
        {
            parentNode = null;
            foreach (var child in searchRoot.Children)
            {
                if (child == targetNode)
                {
                    parentNode = searchRoot;
                    return true;
                }

                if (FindParentNode(child, targetNode, out parentNode))
                {
                    return true;
                }
            }
            return false;
        }

        protected bool IsSelectedFromHomepageMirror()
        {
            if (string.IsNullOrWhiteSpace(SelectedCategoryTreePath))
                return false;

            if (SelectedCategoryTreePath == "主页导航")
                return true;

            var pathParts = SelectedCategoryTreePath.Split(new[] { " > " }, StringSplitOptions.None);
            return pathParts.Length > 1 && pathParts[0] == "主页导航";
        }

        protected bool ShouldCreateCategory(string? formCategory)
        {
            return !string.Equals(FormType, "数据", StringComparison.Ordinal);
        }

        private static string TrimForToast(string? value, int maxLen = 200)
        {
            if (string.IsNullOrWhiteSpace(value)) return "未知错误";
            var s = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length <= maxLen ? s : s[..maxLen] + "...";
        }
    }

    public enum NormalizationType
    {
        StaticOptions,
        DynamicList
    }

    public class FieldNormalizationRule
    {
        public string FieldName { get; set; } = string.Empty;

        public NormalizationType Type { get; set; }

        public List<string>? StaticOptions { get; set; }

        public Func<List<string>>? DynamicOptionsProvider { get; set; }

        public string DefaultValue { get; set; } = string.Empty;

        public bool AllowEmpty { get; set; }

        public bool LogWarning { get; set; } = true;
    }

    public class ModuleNormalizationConfig
    {
        public string ModuleName { get; set; } = string.Empty;

        public List<FieldNormalizationRule> Rules { get; set; } = new();
    }
}

