using System;
using System.Collections.Generic;
using TM.Framework.Common.Services.Factories;

namespace TM.Framework.Appearance.IntelligentGeneration.AIColorScheme
{
    public class AIColorSchemeSettings : BaseSettings<AIColorSchemeSettings, AIColorSchemeData>
    {
        public AIColorSchemeSettings(IStoragePathHelper storagePathHelper, IObjectFactory objectFactory)
            : base(storagePathHelper, objectFactory) { }

        protected override string GetFilePath() =>
            _storagePathHelper.GetFilePath("Framework", "Appearance/IntelligentGeneration/AIColorScheme", "ai_color_scheme_data.json");

        protected override AIColorSchemeData CreateDefaultData() => _objectFactory.Create<AIColorSchemeData>();

        private readonly object _lock = new object();

        public AIColorSchemeData GetData() { lock (_lock) { return Data; } }
        public new AIColorSchemeData LoadData() { lock (_lock) { base.LoadData(); return Data; } }
        public void SaveData(AIColorSchemeData data) { lock (_lock) { Data = data; base.SaveData(); } }

        public void UpdateUserConfig(AIColorSchemeUserConfig config)
        {
            lock (_lock) { Data.UserConfig = config; SaveData(); }
        }

        public void AddGenerationHistory(AIColorSchemeHistoryRecord record)
        {
            lock (_lock)
            {
                if (Data.GenerationHistory == null) Data.GenerationHistory = new List<AIColorSchemeHistoryRecord>();
                Data.GenerationHistory.Add(record);
                if (Data.GenerationHistory.Count > 100)
                {
                    var removeCount = Data.GenerationHistory.Count - 100;
                    Data.GenerationHistory.RemoveRange(0, removeCount);
                }
                SaveData();
            }
        }
    }

    public class AIColorSchemeData
    {
        [System.Text.Json.Serialization.JsonPropertyName("UserConfig")] public AIColorSchemeUserConfig UserConfig { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("GenerationHistory")] public List<AIColorSchemeHistoryRecord> GenerationHistory { get; set; } = new();
    }

    public class AIColorSchemeUserConfig
    {
        [System.Text.Json.Serialization.JsonPropertyName("LastColorHarmony")] public string LastColorHarmony { get; set; } = "互补色";
        [System.Text.Json.Serialization.JsonPropertyName("LastThemeType")] public string LastThemeType { get; set; } = "浅色主题";
        [System.Text.Json.Serialization.JsonPropertyName("LastEmotion")] public string LastEmotion { get; set; } = "无";
        [System.Text.Json.Serialization.JsonPropertyName("LastScene")] public string LastScene { get; set; } = "通用";
        [System.Text.Json.Serialization.JsonPropertyName("LastKeywords")] public string LastKeywords { get; set; } = string.Empty;
    }

    public class AIColorSchemeHistoryRecord
    {
        [System.Text.Json.Serialization.JsonPropertyName("SchemeName")] public string SchemeName { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Keywords")] public string Keywords { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Harmony")] public string Harmony { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("ThemeType")] public string ThemeType { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Emotion")] public string Emotion { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Scene")] public string Scene { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("PrimaryColorHex")] public string PrimaryColorHex { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("SecondaryColorHex")] public string SecondaryColorHex { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("AccentColorHex")] public string AccentColorHex { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("BackgroundColorHex")] public string BackgroundColorHex { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("TextColorHex")] public string TextColorHex { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Score")] public int Score { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("GeneratedTime")] public DateTime GeneratedTime { get; set; }
    }
}
