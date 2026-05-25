using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Framework.User.Services;

namespace TM.Framework.User.Profile.Subscription
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class SubscriptionViewModel : INotifyPropertyChanged
    {
        private readonly SubscriptionService _subscriptionService;

        public SubscriptionViewModel(SubscriptionService subscriptionService)
        {
            _subscriptionService = subscriptionService;

            RefreshCommand = new SubscriptionRelayCommand(async () => await RefreshAsync());
            ActivateCardKeyCommand = new SubscriptionRelayCommand(async () => await ActivateCardKeyAsync());
            UpgradeCommand = new SubscriptionRelayCommand(async () => await UpgradeAsync());

        }

        #region 属性

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        private string _planType = "free";
        public string PlanType
        {
            get => _planType;
            set { _planType = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlanTypeDisplay)); OnPropertyChanged(nameof(UpgradeButtonText)); }
        }

        public string PlanTypeDisplay => PlanType switch
        {
            "free" => "免费版",
            "basic" => "基础版",
            "pro" => "专业版",
            "enterprise" => "企业版",
            _ => "免费版"
        };

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(StatusColor)); }
        }

        public string StatusText => IsActive ? "已激活" : "未激活";
        public string StatusColor => IsActive ? "#10B981" : "#6B7280";

        private int _remainingDays;
        public int RemainingDays
        {
            get => _remainingDays;
            set { _remainingDays = value; OnPropertyChanged(); OnPropertyChanged(nameof(RemainingDaysText)); }
        }

        public string RemainingDaysText => RemainingDays > 0 ? $"剩余 {RemainingDays} 天" : "已过期";

        private DateTime? _endTime;
        public DateTime? EndTime
        {
            get => _endTime;
            set { _endTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(EndTimeText)); }
        }

        public string EndTimeText => EndTime.HasValue ? EndTime.Value.ToString("yyyy-MM-dd") : "-";

        private string _cardKey = string.Empty;
        public string CardKey
        {
            get => _cardKey;
            set { _cardKey = value; OnPropertyChanged(); }
        }

        private bool _isActivating;
        public bool IsActivating
        {
            get => _isActivating;
            set { _isActivating = value; OnPropertyChanged(); }
        }

        private ObservableCollection<ActivationHistoryItem> _activationHistory = new();
        public ObservableCollection<ActivationHistoryItem> ActivationHistory
        {
            get => _activationHistory;
            set { _activationHistory = value; OnPropertyChanged(); }
        }

        public string UpgradeButtonText => PlanType switch
        {
            "free" => "立即升级",
            "basic" => "升级专业版",
            "pro" => "续费",
            _ => "续费"
        };

        #endregion

        #region 命令

        public ICommand RefreshCommand { get; }
        public ICommand ActivateCardKeyCommand { get; }
        public ICommand UpgradeCommand { get; }

        #endregion

        public async Task RefreshAsync()
        {
            IsLoading = true;

            try
            {
                var subscription = await _subscriptionService.GetSubscriptionFromServerAsync();
                if (subscription != null)
                {
                    PlanType = subscription.PlanType ?? "free";
                    IsActive = subscription.IsActive;
                    EndTime = subscription.EndTime;
                    RemainingDays = _subscriptionService.RemainingDays;
                }

                var history = await _subscriptionService.GetActivationHistoryAsync();
                ActivationHistory.Clear();
                foreach (var item in history)
                {
                    ActivationHistory.Add(item);
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SubscriptionViewModel] 刷新失败: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task UpgradeAsync()
        {
            await Task.CompletedTask;

            var message = PlanType switch
            {
                "free" => "升级会员可解锁更多功能！\n\n请通过以下方式获取会员：\n• 联系客服购买\n• 使用卡密激活",
                "basic" => "升级专业版可获得无限AI额度和高级模板！\n\n请联系客服了解升级详情。",
                _ => "续费会员可延长使用期限！\n\n请联系客服或使用卡密续费。"
            };

            StandardDialog.ShowInfo("升级/续费", message);
            TM.App.Log($"[SubscriptionViewModel] 用户点击升级/续费，当前套餐: {PlanType}");
        }

        public async Task ActivateCardKeyAsync()
        {
            if (string.IsNullOrWhiteSpace(CardKey))
            {
                GlobalToast.Warning("续费卡密", "请输入卡密");
                return;
            }

            IsActivating = true;

            try
            {
                var result = await _subscriptionService.ActivateCardKeyAsync(CardKey);
                if (result.Success)
                {
                    GlobalToast.Success("续费成功", result.Message ?? $"已增加{result.DaysAdded}天会员时长");
                    CardKey = string.Empty;

                    await RefreshAsync();
                }
                else
                {
                    GlobalToast.Error("续费失败", result.ErrorMessage ?? "卡密无效");
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SubscriptionViewModel] 续费卡密失败: {ex.Message}");
                GlobalToast.Error("续费失败", $"操作失败: {ex.Message}");
            }
            finally
            {
                IsActivating = false;
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

    #region RelayCommand

    public class SubscriptionRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool>? _canExecute;
        private bool _isExecuting;

        public SubscriptionRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

        public void Execute(object? parameter)
        {
            if (_isExecuting) return;
            _isExecuting = true;

            _ = ExecuteAsync();
        }

        private async Task ExecuteAsync()
        {
            try
            {
                await _execute();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SubscriptionRelayCommand] 执行失败: {ex.Message}");
            }
            finally
            {
                _isExecuting = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    #endregion

    #region 转换器
    public class BoolToActivatingTextConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value is bool isActivating && isActivating ? "续费中..." : "续费";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public class ZeroToVisibleConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is int count)
            {
                return count == 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            }
            return System.Windows.Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    #endregion
}
