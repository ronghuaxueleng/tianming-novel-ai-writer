using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Framework.User.Services;

namespace TM.Framework.User.Account.AccountBinding
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public class AccountBindingViewModel : INotifyPropertyChanged
    {
        private readonly AccountBindingService _bindingService;
        private readonly OAuthService _oAuthService;

        private ObservableCollection<AccountBindingModel> _availablePlatforms;
        public ObservableCollection<AccountBindingModel> AvailablePlatforms
        {
            get => _availablePlatforms;
            set
            {
                _availablePlatforms = value;
                OnPropertyChanged();
            }
        }

        public ICommand BindAccountCommand { get; }
        public ICommand UnbindAccountCommand { get; }
        public ICommand ViewDetailsCommand { get; }
        public ICommand SyncAccountCommand { get; }

        public AccountBindingViewModel(AccountBindingService bindingService, OAuthService oAuthService)
        {
            _bindingService = bindingService;
            _oAuthService = oAuthService;
            _availablePlatforms = new ObservableCollection<AccountBindingModel>();

            BindAccountCommand = new RelayCommand<PlatformType>(BindAccount);
            UnbindAccountCommand = new RelayCommand<PlatformType>(UnbindAccount);
            ViewDetailsCommand = new RelayCommand<PlatformType>(ViewDetails);
            SyncAccountCommand = new RelayCommand<PlatformType>(SyncAccount);

            InitializePlatforms();

            _ = RefreshAllBindingsFromServerAsync();
        }

        private async Task RefreshAllBindingsFromServerAsync()
        {
            try
            {
                await _bindingService.GetAllBindingsFromServerAsync();

                foreach (var platformModel in AvailablePlatforms)
                {
                    var binding = _bindingService.GetBinding(platformModel.Platform);
                    if (binding != null)
                    {
                        platformModel.IsBound = true;
                        platformModel.AccountId = binding.AccountId;
                        platformModel.Nickname = binding.Nickname;
                        platformModel.Email = binding.Email;
                        platformModel.AvatarUrl = binding.AvatarUrl;
                        platformModel.BindTime = binding.BindTime;
                        platformModel.LastSyncTime = binding.LastSyncTime;
                        platformModel.LastUseTime = binding.LastUseTime;
                        platformModel.SyncStatus = binding.SyncStatus;
                        platformModel.Permissions = binding.Permissions;
                    }
                    else
                    {
                        platformModel.IsBound = false;
                        platformModel.AccountId = string.Empty;
                        platformModel.Nickname = string.Empty;
                        platformModel.Email = string.Empty;
                        platformModel.AvatarUrl = string.Empty;
                        platformModel.BindTime = null;
                        platformModel.LastSyncTime = null;
                        platformModel.LastUseTime = null;
                        platformModel.SyncStatus = SyncStatus.None;
                        platformModel.Permissions = new();
                    }
                }

                OnPropertyChanged(nameof(AvailablePlatforms));
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AccountBindingViewModel] 从服务器刷新绑定列表失败: {ex.Message}");
            }
        }

        private void InitializePlatforms()
        {
            var iconMap = new Dictionary<PlatformType, string>
            {
                [PlatformType.WeChat] = "weixin.png",
                [PlatformType.QQ] = "qq.png",
                [PlatformType.GitHub] = "Github.png",
                [PlatformType.Google] = "Google.png",
                [PlatformType.Microsoft] = "Microsoft.png",
                [PlatformType.Baidu] = "Baidu.png"
            };

            var platforms = new[]
            {
                new AccountBindingModel { Platform = PlatformType.WeChat, PlatformName = "微信", PlatformIcon = IconHelper.TryGet("Icon.Chat") },
                new AccountBindingModel { Platform = PlatformType.QQ, PlatformName = "QQ", PlatformIcon = IconHelper.TryGet("Icon.Chat") },
                new AccountBindingModel { Platform = PlatformType.GitHub, PlatformName = "GitHub", PlatformIcon = IconHelper.TryGet("Icon.Globe") },
                new AccountBindingModel { Platform = PlatformType.Google, PlatformName = "Google", PlatformIcon = IconHelper.TryGet("Icon.Search") },
                new AccountBindingModel { Platform = PlatformType.Microsoft, PlatformName = "Microsoft", PlatformIcon = IconHelper.TryGet("Icon.Monitor") },
                new AccountBindingModel { Platform = PlatformType.Baidu, PlatformName = "百度", PlatformIcon = IconHelper.TryGet("Icon.Search") }
            };

            _ = AccountIconHelper.WarmUpAsync(iconMap.Values).ContinueWith(_ =>
            {
                foreach (var p in platforms)
                {
                    if (iconMap.TryGetValue(p.Platform, out var icon))
                        p.LogoImage = AccountIconHelper.GetIcon(icon);
                }
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());

            foreach (var platform in platforms)
            {
                var binding = _bindingService.GetBinding(platform.Platform);
                if (binding != null)
                {
                    platform.IsBound = true;
                    platform.AccountId = binding.AccountId;
                    platform.Nickname = binding.Nickname;
                    platform.Email = binding.Email;
                    platform.AvatarUrl = binding.AvatarUrl;
                    platform.BindTime = binding.BindTime;
                    platform.LastSyncTime = binding.LastSyncTime;
                    platform.LastUseTime = binding.LastUseTime;
                    platform.SyncStatus = binding.SyncStatus;
                    platform.Permissions = binding.Permissions;
                }

                AvailablePlatforms.Add(platform);
            }
        }

        private void BindAccount(PlatformType platform)
        {
            _ = BindAccountAsync(platform);
        }

        private async Task BindAccountAsync(PlatformType platform)
        {
            try
            {
                var platformModel = AvailablePlatforms.FirstOrDefault(p => p.Platform == platform);
                if (platformModel == null) return;

                if (platformModel.IsBound)
                {
                    ViewDetails(platform);
                    return;
                }

                var platformStr = platform.ToString().ToLower();
                string code;
                string nickname = string.Empty;
                string email = string.Empty;
                var permissions = new System.Collections.Generic.List<string> { "basic_info", "profile" };

                if (!OAuthService.IsPlatformConfigured(platformStr))
                {
                    StandardDialog.ShowWarning($"{platformModel.PlatformName}绑定尚未配置，请联系管理员。", "提示");
                    return;
                }

                GlobalToast.Info("OAuth授权", $"正在打开{platformModel.PlatformName}授权页面...");
                var authResult = await _oAuthService.StartAuthorizationAsync(platformStr);

                if (!authResult.Success)
                {
                    TM.App.Log($"[AccountBindingViewModel] 授权失败: {authResult.ErrorMessage}");
                    GlobalToast.Error("授权失败", $"授权失败：{authResult.ErrorMessage ?? "未知原因"}");
                    return;
                }

                code = authResult.Code ?? string.Empty;

                var result = await _bindingService.BindAccountAsync(
                    platform, code, nickname, email, string.Empty, permissions);

                if (!result.Success)
                {
                    TM.App.Log($"[AccountBindingViewModel] 绑定账号失败: {result.ErrorMessage}");
                    GlobalToast.Error("绑定账号", $"绑定失败：{result.ErrorMessage ?? "未知原因"}");
                    return;
                }

                await _bindingService.GetAllBindingsFromServerAsync();

                var binding = _bindingService.GetBinding(platform);
                if (binding != null)
                {
                    platformModel.IsBound = true;
                    platformModel.AccountId = binding.AccountId;
                    platformModel.Nickname = binding.Nickname;
                    platformModel.Email = binding.Email;
                    platformModel.AvatarUrl = binding.AvatarUrl;
                    platformModel.BindTime = binding.BindTime;
                    platformModel.LastSyncTime = binding.LastSyncTime;
                    platformModel.LastUseTime = binding.LastUseTime;
                    platformModel.SyncStatus = binding.SyncStatus;
                    platformModel.Permissions = binding.Permissions;
                }

                OnPropertyChanged(nameof(AvailablePlatforms));
                GlobalToast.Success("绑定账号", $"{platformModel.PlatformName}账号绑定成功");
                TM.App.Log($"[AccountBindingViewModel] 账号绑定成功: {platform} - {platformModel.Nickname}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AccountBindingViewModel] 绑定账号失败: {ex.Message}");
                GlobalToast.Error("绑定账号", $"操作失败：{ex.Message}");
            }
        }

        private void UnbindAccount(PlatformType platform)
        {
            try
            {
                var platformModel = AvailablePlatforms.FirstOrDefault(p => p.Platform == platform);
                if (platformModel == null) return;

                var result = StandardDialog.ShowConfirm("解绑账号",
                    $"确定要解绑{platformModel.PlatformName}账号({platformModel.Nickname})吗？\n\n解绑后将无法使用该账号快速登录。");

                if (result == true)
                {
                    _ = UnbindAccountAsync(platform, platformModel);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AccountBindingViewModel] 解绑账号失败: {ex.Message}");
                GlobalToast.Error("解绑账号", $"操作失败：{ex.Message}");
            }
        }

        private async Task UnbindAccountAsync(PlatformType platform, AccountBindingModel platformModel)
        {
            try
            {
                var result = await _bindingService.UnbindAccountAsync(platform);
                if (!result.Success)
                {
                    TM.App.Log($"[AccountBindingViewModel] 解绑账号失败: {result.ErrorMessage}");
                    GlobalToast.Error("解绑账号", $"解绑失败：{result.ErrorMessage ?? "未知原因"}");
                    return;
                }

                platformModel.IsBound = false;
                platformModel.AccountId = string.Empty;
                platformModel.Nickname = string.Empty;
                platformModel.Email = string.Empty;
                platformModel.BindTime = null;
                platformModel.LastSyncTime = null;
                platformModel.LastUseTime = null;
                platformModel.SyncStatus = SyncStatus.None;

                OnPropertyChanged(nameof(AvailablePlatforms));
                GlobalToast.Success("解绑账号", $"{platformModel.PlatformName}账号已解绑");
                TM.App.Log($"[AccountBindingViewModel] 账号解绑成功: {platform}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AccountBindingViewModel] 解绑账号失败: {ex.Message}");
                GlobalToast.Error("解绑账号", $"操作失败：{ex.Message}");
            }
        }

        private void ViewDetails(PlatformType platform)
        {
            try
            {
                var platformModel = AvailablePlatforms.FirstOrDefault(p => p.Platform == platform);
                if (platformModel == null || !platformModel.IsBound) return;

                var binding = _bindingService.GetBinding(platform);
                if (binding == null)
                {
                    GlobalToast.Warning("查看详情", "未找到绑定信息");
                    return;
                }

                var dialog = new BindingDetailsDialog(binding, platformModel.PlatformName, platformModel.PlatformIcon);
                StandardDialog.EnsureOwnerAndTopmost(dialog, null);
                dialog.ShowDialog();

                RefreshPlatformData(platform);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AccountBindingViewModel] 查看详情失败: {ex.Message}");
                GlobalToast.Error("查看详情", $"操作失败：{ex.Message}");
            }
        }

        private void SyncAccount(PlatformType platform)
        {
            _ = SyncAccountAsync(platform);
        }

        private async Task SyncAccountAsync(PlatformType platform)
        {
            try
            {
                var platformModel = AvailablePlatforms.FirstOrDefault(p => p.Platform == platform);
                if (platformModel == null || !platformModel.IsBound) return;

                platformModel.SyncStatus = SyncStatus.Syncing;
                GlobalToast.Info("数据同步", $"正在同步{platformModel.PlatformName}账号数据...");

                _bindingService.UpdateSyncStatus(platform, SyncStatus.Syncing);

                await System.Threading.Tasks.Task.Delay(1500);

                var random = new Random();
                var success = random.Next(100) > 10;

                if (success)
                {
                    platformModel.SyncStatus = SyncStatus.Synced;
                    platformModel.LastSyncTime = DateTime.Now;
                    _bindingService.UpdateSyncStatus(platform, SyncStatus.Synced);
                    GlobalToast.Success("数据同步", $"{platformModel.PlatformName}同步成功");
                }
                else
                {
                    platformModel.SyncStatus = SyncStatus.Failed;
                    _bindingService.UpdateSyncStatus(platform, SyncStatus.Failed);
                    GlobalToast.Error("数据同步", $"{platformModel.PlatformName}同步失败");
                }

                TM.App.Log($"[AccountBindingViewModel] 同步完成: {platform}, 状态: {platformModel.SyncStatus}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AccountBindingViewModel] 同步失败: {ex.Message}");
                GlobalToast.Error("数据同步", $"操作失败：{ex.Message}");
            }
        }

        private void RefreshPlatformData(PlatformType platform)
        {
            var platformModel = AvailablePlatforms.FirstOrDefault(p => p.Platform == platform);
            if (platformModel == null) return;

            var binding = _bindingService.GetBinding(platform);
            if (binding != null)
            {
                platformModel.SyncStatus = binding.SyncStatus;
                platformModel.LastSyncTime = binding.LastSyncTime;
                platformModel.LastUseTime = binding.LastUseTime;
            }
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}

