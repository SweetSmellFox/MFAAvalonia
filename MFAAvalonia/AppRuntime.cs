using System;
using System.Collections.Generic;
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

    public static Dictionary<string, string> ParseArguments(string[] args)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("-"))
            {
                string key = args[i].TrimStart('-').ToLower();
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
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
                LoggerHelper.Error(e);
            }
        }
    }
}