using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;

namespace MFAAvalonia.Configuration;

public static class GlobalConfiguration
{
    private static readonly object _fileLock = new();
    private static readonly string _configPath = Path.Combine(
        AppContext.BaseDirectory,
        "appsettings.json");

    private static IConfigurationRoot LoadConfiguration()
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

    public static void SetValue(string key, string value)
    {
        lock (_fileLock)
        {
            var configDict = File.Exists(_configPath) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_configPath)) : new Dictionary<string, string>();

            configDict[key] = value;

            Directory.CreateDirectory(Path.GetDirectoryName(_configPath));
            File.WriteAllText(_configPath,
                JsonSerializer.Serialize(configDict, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
        }
    }

    public static string GetValue(string key, string defaultValue = "")
    {
        var config = LoadConfiguration();
        return config[key] ?? defaultValue;
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
