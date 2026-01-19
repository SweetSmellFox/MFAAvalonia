using MFAAvalonia.Configuration;
using MFAAvalonia.Helper;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.Extensions.MaaFW;

/// <summary>
/// 脚本执行辅助类
/// </summary>
public static class ScriptRunner
{
    /// <summary>
    /// 运行脚本（前置或后置）
    /// </summary>
    /// <param name="scriptType">脚本类型: "Prescript" 或 "Post-script"</param>
    public static async Task RunScriptAsync(string scriptType = "Prescript")
    {
        var configKey = scriptType switch
        {
            "Prescript" => ConfigurationKeys.Prescript,
            "Post-script" => ConfigurationKeys.Postscript,
            _ => null
        };

        if (configKey == null)
            return;

        var scriptPath = ConfigurationManager.CurrentInstance.GetValue(configKey, string.Empty);
        if (string.IsNullOrWhiteSpace(scriptPath))
            return;

        if (!await ExecuteScriptAsync(scriptPath))
        {
            LoggerHelper.Error($"Failed to execute the {scriptType}.");
        }
    }

    /// <summary>
    /// 执行脚本文件
    /// </summary>
    /// <param name="scriptPath">脚本路径</param>
    /// <returns>是否执行成功</returns>
    public static async Task<bool> ExecuteScriptAsync(string scriptPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(scriptPath))
                return false;

            string fileName;
            string arguments;

            if (scriptPath.StartsWith('\"'))
            {
                var parts = scriptPath.Split("\"", 3);
                fileName = parts[1];
                arguments = parts.Length > 2 ? parts[2] : string.Empty;
            }
            else
            {
                fileName = scriptPath;
                arguments = string.Empty;
            }

            bool createNoWindow = arguments.Contains("-noWindow");
            bool minimized = arguments.Contains("-minimized");

            if (createNoWindow)
            {
                arguments = arguments.Replace("-noWindow", string.Empty).Trim();
            }

            if (minimized)
            {
                arguments = arguments.Replace("-minimized", string.Empty).Trim();
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WindowStyle = minimized ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal,
                    CreateNoWindow = createNoWindow,
                    UseShellExecute = !createNoWindow,
                },
            };
            process.Start();
            await process.WaitForExitAsync();
            return true;
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"Script execution failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 启动可运行文件并等待
    /// </summary>
    /// <param name="exePath">可执行文件路径</param>
    /// <param name="waitTimeInSeconds">等待时间（秒）</param>
    /// <param name="token">取消令牌</param>
    /// <param name="logAction">日志回调</param>
    /// <returns>启动的进程</returns>
    public static async Task<Process?> StartRunnableFileAsync(
        string exePath, 
        double waitTimeInSeconds, 
        CancellationToken token,
        Action<double>? logAction = null)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return null;

        var processName = Path.GetFileNameWithoutExtension(exePath);
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true,
            CreateNoWindow = false
        };

        Process? softwareProcess = null;
        var emulatorConfig = ConfigurationManager.CurrentInstance.GetValue(ConfigurationKeys.EmulatorConfig, string.Empty);

        if (Process.GetProcessesByName(processName).Length == 0)
        {
            if (!string.IsNullOrWhiteSpace(emulatorConfig))
            {
                startInfo.Arguments = emulatorConfig;
            }
            softwareProcess = Process.Start(startInfo);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(emulatorConfig))
            {
                startInfo.Arguments = emulatorConfig;
            }
            softwareProcess = Process.Start(startInfo);
        }

        for (double remainingTime = waitTimeInSeconds + 1; remainingTime > 0; remainingTime -= 1)
        {
            if (token.IsCancellationRequested)
                return softwareProcess;

            logAction?.Invoke(remainingTime);
            await Task.Delay(1000, token);
        }

        return softwareProcess;
    }
}