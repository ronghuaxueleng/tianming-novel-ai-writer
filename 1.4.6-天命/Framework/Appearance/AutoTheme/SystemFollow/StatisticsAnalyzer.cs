using System;
using System.Collections.Generic;
using System.Linq;
using TM.Framework.Appearance.ThemeManagement;

namespace TM.Framework.Appearance.AutoTheme.SystemFollow
{
    public class StatisticsAnalyzer
    {
        private readonly List<SwitchRecord> _switchHistory;

        public StatisticsAnalyzer()
        {
            _switchHistory = new List<SwitchRecord>();
        }

        public void AddSwitchRecord(ThemeType fromTheme, ThemeType toTheme, TimeSpan duration)
        {
            _switchHistory.Add(new SwitchRecord
            {
                Timestamp = DateTime.Now,
                FromTheme = fromTheme,
                ToTheme = toTheme,
                Duration = duration
            });

            var cutoffDate = DateTime.Now.AddDays(-30);
            _switchHistory.RemoveAll(r => r.Timestamp < cutoffDate);
        }

        public int GetDailySwitchCount()
        {
            var today = DateTime.Today;
            return _switchHistory.Count(r => r.Timestamp.Date == today);
        }

        public int GetWeeklySwitchCount()
        {
            var startOfWeek = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
            return _switchHistory.Count(r => r.Timestamp >= startOfWeek);
        }

        public int GetMonthlySwitchCount()
        {
            var startOfMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            return _switchHistory.Count(r => r.Timestamp >= startOfMonth);
        }

        public Dictionary<ThemeType, double> GetThemeUsagePercentage()
        {
            var sevenDaysAgo = DateTime.Now.AddDays(-7);
            var recentRecords = _switchHistory.Where(r => r.Timestamp >= sevenDaysAgo).ToList();

            if (recentRecords.Count == 0)
            {
                return new Dictionary<ThemeType, double>();
            }

            var themeCounts = recentRecords
                .GroupBy(r => r.ToTheme)
                .ToDictionary(g => g.Key, g => g.Count());

            var total = recentRecords.Count;
            var percentages = themeCounts.ToDictionary(
                kvp => kvp.Key,
                kvp => (double)kvp.Value / total * 100
            );

            return percentages;
        }

        public TimeSpan GetAverageSwitchDuration()
        {
            if (_switchHistory.Count == 0)
            {
                return TimeSpan.Zero;
            }

            var totalMs = _switchHistory.Sum(r => r.Duration.TotalMilliseconds);
            return TimeSpan.FromMilliseconds(totalMs / _switchHistory.Count);
        }

        public TimeSpan GetLastSwitchDuration()
        {
            return _switchHistory.LastOrDefault()?.Duration ?? TimeSpan.Zero;
        }

        public Dictionary<int, int> GetHourlySwitchCount()
        {
            var oneDayAgo = DateTime.Now.AddHours(-24);
            var recentRecords = _switchHistory.Where(r => r.Timestamp >= oneDayAgo);

            var hourlyCounts = new Dictionary<int, int>();
            for (int hour = 0; hour < 24; hour++)
            {
                hourlyCounts[hour] = 0;
            }

            foreach (var record in recentRecords)
            {
                var hour = record.Timestamp.Hour;
                hourlyCounts[hour]++;
            }

            return hourlyCounts;
        }

        public ThemeType GetMostUsedTheme()
        {
            if (_switchHistory.Count == 0)
            {
                return ThemeType.Light;
            }

            return _switchHistory
                .GroupBy(r => r.ToTheme)
                .OrderByDescending(g => g.Count())
                .First()
                .Key;
        }

        public string GetPeakSwitchPeriod()
        {
            var hourlyCounts = GetHourlySwitchCount();
            if (hourlyCounts.Count == 0 || hourlyCounts.Values.Sum() == 0)
            {
                return "暂无数据";
            }

            var peakHour = hourlyCounts.OrderByDescending(kvp => kvp.Value).First().Key;
            return $"{peakHour:D2}:00 - {(peakHour + 1) % 24:D2}:00";
        }

        public string GetPerformanceRating()
        {
            var avgDuration = GetAverageSwitchDuration();
            var ms = avgDuration.TotalMilliseconds;

            if (ms < 100) return "优秀";
            if (ms < 300) return "良好";
            if (ms < 500) return "一般";
            return "需优化";
        }

        public void ClearStatistics()
        {
            _switchHistory.Clear();
            TM.App.Log("[StatisticsAnalyzer] 统计数据已清空");
        }

        public List<SwitchRecord> GetAllRecords()
        {
            return new List<SwitchRecord>(_switchHistory);
        }
    }

    public class SwitchRecord
    {
        public DateTime Timestamp { get; set; }

        public ThemeType FromTheme { get; set; }

        public ThemeType ToTheme { get; set; }

        public TimeSpan Duration { get; set; }
    }
}

