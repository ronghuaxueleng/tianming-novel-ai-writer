using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers.Storage;
using TM.Framework.SystemSettings.Logging.LogFormat;
using TM.Framework.SystemSettings.Logging.LogLevel;
using TM.Framework.SystemSettings.Logging.LogOutput;
using TM.Framework.SystemSettings.Logging.LogRotation;

namespace TM.Services.Framework.Settings
{
    public class LogManager
    {
        public static bool IsInitializing { get; private set; }

        private readonly struct LogEntry
        {
            public LogEntry(LogLevelEnum level, string formatted)
            {
                Level = level;
                Formatted = formatted;
            }

            public LogLevelEnum Level { get; }
            public string Formatted { get; }
        }

        private static readonly object _debugLogLock = new();
        private static readonly System.Collections.Generic.HashSet<string> _debugLoggedKeys = new();

        private static readonly Regex _timestampRegex  = new(@"\{timestamp(?::([^}]+))?\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _levelRegex      = new(@"\{level(?::([^}]+))?\}",     RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _messageRegex    = new(@"\{message\}",                RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _callerRegex     = new(@"\{caller\}",                 RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _threadIdRegex   = new(@"\{threadid\}",               RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _processIdRegex  = new(@"\{processid\}",              RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _exceptionRegex  = new(@"\{exception\}",              RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _moduleParseRegex = new(@"^\[(?<module>[^\]]+)\]\s*(?<msg>.*)$", RegexOptions.Compiled);

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (_debugLoggedKeys.Count >= 500 || !_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            Debug.WriteLine($"[LogManager] {key}: {ex.Message}");
        }

        private readonly string _logLevelSettingsPath;
        private readonly string _logFormatSettingsPath;
        private readonly string _logOutputSettingsPath;
        private readonly string _logRotationSettingsPath;

        private readonly Channel<LogEntry> _highPriorityChannel = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
        private readonly Channel<LogEntry> _lowPriorityChannel = Channel.CreateBounded<LogEntry>(
            new BoundedChannelOptions(4096)
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.DropWrite
            });

        private long _droppedLowPriority;
        private DateTime _lastDropReport = DateTime.MinValue;
        private readonly CancellationTokenSource _writerCts = new();
        private readonly Task _writerTask;

        private LogLevelSettings _levelSettings = new();
        private LogFormatSettings _formatSettings = new();
        private LogOutputSettings _outputSettings = new();
        private LogRotationSettings _rotationSettings = new();
        private string? _resolvedLogDir;

        private readonly HashSet<string> _clearedFilesThisSession = new(StringComparer.OrdinalIgnoreCase);

        public LogManager()
        {
            IsInitializing = true;
            try
            {
                _logLevelSettingsPath = StoragePathHelper.GetFilePath(
                    "Framework",
                    "SystemSettings/Logging/LogLevel",
                    "settings.json"
                );

                _logFormatSettingsPath = StoragePathHelper.GetFilePath(
                    "Framework",
                    "SystemSettings/Logging/LogFormat",
                    "settings.json"
                );

                _logOutputSettingsPath = StoragePathHelper.GetFilePath(
                    "Framework",
                    "SystemSettings/Logging/LogOutput",
                    "settings.json"
                );

                _logRotationSettingsPath = StoragePathHelper.GetFilePath(
                    "Framework",
                    "SystemSettings/Logging/LogRotation",
                    "settings.json"
                );

                Reload();
                CleanupOldLogFiles();
            }
            finally
            {
                IsInitializing = false;
            }

            _writerTask = Task.Run(BackgroundWriterLoopAsync);
        }

        public void Flush()
        {
            try
            {
                _highPriorityChannel.Writer.TryComplete();
                _lowPriorityChannel.Writer.TryComplete();
                _writerTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch { }
        }

        public void Reload()
        {
            IsInitializing = true;
            try
            {
                _levelSettings = LoadJsonOrDefault(_logLevelSettingsPath, new LogLevelSettings());
                _formatSettings = LoadJsonOrDefault(_logFormatSettingsPath, new LogFormatSettings());
                _outputSettings = LoadJsonOrDefault(_logOutputSettingsPath, new LogOutputSettings());
                _rotationSettings = LoadJsonOrDefault(_logRotationSettingsPath, new LogRotationSettings());
                _resolvedLogDir = null;
            }
            finally
            {
                IsInitializing = false;
            }
        }

        public void Log(string message)
        {
            try
            {
                var (module, parsedMessage) = TryParseModule(message);
                var level = GuessLevel(parsedMessage);

                if (level == LogLevelEnum.Info && string.IsNullOrWhiteSpace(module))
                {
                    level = LogLevelEnum.Debug;
                }

                if (!ShouldWrite(level, module))
                {
                    return;
                }

                var formatted = Format(level, module, parsedMessage, null);

                if (TM.App.IsDebugMode)
                {
                    Debug.WriteLine(formatted);
                }
                if (TM.App.IsDebugMode && level >= LogLevelEnum.Error)
                {
                    Console.WriteLine(formatted);
                }

                WriteToFile(level, formatted);
            }
            catch (Exception ex)
            {
                DebugLogOnce("WriteLog", ex);
            }
        }

        private void WriteToFile(LogLevelEnum level, string formatted)
        {
            if (!_outputSettings.EnableFileOutput) return;

            var entry = new LogEntry(level, formatted);
            if (level >= LogLevelEnum.Warning)
            {
                _highPriorityChannel.Writer.TryWrite(entry);
                return;
            }

            if (!_lowPriorityChannel.Writer.TryWrite(entry))
            {
                Interlocked.Increment(ref _droppedLowPriority);
            }
        }

        private async Task BackgroundWriterLoopAsync()
        {
            StreamWriter? writer = null;
            string? currentFilePath = null;
            var pendingLines = 0;

            try
            {
                while (true)
                {
                    while (_highPriorityChannel.Reader.TryRead(out var hp))
                    {
                        if (!TryEnsureWriter(ref writer, ref currentFilePath, ref pendingLines))
                            continue;
                        writer!.WriteLine(hp.Formatted);
                        pendingLines = FlushIfNeeded(writer, pendingLines);
                    }

                    while (_lowPriorityChannel.Reader.TryRead(out var lp))
                    {
                        if (!TryEnsureWriter(ref writer, ref currentFilePath, ref pendingLines))
                            continue;
                        writer!.WriteLine(lp.Formatted);
                        pendingLines = FlushIfNeeded(writer, pendingLines);
                    }

                    var dropped = Interlocked.Exchange(ref _droppedLowPriority, 0);
                    if (dropped > 0)
                    {
                        var now = DateTime.Now;
                        if ((now - _lastDropReport).TotalSeconds >= 5)
                        {
                            _lastDropReport = now;
                            if (TryEnsureWriter(ref writer, ref currentFilePath, ref pendingLines))
                            {
                                var line = $"[{now:yyyy-MM-dd HH:mm:ss.fff}] [WRN] [LogManager] 低优先级日志队列已满，已丢弃 {dropped} 条日志";
                                writer!.WriteLine(line);
                                pendingLines = FlushIfNeeded(writer, pendingLines);
                            }
                        }
                        else
                        {
                            Interlocked.Add(ref _droppedLowPriority, dropped);
                        }
                    }

                    if (_highPriorityChannel.Reader.Completion.IsCompleted
                        && _lowPriorityChannel.Reader.Completion.IsCompleted
                        && !_highPriorityChannel.Reader.TryPeek(out _)
                        && !_lowPriorityChannel.Reader.TryPeek(out _))
                    {
                        break;
                    }

                    var hpWait = _highPriorityChannel.Reader.WaitToReadAsync(_writerCts.Token).AsTask();
                    var lpWait = _lowPriorityChannel.Reader.WaitToReadAsync(_writerCts.Token).AsTask();
                    var completed = await Task.WhenAny(hpWait, lpWait).ConfigureAwait(false);
                    _ = await completed.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            finally
            {
                try { writer?.Flush(); writer?.Dispose(); } catch { }
            }
        }

        private bool TryEnsureWriter(ref StreamWriter? writer, ref string? currentFilePath, ref int pendingLines)
        {
            try
            {
                var filePath = ResolveLogFilePath();
                if (filePath == null) return false;

                if (filePath != currentFilePath)
                {
                    writer?.Flush();
                    writer?.Dispose();
                    writer = null;
                    currentFilePath = filePath;
                    pendingLines = 0;
                }

                if (writer != null) return true;

                lock (_clearedFilesThisSession)
                {
                    if (_clearedFilesThisSession.Add(filePath))
                    {
                        try { File.WriteAllText(filePath, string.Empty, Encoding.UTF8); } catch { }
                    }
                }

                writer = new StreamWriter(filePath, append: true, Encoding.UTF8) { AutoFlush = false };
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int FlushIfNeeded(StreamWriter writer, int pendingLines)
        {
            pendingLines++;
            if (pendingLines >= 32)
            {
                writer.Flush();
                return 0;
            }
            return pendingLines;
        }

        private string? ResolveLogFilePath()
        {
            try
            {
                if (_resolvedLogDir == null)
                {
                    var configuredPath = _outputSettings.FileOutputPath;
                    if (string.IsNullOrWhiteSpace(configuredPath))
                        configuredPath = "Logs/application.log";

                    var dir = Path.GetDirectoryName(configuredPath);
                    if (string.IsNullOrWhiteSpace(dir))
                        dir = "Logs";

                    if (!Path.IsPathRooted(dir))
                        dir = Path.Combine(StoragePathHelper.GetStorageRoot(), dir);

                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    _resolvedLogDir = dir;
                }

                var pattern = _outputSettings.FileNamingPattern;
                if (string.IsNullOrWhiteSpace(pattern))
                    pattern = "{date}_{appname}.log";

                var fileName = pattern
                    .Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd"))
                    .Replace("{appname}", "天命");

                return Path.Combine(_resolvedLogDir, fileName);
            }
            catch
            {
                return null;
            }
        }

        private bool ShouldWrite(LogLevelEnum level, string? module)
        {
            var threshold = _levelSettings.GlobalLevel;

            if (!string.IsNullOrWhiteSpace(module) && _levelSettings.ModuleLevels.TryGetValue(module, out var moduleLevel))
            {
                threshold = moduleLevel;
            }

            if (threshold < _levelSettings.MinimumLevel)
            {
                threshold = _levelSettings.MinimumLevel;
            }

            return level >= threshold;
        }

        private string Format(LogLevelEnum level, string? module, string message, Exception? ex)
        {
            var template = string.IsNullOrWhiteSpace(_formatSettings.FormatTemplate)
                ? "[{timestamp}] [{level}] {message}"
                : _formatSettings.FormatTemplate;

            var now = DateTime.Now;

            template = _timestampRegex.Replace(template, m =>
            {
                var fmt = m.Groups[1].Success ? m.Groups[1].Value : _formatSettings.TimestampFormat;
                if (string.IsNullOrWhiteSpace(fmt))
                    fmt = "yyyy-MM-dd HH:mm:ss.fff";
                return now.ToString(fmt);
            });

            template = _levelRegex.Replace(template, m =>
            {
                var fmt = m.Groups[1].Success ? m.Groups[1].Value : string.Empty;
                return string.Equals(fmt, "short", StringComparison.OrdinalIgnoreCase)
                    ? ToShortLevel(level)
                    : level.ToString().ToUpperInvariant();
            });

            template = _messageRegex.Replace(template,   _ => message ?? string.Empty);
            template = _callerRegex.Replace(template,    _ => module ?? string.Empty);
            template = _threadIdRegex.Replace(template,  _ => Environment.CurrentManagedThreadId.ToString());
            template = _processIdRegex.Replace(template, _ => Environment.ProcessId.ToString());
            template = _exceptionRegex.Replace(template, _ => ex?.ToString() ?? string.Empty);

            return template;
        }

        private static string ToShortLevel(LogLevelEnum level)
        {
            return level switch
            {
                LogLevelEnum.Trace => "TRC",
                LogLevelEnum.Debug => "DBG",
                LogLevelEnum.Info => "INF",
                LogLevelEnum.Warning => "WRN",
                LogLevelEnum.Error => "ERR",
                LogLevelEnum.Fatal => "FTL",
                _ => level.ToString().ToUpperInvariant()
            };
        }

        private static (string? Module, string Message) TryParseModule(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return (null, string.Empty);
            }

            var m = _moduleParseRegex.Match(message);
            if (!m.Success)
            {
                return (null, message);
            }

            return (m.Groups["module"].Value.Trim(), m.Groups["msg"].Value);
        }

        private static LogLevelEnum GuessLevel(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return LogLevelEnum.Info;
            }

            if (message.Contains("Fatal", StringComparison.OrdinalIgnoreCase) || message.Contains("致命", StringComparison.OrdinalIgnoreCase))
            {
                return LogLevelEnum.Fatal;
            }

            if (message.Contains("Error", StringComparison.OrdinalIgnoreCase) || message.Contains("失败", StringComparison.OrdinalIgnoreCase) || message.Contains("错误", StringComparison.OrdinalIgnoreCase) || message.Contains("异常", StringComparison.OrdinalIgnoreCase))
            {
                return LogLevelEnum.Error;
            }

            if (message.Contains("Warn", StringComparison.OrdinalIgnoreCase) || message.Contains("警告", StringComparison.OrdinalIgnoreCase))
            {
                return LogLevelEnum.Warning;
            }

            if (message.Contains("清理完成", StringComparison.OrdinalIgnoreCase)
                || message.Contains("目录清理完成", StringComparison.OrdinalIgnoreCase)
                || message.Contains("清理成功", StringComparison.OrdinalIgnoreCase)
                || message.Contains("开始加载视图", StringComparison.OrdinalIgnoreCase)
                || message.Contains("view ok", StringComparison.OrdinalIgnoreCase)
                || message.Contains("开始初始化", StringComparison.OrdinalIgnoreCase)
                || message.Contains("初始化完成", StringComparison.OrdinalIgnoreCase)
                || message.Contains("文件不存在，使用默认数据", StringComparison.OrdinalIgnoreCase)
                || message.Contains("配置文件不存在，使用默认设置", StringComparison.OrdinalIgnoreCase)
                || message.Contains("设置文件不存在，使用默认配置", StringComparison.OrdinalIgnoreCase)
                || message.Contains("数据已加载", StringComparison.OrdinalIgnoreCase)
                || message.Contains("数据已异步加载", StringComparison.OrdinalIgnoreCase)
                || message.Contains("数据已保存", StringComparison.OrdinalIgnoreCase)
                || message.Contains("数据已异步保存", StringComparison.OrdinalIgnoreCase)
                || message.Contains("显示通知", StringComparison.OrdinalIgnoreCase)
                || message.Contains("使用类型配置", StringComparison.OrdinalIgnoreCase)
                || message.Contains("拦截通知", StringComparison.OrdinalIgnoreCase)
                || message.Contains("进度更新", StringComparison.OrdinalIgnoreCase)
                || message.Contains("任务完成", StringComparison.OrdinalIgnoreCase)
                || message.Contains("流式发送", StringComparison.OrdinalIgnoreCase)
                || message.Contains("并行执行批次", StringComparison.OrdinalIgnoreCase)
                || message.Contains("模块启用状态已更新", StringComparison.OrdinalIgnoreCase))
            {
                return LogLevelEnum.Debug;
            }

            if ((message.Contains("-> 200", StringComparison.OrdinalIgnoreCase)
                 || message.Contains("-> 201", StringComparison.OrdinalIgnoreCase)
                 || message.Contains("-> 204", StringComparison.OrdinalIgnoreCase))
                && (message.Contains("GET ", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("POST ", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("PUT ", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("DELETE ", StringComparison.OrdinalIgnoreCase)))
            {
                return LogLevelEnum.Debug;
            }

            if (message.Contains("Debug", StringComparison.OrdinalIgnoreCase))
            {
                return LogLevelEnum.Debug;
            }

            if (message.Contains("Trace", StringComparison.OrdinalIgnoreCase))
            {
                return LogLevelEnum.Trace;
            }

            return LogLevelEnum.Info;
        }

        private void CleanupOldLogFiles()
        {
            try
            {
                if (!_rotationSettings.EnableAutoCleanup) return;

                var configuredPath = _outputSettings.FileOutputPath;
                if (string.IsNullOrWhiteSpace(configuredPath))
                    configuredPath = "Logs/application.log";

                var dir = Path.GetDirectoryName(configuredPath);
                if (string.IsNullOrWhiteSpace(dir)) dir = "Logs";
                if (!Path.IsPathRooted(dir))
                    dir = Path.Combine(StoragePathHelper.GetStorageRoot(), dir);

                if (!Directory.Exists(dir)) return;

                var files = Directory.GetFiles(dir, "*.log")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .ToList();

                if (files.Count == 0) return;

                var maxDays = _rotationSettings.MaxRetainDays > 0 ? _rotationSettings.MaxRetainDays : 7;
                var maxCount = _rotationSettings.MaxRetainCount > 0 ? _rotationSettings.MaxRetainCount : 7;
                var cutoff = DateTime.Now.AddDays(-maxDays);

                int deleted = 0;
                for (int i = 0; i < files.Count; i++)
                {
                    bool shouldDelete = _rotationSettings.CleanupStrategy switch
                    {
                        CleanupStrategy.ByTime => files[i].LastWriteTime < cutoff,
                        CleanupStrategy.ByCount => i >= maxCount,
                        CleanupStrategy.BySize => i >= maxCount || files[i].LastWriteTime < cutoff,
                        _ => files[i].LastWriteTime < cutoff
                    };

                    if (shouldDelete)
                    {
                        try { files[i].Delete(); deleted++; }
                        catch { }
                    }
                }

                if (deleted > 0)
                    Debug.WriteLine($"[LogManager] 启动清理：删除 {deleted} 个旧日志文件（策略={_rotationSettings.CleanupStrategy}, 保留{maxDays}天/{maxCount}个）");
            }
            catch (Exception ex)
            {
                DebugLogOnce("CleanupOldLogs", ex);
            }
        }

        private static T LoadJsonOrDefault<T>(string path, T defaultValue) where T : class
        {
            try
            {
                if (!File.Exists(path))
                {
                    return defaultValue;
                }

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<T>(json) ?? defaultValue;
            }
            catch (Exception ex)
            {
                DebugLogOnce($"LoadConfig:{path}", ex);
                return defaultValue;
            }
        }
    }
}
