using System;
using NAudio.CoreAudioApi;

namespace TM.Framework.Notifications.Sound.VolumeAndDevice
{
    public class SystemVolumeController
    {
        private readonly MMDeviceEnumerator? _deviceEnumerator;
        private MMDevice? _defaultDevice;

        public SystemVolumeController()
        {
            try
            {
                _deviceEnumerator = new MMDeviceEnumerator();
                _defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                App.Log("[SystemVolumeController] 系统音量控制器初始化成功");
            }
            catch (Exception ex)
            {
                App.Log($"[SystemVolumeController] 初始化失败: {ex.Message}");
                _deviceEnumerator = null;
                _defaultDevice = null;
            }
        }

        public double GetMasterVolume()
        {
            try
            {
                if (_defaultDevice == null)
                {
                    App.Log("[SystemVolumeController] 默认设备未初始化");
                    return 80.0;
                }

                RefreshDefaultDevice();
                var volume = _defaultDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100;
                App.Log($"[SystemVolumeController] 当前系统音量: {volume:F0}%");
                return volume;
            }
            catch (Exception ex)
            {
                App.Log($"[SystemVolumeController] 获取主音量失败: {ex.Message}");
                return 80.0;
            }
        }

        public bool SetMasterVolume(double volume)
        {
            try
            {
                if (_defaultDevice == null)
                {
                    App.Log("[SystemVolumeController] 默认设备未初始化，无法设置音量");
                    return false;
                }

                RefreshDefaultDevice();

                volume = Math.Clamp(volume, 0, 100);

                float volumeLevel = (float)(volume / 100.0);
                _defaultDevice.AudioEndpointVolume.MasterVolumeLevelScalar = volumeLevel;

                App.Log($"[SystemVolumeController] 设置系统音量: {volume:F0}%");
                return true;
            }
            catch (Exception ex)
            {
                App.Log($"[SystemVolumeController] 设置主音量失败: {ex.Message}");
                return false;
            }
        }

        public bool IsMuted()
        {
            try
            {
                if (_defaultDevice == null)
                {
                    return false;
                }

                RefreshDefaultDevice();
                return _defaultDevice.AudioEndpointVolume.Mute;
            }
            catch (Exception ex)
            {
                App.Log($"[SystemVolumeController] 获取静音状态失败: {ex.Message}");
                return false;
            }
        }

        public bool SetMute(bool mute)
        {
            try
            {
                if (_defaultDevice == null)
                {
                    App.Log("[SystemVolumeController] 默认设备未初始化，无法设置静音");
                    return false;
                }

                RefreshDefaultDevice();
                _defaultDevice.AudioEndpointVolume.Mute = mute;

                App.Log($"[SystemVolumeController] 设置静音状态: {(mute ? "静音" : "取消静音")}");
                return true;
            }
            catch (Exception ex)
            {
                App.Log($"[SystemVolumeController] 设置静音状态失败: {ex.Message}");
                return false;
            }
        }

        public bool ToggleMute()
        {
            bool currentMute = IsMuted();
            return SetMute(!currentMute);
        }

        private void RefreshDefaultDevice()
        {
            try
            {
                if (_deviceEnumerator != null)
                {
                    _defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                }
            }
            catch (Exception ex)
            {
                App.Log($"[SystemVolumeController] 刷新默认设备失败: {ex.Message}");
            }
        }

        public (double min, double max) GetVolumeRange()
        {
            try
            {
                if (_defaultDevice == null)
                {
                    return (0, 100);
                }

                RefreshDefaultDevice();
                var volumeRange = _defaultDevice.AudioEndpointVolume.VolumeRange;

                App.Log($"[SystemVolumeController] 音量范围: {volumeRange.MinDecibels}dB - {volumeRange.MaxDecibels}dB");
                return (0, 100);
            }
            catch (Exception ex)
            {
                App.Log($"[SystemVolumeController] 获取音量范围失败: {ex.Message}");
                return (0, 100);
            }
        }
    }
}

