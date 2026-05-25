using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.Common.Services;

namespace TM.Framework.User.Preferences.Display
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class DisplayViewModel : INotifyPropertyChanged
    {
        private readonly DisplayService _service;
        private readonly DisplaySettings _settings;

        #region 属性

        private double _uiScale = 1.0;
        public double UiScale
        {
            get => _uiScale;
            set
            {
                _uiScale = Math.Max(0.8, Math.Min(2.0, value));
                OnPropertyChanged();
                OnPropertyChanged(nameof(UiScalePercent));
                _service.UpdateUiScale(_uiScale);
            }
        }

        public string UiScalePercent => $"{(_uiScale * 100):F0}%";

        private bool _showFunctionBar = true;
        public bool ShowFunctionBar
        {
            get => _showFunctionBar;
            set
            {
                _showFunctionBar = value;
                OnPropertyChanged();
                _service.UpdateShowFunctionBar(value);
            }
        }

        private ListDensity _selectedDensity = ListDensity.Standard;
        public ListDensity SelectedDensity
        {
            get => _selectedDensity;
            set
            {
                _selectedDensity = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DensityDescription));
                _service.UpdateListDensity(value);
            }
        }

        public string DensityDescription
        {
            get
            {
                return SelectedDensity switch
                {
                    ListDensity.Compact => "紧凑 - 显示更多内容",
                    ListDensity.Comfortable => "宽松 - 更舒适的阅读体验",
                    _ => "标准 - 平衡的显示效果"
                };
            }
        }

        #endregion

        #region 命令

        public ICommand ResetCommand { get; }

        #endregion

        public DisplayViewModel(DisplayService service, DisplaySettings settings)
        {
            _service = service;
            _settings = settings;

            ResetCommand = new RelayCommand(ResetToDefaults);

            AsyncSettingsLoader.RunOrDefer(() =>
            {
                var s = _settings.LoadSettings();
                double scale = 1.0;
                try
                {
                    var resService = ServiceLocator.TryGet<Framework.Appearance.Animation.UIResolution.UIResolutionService>();
                    if (resService != null) scale = resService.LoadSettings().ScalePercent / 100.0;
                }
                catch { }
                return () =>
                {
                    _uiScale = scale;
                    _showFunctionBar = s.ShowFunctionBar;
                    _selectedDensity = s.ListDensity;
                    OnPropertyChanged(nameof(UiScale));
                    OnPropertyChanged(nameof(UiScalePercent));
                    OnPropertyChanged(nameof(ShowFunctionBar));
                    OnPropertyChanged(nameof(SelectedDensity));
                    OnPropertyChanged(nameof(DensityDescription));
                };
            }, "Display");
        }

        private void LoadSettings()
        {
            try
            {
                var settings = _settings.LoadSettings();

                try
                {
                    var resService = ServiceLocator.TryGet<Framework.Appearance.Animation.UIResolution.UIResolutionService>();
                    if (resService != null)
                    {
                        var resCfg = resService.LoadSettings();
                        _uiScale = resCfg.ScalePercent / 100.0;
                    }
                    else
                    {
                        _uiScale = 1.0;
                    }
                }
                catch { _uiScale = 1.0; }

                _showFunctionBar = settings.ShowFunctionBar;
                _selectedDensity = settings.ListDensity;

                OnPropertyChanged(nameof(UiScale));
                OnPropertyChanged(nameof(UiScalePercent));
                OnPropertyChanged(nameof(ShowFunctionBar));
                OnPropertyChanged(nameof(SelectedDensity));
                OnPropertyChanged(nameof(DensityDescription));

                TM.App.Log("[DisplayViewModel] 显示设置已加载");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DisplayViewModel] 加载设置失败: {ex.Message}");
                GlobalToast.Error("加载失败", "无法加载显示设置");
            }
        }

        private void ResetToDefaults()
        {
            try
            {
                var result = StandardDialog.ShowConfirm(
                    "确定要重置为默认显示设置吗？\n\n所有自定义设置将被清除。",
                    "重置确认"
                );

                if (result != true) return;

                _service.ResetToDefaults();
                LoadSettings();

                GlobalToast.Success("重置成功", "显示设置已恢复为默认值");
                TM.App.Log("[DisplayViewModel] 显示设置已重置");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DisplayViewModel] 重置失败: {ex.Message}");
                GlobalToast.Error("重置失败", ex.Message);
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

