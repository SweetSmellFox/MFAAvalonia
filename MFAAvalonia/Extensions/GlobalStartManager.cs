using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Windows;

namespace MFAAvalonia.Extensions;

/// <summary>
/// 全局启动管理器：统一管理多开模拟器的发现、启动、端口检测和实例任务执行
/// </summary>
internal static class GlobalStartManager
{
    private const int MaxEmulatorEntries = 20;
    private const int DefaultPort = 16384;
    private const int PortOffsetPerInstance = 32;

    /// <summary>
    /// 从 GlobalConfiguration 读取所有已配置的模拟器条目
    /// </summary>
    public static List<(string Name, string Path, string Args)> DiscoverEmulators()
    {
        var emulators = new List<(string Name, string Path, string Args)>();
        for (var i = 0; i < MaxEmulatorEntries; i++)
        {
            var path = GlobalConfiguration.GetValue(string.Format(ConfigurationKeys.GlobalEmulatorPathKeyFormat, i), string.Empty);
            if (string.IsNullOrWhiteSpace(path)) continue;
            var args = GlobalConfiguration.GetValue(string.Format(ConfigurationKeys.GlobalEmulatorArgsKeyFormat, i), string.Empty);
            var name = GlobalConfiguration.GetValue(string.Format(ConfigurationKeys.GlobalEmulatorNameKeyFormat, i), $"模拟器 {i + 1}");
            emulators.Add((name, path, args));
        }
        return emulators;
    }

    /// <summary>
    /// 启动所有配置的模拟器进程
    /// </summary>
    public static void StartEmulatorProcesses(List<(string Name, string Path, string Args)> emulators)
    {
        LoggerHelper.Info($"全局启动：准备启动 {emulators.Count} 个模拟器");
        foreach (var emu in emulators)
        {
            if (!System.IO.File.Exists(emu.Path))
            {
                LoggerHelper.Warning($"全局启动：{emu.Name} 路径不存在: {emu.Path}");
                continue;
            }
            LoggerHelper.Info($"全局启动：启动 {emu.Name} - {emu.Path} {emu.Args}");
            var startInfo = new ProcessStartInfo
            {
                FileName = emu.Path,
                UseShellExecute = true,
                CreateNoWindow = false
            };
            if (!string.IsNullOrWhiteSpace(emu.Args))
                startInfo.Arguments = emu.Args;
            Process.Start(startInfo);
        }
    }

    /// <summary>
    /// 从模拟器参数中推导 ADB 端口号
    /// </summary>
    public static int DerivePort(string emuArgs, int fallbackIndex = 0)
    {
        if (!string.IsNullOrWhiteSpace(emuArgs))
        {
            var parts = emuArgs.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (var j = 0; j < parts.Length - 1; j++)
            {
                if (parts[j] == "-v" && int.TryParse(parts[j + 1], out var vmIndex))
                    return DefaultPort + vmIndex * PortOffsetPerInstance;
            }
        }
        return DefaultPort + fallbackIndex * PortOffsetPerInstance;
    }

    /// <summary>
    /// 等待 ADB 端口就绪（使用 Stopwatch 精确计时）
    /// </summary>
    public static async Task WaitForPortReady(string host, int port, int maxWaitSeconds)
    {
        var timeout = TimeSpan.FromSeconds(maxWaitSeconds);
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            var remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero) break;

            try
            {
                using var client = new TcpClient();
                var result = client.BeginConnect(host, port, null, null);
                var waitMs = (int)Math.Min(2000, remaining.TotalMilliseconds);
                var success = result.AsyncWaitHandle.WaitOne(waitMs);
                if (success && client.Connected)
                {
                    client.EndConnect(result);
                    LoggerHelper.Info($"全局启动：ADB 端口 {host}:{port} 已就绪（等待了 {stopwatch.Elapsed.TotalSeconds:F1} 秒）");
                    return;
                }
            }
            catch { /* 忽略瞬时连接异常 */ }

            remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero) break;
            var delay = remaining < TimeSpan.FromSeconds(3) ? remaining : TimeSpan.FromSeconds(3);
            await Task.Delay(delay);
        }

        LoggerHelper.Warning($"全局启动：等待 ADB 端口 {host}:{port} 超时（已实际等待 {stopwatch.Elapsed.TotalSeconds:F1} 秒）");
    }

    /// <summary>
    /// 启动所有模拟器，通过端口检测等待就绪，然后执行实例任务（端口检测版）
    /// </summary>
    public static async Task StartAllAndRunTasksWithPortCheck()
    {
        try
        {
            var emulators = DiscoverEmulators();
            if (emulators.Count == 0)
            {
                LoggerHelper.Warning("全局启动：没有配置模拟器");
                return;
            }

            StartEmulatorProcesses(emulators);

            var manager = MaaProcessorManager.Instance;
            var allInstances = manager.GetAllInstanceIdsAndNames().ToList();
            LoggerHelper.Info($"全局启动：准备执行 {allInstances.Count} 个实例任务");

            var portTasks = new List<Task>();
            for (var i = 0; i < allInstances.Count; i++)
            {
                var instanceId = allInstances[i].Id;
                manager.EnsureInstanceLoaded(instanceId);
                var instVm = manager.GetViewModel(instanceId);
                if (instVm == null || instVm.IsRunning) continue;

                var idx = i;
                var emuArgs = idx < emulators.Count ? emulators[idx].Args : string.Empty;
                var port = DerivePort(emuArgs, idx);
                const string host = "127.0.0.1";
                LoggerHelper.Info($"全局启动：等待 {allInstances[idx].Name} 的 ADB 端口 {host}:{port} 就绪...");

                portTasks.Add(Task.Run(async () =>
                {
                    await WaitForPortReady(host, port, 120);
                    DispatcherHelper.RunOnMainThread(() =>
                    {
                        Instances.InstanceTabBarViewModel.SwitchToInstanceById(allInstances[idx].Id);
                        instVm.TryReadAdbDeviceFromConfig(false, false, true, false);
                        instVm.Processor.Start();
                    });
                }));
            }

            await Task.WhenAll(portTasks);
            LoggerHelper.Info("全局启动：所有实例已启动");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"全局启动失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 启动所有模拟器，等待固定时间后执行实例任务（定时器版）
    /// </summary>
    public static async Task StartAllAndRunTasksWithDelay()
    {
        try
        {
            var waitTimeStr = GlobalConfiguration.GetValue(ConfigurationKeys.GlobalWaitSoftwareTime, "60");
            var waitTime = double.TryParse(waitTimeStr, out var parsed) ? parsed : 60.0;

            var emulators = DiscoverEmulators();
            if (emulators.Count == 0)
            {
                LoggerHelper.Warning("全局启动：没有配置模拟器");
                return;
            }

            StartEmulatorProcesses(emulators);
            LoggerHelper.Info($"全局启动：等待模拟器启动 {waitTime} 秒...");
            await Task.Delay(TimeSpan.FromSeconds(waitTime));

            var manager = MaaProcessorManager.Instance;
            var allInstances = manager.GetAllInstanceIdsAndNames().ToList();
            LoggerHelper.Info($"全局启动：开始执行 {allInstances.Count} 个实例任务");

            for (var i = 0; i < allInstances.Count; i++)
            {
                var instanceId = allInstances[i].Id;
                manager.EnsureInstanceLoaded(instanceId);
                var vm = manager.GetViewModel(instanceId);
                if (vm != null && !vm.IsRunning)
                {
                    var idx = i;
                    DispatcherHelper.RunOnMainThread(() =>
                    {
                        Instances.InstanceTabBarViewModel.SwitchToInstanceById(allInstances[idx].Id);
                        vm.StartTask();
                    });
                    await Task.Delay(3000);
                }
            }

            LoggerHelper.Info("全局启动：所有实例已启动");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"全局启动失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 启动所有模拟器，等待固定时间后执行实例任务（设置页手动触发版）
    /// </summary>
    public static async Task StartAllAndRunTasksManual(double waitSoftwareTime, IEnumerable<ViewModels.UsersControls.Settings.EmulatorStartEntry> emulatorEntries)
    {
        try
        {
            var manager = MaaProcessorManager.Instance;
            var allInstances = manager.GetAllInstanceIdsAndNames().ToList();

            LoggerHelper.Info($"全局启动：准备启动 {emulatorEntries.Count()} 个模拟器");

            // 并行启动所有模拟器
            foreach (var entry in emulatorEntries)
            {
                if (string.IsNullOrWhiteSpace(entry.SoftwarePath)) continue;
                if (!System.IO.File.Exists(entry.SoftwarePath))
                {
                    LoggerHelper.Warning($"全局启动：模拟器路径不存在: {entry.SoftwarePath}");
                    continue;
                }
                LoggerHelper.Info($"全局启动：启动 {entry.Name} - {entry.SoftwarePath} {entry.EmulatorConfig}");
                var startInfo = new ProcessStartInfo
                {
                    FileName = entry.SoftwarePath,
                    UseShellExecute = true,
                    CreateNoWindow = false
                };
                if (!string.IsNullOrWhiteSpace(entry.EmulatorConfig))
                    startInfo.Arguments = entry.EmulatorConfig;
                Process.Start(startInfo);
            }

            LoggerHelper.Info($"全局启动：等待模拟器启动 {waitSoftwareTime} 秒...");
            await Task.Delay(TimeSpan.FromSeconds(waitSoftwareTime));

            LoggerHelper.Info("全局启动：开始执行所有实例任务");

            var configuredCount = emulatorEntries.Count();
            var instanceCount = allInstances.Count;
            if (configuredCount != instanceCount)
            {
                LoggerHelper.Warning($"全局启动：实例数量与模拟器配置数量不一致（实例数={instanceCount}，配置数={configuredCount}），按索引匹配");
            }

            var maxIndex = Math.Min(configuredCount, instanceCount);
            for (var i = 0; i < maxIndex; i++)
            {
                var instanceId = allInstances[i].Id;
                manager.EnsureInstanceLoaded(instanceId);
                var vm = manager.GetViewModel(instanceId);
                if (vm != null && !vm.IsRunning)
                {
                    var idx = i;
                    DispatcherHelper.RunOnMainThread(() =>
                    {
                        Instances.InstanceTabBarViewModel.SwitchToInstanceById(allInstances[idx].Id);
                        vm.StartTask();
                    });
                    await Task.Delay(3000);
                }
            }

            if (configuredCount > instanceCount)
                LoggerHelper.Warning($"全局启动：有 {configuredCount - instanceCount} 个模拟器配置未映射到实例，已跳过");
            else if (instanceCount > configuredCount)
                LoggerHelper.Warning($"全局启动：有 {instanceCount - configuredCount} 个实例没有模拟器配置，未启动");

            LoggerHelper.Info("全局启动：所有实例已启动");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"全局启动失败: {ex.Message}", ex);
        }
    }
}
