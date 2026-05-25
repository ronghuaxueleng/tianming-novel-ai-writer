using System.Threading;
using System.Threading.Tasks;
using TM.Modules.AIAssistant.ModelIntegration.Alert.Models;

namespace TM.Modules.AIAssistant.ModelIntegration.Alert.Services;

public interface INotificationChannel
{
    string ChannelName { get; }

    bool IsEnabled(AlertConfig config);

    Task<NotificationResult> SendAsync(AlertConfig config, AlertMessage message, CancellationToken ct);
}

public class NotificationResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;

    public static NotificationResult Ok() => new() { Success = true };
    public static NotificationResult Fail(string err) => new() { Success = false, ErrorMessage = err };
}
