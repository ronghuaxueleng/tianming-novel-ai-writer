using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Models;

namespace TM.Framework.Common.ViewModels
{
    public abstract partial class TreeDataViewModelBase<TData, TCategory>
        where TCategory : ICategory
    {
        private List<TCategory> FilterOrphanCategories(List<TCategory> allCategories)
        {
            var categoryNames = new HashSet<string>(allCategories.Select(c => c.Name));
            return allCategories
                .Where(c => string.IsNullOrEmpty(c.ParentCategory) || categoryNames.Contains(c.ParentCategory))
                .ToList();
        }

        private TreeNodeItem? BuildCategoryTreeWithChildren(
            TCategory category,
            List<TCategory> allCategories,
            bool hasSearchKeyword,
            int maxLevel)
        {
            var childrenData = GetChildrenDataForCategory(category.Name);

            var childCategories = allCategories
                .Where(c => c.ParentCategory == category.Name)
                .OrderBy(c => c.Order)
                .ToList();

            var childCategoryNodes = new List<TreeNodeItem>();
            if (category.Level < maxLevel)
            {
                foreach (var child in childCategories)
                {
                    var childNode = BuildCategoryTreeWithChildren(child, allCategories, hasSearchKeyword, maxLevel);
                    if (childNode != null)
                    {
                        childCategoryNodes.Add(childNode);
                    }
                }
            }

            if (hasSearchKeyword)
            {
                bool hasMatchedContent = childrenData.Count > 0 || childCategoryNodes.Count > 0;
                if (!hasMatchedContent)
                {
                    return null;
                }
            }

            var categoryNode = new TreeNodeItem
            {
                Name = category.Name,
                Icon = IconHelper.TryGet(category.Icon),
                LogoImage = GetCategoryLogoImage(category),
                Level = category.Level,
                Tag = category,
                IsExpanded = hasSearchKeyword && (childrenData.Count > 0 || childCategoryNodes.Count > 0),
                ShowChildCount = true
            };

            foreach (var childNode in childCategoryNodes)
            {
                categoryNode.Children.Add(childNode);
            }

            foreach (var childData in childrenData)
            {
                var childNode = ConvertDataToTreeNode(childData);
                childNode.Level = category.Level + 1;
                categoryNode.Children.Add(childNode);
            }

            return categoryNode;
        }

        private TreeBuildPlan? CollectCategoryPlanLimited(
            TCategory category,
            List<TCategory> allCategories,
            int maxLevel,
            ref int totalCount,
            int maxCount,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (totalCount >= maxCount)
                return null;

            var childrenData = GetChildrenDataForCategory(category.Name);

            var childCategories = allCategories
                .Where(c => c.ParentCategory == category.Name)
                .OrderBy(c => c.Order)
                .ToList();

            var childPlans = new List<TreeBuildPlan>();
            if (category.Level < maxLevel)
            {
                foreach (var child in childCategories)
                {
                    if (totalCount >= maxCount) break;
                    ct.ThrowIfCancellationRequested();

                    var childPlan = CollectCategoryPlanLimited(
                        child, allCategories, maxLevel,
                        ref totalCount, maxCount, ct);
                    if (childPlan != null)
                        childPlans.Add(childPlan);
                }
            }

            bool hasMatchedContent = childrenData.Count > 0 || childPlans.Count > 0;
            if (!hasMatchedContent)
                return null;

            var plan = new TreeBuildPlan { Category = category };
            plan.ChildPlans.AddRange(childPlans);
            foreach (var data in childrenData)
            {
                if (totalCount >= maxCount) break;
                plan.ChildrenData.Add(data);
                totalCount++;
            }

            return plan;
        }

        private List<TreeNodeItem> BuildTreeNodesFromPlan(List<TreeBuildPlan>? plans, CancellationToken ct, bool autoExpand = true)
        {
            var result = new List<TreeNodeItem>();
            if (plans == null) return result;

            foreach (var plan in plans)
            {
                if (ct.IsCancellationRequested) break;
                var node = BuildNodeFromPlan(plan, ct, autoExpand);
                if (node != null) result.Add(node);
            }
            return result;
        }

        private TreeNodeItem? BuildNodeFromPlan(TreeBuildPlan plan, CancellationToken ct, bool autoExpand = true)
        {
            if (ct.IsCancellationRequested) return null;

            var category = plan.Category;
            var categoryNode = new TreeNodeItem
            {
                Name = category.Name,
                Icon = IconHelper.TryGet(category.Icon),
                LogoImage = GetCategoryLogoImage(category),
                Level = category.Level,
                Tag = category,
                IsExpanded = autoExpand && (plan.ChildrenData.Count > 0 || plan.ChildPlans.Count > 0),
                ShowChildCount = true
            };

            foreach (var childPlan in plan.ChildPlans)
            {
                var childNode = BuildNodeFromPlan(childPlan, ct, autoExpand);
                if (childNode != null) categoryNode.Children.Add(childNode);
            }

            foreach (var childData in plan.ChildrenData)
            {
                var childNode = ConvertDataToTreeNode(childData);
                childNode.Level = category.Level + 1;
                categoryNode.Children.Add(childNode);
            }

            return categoryNode;
        }

        protected abstract List<TCategory> GetAllCategories();

        protected abstract List<TData> GetChildrenDataForCategory(string categoryName);

        protected abstract TreeNodeItem ConvertDataToTreeNode(TData data);

        protected virtual bool IsNodeCategory(TreeNodeItem node)
        {
            return node?.Tag is TCategory;
        }

        protected virtual TCategory? GetCategoryFromNode(TreeNodeItem node)
        {
            return node?.Tag is TCategory category ? category : default;
        }

        protected virtual TData? GetDataFromNode(TreeNodeItem node)
        {
            return node?.Tag is TData data ? data : default;
        }

        protected virtual string GetCategoryIcon(string categoryName)
        {
            return "Icon.Folder";
        }

        protected virtual System.Windows.Media.ImageSource? GetCategoryLogoImage(TCategory category)
        {
            try
            {
                var logoPath = (category as ILogoPathHost)?.LogoPath;
                var icon = category.Icon ?? "";

                if (category is ILogoPathHost)
                {
                    if (string.IsNullOrEmpty(logoPath) && category.Level == 1)
                    {
                        logoPath = "app.png";
                    }
                    else if (string.IsNullOrEmpty(logoPath) && category.Level >= 2)
                    {
                        logoPath = TM.Framework.Common.Helpers.AI.ProviderLogoHelper.GetLogoFileName(category.Name);
                    }

                    return TM.Framework.Common.Helpers.AI.ProviderLogoHelper.GetLogo(logoPath, icon);
                }
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(GetCategoryLogoImage), ex);
            }

            return null;
        }

        protected virtual void OnNodeDoubleClick(TreeNodeItem node)
        {
            var data = GetDataFromNode(node);
            if (data != null)
            {
                OnDataSelected(data);
            }
        }

        protected virtual void OnDataSelected(TData data)
        {
        }

        protected virtual TData? CreateNewData(string? categoryName = null)
        {
            TM.App.Log($"[TreeDataViewModelBase] CreateNewData未实现，子类需要重写此方法以支持新建数据");
            return default;
        }

        protected virtual void OnDataChanged()
        {
            RefreshTreeData();
        }

        protected virtual void OnTreeDataRefreshed()
        {
        }

        protected void FocusTreeNode(Func<TreeNodeItem, bool> predicate)
        {
            if (predicate == null)
            {
                return;
            }

            var path = FindNodePath(TreeData, predicate);
            if (path == null || path.Count == 0)
            {
                return;
            }

            ClearSelection(TreeData);

            for (var i = 0; i < path.Count; i++)
            {
                var node = path[i];

                if (i < path.Count - 1 && !node.IsExpanded)
                {
                    node.IsExpanded = true;
                }

                node.IsSelected = true;
                node.IsSelectionFocus = i == path.Count - 1;
            }
        }

        private static List<TreeNodeItem>? FindNodePath(IEnumerable<TreeNodeItem> nodes, Func<TreeNodeItem, bool> predicate)
        {
            foreach (var node in nodes)
            {
                var path = FindNodePathRecursive(node, predicate);
                if (path != null)
                {
                    return path;
                }
            }

            return null;
        }

        private static List<TreeNodeItem>? FindNodePathRecursive(TreeNodeItem current, Func<TreeNodeItem, bool> predicate)
        {
            if (predicate(current))
            {
                return new List<TreeNodeItem> { current };
            }

            foreach (var child in current.Children)
            {
                var childPath = FindNodePathRecursive(child, predicate);
                if (childPath != null)
                {
                    childPath.Insert(0, current);
                    return childPath;
                }
            }

            return null;
        }

        private static void ClearSelection(IEnumerable<TreeNodeItem> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.IsSelected)
                {
                    node.IsSelected = false;
                }

                if (node.IsSelectionFocus)
                {
                    node.IsSelectionFocus = false;
                }

                if (node.Children.Count > 0)
                {
                    ClearSelection(node.Children);
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

