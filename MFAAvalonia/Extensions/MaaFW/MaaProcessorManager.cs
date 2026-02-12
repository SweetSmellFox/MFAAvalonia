using System;
using System.Collections.Generic;
using System.Linq;
using MFAAvalonia.Configuration;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Pages;

namespace MFAAvalonia.Extensions.MaaFW;

public sealed class MaaProcessorManager
{
    private static readonly Lazy<MaaProcessorManager> LazyInstance = new(() => new MaaProcessorManager());
    public static MaaProcessorManager Instance => LazyInstance.Value;
    public static bool IsInstanceCreated => LazyInstance.IsValueCreated;

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
                    // 使用i18n的"配置+数字"格式命名
                    var configName = LangKeys.Config.ToLocalization();
                    var nextNumber = GetNextInstanceNumber();
                    _instanceNames[instanceId] = $"{configName} {nextNumber}";
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
            } while (_instances.ContainsKey(id));
        }

        return id;
    }

    public static string CreateInstanceId()
    {
        var id = Guid.NewGuid().ToString("N");
        return id.Length > 8 ? id[..8] : id;
    }

    /// <summary>
    /// 获取下一个可用的实例编号
    /// </summary>
    private int GetNextInstanceNumber()
    {
        var configName = LangKeys.Config.ToLocalization();
        var usedNumbers = new HashSet<int>();

        foreach (var name in _instanceNames.Values)
        {
            if (name.StartsWith(configName))
            {
                var suffix = name[configName.Length..].Trim();
                if (int.TryParse(suffix, out var num))
                {
                    usedNumbers.Add(num);
                }
            }
        }

        // 找到最小的未使用编号
        var next = 1;
        while (usedNumbers.Contains(next))
            next++;
        return next;
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
    /// <summary>
    /// 需要延迟加载的实例ID列表
    /// </summary>
    private readonly List<string> _pendingInstanceIds = new();
    private bool _isLazyLoadingComplete;

    public void LoadInstanceConfig()
    {
        var listStr = GlobalConfiguration.GetValue(ConfigurationKeys.InstanceList, "");

        if (string.IsNullOrEmpty(listStr))
        {
            SaveInstanceConfig();
            return;
        }

        var ids = listStr.Split(',', StringSplitOptions.RemoveEmptyEntries);

        lock (_lock)
        {
            if (MaaProcessor.Interface == null)
            {
                MaaProcessor.ReadInterface();
            }

            // 1. 恢复顺序和名称（不创建实例）
            var orderStr = GlobalConfiguration.GetValue(ConfigurationKeys.InstanceOrder, "");
            _instanceOrder.Clear();
            if (!string.IsNullOrEmpty(orderStr))
            {
                var orders = orderStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var o in orders)
                {
                    if (ids.Contains(o))
                        _instanceOrder.Add(o);
                }
            }

            foreach (var id in ids)
            {
                if (!_instanceOrder.Contains(id))
                    _instanceOrder.Add(id);

                var nameKey = string.Format(ConfigurationKeys.InstanceNameTemplate, id);
                var name = GlobalConfiguration.GetValue(nameKey, "");
                if (!string.IsNullOrEmpty(name))
                    _instanceNames[id] = name;
                else if (!_instanceNames.ContainsKey(id))
                    _instanceNames[id] = id;
            }

            // 2. 清理不在配置中的实例
            var validIds = new HashSet<string>(ids);
            var toRemove = _instances.Keys.Where(k => !validIds.Contains(k)).ToList();
            foreach (var key in toRemove)
            {
                if (_instances.TryGetValue(key, out var p))
                {
                    p.Dispose();
                    _instances.Remove(key);
                    _instanceNames.Remove(key);
                }
            }

            // 3. 优先加载 ActiveTab 实例
            var lastActive = GlobalConfiguration.GetValue(ConfigurationKeys.LastActiveInstance, "");
            if (string.IsNullOrEmpty(lastActive) || !validIds.Contains(lastActive))
                lastActive = _instanceOrder.FirstOrDefault() ?? ids[0];

            LoadSingleInstance(lastActive);
            Current = _instances[lastActive];

            // 4. 收集剩余待加载的实例ID
            _pendingInstanceIds.Clear();
            foreach (var id in _instanceOrder)
            {
                if (id != lastActive && validIds.Contains(id))
                    _pendingInstanceIds.Add(id);
            }

            _isLazyLoadingComplete = false;
        }
    }

    /// <summary>
    /// 加载单个实例（内部方法，需在锁内调用或确保线程安全）
    /// </summary>
    private void LoadSingleInstance(string id)
    {
        if (!_instances.ContainsKey(id))
        {
            CreateInstanceInternal(id, setCurrent: false);
        }
        // 无论实例是否已存在（如构造函数中创建的 default），都需要确保初始化数据
        _instances[id].InitializeData();
    }

    /// <summary>
    /// 懒加载第二阶段：加载当天有定时任务的实例
    /// </summary>
    public void LoadScheduledInstances()
    {
        var timerModel = MFAAvalonia.ViewModels.Other.TimerModel.Instance;
        var scheduledInstanceIds = new HashSet<string>();

        var now = DateTime.Now;
        foreach (var timer in timerModel.Timers)
        {
            if (timer.IsOn
                && timer.ScheduleConfig.ShouldTrigger(now)
                && !string.IsNullOrEmpty(timer.TimerConfig))
            {
                scheduledInstanceIds.Add(timer.TimerConfig);
            }
        }

        lock (_lock)
        {
            var loaded = new List<string>();
            foreach (var id in _pendingInstanceIds)
            {
                if (scheduledInstanceIds.Contains(id))
                {
                    LoadSingleInstance(id);
                    loaded.Add(id);
                }
            }

            foreach (var id in loaded)
                _pendingInstanceIds.Remove(id);
        }
    }

    /// <summary>
    /// 懒加载第三阶段：逐个加载剩余实例
    /// </summary>
    public async System.Threading.Tasks.Task LoadRemainingInstancesAsync()
    {
        while (true)
        {
            string? nextId = null;
            lock (_lock)
            {
                if (_pendingInstanceIds.Count == 0)
                {
                    _isLazyLoadingComplete = true;
                    return;
                }

                nextId = _pendingInstanceIds[0];
                _pendingInstanceIds.RemoveAt(0);
            }

            if (nextId != null)
            {
                lock (_lock)
                {
                    LoadSingleInstance(nextId);
                }

                // 每加载一个实例后等待1秒，缓慢加载避免卡UI
                await System.Threading.Tasks.Task.Delay(1000);
            }
        }
    }

    /// <summary>
    /// 检查实例是否已加载
    /// </summary>
    public bool IsInstanceLoaded(string instanceId)
    {
        lock (_lock)
        {
            return _instances.ContainsKey(instanceId);
        }
    }

    /// <summary>
    /// 确保指定实例已加载（按需加载）
    /// </summary>
    public void EnsureInstanceLoaded(string instanceId)
    {
        lock (_lock)
        {
            if (!_instances.ContainsKey(instanceId))
            {
                LoadSingleInstance(instanceId);
                _pendingInstanceIds.Remove(instanceId);
            }
        }
    }

    /// <summary>
    /// 获取所有实例ID和名称（包括尚未加载的），供定时器UI使用
    /// </summary>
    public List<(string Id, string Name)> GetAllInstanceIdsAndNames()
    {
        lock (_lock)
        {
            var result = new List<(string, string)>();
            foreach (var id in _instanceOrder)
            {
                var name = _instanceNames.TryGetValue(id, out var n) ? n : id;
                result.Add((id, name));
            }
            return result;
        }
    }

    /// <summary>
    /// 启动懒加载流程（三阶段）
    /// </summary>
    public async System.Threading.Tasks.Task StartLazyLoadingAsync()
    {
        try
        {
            // 阶段2：加载当天有定时任务的实例
            LoadScheduledInstances();
            LoggerHelper.Info("[懒加载] 已加载当天有定时任务的实例");

            // 让UI有时间响应
            await System.Threading.Tasks.Task.Delay(100);

            // 阶段3：逐个加载剩余实例
            await LoadRemainingInstancesAsync();
            LoggerHelper.Info("[懒加载] 所有实例加载完成");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"[懒加载] 加载实例失败: {ex}");
        }
    }
}
