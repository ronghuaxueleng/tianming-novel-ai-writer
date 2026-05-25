using System;
using System.Reflection;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TM.Framework.Notifications.SystemNotifications.NotificationTypes
{
    [System.Reflection.Obfuscation(Exclude = true)]
    public enum NotificationPriority
    {
        Low,
        Medium,
        High
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class NotificationTypeData : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _name = string.Empty;
        private string _icon = string.Empty;
        private string _color = "#3B82F6";
        private NotificationPriority _priority = NotificationPriority.Medium;
        private bool _isEnabled = true;
        private string _groupName = string.Empty;
        private string _description = string.Empty;
        private bool _isSelected = false;

        [System.Text.Json.Serialization.JsonPropertyName("Id")]
        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(nameof(Id)); }
        }

        [System.Text.Json.Serialization.JsonPropertyName("Name")]
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        [System.Text.Json.Serialization.JsonPropertyName("Icon")]
        public string Icon
        {
            get => _icon;
            set { _icon = value; OnPropertyChanged(nameof(Icon)); }
        }

        [System.Text.Json.Serialization.JsonPropertyName("Color")]
        public string Color
        {
            get => _color;
            set { _color = value; OnPropertyChanged(nameof(Color)); }
        }

        [System.Text.Json.Serialization.JsonPropertyName("Priority")]
        public NotificationPriority Priority
        {
            get => _priority;
            set { _priority = value; OnPropertyChanged(nameof(Priority)); }
        }

        [System.Text.Json.Serialization.JsonPropertyName("IsEnabled")]
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); }
        }

        [System.Text.Json.Serialization.JsonPropertyName("GroupName")]
        public string GroupName
        {
            get => _groupName;
            set { _groupName = value; OnPropertyChanged(nameof(GroupName)); }
        }

        [System.Text.Json.Serialization.JsonPropertyName("Description")]
        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(nameof(Description)); }
        }

        [System.Text.Json.Serialization.JsonPropertyName("IsSelected")]
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class NotificationTypeSettingsData
    {
        [System.Text.Json.Serialization.JsonPropertyName("Types")] public List<NotificationTypeData> Types { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("LastModified")] public DateTime LastModified { get; set; } = DateTime.Now;
    }

    public class NotificationTypeSettings : BaseSettings<NotificationTypeSettings, NotificationTypeSettingsData>
    {
        private List<NotificationTypeData>? _cachedTypes;

        public NotificationTypeSettings(Common.Services.Factories.IStoragePathHelper storagePathHelper, 
            Common.Services.Factories.IObjectFactory objectFactory)
            : base(storagePathHelper, objectFactory) { }

        protected override string GetFilePath() =>
            _storagePathHelper.GetFilePath("Framework", "Notifications/SystemNotifications/NotificationTypes", "notification_types.json");

        protected override NotificationTypeSettingsData CreateDefaultData() => _objectFactory.Create<NotificationTypeSettingsData>();

        public List<NotificationTypeData> LoadSettings()
        {
            if (_cachedTypes != null)
                return _cachedTypes;

            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var data = JsonSerializer.Deserialize<NotificationTypeSettingsData>(json);

                    if (data?.Types != null && data.Types.Count > 0)
                    {
                        App.Log($"[NotificationTypeSettings] 已加载 {data.Types.Count} 个通知类型");
                        _cachedTypes = data.Types;
                        return _cachedTypes;
                    }
                }

                App.Log("[NotificationTypeSettings] 配置文件不存在，使用默认配置");
                _cachedTypes = GetDefaultTypes();
                return _cachedTypes;
            }
            catch (Exception ex)
            {
                App.Log($"[NotificationTypeSettings] 加载设置失败: {ex.Message}");
                return GetDefaultTypes();
            }
        }

        public void SaveSettings(List<NotificationTypeData> types)
        {
            try
            {
                var data = new NotificationTypeSettingsData
                {
                    Types = types,
                    LastModified = DateTime.Now
                };

                var options = JsonHelper.CnDefault;

                var json = JsonSerializer.Serialize(data, options);

                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                    _storagePathHelper.EnsureDirectoryExists(directory);

                var tmpNts = FilePath + ".tmp";
                File.WriteAllText(tmpNts, json);
                File.Move(tmpNts, FilePath, overwrite: true);
                _cachedTypes = types;
                App.Log($"[NotificationTypeSettings] 已保存 {types.Count} 个通知类型");
            }
            catch (Exception ex)
            {
                App.Log($"[NotificationTypeSettings] 保存设置失败: {ex.Message}");
                throw;
            }
        }

        public async System.Threading.Tasks.Task SaveSettingsAsync(List<NotificationTypeData> types)
        {
            try
            {
                var data = new NotificationTypeSettingsData
                {
                    Types = types,
                    LastModified = DateTime.Now
                };

                var options = JsonHelper.CnDefault;

                var json = JsonSerializer.Serialize(data, options);

                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                    _storagePathHelper.EnsureDirectoryExists(directory);

                var tmpNtsA = FilePath + ".tmp";
                await File.WriteAllTextAsync(tmpNtsA, json);
                File.Move(tmpNtsA, FilePath, overwrite: true);
                _cachedTypes = types;
                App.Log($"[NotificationTypeSettings] 已异步保存 {types.Count} 个通知类型");
            }
            catch (Exception ex)
            {
                App.Log($"[NotificationTypeSettings] 异步保存设置失败: {ex.Message}");
                throw;
            }
        }

        public List<NotificationTypeData> GetDefaultTypes()
        {
            return new List<NotificationTypeData>
            {
                new NotificationTypeData
                {
                    Id = "system",
                    Name = "系统通知",
                    Icon = "⚙️",
                    Color = "#2196F3",
                    Priority = NotificationPriority.Medium,
                    IsEnabled = true,
                    GroupName = "系统",
                    Description = "系统级别的通知消息"
                },
                new NotificationTypeData
                {
                    Id = "application",
                    Name = "应用通知",
                    Icon = "📱",
                    Color = "#16A34A",
                    Priority = NotificationPriority.Medium,
                    IsEnabled = true,
                    GroupName = "应用",
                    Description = "应用程序的通知消息"
                },
                new NotificationTypeData
                {
                    Id = "warning",
                    Name = "警告",
                    Icon = "⚠️",
                    Color = "#FF9800",
                    Priority = NotificationPriority.High,
                    IsEnabled = true,
                    GroupName = "状态",
                    Description = "警告级别的通知"
                },
                new NotificationTypeData
                {
                    Id = "error",
                    Name = "错误",
                    Icon = "❌",
                    Color = "#F44336",
                    Priority = NotificationPriority.High,
                    IsEnabled = true,
                    GroupName = "状态",
                    Description = "错误级别的通知"
                },
                new NotificationTypeData
                {
                    Id = "success",
                    Name = "成功",
                    Icon = "✅",
                    Color = "#16A34A",
                    Priority = NotificationPriority.Medium,
                    IsEnabled = true,
                    GroupName = "状态",
                    Description = "成功提示通知"
                },
                new NotificationTypeData
                {
                    Id = "info",
                    Name = "信息",
                    Icon = "ℹ️",
                    Color = "#9C27B0",
                    Priority = NotificationPriority.Low,
                    IsEnabled = true,
                    GroupName = "状态",
                    Description = "普通信息通知"
                }
            };
        }
    }
}

