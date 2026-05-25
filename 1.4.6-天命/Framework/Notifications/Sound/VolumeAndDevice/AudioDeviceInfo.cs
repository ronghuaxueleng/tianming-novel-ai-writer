using System;

namespace TM.Framework.Notifications.Sound.VolumeAndDevice
{
    public class AudioDeviceInfo
    {
        public string DeviceId { get; set; } = string.Empty;

        public string DeviceName { get; set; } = string.Empty;

        public string DeviceType { get; set; } = string.Empty;

        public bool IsDefault { get; set; }

        public string Status { get; set; } = "已连接";

        public string DisplayName => IsDefault ? $"{DeviceName} (默认)" : DeviceName;
    }
}

