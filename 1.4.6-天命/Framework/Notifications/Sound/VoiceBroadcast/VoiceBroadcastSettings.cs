using System;
using System.Reflection;
using TM.Framework.Common.Services.Factories;

namespace TM.Framework.Notifications.Sound.VoiceBroadcast
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class VoiceBroadcastSettingsData
    {
        [System.Text.Json.Serialization.JsonPropertyName("IsEnabled")] public bool IsEnabled { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("Speed")] public int Speed { get; set; } = 0;
        [System.Text.Json.Serialization.JsonPropertyName("Volume")] public int Volume { get; set; } = 100;
        [System.Text.Json.Serialization.JsonPropertyName("Pitch")] public int Pitch { get; set; } = 0;
        [System.Text.Json.Serialization.JsonPropertyName("TestText")] public string TestText { get; set; } = "这是一条测试语音播报";
        [System.Text.Json.Serialization.JsonPropertyName("BroadcastOnNotification")] public bool BroadcastOnNotification { get; set; } = true;
        [System.Text.Json.Serialization.JsonPropertyName("BroadcastOnError")] public bool BroadcastOnError { get; set; } = true;
        [System.Text.Json.Serialization.JsonPropertyName("BroadcastOnSuccess")] public bool BroadcastOnSuccess { get; set; } = true;
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class VoiceBroadcastSettings : BaseSettings<VoiceBroadcastSettings, VoiceBroadcastSettingsData>
    {
        public VoiceBroadcastSettings(IStoragePathHelper storagePathHelper, IObjectFactory objectFactory)
            : base(storagePathHelper, objectFactory) { }

        protected override string GetFilePath() =>
            _storagePathHelper.GetFilePath("Framework", "Notifications/Sound/VoiceBroadcast", "settings.json");

        protected override VoiceBroadcastSettingsData CreateDefaultData() => _objectFactory.Create<VoiceBroadcastSettingsData>();

        public bool IsEnabled { get => Data.IsEnabled; set { Data.IsEnabled = value; OnPropertyChanged(); } }
        public int Speed { get => Data.Speed; set { Data.Speed = value; OnPropertyChanged(); } }
        public int Volume { get => Data.Volume; set { Data.Volume = value; OnPropertyChanged(); } }
        public int Pitch { get => Data.Pitch; set { Data.Pitch = value; OnPropertyChanged(); } }
        public string TestText { get => Data.TestText; set { Data.TestText = value; OnPropertyChanged(); } }
        public bool BroadcastOnNotification { get => Data.BroadcastOnNotification; set { Data.BroadcastOnNotification = value; OnPropertyChanged(); } }
        public bool BroadcastOnError { get => Data.BroadcastOnError; set { Data.BroadcastOnError = value; OnPropertyChanged(); } }
        public bool BroadcastOnSuccess { get => Data.BroadcastOnSuccess; set { Data.BroadcastOnSuccess = value; OnPropertyChanged(); } }

        public void LoadSettings() => LoadData();
        public void SaveSettings() => SaveData();
    }
}

