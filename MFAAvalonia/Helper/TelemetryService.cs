using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.ViewModels.Windows;
using Sentry;
using Sentry.Protocol;
using System;
using System.Collections.Generic;

namespace MFAAvalonia.Helper;

public static class TelemetryService
{
    private sealed class RunState
    {
        public required ITransactionTracer Transaction { get; init; }
        public Dictionary<MFATask, ISpan> Tasks { get; } = new();
        public MFATask? ActiveTask { get; set; }
        public bool HasFailed { get; set; }
        public int FailedNodeCount { get; set; }
    }

    private const int MaxFailedNodesPerRun = 32;
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, RunState> Runs = new();
    private static IDisposable? _sdk;

    public static bool IsActive
    {
        get
        {
            lock (SyncRoot)
                return _sdk != null;
        }
    }

    public static void InitializeFromInterface()
    {
        var config = MaaProcessor.Interface?.Telemetry?.Sentry;
        if (config == null || string.IsNullOrWhiteSpace(config.Dsn))
        {
            LoggerHelper.Info("[Telemetry] interface 未配置 Sentry DSN，跳过初始化");
            return;
        }

        if (!IsUserEnabled() || IsBlockedBuild(MaaProcessor.Interface?.Version))
        {
            LoggerHelper.Info("[Telemetry] 用户已关闭或当前为调试资源，跳过初始化");
            return;
        }

        lock (SyncRoot)
        {
            if (_sdk != null)
                return;

            try
            {
                var resourceName = MaaProcessor.Interface?.Name ?? "unknown";
                var resourceVersion = MaaProcessor.Interface?.Version ?? "0.0.0";
                var tracing = config.Tracing ?? true;
                var sampleRate = Math.Clamp(config.TracesSampleRate ?? 1.0, 0.0, 1.0);

                _sdk = SentrySdk.Init(options =>
                {
                    options.Dsn = config.Dsn;
                    options.SendDefaultPii = false;
                    options.AutoSessionTracking = true;
                    options.IsGlobalModeEnabled = true;
                    options.ShutdownTimeout = TimeSpan.FromSeconds(1);
                    options.Release = $"MFA@{RootViewModel.Version}+{resourceName}@{resourceVersion}";
                    options.Environment = string.IsNullOrWhiteSpace(config.Environment)
                        ? GetDefaultEnvironment(resourceVersion)
                        : config.Environment;
                    options.TracesSampleRate = tracing ? sampleRate : 0;
                });

                SentrySdk.ConfigureScope(scope =>
                {
                    scope.SetTag("resource.name", resourceName);
                    scope.SetTag("resource.version", resourceVersion);
                    scope.SetTag("mfa.version", RootViewModel.Version);
                });
                LoggerHelper.Info("[Telemetry] Sentry 已初始化");
            }
            catch (Exception ex)
            {
                _sdk?.Dispose();
                _sdk = null;
                LoggerHelper.Warning($"[Telemetry] Sentry 初始化失败：{ex.Message}");
            }
        }
    }

    public static void SetEnabled(bool enabled)
    {
        GlobalConfiguration.SetValue(ConfigurationKeys.HelpImproveSoftware, enabled.ToString());
        if (enabled)
            InitializeFromInterface();
        else
            Shutdown();
    }

    public static void CaptureException(Exception exception, string source)
    {
        if (!IsActive)
            return;

        using (SentrySdk.PushScope())
        {
            SentrySdk.ConfigureScope(scope => scope.SetTag("exception.source", source));
            SentrySdk.CaptureException(exception);
        }
    }

    public static void StartRun(string instanceId, int taskCount)
    {
        if (!IsActive)
            return;

        lock (SyncRoot)
        {
            FinishRunLocked(instanceId, SpanStatus.Cancelled);
            var transaction = SentrySdk.StartTransaction("task-list", "mfa.task-list");
            transaction.SetData("task.count", taskCount);
            Runs[instanceId] = new RunState { Transaction = transaction };
        }
    }

    public static void StartTask(string instanceId, MFATask task)
    {
        lock (SyncRoot)
        {
            if (!Runs.TryGetValue(instanceId, out var run))
                return;

            var name = task.SourceItem?.InterfaceItem?.Name ?? task.Name ?? "unknown";
            var span = run.Transaction.StartChild("mfa.task", name);
            span.SetData("repeat.count", task.Count);
            run.Tasks[task] = span;
            run.ActiveTask = task;
        }
    }

    public static void FinishTask(string instanceId, MFATask task, MFATask.MFATaskStatus status)
    {
        lock (SyncRoot)
        {
            if (!Runs.TryGetValue(instanceId, out var run) || !run.Tasks.Remove(task, out var span))
                return;

            var spanStatus = ToSpanStatus(status);
            span.Status = spanStatus;
            span.SetData("result", ToResult(status));
            span.Finish();
            if (status == MFATask.MFATaskStatus.FAILED)
                run.HasFailed = true;
            if (ReferenceEquals(run.ActiveTask, task))
                run.ActiveTask = null;
        }
    }

    public static void RecordFailedNode(string instanceId, string? nodeName)
    {
        lock (SyncRoot)
        {
            if (!Runs.TryGetValue(instanceId, out var run)
                || run.ActiveTask == null
                || !run.Tasks.TryGetValue(run.ActiveTask, out var taskSpan)
                || run.FailedNodeCount++ >= MaxFailedNodesPerRun)
                return;

            var span = taskSpan.StartChild("mfa.pipeline-node", string.IsNullOrWhiteSpace(nodeName) ? "unknown" : nodeName);
            span.Status = SpanStatus.InternalError;
            span.Finish();
        }
    }

    public static void FinishRun(string instanceId, MFATask.MFATaskStatus status)
    {
        lock (SyncRoot)
            FinishRunLocked(instanceId, ToSpanStatus(status));
    }

    public static void Shutdown()
    {
        lock (SyncRoot)
        {
            foreach (var instanceId in new List<string>(Runs.Keys))
                FinishRunLocked(instanceId, SpanStatus.Cancelled);
            _sdk?.Dispose();
            _sdk = null;
        }
    }

    private static void FinishRunLocked(string instanceId, SpanStatus status)
    {
        if (!Runs.Remove(instanceId, out var run))
            return;

        foreach (var span in run.Tasks.Values)
        {
            span.Status = SpanStatus.Cancelled;
            span.SetData("result", "cancelled");
            span.Finish();
        }

        if (status == SpanStatus.Ok && run.HasFailed)
            status = SpanStatus.InternalError;
        run.Transaction.Status = status;
        run.Transaction.SetData("result", status == SpanStatus.Ok ? "success" : status == SpanStatus.Cancelled ? "cancelled" : "failure");
        run.Transaction.Finish();
    }

    private static bool IsUserEnabled() =>
        bool.TryParse(GlobalConfiguration.GetValue(ConfigurationKeys.HelpImproveSoftware, bool.TrueString), out var enabled) && enabled;

    private static bool IsBlockedBuild(string? resourceVersion)
    {
        if (Environment.GetEnvironmentVariable("MFA_TELEMETRY_FORCE") == "1")
            return false;
#if DEBUG
        return true;
#else
        var version = resourceVersion?.Trim() ?? string.Empty;
        return version.Equals("DEBUG_VERSION", StringComparison.OrdinalIgnoreCase)
               || version.Contains("alpha", StringComparison.OrdinalIgnoreCase)
               || version.Contains("dev", StringComparison.OrdinalIgnoreCase);
#endif
    }

    private static string GetDefaultEnvironment(string version) =>
        version.Contains("beta", StringComparison.OrdinalIgnoreCase) ? "beta"
        : version.Contains("rc", StringComparison.OrdinalIgnoreCase) ? "rc"
        : "production";

    private static SpanStatus ToSpanStatus(MFATask.MFATaskStatus status) => status switch
    {
        MFATask.MFATaskStatus.SUCCEEDED => SpanStatus.Ok,
        MFATask.MFATaskStatus.STOPPED or MFATask.MFATaskStatus.STOPPING => SpanStatus.Cancelled,
        _ => SpanStatus.InternalError
    };

    private static string ToResult(MFATask.MFATaskStatus status) => status switch
    {
        MFATask.MFATaskStatus.SUCCEEDED => "success",
        MFATask.MFATaskStatus.STOPPED or MFATask.MFATaskStatus.STOPPING => "cancelled",
        _ => "failure"
    };
}
