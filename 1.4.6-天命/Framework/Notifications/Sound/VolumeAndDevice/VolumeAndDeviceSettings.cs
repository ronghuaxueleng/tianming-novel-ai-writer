using System;
using System.Reflection;
using TM.Framework.Common.Services.Factories;

namespace TM.Framework.Notifications.Sound.VolumeAndDevice
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class VolumeAndDeviceSettingsData
    {
        [System.Text.Json.Serialization.JsonPropertyName("SystemVolume")] public double SystemVolume { get; set; } = 80;
        [System.Text.Json.Serialization.JsonPropertyName("NotificationVolume")] public double NotificationVolume { get; set; } = 100;
        [System.Text.Json.Serialization.JsonPropertyName("EffectVolume")] public double EffectVolume { get; set; } = 80;
        [System.Text.Json.Serialization.JsonPropertyName("IsMuted")] public bool IsMuted { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("BassLevel")] public double BassLevel { get; set; } = 0;
        [System.Text.Json.Serialization.JsonPropertyName("MidBassLevel")] public double MidBassLevel { get; set; } = 0;
        [System.Text.Json.Serialization.JsonPropertyName("MidLevel")] public double MidLevel { get; set; } = 0;
        [System.Text.Json.Serialization.JsonPropertyName("MidTrebleLevel")] public double MidTrebleLevel { get; set; } = 0;
        [System.Text.Json.Serialization.JsonPropertyName("TrebleLevel")] public double TrebleLevel { get; set; } = 0;
        [System.Text.Json.Serialization.JsonPropertyName("EqualizerPreset")] public string EqualizerPreset { get; set; } = "默认";
        [System.Text.Json.Serialization.JsonPropertyName("OutputDeviceId")] public string? OutputDeviceId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("InputDeviceId")] public string? InputDeviceId { get; set; }
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class VolumeAndDeviceSettings : BaseSettings<VolumeAndDeviceSettings, VolumeAndDeviceSettingsData>
    {
        public VolumeAndDeviceSettings(IStoragePathHelper storagePathHelper, IObjectFactory objectFactory)
            : base(storagePathHelper, objectFactory) { }

        protected override string GetFilePath() =>
            _storagePathHelper.GetFilePath("Framework", "Notifications/Sound/VolumeAndDevice", "settings.json");

        protected override VolumeAndDeviceSettingsData CreateDefaultData() => _objectFactory.Create<VolumeAndDeviceSettingsData>();

        public double SystemVolume { get => Data.SystemVolume; set { Data.SystemVolume = value; OnPropertyChanged(); } }
        public double NotificationVolume { get => Data.NotificationVolume; set { Data.NotificationVolume = value; OnPropertyChanged(); } }
        public double EffectVolume { get => Data.EffectVolume; set { Data.EffectVolume = value; OnPropertyChanged(); } }
        public bool IsMuted { get => Data.IsMuted; set { Data.IsMuted = value; OnPropertyChanged(); } }
        public double BassLevel { get => Data.BassLevel; set { Data.BassLevel = value; OnPropertyChanged(); } }
        public double MidBassLevel { get => Data.MidBassLevel; set { Data.MidBassLevel = value; OnPropertyChanged(); } }
        public double MidLevel { get => Data.MidLevel; set { Data.MidLevel = value; OnPropertyChanged(); } }
        public double MidTrebleLevel { get => Data.MidTrebleLevel; set { Data.MidTrebleLevel = value; OnPropertyChanged(); } }
        public double TrebleLevel { get => Data.TrebleLevel; set { Data.TrebleLevel = value; OnPropertyChanged(); } }
        public string EqualizerPreset { get => Data.EqualizerPreset; set { Data.EqualizerPreset = value; OnPropertyChanged(); } }
        public string? OutputDeviceId { get => Data.OutputDeviceId; set { Data.OutputDeviceId = value; OnPropertyChanged(); } }
        public string? InputDeviceId { get => Data.InputDeviceId; set { Data.InputDeviceId = value; OnPropertyChanged(); } }

        public void LoadSettings() => LoadData();
        public void SaveSettings() => SaveData();
    }
}

