using System;
using System.Reflection;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.User.Services;

namespace TM.Framework.User.Account.AccountDeletion
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class AccountDeletionViewModel : INotifyPropertyChanged
    {
        private readonly ApiService _apiService;
        private readonly AuthTokenManager _tokenManager;

        #region 属性

        private string _confirmText = string.Empty;
        public string ConfirmText
        {
            get => _confirmText;
            set
            {
                _confirmText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanConfirm));
            }
        }

        public bool CanConfirm => ConfirmText == "确认注销";

        #endregion

        #region 命令

        public ICommand ConfirmDeletionCommand { get; }
        public ICommand CancelCommand { get; }

        #endregion

        public AccountDeletionViewModel(ApiService apiService, AuthTokenManager tokenManager)
        {
            _apiService = apiService;
            _tokenManager = tokenManager;
            ConfirmDeletionCommand = new RelayCommand(ConfirmDeletion);
            CancelCommand = new RelayCommand(Cancel);
        }

        private void ConfirmDeletion()
        {
            _ = ConfirmDeletionAsync();
        }

        private async Task ConfirmDeletionAsync()
        {
            if (!CanConfirm)
            {
                GlobalToast.Warning("提示", "请输入「确认注销」");
                return;
            }

            var result = StandardDialog.ShowConfirm("此操作不可撤销，确定要注销账号吗？", "确认注销");
            if (result != true) return;

            try
            {
                var apiResult = await _apiService.RequestDeletionAsync(new DeletionRequestDto
                {
                    Reasons = new System.Collections.Generic.List<string> { "用户主动注销" },
                    CustomFeedback = "",
                    RetainLoginHistory = false,
                    RetainThemes = false,
                    RetainSettings = false
                });

                if (apiResult.Success)
                {
                    TM.App.Log("[AccountDeletion] 账号注销成功");

                    _tokenManager.ClearTokens();

                    StandardDialog.ShowInfo("您的账号已成功注销，程序即将关闭。", "注销成功");

                    Application.Current.Shutdown();
                }
                else
                {
                    GlobalToast.Error("注销失败", apiResult.Message ?? "请稍后重试");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AccountDeletion] 注销异常: {ex.Message}");
                GlobalToast.Error("注销失败", ex.Message);
            }
        }

        private void Cancel()
        {
            ConfirmText = string.Empty;
            GlobalToast.Info("已取消", "注销操作已取消");
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
