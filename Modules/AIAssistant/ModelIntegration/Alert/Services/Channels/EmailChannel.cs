using System;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using TM.Modules.AIAssistant.ModelIntegration.Alert.Models;

namespace TM.Modules.AIAssistant.ModelIntegration.Alert.Services.Channels;

public class EmailChannel : INotificationChannel
{
    public string ChannelName => "邮件";

    public bool IsEnabled(AlertConfig config)
    {
        if (config?.Email == null) return false;
        var e = config.Email;
        return e.Enabled
               && !string.IsNullOrWhiteSpace(e.SmtpHost)
               && e.SmtpPort > 0
               && !string.IsNullOrWhiteSpace(e.SenderEmail)
               && !string.IsNullOrWhiteSpace(e.AuthCode)
               && e.Recipients != null
               && e.Recipients.Count > 0;
    }

    public async Task<NotificationResult> SendAsync(AlertConfig config, AlertMessage message, CancellationToken ct)
    {
        try
        {
            if (!IsEnabled(config))
            {
                return NotificationResult.Fail("邮件渠道未启用或配置不完整");
            }

            var e = config.Email;

            using var smtp = new SmtpClient(e.SmtpHost, e.SmtpPort)
            {
                EnableSsl = e.EnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(e.SenderEmail, e.AuthCode),
                Timeout = 30000
            };

            using var mail = new MailMessage
            {
                From = new MailAddress(e.SenderEmail, string.IsNullOrWhiteSpace(e.SenderDisplayName) ? e.SenderEmail : e.SenderDisplayName),
                Subject = string.IsNullOrWhiteSpace(message.Title) ? "[天命] AI告警" : message.Title,
                Body = message.Body ?? string.Empty,
                IsBodyHtml = false,
                BodyEncoding = System.Text.Encoding.UTF8,
                SubjectEncoding = System.Text.Encoding.UTF8
            };

            foreach (var recipient in e.Recipients)
            {
                if (string.IsNullOrWhiteSpace(recipient)) continue;
                try { mail.To.Add(recipient.Trim()); }
                catch (Exception exAddr) { TM.App.Log($"[EmailChannel] 收件人地址非法: {recipient} - {exAddr.Message}"); }
            }

            if (mail.To.Count == 0)
            {
                return NotificationResult.Fail("收件人列表为空或全部非法");
            }

            await smtp.SendMailAsync(mail, ct).ConfigureAwait(false);
            TM.App.Log($"[EmailChannel] 邮件发送成功: {message.Title} -> {mail.To.Count} 位收件人");
            return NotificationResult.Ok();
        }
        catch (OperationCanceledException)
        {
            return NotificationResult.Fail("已取消");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[EmailChannel] 邮件发送失败: {ex.GetType().Name} - {ex.Message}");
            return NotificationResult.Fail(ex.Message);
        }
    }
}
