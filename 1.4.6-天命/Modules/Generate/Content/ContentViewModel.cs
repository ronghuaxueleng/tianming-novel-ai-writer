using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Framework.Common.Controls;
using TM.Framework.Common.Controls.Dialogs;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.Common.Services;
using TM.Services.Modules.ProjectData.Models.Generate.Content;
using TM.Modules.Generate.Content.Services;
using TM.Modules.Generate.Content.Views;
using TM.Services.Modules.ProjectData.Helpers;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.ChangeDetection;
using TM.Services.Framework.SystemIntegration;
using TM.Framework.UI.Workspace.Services;

namespace TM.Modules.Generate.Content
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ContentViewModel : INotifyPropertyChanged
    {
        private readonly IChangeDetectionService _changeDetectionService;
        private readonly IPublishService _publishService;
        private readonly IModuleEnabledService _moduleEnabledService;
        private readonly ContentConfigService _configService;
        private bool _isLoading;
        private string _statusSummary = string.Empty;
        private string _lastPublishTime = "从未打包";
        private int _changedCount;

        public ContentViewModel(IChangeDetectionService changeDetectionService, IPublishService publishService, IModuleEnabledService moduleEnabledService, ContentConfigService configService)
        {
            _changeDetectionService = changeDetectionService;
            _publishService = publishService;
            _moduleEnabledService = moduleEnabledService;
            _configService = configService;

            ModuleGroups = new ObservableCollection<ModuleGroupInfo>();

            RefreshCommand = new AsyncRelayCommand(RefreshAllAsync);
            PublishCommand = new AsyncRelayCommand(PublishAsync);
            ClearPackageCommand = new AsyncRelayCommand(ClearPackageAsync);
            ShowHistoryCommand = new RelayCommand(_ => ShowHistory());
            NavigateToModuleCommand = new RelayCommand(NavigateToModule);
            RefreshModuleCommand = new RelayCommand(RefreshSingleModule);
            ToggleModuleEnabledCommand = new AsyncRelayCommand(ToggleModuleEnabledAsync);
            GlobalCleanupCommand = new RelayCommand(_ => ExecuteGlobalCleanup());
            BusinessCleanupCommand = new RelayCommand(_ => ExecuteBusinessCleanup());

            _ = InitializeAsync();
        }

        public ObservableCollection<ModuleGroupInfo> ModuleGroups { get; }

        public ObservableCollection<ModuleCardInfo> AllCards { get; } = new();

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

        public ICommand RefreshCommand { get; }
        public ICommand PublishCommand { get; }
        public ICommand ClearPackageCommand { get; }
        public ICommand ShowHistoryCommand { get; }
        public ICommand NavigateToModuleCommand { get; }
        public ICommand RefreshModuleCommand { get; }
        public ICommand ToggleModuleEnabledCommand { get; }
        public ICommand GlobalCleanupCommand { get; }
        public ICommand BusinessCleanupCommand { get; }

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

                var manifest = _publishService.GetManifest();
                if (manifest != null)
                {
                    LastPublishTime = manifest.PublishTime.ToString("yyyy-MM-dd HH:mm");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentViewModel] 刷新失败: {ex.Message}");
                GlobalToast.Error("刷新失败", ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private static readonly (string ModuleType, string SubModule, string DisplayName, string GroupName, string GroupIcon)[] PackageModuleAllowlist =
        {
            ("Design", "GlobalSettings", "全局设定", "设计模块", "📋"),
            ("Design", "Elements", "设计元素", "设计模块", "📋"),
            ("Generate", "GlobalSettings", "全书设定", "生成模块", "📋"),
            ("Generate", "Elements", "创作元素", "生成模块", "📋")
        };

        private void BuildModuleGroups()
        {
            ModuleGroups.Clear();

            var groups = new Dictionary<string, ModuleGroupInfo>();

            foreach (var (moduleType, subModule, displayName, groupName, groupIcon) in PackageModuleAllowlist)
            {
                if (moduleType == "Generate" && subModule == "Content")
                    continue;

                if (!groups.TryGetValue(moduleType, out var group))
                {
                    group = new ModuleGroupInfo
                    {
                        ModuleType = moduleType,
                        DisplayName = groupName,
                        Icon = groupIcon
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

            foreach (var group in groups.Values)
            {
                if (group.Cards.Count > 0)
                {
                    ModuleGroups.Add(group);
                }
            }

            AllCards.Clear();
            foreach (var group in ModuleGroups)
            {
                foreach (var card in group.Cards)
                {
                    AllCards.Add(card);
                }
            }
        }

        private string GetModuleIcon(string moduleType, string subModule)
        {
            var functions = NavigationConfigParser.GetFunctionsBySubModule(moduleType, subModule);
            if (functions.Count > 0)
            {
                return functions[0].Icon ?? "📁";
            }
            return "📁";
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
                    var errorText = !string.IsNullOrWhiteSpace(result.ErrorDetail)
                        ? result.ErrorDetail
                        : (result.Message ?? "未知错误");
                    GlobalToast.Error("打包失败", errorText);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentViewModel] 打包失败: {ex.Message}");
                GlobalToast.Error("打包失败", ex.Message);
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
                GlobalToast.Error("清除失败", ex.Message);
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
                    GlobalToast.Warning("清理失败", "清理过程中遇到问题，请查看日志");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentViewModel] 全局清理异常: {ex.Message}");
                GlobalToast.Error("清理失败", ex.Message);
            }
        }

        private void ExecuteBusinessCleanup()
        {
            if (!StandardDialog.ShowConfirm(
                "业务清理将清除【设计】和【创作】模块所有已生成的数据，包括：\n\n" +
                "• 设计：智能拆书、创作模板、世界观规则、角色/势力/地点/剧情规则\n" +
                "• 创作：大纲设计、分卷设计、章节设计、蓝图设计\n\n" +
                "此操作不可恢复，确定要执行吗？",
                "业务清理确认"))
            {
                return;
            }

            try
            {
                TM.App.Log("[ContentViewModel] 执行业务清理");

                var (success, clearedCount, details) = BusinessCleanupService.Execute();

                if (success)
                {
                    GlobalToast.Success("业务清理完成",
                        clearedCount > 0
                            ? $"已即时清空 {clearedCount} 条业务数据"
                            : "当前无可清理业务数据");

                    ServiceLocator.Get<PanelCommunicationService>().PublishBusinessDataCleared();

                    _ = RefreshAllAsync();
                }
                else
                {
                    GlobalToast.Warning("清理失败", "清理过程中遇到问题，请查看日志");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentViewModel] 业务清理异常: {ex.Message}");
                GlobalToast.Error("清理失败", ex.Message);
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
            if (parameter is string modulePath)
            {
                TM.App.Log($"[ContentViewModel] 导航到模块: {modulePath}");
                NavigationRequested?.Invoke(this, modulePath);
            }
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
                    GlobalToast.Error("操作失败", ex.Message);
                }
                finally
                {
                    IsLoading = false;
                }
            }
        }

        public event EventHandler<string>? NavigationRequested;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
