using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Modules.AIAssistant.ModelIntegration.Alert.Models;
using TM.Services.Framework.AI.Monitoring;

namespace TM.Modules.AIAssistant.ModelIntegration.Alert.Services;

public class AlertService : IDisposable
{
    private readonly IReadOnlyList<INotificationChannel> _channels;
    private readonly string _configPath;
    private readonly object _configLock = new();
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    private AlertConfig _config = new();

    private readonly Dictionary<string, int> _consecutiveFailures = new(StringComparer.Ordinal);
    private readonly Dictionary<AlertReason, DateTime> _lastSentAt = new();

    private bool _subscribed;
    private bool _disposed;

    public event EventHandler? ConfigChanged;

    public AlertService(IEnumerable<INotificationChannel> channels)
    {
        _channels = (channels ?? Array.Empty<INotificationChannel>()).ToList();
        _configPath = StoragePathHelper.GetFilePath("Modules", "AIAssistant/ModelIntegration/Alert", "config.json");

        LoadConfig();
        Subscribe();
    }

    public AlertConfig GetConfig()
    {
        lock (_configLock)
        {
            return CloneConfig(_config);
        }
    }

    public void UpdateConfig(AlertConfig newConfig)
    {
        if (newConfig == null) return;

        lock (_configLock)
        {
            _config = CloneConfig(newConfig);
        }

        _ = SaveConfigAsync();
        try { ConfigChanged?.Invoke(this, EventArgs.Empty); } catch { }
        TM.App.Log("[AlertService] 配置已更新");
    }

    public async Task<NotificationResult> UpdateConfigAsync(AlertConfig newConfig)
    {
        if (newConfig == null) return NotificationResult.Fail("配置为空");

        lock (_configLock)
        {
            _config = CloneConfig(newConfig);
        }

        var saveResult = await SaveConfigAsync().ConfigureAwait(false);
        try { ConfigChanged?.Invoke(this, EventArgs.Empty); } catch { }

        if (saveResult.Success)
        {
            TM.App.Log("[AlertService] 配置已更新并落盘");
            return NotificationResult.Ok();
        }

        TM.App.Log($"[AlertService] 配置已更新但落盘失败: {saveResult.ErrorMessage}");
        return saveResult;
    }

    public async Task<NotificationResult> TestSendAsync(CancellationToken ct = default)
    {
        var cfg = GetConfig();
        var msg = new AlertMessage
        {
            Reason = AlertReason.Manual,
            Title = "[天命] 测试告警",
            Body = $"这是一封测试邮件。\n时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n如果你能看到这封邮件，表示告警通道配置正确。",
            ModelName = "(测试)",
            Provider = "(测试)"
        };

        var results = await DispatchAsync(cfg, msg, ct).ConfigureAwait(false);
        if (results.Count == 0) return NotificationResult.Fail("没有任何启用的通知渠道");

        var failed = results.Where(r => !r.Success).ToList();
        if (failed.Count == 0) return NotificationResult.Ok();
        return NotificationResult.Fail(string.Join("；", failed.Select(r => r.ErrorMessage)));
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        StatisticsService.CallRecorded += OnCallRecorded;
        _subscribed = true;
        TM.App.Log("[AlertService] 已订阅 API 调用记录事件");
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        StatisticsService.CallRecorded -= OnCallRecorded;
        _subscribed = false;
    }

    private void OnCallRecorded(ApiCallRecord record)
    {
        try
        {
            if (record == null) return;

            AlertConfig cfg;
            lock (_configLock) { cfg = CloneConfig(_config); }

            if (!cfg.Enabled) return;

            var policy = cfg.TriggerPolicy ?? new TriggerPolicy();

            var key = $"{record.Provider}|{record.ModelName}";

            if (record.Success)
            {
                lock (_stateLock)
                {
                    if (_consecutiveFailures.ContainsKey(key))
                        _consecutiveFailures[key] = 0;
                }
                return;
            }

            int currentCount;
            lock (_stateLock)
            {
                if (!_consecutiveFailures.TryGetValue(key, out currentCount)) currentCount = 0;
                currentCount++;
                _consecutiveFailures[key] = currentCount;
            }

            var reasons = EvaluateReasons(record, policy, currentCount);
            if (reasons.Count == 0) return;

            foreach (var reason in reasons)
            {
                if (!ShouldSend(reason, policy.CooldownMinutes)) continue;

                var msg = BuildMessage(reason, record, currentCount);
                _ = DispatchAsync(cfg, msg, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[AlertService] OnCallRecorded异常: {ex.Message}");
        }
    }

    private static List<AlertReason> EvaluateReasons(ApiCallRecord record, TriggerPolicy policy, int consecutiveCount)
    {
        var reasons = new List<AlertReason>();

        var err = record.ErrorMessage ?? string.Empty;
        var isAborted = err.Contains("[已取消]", StringComparison.Ordinal)
                        || err.Contains("[会话终止]", StringComparison.Ordinal)
                        || err.Contains("请求超时", StringComparison.Ordinal);

        if (policy.OnTaskAborted && isAborted)
            reasons.Add(AlertReason.TaskAborted);

        if (policy.OnConsecutiveFailures && consecutiveCount >= Math.Max(1, policy.ConsecutiveFailureThreshold))
            reasons.Add(AlertReason.ConsecutiveFailures);

        if (policy.OnAnyError)
            reasons.Add(AlertReason.AnyError);

        return reasons.Distinct().ToList();
    }

    private bool ShouldSend(AlertReason reason, int cooldownMinutes)
    {
        if (cooldownMinutes <= 0) return true;
        var threshold = TimeSpan.FromMinutes(cooldownMinutes);
        var now = DateTime.Now;
        lock (_stateLock)
        {
            if (_lastSentAt.TryGetValue(reason, out var last) && (now - last) < threshold)
            {
                return false;
            }
            _lastSentAt[reason] = now;
            return true;
        }
    }

    private static AlertMessage BuildMessage(AlertReason reason, ApiCallRecord record, int consecutiveCount)
    {
        var reasonText = reason switch
        {
            AlertReason.AnyError => "API调用失败",
            AlertReason.ConsecutiveFailures => $"API连续失败 {consecutiveCount} 次",
            AlertReason.TaskAborted => "任务被中止",
            AlertReason.Manual => "手动测试",
            _ => "未知原因"
        };

        var title = $"[天命] {reasonText} - {record.ModelName}";
        var body =
$@"原因：{reasonText}
时间：{record.Timestamp:yyyy-MM-dd HH:mm:ss}
供应商：{record.Provider}
模型：{record.ModelName}
响应耗时：{record.ResponseTimeMs} ms
连续失败次数：{consecutiveCount}
错误信息：{record.ErrorMessage}

—— 此邮件由天命AI自动发送，请勿回复。";

        return new AlertMessage
        {
            Reason = reason,
            Title = title,
            Body = body,
            ModelName = record.ModelName ?? string.Empty,
            Provider = record.Provider ?? string.Empty,
            ErrorMessage = record.ErrorMessage ?? string.Empty,
            ConsecutiveCount = consecutiveCount,
            Timestamp = record.Timestamp
        };
    }

    private async Task<List<NotificationResult>> DispatchAsync(AlertConfig cfg, AlertMessage msg, CancellationToken ct)
    {
        var results = new List<NotificationResult>();
        foreach (var channel in _channels)
        {
            try
            {
                if (!channel.IsEnabled(cfg)) continue;
                var result = await channel.SendAsync(cfg, msg, ct).ConfigureAwait(false);
                results.Add(result);
                TM.App.Log($"[AlertService] 渠道[{channel.ChannelName}]发送结果: {(result.Success ? "成功" : "失败")} - {result.ErrorMessage}");
            }
            catch (Exception ex)
            {
                results.Add(NotificationResult.Fail(ex.Message));
                TM.App.Log($"[AlertService] 渠道[{channel.ChannelName}]异常: {ex.Message}");
            }
        }
        return results;
    }

    private void LoadConfig()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                TM.App.Log($"[AlertService] 配置文件不存在，使用默认配置: {_configPath}");
                return;
            }

            var json = File.ReadAllText(_configPath);
            var loaded = JsonSerializer.Deserialize<AlertConfig>(json, JsonHelper.CnDefault);
            if (loaded != null)
            {
                lock (_configLock) { _config = loaded; }
                TM.App.Log("[AlertService] 配置加载成功");
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[AlertService] 配置加载失败: {ex.Message}");
        }
    }

    private async Task<NotificationResult> SaveConfigAsync()
    {
        var acquired = false;
        try
        {
            await _saveLock.WaitAsync().ConfigureAwait(false);
            acquired = true;

            AlertConfig snapshot;
            lock (_configLock) { snapshot = CloneConfig(_config); }

            var dir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(snapshot, JsonHelper.CnDefault);
            var tmp = _configPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
            File.Move(tmp, _configPath, overwrite: true);
            TM.App.Log("[AlertService] 配置已保存");
            return NotificationResult.Ok();
        }
        catch (Exception ex)
        {
            TM.App.Log($"[AlertService] 配置保存失败: {ex.Message}");
            return NotificationResult.Fail(ex.Message);
        }
        finally
        {
            if (acquired) _saveLock.Release();
        }
    }

    private static AlertConfig CloneConfig(AlertConfig src)
    {
        var json = JsonSerializer.Serialize(src);
        return JsonSerializer.Deserialize<AlertConfig>(json) ?? new AlertConfig();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unsubscribe();
        _saveLock.Dispose();
    }
}
