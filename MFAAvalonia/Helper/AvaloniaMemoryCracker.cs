using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.Helper;

/// <summary>
/// 高性能内存清理器，针对 Avalonia 应用优化
/// 使用后台线程阻塞式 GC 策略，确保清理效果同时避免 UI 卡顿
/// </summary>
public class AvaloniaMemoryCracker : IDisposable
{
    #region 平台相关API

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    #endregion

    #region 配置常量

    // 内存优化阈值（可配置）
    private const long MemoryThresholdBytes = 256 * 1024 * 1024; // 256MB
    private const long HighMemoryPressureThreshold = 512 * 1024 * 1024; // 512MB - 高内存压力阈值
    private const long CriticalMemoryPressureThreshold = 1024 * 1024 * 1024; // 1GB - 临界内存压力阈值
    private const long EmergencyMemoryThreshold = 1536 * 1024 * 1024; // 1.5GB - 紧急内存阈值

    // 内存历史记录配置
    private const int MaxHistoryCount = 10;

    // LOH 压缩间隔（每N次清理执行一次LOH压缩）
    private const int LohCompactionInterval = 5;

    // 用户空闲时间阈值（毫秒）- 超过此时间认为用户空闲
    private const int UserIdleThresholdMs = 2000; // 2秒

    // 清理效果验证的最小释放比例（如果释放内存少于此比例，尝试更激进的策略）
    private const double MinEffectiveCleanupRatio = 0.1; // 10%

    // 连续无效清理次数阈值（超过此次数后强制执行激进清理）
    private const int MaxIneffectiveCleanupCount = 3;

    #endregion

    #region 字段

    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;
    private Task? _monitorTask;

    // 专用 GC 线程（用于执行阻塞式 GC，避免阻塞 UI）
    private readonly Thread _gcThread;
    private readonly AutoResetEvent _gcTrigger = new(false);
    private readonly ManualResetEventSlim _gcComplete = new(true);
    private volatile GcRequest? _pendingGcRequest;

    // 内存历史记录（用于诊断泄漏趋势）
    private readonly Queue<(DateTime Time, long Memory)> _memoryHistory = new();
    private readonly object _historyLock = new();

    // 清理计数器（用于控制LOH压缩频率）
    private int _cleanupCount;
    // 上次清理时间（用于自适应清理间隔）
    private DateTime _lastCleanupTime = DateTime.MinValue;

    // 上次内存使用量（用于判断是否需要清理）
    private long _lastMemoryUsage;

    // 上次激进 GC 时间（避免频繁执行）
    private DateTime _lastAggressiveGcTime = DateTime.MinValue;

    // 连续无效清理计数
    private int _ineffectiveCleanupCount;

    // 已注册的可释放资源（用于管理非托管资源）
    private readonly ConcurrentDictionary<string, WeakReference<IDisposable>> _registeredDisposables = new();

    #endregion

    #region GC 请求结构

    private class GcRequest
    {
        public GcIntensity Intensity { get; init; }
        public bool CompactLoh { get; init; }
        public long BeforeMemory { get; init; }
        public TaskCompletionSource<GcResult> Completion { get; } = new();
    }

    private enum GcIntensity
    {
        Light, // 轻量级：Gen0/Gen1
        Medium, // 中等：Gen2 非压缩
        Heavy, // 重度：Gen2 +压缩
        Aggressive // 激进：完整 GC + LOH 压缩+ 终结器
    }

    private readonly struct GcResult
    {
        public long FreedMemory { get; init; }
        public long AfterMemory { get; init; }
        public bool Success { get; init; }
    }

    #endregion

    #region 构造函数

    public AvaloniaMemoryCracker()
    {
        // 创建专用 GC 线程，设置为后台线程
        _gcThread = new Thread(GcThreadLoop)
        {
            Name = "MemoryCracker-GC",
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal // 低优先级，避免影响 UI
        };
        _gcThread.Start();
    }

    #endregion

    #region GC 线程

    /// <summary>GC 线程主循环 - 在独立线程执行阻塞式 GC</summary>
    private void GcThreadLoop()
    {
        while (!_disposed)
        {
            try
            {
                // 等待 GC 请求
                if (!_gcTrigger.WaitOne(1000))
                    continue;

                var request = _pendingGcRequest;
                if (request == null)
                    continue;

                _gcComplete.Reset();

                try
                {
                    var result = ExecuteGcOnDedicatedThread(request);
                    request.Completion.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    LoggerHelper.Warning($"[内存管理]GC线程执行异常: {ex.Message}");
                    request.Completion.TrySetResult(new GcResult
                    {
                        Success = false
                    });
                }
                finally
                {
                    _pendingGcRequest = null;
                    _gcComplete.Set();
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.Warning($"[内存管理]GC线程循环异常: {ex.Message}");
            }
        }
    }

    /// <summary>在专用线程上执行 GC（可以使用阻塞模式）</summary>
    private GcResult ExecuteGcOnDedicatedThread(GcRequest request)
    {
        var beforeMemory = request.BeforeMemory;

        try
        {
            switch (request.Intensity)
            {
                case GcIntensity.Light:
                    // 轻量级：仅 Gen0/Gen1，非阻塞
                    GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
                    break;

                case GcIntensity.Medium:
                    // 中等：Gen2，阻塞但不压缩（在后台线程阻塞不影响 UI）
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(1, GCCollectionMode.Forced, blocking: true, compacting: false);
                    break;

                case GcIntensity.Heavy:
                    // 重度：Gen2 + 压缩
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                    break;

                case GcIntensity.Aggressive:
                    // 激进：完整清理
                    if (request.CompactLoh)
                    {
                        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    }

                    // 第一轮：强制完整 GC
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                    GC.WaitForPendingFinalizers();

                    // 第二轮：清理终结器释放的对象
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                    GC.WaitForPendingFinalizers();

                    // 第三轮：最终清理
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                    break;
            }

            // 执行平台特定优化
            PerformPlatformSpecificOptimization(request.Intensity >= GcIntensity.Heavy);

            var afterMemory = GC.GetTotalMemory(false);
            return new GcResult
            {
                FreedMemory = beforeMemory - afterMemory,
                AfterMemory = afterMemory,
                Success = true
            };
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"[内存管理]GC执行失败: {ex.Message}");
            return new GcResult
            {
                Success = false,
                AfterMemory = GC.GetTotalMemory(false)
            };
        }
    }

    /// <summary>请求在后台线程执行 GC</summary>
    private async Task<GcResult> RequestGcAsync(GcIntensity intensity, bool compactLoh = false)
    {
        var beforeMemory = GC.GetTotalMemory(false);

        // 如果上一次 GC 还在执行，等待完成
        if (!_gcComplete.Wait(5000))
        {
            LoggerHelper.Warning("[内存管理]等待上次GC超时");
            return new GcResult
            {
                Success = false,
                AfterMemory = beforeMemory
            };
        }

        var request = new GcRequest
        {
            Intensity = intensity,
            CompactLoh = compactLoh,
            BeforeMemory = beforeMemory
        };

        _pendingGcRequest = request;
        _gcTrigger.Set();

        try
        {
            // 等待 GC 完成，设置超时
            var timeout = intensity switch
            {
                GcIntensity.Light => 2000,
                GcIntensity.Medium => 5000,
                GcIntensity.Heavy => 10000,
                GcIntensity.Aggressive => 15000,
                _ => 5000
            };

            using var cts = new CancellationTokenSource(timeout);
            return await request.Completion.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            LoggerHelper.Warning($"[内存管理]GC请求超时 (强度: {intensity})");
            return new GcResult
            {
                Success = false,
                AfterMemory = GC.GetTotalMemory(false)
            };
        }
    }

    #endregion

    #region 核心逻辑

    /// <summary>启动内存优化守护进程</summary>
    /// <param name="intervalSeconds">基础清理间隔秒数（默认30秒），实际间隔会根据内存压力自适应调整</param>
    public void Cracker(int intervalSeconds = 30)
    {
        _monitorTask = Task.Run(async () =>
        {
            // 初始延迟，避免在应用启动的关键阶段干扰资源加载
            try { await Task.Delay(5000, _cts.Token); } catch { return; }

            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var currentMemory = GetCurrentMemoryUsage();
                    var memoryInfo = GetMemoryPressureInfo();

                    // 根据内存压力决定是否需要清理
                    var shouldCleanup = ShouldPerformCleanup(currentMemory, memoryInfo);

                    if (shouldCleanup)
                    {
                        var beforeMemory = currentMemory;
                        await PerformMemoryCleanupAsync(memoryInfo);
                        var afterMemory = GetCurrentMemoryUsage();

                        // 记录内存变化用于诊断
                        RecordMemorySnapshot(afterMemory);

                        // 验证清理效果
                        ValidateCleanupEffectiveness(beforeMemory, afterMemory, memoryInfo);

                        _lastCleanupTime = DateTime.UtcNow;
                        _lastMemoryUsage = afterMemory;
                    }

                    // 自适应清理间隔：内存压力越大，间隔越短
                    var adaptiveInterval = CalculateAdaptiveInterval(intervalSeconds, memoryInfo);
                    await Task.Delay(TimeSpan.FromSeconds(adaptiveInterval), _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LoggerHelper.Warning($"[内存管理]内存清理异常: {ex.Message}"); // 发生异常时使用默认间隔
                    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), _cts.Token).ConfigureAwait(false);
                }
            }
        }, _cts.Token);
    }

    /// <summary>判断是否需要执行清理</summary>
    private bool ShouldPerformCleanup(long currentMemory, MemoryPressureInfo memoryInfo)
    {
        // 如果内存超过阈值，需要清理
        if (currentMemory > MemoryThresholdBytes)
            return true;

        // 如果内存压力高，需要清理
        if (memoryInfo.MemoryLoadPercentage > 70)
            return true;

        // 如果距离上次清理时间过长（超过5分钟），执行一次清理
        if ((DateTime.UtcNow - _lastCleanupTime).TotalMinutes > 5) return true;

        // 如果内存增长超过上次的50%，需要清理
        if (_lastMemoryUsage > 0 && currentMemory > _lastMemoryUsage * 1.5)
            return true;

        return false;
    }

    /// <summary>验证清理效果并调整策略</summary>
    private void ValidateCleanupEffectiveness(long beforeMemory, long afterMemory, MemoryPressureInfo memoryInfo)
    {
        var freedMemory = beforeMemory - afterMemory;
        var freedRatio = beforeMemory > 0 ? (double)freedMemory / beforeMemory : 0;
        var freedMB = freedMemory / (1024.0 * 1024.0);

        if (freedMemory > 1024 * 1024) // 只记录释放超过1MB的情况
        {
            LoggerHelper.Info($"[内存管理]释放了{freedMB:F2} MB ({freedRatio * 100:F1}%)");
            _ineffectiveCleanupCount = 0; // 重置无效计数
        }
        else if (beforeMemory > HighMemoryPressureThreshold)
        {
            // 高内存但清理效果差
            _ineffectiveCleanupCount++;
            LoggerHelper.Info($"[内存管理]清理效果不佳 (释放 {freedMB:F2} MB)，连续无效次数: {_ineffectiveCleanupCount}");

            // 如果连续多次清理无效，下次将使用更激进的策略
            if (_ineffectiveCleanupCount >= MaxIneffectiveCleanupCount)
            {
                LoggerHelper.Info("[内存管理]连续清理无效，将在下次使用激进策略");
            }
        }

        // 检查内存是否持续增长（泄漏预警）
        CheckMemoryLeakTrend(beforeMemory, afterMemory);
    }

    /// <summary>计算自适应清理间隔</summary>
    private int CalculateAdaptiveInterval(int baseInterval, MemoryPressureInfo memoryInfo)
    {
        // 根据内存压力调整间隔
        if (memoryInfo.TotalMemory > EmergencyMemoryThreshold)
            return Math.Max(5, baseInterval / 4); // 紧急：间隔缩短到1/4，最少5秒
        if (memoryInfo.MemoryLoadPercentage > 90 || memoryInfo.TotalMemory > CriticalMemoryPressureThreshold)
            return Math.Max(10, baseInterval / 2); // 高压力：间隔缩短到1/2，最少10秒
        if (memoryInfo.MemoryLoadPercentage > 70 || memoryInfo.TotalMemory > HighMemoryPressureThreshold)
            return Math.Max(15, baseInterval * 2 / 3); // 中等压力：间隔缩短到2/3
        if (memoryInfo.MemoryLoadPercentage < 30 && memoryInfo.TotalMemory < MemoryThresholdBytes)
            return baseInterval * 2; // 低压力：间隔延长到2倍

        return baseInterval;
    }

    /// <summary>记录内存快照用于趋势分析</summary>
    private void RecordMemorySnapshot(long memory)
    {
        lock (_historyLock)
        {
            _memoryHistory.Enqueue((DateTime.UtcNow, memory));
            while (_memoryHistory.Count > MaxHistoryCount)
            {
                _memoryHistory.Dequeue();
            }
        }
    }

    /// <summary>检测内存泄漏趋势</summary>
    private void CheckMemoryLeakTrend(long beforeCleanup, long afterCleanup)
    {
        // 检查内存是否持续增长（泄漏预警）
        lock (_historyLock)
        {
            if (_memoryHistory.Count >= 5)
            {
                var snapshots = _memoryHistory.ToArray();
                var firstMemory = snapshots[0].Memory;
                var lastMemory = snapshots[^1].Memory;

                if (firstMemory > 200 * 1024 * 1024) // 只在内存超过200MB时检测
                {
                    var growthRate = (lastMemory - firstMemory) / (double)firstMemory;

                    // 如果在多次清理后内存仍持续增长超过 50%，发出警告
                    if (growthRate > 0.5)
                    {
                        LoggerHelper.Warning($"[内存管理]检测到潜在内存泄漏:内存从 {firstMemory / (1024 * 1024)} MB 增长到 {lastMemory / (1024 * 1024)} MB (增长 {growthRate * 100:F1}%)");
                    }
                }
            }
        }
    }

    /// <summary>执行内存清理策略（异步，在后台线程执行阻塞式GC）</summary>
    private async Task PerformMemoryCleanupAsync(MemoryPressureInfo memoryInfo)
    {
        _cleanupCount++;

        var isUserIdle = IsUserIdle();
        var timeSinceLastAggressiveGc = DateTime.UtcNow - _lastAggressiveGcTime;
        var shouldCompactLoh = _cleanupCount % LohCompactionInterval == 0;

        // 根据内存压力和清理效果选择策略
        GcIntensity intensity;

        if (_ineffectiveCleanupCount >= MaxIneffectiveCleanupCount && memoryInfo.TotalMemory > HighMemoryPressureThreshold)
        {
            // 连续清理无效且内存高：强制使用激进策略
            intensity = GcIntensity.Aggressive;
            _ineffectiveCleanupCount = 0; // 重置计数
            LoggerHelper.Info("[内存管理]由于连续清理无效，执行激进清理");
        }
        else if (memoryInfo.TotalMemory > EmergencyMemoryThreshold)
        {
            // 紧急情况：内存超过 1.5GB
            intensity = GcIntensity.Aggressive;
            LoggerHelper.Info($"[内存管理]内存紧急({memoryInfo.TotalMemory / (1024 * 1024)} MB)，执行激进清理");
        }
        else if (memoryInfo.TotalMemory > CriticalMemoryPressureThreshold)
        {
            // 临界情况：内存超过 1GB
            intensity = isUserIdle ? GcIntensity.Aggressive : GcIntensity.Heavy;
            LoggerHelper.Info($"[内存管理]内存临界({memoryInfo.TotalMemory / (1024 * 1024)} MB)，执行{(isUserIdle ? "激进" : "重度")}清理");
        }
        else if (memoryInfo.TotalMemory > HighMemoryPressureThreshold)
        {
            // 高压力：内存超过 512MB
            if (isUserIdle && timeSinceLastAggressiveGc.TotalMinutes > 2)
            {
                intensity = GcIntensity.Heavy;
            }
            else
            {
                intensity = GcIntensity.Medium;
            }
        }
        else if (memoryInfo.TotalMemory > MemoryThresholdBytes)
        {
            // 中等压力：内存超过 256MB
            intensity = isUserIdle ? GcIntensity.Medium : GcIntensity.Light;
        }
        else
        {
            // 低压力
            intensity = GcIntensity.Light;
        }

        // 在后台线程执行 GC
        var result = await RequestGcAsync(intensity, shouldCompactLoh && intensity >= GcIntensity.Heavy);

        if (result.Success && intensity >= GcIntensity.Heavy)
        {
            _lastAggressiveGcTime = DateTime.UtcNow;
        }

        // 如果是激进清理，额外等待一下让系统稳定
        if (intensity == GcIntensity.Aggressive)
        {
            await Task.Delay(100, _cts.Token).ConfigureAwait(false);
        }

        // 在高内存压力时，清理已失效的弱引用
        if (intensity >= GcIntensity.Heavy)
        {
            CleanupDeadReferences();
        }
    }

    /// <summary>检测用户是否空闲</summary>
    private static bool IsUserIdle()
    {
        if (!OperatingSystem.IsWindows())
        {
            // 非 Windows 平台，假设用户不空闲，使用保守策略
            return false;
        }

        try
        {
            var lastInputInfo = new LASTINPUTINFO
            {
                cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>()
            };
            if (GetLastInputInfo(ref lastInputInfo))
            {
                var idleTime = (uint)Environment.TickCount - lastInputInfo.dwTime;
                return idleTime > UserIdleThresholdMs;
            }
        }
        catch
        {
            // 忽略异常
        }

        return false;
    }

    /// <summary>
    /// 注册一个可释放的资源，在内存清理时会检查并清理已失效的引用
    /// 使用弱引用避免阻止对象被 GC 回收
    /// </summary>
    /// <param name="key">资源的唯一标识符</param>
    /// <param name="disposable">要注册的可释放资源</param>
    public void RegisterDisposable(string key, IDisposable disposable)
    {
        if (string.IsNullOrEmpty(key) || disposable == null)
            return;

        _registeredDisposables[key] = new WeakReference<IDisposable>(disposable);
    }

    /// <summary>
    /// 取消注册一个可释放的资源
    /// </summary>
    /// <param name="key">资源的唯一标识符</param>
    public void UnregisterDisposable(string key)
    {
        if (string.IsNullOrEmpty(key))
            return;

        _registeredDisposables.TryRemove(key, out _);
    }

    /// <summary>
    /// 强制释放指定的已注册资源
    /// </summary>
    /// <param name="key">资源的唯一标识符</param>
    /// <returns>是否成功释放</returns>
    public bool ForceDisposeResource(string key)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        if (_registeredDisposables.TryRemove(key, out var weakRef))
        {
            if (weakRef.TryGetTarget(out var disposable))
            {
                try
                {
                    disposable.Dispose();
                    LoggerHelper.Info($"[内存管理]已强制释放资源: {key}");
                    return true;
                }
                catch (Exception ex)
                {
                    LoggerHelper.Warning($"[内存管理]释放资源 {key} 时发生异常: {ex.Message}");
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 强制释放所有已注册的资源
    /// 适用于应用程序退出或需要紧急释放内存的场景
    /// </summary>
    public void ForceDisposeAllResources()
    {
        var keys = _registeredDisposables.Keys.ToArray();
        var disposedCount = 0;
        var failedCount = 0;

        foreach (var key in keys)
        {
            if (_registeredDisposables.TryRemove(key, out var weakRef))
            {
                if (weakRef.TryGetTarget(out var disposable))
                {
                    try
                    {
                        disposable.Dispose();
                        disposedCount++;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        LoggerHelper.Warning($"[内存管理]释放资源 {key} 时发生异常: {ex.Message}");
                    }
                }
            }
        }

        if (disposedCount > 0 || failedCount > 0)
        {
            LoggerHelper.Info($"[内存管理]强制释放资源完成: 成功 {disposedCount} 个, 失败 {failedCount} 个");
        }
    }

    /// <summary>
    /// 清理已失效的弱引用（对象已被 GC 回收）
    /// </summary>
    private void CleanupDeadReferences()
    {
        var deadKeys = new List<string>();

        foreach (var kvp in _registeredDisposables)
        {
            if (!kvp.Value.TryGetTarget(out _))
            {
                deadKeys.Add(kvp.Key);
            }
        }

        foreach (var key in deadKeys)
        {
            _registeredDisposables.TryRemove(key, out _);
        }

        if (deadKeys.Count > 0)
        {
            LoggerHelper.Info($"[内存管理]清理了 {deadKeys.Count} 个已失效的资源引用");
        }
    }

    /// <summary>执行平台特定优化</summary>
    private static void PerformPlatformSpecificOptimization(bool aggressive)
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsMemoryOptimization(aggressive);
        }
        // Linux 和 macOS 的优化通过 GC 已经处理
    }

    #endregion

    #region 平台特定实现

    /// <summary>Windows平台优化（工作集调整）</summary>
    /// <param name="aggressive">是否使用激进模式（已禁用，因为会导致严重卡顿）</param>
    private static void WindowsMemoryOptimization(bool aggressive)
    {
        try
        {
            var processHandle = GetCurrentProcess();

            // 注意：EmptyWorkingSet 会导致大量页面错误，造成后续操作严重卡顿
            // 因此我们只使用 SetProcessWorkingSetSize 来提示系统可以回收内存
            // 这样既能降低内存占用，又不会导致严重的性能问题
            SetProcessWorkingSetSize(processHandle, (IntPtr)(-1), (IntPtr)(-1));
        }
        catch (Exception ex)
        {
            LoggerHelper.Info($"[内存管理]Windows内存优化失败: {ex.Message}");
        }
    }

    #endregion

    #region 内存信息获取

    /// <summary>获取当前进程内存占用（字节）</summary>
    private static long GetCurrentMemoryUsage()
    {
        try
        {
            return GC.GetTotalMemory(false);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>获取内存压力信息</summary>
    private static MemoryPressureInfo GetMemoryPressureInfo()
    {
        try
        {
            var gcMemoryInfo = GC.GetGCMemoryInfo();
            var totalMemory = GC.GetTotalMemory(false);

            // 计算内存负载百分比
            var memoryLoadPercentage = gcMemoryInfo.HighMemoryLoadThresholdBytes > 0
                ? (int)((double)gcMemoryInfo.MemoryLoadBytes / gcMemoryInfo.HighMemoryLoadThresholdBytes * 100)
                : 0;

            return new MemoryPressureInfo
            {
                TotalMemory = totalMemory,
                HeapSize = gcMemoryInfo.HeapSizeBytes,
                FragmentedBytes = gcMemoryInfo.FragmentedBytes,
                MemoryLoadBytes = gcMemoryInfo.MemoryLoadBytes,
                HighMemoryLoadThreshold = gcMemoryInfo.HighMemoryLoadThresholdBytes,
                MemoryLoadPercentage = Math.Min(100, Math.Max(0, memoryLoadPercentage)),
                PinnedObjectsCount = gcMemoryInfo.PinnedObjectsCount,
                Generation0Count = GC.CollectionCount(0),
                Generation1Count = GC.CollectionCount(1),
                Generation2Count = GC.CollectionCount(2)
            };
        }
        catch
        {
            return new MemoryPressureInfo
            {
                TotalMemory = GC.GetTotalMemory(false),
                MemoryLoadPercentage = 50 // 默认中等压力
            };
        }
    }

    /// <summary>内存压力信息</summary>
    private readonly struct MemoryPressureInfo
    {
        public long TotalMemory { get; init; }
        public long HeapSize { get; init; }
        public long FragmentedBytes { get; init; }
        public long MemoryLoadBytes { get; init; }
        public long HighMemoryLoadThreshold { get; init; }
        public int MemoryLoadPercentage { get; init; }
        public long PinnedObjectsCount { get; init; }
        public int Generation0Count { get; init; }
        public int Generation1Count { get; init; }
        public int Generation2Count { get; init; }
    }

    #endregion

    #region 资源释放

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        _disposed = true;

        if (disposing)
        {
            _cts.Cancel();

            // 释放所有已注册的可释放资源
            ForceDisposeAllResources();

            // 唤醒 GC 线程让它退出
            _gcTrigger.Set();

            // 等待监控任务完成
            try
            {
                _monitorTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // 忽略等待异常
            }

            // 等待 GC 线程退出
            try
            {
                _gcThread.Join(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // 忽略
            }

            _cts.Dispose();
            _gcTrigger.Dispose();
            _gcComplete.Dispose();
        }
    }

    ~AvaloniaMemoryCracker() => Dispose(false);

    #endregion
}
