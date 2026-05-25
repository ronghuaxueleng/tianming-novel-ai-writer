using System;
using System.Collections.Generic;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Framework.Common.Services
{
    public class MemoryOptimizationService
    {
        private Timer? _gcTimer;
        private Timer? _cacheCleanupTimer;
        private readonly object _lock = new();
        private bool _isRunning;

        private const int GCIntervalMinutes = 5;
        private const int CacheCleanupIntervalMinutes = 10;
        private const long MemoryThresholdMB = 300;
        private const long CriticalMemoryThresholdMB = 600;
        private const int IdleThresholdSeconds = 60;

        private DateTime _lastUserActivity = DateTime.Now;
        private DateTime _startTime = DateTime.Now;
        private int _gcCount = 0;

        private readonly List<Action> _cacheCleanupCallbacks = new();

        public void Start()
        {
            lock (_lock)
            {
                if (_isRunning) return;

                _startTime = DateTime.Now;

                _gcTimer = new Timer(OnGCTimerCallback, null, 
                    TimeSpan.FromMinutes(GCIntervalMinutes), 
                    TimeSpan.FromMinutes(GCIntervalMinutes));

                _cacheCleanupTimer = new Timer(OnCacheCleanupCallback, null,
                    TimeSpan.FromMinutes(CacheCleanupIntervalMinutes),
                    TimeSpan.FromMinutes(CacheCleanupIntervalMinutes));

                _isRunning = true;
                TM.App.Log($"[MemoryOptimization] 服务已启动，GC间隔: {GCIntervalMinutes}分钟，缓存清理间隔: {CacheCleanupIntervalMinutes}分钟");
            }
        }

        public void RegisterCacheCleanup(Action cleanupAction)
        {
            lock (_cacheCleanupCallbacks)
            {
                _cacheCleanupCallbacks.Add(cleanupAction);
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _gcTimer?.Dispose();
                _gcTimer = null;
                _cacheCleanupTimer?.Dispose();
                _cacheCleanupTimer = null;
                _isRunning = false;

                var runTime = DateTime.Now - _startTime;
                TM.App.Log($"[MemoryOptimization] 服务已停止，运行时长: {runTime.TotalHours:F1}小时，GC次数: {_gcCount}");
            }
        }

        private void OnCacheCleanupCallback(object? state)
        {
            try
            {
                List<Action> callbacks;
                lock (_cacheCleanupCallbacks)
                {
                    callbacks = new List<Action>(_cacheCleanupCallbacks);
                }

                foreach (var callback in callbacks)
                {
                    try
                    {
                        callback();
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[MemoryOptimization] 缓存清理回调失败: {ex.Message}");
                    }
                }

                var runTime = DateTime.Now - _startTime;
                if (runTime.TotalHours > 6)
                {
                    var memInfo = GetMemoryInfo();
                    if (memInfo.ManagedMemoryMB > 300)
                    {
                        TM.App.Log($"[MemoryOptimization] 长时间运行({runTime.TotalHours:F1}h)，执行深度清理");
                        OptimizeNow(aggressive: true);
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[MemoryOptimization] 缓存清理失败: {ex.Message}");
            }
        }

        public void NotifyUserActivity()
        {
            _lastUserActivity = DateTime.Now;
        }

        public void OptimizeNow(bool aggressive = false)
        {
            Task.Run(() =>
            {
                try
                {
                    var before = GC.GetTotalMemory(false) / 1024 / 1024;

                    if (aggressive)
                    {
                        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                    }
                    else
                    {
                        GC.Collect(1, GCCollectionMode.Optimized, false);
                    }

                    var after = GC.GetTotalMemory(true) / 1024 / 1024;
                    var freed = before - after;

                    if (freed > 10)
                    {
                        TM.App.Log($"[MemoryOptimization] 内存优化完成: {before}MB → {after}MB (释放 {freed}MB)");
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[MemoryOptimization] 优化失败: {ex.Message}");
                }
            });
        }

        private void OnGCTimerCallback(object? state)
        {
            try
            {
                var currentMemoryMB = GC.GetTotalMemory(false) / 1024 / 1024;
                var idleSeconds = (DateTime.Now - _lastUserActivity).TotalSeconds;

                if (currentMemoryMB > CriticalMemoryThresholdMB)
                {
                    TM.App.Log($"[MemoryOptimization] ⚠️ 内存使用 {currentMemoryMB}MB 超过临界阈值，紧急清理");
                    OptimizeNow(aggressive: true);
                    _gcCount++;
                    return;
                }

                if (currentMemoryMB > MemoryThresholdMB)
                {
                    TM.App.Log($"[MemoryOptimization] 内存使用 {currentMemoryMB}MB 超过阈值，触发清理");
                    OptimizeNow(aggressive: false);
                    _gcCount++;
                    return;
                }

                if (idleSeconds > IdleThresholdSeconds && currentMemoryMB > 200)
                {
                    TM.App.Log($"[MemoryOptimization] 用户空闲 {(int)idleSeconds}秒，触发深度清理");
                    OptimizeNow(aggressive: true);
                    _gcCount++;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[MemoryOptimization] 定时检查失败: {ex.Message}");
            }
        }

        public MemoryInfo GetMemoryInfo()
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            return new MemoryInfo
            {
                ManagedMemoryMB = GC.GetTotalMemory(false) / 1024 / 1024,
                WorkingSetMB = process.WorkingSet64 / 1024 / 1024,
                PrivateMemoryMB = process.PrivateMemorySize64 / 1024 / 1024,
                Gen0Collections = GC.CollectionCount(0),
                Gen1Collections = GC.CollectionCount(1),
                Gen2Collections = GC.CollectionCount(2)
            };
        }
    }

    public class MemoryInfo
    {
        public long ManagedMemoryMB { get; set; }
        public long WorkingSetMB { get; set; }
        public long PrivateMemoryMB { get; set; }
        public int Gen0Collections { get; set; }
        public int Gen1Collections { get; set; }
        public int Gen2Collections { get; set; }

        public override string ToString() => 
            $"托管: {ManagedMemoryMB}MB, 工作集: {WorkingSetMB}MB, GC次数: G0={Gen0Collections}, G1={Gen1Collections}, G2={Gen2Collections}";
    }
}
