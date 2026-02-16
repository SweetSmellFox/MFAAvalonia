using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Configuration;
using MFAAvalonia.Helper;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MaaAgentClient = MaaFramework.Binding.MaaAgentClient;
using MaaTasker = MaaFramework.Binding.MaaTasker;

namespace MFAAvalonia.Extensions.MaaFW;

/// <summary>
/// 单个 Agent 的运行时上下文
/// </summary>
public class AgentContext
{
    public MaaAgentClient? Client { get; set; }
    public Process? Process { get; set; }
    public CancellationTokenSource? ReadCancellationTokenSource { get; set; }
    public readonly Lock ReadLock = new();
    public SafeJobHandle? JobHandle { get; set; }
    public readonly Lock JobLock = new();
    public MaaInterface.MaaInterfaceAgent? Config { get; set; }

    [SupportedOSPlatform("windows")]
    public sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobHandle() : base(true) { }
        protected override bool ReleaseHandle() => AgentHelper.CloseHandle(handle);
    }
}

/// <summary>
/// Agent 启动/停止/管理的静态辅助类，支持多 Agent 同时运行
/// </summary>
public static class AgentHelper
{
    private static readonly Random Random = new();

    /// <summary>
    /// 启动所有配置的 Agent
    /// </summary>
    public static async Task<List<AgentContext>> StartAgentsAsync(
        MaaTasker tasker,
        List<MaaInterface.MaaInterfaceAgent> agentConfigs,
        InstanceConfiguration instanceConfig,
        MaaProcessor processor,
        CancellationToken token)
    {
        var contexts = new List<AgentContext>();
        var validConfigs = agentConfigs.Where(a => a.ChildExec != null).ToList();
        if (validConfigs.Count == 0)
            return contexts;

        processor.AddLogByKey(LangKeys.StartingAgent, (Avalonia.Media.IBrush?)null);

        foreach (var agentConfig in validConfigs)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var ctx = await StartSingleAgentAsync(tasker, agentConfig, instanceConfig, processor, token);
                if (ctx != null)
                    contexts.Add(ctx);
            }
            catch (OperationCanceledException)
            {
                // 取消时清理已启动的 agent
                KillAllAgents(contexts);
                throw;
            }
            catch (Exception ex)
            {
                LoggerHelper.Error($"Failed to start agent '{agentConfig.ChildExec}': {ex.Message}");
                processor.AddLogByKey(LangKeys.AgentStartFailed, Avalonia.Media.Brushes.OrangeRed, changeColor: false);
                ToastHelper.Error(LangKeys.AgentStartFailed.ToLocalization(), ex.Message);
                // 清理已启动的 agent
                KillAllAgents(contexts);
                return [];
            }
        }

        return contexts;
    }

    /// <summary>
    /// 启动单个 Agent
    /// </summary>
    private static async Task<AgentContext?> StartSingleAgentAsync(
        MaaTasker tasker,
        MaaInterface.MaaInterfaceAgent agentConfig,
        InstanceConfiguration instanceConfig,
        MaaProcessor processor,
        CancellationToken token)
    {
        var ctx = new AgentContext { Config = agentConfig };

        var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var identifier = string.IsNullOrWhiteSpace(agentConfig.Identifier)
            ? new string(Enumerable.Repeat(chars, 8).Select(c => c[Random.Next(c.Length)]).ToArray())
            : agentConfig.Identifier;
        LoggerHelper.Info($"Agent Identifier: {identifier}");

        ctx.Client = instanceConfig.GetValue(ConfigurationKeys.AgentTcpMode, false)
            ? MaaAgentClient.CreateTcp(tasker)
            : MaaAgentClient.Create(identifier, tasker);

        var timeOut = agentConfig.Timeout ?? 120;
        ctx.Client.SetTimeout(TimeSpan.FromSeconds(timeOut < 0 ? int.MaxValue : timeOut));
        ctx.Client.Releasing += (_, _) =>
        {
            LoggerHelper.Info("退出Agent进程");
            ctx.Client = null;
        };

        LoggerHelper.Info($"Agent Client Hash: {ctx.Client?.GetHashCode()}");

        if (!Directory.Exists($"{AppContext.BaseDirectory}"))
            Directory.CreateDirectory($"{AppContext.BaseDirectory}");

        var program = MaaInterface.ReplacePlaceholder(agentConfig.ChildExec, AppContext.BaseDirectory, true);
        if (IsPathLike(program))
            program = Path.GetFullPath(program, AppContext.BaseDirectory);

        var rawArgs = agentConfig.ChildArgs ?? [];
        var replacedArgs = MaaInterface.ReplacePlaceholder(rawArgs, AppContext.BaseDirectory, true)
            .Select(arg =>
            {
                if (IsPathLike(arg))
                {
                    try { return Path.GetFullPath(arg, AppContext.BaseDirectory); }
                    catch (Exception) { return arg; }
                }
                return arg;
            })
            .Select(ConvertPath).ToList();

        var executablePath = PathFinder.FindPath(program);

        if (!File.Exists(executablePath))
        {
            var errorMsg = LangKeys.AgentExecutableNotFound.ToLocalizationFormatted(false, executablePath);
            throw new FileNotFoundException(errorMsg, executablePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = AppContext.BaseDirectory,
            Arguments = $"{(program!.Contains("python") && replacedArgs.Contains(".py") && !replacedArgs.Any(arg => arg.Contains("-u")) ? "-u " : "")}{string.Join(" ", replacedArgs)} {ctx.Client.Id}",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true
        };

        LoggerHelper.Info(
            $"Agent Command: {program} {(program!.Contains("python") && replacedArgs.Contains(".py") && !replacedArgs.Any(arg => arg.Contains("-u")) ? "-u " : "")}{string.Join(" ", replacedArgs)} {ctx.Client.Id} "
            + $"socket_id: {ctx.Client.Id}");

        IMaaAgentClient.AgentServerStartupMethod method = (s, directory) =>
        {
            ctx.Process = System.Diagnostics.Process.Start(startInfo);
            if (ctx.Process == null)
                LoggerHelper.Error("Agent start failed!");
            else
            {
                ctx.Process.Exited += (_, _) =>
                {
                    LoggerHelper.Info("Agent process exited!");
                    StopReadStreams(ctx);
                    ctx.Process = null;
                };

                BindProcessLifetime(ctx);

                var readToken = ResetReadCancellation(ctx).Token;
                TaskManager.RunTaskAsync(() => ReadProcessStreamAsync(ctx.Process.StandardOutput.BaseStream,
                    line => HandleOutputLine(line, processor), readToken), token: readToken, noMessage: true);
                TaskManager.RunTaskAsync(() => ReadProcessStreamAsync(ctx.Process.StandardError.BaseStream,
                    line => HandleOutputLine(line, processor), readToken), token: readToken, noMessage: true);

                TaskManager.RunTaskAsync(async () => await ctx.Process.WaitForExitAsync(token), token: token, name: "Agent程序启动");
            }
            return ctx.Process;
        };

        // 重连逻辑
        const int maxRetries = 3;
        bool linkStartSuccess = false;
        Exception? lastException = null;

        for (int retryCount = 0; retryCount < maxRetries && !linkStartSuccess && !token.IsCancellationRequested; retryCount++)
        {
            try
            {
                token.ThrowIfCancellationRequested();

                if (retryCount > 0)
                {
                    LoggerHelper.Info($"Agent LinkStart retry attempt {retryCount + 1}/{maxRetries}");
                    processor.AddLog(LangKeys.AgentConnectionRetry.ToLocalizationFormatted(false, $"{retryCount + 1}/{maxRetries}"),
                        Avalonia.Media.Brushes.Orange, changeColor: false);
                    await Task.Delay(1000 * retryCount, token);

                    if (ctx.Process != null && !ctx.Process.HasExited)
                    {
                        try
                        {
                            ctx.Process.Kill(true);
                            ctx.Process.WaitForExit(3000);
                        }
                        catch (Exception killEx)
                        {
                            LoggerHelper.Warning($"Failed to kill agent process: {killEx.Message}");
                        }
                        ctx.Process.Dispose();
                        ctx.Process = null;
                    }
                }

                linkStartSuccess = ctx.Client.LinkStart(method, token);
            }
            catch (OperationCanceledException)
            {
                LoggerHelper.Info("Agent LinkStart was canceled by user");
                throw;
            }
            catch (SEHException sehEx)
            {
                lastException = sehEx;
                LoggerHelper.Warning($"SEHException during LinkStart (attempt {retryCount + 1}): {sehEx.Message}");

                if (retryCount < maxRetries - 1)
                {
                    if (token.IsCancellationRequested)
                    {
                        LoggerHelper.Info("Agent retry canceled by user");
                        token.ThrowIfCancellationRequested();
                    }

                    KillSingleAgent(ctx);

                    try
                    {
                        ctx.Client = instanceConfig.GetValue(ConfigurationKeys.AgentTcpMode, false)
                            ? MaaAgentClient.Create(identifier, tasker)
                            : MaaAgentClient.CreateTcp(tasker);
                        timeOut = agentConfig.Timeout ?? 120;
                        ctx.Client.SetTimeout(TimeSpan.FromSeconds(timeOut < 0 ? int.MaxValue : timeOut));
                        ctx.Client.Releasing += (_, _) => LoggerHelper.Info("退出Agent进程");
                    }
                    catch (Exception recreateEx)
                    {
                        LoggerHelper.Error($"Failed to recreate AgentClient: {recreateEx.Message}");
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
                LoggerHelper.Warning($"Exception during LinkStart (attempt {retryCount + 1}): {ex.Message}");
                break;
            }
        }

        if (token.IsCancellationRequested && !linkStartSuccess)
        {
            LoggerHelper.Info("Agent LinkStart loop exited due to cancellation");
            token.ThrowIfCancellationRequested();
        }

        if (!linkStartSuccess)
        {
            var errorMessage = lastException?.Message ?? "Failed to LinkStart agentClient!";
            var agentProcess = ctx.Process;
            if (agentProcess != null)
            {
                try
                {
                    var errorDetails = new StringBuilder();
                    errorDetails.AppendLine(errorMessage);

                    if (agentProcess.HasExited)
                    {
                        var exitCode = agentProcess.ExitCode;
                        var stderr = await agentProcess.StandardError.ReadToEndAsync(token);
                        var stdout = await agentProcess.StandardOutput.ReadToEndAsync(token);
                        errorDetails.AppendLine($"Agent process exited with code: {exitCode}");
                        if (!string.IsNullOrWhiteSpace(stderr))
                        {
                            errorDetails.AppendLine($"StandardError: {stderr}");
                            LoggerHelper.Error($"Agent StandardError: {stderr}");
                            processor.AddLog($"Agent Error: {stderr}", Avalonia.Media.Brushes.OrangeRed, changeColor: false);
                        }
                        if (!string.IsNullOrWhiteSpace(stdout))
                        {
                            errorDetails.AppendLine($"StandardOutput: {stdout}");
                            LoggerHelper.Info($"Agent StandardOutput: {stdout}");
                        }
                        errorMessage = errorDetails.ToString();
                    }
                    else
                    {
                        if (agentProcess.WaitForExit(3000))
                        {
                            var exitCode = agentProcess.ExitCode;
                            var stderr = await agentProcess.StandardError.ReadToEndAsync(token);
                            var stdout = await agentProcess.StandardOutput.ReadToEndAsync(token);
                            errorDetails.AppendLine($"Agent process exited with code: {exitCode}");
                            if (!string.IsNullOrWhiteSpace(stderr))
                            {
                                errorDetails.AppendLine($"StandardError: {stderr}");
                                LoggerHelper.Error($"Agent StandardError: {stderr}");
                                processor.AddLog($"Agent Error: {stderr}", Avalonia.Media.Brushes.OrangeRed, changeColor: false);
                                if (!string.IsNullOrWhiteSpace(stdout))
                                {
                                    errorDetails.AppendLine($"StandardOutput: {stdout}");
                                    LoggerHelper.Info($"Agent StandardOutput: {stdout}");
                                    errorMessage = errorDetails.ToString();
                                }
                            }
                        }
                    }
                }
                catch (Exception readEx)
                {
                    LoggerHelper.Warning($"Failed to read agent process output: {readEx.Message}");
                }
            }
            throw new Exception(errorMessage);
        }

        return ctx;
    }

    /// <summary>
    /// 终止所有 Agent 进程并释放资源
    /// </summary>
    public static void KillAllAgents(List<AgentContext> contexts, MaaTasker? taskerToDispose = null)
    {
        foreach (var ctx in contexts)
        {
            KillSingleAgent(ctx, taskerToDispose);
        }
        contexts.Clear();

        // 只在最后处理一次 tasker
        if (taskerToDispose != null)
        {
            DisposeMaaTasker(taskerToDispose);
        }
    }

    /// <summary>
    /// 终止单个 Agent（不处理 MaaTasker）
    /// </summary>
    public static void KillSingleAgent(AgentContext ctx, MaaTasker? taskerToDispose = null)
    {
        var agentClient = ctx.Client;
        var agentProcess = ctx.Process;

        StopReadStreams(ctx);

        ctx.Client = null;
        ctx.Process = null;

        // 步骤 1: 停止 AgentClient
        if (agentClient != null)
        {
            LoggerHelper.Info("Stopping AgentClient connection");
            try
            {
                bool shouldStop = false;
                try { shouldStop = !agentClient.IsStateless && !agentClient.IsInvalid; }
                catch (ObjectDisposedException) { }

                if (shouldStop)
                {
                    try
                    {
                        agentClient.LinkStop();
                        LoggerHelper.Info("AgentClient LinkStop succeeded");
                    }
                    catch (Exception e)
                    {
                        LoggerHelper.Warning($"AgentClient LinkStop failed: {e.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                LoggerHelper.Warning($"AgentClient LinkStop check failed: {e.Message}");
            }
        }

        // 步骤 2: 终止进程
        if (agentProcess != null)
        {
            LoggerHelper.Info("Terminating Agent process");
            try
            {
                var hasExited = true;
                try { hasExited = agentProcess.HasExited; }
                catch (InvalidOperationException) { }
                catch (Exception ex)
                {
                    LoggerHelper.Warning($"Failed to check if agent process has exited: {ex.Message}");
                }

                if (!hasExited)
                {
                    try
                    {
                        LoggerHelper.Info($"Kill AgentProcess: {agentProcess.ProcessName}");
                        agentProcess.Kill(true);
                        agentProcess.WaitForExit(5000);
                        LoggerHelper.Info("Agent process killed successfully");
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.Warning($"Failed to kill agent process: {ex.Message}");
                    }
                }
                else
                {
                    LoggerHelper.Info("AgentProcess has already exited");
                }
            }
            catch (Exception e)
            {
                LoggerHelper.Error($"Error handling agent process: {e.Message}");
            }
            finally
            {
                try { agentProcess.Dispose(); }
                catch (Exception e) { LoggerHelper.Warning($"AgentProcess Dispose failed: {e.Message}"); }
            }
        }

        DisposeJob(ctx);
    }

    /// <summary>
    /// 停止所有 Agent 的输出流读取
    /// </summary>
    public static void StopAllReadStreams(List<AgentContext> contexts)
    {
        foreach (var ctx in contexts)
            StopReadStreams(ctx);
    }

    /// <summary>
    /// 检查是否有任何 Agent 配置需要启动
    /// </summary>
    public static bool HasAgentConfigs(List<MaaInterface.MaaInterfaceAgent>? agents)
    {
        return agents is { Count: > 0 } && agents.Any(a => a.ChildExec != null);
    }

    #region 内部辅助方法

    private static CancellationTokenSource ResetReadCancellation(AgentContext ctx)
    {
        lock (ctx.ReadLock)
        {
            ctx.ReadCancellationTokenSource?.Cancel();
            ctx.ReadCancellationTokenSource?.Dispose();
            ctx.ReadCancellationTokenSource = new CancellationTokenSource();
            return ctx.ReadCancellationTokenSource;
        }
    }

    private static void StopReadStreams(AgentContext ctx)
    {
        lock (ctx.ReadLock)
        {
            if (ctx.ReadCancellationTokenSource == null) return;
            ctx.ReadCancellationTokenSource.Cancel();
            ctx.ReadCancellationTokenSource.Dispose();
            ctx.ReadCancellationTokenSource = null;
        }
    }

    private static void HandleOutputLine(string? line, MaaProcessor processor)
    {
        if (string.IsNullOrEmpty(line)) return;

        var outData = line;
        try { outData = Regex.Replace(outData, @"\x1B\[[0-9;]*[a-zA-Z]", ""); }
        catch (Exception) { }

        DispatcherHelper.PostOnMainThread(() =>
        {
            if (MaaProcessor.CheckShouldLog(outData))
                processor.AddLog(outData, (Avalonia.Media.IBrush?)null);
            else
                LoggerHelper.Info("agent:" + outData);
        });
    }

    private static string ConvertPath(string path)
    {
        if (Path.Exists(path) && !path.Contains("\""))
            return $"\"{path}\"";
        return path;
    }

    private static bool IsPathLike(string? input)
    {
        if (string.IsNullOrEmpty(input)) return false;
        bool hasPathSeparator = input.Contains(Path.DirectorySeparatorChar) || input.Contains(Path.AltDirectorySeparatorChar);
        bool isAbsolutePath = Path.IsPathRooted(input);
        bool isRelativePath = input.StartsWith("./") || input.StartsWith("../") || (hasPathSeparator && !input.StartsWith("-"));
        bool hasFileExtension = Path.HasExtension(input) && !input.StartsWith("-");
        return hasPathSeparator || isAbsolutePath || isRelativePath || hasFileExtension;
    }

    private static void DisposeMaaTasker(MaaTasker maaTasker)
    {
        if (maaTasker.IsRunning && !maaTasker.IsStopping)
        {
            LoggerHelper.Info("Stopping MaaTasker before dispose");
            try
            {
                var stopResult = maaTasker.Stop().Wait();
                LoggerHelper.Info($"MaaTasker Stop result: {stopResult}");
            }
            catch (ObjectDisposedException) { LoggerHelper.Info("MaaTasker was already disposed during Stop"); }
            catch (Exception e) { LoggerHelper.Warning($"MaaTasker Stop failed: {e.Message}"); }
        }

        LoggerHelper.Info("Disposing MaaTasker");
        try
        {
            maaTasker.Dispose();
            LoggerHelper.Info("MaaTasker disposed successfully");
        }
        catch (ObjectDisposedException) { LoggerHelper.Info("MaaTasker was already disposed"); }
        catch (Exception e) { LoggerHelper.Warning($"MaaTasker Dispose failed: {e.Message}"); }
    }

    #endregion

    #region 进程生命周期绑定 (Windows)

    private static void BindProcessLifetime(AgentContext ctx)
    {
        if (!OperatingSystem.IsWindows()) return;
        TryBindProcessToJob(ctx);
    }

    private static void DisposeJob(AgentContext ctx)
    {
        if (!OperatingSystem.IsWindows()) return;
        lock (ctx.JobLock)
        {
            if (ctx.JobHandle == null) return;
            ctx.JobHandle.Dispose();
            ctx.JobHandle = null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void TryBindProcessToJob(AgentContext ctx)
    {
        if (ctx.Process == null) return;

        lock (ctx.JobLock)
        {
            if (ctx.JobHandle == null || ctx.JobHandle.IsInvalid)
            {
                ctx.JobHandle = CreateJobObject(IntPtr.Zero, null);
                if (ctx.JobHandle == null || ctx.JobHandle.IsInvalid)
                {
                    LoggerHelper.Warning($"CreateJobObject failed: {Marshal.GetLastWin32Error()}");
                    ctx.JobHandle = null;
                    return;
                }

                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                {
                    BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                    {
                        LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                    }
                };

                if (!SetInformationJobObject(ctx.JobHandle, JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation,
                        ref info, (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
                {
                    LoggerHelper.Warning($"SetInformationJobObject failed: {Marshal.GetLastWin32Error()}");
                    ctx.JobHandle.Dispose();
                    ctx.JobHandle = null;
                    return;
                }
            }

            if (!AssignProcessToJobObject(ctx.JobHandle, ctx.Process.Handle))
                LoggerHelper.Warning($"AssignProcessToJobObject failed: {Marshal.GetLastWin32Error()}");
        }
    }

    #endregion

    #region 流读取

    private static readonly Encoding Utf8Strict = new UTF8Encoding(false, true);
    private static readonly Lazy<Encoding> GbkEncoding = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936);
    });
    private static readonly Lazy<Encoding> Gb18030Encoding = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(54936);
    });
    private static readonly Lazy<Encoding> Big5Encoding = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(950);
    });

    private static string DecodeProcessLine(byte[] buffer)
    {
        try { return Utf8Strict.GetString(buffer); }
        catch (DecoderFallbackException) { }
        try { return Gb18030Encoding.Value.GetString(buffer); }
        catch (DecoderFallbackException) { }
        try { return GbkEncoding.Value.GetString(buffer); }
        catch (DecoderFallbackException) { }
        return Big5Encoding.Value.GetString(buffer);
    }

    internal static async Task ReadProcessStreamAsync(Stream stream, Action<string> onLine, CancellationToken token)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var readBuffer = new byte[4096];
        var lineBuffer = new List<byte>();

        while (!token.IsCancellationRequested)
        {
            int bytesRead;
            try { bytesRead = await stream.ReadAsync(readBuffer.AsMemory(0, readBuffer.Length), token); }
            catch (OperationCanceledException) { break; }
            if (bytesRead <= 0) break;

            for (int i = 0; i < bytesRead; i++)
            {
                var value = readBuffer[i];
                if (value == (byte)'\n')
                {
                    if (lineBuffer.Count > 0 && lineBuffer[^1] == (byte)'\r')
                        lineBuffer.RemoveAt(lineBuffer.Count - 1);
                    onLine(lineBuffer.Count > 0 ? DecodeProcessLine(lineBuffer.ToArray()) : string.Empty);
                    lineBuffer.Clear();
                }
                else
                {
                    lineBuffer.Add(value);
                }
            }
        }

        if (lineBuffer.Count > 0)
            onLine(DecodeProcessLine(lineBuffer.ToArray()));
    }

    #endregion

    #region Windows P/Invoke

    [SupportedOSPlatform("windows")]
    private enum JOBOBJECTINFOCLASS { JobObjectExtendedLimitInformation = 9 }

    [SupportedOSPlatform("windows")]
    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [SupportedOSPlatform("windows")]
    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [SupportedOSPlatform("windows")]
    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [SupportedOSPlatform("windows")]
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern AgentContext.SafeJobHandle CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(AgentContext.SafeJobHandle hJob, JOBOBJECTINFOCLASS infoClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint cbJobObjectInfoLength);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(AgentContext.SafeJobHandle hJob, IntPtr hProcess);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr hObject);

    #endregion
}
