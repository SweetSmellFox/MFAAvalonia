using MFAAvalonia.Helper;
using MFAAvalonia.Helper.Converters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;

namespace MFAAvalonia.Configuration;

public sealed class InstanceConfiguration
{
    private readonly string _instanceId;
    private Dictionary<string, object> _config;

    internal static readonly string InstancesDir = Path.Combine(
        AppContext.BaseDirectory, "config", "instances");

    public InstanceConfiguration(string instanceId)
    {
        _instanceId = instanceId;
        _config = LoadInstanceConfig();
    }

    /// <summary>
    /// 获取实例配置文件路径：config/instances/{id}.json
    /// </summary>
    public string GetConfigFilePath() =>
        Path.Combine(InstancesDir, $"{_instanceId}.json");

    /// <summary>
    /// 加载实例独立配置文件
    /// </summary>
    private Dictionary<string, object> LoadInstanceConfig()
    {
        if (!Directory.Exists(InstancesDir))
            Directory.CreateDirectory(InstancesDir);

        var filePath = GetConfigFilePath();
        if (!File.Exists(filePath))
            return new Dictionary<string, object>();

        return JsonHelper.LoadJson(filePath, new Dictionary<string, object>());
    }

    /// <summary>
    /// 保存实例配置到独立文件
    /// </summary>
    private void SaveInstanceConfig()
    {
        if (!Directory.Exists(InstancesDir))
            Directory.CreateDirectory(InstancesDir);

        JsonHelper.SaveJson(
            GetConfigFilePath(),
            _config,
            new MaaInterfaceSelectAdvancedConverter(false),
            new MaaInterfaceSelectOptionConverter(false));
    }

    private MFAConfiguration GlobalConfig => ConfigurationManager.Current;

    public bool ContainsKey(string key)
        => _config.ContainsKey(key) || GlobalConfig.ContainsKey(key);

    public void SetValue(string key, object? value)
    {
        if (value == null) return;
        _config[key] = value;
        SaveInstanceConfig();
    }

    public T GetValue<T>(string key, T defaultValue)
    {
        // 优先从实例独立配置读取
        if (_config.TryGetValue(key, out var data))
        {
            var result = ConvertValue<T>(data);
            if (result != null) return result;
        }

        // 回退：从全局 config.json 的旧 scoped key 读取（兼容过渡）
        var scopedKey = $"Instance.{_instanceId}.{key}";
        if (GlobalConfig.ContainsKey(scopedKey))
        {
            var value = GlobalConfig.GetValue<T>(scopedKey, defaultValue);
            // 迁移到实例文件
            SetValue(key, value);
            return value;
        }

        // 回退：从 default 实例的 scoped key 读取
        if (_instanceId != "default")
        {
            var defaultScopedKey = $"Instance.default.{key}";
            if (GlobalConfig.ContainsKey(defaultScopedKey))
            {
                var value = GlobalConfig.GetValue<T>(defaultScopedKey, defaultValue);
                SetValue(key, value);
                return value;
            }
        }

        // 回退：从全局 config.json 的无前缀 key 读取（最早版本兼容）
        if (GlobalConfig.ContainsKey(key))
        {
            var value = GlobalConfig.GetValue<T>(key, defaultValue);
            SetValue(key, value);
            return value;
        }

        return defaultValue;
    }

    public T GetValue<T>(string key, T defaultValue, List<T> whitelist)
    {
        var value = GetValue(key, defaultValue);
        return whitelist.Contains(value) ? value : defaultValue;
    }

    public T GetValue<T>(string key, T defaultValue, Dictionary<object, T> options)
    {
        if (_config.TryGetValue(key, out var data))
        {
            if (options != null && options.TryGetValue(data, out var result))
                return result;
            var converted = ConvertValue<T>(data);
            if (converted != null) return converted;
        }

        // 回退到全局配置
        var scopedKey = $"Instance.{_instanceId}.{key}";
        if (GlobalConfig.ContainsKey(scopedKey))
        {
            var value = GlobalConfig.GetValue<T>(scopedKey, defaultValue, options);
            SetValue(key, value);
            return value;
        }

        if (_instanceId != "default")
        {
            var defaultScopedKey = $"Instance.default.{key}";
            if (GlobalConfig.ContainsKey(defaultScopedKey))
            {
                var value = GlobalConfig.GetValue<T>(defaultScopedKey, defaultValue, options);
                SetValue(key, value);
                return value;
            }
        }

        if (GlobalConfig.ContainsKey(key))
        {
            var value = GlobalConfig.GetValue<T>(key, defaultValue, options);
            SetValue(key, value);
            return value;
        }

        return defaultValue;
    }

    public T GetValue<T>(string key, T defaultValue, T? noValue = default, params JsonConverter[] valueConverters)
    {
        if (_config.TryGetValue(key, out var data))
        {
            try
            {
                var settings = new JsonSerializerSettings();
                foreach (var converter in valueConverters)
                    settings.Converters.Add(converter);
                var result = JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(data), settings) ?? defaultValue;
                if (result != null && !result.Equals(noValue))
                    return result;
            }
            catch (Exception e)
            {
                LoggerHelper.Error($"类型转换失败: {e.Message}");
            }
        }

        // 回退到全局配置
        var scopedKey = $"Instance.{_instanceId}.{key}";
        if (GlobalConfig.ContainsKey(scopedKey))
        {
            var value = GlobalConfig.GetValue<T>(scopedKey, defaultValue, noValue, valueConverters);
            SetValue(key, value);
            return value;
        }

        if (_instanceId != "default")
        {
            var defaultScopedKey = $"Instance.default.{key}";
            if (GlobalConfig.ContainsKey(defaultScopedKey))
            {
                var value = GlobalConfig.GetValue<T>(defaultScopedKey, defaultValue, noValue, valueConverters);
                SetValue(key, value);
                return value;
            }
        }

        if (GlobalConfig.ContainsKey(key))
        {
            var value = GlobalConfig.GetValue<T>(key, defaultValue, noValue, valueConverters);
            SetValue(key, value);
            return value;
        }

        return defaultValue;
    }

    public T GetValue<T>(string key, T defaultValue, List<T>? noValue = null, params JsonConverter[] valueConverters)
    {
        if (_config.TryGetValue(key, out var data))
        {
            try
            {
                var settings = new JsonSerializerSettings();
                foreach (var converter in valueConverters)
                    settings.Converters.Add(converter);
                var result = JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(data), settings) ?? defaultValue;
                if (noValue == null || !noValue.Contains(result))
                    return result;
            }
            catch (Exception e)
            {
                LoggerHelper.Error($"类型转换失败: {e.Message}");
            }
        }

        // 回退到全局配置
        var scopedKey = $"Instance.{_instanceId}.{key}";
        if (GlobalConfig.ContainsKey(scopedKey))
        {
            var value = GlobalConfig.GetValue<T>(scopedKey, defaultValue, noValue, valueConverters);
            SetValue(key, value);
            return value;
        }

        if (_instanceId != "default")
        {
            var defaultScopedKey = $"Instance.default.{key}";
            if (GlobalConfig.ContainsKey(defaultScopedKey))
            {
                var value = GlobalConfig.GetValue<T>(defaultScopedKey, defaultValue, noValue, valueConverters);
                SetValue(key, value);
                return value;
            }
        }

        if (GlobalConfig.ContainsKey(key))
        {
            var value = GlobalConfig.GetValue<T>(key, defaultValue, noValue, valueConverters);
            SetValue(key, value);
            return value;
        }

        return defaultValue;
    }

    public bool TryGetValue<T>(string key, out T output, params JsonConverter[] valueConverters)
    {
        if (_config.TryGetValue(key, out var data))
        {
            try
            {
                var settings = new JsonSerializerSettings();
                foreach (var converter in valueConverters)
                    settings.Converters.Add(converter);
                output = JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(data), settings) ?? default!;
                return true;
            }
            catch
            {
                // fall through
            }
        }

        // 回退到全局配置
        var scopedKey = $"Instance.{_instanceId}.{key}";
        if (GlobalConfig.TryGetValue(scopedKey, out output, valueConverters))
        {
            SetValue(key, output);
            return true;
        }

        if (_instanceId != "default")
        {
            var defaultScopedKey = $"Instance.default.{key}";
            if (GlobalConfig.TryGetValue(defaultScopedKey, out output, valueConverters))
            {
                SetValue(key, output);
                return true;
            }
        }

        if (GlobalConfig.TryGetValue(key, out output, valueConverters))
        {
            SetValue(key, output);
            return true;
        }

        output = default!;
        return false;
    }

    /// <summary>
    /// 从实例配置文件中快速读取指定 key 的字符串值（不创建完整实例）
    /// </summary>
    public static string ReadValueFromFile(string instanceId, string key, string defaultValue = "")
    {
        var filePath = Path.Combine(InstancesDir, $"{instanceId}.json");
        if (!File.Exists(filePath)) return defaultValue;

        try
        {
            var data = JsonHelper.LoadJson(filePath, new Dictionary<string, object>());
            if (data.TryGetValue(key, out var value) && value is string str)
                return str;
        }
        catch
        {
            // ignore
        }

        return defaultValue;
    }

    /// <summary>
    /// 从磁盘重新加载配置（迁移后调用，刷新内存中的过期数据）
    /// </summary>
    public void ReloadFromDisk()
    {
        _config = LoadInstanceConfig();
    }

    /// <summary>
    /// 从另一个实例复制配置（排除实例名称）
    /// </summary>
    public void CopyFrom(InstanceConfiguration source)
    {
        foreach (var kvp in source._config)
        {
            if (kvp.Key == ConfigurationKeys.InstanceName) continue;
            _config[kvp.Key] = kvp.Value;
        }
        SaveInstanceConfig();
    }

    /// <summary>
    /// 批量设置配置（用于迁移，只保存一次）
    /// </summary>
    public void SetValues(Dictionary<string, object> values)
    {
        foreach (var kvp in values)
        {
            _config[kvp.Key] = kvp.Value;
        }
        SaveInstanceConfig();
    }

    /// <summary>
    /// 删除实例配置文件
    /// </summary>
    public void DeleteConfigFile()
    {
        var filePath = GetConfigFilePath();
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    /// <summary>
    /// 反序列化时使用的自定义转换器（与 SaveInstanceConfig 保持一致，确保读写对称）
    /// </summary>
    private static readonly JsonSerializerSettings DeserializeSettings = new()
    {
        Converters =
        {
            new MaaInterfaceSelectAdvancedConverter(false),
            new MaaInterfaceSelectOptionConverter(false),
        }
    };

    private static T? ConvertValue<T>(object data)
    {
        try
        {
            if (data is long longValue && typeof(T) == typeof(int))
                return (T)(object)Convert.ToInt32(longValue);

            if (data is T t)
                return t;

            if (data is JArray jArray)
                return JsonConvert.DeserializeObject<T>(jArray.ToString(), DeserializeSettings);

            if (data is JObject jObject)
                return JsonConvert.DeserializeObject<T>(jObject.ToString(), DeserializeSettings);
        }
        catch (Exception e)
        {
            LoggerHelper.Error("在进行类型转换时发生错误!", e);
        }

        return default;
    }
}
