using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Serialization;

namespace TM.Framework.SystemSettings.Logging.LogFormat
{
    [Flags]
    [Obfuscation(Exclude = true)]
    public enum FormatField
    {
        None = 0,
        Timestamp = 1,
        Level = 2,
        Message = 4,
        Caller = 8,
        ThreadId = 16,
        ProcessId = 32,
        Exception = 64
    }

    [Obfuscation(Exclude = true)]
    public enum FieldDataType
    {
        String,
        Integer,
        DateTime,
        Boolean,
        Double
    }

    [Obfuscation(Exclude = true)]
    public enum ValidationSeverity
    {
        Error,
        Warning,
        Info
    }

    public class CustomField
    {
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Placeholder")] public string Placeholder { get; set; } = string.Empty;
        [JsonPropertyName("DataType")] public FieldDataType DataType { get; set; } = FieldDataType.String;
        [JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
        [JsonPropertyName("DefaultValue")] public string DefaultValue { get; set; } = string.Empty;
        [JsonPropertyName("IsRequired")] public bool IsRequired { get; set; }
        [JsonPropertyName("CreatedTime")] public DateTime CreatedTime { get; set; } = DateTime.Now;
    }

    public class FormatTemplate
    {
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Template")] public string Template { get; set; } = string.Empty;
        [JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
        [JsonPropertyName("CreatedTime")] public DateTime CreatedTime { get; set; } = DateTime.Now;
        [JsonPropertyName("LastUsedTime")] public DateTime LastUsedTime { get; set; }
        [JsonPropertyName("UsageCount")] public int UsageCount { get; set; }
        [JsonPropertyName("IsFavorite")] public bool IsFavorite { get; set; }
        [JsonPropertyName("Tags")] public List<string> Tags { get; set; } = new List<string>();
    }

    public class ValidationResult
    {
        [JsonPropertyName("IsValid")] public bool IsValid { get; set; }
        [JsonPropertyName("Severity")] public ValidationSeverity Severity { get; set; }
        [JsonPropertyName("Message")] public string Message { get; set; } = string.Empty;
        [JsonPropertyName("Position")] public int Position { get; set; }
        [JsonPropertyName("Suggestion")] public string Suggestion { get; set; } = string.Empty;
    }

    [Obfuscation(Exclude = true)]
    public enum OutputFormatType
    {
        Text,
        JSON,
        XML
    }

    public class LogFormatSettings
    {
        [JsonPropertyName("FormatTemplate")] public string FormatTemplate { get; set; } = "[{timestamp}] [{level}] {message}";
        [JsonPropertyName("SelectedFields")] public FormatField SelectedFields { get; set; } = FormatField.Timestamp | FormatField.Level | FormatField.Message;
        [JsonPropertyName("TimestampFormat")] public string TimestampFormat { get; set; } = "yyyy-MM-dd HH:mm:ss.fff";
        [JsonPropertyName("FieldSeparator")] public string FieldSeparator { get; set; } = " | ";
        [JsonPropertyName("OutputFormat")] public OutputFormatType OutputFormat { get; set; } = OutputFormatType.Text;
        [JsonPropertyName("EnableFieldAlignment")] public bool EnableFieldAlignment { get; set; } = false;
        [JsonPropertyName("TimestampWidth")] public int TimestampWidth { get; set; } = 25;
        [JsonPropertyName("LevelWidth")] public int LevelWidth { get; set; } = 10;
        [JsonPropertyName("EnableMultilineIndent")] public bool EnableMultilineIndent { get; set; } = true;
        [JsonPropertyName("MultilineIndent")] public string MultilineIndent { get; set; } = "    ";
        [JsonPropertyName("CustomFields")] public List<CustomField> CustomFields { get; set; } = new List<CustomField>();
        [JsonPropertyName("SavedTemplates")] public List<FormatTemplate> SavedTemplates { get; set; } = new List<FormatTemplate>();

        public static Dictionary<string, string> PresetTemplates = new Dictionary<string, string>
        {
            { "简单", "[{level}] {message}" },
            { "详细", "[{timestamp}] [{level}] [{caller}] {message}" },
            { "紧凑", "{timestamp:HH:mm:ss} {level:short} {message}" },
            { "调试", "[{timestamp}] [{level}] [Thread:{threadid}] [{caller}] {message} {exception}" }
        };

        public static List<string> BuiltInFields = new List<string>
        {
            "timestamp", "level", "message", "caller", "threadid", "processid", "exception"
        };
    }
}

