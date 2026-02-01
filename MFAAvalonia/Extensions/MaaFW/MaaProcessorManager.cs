using System;
using System.Collections.Generic;
using System.Linq;
using MFAAvalonia.Configuration;
using MFAAvalonia.ViewModels.Pages;

namespace MFAAvalonia.Extensions.MaaFW;

public sealed class MaaProcessorManager
{
    private static readonly Lazy<MaaProcessorManager> LazyInstance = new(() => new MaaProcessorManager());
    public static MaaProcessorManager Instance => LazyInstance.Value;

    private readonly Dictionary<string, MaaProcessor> _instances = new();
    private readonly Dictionary<string, string> _instanceNames = new();
    private readonly List<string> _instanceOrder = new();
    private readonly Dictionary<string, TaskQueueViewModel> _viewModels = new();
    private readonly object _lock = new();

    public MaaProcessor Current { get; private set; }

    private MaaProcessorManager()
    {
        // 构造函数初始化默认状态，后续 LoadInstanceConfig 可覆盖
        Current = CreateInstanceInternal("default", setCurrent: true);
        _instanceNames["default"] = "Default";
        _instanceOrder.Add("default");
    }

    public IReadOnlyCollection<MaaProcessor> Instances
    {
        get
        {
            lock (_lock)
            {
                var list = new List<MaaProcessor>();
                // 按顺序添加
                foreach (var id in _instanceOrder)
                {
                    if (_instances.TryGetValue(id, out var processor))
                    {
                        list.Add(processor);
                    }
                }
                // 添加可能遗漏的（不在_instanceOrder中的）
                foreach (var kvp in _instances)
                {
                    if (!_instanceOrder.Contains(kvp.Key))
                    {
                        list.Add(kvp.Value);
                    }
                }
                return list.AsReadOnly();
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

        var processor = CreateInstanceInternal(instanceId, setCurrent);

        lock (_lock)
        {
            if (!_instanceOrder.Contains(instanceId))
            {
                _instanceOrder.Add(instanceId);
                if (!_instanceNames.ContainsKey(instanceId))
                {
                    _instanceNames[instanceId] = $"Instance {instanceId}";
                }
                SaveInstanceConfig();
            }
        }

        return processor;
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
                SaveInstanceConfig();
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

            var vm = new TaskQueueViewModel(instanceId);
            var processor = vm.Processor;
            _viewModels[instanceId] = vm;
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

    public MFAAvalonia.ViewModels.Pages.TaskQueueViewModel GetViewModel(string instanceId)
    {
        lock (_lock)
        {
            if (!_viewModels.TryGetValue(instanceId, out var vm))
            {
                vm = new MFAAvalonia.ViewModels.Pages.TaskQueueViewModel(instanceId);
                _viewModels[instanceId] = vm;
                _instances[instanceId] = vm.Processor;
            }
            return vm;
        }
    }

    public string GetInstanceName(string instanceId)
    {
        lock (_lock)
        {
            if (_instanceNames.TryGetValue(instanceId, out var name))
                return name;
            return instanceId;
        }
    }

    public void SetInstanceName(string instanceId, string name)
    {
        lock (_lock)
        {
            _instanceNames[instanceId] = name;
            SaveInstanceConfig();
        }
    }

    public bool RemoveInstance(string instanceId)
    {
        lock (_lock)
        {
            if (_instances.Count <= 1)
                return false; // 不能删除最后一个实例

            if (_instances.TryGetValue(instanceId, out var processor))
            {
                // 如果删除的是当前实例，切换到其他实例
                if (Current.InstanceId == instanceId)
                {
                    var otherId = _instanceOrder.FirstOrDefault(id => id != instanceId)
                                  ?? _instances.Keys.FirstOrDefault(k => k != instanceId);

                    if (otherId != null && _instances.TryGetValue(otherId, out var other))
                    {
                        Current = other;
                    }
                }

                processor.Dispose();
                _instances.Remove(instanceId);
                _instanceNames.Remove(instanceId);
                _instanceOrder.Remove(instanceId);
                _viewModels.Remove(instanceId);

                SaveInstanceConfig();
                return true;
            }
        }
        return false;
    }

    private void SaveInstanceConfig()
    {
        var list = string.Join(",", _instances.Keys);
        var order = string.Join(",", _instanceOrder);

        GlobalConfiguration.SetValue(ConfigurationKeys.InstanceList, list);
        GlobalConfiguration.SetValue(ConfigurationKeys.InstanceOrder, order);
        GlobalConfiguration.SetValue(ConfigurationKeys.LastActiveInstance, Current.InstanceId);
        foreach (var kvp in _instanceNames)
        {
            var key = string.Format(ConfigurationKeys.InstanceNameTemplate, kvp.Key);
            GlobalConfiguration.SetValue(key, kvp.Value);
        }
    }

    public void LoadInstanceConfig()
    {
        var listStr = GlobalConfiguration.GetValue(ConfigurationKeys.InstanceList, "");

        if (string.IsNullOrEmpty(listStr))
        {
            // 如果为空，保存当前默认状态
            SaveInstanceConfig();
            return;
        }

        var ids = listStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var loadedIds = new HashSet<string>();

        lock (_lock)
        {
            // 1. 加载/创建配置中的实例
            foreach (var id in ids)
            {
                loadedIds.Add(id);
                if (!_instances.ContainsKey(id))
                {
                    var processor = CreateInstanceInternal(id, setCurrent: false);
                    processor.InitializeData();
                }

                var nameKey = string.Format(ConfigurationKeys.InstanceNameTemplate, id);
                var name = GlobalConfiguration.GetValue(nameKey, "");
                if (!string.IsNullOrEmpty(name))
                {
                    _instanceNames[id] = name;
                }
                else if (!_instanceNames.ContainsKey(id))
                {
                    _instanceNames[id] = id;
                }
            }

            // 2. 清理不在配置中的实例
            var toRemove = _instances.Keys.Where(k => !loadedIds.Contains(k)).ToList();
            foreach (var key in toRemove)
            {
                if (_instances.TryGetValue(key, out var p))
                {
                    p.Dispose();
                    _instances.Remove(key);
                    _instanceNames.Remove(key);
                }
            }

            // 3. 恢复顺序
            var orderStr = GlobalConfiguration.GetValue(ConfigurationKeys.InstanceOrder, "");
            _instanceOrder.Clear();
            if (!string.IsNullOrEmpty(orderStr))
            {
                var orders = orderStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var o in orders)
                {
                    if (_instances.ContainsKey(o))
                    {
                        _instanceOrder.Add(o);
                    }
                }
            }

            // 补充漏掉的顺序
            foreach (var id in loadedIds)
            {
                if (!_instanceOrder.Contains(id))
                {
                    _instanceOrder.Add(id);
                }
            }

            // 4. 恢复激活状态
            var lastActive = GlobalConfiguration.GetValue(ConfigurationKeys.LastActiveInstance, "");
            if (!string.IsNullOrEmpty(lastActive) && _instances.ContainsKey(lastActive))
            {
                Current = _instances[lastActive];
            }
            else if (_instances.Count > 0)
            {
                Current = _instances.Values.First();
            }
        }
    }
}
