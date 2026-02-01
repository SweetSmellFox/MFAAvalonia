using Avalonia.Collections;
using MFAAvalonia;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MFAAvalonia.Configuration;

public static class ConfigurationManager
{
    private static readonly string _configDir = Path.Combine(
        AppContext.BaseDirectory,
        "config");
    public static readonly MFAConfiguration Maa = new("Maa", "maa_option", new Dictionary<string, object>());
    public static MFAConfiguration Current = new("Default", "config", new Dictionary<string, object>());
    public static InstanceConfiguration CurrentInstance => MaaProcessorManager.Instance?.Current?.InstanceConfiguration ?? new InstanceConfiguration("Default");

    public static AvaloniaList<MFAConfiguration> Configs { get; } = LoadConfigurations();

    public static event Action<string>? ConfigurationSwitched;

    public static bool IsSwitching { get; private set; }
    private static readonly object _switchLock = new();
    private static string? _pendingSwitchName;

    public static string ConfigName { get; set; }
    public static string GetCurrentConfiguration() => ConfigName;

    public static string GetActualConfiguration()
    {
        if (ConfigName.Equals("Default", StringComparison.OrdinalIgnoreCase))
            return "config";
        return $"mfa_{GetCurrentConfiguration()}";
    }

    public static void Initialize()
    {
        LoggerHelper.Info("Current Configuration: " + GetCurrentConfiguration());
    }

    public static void SwitchConfiguration(string? name)
    {
        _ = SwitchConfigurationAsync(name);
    }

    private static async Task SwitchConfigurationAsync(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (ConfigName.Equals(name, StringComparison.OrdinalIgnoreCase))
            return;

        if (!Configs.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            LoggerHelper.Warning($"配置 {name} 不存在，切换已取消");
            return;
        }

        lock (_switchLock)
        {
            if (IsSwitching)
            {
                _pendingSwitchName = name;
                return;
            }
            IsSwitching = true;
        }

        if (Instances.RootViewModel.IsRunning)
        {
            ToastHelper.Warn(LangKeys.SwitchConfiguration.ToLocalization());
            lock (_switchLock)
            {
                IsSwitching = false;
            }
            return;
        }

        await DispatcherHelper.RunOnMainThreadAsync(() =>
        {
            Instances.RootViewModel.SetConfigSwitchingState(true);
            Instances.RootViewModel.SetConfigSwitchProgress(5);
        });
        await Task.Run(() => MaaProcessorManager.Instance.Current.SetTasker());
        await Task.Delay(60);

        try
        {
            DispatcherHelper.PostOnMainThread(() => Instances.RootViewModel.SetConfigSwitchProgress(25));

            var config = Configs.First(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var configData = await Task.Run(() => JsonHelper.LoadConfig(config.FileName, new Dictionary<string, object>()));

            await DispatcherHelper.RunOnMainThreadAsync(() =>
            {
                SetDefaultConfig(name);
                ConfigName = name;
                config.SetConfig(configData);
                Current = config;
                Instances.RootViewModel.SetConfigSwitchProgress(55);
            });

            await DispatcherHelper.RunOnMainThreadAsync(() => ConfigurationSwitched?.Invoke(name));
            await Instances.ReloadConfigurationForSwitchAsync();

            DispatcherHelper.PostOnMainThread(() => Instances.RootViewModel.SetConfigSwitchProgress(98));
        }
        finally
        {
            await DispatcherHelper.RunOnMainThreadAsync(() => Instances.RootViewModel.SetConfigSwitchProgress(100));
            await Task.Delay(120);
            DispatcherHelper.PostOnMainThread(() => Instances.RootViewModel.SetConfigSwitchingState(false));

            lock (_switchLock)
            {
                IsSwitching = false;
            }
        }

        string? pending;
        lock (_switchLock)
        {
            pending = _pendingSwitchName;
            _pendingSwitchName = null;
        }

        if (!string.IsNullOrWhiteSpace(pending) && !pending.Equals(ConfigName, StringComparison.OrdinalIgnoreCase))
        {
            await SwitchConfigurationAsync(pending);
        }
    }

    public static void SetDefaultConfig(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        GlobalConfiguration.SetValue(ConfigurationKeys.DefaultConfig, name);
    }

    public static string GetDefaultConfig()
    {
        return GlobalConfiguration.GetValue(ConfigurationKeys.DefaultConfig, "Default");
    }

    private static AvaloniaList<MFAConfiguration> LoadConfigurations()
    {
        LoggerHelper.Info("Loading Configurations...");
        ConfigName = GetDefaultConfig();

        var collection = new AvaloniaList<MFAConfiguration>();

        var defaultConfigPath = Path.Combine(_configDir, "config.json");
        if (!Directory.Exists(_configDir))
            Directory.CreateDirectory(_configDir);
        if (!File.Exists(defaultConfigPath))
            File.WriteAllText(defaultConfigPath, "{}");
        if (ConfigName != "Default" && !File.Exists(Path.Combine(_configDir, $"mfa_{ConfigName}.json")))
            ConfigName = "Default";
        collection.Add(Current.SetConfig(JsonHelper.LoadConfig("config", new Dictionary<string, object>())));
        foreach (var file in Directory.EnumerateFiles(_configDir, "mfa_*.json"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (fileName == "maa_option" || fileName == "config") continue;
            string nameWithoutPrefix = fileName.StartsWith("mfa_")
                ? fileName.Substring("mfa_".Length)
                : fileName;
            var configs = JsonHelper.LoadConfig(fileName, new Dictionary<string, object>());

            var config = new MFAConfiguration(nameWithoutPrefix, fileName, configs);

            collection.Add(config);
        }

        Maa.SetConfig(JsonHelper.LoadConfig("maa_option", new Dictionary<string, object>()));

        if (AppRuntime.Args.TryGetValue("c", out var param) && !string.IsNullOrEmpty(param))
        {
            if (collection.Any(c => c.Name == param))
                ConfigName = param;
        }
        Current = collection.FirstOrDefault(c
                => !string.IsNullOrWhiteSpace(c.Name)
                && c.Name.Equals(ConfigName, StringComparison.OrdinalIgnoreCase))
            ?? Current;

        return collection;
    }

    public static void SaveConfiguration(string configName)
    {
        var config = Configs.FirstOrDefault(c => c.Name == configName);
        if (config != null)
        {
            JsonHelper.SaveConfig(config.FileName, config.Config);
        }
    }

    public static MFAConfiguration Add(string name)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "config");
        var newConfigPath = Path.Combine(configPath, $"{name}.json");
        var newConfig = new MFAConfiguration(name.Equals("config", StringComparison.OrdinalIgnoreCase) ? "Default" : name, name.Equals("config", StringComparison.OrdinalIgnoreCase) ? name : $"mfa_{name}", new Dictionary<string, object>());
        Configs.Add(newConfig);
        return newConfig;
    }
}
