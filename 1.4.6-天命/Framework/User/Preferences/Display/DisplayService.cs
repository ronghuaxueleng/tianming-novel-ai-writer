using System;
using TM.Framework.Common.Services;
using TM.Framework.Appearance.Animation.UIResolution;

namespace TM.Framework.User.Preferences.Display
{
    public class DisplayService
    {
        private readonly DisplaySettings _settings;

        private DisplaySettings Settings => _settings;

        public DisplayService(DisplaySettings settings)
        {
            _settings = settings;
        }

        public void UpdateUiScale(double scale)
        {
            scale = Math.Max(0.8, Math.Min(2.0, scale));
            var scalePercent = (int)Math.Round(scale * 100.0);

            try
            {
                var resService = ServiceLocator.TryGet<UIResolutionService>();
                if (resService != null)
                {
                    var resCfg = resService.LoadSettings();
                    resCfg.ScalePercent = scalePercent;
                    resService.SaveSettings(resCfg);
                    resService.ApplyUIScale(scalePercent);
                }
            }
            catch { }

            TM.App.Log($"[DisplayService] 更新界面缩放: {scalePercent}%");
        }

        public void UpdateShowFunctionBar(bool show)
        {
            var loadedSettings = Settings.LoadSettings();
            loadedSettings.ShowFunctionBar = show;
            Settings.SaveSettings(loadedSettings);
            TM.App.Log($"[DisplayService] 更新功能栏显示状态: {show}");
        }

        public void UpdateListDensity(ListDensity density)
        {
            var loadedSettings = Settings.LoadSettings();
            loadedSettings.ListDensity = density;
            Settings.SaveSettings(loadedSettings);
            TM.App.Log($"[DisplayService] 更新列表密度: {density}");
        }

        public void ResetToDefaults()
        {
            Settings.ResetToDefaults();

            try
            {
                var resService = ServiceLocator.TryGet<UIResolutionService>();
                if (resService != null)
                {
                    var resCfg = resService.LoadSettings();
                    resCfg.ScalePercent = 100;
                    resService.SaveSettings(resCfg);
                    resService.ApplyUIScale(100);
                }
            }
            catch { }

            TM.App.Log("[DisplayService] 重置为默认显示设置");
        }
    }
}

