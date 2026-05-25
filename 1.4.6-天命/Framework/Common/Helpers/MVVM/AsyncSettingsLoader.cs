using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TM.Framework.Common.Helpers.MVVM
{
    public static class AsyncSettingsLoader
    {
        private static readonly JsonSerializerOptions _readOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        public static void LoadOrDefer<T>(string filePath, Action<T> onLoaded, string logTag) where T : class, new()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && dispatcher.CheckAccess())
            {
                _ = LoadOnBackgroundAsync(filePath, onLoaded, logTag);
            }
            else
            {
                LoadSync(filePath, onLoaded, logTag);
            }
        }

        private static async System.Threading.Tasks.Task LoadOnBackgroundAsync<T>(
            string filePath, Action<T> onLoaded, string logTag) where T : class, new()
        {
            T data;

            try
            {
                if (!File.Exists(filePath))
                {
                    TM.App.Log($"[{logTag}] 设置文件不存在，使用默认配置: {filePath}");
                    data = new T();
                }
                else
                {
                    var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8).ConfigureAwait(false);
                    data = JsonSerializer.Deserialize<T>(json, _readOptions) ?? new T();
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{logTag}] 后台加载失败: {ex.Message}");
                data = new T();
            }

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    onLoaded(data);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[{logTag}] UI回调失败: {ex.Message}");
                }
            });
        }

        private static void LoadSync<T>(string filePath, Action<T> onLoaded, string logTag) where T : class, new()
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    onLoaded(new T());
                    TM.App.Log($"[{logTag}] 设置文件不存在，使用默认配置: {filePath}");
                    return;
                }

                var json = File.ReadAllText(filePath, Encoding.UTF8);
                var data = JsonSerializer.Deserialize<T>(json, _readOptions) ?? new T();
                onLoaded(data);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{logTag}] 同步加载失败: {ex.Message}");
                try
                {
                    onLoaded(new T());
                }
                catch (Exception ex2)
                {
                    TM.App.Log($"[{logTag}] 默认配置回调失败: {ex2.Message}");
                }
            }
        }

        public static void RunOrDefer(Func<Action?> work, string logTag)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && dispatcher.CheckAccess())
            {
                _ = RunOnBackgroundAsync(work, logTag);
            }
            else
            {
                try
                {
                    var uiAction = work();
                    uiAction?.Invoke();
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[{logTag}] 同步执行失败: {ex.Message}");
                }
            }
        }

        private static async System.Threading.Tasks.Task RunOnBackgroundAsync(Func<Action?> work, string logTag)
        {
            Action? uiAction = null;
            try
            {
                uiAction = await System.Threading.Tasks.Task.Run(work).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[{logTag}] 后台执行失败: {ex.Message}");
            }

            if (uiAction == null) return;

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    uiAction();
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[{logTag}] UI回调失败: {ex.Message}");
                }
            });
        }
    }
}
