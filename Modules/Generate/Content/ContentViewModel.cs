using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Services.Modules.ProjectData.Models.Generate.Content;
using TM.Modules.Generate.Content.Services;
using TM.Modules.Generate.Content.Views;
using TM.Services.Modules.ProjectData.Helpers;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.ChangeDetection;
using TM.Services.Framework.SystemIntegration;
using TM.Framework.Common.Constants;
using TM.Framework.Common.ViewModels;
using TM.Framework.UI.Workspace.Services;

namespace TM.Modules.Generate.Content
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class ContentViewModel : INotifyPropertyChanged
    {
        private readonly IChangeDetectionService _changeDetectionService;
        private readonly IPublishService _publishService;
        private readonly IModuleEnabledService _moduleEnabledService;
        private readonly ContentConfigService _configService;
        private readonly PanelCommunicationService _panelCommunicationService;
        private bool _isLoading;
        private string _statusSummary = string.Empty;
        private string _lastPublishTime = "从未打包";
        private int _changedCount;

        public ContentViewModel(IChangeDetectionService changeDetectionService, IPublishService publishService, IModuleEnabledService moduleEnabledService, ContentConfigService configService, PanelCommunicationService panelCommunicationService)
        {
            _changeDetectionService = changeDetectionService;
            _publishService = publishService;
            _moduleEnabledService = moduleEnabledService;
            _configService = configService;
            _panelCommunicationService = panelCommunicationService;

            ModuleGroups = new RangeObservableCollection<ModuleGroupInfo>();

            PublishCommand = new AsyncRelayCommand(PublishAsync);
            ClearPackageCommand = new AsyncRelayCommand(ClearPackageAsync);
            ShowHistoryCommand = new RelayCommand(_ => ShowHistory());
            NavigateToModuleCommand = new RelayCommand(NavigateToModule);
            RefreshModuleCommand = new RelayCommand(RefreshSingleModule);
            ToggleModuleEnabledCommand = new AsyncRelayCommand(ToggleModuleEnabledAsync);
            GlobalCleanupCommand = new RelayCommand(_ => ExecuteGlobalCleanup());

            _ = InitializeAsync();
        }

        public RangeObservableCollection<ModuleGroupInfo> ModuleGroups { get; }

        public RangeObservableCollection<ModuleCardInfo> AllCards { get; } = new RangeObservableCollection<ModuleCardInfo>();

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        public string StatusSummary
        {
            get => _statusSummary;
            set { _statusSummary = value; OnPropertyChanged(); }
        }

        public string LastPublishTime
        {
            get => _lastPublishTime;
            set { _lastPublishTime = value; OnPropertyChanged(); }
        }

        public int ChangedCount
        {
            get => _changedCount;
            set { _changedCount = value; OnPropertyChanged(); }
        }

        public ICommand PublishCommand { get; }
        public ICommand ClearPackageCommand { get; }
        public ICommand ShowHistoryCommand { get; }
        public ICommand NavigateToModuleCommand { get; }
        public ICommand RefreshModuleCommand { get; }
        public ICommand ToggleModuleEnabledCommand { get; }
        public ICommand GlobalCleanupCommand { get; }

        private async Task InitializeAsync()
        {
            TM.App.Log("[ContentViewModel] 初始化正文模块");
            await RefreshAllAsync();
        }

        private async Task RefreshAllAsync()
        {
            IsLoading = true;
            TM.App.Log("[ContentViewModel] 刷新所有模块状态");

            try
            {
                await _changeDetectionService.RefreshAllAsync();

                BuildModuleGroups();

                UpdateStatusSummary();

                var manifest = await _publishService.GetManifestAsync().ConfigureAwait(true);
                if (manifest != null)
                {
                    LastPublishTime = manifest.PublishTime.ToString("yyyy-MM-dd HH:mm");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentViewModel] 刷新失败: {ex.Message}");
                GlobalToast.Error("刷新失败", $"刷新失败：{ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private static readonly Dictionary<string, (string GroupName, string GroupIcon)> _moduleGroupMeta = new()
        {
            ["Design"] = ("设计模块", ""),
            ["Generate"] = ("生成模块", "")
        };

        private void BuildModuleGroups()
        {
            var groups = new Dictionary<string, ModuleGroupInfo>();

            foreach (var (moduleType, allowedNames) in PackagingAllowlist.SubModules)
            {
                if (!_moduleGroupMeta.TryGetValue(moduleType, out var meta)) continue;

                var subModules = NavigationConfigParser.GetSubModules(moduleType)
                    .Where(sm => allowedNames.Contains(sm.DisplayName))
                    .ToList();

                foreach (var (subModule, displayName) in subModules)
                {
                    if (moduleType == "Generate" && subModule == "Content")
                        continue;

                    if (!groups.TryGetValue(moduleType, out var group))
                    {
                        group = new ModuleGroupInfo
                        {
                            ModuleType = moduleType,
                            DisplayName = meta.GroupName,
                            Icon = meta.GroupIcon
                        };
                        groups[moduleType] = group;
                    }

                    var modulePath = $"{moduleType}/{subModule}";
                    var status = _changeDetectionService.GetStatus(modulePath);

                    var isEnabled = _configService.IsModuleEnabled(modulePath);

                    var card = new ModuleCardInfo
                    {
                        ModulePath = modulePath,
                        ModuleType = moduleType,
                        SubModuleName = subModule,
                        DisplayName = displayName,
                        Icon = GetModuleIcon(moduleType, subModule),
                        IsEnabled = isEnabled,
                        HasChanges = status.Status == ChangeStatusType.Changed || status.Status == ChangeStatusType.Never,
                        ItemCountText = $"{status.ItemCount}项"
                    };

                    _changeDetectionService.MarkModuleEnabled(modulePath, isEnabled);

                    card.PropertyChanged += OnCardPropertyChanged;

                    group.Cards.Add(card);
                }
            }

            var newGroups = new List<ModuleGroupInfo>();
            foreach (var group in groups.Values)
            {
                if (group.Cards.Count > 0)
                {
                    newGroups.Add(group);
                }
            }

            ReplaceCollection(ModuleGroups, newGroups);

            var allCards = new List<ModuleCardInfo>();
            foreach (var group in newGroups)
            {
                foreach (var card in group.Cards)
                {
                    allCards.Add(card);
                }
            }

            ReplaceCollection(AllCards, allCards);
        }

        private static void ReplaceCollection<T>(RangeObservableCollection<T> target, IEnumerable<T> items)
        {
            target.ReplaceAll(items is IList<T> list ? list : items.ToList());
        }

        private string GetModuleIcon(string moduleType, string subModule)
        {
            var functions = NavigationConfigParser.GetFunctionsBySubModule(moduleType, subModule);
            if (functions.Count > 0)
            {
                return functions[0].Icon ?? "";
            }
            return "";
        }

        private void OnCardPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ModuleCardInfo.IsEnabled) && sender is ModuleCardInfo card)
            {
                TM.App.Log($"[ContentViewModel] 模块启用状态变化: {card.ModulePath} = {card.IsEnabled}");

                _configService.SetModuleEnabled(card.ModulePath, card.IsEnabled);

                _changeDetectionService.MarkModuleEnabled(card.ModulePath, card.IsEnabled);

                UpdateStatusSummary();
            }
        }

        private void UpdateStatusSummary()
        {
            var enabledCounts = ModuleGroups.Select(g => $"{g.DisplayName.Replace("模块", "")}({g.EnabledCountText})");
            var totalEnabled = ModuleGroups.Sum(g => g.Cards.Count(c => c.IsEnabled));
            var totalCount = ModuleGroups.Sum(g => g.Cards.Count);

            ChangedCount = ModuleGroups.Sum(g => g.Cards.Count(c => c.HasChanges));

            StatusSummary = $"已启用：{string.Join(" + ", enabledCounts)}";
        }

        private async Task PublishAsync()
        {
            var enabledCount = ModuleGroups.Sum(g => g.Cards.Count(c => c.IsEnabled));
            if (enabledCount == 0)
            {
                GlobalToast.Warning("无法打包", "请至少启用一个模块");
                return;
            }

            var confirmMessage = ChangedCount > 0
                ? $"检测到 {ChangedCount} 个模块有变更，确定要重新打包吗？"
                : "确定要重新打包所有已启用的模块吗？";

            if (!StandardDialog.ShowConfirm(confirmMessage, "确认打包"))
                return;

            IsLoading = true;
            TM.App.Log("[ContentViewModel] 开始打包");

            try
            {
                TM.App.Log("[ContentViewModel] 打包前自动执行全局清理");
                GlobalCleanupService.Execute();

                var result = await _publishService.PublishAllAsync();

                if (result.IsSuccess)
                {
                    GlobalToast.Success("打包成功", $"版本 {result.Version}，共 {result.PackagedModules.Count} 个模块");

                    await RefreshAllAsync();
                }
                else
                {
                    TM.App.Log($"[ContentViewModel] 打包失败: {result.ErrorDetail ?? result.Message}");
                    GlobalToast.Error("打包失败", $"打包失败：{result.ErrorDetail ?? result.Message ?? "未知原因"}");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentViewModel] 打包失败: {ex.Message}");
                GlobalToast.Error("打包失败", $"打包失败：{ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ClearPackageAsync()
        {
            if (!StandardDialog.ShowConfirm("确定要清除所有打包文件、已生成章节、向量索引和历史记录吗？\n此操作不可恢复。", "确认清除"))
                return;

            IsLoading = true;
            TM.App.Log("[ContentViewModel] 开始清除打包");

            try
            {
                var historyService = ServiceLocator.Get<IPackageHistoryService>();
                var success = await historyService.ClearAllAsync();

                if (success)
                {
                    GlobalToast.Success("清除成功", "所有打包文件、生成内容和缓存已清除");
                    await RefreshAllAsync();
                }
                else
                {
                    GlobalToast.Error("清除失败", "无法删除打包文件");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentViewModel] 清除打包失败: {ex.Message}");
                GlobalToast.Error("清除失败", $"清除失败：{ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ExecuteGlobalCleanup()
        {
            if (!StandardDialog.ShowConfirm(
                "全局清理将清除所有AI生成会话的上下文缓存，\n\n确定要执行清理吗？",
                "全局清理确认"))
            {
                return;
            }

            try
            {
                TM.App.Log("[ContentViewModel] 执行全局清理");

                var success = GlobalCleanupService.Execute();

                if (success)
                {
                    GlobalToast.Success("清理完成", "已清理所有AI生成会话缓存");
                }
                else
                {
                    GlobalToast.Warning("清理失败", "清理未完全成功，请稍后重试");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentViewModel] 全局清理异常: {ex.Message}");
                GlobalToast.Error("清理失败", $"清理失败：{ex.Message}");
            }
        }

        private void ShowHistory()
        {
            var dialog = new PackageHistoryDialog();
            StandardDialog.EnsureOwnerAndTopmost(dialog, null);
            dialog.ShowDialog();

            _ = RefreshAllAsync();
        }

        private void NavigateToModule(object? parameter)
        {
            if (parameter is not string modulePath) return;

            var parts = modulePath.Split('/');
            if (parts.Length < 2) return;

            var moduleType = parts[0];
            var subModule = parts[1];

            var moduleNav = NavigationDefinitions.GetModuleByName(moduleType);
            if (moduleNav == null)
            {
                TM.App.Log($"[ContentViewModel] 导航失败：找不到模块 {moduleType}");
                return;
            }

            var subModuleDisplayName = NavigationConfigParser.GetSubModuleDisplayName(subModule);
            var subModuleNav = moduleNav.SubModules
                .FirstOrDefault(s => string.Equals(s.Name, subModuleDisplayName, StringComparison.Ordinal));
            var viewType = subModuleNav?.Functions.FirstOrDefault()?.ViewType;

            if (viewType == null)
            {
                TM.App.Log($"[ContentViewModel] 导航失败：找不到 {modulePath} 对应的视图类型");
                return;
            }

            TM.App.Log($"[ContentViewModel] 导航到模块: {modulePath} -> {viewType.Name}");
            _panelCommunicationService.PublishFunctionNavigationRequested(moduleType, subModule, viewType);
        }

        private void RefreshSingleModule(object? parameter)
        {
            if (parameter is ModuleCardInfo card)
            {
                TM.App.Log($"[ContentViewModel] 刷新单个模块: {card.ModulePath}");
                var status = _changeDetectionService.GetStatus(card.ModulePath);
                card.HasChanges = status.Status == ChangeStatusType.Changed || status.Status == ChangeStatusType.Never;
                card.ItemCountText = $"{status.ItemCount}项";
                GlobalToast.Success("已刷新", $"{card.DisplayName} 状态已更新");
            }
        }

        private async Task ToggleModuleEnabledAsync(object? parameter)
        {
            if (parameter is ModuleCardInfo card)
            {
                var newEnabled = !card.IsEnabled;

                try
                {
                    IsLoading = true;

                    var updatedCount = await _moduleEnabledService.SetModuleEnabledAsync(
                        card.ModuleType,
                        card.SubModuleName,
                        newEnabled);

                    card.IsEnabled = newEnabled;

                    UpdateStatusSummary();

                    var statusText = newEnabled ? "启用" : "禁用";
                    GlobalToast.Success("状态已更新", $"{card.DisplayName}已{statusText}，更新了{updatedCount}条数据");

                    TM.App.Log($"[ContentViewModel] {card.DisplayName} 设置为 {statusText}，更新了 {updatedCount} 条数据");
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ContentViewModel] 切换启用状态失败: {ex.Message}");
                    GlobalToast.Error("操作失败", $"操作失败：{ex.Message}");
                }
                finally
                {
                    IsLoading = false;
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
