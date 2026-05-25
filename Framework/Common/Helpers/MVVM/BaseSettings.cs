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
using System.Threading;
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

        protected virtual void SetData(TData data) { Data = data; }

        protected string FilePath { get; set; } = string.Empty;

        private System.Threading.Tasks.Task? _initialLoadTask;

        private int _dataMutationVersion;

        private readonly SemaphoreSlim _saveLock = new(1, 1);

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
            if (dispatcher != null)
            {
                _initialLoadTask = LoadDataOnBackgroundAsync();
            }
            else
            {
                _initialLoadTask = LoadDataAsync();
            }
        }

        private async System.Threading.Tasks.Task LoadDataOnBackgroundAsync()
        {
            var loadVersion = Volatile.Read(ref _dataMutationVersion);

            string? json = null;
            bool fileExists = false;
            try
            {
                var path = FilePath;
                if (File.Exists(path))
                {
                    json = await File.ReadAllTextAsync(path, Encoding.UTF8).ConfigureAwait(false);
                    fileExists = true;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetLogTag()}] 后台读取文件失败: {ex.Message}");
            }

            TData? loadedData = null;
            if (fileExists && json != null)
            {
                try
                {
                    loadedData = JsonSerializer.Deserialize<TData>(json, _readOptions);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[{GetLogTag()}] 后台反序列化失败: {ex.Message}");
                }
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                await dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        if (loadVersion != Volatile.Read(ref _dataMutationVersion))
                            return;
                        if (fileExists)
                        {
                            if (loadedData != null)
                            {
                                SetData(loadedData);
                                OnDataLoaded();
                                TM.App.Log($"[{GetLogTag()}] 数据已延迟加载: {FilePath}");
                            }
                            else
                            {
                                TM.App.Log($"[{GetLogTag()}] 反序列化为null，使用默认数据");
                                SetData(CreateDefaultData());
                                OnDataLoaded();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[{GetLogTag()}] 延迟加载失败: {ex.Message}");
                        SetData(CreateDefaultData());
                        OnLoadFailed(ex);
                    }
                    OnPropertyChanged(null);
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        #endregion

        #region 序列化/反序列化

        public virtual void SaveData()
        {
            SaveDataAsync().SafeFireAndForget(ex => TM.App.Log($"[BaseSettings] 保存失败: {ex.Message}"));
        }

        public virtual async System.Threading.Tasks.Task LoadDataAsync()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    await using var stream = File.OpenRead(FilePath);
                    var loadedData = await JsonSerializer.DeserializeAsync<TData>(stream, _readOptions);
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
            var pending = _initialLoadTask;
            if (pending != null)
            {
                try { await pending.ConfigureAwait(false); } catch { }
                _initialLoadTask = null;
            }

            byte[] jsonBytes;
            try
            {
                jsonBytes = JsonSerializer.SerializeToUtf8Bytes(Data, _writeOptions);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetLogTag()}] 序列化失败: {ex.Message}");
                return;
            }

            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var tmp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllBytesAsync(tmp, jsonBytes).ConfigureAwait(false);
                File.Move(tmp, FilePath, overwrite: true);

                OnDataSaved();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{GetLogTag()}] 异步保存失败: {ex.Message} (path={FilePath})");
                OnSaveFailed(ex);
            }
            finally
            {
                _saveLock.Release();
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
            if (propertyName != null)
                Interlocked.Increment(ref _dataMutationVersion);
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

