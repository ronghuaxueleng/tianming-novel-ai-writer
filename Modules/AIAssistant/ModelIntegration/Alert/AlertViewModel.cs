using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using TM.Modules.AIAssistant.ModelIntegration.Alert.Models;
using TM.Modules.AIAssistant.ModelIntegration.Alert.Services;

namespace TM.Modules.AIAssistant.ModelIntegration.Alert;

[Obfuscation(Exclude = true, ApplyToMembers = true)]
[Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
public class AlertViewModel : INotifyPropertyChanged
{
    private readonly AlertService _service;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _enabled;
    private bool _onAnyError;
    private bool _onConsecutiveFailures;
    private int _consecutiveFailureThreshold;
    private bool _onTaskAborted;
    private int _cooldownMinutes;

    private bool _emailEnabled;
    private string _smtpHost = string.Empty;
    private int _smtpPort;
    private bool _enableSsl;
    private string _senderEmail = string.Empty;
    private string _senderDisplayName = string.Empty;
    private string _authCode = string.Empty;
    private string _recipientsText = string.Empty;

    private bool _isBusy;

    public bool Enabled
    {
        get => _enabled;
        set { if (_enabled != value) { _enabled = value; OnPropertyChanged(); } }
    }

    public bool OnAnyError
    {
        get => _onAnyError;
        set { if (_onAnyError != value) { _onAnyError = value; OnPropertyChanged(); } }
    }

    public bool OnConsecutiveFailures
    {
        get => _onConsecutiveFailures;
        set { if (_onConsecutiveFailures != value) { _onConsecutiveFailures = value; OnPropertyChanged(); } }
    }

    public int ConsecutiveFailureThreshold
    {
        get => _consecutiveFailureThreshold;
        set { if (_consecutiveFailureThreshold != value) { _consecutiveFailureThreshold = value; OnPropertyChanged(); } }
    }

    public bool OnTaskAborted
    {
        get => _onTaskAborted;
        set { if (_onTaskAborted != value) { _onTaskAborted = value; OnPropertyChanged(); } }
    }

    public int CooldownMinutes
    {
        get => _cooldownMinutes;
        set { if (_cooldownMinutes != value) { _cooldownMinutes = value; OnPropertyChanged(); } }
    }

    public bool EmailEnabled
    {
        get => _emailEnabled;
        set { if (_emailEnabled != value) { _emailEnabled = value; OnPropertyChanged(); } }
    }

    public string SmtpHost
    {
        get => _smtpHost;
        set { if (_smtpHost != value) { _smtpHost = value ?? string.Empty; OnPropertyChanged(); } }
    }

    public int SmtpPort
    {
        get => _smtpPort;
        set { if (_smtpPort != value) { _smtpPort = value; OnPropertyChanged(); } }
    }

    public bool EnableSsl
    {
        get => _enableSsl;
        set { if (_enableSsl != value) { _enableSsl = value; OnPropertyChanged(); } }
    }

    public string SenderEmail
    {
        get => _senderEmail;
        set { if (_senderEmail != value) { _senderEmail = value ?? string.Empty; OnPropertyChanged(); } }
    }

    public string SenderDisplayName
    {
        get => _senderDisplayName;
        set { if (_senderDisplayName != value) { _senderDisplayName = value ?? string.Empty; OnPropertyChanged(); } }
    }

    public string AuthCode
    {
        get => _authCode;
        set { if (_authCode != value) { _authCode = value ?? string.Empty; OnPropertyChanged(); } }
    }

    public string RecipientsText
    {
        get => _recipientsText;
        set { if (_recipientsText != value) { _recipientsText = value ?? string.Empty; OnPropertyChanged(); } }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set { if (_isBusy != value) { _isBusy = value; OnPropertyChanged(); } }
    }

    public ICommand SaveCommand { get; }
    public ICommand TestSendCommand { get; }
    public ICommand ReloadCommand { get; }

    public AlertViewModel(AlertService service)
    {
        _service = service;

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        TestSendCommand = new AsyncRelayCommand(TestSendAsync, () => !IsBusy);
        ReloadCommand = new RelayCommand(LoadFromService);

        LoadFromService();
    }

    private void LoadFromService()
    {
        var cfg = _service.GetConfig();
        Enabled = cfg.Enabled;

        var p = cfg.TriggerPolicy ?? new TriggerPolicy();
        OnAnyError = p.OnAnyError;
        OnConsecutiveFailures = p.OnConsecutiveFailures;
        ConsecutiveFailureThreshold = Math.Max(1, p.ConsecutiveFailureThreshold);
        OnTaskAborted = p.OnTaskAborted;
        CooldownMinutes = Math.Max(0, p.CooldownMinutes);

        var e = cfg.Email ?? new EmailChannelConfig();
        EmailEnabled = e.Enabled;
        SmtpHost = e.SmtpHost ?? string.Empty;
        SmtpPort = NormalizePort(e.SmtpPort);
        EnableSsl = e.EnableSsl;
        SenderEmail = e.SenderEmail ?? string.Empty;
        SenderDisplayName = e.SenderDisplayName ?? string.Empty;
        AuthCode = e.AuthCode ?? string.Empty;
        RecipientsText = e.Recipients == null ? string.Empty : string.Join(";", e.Recipients);
    }

    private AlertConfig BuildConfig()
    {
        return new AlertConfig
        {
            Enabled = Enabled,
            TriggerPolicy = new TriggerPolicy
            {
                OnAnyError = OnAnyError,
                OnConsecutiveFailures = OnConsecutiveFailures,
                ConsecutiveFailureThreshold = Math.Max(1, ConsecutiveFailureThreshold),
                OnTaskAborted = OnTaskAborted,
                CooldownMinutes = Math.Max(0, CooldownMinutes)
            },
            Email = new EmailChannelConfig
            {
                Enabled = EmailEnabled,
                SmtpHost = SmtpHost?.Trim() ?? string.Empty,
                SmtpPort = NormalizePort(SmtpPort),
                EnableSsl = EnableSsl,
                SenderEmail = SenderEmail?.Trim() ?? string.Empty,
                SenderDisplayName = SenderDisplayName?.Trim() ?? string.Empty,
                AuthCode = AuthCode ?? string.Empty,
                Recipients = ParseRecipients(RecipientsText)
            }
        };
    }

    private static int NormalizePort(int port)
    {
        if (port < 1 || port > 65535) return 587;
        return port;
    }

    private static System.Collections.Generic.List<string> ParseRecipients(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new System.Collections.Generic.List<string>();
        return text.Split(new[] { ',', ';', '；', '，', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(s => s.Trim())
                   .Where(s => !string.IsNullOrEmpty(s))
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .ToList();
    }

    private async Task SaveAsync()
    {
        try
        {
            IsBusy = true;
            var result = await _service.UpdateConfigAsync(BuildConfig()).ConfigureAwait(true);
            if (result.Success)
            {
                GlobalToast.Success("已保存", "API告警配置已更新");
            }
            else
            {
                GlobalToast.Error("保存失败", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[AlertViewModel] 保存异常: {ex.Message}");
            GlobalToast.Error("保存异常", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task TestSendAsync()
    {
        try
        {
            IsBusy = true;
            var saveResult = await _service.UpdateConfigAsync(BuildConfig()).ConfigureAwait(true);
            if (!saveResult.Success)
            {
                GlobalToast.Error("配置保存失败", saveResult.ErrorMessage);
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var result = await _service.TestSendAsync(cts.Token).ConfigureAwait(true);
            if (result.Success)
            {
                GlobalToast.Success("测试发送成功", "已向收件人发送测试邮件，请查收");
            }
            else
            {
                GlobalToast.Error("测试发送失败", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[AlertViewModel] 测试发送异常: {ex.Message}");
            GlobalToast.Error("测试发送异常", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
