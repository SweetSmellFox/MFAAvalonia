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
    
    private string DefaultScopedKey(string key) => $"Instance.default.{key}";

    public bool ContainsKey(string key)
        => Config.ContainsKey(ScopedKey(key)) || Config.ContainsKey(key);

    public void SetValue(string key, object? value)
    {
        // 移除配置切换时阻止 TaskItems 保存的逻辑
        // 这会导致任务配置无法正确更新到新配置中
        Config.SetValue(ScopedKey(key), value);
    }
    
    public T GetValue<T>(string key, T defaultValue)
    {
        var scopedKey = ScopedKey(key);
        if (Config.ContainsKey(scopedKey))
        {
            return Config.GetValue<T>(scopedKey, defaultValue);
        }

        // 新增：从 default 实例回退（用于多实例场景下的配置迁移）
        if (_instanceId != "default")
        {
            var defaultScopedKey = DefaultScopedKey(key);
            if (Config.ContainsKey(defaultScopedKey))
            {
                var value = Config.GetValue<T>(defaultScopedKey, defaultValue);
                SetValue(key, value);  // 复制到当前实例
                return value;
            }
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

        // 从 default 实例回退
        if (_instanceId != "default")
        {
            var defaultScopedKey = DefaultScopedKey(key);
            if (Config.ContainsKey(defaultScopedKey))
            {
                var value = Config.GetValue<T>(defaultScopedKey, defaultValue, whitelist);
                SetValue(key, value);
                return value;
            }
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

        // 从 default 实例回退
        if (_instanceId != "default")
        {
            var defaultScopedKey = DefaultScopedKey(key);
            if (Config.ContainsKey(defaultScopedKey))
            {
                var value = Config.GetValue<T>(defaultScopedKey, defaultValue, options);
                SetValue(key, value);
                return value;
            }
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

        // 从 default 实例回退
        if (_instanceId != "default")
        {
            var defaultScopedKey = DefaultScopedKey(key);
            if (Config.ContainsKey(defaultScopedKey))
            {
                var value = Config.GetValue<T>(defaultScopedKey, defaultValue, noValue, valueConverters);
                SetValue(key, value);
                return value;
            }
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

        // 从 default 实例回退
        if (_instanceId != "default")
        {
            var defaultScopedKey = DefaultScopedKey(key);
            if (Config.ContainsKey(defaultScopedKey))
            {
                var value = Config.GetValue<T>(defaultScopedKey, defaultValue, noValue, valueConverters);
                SetValue(key, value);
                return value;
            }
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

        // 从 default 实例回退
        if (_instanceId != "default")
        {
            var defaultScopedKey = DefaultScopedKey(key);
            if (Config.TryGetValue(defaultScopedKey, out output, valueConverters))
            {
                SetValue(key, output);
                return true;
            }
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