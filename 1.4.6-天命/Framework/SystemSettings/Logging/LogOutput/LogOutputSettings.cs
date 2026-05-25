using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Serialization;

namespace TM.Framework.SystemSettings.Logging.LogOutput
{
    [Obfuscation(Exclude = true)]
    public enum OutputTargetType
    {
        File,
        Console,
        EventLog,
        RemoteHttp,
        RemoteTcp
    }

    [Obfuscation(Exclude = true)]
    public enum FileEncodingType
    {
        UTF8,
        UTF8BOM,
        ASCII,
        Unicode
    }

    public class OutputTarget
    {
        [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("Type")] public OutputTargetType Type { get; set; }
        [JsonPropertyName("IsEnabled")] public bool IsEnabled { get; set; } = true;
        [JsonPropertyName("Priority")] public int Priority { get; set; } = 0;
        [JsonPropertyName("Settings")] public Dictionary<string, string> Settings { get; set; } = new Dictionary<string, string>();
    }

    [Obfuscation(Exclude = true)]
    public enum TestStatus
    {
        Success,
        Failed,
        Timeout,
        NotTested
    }

    public class TestResult
    {
        [JsonPropertyName("TargetName")] public string TargetName { get; set; } = string.Empty;
        [JsonPropertyName("TargetType")] public OutputTargetType TargetType { get; set; }
        [JsonPropertyName("Status")] public TestStatus Status { get; set; }
        [JsonPropertyName("TestTime")] public DateTime TestTime { get; set; }
        [JsonPropertyName("ResponseTime")] public long ResponseTime { get; set; }
        [JsonPropertyName("Message")] public string Message { get; set; } = string.Empty;
        [JsonPropertyName("Details")] public string Details { get; set; } = string.Empty;
    }

    public class OutputStatistics
    {
        [JsonPropertyName("TargetName")] public string TargetName { get; set; } = string.Empty;
        [JsonPropertyName("TargetType")] public OutputTargetType TargetType { get; set; }
        [JsonPropertyName("TotalAttempts")] public int TotalAttempts { get; set; }
        [JsonPropertyName("SuccessCount")] public int SuccessCount { get; set; }
        [JsonPropertyName("FailureCount")] public int FailureCount { get; set; }
        public double SuccessRate => TotalAttempts > 0 ? (SuccessCount * 100.0 / TotalAttempts) : 0;
        [JsonPropertyName("AverageResponseTime")] public long AverageResponseTime { get; set; }
        [JsonPropertyName("TotalBytes")] public long TotalBytes { get; set; }
        [JsonPropertyName("LastUpdateTime")] public DateTime LastUpdateTime { get; set; }
        public string StatusIcon => SuccessRate >= 90 ? "✅" : SuccessRate >= 70 ? "⚠️" : "❌";
    }

    public class RetryConfig
    {
        [JsonPropertyName("EnableRetry")] public bool EnableRetry { get; set; } = true;
        [JsonPropertyName("MaxRetryAttempts")] public int MaxRetryAttempts { get; set; } = 3;
        [JsonPropertyName("RetryIntervalMs")] public int RetryIntervalMs { get; set; } = 1000;
        [JsonPropertyName("EnableExponentialBackoff")] public bool EnableExponentialBackoff { get; set; } = true;
        [JsonPropertyName("MaxRetryIntervalMs")] public int MaxRetryIntervalMs { get; set; } = 10000;
    }

    public class FailureRecord
    {
        [JsonPropertyName("FailureTime")] public DateTime FailureTime { get; set; }
        [JsonPropertyName("TargetName")] public string TargetName { get; set; } = string.Empty;
        [JsonPropertyName("TargetType")] public OutputTargetType TargetType { get; set; }
        [JsonPropertyName("ErrorMessage")] public string ErrorMessage { get; set; } = string.Empty;
        [JsonPropertyName("RetryAttempts")] public int RetryAttempts { get; set; }
        [JsonPropertyName("IsResolved")] public bool IsResolved { get; set; }
        [JsonPropertyName("LogContent")] public string LogContent { get; set; } = string.Empty;
    }

    public class LogOutputSettings
    {
        [JsonPropertyName("EnableFileOutput")] public bool EnableFileOutput { get; set; } = true;
        [JsonPropertyName("FileOutputPath")] public string FileOutputPath { get; set; } = "Logs/application.log";
        [JsonPropertyName("FileNamingPattern")] public string FileNamingPattern { get; set; } = "{date}_{appname}.log";
        [JsonPropertyName("FileEncoding")] public FileEncodingType FileEncoding { get; set; } = FileEncodingType.UTF8;
        [JsonPropertyName("EnableConsoleOutput")] public bool EnableConsoleOutput { get; set; } = true;
        [JsonPropertyName("ConsoleColorCoding")] public bool ConsoleColorCoding { get; set; } = true;
        [JsonPropertyName("EnableEventLog")] public bool EnableEventLog { get; set; } = false;
        [JsonPropertyName("EventLogSource")] public string EventLogSource { get; set; } = "TM";
        [JsonPropertyName("EnableRemoteOutput")] public bool EnableRemoteOutput { get; set; } = false;
        [JsonPropertyName("RemoteProtocol")] public string RemoteProtocol { get; set; } = "HTTP";
        [JsonPropertyName("RemoteAddress")] public string RemoteAddress { get; set; } = "http://localhost:8080/log";
        [JsonPropertyName("BufferSize")] public int BufferSize { get; set; } = 1024;
        [JsonPropertyName("EnableAsyncOutput")] public bool EnableAsyncOutput { get; set; } = true;
        [JsonPropertyName("OutputTargets")] public List<OutputTarget> OutputTargets { get; set; } = new List<OutputTarget>();
        [JsonPropertyName("RetryConfiguration")] public RetryConfig RetryConfiguration { get; set; } = new RetryConfig();
    }
}

