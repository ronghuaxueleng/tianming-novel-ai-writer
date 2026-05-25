using System;
using System.Reflection;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using TM.Framework.Common.Helpers;
using TM.Framework.Common.Helpers.MVVM;
using TM.Framework.SystemSettings.Info.Models;

namespace TM.Framework.SystemSettings.Info.RuntimeEnv
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class RuntimeEnvViewModel : INotifyPropertyChanged
    {
        private RuntimeEnvSettings _settings = null!;
        private readonly string _settingsFilePath = null!;

        public event PropertyChangedEventHandler? PropertyChanged;

        public RuntimeEnvViewModel()
        {
            _settings = new RuntimeEnvSettings();
            _settingsFilePath = StoragePathHelper.GetFilePath(
                "Framework",
                "SystemSettings/Info/RuntimeEnv",
                "settings.json"
            );

            AsyncSettingsLoader.LoadOrDefer<RuntimeEnvSettings>(_settingsFilePath, s => { _settings = s; }, "RuntimeEnv");

            AssemblyList = new ObservableCollection<AssemblyItem>();
            EnvironmentVariables = new ObservableCollection<EnvVarItem>();

            RefreshCommand = new RelayCommand(RefreshInfo);
            ExportCommand = new RelayCommand(ExportInfo);

            RefreshInfo();
        }

        private string _runtimeVersion = string.Empty;
        public string RuntimeVersion
        {
            get => _runtimeVersion;
            set { _runtimeVersion = value; OnPropertyChanged(nameof(RuntimeVersion)); }
        }

        private string _clrVersion = string.Empty;
        public string CLRVersion
        {
            get => _clrVersion;
            set { _clrVersion = value; OnPropertyChanged(nameof(CLRVersion)); }
        }

        private string _frameworkDescription = string.Empty;
        public string FrameworkDescription
        {
            get => _frameworkDescription;
            set { _frameworkDescription = value; OnPropertyChanged(nameof(FrameworkDescription)); }
        }

        private string _gcMode = string.Empty;
        public string GCMode
        {
            get => _gcMode;
            set { _gcMode = value; OnPropertyChanged(nameof(GCMode)); }
        }

        private string _currentCulture = string.Empty;
        public string CurrentCulture
        {
            get => _currentCulture;
            set { _currentCulture = value; OnPropertyChanged(nameof(CurrentCulture)); }
        }

        private string _timeZone = string.Empty;
        public string TimeZone
        {
            get => _timeZone;
            set { _timeZone = value; OnPropertyChanged(nameof(TimeZone)); }
        }

        public ObservableCollection<AssemblyItem> AssemblyList { get; } = null!;
        public ObservableCollection<EnvVarItem> EnvironmentVariables { get; } = null!;

        public ICommand RefreshCommand { get; } = null!;
        public ICommand ExportCommand { get; } = null!;

        private void RefreshInfo()
        {
            try
            {
                RuntimeVersion = RuntimeInformation.FrameworkDescription;
                CLRVersion = Environment.Version.ToString();
                FrameworkDescription = RuntimeInformation.RuntimeIdentifier;
                GCMode = System.Runtime.GCSettings.IsServerGC ? "服务器模式" : "工作站模式";
                CurrentCulture = CultureInfo.CurrentCulture.DisplayName;
                TimeZone = TimeZoneInfo.Local.DisplayName;

                LoadAssemblies();
                LoadEnvironmentVariables();

                TM.App.Log($"[RuntimeEnv] 刷新运行环境信息成功");
                GlobalToast.Success("刷新成功", "运行环境信息已更新");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[RuntimeEnv] 刷新失败: {ex.Message}");
                GlobalToast.Error("刷新失败", $"无法刷新运行环境信息: {ex.Message}");
            }
        }

        private void LoadAssemblies()
        {
            try
            {
                AssemblyList.Clear();
                var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic)
                    .OrderBy(a => a.GetName().Name);

                foreach (var assembly in assemblies.Take(100))
                {
                    var name = assembly.GetName();
                    AssemblyList.Add(new AssemblyItem
                    {
                        Name = name.Name ?? "Unknown",
                        Version = name.Version?.ToString() ?? "N/A",
                        Location = assembly.Location
                    });
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[RuntimeEnv] 加载程序集列表失败: {ex.Message}");
            }
        }

        private void LoadEnvironmentVariables()
        {
            try
            {
                EnvironmentVariables.Clear();
                var envVars = Environment.GetEnvironmentVariables();

                foreach (var key in envVars.Keys.Cast<string>().OrderBy(k => k).Take(100))
                {
                    EnvironmentVariables.Add(new EnvVarItem
                    {
                        Name = key,
                        Value = envVars[key]?.ToString() ?? ""
                    });
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[RuntimeEnv] 加载环境变量失败: {ex.Message}");
            }
        }

        private async void ExportInfo()
        {
            try
            {
                var exportPath = StoragePathHelper.GetFilePath(
                    "Framework",
                    "SystemSettings/Info/RuntimeEnv",
                    $"runtime_env_export_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                );

                var sb = new StringBuilder();
                sb.AppendLine("==================== 运行环境信息报告 ====================");
                sb.AppendLine($"运行时版本: {RuntimeVersion}");
                sb.AppendLine($"CLR版本: {CLRVersion}");
                sb.AppendLine($"GC模式: {GCMode}");
                sb.AppendLine($"区域: {CurrentCulture}");
                sb.AppendLine($"时区: {TimeZone}");
                sb.AppendLine();
                sb.AppendLine("【已加载程序集】");
                foreach (var asm in AssemblyList.Take(50))
                {
                    sb.AppendLine($"  {asm.Name} - {asm.Version}");
                }

                await File.WriteAllTextAsync(exportPath, sb.ToString());

                TM.App.Log($"[RuntimeEnv] 导出运行环境信息成功");
                GlobalToast.Success("导出成功", $"运行环境信息已导出");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[RuntimeEnv] 导出失败: {ex.Message}");
                GlobalToast.Error("导出失败", $"无法导出运行环境信息: {ex.Message}");
            }
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class EnvVarItem
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}

