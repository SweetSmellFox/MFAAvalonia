using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using MFAAvalonia.Helper;

namespace MFAAvalonia;

public static class AppRuntime
{
    public static Dictionary<string, string> Args { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    private static Mutex? _mutex;
    private static bool _mutexReleased;
    private static readonly object _mutexLock = new();
    private static int _mutexOwnerThreadId = -1;

    public static bool IsNewInstance { get; private set; } = true;

    public static bool IsAutoStart => Args.ContainsKey("autostart");

    public static bool QuitAfterRun => Args.ContainsKey("quit-after-run");

    public static string? RequestedInstance =>
        Args.TryGetValue("instance", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    public static Dictionary<string, string> ParseArguments(string[] args)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("-", StringComparison.Ordinal))
            {
                var token = args[i].TrimStart('-');
                var equalsIndex = token.IndexOf('=');
                var key = NormalizeKey(equalsIndex >= 0 ? token[..equalsIndex] : token);
                var inlineValue = equalsIndex >= 0 ? token[(equalsIndex + 1)..] : null;

                if (inlineValue != null)
                {
                    parameters[key] = inlineValue;
                }
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    parameters[key] = args[i + 1];
                    i++;
                }
                else
                {
                    parameters[key] = "";
                }
            }
        }
        return parameters;
    }

    private static string NormalizeKey(string key)
    {
        return key.ToLowerInvariant() switch
        {
            "c" or "i" => "instance",
            "q" => "quit-after-run",
            "h" => "help",
            _ => key.ToLowerInvariant()
        };
    }

    public static bool IsHelpRequested(string[] args) => ParseArguments(args).ContainsKey("help");

    public static string GetHelpText(string? executablePath = null)
    {
        var executableName = string.IsNullOrWhiteSpace(executablePath)
            ? "MFAAvalonia"
            : Path.GetFileName(executablePath);

        return $"""
MFAAvalonia 命令行参数

用法:
  {executableName} [参数]

参数:
  -h, --help
      显示本帮助并退出

  --autostart
      启动后自动执行目标实例的任务

  -c, -i, --instance <实例名称或实例 ID>
      指定启动时激活的实例；与 --autostart 配合时指定自动执行的实例
      也支持 -c=<值>、-i=<值> 与 --instance=<值>

  -q, --quit-after-run
      本次命令行自动执行的任务完成后退出程序

示例:
  {executableName} --instance "日常任务"
  {executableName} --autostart -i "日常任务" --quit-after-run
""";
    }

    public static void Initialize(string[] args, string mutexName)
    {
        Args = ParseArguments(args);
        _mutex = new Mutex(true, mutexName, out var isNewInstance);
        IsNewInstance = isNewInstance;
        _mutexOwnerThreadId = Environment.CurrentManagedThreadId;
        _mutexReleased = false;
    }

    public static void ReleaseMutex()
    {
        if (_mutexReleased || _mutex == null)
        {
            return;
        }

        if (Environment.CurrentManagedThreadId != _mutexOwnerThreadId)
        {
            try
            {
                _ = DispatcherHelper.RunOnMainThreadAsync(ReleaseMutexInternal);
            }
            catch (Exception)
            {
                try
                {
                    _mutex?.Close();
                    _mutex = null;
                    _mutexReleased = true;
                }
                catch
                {
                }
            }
            return;
        }

        ReleaseMutexInternal();
    }

    private static void ReleaseMutexInternal()
    {
        lock (_mutexLock)
        {
            if (_mutexReleased || _mutex == null)
            {
                return;
            }

            try
            {
                _mutex.ReleaseMutex();
                _mutex.Close();
                _mutex = null;
                _mutexReleased = true;
            }
            catch (ApplicationException)
            {
                try
                {
                    _mutex?.Close();
                    _mutex = null;
                    _mutexReleased = true;
                }
                catch (Exception)
                {
                }
            }
            catch (Exception e)
            {
                LoggerHelper.Error($"释放应用互斥锁失败：原因={e.Message}", e);
            }
        }
    }
}
