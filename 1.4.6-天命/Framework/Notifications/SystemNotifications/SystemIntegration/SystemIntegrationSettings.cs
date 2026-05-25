using System;
using System.Reflection;

namespace TM.Framework.Notifications.SystemNotifications.SystemIntegration
{
    [System.Reflection.Obfuscation(Exclude = true)]
    public enum ClickBehavior
    {
        ShowWindow,
        HideWindow,
        Toggle,
        DoNothing
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum StartupMode
    {
        Normal,
        Minimized
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class SystemIntegrationData
    {
        [System.Text.Json.Serialization.JsonPropertyName("EnableWindowsNotification")] public bool EnableWindowsNotification { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("NotificationSound")] public bool NotificationSound { get; set; } = true;
        [System.Text.Json.Serialization.JsonPropertyName("NotificationPriority")] public string NotificationPriority { get; set; } = "Normal";

        [System.Text.Json.Serialization.JsonPropertyName("ShowTrayIcon")] public bool ShowTrayIcon { get; set; } = true;
        [System.Text.Json.Serialization.JsonPropertyName("SingleClickBehavior")] public ClickBehavior SingleClickBehavior { get; set; } = ClickBehavior.Toggle;
        [System.Text.Json.Serialization.JsonPropertyName("DoubleClickBehavior")] public ClickBehavior DoubleClickBehavior { get; set; } = ClickBehavior.ShowWindow;
        [System.Text.Json.Serialization.JsonPropertyName("CloseToTray")] public bool CloseToTray { get; set; } = false;

        [System.Text.Json.Serialization.JsonPropertyName("AutoStartup")] public bool AutoStartup { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("StartupMode")] public StartupMode StartupMode { get; set; } = StartupMode.Normal;
        [System.Text.Json.Serialization.JsonPropertyName("StartupDelay")] public int StartupDelay { get; set; } = 0;

        [System.Text.Json.Serialization.JsonPropertyName("RegisterUrlProtocol")] public bool RegisterUrlProtocol { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("AssociateFileType")] public bool AssociateFileType { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("AddToContextMenu")] public bool AddToContextMenu { get; set; } = false;
        [System.Text.Json.Serialization.JsonPropertyName("AddToSendToMenu")] public bool AddToSendToMenu { get; set; } = false;
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class SystemIntegrationSettings : BaseSettings<SystemIntegrationSettings, SystemIntegrationData>
    {
        public SystemIntegrationSettings(Common.Services.Factories.IStoragePathHelper storagePathHelper, 
            Common.Services.Factories.IObjectFactory objectFactory)
            : base(storagePathHelper, objectFactory) { }

        protected override string GetFilePath() =>
            _storagePathHelper.GetFilePath("Framework", "Notifications/SystemNotifications/SystemIntegration", "system_integration.json");

        protected override SystemIntegrationData CreateDefaultData() => _objectFactory.Create<SystemIntegrationData>();

        #region 便捷访问属性

        public bool EnableWindowsNotification { get => Data.EnableWindowsNotification; set { Data.EnableWindowsNotification = value; SaveData(); OnPropertyChanged(); } }
        public bool NotificationSound { get => Data.NotificationSound; set { Data.NotificationSound = value; SaveData(); OnPropertyChanged(); } }
        public string NotificationPriority { get => Data.NotificationPriority; set { Data.NotificationPriority = value; SaveData(); OnPropertyChanged(); } }

        public bool ShowTrayIcon { get => Data.ShowTrayIcon; set { Data.ShowTrayIcon = value; SaveData(); OnPropertyChanged(); } }
        public ClickBehavior SingleClickBehavior { get => Data.SingleClickBehavior; set { Data.SingleClickBehavior = value; SaveData(); OnPropertyChanged(); } }
        public ClickBehavior DoubleClickBehavior { get => Data.DoubleClickBehavior; set { Data.DoubleClickBehavior = value; SaveData(); OnPropertyChanged(); } }
        public bool CloseToTray { get => Data.CloseToTray; set { Data.CloseToTray = value; SaveData(); OnPropertyChanged(); } }

        public bool AutoStartup { get => Data.AutoStartup; set { Data.AutoStartup = value; SaveData(); OnPropertyChanged(); } }
        public StartupMode StartupMode { get => Data.StartupMode; set { Data.StartupMode = value; SaveData(); OnPropertyChanged(); } }
        public int StartupDelay { get => Data.StartupDelay; set { Data.StartupDelay = value; SaveData(); OnPropertyChanged(); } }

        public bool RegisterUrlProtocol { get => Data.RegisterUrlProtocol; set { Data.RegisterUrlProtocol = value; SaveData(); OnPropertyChanged(); } }
        public bool AssociateFileType { get => Data.AssociateFileType; set { Data.AssociateFileType = value; SaveData(); OnPropertyChanged(); } }
        public bool AddToContextMenu { get => Data.AddToContextMenu; set { Data.AddToContextMenu = value; SaveData(); OnPropertyChanged(); } }
        public bool AddToSendToMenu { get => Data.AddToSendToMenu; set { Data.AddToSendToMenu = value; SaveData(); OnPropertyChanged(); } }

        #endregion

        public void LoadSettings() => LoadData();
        public void SaveSettings() => SaveData();
    }
}

