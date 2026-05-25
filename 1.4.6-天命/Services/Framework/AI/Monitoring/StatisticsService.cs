using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using TM.Services.Framework.AI.Interfaces.AI;

namespace TM.Services.Framework.AI.Monitoring;

public class StatisticsService : IAIUsageStatisticsService
{

    private readonly List<ApiCallRecord> _records = new();
    private readonly object _lock = new();
    private readonly string _storagePath;
    private bool _savePending;
    private DateTime _lastSaveTime = DateTime.MinValue;
    private static readonly TimeSpan SaveThrottleInterval = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private static readonly int RetentionDays = 3;
    private Timer? _dailyTrimTimer;

    public StatisticsService()
    {
        _storagePath = StoragePathHelper.GetFilePath("Services", "AI/Monitoring", "api_statistics.json");
        LoadRecords();
        TrimExpiredRecords();
        StartDailyTrimTimer();
    }

    private void StartDailyTrimTimer()
    {
        var now = DateTime.Now;
        var nextMidnight = now.Date.AddDays(1);
        var delay = nextMidnight - now;

        _dailyTrimTimer = new Timer(_ =>
        {
            TrimExpiredRecords();
        }, null, delay, TimeSpan.FromDays(1));
    }

    private void TrimExpiredRecords()
    {
        var cutoff = DateTime.Now.AddDays(-RetentionDays);
        int removed;

        lock (_lock)
        {
            var before = _records.Count;
            _records.RemoveAll(r => r.Timestamp < cutoff);
            removed = before - _records.Count;
        }

        if (removed > 0)
        {
            _ = SaveRecordsAsync();
            TM.App.Log($"[StatisticsService] 每日裁剪: 移除了 {removed} 条超过 {RetentionDays} 天的记录");
        }
    }

    public void RecordCall(string modelName, string provider, bool success, int responseTimeMs, 
                          int inputTokens = 0, int outputTokens = 0, string? errorMessage = null)
    {
        try
        {
            var record = new ApiCallRecord
            {
                Timestamp = DateTime.Now,
                ModelName = modelName,
                Provider = provider,
                Success = success,
                ResponseTimeMs = responseTimeMs,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                ErrorMessage = errorMessage
            };

            lock (_lock)
            {
                _records.Add(record);
            }

            _ = SaveRecordsAsync();

            TM.App.Log($"[StatisticsService] 记录API调用: {modelName} - {(success ? "成功" : "失败")} - {responseTimeMs}ms");
        }
        catch (Exception ex)
        {
            TM.App.Log($"[StatisticsService] 记录失败: {ex.Message}");
        }
    }

    public void RecordCall(ApiCallRecord record)
    {
        if (record == null)
        {
            return;
        }

        RecordCall(record.ModelName,
                   record.Provider,
                   record.Success,
                   record.ResponseTimeMs,
                   record.InputTokens,
                   record.OutputTokens,
                   record.ErrorMessage);
    }

    public StatisticsSummary GetSummary()
    {
        List<ApiCallRecord> snapshot;
        lock (_lock) { snapshot = _records.ToList(); }

        if (snapshot.Count == 0)
        {
            return new StatisticsSummary();
        }

        int total = snapshot.Count, success = 0;
        long responseSum = 0, inputSum = 0, outputSum = 0;
        var minTime = DateTime.MaxValue;
        var maxTime = DateTime.MinValue;

        foreach (var r in snapshot)
        {
            if (r.Success) success++;
            responseSum += r.ResponseTimeMs;
            inputSum += r.InputTokens;
            outputSum += r.OutputTokens;
            if (r.Timestamp < minTime) minTime = r.Timestamp;
            if (r.Timestamp > maxTime) maxTime = r.Timestamp;
        }

        return new StatisticsSummary
        {
            TotalCalls = total,
            SuccessCalls = success,
            FailedCalls = total - success,
            AverageResponseTime = (double)responseSum / total,
            TotalInputTokens = (int)inputSum,
            TotalOutputTokens = (int)outputSum,
            FirstCallTime = minTime,
            LastCallTime = maxTime
        };
    }

    public IReadOnlyList<DailyStatistics> GetDailyStatistics(int days = 7)
    {
        var startDate = DateTime.Now.Date.AddDays(-days);

        List<ApiCallRecord> records;
        lock (_lock) { records = _records.Where(r => r.Timestamp >= startDate).ToList(); }

        var dailyGroups = records.GroupBy(r => r.Timestamp.Date);

        return dailyGroups.Select(g => new DailyStatistics
        {
            Date = g.Key,
            TotalCalls = g.Count(),
            SuccessCalls = g.Count(r => r.Success),
            FailedCalls = g.Count(r => !r.Success),
            AverageResponseTime = g.Average(r => r.ResponseTimeMs)
        })
        .OrderBy(d => d.Date)
        .ToList();
    }

    public IReadOnlyDictionary<string, StatisticsSummary> GetStatisticsByModel()
    {
        List<ApiCallRecord> snapshot;
        lock (_lock) { snapshot = _records.ToList(); }

        var result = new Dictionary<string, StatisticsSummary>();

        var groups = snapshot.GroupBy(r => r.ModelName);

        foreach (var group in groups)
        {
            var records = group.ToList();
            result[group.Key] = new StatisticsSummary
            {
                TotalCalls = records.Count,
                SuccessCalls = records.Count(r => r.Success),
                FailedCalls = records.Count(r => !r.Success),
                AverageResponseTime = records.Average(r => r.ResponseTimeMs),
                TotalInputTokens = records.Sum(r => r.InputTokens),
                TotalOutputTokens = records.Sum(r => r.OutputTokens),
                FirstCallTime = records.Min(r => r.Timestamp),
                LastCallTime = records.Max(r => r.Timestamp)
            };
        }

        return result;
    }

    public IReadOnlyList<ApiCallRecord> GetRecentRecords(int count = 50)
    {
        lock (_lock)
        {
            var skip = Math.Max(0, _records.Count - count);
            var result = new List<ApiCallRecord>(Math.Min(count, _records.Count));
            for (int i = _records.Count - 1; i >= skip; i--)
                result.Add(_records[i]);
            return result;
        }
    }

    public System.Collections.Generic.IReadOnlyList<ApiCallRecord> GetAllRecords()
    {
        lock (_lock)
        {
            return _records.ToList();
        }
    }

    public void ClearStatistics()
    {
        lock (_lock) { _records.Clear(); }
        _ = SaveRecordsAsync();
        TM.App.Log("[StatisticsService] 统计数据已清空");
    }

    private void LoadRecords()
    {
        try
        {
            if (File.Exists(_storagePath))
            {
                var json = File.ReadAllText(_storagePath);
                var records = JsonSerializer.Deserialize<List<ApiCallRecord>>(json);
                if (records != null)
                {
                    _records.AddRange(records);
                    TM.App.Log($"[StatisticsService] 加载了 {_records.Count} 条统计记录");
                }
            }
        }
        catch (Exception ex)
        {
            TM.App.Log($"[StatisticsService] 加载统计数据失败: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task SaveRecordsAsync()
    {
        try
        {
            TimeSpan elapsed;
            lock (_lock) { elapsed = DateTime.Now - _lastSaveTime; }

            if (elapsed < SaveThrottleInterval)
            {
                lock (_lock)
                {
                    if (_savePending) return;
                    _savePending = true;
                }
                var delay = SaveThrottleInterval - elapsed;
                await System.Threading.Tasks.Task.Delay(delay).ConfigureAwait(false);
                lock (_lock) { _savePending = false; }
                await SaveRecordsCore().ConfigureAwait(false);
                return;
            }

            await SaveRecordsCore().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            TM.App.Log($"[StatisticsService] SaveRecords异常: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task SaveRecordsCore()
    {
        var acquired = false;
        try
        {
            await _saveLock.WaitAsync().ConfigureAwait(false);
            acquired = true;

            lock (_lock) { _lastSaveTime = DateTime.Now; }

            var directory = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json;
            lock (_lock) { json = JsonSerializer.Serialize(_records, JsonHelper.CnDefault); }

            var tmp = _storagePath + ".tmp";
            await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
            File.Move(tmp, _storagePath, overwrite: true);
        }
        catch (Exception ex)
        {
            TM.App.Log($"[StatisticsService] 保存统计数据失败: {ex.Message}");
        }
        finally
        {
            if (acquired)
                _saveLock.Release();
        }
    }
}
