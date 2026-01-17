using Avalonia.Controls;
using Serilog;
using SharpHook.Data;
using System;
using System.Collections.Generic;

namespace MFAAvalonia.Helper;

public static class LoggerHelper
{
    private static Serilog.Core.Logger? _logger;
    private static readonly List<(LogLevel level, string message)> _logCache = [];

    public static void InitializeLogger()
    {
        if (Design.IsDesignMode)
            return;
        if (_logger != null) return;
        _logger = new LoggerConfiguration()
            .WriteTo.File(
                $"logs/log-.log",
                rollingInterval: RollingInterval.Day,
                shared: true,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}][{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}][{Level:u3}] {Message:lj}{NewLine}{Exception}").CreateLogger();
        FlushCache();
    }

    public static void DisposeLogger()
    {
        _logger?.Dispose();
        _logger = null;
    }

    private static void FlushCache()
    {
        if (_logger == null) return;
        foreach (var (level, msg) in _logCache)
        {
            switch (level)
            {
                case LogLevel.Info:
                    _logger.Information(msg);
                    break;
                case LogLevel.Error:
                    _logger.Error(msg);
                    break;
                case LogLevel.Warn:
                    _logger.Warning(msg);
                    break;
            }
        }
        _logCache.Clear();
    }

    public static void Info(object? message)
    {
        if (_logger == null)
        {
            _logCache.Add((LogLevel.Info, message?.ToString() ?? string.Empty));
        }
        else
        {
            _logger.Information(message?.ToString() ?? string.Empty);
        }
    }
    
    public static void Debug(object? message)
    {
        if (_logger == null)
        {
            _logCache.Add((LogLevel.Debug, message?.ToString() ?? string.Empty));
        }
        else
        {
            _logger.Debug(message?.ToString() ?? string.Empty);
        }
    }
    
    public static void Error(object message)
    {
        if (_logger == null)
        {
            _logCache.Add((LogLevel.Error, message.ToString() ?? string.Empty));
        }
        else
        {
            _logger.Error(message.ToString() ?? string.Empty);
        }
    }

    public static void Error(object message, Exception e)
    {
        var errorMsg = $"{message}\n{e}";
        if (_logger == null)
        {
            _logCache.Add((LogLevel.Error, errorMsg));
        }
        else
        {
            _logger.Error(errorMsg);
        }
    }
    
    public static void Warn(object message)
    {
        if (_logger == null)
        {
            _logCache.Add((LogLevel.Warn, message.ToString() ?? string.Empty));
        }
        else
        {
            _logger.Warning(message.ToString() ?? string.Empty);
        }
    }
    
    public static void Warning(object message)
    {
        if (_logger == null)
        {
            _logCache.Add((LogLevel.Warn, message.ToString() ?? string.Empty));
        }
        else
        {
            _logger.Warning(message.ToString() ?? string.Empty);
        }
    }
}
