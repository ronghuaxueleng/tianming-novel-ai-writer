using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.Common.Helpers.MVVM;

namespace TM.Framework.Notifications.SystemNotifications.NotificationTypes
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class NotificationTypesViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly NotificationTypeSettings _settings;
        private ObservableCollection<NotificationTypeData> _types;
        private bool _disposed;

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (!_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[NotificationTypesViewModel] {key}: {ex.Message}");
        }

        public NotificationTypesViewModel(NotificationTypeSettings settings)
        {
            _settings = settings;
            _types = new ObservableCollection<NotificationTypeData>();

            AddTypeCommand = new RelayCommand(() => ExecuteAddType());
            DeleteTypeCommand = new RelayCommand(() => ExecuteDeleteType(), () => CanDeleteType());
            ResetDefaultsCommand = new RelayCommand(() => ExecuteResetDefaults());
            SaveSettingsCommand = new RelayCommand(() => ExecuteSaveSettings());

            _types.CollectionChanged += OnTypesCollectionChanged;

            AsyncSettingsLoader.RunOrDefer(() =>
            {
                var loaded = _settings.LoadSettings();
                return () =>
                {
                    foreach (var type in loaded)
                    {
                        _types.Add(type);
                    }
                    UpdateStatistics();
                    App.Log($"[NotificationTypesViewModel] 初始化完成，加载了 {_types.Count} 个通知类型");
                };
            }, "NotificationTypes");
        }

        #region Commands

        public ICommand AddTypeCommand { get; }
        public ICommand DeleteTypeCommand { get; }
        public ICommand ResetDefaultsCommand { get; }
        public ICommand SaveSettingsCommand { get; }

        private void ExecuteAddType()
        {
            var name = StandardDialog.ShowInput("添加通知类型", "请输入通知类型名称：");
            if (!string.IsNullOrWhiteSpace(name))
            {
                AddType(name, "📌", $"{name}类型的通知");
                GlobalToast.Success("添加成功", $"已添加通知类型：{name}");
            }
        }

        private bool CanDeleteType()
        {
            return Types.Any(t => t.IsSelected);
        }

        private void ExecuteDeleteType()
        {
            var selected = Types.FirstOrDefault(t => t.IsSelected);
            if (selected != null)
            {
                DeleteType(selected);
                GlobalToast.Success("删除成功", $"已删除通知类型：{selected.Name}");
            }
            else
            {
                GlobalToast.Warning("请先选择", "请先点击选中要删除的通知类型");
            }
        }

        private void ExecuteResetDefaults()
        {
            ResetToDefaults();
            GlobalToast.Success("重置成功", "已恢复默认通知类型配置");
        }

        private void ExecuteSaveSettings()
        {
            try
            {
                SaveSettings();
                GlobalToast.Success("保存成功", "通知类型配置已保存");
            }
            catch (Exception ex)
            {
                DebugLogOnce(nameof(ExecuteSaveSettings), ex);
                GlobalToast.Error("保存失败", "无法保存配置，请重试");
            }
        }

        #endregion

        private void OnTypesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (NotificationTypeData item in e.OldItems)
                {
                    item.PropertyChanged -= OnTypePropertyChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (NotificationTypeData item in e.NewItems)
                {
                    item.PropertyChanged += OnTypePropertyChanged;
                }
            }

            UpdateStatistics();
        }

        private void OnTypePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NotificationTypeData.IsEnabled) ||
                e.PropertyName == nameof(NotificationTypeData.GroupName))
            {
                UpdateStatistics();
            }
        }

        public ObservableCollection<NotificationTypeData> Types
        {
            get => _types;
            set
            {
                _types = value;
                OnPropertyChanged(nameof(Types));
                UpdateStatistics();
            }
        }

        public int TotalCount { get; private set; }

        public int EnabledCount { get; private set; }

        public int GroupCount { get; private set; }

        public void AddType(string name, string icon, string description)
        {
            var newType = new NotificationTypeData
            {
                Id = ShortIdGenerator.NewGuid().ToString("N").Substring(0, 8),
                Name = name,
                Icon = icon,
                Description = description,
                Color = "#2196F3",
                Priority = NotificationPriority.Medium,
                IsEnabled = true,
                GroupName = "自定义"
            };

            Types.Add(newType);
            App.Log($"[NotificationTypesViewModel] 添加新类型: {name}");
        }

        public void DeleteType(NotificationTypeData type)
        {
            if (type != null && Types.Contains(type))
            {
                Types.Remove(type);
                App.Log($"[NotificationTypesViewModel] 删除类型: {type.Name}");
            }
        }

        public void ResetToDefaults()
        {
            var defaultTypes = _settings.GetDefaultTypes();
            Types.Clear();

            foreach (var type in defaultTypes)
            {
                Types.Add(type);
            }

            App.Log("[NotificationTypesViewModel] 已重置为默认类型");
        }

        public void SaveSettings()
        {
            try
            {
                _settings.SaveSettings(Types.ToList());
                App.Log($"[NotificationTypesViewModel] 保存配置成功，共 {Types.Count} 个类型");
            }
            catch (Exception ex)
            {
                App.Log($"[NotificationTypesViewModel] 保存配置失败: {ex.Message}");
                throw;
            }
        }

        private void UpdateStatistics()
        {
            TotalCount = Types.Count;
            EnabledCount = Types.Count(t => t.IsEnabled);
            GroupCount = Types.Where(t => !string.IsNullOrWhiteSpace(t.GroupName))
                              .Select(t => t.GroupName)
                              .Distinct()
                              .Count();

            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(EnabledCount));
            OnPropertyChanged(nameof(GroupCount));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                if (_types != null)
                {
                    _types.CollectionChanged -= OnTypesCollectionChanged;

                    foreach (var type in _types)
                    {
                        type.PropertyChanged -= OnTypePropertyChanged;
                    }
                }

                App.Log("[NotificationTypesViewModel] 资源已释放");
            }

            _disposed = true;
        }
    }
}

