using System;

namespace TM.Modules.AIAssistant.ModelIntegration.Alert.Models;

public enum AlertReason
{
    AnyError,
    ConsecutiveFailures,
    TaskAborted,
    Manual
}

public class AlertMessage
{
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public AlertReason Reason { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string ErrorMessage { get; set; } = string.Empty;

    public int ConsecutiveCount { get; set; }
}
