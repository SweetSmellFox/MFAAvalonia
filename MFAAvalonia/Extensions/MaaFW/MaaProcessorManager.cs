using System;
using System.Collections.Generic;

namespace MFAAvalonia.Extensions.MaaFW;

public sealed class MaaProcessorManager
{
    private static readonly Lazy<MaaProcessorManager> LazyInstance = new(() => new MaaProcessorManager());
    public static MaaProcessorManager Instance => LazyInstance.Value;

    private readonly Dictionary<string, MaaProcessor> _instances = new();
    private readonly object _lock = new();

    public MaaProcessor Current { get; private set; }

    private MaaProcessorManager()
    {
        // 使用固定的默认 InstanceId，确保重启后能读取到之前保存的配置
        Current = CreateInstanceInternal("default", setCurrent: true);
    }

    public IReadOnlyCollection<MaaProcessor> Instances
    {
        get
        {
            lock (_lock)
            {
                return new List<MaaProcessor>(_instances.Values).AsReadOnly();
            }
        }
    }

    public MaaProcessor CreateInstance(bool setCurrent = true)
    {
        return CreateInstance(CreateUniqueId(), setCurrent);
    }

    public MaaProcessor CreateInstance(string instanceId, bool setCurrent = true)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            instanceId = CreateUniqueId();
        }

        return CreateInstanceInternal(instanceId, setCurrent);
    }

    public bool TryGetInstance(string instanceId, out MaaProcessor processor)
    {
        lock (_lock)
        {
            return _instances.TryGetValue(instanceId, out processor!);
        }
    }

    public bool SwitchCurrent(string instanceId)
    {
        lock (_lock)
        {
            if (_instances.TryGetValue(instanceId, out var processor))
            {
                Current = processor;
                return true;
            }
        }

        return false;
    }

    private MaaProcessor CreateInstanceInternal(string instanceId, bool setCurrent)
    {
        lock (_lock)
        {
            if (_instances.TryGetValue(instanceId, out var existing))
            {
                if (setCurrent)
                {
                    Current = existing;
                }

                return existing;
            }

            var processor = new MaaProcessor(instanceId);
            _instances[instanceId] = processor;

            if (setCurrent)
            {
                Current = processor;
            }

            return processor;
        }
    }

    private string CreateUniqueId()
    {
        string id;
        lock (_lock)
        {
            do
            {
                id = CreateInstanceId();
            }
            while (_instances.ContainsKey(id));
        }

        return id;
    }

    public static string CreateInstanceId()
    {
        var id = Guid.NewGuid().ToString("N");
        return id.Length > 8 ? id[..8] : id;
    }
}