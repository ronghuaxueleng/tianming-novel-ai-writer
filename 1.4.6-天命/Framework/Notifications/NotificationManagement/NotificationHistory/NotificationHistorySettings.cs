using System;
using System.Collections.Generic;
using System.Linq;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.Common.Services.Factories;

namespace TM.Framework.Notifications.NotificationManagement.NotificationHistory
{
    public class NotificationHistoryData
    {
        [System.Text.Json.Serialization.JsonPropertyName("Records")] public List<NotificationRecordData> Records { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("MaxRecords")] public int MaxRecords { get; set; } = 10;
    }

    public class NotificationRecordData
    {
        [System.Text.Json.Serialization.JsonPropertyName("Id")] public string Id { get; set; } = ShortIdGenerator.New("D");
        [System.Text.Json.Serialization.JsonPropertyName("Title")] public string Title { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Content")] public string Content { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("Time")] public DateTime Time { get; set; } = DateTime.Now;
        [System.Text.Json.Serialization.JsonPropertyName("Type")] public string Type { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("IsRead")] public bool IsRead { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("WasBlocked")] public bool WasBlocked { get; set; }
    }

    public class NotificationHistorySettings : BaseSettings<NotificationHistorySettings, NotificationHistoryData>
    {
        public NotificationHistorySettings(IStoragePathHelper storagePathHelper, IObjectFactory objectFactory)
            : base(storagePathHelper, objectFactory) { }

        protected override string GetFilePath() =>
            _storagePathHelper.GetFilePath("Framework", "Notifications/NotificationManagement/NotificationHistory", "notification_history.json");

        protected override NotificationHistoryData CreateDefaultData() => _objectFactory.Create<NotificationHistoryData>();

        public void AddRecord(string title, string content, string type, bool wasBlocked = false)
        {
            Data.MaxRecords = 10;

            var record = new NotificationRecordData
            {
                Id = ShortIdGenerator.New("D"),
                Title = title,
                Content = content,
                Time = DateTime.Now,
                Type = type,
                IsRead = false,
                WasBlocked = wasBlocked
            };

            Data.Records.Insert(0, record);

            if (Data.Records.Count > Data.MaxRecords)
            {
                Data.Records = Data.Records.Take(Data.MaxRecords).ToList();
            }

            _ = SaveDataAsync().ContinueWith(
                t => TM.App.Log($"[NotificationHistorySettings] 异步保存失败: {t.Exception?.GetBaseException().Message}"),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }

        public List<NotificationRecordData> GetRecords()
        {
            Data.MaxRecords = 10;
            if (Data.Records.Count > Data.MaxRecords)
            {
                Data.Records = Data.Records.Take(Data.MaxRecords).ToList();
                SaveData();
            }
            return Data.Records;
        }

        public void ClearAll()
        {
            Data.Records.Clear();
            SaveData();
        }

        public void DeleteRecord(string id)
        {
            Data.Records.RemoveAll(r => r.Id == id);
            SaveData();
        }

        public void MarkAsRead(string id)
        {
            var record = Data.Records.FirstOrDefault(r => r.Id == id);
            if (record != null)
            {
                record.IsRead = true;
                SaveData();
            }
        }
    }
}
