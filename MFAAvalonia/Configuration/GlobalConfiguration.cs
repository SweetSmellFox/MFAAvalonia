using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using MFAAvalonia.Helper;

namespace MFAAvalonia.Configuration;

public static class GlobalConfiguration
{
    private static readonly object _fileLock = new();
    private static readonly string _configPath = Path.Combine(
        AppContext.BaseDirectory,
        "appsettings.json");

    public static string ConfigPath => _configPath;
    public static bool HasFileAccessError { get; private set; }
    public static string? LastFileAccessErrorMessage { get; private set; }

    private static IConfigurationRoot LoadConfiguration()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_configPath));
                File.WriteAllText(_configPath, "{}");
            }

            var builder = new ConfigurationBuilder()
                .SetBasePath(Path.GetDirectoryName(_configPath))
                .AddJsonFile(_configPath, optional: false, reloadOnChange: false);

            return builder.Build();
        }
        catch (InvalidDataException ex)
        {
            ReportFileAccessError(ex);
        }
        catch (IOException ex)
        {
            ReportFileAccessError(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            ReportFileAccessError(ex);
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>())
            .Build();
    }

    public static void SetValue(string key, string value)
    {
        lock (_fileLock)
        {
            try
            {
                var configDict = new Dictionary<string, string>();
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    configDict = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                        ?? new Dictionary<string, string>();
                }

                configDict[key] = value;

                Directory.CreateDirectory(Path.GetDirectoryName(_configPath));
                File.WriteAllText(_configPath,
                    JsonSerializer.Serialize(configDict, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
            }
            catch (IOException ex)
            {
                ReportFileAccessError(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                ReportFileAccessError(ex);
            }
        }
    }

    public static string GetValue(string key, string defaultValue = "")
    {
        var config = LoadConfiguration();
        return config[key] ?? defaultValue;
    }

    private static void ReportFileAccessError(Exception ex)
    {
        HasFileAccessError = true;
        LastFileAccessErrorMessage = ex.Message;
        LoggerHelper.Error($"全局配置文件访问失败: {_configPath}", ex);
    }

    public static string GetTimer(int i, string defaultValue)
    {
        return GetValue($"Timer.Timer{i + 1}", defaultValue);
    }

    public static void SetTimer(int i, string value)
    {
        SetValue($"Timer.Timer{i + 1}", value);
    }

    public static string GetTimerTime(int i, string defaultValue)
    {
        return GetValue($"Timer.Timer{i + 1}Time", defaultValue);
    }

    public static void SetTimerTime(int i, string value)
    {
        SetValue($"Timer.Timer{i + 1}Time", value);
    }

    public static string GetTimerConfig(int i, string defaultValue)
    {
        return GetValue($"Timer.Timer{i + 1}.Config", defaultValue);
    }

    public static void SetTimerConfig(int i, string value)
    {
        SetValue($"Timer.Timer{i + 1}.Config", value);
    }

    public static string GetTimerSchedule(int i, string defaultValue)
    {
        return GetValue($"Timer.Timer{i + 1}.Schedule", defaultValue);
    }

    public static void SetTimerSchedule(int i, string value)
    {
        SetValue($"Timer.Timer{i + 1}.Schedule", value);
    }
}
