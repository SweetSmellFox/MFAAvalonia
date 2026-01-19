using MFAAvalonia.Helper.Converters;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace MFAAvalonia.Configuration;

public sealed class InstanceConfiguration
{
    private readonly string _instanceId;

    public InstanceConfiguration(string instanceId)
    {
        _instanceId = instanceId;
    }

    private MFAConfiguration Config => ConfigurationManager.Current;

    private string ScopedKey(string key) => $"Instance.{_instanceId}.{key}";

    public bool ContainsKey(string key)
        => Config.ContainsKey(ScopedKey(key)) || Config.ContainsKey(key);

    public void SetValue(string key, object? value)
    {
        if (ConfigurationManager.IsSwitching && key == ConfigurationKeys.TaskItems)
        {
            return;
        }

        Config.SetValue(ScopedKey(key), value);
    }
    public T GetValue<T>(string key, T defaultValue)
    {
        var scopedKey = ScopedKey(key);
        if (Config.ContainsKey(scopedKey))
        {
            return Config.GetValue<T>(scopedKey, defaultValue);
        }

        if (Config.ContainsKey(key))
        {
            var value = Config.GetValue<T>(key, defaultValue);
            SetValue(key, value);
            return value;
        }

        return defaultValue;
    }

    public T GetValue<T>(string key, T defaultValue, List<T> whitelist)
    {
        var scopedKey = ScopedKey(key);
        if (Config.ContainsKey(scopedKey))
        {
            return Config.GetValue<T>(scopedKey, defaultValue, whitelist);
        }

        if (Config.ContainsKey(key))
        {
            var value = Config.GetValue<T>(key, defaultValue, whitelist);
            SetValue(key, value);
            return value;
        }

        return defaultValue;
    }

    public T GetValue<T>(string key, T defaultValue, Dictionary<object, T> options)
    {
        var scopedKey = ScopedKey(key);
        if (Config.ContainsKey(scopedKey))
        {
            return Config.GetValue<T>(scopedKey, defaultValue, options);
        }

        if (Config.ContainsKey(key))
        {
            var value = Config.GetValue<T>(key, defaultValue, options);
            SetValue(key, value);
            return value;
        }

        return defaultValue;
    }

    public T GetValue<T>(string key, T defaultValue, T? noValue = default, params JsonConverter[] valueConverters)
    {
        var scopedKey = ScopedKey(key);
        if (Config.ContainsKey(scopedKey))
        {
            return Config.GetValue<T>(scopedKey, defaultValue, noValue, valueConverters);
        }

        if (Config.ContainsKey(key))
        {
            var value = Config.GetValue<T>(key, defaultValue, noValue, valueConverters);
            SetValue(key, value);
            return value;
        }

        return defaultValue;
    }

    public T GetValue<T>(string key, T defaultValue, List<T>? noValue = null, params JsonConverter[] valueConverters)
    {
        var scopedKey = ScopedKey(key);
        if (Config.ContainsKey(scopedKey))
        {
            return Config.GetValue<T>(scopedKey, defaultValue, noValue, valueConverters);
        }

        if (Config.ContainsKey(key))
        {
            var value = Config.GetValue<T>(key, defaultValue, noValue, valueConverters);
            SetValue(key, value);
            return value;
        }

        return defaultValue;
    }

    public bool TryGetValue<T>(string key, out T output, params JsonConverter[] valueConverters)
    {
        var scopedKey = ScopedKey(key);
        if (Config.TryGetValue(scopedKey, out output, valueConverters))
        {
            return true;
        }

        if (Config.TryGetValue(key, out output, valueConverters))
        {
            SetValue(key, output);
            return true;
        }

        output = default!;
        return false;
    }
}