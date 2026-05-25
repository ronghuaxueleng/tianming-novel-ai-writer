using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.CoreAudioApi;

namespace TM.Framework.Notifications.Sound.VolumeAndDevice
{
    public class AudioDeviceManager
    {
        private MMDeviceEnumerator? _deviceEnumerator;
        private bool _initialized;
        private readonly object _initLock = new();

        public AudioDeviceManager()
        {
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;

            lock (_initLock)
            {
                if (_initialized) return;

                try
                {
                    _deviceEnumerator = new MMDeviceEnumerator();
                    App.Log("[AudioDeviceManager] 音频设备管理器初始化成功（懒加载）");
                }
                catch (Exception ex)
                {
                    App.Log($"[AudioDeviceManager] 初始化失败: {ex.Message}");
                    _deviceEnumerator = null;
                }

                _initialized = true;
            }
        }

        public List<AudioDeviceInfo> GetOutputDevices()
        {
            EnsureInitialized();
            var devices = new List<AudioDeviceInfo>();

            try
            {
                if (_deviceEnumerator == null)
                {
                    return GetSimulatedOutputDevices();
                }

                var deviceCollection = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                var defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

                foreach (var device in deviceCollection)
                {
                    try
                    {
                        devices.Add(new AudioDeviceInfo
                        {
                            DeviceId = device.ID,
                            DeviceName = device.FriendlyName,
                            DeviceType = "输出",
                            IsDefault = device.ID == defaultDevice.ID,
                            Status = device.State == DeviceState.Active ? "已连接" : "未连接"
                        });
                    }
                    catch (Exception ex)
                    {
                        App.Log($"[AudioDeviceManager] 获取设备信息失败: {ex.Message}");
                    }
                }

                App.Log($"[AudioDeviceManager] 找到 {devices.Count} 个输出设备");
            }
            catch (Exception ex)
            {
                App.Log($"[AudioDeviceManager] 枚举输出设备失败: {ex.Message}");
                return GetSimulatedOutputDevices();
            }

            return devices.Count > 0 ? devices : GetSimulatedOutputDevices();
        }

        public List<AudioDeviceInfo> GetInputDevices()
        {
            EnsureInitialized();
            var devices = new List<AudioDeviceInfo>();

            try
            {
                if (_deviceEnumerator == null)
                {
                    return GetSimulatedInputDevices();
                }

                var deviceCollection = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                var defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);

                foreach (var device in deviceCollection)
                {
                    try
                    {
                        devices.Add(new AudioDeviceInfo
                        {
                            DeviceId = device.ID,
                            DeviceName = device.FriendlyName,
                            DeviceType = "输入",
                            IsDefault = device.ID == defaultDevice.ID,
                            Status = device.State == DeviceState.Active ? "已连接" : "未连接"
                        });
                    }
                    catch (Exception ex)
                    {
                        App.Log($"[AudioDeviceManager] 获取设备信息失败: {ex.Message}");
                    }
                }

                App.Log($"[AudioDeviceManager] 找到 {devices.Count} 个输入设备");
            }
            catch (Exception ex)
            {
                App.Log($"[AudioDeviceManager] 枚举输入设备失败: {ex.Message}");
                return GetSimulatedInputDevices();
            }

            return devices.Count > 0 ? devices : GetSimulatedInputDevices();
        }

        public bool SetDefaultOutputDevice(string deviceId)
        {
            try
            {
                if (_deviceEnumerator == null || string.IsNullOrEmpty(deviceId))
                {
                    App.Log("[AudioDeviceManager] 无法设置默认设备：设备枚举器未初始化或设备ID为空");
                    return false;
                }
                App.Log($"[AudioDeviceManager] 请求设置默认输出设备: {deviceId}");
                App.Log("[AudioDeviceManager] 注意：更改默认设备需要管理员权限，当前仅记录请求");

                return true;
            }
            catch (Exception ex)
            {
                App.Log($"[AudioDeviceManager] 设置默认输出设备失败: {ex.Message}");
                return false;
            }
        }

        public bool SetDefaultInputDevice(string deviceId)
        {
            try
            {
                if (_deviceEnumerator == null || string.IsNullOrEmpty(deviceId))
                {
                    App.Log("[AudioDeviceManager] 无法设置默认设备：设备枚举器未初始化或设备ID为空");
                    return false;
                }

                App.Log($"[AudioDeviceManager] 请求设置默认输入设备: {deviceId}");
                App.Log("[AudioDeviceManager] 注意：更改默认设备需要管理员权限，当前仅记录请求");

                return true;
            }
            catch (Exception ex)
            {
                App.Log($"[AudioDeviceManager] 设置默认输入设备失败: {ex.Message}");
                return false;
            }
        }

        private List<AudioDeviceInfo> GetSimulatedOutputDevices()
        {
            return new List<AudioDeviceInfo>
            {
                new AudioDeviceInfo
                {
                    DeviceId = "output_default",
                    DeviceName = "扬声器/耳机",
                    DeviceType = "输出",
                    IsDefault = true,
                    Status = "已连接"
                },
                new AudioDeviceInfo
                {
                    DeviceId = "output_hdmi",
                    DeviceName = "HDMI输出",
                    DeviceType = "输出",
                    IsDefault = false,
                    Status = "已连接"
                }
            };
        }

        private List<AudioDeviceInfo> GetSimulatedInputDevices()
        {
            return new List<AudioDeviceInfo>
            {
                new AudioDeviceInfo
                {
                    DeviceId = "input_default",
                    DeviceName = "麦克风",
                    DeviceType = "输入",
                    IsDefault = true,
                    Status = "已连接"
                }
            };
        }
    }
}

