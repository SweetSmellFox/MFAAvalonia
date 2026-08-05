using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.ViewModels.Windows;
using Sentry;
using Sentry.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Management;

namespace MFAAvalonia.Helper;

public static class TelemetryService
{
    private sealed class RunState
    {
        public required ITransactionTracer Transaction { get; init; }
        public Dictionary<MFATask, ISpan> Tasks { get; } = new();
        public MFATask? ActiveTask { get; set; }
        public bool HasFailed { get; set; }
        public int TaskCount { get; init; }
        public int FailedNodeCount { get; set; }
        public FailureInfo? RootFailure { get; set; }
        public FailureInfo? TerminalFailure { get; set; }
        public TaskEvidenceSnapshot? Evidence { get; set; }
        public DateTimeOffset ActiveTaskStartedAt { get; set; }
        public Dictionary<long, (long NodeId, DateTimeOffset StartedAt)> LastPipelineSteps { get; } = new();
    }

    private sealed class PendingFailureWorker
    {
        private readonly object _sync = new();
        private bool _captured;
        public required SentryEvent Event { get; init; }
        public Task? Task { get; set; }
        public bool TryCapture(SentryHint hint, Action<SentryEvent> configure)
        {
            lock (_sync)
            {
                if (_captured || !IsActive) return false;
                _captured = true;
                configure(Event);
                SentrySdk.CaptureEvent(Event, hint, _ => { });
                return true;
            }
        }
    }

    private const int MaxFailedNodesPerRun = 32;
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, RunState> Runs = new();
    private static IDisposable? _sdk;
    private static readonly string AnonymousMachineId = CreateAnonymousMachineId();
    private static double FailureAttachmentSampleRate = 1.0;
    private static readonly List<PendingFailureWorker> PendingAttachmentWorkers = new();

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
                FailureAttachmentSampleRate = Math.Clamp(config.FailureAttachmentsSampleRate ?? 1.0, 0.0, 1.0);

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
                    scope.User = new SentryUser { Id = AnonymousMachineId };
                    scope.Contexts["hardware"] = CollectHardwareContext();
                    scope.Contexts["client"] = new Dictionary<string, object>
                    {
                        ["name"] = "MFAAvalonia",
                        ["version"] = RootViewModel.Version,
                        ["runtime"] = RuntimeInformation.FrameworkDescription
                    };
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
            Runs[instanceId] = new RunState { Transaction = transaction, TaskCount = taskCount };
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
            foreach (var option in BuildOptionSummary(task))
            {
                span.SetData($"option.{option.Key}", option.Value);
            }
            run.Tasks[task] = span;
            run.ActiveTask = task;
            run.ActiveTaskStartedAt = DateTimeOffset.UtcNow;
            run.FailedNodeCount = 0;
            run.RootFailure = null;
            run.TerminalFailure = null;
            run.LastPipelineSteps.Clear();
            run.Evidence = run.Tasks.Count == 1 && FailureAttachmentSampleRate > 0
                ? TaskDiagnostics.CaptureStart(Runs.Count == 1)
                : null;
        }
    }

    public static void FinishTask(string instanceId, MFATask task, MFATask.MFATaskStatus status, bool hadFailure = false)
    {
        lock (SyncRoot)
        {
            if (!Runs.TryGetValue(instanceId, out var run) || !run.Tasks.Remove(task, out var span))
                return;

            var spanStatus = hadFailure ? SpanStatus.InternalError : ToSpanStatus(status);
            span.Status = spanStatus;
            span.SetData("result", hadFailure ? "failure" : ToResult(status));
            span.Finish();
            if (status == MFATask.MFATaskStatus.FAILED || hadFailure)
            {
                run.HasFailed = true;
                CaptureFailureEvent(instanceId, task, span, run);
            }
            if (ReferenceEquals(run.ActiveTask, task))
                run.ActiveTask = null;
        }
    }

    public static void RecordFailedNode(string instanceId, string? nodeName, string? stage = null, long? nodeId = null, long? taskId = null, long? durationMs = null)
    {
        lock (SyncRoot)
        {
            if (!Runs.TryGetValue(instanceId, out var run)
                || run.ActiveTask == null
                || !run.Tasks.TryGetValue(run.ActiveTask, out var taskSpan)
                || run.FailedNodeCount++ >= MaxFailedNodesPerRun)
                return;

            var span = taskSpan.StartChild("mfa.pipeline-node", string.IsNullOrWhiteSpace(nodeName) ? "unknown" : nodeName);
            span.SetData("stage", stage ?? "unknown");
            if (nodeId.HasValue) span.SetData("node.id", nodeId.Value);
            if (taskId.HasValue) span.SetData("task.id", taskId.Value);
            if (durationMs.HasValue) span.SetData("duration_ms", durationMs.Value);
            span.Status = SpanStatus.InternalError;
            span.Finish();
            var failure = new FailureInfo(nodeName ?? "unknown", stage ?? "unknown", nodeId, taskId, durationMs);
            run.RootFailure ??= failure;
            run.TerminalFailure = failure;
        }
    }

    public static void RecordNodeEvent(string instanceId, string message, string details)
    {
        if (!message.Equals("Node.PipelineNode.Starting", StringComparison.Ordinal)
            && !message.Equals("Node.PipelineNode.Failed", StringComparison.Ordinal))
            return;

        JObject detail;
        try { detail = JObject.Parse(details); }
        catch { return; }
        var taskId = detail["task_id"]?.Value<long>();
        var nodeId = detail["node_id"]?.Value<long>();
        if (!taskId.HasValue) return;

        lock (SyncRoot)
        {
            if (!Runs.TryGetValue(instanceId, out var run)) return;
            if (message.Equals("Node.PipelineNode.Starting", StringComparison.Ordinal))
            {
                if (nodeId.HasValue) run.LastPipelineSteps[taskId.Value] = (nodeId.Value, DateTimeOffset.UtcNow);
                return;
            }

            var hitNode = detail["node_details"]?["name"]?.Value<string>();
            var fallbackNode = detail["name"]?.Value<string>();
            var failedNode = string.IsNullOrWhiteSpace(hitNode) ? fallbackNode : hitNode;
            if (string.IsNullOrWhiteSpace(failedNode) || run.ActiveTask == null
                || !run.Tasks.TryGetValue(run.ActiveTask, out var taskSpan)) return;
            var stage = string.IsNullOrWhiteSpace(hitNode) ? "recognition" : "action";
            var duration = run.LastPipelineSteps.Remove(taskId.Value, out var step)
                && nodeId.HasValue && step.NodeId == nodeId.Value
                ? (long?)(DateTimeOffset.UtcNow - step.StartedAt).TotalMilliseconds : null;
            run.FailedNodeCount++;
            var failure = new FailureInfo(failedNode!, stage, nodeId, taskId, duration);
            run.RootFailure ??= failure;
            run.TerminalFailure = failure;
            if (run.FailedNodeCount > MaxFailedNodesPerRun) return;
            var span = taskSpan.StartChild("mfa.pipeline-node", failedNode);
            span.SetData("stage", stage);
            span.SetData("task.id", taskId.Value);
            if (nodeId.HasValue) span.SetData("node.id", nodeId.Value);
            if (duration.HasValue) span.SetData("duration_ms", duration.Value);
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
        PendingFailureWorker[] pending;
        lock (SyncRoot)
        {
            foreach (var instanceId in new List<string>(Runs.Keys))
                FinishRunLocked(instanceId, SpanStatus.Cancelled);
            pending = PendingAttachmentWorkers.ToArray();
        }

        try
        {
            Task.WaitAll(pending.Select(worker => worker.Task).Where(task => task != null).Cast<Task>().ToArray(), TimeSpan.FromSeconds(1));
        }
        catch (AggregateException) { }
        finally
        {
            foreach (var worker in pending.Where(worker => worker.Task is { IsCompleted: false }))
            {
                worker.TryCapture(new SentryHint(), @event =>
                {
                    @event.SetExtra("attachment.status", "shutdown_timeout");
                    @event.SetExtra("attachment.detail", "attachment worker did not finish before telemetry shutdown");
                });
            }
            lock (SyncRoot)
            {
                _sdk?.Dispose();
                _sdk = null;
                PendingAttachmentWorkers.Clear();
            }
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

    private sealed record FailureInfo(string Node, string Stage, long? NodeId, long? TaskId, long? DurationMs);

    private static void CaptureFailureEvent(string instanceId, MFATask task, ISpan taskSpan, RunState run)
    {
        var taskName = task.SourceItem?.InterfaceItem?.Name ?? task.Name ?? "unknown-task";
        var @event = new SentryEvent
        {
            Message = $"Maa task failed: {taskName}",
            TransactionName = "mfa.task.failure",
            Fingerprint = new[] { "mfa-task-failure", MaaProcessor.Interface?.Name ?? "unknown", taskName, run.RootFailure?.Node ?? "terminal_failure", run.RootFailure?.Stage ?? "unknown" }
        };
        @event.SetTag("task.name", taskName);
        @event.SetTag("result", "failure");
        var traceHeader = taskSpan.GetTraceHeader();
        @event.Contexts.Trace.TraceId = traceHeader.TraceId;
        @event.Contexts.Trace.SpanId = traceHeader.SpanId;
        @event.Contexts.Trace.Operation = taskSpan.Operation;
        @event.Contexts.Trace.Description = taskSpan.Description;
        @event.Contexts.Trace.Status = taskSpan.Status;
        if (run.RootFailure != null)
        {
            SetFailureData(@event, "failure", run.RootFailure, asTags: true);
            if (run.TerminalFailure != null && run.TerminalFailure != run.RootFailure)
                SetFailureData(@event, "terminal_failure", run.TerminalFailure, asTags: false);
        }
        foreach (var option in BuildOptionSummary(task))
            @event.SetExtra($"option.{option.Key}", option.Value);
        @event.SetExtra("instance.task_count", run.TaskCount);
        @event.SetExtra("task.duration_ms", Math.Max(0, (DateTimeOffset.UtcNow - run.ActiveTaskStartedAt).TotalMilliseconds));
        if (run.Evidence != null && ShouldSampleAttachment(instanceId, taskName, FailureAttachmentSampleRate))
        {
            @event.SetExtra("attachment.status", "pending");
            var evidence = run.Evidence;
            var pendingWorker = new PendingFailureWorker { Event = @event };
            var workerTask = Task.Run(() =>
            {
                try
                {
                    var result = TaskDiagnostics.Build(evidence);
                    if (result.Status != TaskEvidenceBuildStatus.Success || result.Data == null)
                    {
                        pendingWorker.TryCapture(new SentryHint(), item =>
                        {
                            item.SetExtra("attachment.status", ToAttachmentStatus(result.Status));
                            item.SetExtra("attachment.log_count", result.LogCount);
                            item.SetExtra("attachment.image_count", result.ImageCount);
                            item.SetExtra("attachment.selected_raw_bytes", result.RawBytes);
                        });
                        return;
                    }
                    var hint = new SentryHint();
                    hint.AddAttachment(result.Data, $"mfa-task-failure-{SanitizeFileName(taskName)}.zip", AttachmentType.Default, "application/zip");
                    pendingWorker.TryCapture(hint, item =>
                    {
                        item.SetExtra("attachment.status", "attached");
                        item.SetExtra("attachment.log_count", result.LogCount);
                        item.SetExtra("attachment.image_count", result.ImageCount);
                        item.SetExtra("attachment.selected_raw_bytes", result.RawBytes);
                        item.SetExtra("attachment.compressed_bytes", result.Data.Length);
                    });
                }
                catch (Exception ex)
                {
                    pendingWorker.TryCapture(new SentryHint(), item =>
                    {
                        item.SetExtra("attachment.status", "build_failed");
                        item.SetExtra("attachment.detail", ex.GetType().Name);
                    });
                }
            });
            pendingWorker.Task = workerTask;
            lock (SyncRoot) PendingAttachmentWorkers.Add(pendingWorker);
            _ = workerTask.ContinueWith(_ =>
            {
                lock (SyncRoot) PendingAttachmentWorkers.Remove(pendingWorker);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return;
        }

        @event.SetExtra("attachment.status", "not_selected");
        SentrySdk.CaptureEvent(@event, new SentryHint(), _ => { });
    }

    private static void SetFailureData(SentryEvent @event, string prefix, FailureInfo failure, bool asTags)
    {
        if (asTags)
        {
            @event.SetTag($"{prefix}.node", failure.Node);
            @event.SetTag($"{prefix}.stage", failure.Stage);
        }
        else
        {
            @event.SetExtra($"{prefix}.node", failure.Node);
            @event.SetExtra($"{prefix}.stage", failure.Stage);
        }
        if (failure.NodeId.HasValue) @event.SetExtra($"{prefix}.node_id", failure.NodeId.Value);
        if (failure.TaskId.HasValue) @event.SetExtra($"{prefix}.task_id", failure.TaskId.Value);
        if (failure.DurationMs.HasValue) @event.SetExtra($"{prefix}.duration_ms", failure.DurationMs.Value);
    }

    private static string ToAttachmentStatus(TaskEvidenceBuildStatus status) => status switch
    {
        TaskEvidenceBuildStatus.NotIsolated => "ambiguous_instance",
        TaskEvidenceBuildStatus.NoEvidence => "no_evidence",
        TaskEvidenceBuildStatus.RawTooLarge => "raw_too_large",
        TaskEvidenceBuildStatus.CompressedTooLarge => "compressed_too_large",
        _ => "build_failed"
    };

    private static string SanitizeFileName(string value)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown-task" : sanitized;
    }

    private static IReadOnlyDictionary<string, string> BuildOptionSummary(MFATask task)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var item = task.SourceItem?.InterfaceItem;
        if (item == null) return result;
        foreach (var option in item.Option ?? [])
            AddOptionSummary(result, option);
        foreach (var advanced in item.Advanced ?? [])
        {
            foreach (var data in advanced.Data)
                result[$"advanced.{advanced.Name}.{data.Key}"] = string.IsNullOrEmpty(data.Value) ? "empty" : "filled";
        }
        return result;
    }

    private static void AddOptionSummary(Dictionary<string, string> result, MaaInterface.MaaInterfaceSelectOption option)
    {
        if (string.IsNullOrWhiteSpace(option.Name)) return;
        MaaInterface.MaaInterfaceOption? definition = null;
        MaaProcessor.Interface?.Option?.TryGetValue(option.Name, out definition);
        if (option.Index.HasValue)
        {
            var selectedCase = definition?.Cases?.ElementAtOrDefault(option.Index.Value)?.Name;
            result[option.Name] = selectedCase ?? option.Index.Value.ToString();
        }
        if (option.SelectedCases is { Count: > 0 }) result[$"{option.Name}.cases"] = string.Join(",", option.SelectedCases);
        if (option.Data != null && definition != null)
        {
            foreach (var input in definition.Inputs ?? [])
            {
                var value = option.Data.GetValueOrDefault(input.Name ?? string.Empty);
                var type = input.PipelineType?.ToLowerInvariant();
                result[$"{option.Name}.{input.Name}"] = type is "int" or "float" or "bool" ? value ?? string.Empty : string.IsNullOrEmpty(value) ? "empty" : "filled";
            }
        }
        foreach (var child in option.SubOptions ?? []) AddOptionSummary(result, child);
    }

    private static string CreateAnonymousMachineId()
    {
        var raw = SimpleEncryptionHelper.GetPlatformSpecificId();
        if (string.IsNullOrWhiteSpace(raw)) raw = Environment.MachineName;
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes($"mfa-telemetry-v1:{raw}"))).ToLowerInvariant();
    }

    private static Dictionary<string, object> CollectHardwareContext()
    {
        var result = new Dictionary<string, object>
        {
            ["os"] = RuntimeInformation.OSDescription,
            ["architecture"] = RuntimeInformation.OSArchitecture.ToString(),
            ["cpu"] = "unknown",
            ["cpu_cores"] = Environment.ProcessorCount,
            ["memory_total_mb"] = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024,
            ["gpu"] = "unknown"
        };
        if (!System.OperatingSystem.IsWindows()) return result;

        try
        {
#pragma warning disable CA1416
            using (var cpuSearcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
            {
                var cpu = cpuSearcher.Get().Cast<ManagementObject>()
                    .Select(item => item["Name"]?.ToString()?.Trim())
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
                if (cpu != null) result["cpu"] = cpu;
            }
            using (var memorySearcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
            {
                var memory = memorySearcher.Get().Cast<ManagementObject>()
                    .Select(item => item["TotalPhysicalMemory"])
                    .FirstOrDefault(value => value != null);
                if (memory != null && ulong.TryParse(memory.ToString(), out var bytes))
                    result["memory_total_mb"] = bytes / 1024 / 1024;
            }
            using var gpuSearcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            var names = gpuSearcher.Get().Cast<ManagementObject>()
                .Select(item => item["Name"]?.ToString()?.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4);
            var gpu = string.Join(", ", names);
            if (!string.IsNullOrWhiteSpace(gpu)) result["gpu"] = gpu;
#pragma warning restore CA1416
        }
        catch (Exception ex)
        {
            LoggerHelper.Debug($"[Telemetry] 硬件信息采集失败：{ex.GetType().Name}");
        }
        return result;
    }

    private static bool ShouldSampleAttachment(string instanceId, string taskName, double rate)
    {
        if (rate <= 0) return false;
        if (rate >= 1) return true;
        using var sha = SHA256.Create();
        var input = Encoding.UTF8.GetBytes($"mfa-failure-attachment-v1:{instanceId}:{taskName}");
        var digest = sha.ComputeHash(input);
        var bucket = BitConverter.ToUInt64(digest, 0) / (double)ulong.MaxValue;
        return bucket < rate;
    }
}
