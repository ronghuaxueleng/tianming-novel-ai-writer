using System;
using System.Text.Json.Serialization;

namespace TM.Framework.User.Preferences.Locale
{
    public class LocaleModel
    {
        [JsonPropertyName("Language")] public string Language { get; set; } = "zh-CN";
        [JsonPropertyName("LanguageName")] public string LanguageName { get; set; } = "简体中文";
        [JsonPropertyName("TimeZoneId")] public string TimeZoneId { get; set; } = "China Standard Time";
        [JsonPropertyName("DateFormat")] public string DateFormat { get; set; } = "yyyy-MM-dd";
        [JsonPropertyName("TimeFormat")] public string TimeFormat { get; set; } = "HH:mm:ss";
        [JsonPropertyName("NumberFormat")] public string NumberFormat { get; set; } = "1,234.56";
        [JsonPropertyName("CurrencySymbol")] public string CurrencySymbol { get; set; } = "¥";
        [JsonPropertyName("Use24HourFormat")] public bool Use24HourFormat { get; set; } = true;
        [JsonPropertyName("WeekStartDay")] public int WeekStartDay { get; set; } = 1;
        [JsonPropertyName("LastModified")] public DateTime LastModified { get; set; } = DateTime.Now;
    }
}

