using System;
using System.Collections.Generic;
using TM.Framework.Common.ViewModels;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;

namespace TM.Framework.SystemSettings.Info.SystemInfo
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class SystemInfoViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly SystemInfoSettings _infoSettings;

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (!_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            Debug.WriteLine($"[SystemInfo] {key}: {ex.Message}");
        }

        public SystemInfoViewModel(SystemInfoSettings infoSettings)
        {
            _infoSettings = infoSettings;
            DriveInfos = new RangeObservableCollection<DriveInfoItem>();
            NetworkAdapters = new RangeObservableCollection<NetworkAdapterItem>();
            GpuInfos = new RangeObservableCollection<GpuInfoItem>();
            MemoryModules = new RangeObservableCollection<MemoryModuleItem>();
            Displays = new RangeObservableCollection<DisplayInfoItem>();

            RefreshCommand = new RelayCommand(() => _ = RefreshAllInfoAsync());
            ExportCommand = new RelayCommand(() => ExportInfo().SafeFireAndForget(ex => TM.App.Log($"[SystemInfoViewModel] {ex.Message}")));

            _ = LoadDataAsync();
        }

        private async System.Threading.Tasks.Task LoadDataAsync()
        {
            try
            {
                TM.App.Log($"[SystemInfo] 开始异步加载数据");
                await System.Threading.Tasks.Task.Delay(500);
                await RefreshAllInfoAsync().ConfigureAwait(true);
                TM.App.Log($"[SystemInfo] 数据加载任务已完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemInfo] 异步加载数据失败: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task RefreshAllInfoAsync()
        {
            try
            {
                TM.App.Log($"[SystemInfo] 开始刷新系统信息");

                await System.Threading.Tasks.Task.WhenAll(
                    System.Threading.Tasks.Task.Run(CollectOSInfo),
                    System.Threading.Tasks.Task.Run(CollectCPUInfo),
                    System.Threading.Tasks.Task.Run(CollectMemoryInfo),
                    System.Threading.Tasks.Task.Run(CollectGpuInfo),
                    System.Threading.Tasks.Task.Run(CollectDiskInfo),
                    System.Threading.Tasks.Task.Run(CollectScreenInfo),
                    System.Threading.Tasks.Task.Run(CollectNetworkInfo),
                    System.Threading.Tasks.Task.Run(CollectMotherboardInfo)
                ).ConfigureAwait(true);

                _infoSettings.LastRefreshTime = DateTime.Now;
                await _infoSettings.SaveSettingsAsync().ConfigureAwait(true);
                TM.App.Log($"[SystemInfo] 系统信息刷新完成");
                GlobalToast.Success("刷新成功", "系统信息已更新");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SystemInfo] 刷新失败: {ex.Message}");
                GlobalToast.Error("刷新失败", $"刷新失败：{ex.Message}");
            }
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}

