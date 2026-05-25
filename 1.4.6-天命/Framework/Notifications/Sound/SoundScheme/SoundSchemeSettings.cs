using System;
using System.Reflection;
using System.Collections.Generic;
using TM.Framework.Common.Services.Factories;

namespace TM.Framework.Notifications.Sound.SoundScheme
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class SoundSchemeSettingsData
    {
        [System.Text.Json.Serialization.JsonPropertyName("ActiveSchemeId")] public string ActiveSchemeId { get; set; } = "default";
        [System.Text.Json.Serialization.JsonPropertyName("EventSoundMappings")] public Dictionary<string, string> EventSoundMappings { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("CustomSoundFiles")] public List<string> CustomSoundFiles { get; set; } = new();
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class SoundSchemeSettings : BaseSettings<SoundSchemeSettings, SoundSchemeSettingsData>
    {
        public SoundSchemeSettings(IStoragePathHelper storagePathHelper, IObjectFactory objectFactory)
            : base(storagePathHelper, objectFactory) { }

        protected override string GetFilePath() =>
            _storagePathHelper.GetFilePath("Framework", "Notifications/Sound/SoundScheme", "settings.json");

        protected override SoundSchemeSettingsData CreateDefaultData() => _objectFactory.Create<SoundSchemeSettingsData>();

        public string ActiveSchemeId { get => Data.ActiveSchemeId; set { Data.ActiveSchemeId = value; OnPropertyChanged(); } }
        public Dictionary<string, string> EventSoundMappings { get => Data.EventSoundMappings; set { Data.EventSoundMappings = value; OnPropertyChanged(); } }
        public List<string> CustomSoundFiles { get => Data.CustomSoundFiles; set { Data.CustomSoundFiles = value; OnPropertyChanged(); } }

        public void LoadSettings() => LoadData();
        public void SaveSettings() => SaveData();
    }
}

