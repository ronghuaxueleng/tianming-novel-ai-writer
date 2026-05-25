using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TM.Framework.Common.Services.Factories;

namespace TM.Framework.Common.Helpers.MVVM
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public abstract class BaseSettings<T, TData> : INotifyPropertyChanged
        where T : BaseSettings<T, TData>
        where TData : class
    {
        #region 静态JsonSerializerOptions（复用避免重复分配）

        private static readonly JsonSerializerOptions _readOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        private static readonly JsonSerializerOptions _writeOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        #endregion

        #region R1

        protected readonly IStoragePathHelper _storagePathHelper;
        protected readonly IObjectFactory _objectFactory;

        #endregion

        #region 数据管理

        protected TData Data { get; set; } = default!;

        protected string FilePath { get; set; } = string.Empty;

        protected abstract string GetFilePath();

        protected abstract TData CreateDefaultData();

        protected virtual string GetLogTag() => typeof(T).Name;

        protected BaseSettings(IStoragePathHelper storagePathHelper, IObjectFactory objectFactory)
        {
            _storagePathHelper = storagePathHelper ?? throw new ArgumentNullException(nameof(storagePathHelper));
            _objectFactory = objectFactory ?? throw new ArgumentNullException(nameof(objectFactory));
            FilePath = GetFilePath();
            Data = CreateDefaultData();
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && dispatcher.CheckAccess())
            {
                _ = LoadDataOnBackgroundAsync();
            }
            else
            {
                LoadData();
            }
        }

        private async System.Threading.Tasks.Task LoadDataOnBackgroundAsync()
        {
            string? json = null;
            bool fileExists = false;
            try
            {
                var path = FilePath;
                (fileExists, json) = await System.Threading.Tasks.Task.Run(() =>
                {
                    if (!File.Exists(path)) return (false, (string?)null);
                    return (true, File.ReadAllText(path, Encoding.UTF8));
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetLogTag()}] 后台读取文件失败: {ex.Message}");
            }

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (fileExists && json != null)
                    {
                        var loadedData = JsonSerializer.Deserialize<TData>(json, _readOptions);
                        if (loadedData != null)
                        {
                            Data = loadedData;
                            OnDataLoaded();
                            TM.App.Log($"[{GetLogTag()}] 数据已延迟加载: {FilePath}");
                        }
                        else
                        {
                            TM.App.Log($"[{GetLogTag()}] 反序列化为null，使用默认数据");
                            Data = CreateDefaultData();
                            OnDataLoaded();
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[{GetLogTag()}] 延迟加载失败: {ex.Message}");
                    Data = CreateDefaultData();
                    OnLoadFailed(ex);
                }
                OnPropertyChanged(null);
            });
        }

        #endregion

        #region 序列化/反序列化

        public virtual void LoadData()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath, Encoding.UTF8);
                    var loadedData = JsonSerializer.Deserialize<TData>(json, _readOptions);
                    if (loadedData != null)
                    {
                        Data = loadedData;
                        OnDataLoaded();
                    }
                    else
                    {
                        Data = CreateDefaultData();
                        OnDataLoaded();
                    }
                }
                else
                {
                    Data = CreateDefaultData();
                    OnDataLoaded();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetLogTag()}] 加载失败: {ex.Message}");
                Data = CreateDefaultData();
                OnLoadFailed(ex);
            }
        }

        public virtual void SaveData()
        {
            try
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(Data, _writeOptions);
                var tmp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(tmp, json, Encoding.UTF8);
                File.Move(tmp, FilePath, overwrite: true);

                OnDataSaved();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetLogTag()}] 保存失败: {ex.Message}");
                OnSaveFailed(ex);
                throw;
            }
        }

        public virtual async System.Threading.Tasks.Task LoadDataAsync()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = await File.ReadAllTextAsync(FilePath, Encoding.UTF8);
                    var loadedData = JsonSerializer.Deserialize<TData>(json, _readOptions);
                    if (loadedData != null)
                    {
                        Data = loadedData;
                        OnDataLoaded();
                    }
                    else
                    {
                        Data = CreateDefaultData();
                        OnDataLoaded();
                    }
                }
                else
                {
                    Data = CreateDefaultData();
                    OnDataLoaded();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetLogTag()}] 异步加载失败: {ex.Message}");
                Data = CreateDefaultData();
                OnLoadFailed(ex);
            }
        }

        public virtual async System.Threading.Tasks.Task SaveDataAsync()
        {
            try
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(Data, _writeOptions);
                var tmp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(tmp, json, Encoding.UTF8);
                File.Move(tmp, FilePath, overwrite: true);

                OnDataSaved();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetLogTag()}] 异步保存失败: {ex.Message}");
                OnSaveFailed(ex);
                throw;
            }
        }

        public virtual void ResetToDefaults()
        {
            Data = CreateDefaultData();
            _ = SaveDataAsync();
            OnPropertyChanged(nameof(Data));
            TM.App.Log($"[{GetLogTag()}] 已重置为默认值");
        }

        public virtual async System.Threading.Tasks.Task ResetToDefaultsAsync()
        {
            Data = CreateDefaultData();
            await SaveDataAsync();
            OnPropertyChanged(nameof(Data));
            TM.App.Log($"[{GetLogTag()}] 已异步重置为默认值");
        }

        #endregion

        #region 钩子方法（子类可重写）

        protected virtual void OnDataLoaded() { }

        protected virtual void OnLoadFailed(Exception ex) { }

        protected virtual void OnDataSaved() { }

        protected virtual void OnSaveFailed(Exception ex) { }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<TValue>(ref TValue field, TValue value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<TValue>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }
}

