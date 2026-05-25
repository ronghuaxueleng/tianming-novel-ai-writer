using System;
using System.Collections.Generic;
using System.Linq;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Services.Factories;

namespace TM.Framework.Appearance.IntelligentGeneration.GenerationHistory
{
    public class GenerationHistoryData
    {
        [System.Text.Json.Serialization.JsonPropertyName("Records")] public List<HistoryRecordData> Records { get; set; } = new();
    }

    public class GenerationHistorySettings : BaseSettings<GenerationHistorySettings, GenerationHistoryData>
    {
        public GenerationHistorySettings(IStoragePathHelper storagePathHelper, IObjectFactory objectFactory)
            : base(storagePathHelper, objectFactory) { }

        protected override string GetFilePath() =>
            _storagePathHelper.GetFilePath("Framework", "Appearance/IntelligentGeneration/GenerationHistory", "generation_history.json");

        protected override GenerationHistoryData CreateDefaultData() => _objectFactory.Create<GenerationHistoryData>();

        private readonly object _lock = new object();

        private List<HistoryRecordData> GetSnapshot()
        {
            lock (_lock) { return new List<HistoryRecordData>(Data.Records); }
        }

        public List<HistoryRecordData> GetAllRecords()
            => GetSnapshot().OrderByDescending(r => r.Timestamp).ToList();

        public void AddRecord(HistoryRecordData record)
        {
            ArgumentNullException.ThrowIfNull(record);
            lock (_lock)
            {
                if (string.IsNullOrEmpty(record.Id)) record.Id = ShortIdGenerator.New("D");
                var existing = Data.Records.FindIndex(r => r.Id == record.Id);
                if (existing >= 0) Data.Records[existing] = record;
                else Data.Records.Add(record);
            }
            SaveDataAsync().SafeFireAndForget(ex => TM.App.Log($"[GenerationHistorySettings] 保存失败: {ex.Message}"));
        }

        public void DeleteRecord(string recordId)
        {
            if (string.IsNullOrEmpty(recordId)) throw new ArgumentNullException(nameof(recordId));
            lock (_lock)
            {
                var record = Data.Records.FirstOrDefault(r => r.Id == recordId);
                if (record == null) return;
                Data.Records.Remove(record);
            }
            SaveDataAsync().SafeFireAndForget(ex => TM.App.Log($"[GenerationHistorySettings] 保存失败: {ex.Message}"));
        }

        public void UpdateFavorite(string recordId, bool isFavorite)
        {
            if (string.IsNullOrEmpty(recordId)) throw new ArgumentNullException(nameof(recordId));
            lock (_lock)
            {
                var record = Data.Records.FirstOrDefault(r => r.Id == recordId);
                if (record == null) return;
                record.IsFavorite = isFavorite;
            }
            SaveDataAsync().SafeFireAndForget(ex => TM.App.Log($"[GenerationHistorySettings] 保存失败: {ex.Message}"));
        }

        public void ClearAll()
        {
            lock (_lock) { Data.Records.Clear(); }
            SaveDataAsync().SafeFireAndForget(ex => TM.App.Log($"[GenerationHistorySettings] 保存失败: {ex.Message}"));
        }

        public List<HistoryRecordData> GetFavoriteRecords()
            => GetSnapshot().Where(r => r.IsFavorite).OrderByDescending(r => r.Timestamp).ToList();

        public List<HistoryRecordData> GetRecordsByType(string type)
            => GetSnapshot().Where(r => r.Type == type).OrderByDescending(r => r.Timestamp).ToList();

        public List<HistoryRecordData> GetRecordsByDateRange(DateTime startDate, DateTime endDate)
            => GetSnapshot().Where(r => r.Timestamp >= startDate && r.Timestamp <= endDate).OrderByDescending(r => r.Timestamp).ToList();

        public List<HistoryRecordData> SearchRecords(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return GetAllRecords();
            return GetSnapshot()
                .Where(r => r.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                            r.Keywords.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.Timestamp)
                .ToList();
        }

        public HistoryStatistics GetStatistics()
        {
            lock (_lock)
            {
                return new HistoryStatistics
                {
                    TotalCount = Data.Records.Count,
                    FavoriteCount = Data.Records.Count(r => r.IsFavorite),
                    ImagePickerCount = Data.Records.Count(r => r.Type == "图片取色"),
                    AIGeneratedCount = Data.Records.Count(r => r.Type == "AI配色"),
                    TodayCount = Data.Records.Count(r => r.Timestamp.Date == DateTime.Now.Date),
                    ThisWeekCount = Data.Records.Count(r => r.Timestamp >= DateTime.Now.AddDays(-(int)DateTime.Now.DayOfWeek)),
                    ThisMonthCount = Data.Records.Count(r => r.Timestamp.Month == DateTime.Now.Month && r.Timestamp.Year == DateTime.Now.Year)
                };
            }
        }
    }

    public class HistoryStatistics
    {
        public int TotalCount { get; set; }
        public int FavoriteCount { get; set; }
        public int ImagePickerCount { get; set; }
        public int AIGeneratedCount { get; set; }
        public int TodayCount { get; set; }
        public int ThisWeekCount { get; set; }
        public int ThisMonthCount { get; set; }
    }
}
