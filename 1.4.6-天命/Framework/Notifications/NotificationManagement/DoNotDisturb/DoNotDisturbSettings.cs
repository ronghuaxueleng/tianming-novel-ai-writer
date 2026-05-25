using System;
using System.Reflection;
using System.Collections.Generic;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.Common.Services.Factories;

namespace TM.Framework.Notifications.NotificationManagement.DoNotDisturb
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class DoNotDisturbData
    {
        [System.Text.Json.Serialization.JsonPropertyName("IsEnabled")] public bool IsEnabled { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("StartTime")] public TimeSpan StartTime { get; set; } = new TimeSpan(22, 0, 0);
        [System.Text.Json.Serialization.JsonPropertyName("EndTime")] public TimeSpan EndTime { get; set; } = new TimeSpan(8, 0, 0);
        [System.Text.Json.Serialization.JsonPropertyName("AllowUrgentNotifications")] public bool AllowUrgentNotifications { get; set; } = true;
        [System.Text.Json.Serialization.JsonPropertyName("AutoEnableInFullscreen")] public bool AutoEnableInFullscreen { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("ExceptionApps")] public List<string> ExceptionApps { get; set; } = new();
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class DoNotDisturbSettings : BaseSettings<DoNotDisturbSettings, DoNotDisturbData>
    {
        public DoNotDisturbSettings(IStoragePathHelper storagePathHelper, IObjectFactory objectFactory)
            : base(storagePathHelper, objectFactory) { }

        protected override string GetFilePath() =>
            _storagePathHelper.GetFilePath("Framework", "Notifications/NotificationManagement/DoNotDisturb", "dnd_settings.json");

        protected override DoNotDisturbData CreateDefaultData() => _objectFactory.Create<DoNotDisturbData>();

        public bool IsEnabled
        {
            get => Data.IsEnabled;
            set { Data.IsEnabled = value; SaveData(); OnPropertyChanged(); }
        }

        public TimeSpan StartTime
        {
            get => Data.StartTime;
            set { Data.StartTime = value; SaveData(); OnPropertyChanged(); }
        }

        public TimeSpan EndTime
        {
            get => Data.EndTime;
            set { Data.EndTime = value; SaveData(); OnPropertyChanged(); }
        }

        public bool AllowUrgentNotifications
        {
            get => Data.AllowUrgentNotifications;
            set { Data.AllowUrgentNotifications = value; SaveData(); OnPropertyChanged(); }
        }

        public bool AutoEnableInFullscreen
        {
            get => Data.AutoEnableInFullscreen;
            set { Data.AutoEnableInFullscreen = value; SaveData(); OnPropertyChanged(); }
        }

        public List<string> ExceptionApps
        {
            get => Data.ExceptionApps;
            set { Data.ExceptionApps = value; SaveData(); OnPropertyChanged(); }
        }

        public bool ShouldBlock(bool isHighPriority = false)
        {
            if (!IsEnabled)
                return false;

            if (isHighPriority && AllowUrgentNotifications)
                return false;

            return true;
        }
    }
}
