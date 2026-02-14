using MFAAvalonia.Configuration;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.ViewModels.Other;
using MFAAvalonia.ViewModels.Pages;
using MFAAvalonia.ViewModels.UsersControls.Settings;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW;

/// <summary>
/// 任务加载器
/// </summary>
public class TaskLoader(MaaInterface? maaInterface, TaskQueueViewModel taskQueueViewModel)
{
    public const string NEW_SEPARATOR = "<|||>";
    public const string OLD_SEPARATOR = ":";


    /// <summary>
    /// 加载任务列表
    /// </summary>
    public void LoadTasks(
        List<MaaInterface.MaaInterfaceTask> tasks,
        ObservableCollection<DragItemViewModel> tasksSource,
        ref bool firstTask,
        IList<DragItemViewModel>? oldDrags = null)
    {
        var instanceConfig = taskQueueViewModel.Processor.InstanceConfiguration;
        var currentTasks = instanceConfig.GetValue(ConfigurationKeys.CurrentTasks, new List<string>());
        if (currentTasks.Any(t => t.Contains(OLD_SEPARATOR) && !t.Contains(NEW_SEPARATOR)))
        {
            currentTasks = currentTasks
                .Select(item =>
                {
                    var parts = item.Split(OLD_SEPARATOR, 2);
                    return parts.Length == 2 ? $"{parts[0]}{NEW_SEPARATOR}{parts[1]}" : item;
                })
                .Distinct()
                .ToList();
        }
        // 如果传入了 oldDrags（用户当前的任务列表），优先使用它来保留用户的顺序和 check 状态
        // 只有当 oldDrags 为空时，才从配置中读取
        List<DragItemViewModel> drags;
        if (oldDrags != null && oldDrags.Count > 0)
        {
            drags = oldDrags.ToList();
        }
        else
        {
            var items = instanceConfig.GetValue(ConfigurationKeys.TaskItems, new List<MaaInterface.MaaInterfaceTask>()) ?? new List<MaaInterface.MaaInterfaceTask>();
            drags = items.Select(interfaceItem => new DragItemViewModel(interfaceItem) { OwnerViewModel = taskQueueViewModel }).ToList();
        }

        if (firstTask)
        {
            InitializeResources();
            firstTask = false;
        }

        var (updateList, removeList) = SynchronizeTaskItems(ref currentTasks, drags, tasks);
        instanceConfig.SetValue(ConfigurationKeys.CurrentTasks, currentTasks);
        
        updateList.RemoveAll(d => removeList.Contains(d));

        // 同步保存 TaskItems，确保多实例 fallback 时 CurrentTasks 和 TaskItems 一致
        // 避免非默认实例通过 fallback 读到已更新的 CurrentTasks 但旧的 TaskItems，
        // 导致新任务被误判为"已删除"
        // interface 加载失败时 maaInterface?.Task 为 null，此时不保存以免清空配置
        if (maaInterface?.Task != null)
        {
            instanceConfig.SetValue(ConfigurationKeys.TaskItems,
                updateList.Where(m => !m.IsResourceOptionItem).Select(m => m.InterfaceItem));
        }

        UpdateViewModels(updateList, tasks, tasksSource);
    }

    private void InitializeResources()
    {
        var allResources = maaInterface?.Resources.Values.Count > 0
            ? maaInterface.Resources.Values.ToList()
            :
            [
                new()
                {
                    Name = "Default",
                    Path = [MaaProcessor.ResourceBase]
                }
            ];

        // 获取当前控制器的名称
        var currentControllerName = GetCurrentControllerName();

        // 根据控制器过滤资源
        var filteredResources = FilterResourcesByController(allResources, currentControllerName);

        foreach (var resource in filteredResources)
        {
            resource.InitializeDisplayName();
            // 初始化资源的 SelectOptions
            InitializeResourceSelectOptions(resource, maaInterface, taskQueueViewModel.Processor.InstanceConfiguration);
        }
        taskQueueViewModel.CurrentResources = new ObservableCollection<MaaInterface.MaaInterfaceResource>(filteredResources);
        taskQueueViewModel.CurrentResource = taskQueueViewModel.Processor.InstanceConfiguration.GetValue(ConfigurationKeys.Resource, string.Empty);
        if (taskQueueViewModel.CurrentResources.Count > 0 && taskQueueViewModel.CurrentResources.All(r => r.Name != taskQueueViewModel.CurrentResource))
            taskQueueViewModel.CurrentResource = taskQueueViewModel.CurrentResources[0].Name ?? "Default";
    }

    /// <summary>
    /// 初始化资源的 SelectOptions（从 Option 字符串列表转换为 MaaInterfaceSelectOption 列表）
    /// 只初始化顶级选项，子选项会在运行时由 UpdateSubOptions 动态创建
    /// 会保留已有的值并从配置中恢复保存的值
    /// </summary>
    public static void InitializeResourceSelectOptions(MaaInterface.MaaInterfaceResource resource, MaaInterface? maaInterface, InstanceConfiguration config)
    {
        if (resource.Option == null || resource.Option.Count == 0)
        {
            resource.SelectOptions = null;
            return;
        }

        // 收集所有子选项名称（这些选项不应该在顶级初始化）
        var subOptionNames = new HashSet<string>();
        foreach (var optionName in resource.Option)
        {
            if (maaInterface?.Option?.TryGetValue(optionName, out var interfaceOption) == true)
            {
                if (interfaceOption.Cases != null)
                {
                    foreach (var caseOption in interfaceOption.Cases)
                    {
                        if (caseOption.Option != null)
                        {
                            foreach (var subOptionName in caseOption.Option)
                            {
                                subOptionNames.Add(subOptionName);
                            }
                        }
                    }
                }
            }
        }

        // 获取已保存的配置
        var savedResourceOptions = config.GetValue(
            ConfigurationKeys.ResourceOptionItems,
            new Dictionary<string, List<MaaInterface.MaaInterfaceSelectOption>>());

        Dictionary<string, MaaInterface.MaaInterfaceSelectOption>? savedDict = null;
        if (savedResourceOptions.TryGetValue(resource.Name ?? string.Empty, out var savedOptions) && savedOptions != null)
        {
            savedDict = savedOptions.ToDictionary(o => o.Name ?? string.Empty);
        }

        // 保留已有的 SelectOptions 值
        var existingDict = resource.SelectOptions?.ToDictionary(o => o.Name ?? string.Empty)
            ?? new Dictionary<string, MaaInterface.MaaInterfaceSelectOption>();

        // 只初始化顶级选项（不是子选项的选项）
        resource.SelectOptions = resource.Option
            .Where(optionName => !subOptionNames.Contains(optionName))
            .Select(optionName =>
            {
                // 优先使用已有的值（保留运行时的修改）
                if (existingDict.TryGetValue(optionName, out var existingOpt))
                {
                    return existingOpt;
                }

                // 其次使用配置中保存的值
                if (savedDict?.TryGetValue(optionName, out var savedOpt) == true)
                {
                    // 克隆保存的选项，避免引用问题
                    var clonedOpt = new MaaInterface.MaaInterfaceSelectOption
                    {
                        Name = savedOpt.Name,
                        Index = savedOpt.Index,
                        Data = savedOpt.Data != null ? new Dictionary<string, string?>(savedOpt.Data) : null,
                        SubOptions = savedOpt.SubOptions != null ? CloneSubOptions(savedOpt.SubOptions) : null
                    };
                    return clonedOpt;
                }

                // 最后创建新的并设置默认值
                var selectOption = new MaaInterface.MaaInterfaceSelectOption
                {
                    Name = optionName
                };
                SetDefaultOptionValue(maaInterface, selectOption);
                return selectOption;
            }).ToList();
    }

    /// <summary>
    /// 克隆子选项列表
    /// </summary>
    private static List<MaaInterface.MaaInterfaceSelectOption> CloneSubOptions(List<MaaInterface.MaaInterfaceSelectOption> subOptions)
    {
        return subOptions.Select(opt => new MaaInterface.MaaInterfaceSelectOption
        {
            Name = opt.Name,
            Index = opt.Index,
            Data = opt.Data != null ? new Dictionary<string, string?>(opt.Data) : null,
            SubOptions = opt.SubOptions != null ? CloneSubOptions(opt.SubOptions) : null
        }).ToList();
    }
    /// <summary>
    /// 获取当前控制器的名称
    /// </summary>
    public string? GetCurrentControllerName()
    {
        return GetControllerName(taskQueueViewModel.CurrentController, maaInterface);
    }

    public static string? GetControllerName(MaaControllerTypes currentControllerType, MaaInterface? maaInterface)
    {
        var controllerTypeKey = currentControllerType.ToJsonKey();

        // 从 interface 的 controller 配置中查找匹配的控制器
        var controller = maaInterface?.Controller?.Find(c =>
            c.Type != null && c.Type.Equals(controllerTypeKey, StringComparison.OrdinalIgnoreCase));

        return controller?.Name;
    }

    /// <summary>
    /// 根据控制器过滤资源
    /// </summary>
    /// <param name="resources">所有资源列表</param>
    /// <param name="controllerName">当前控制器名称</param>
    /// <returns>过滤后的资源列表</returns>
    public static List<MaaInterface.MaaInterfaceResource> FilterResourcesByController(
        List<MaaInterface.MaaInterfaceResource> resources,
        string? controllerName)
    {
        return resources.Where(r =>
        {
            // 如果资源没有指定 controller，则支持所有控制器
            if (r.Controller == null || r.Controller.Count == 0)
                return true;

            // 如果当前控制器名称为空，则显示所有资源
            if (string.IsNullOrWhiteSpace(controllerName))
                return true;

            // 检查资源是否支持当前控制器
            return r.Controller.Any(c =>
                c.Equals(controllerName, StringComparison.OrdinalIgnoreCase));
        }).ToList();
    }

    private (List<DragItemViewModel> updateList, List<DragItemViewModel> removeList) SynchronizeTaskItems(
        ref List<string> currentTasks,
        IList<DragItemViewModel> drags,
        List<MaaInterface.MaaInterfaceTask> tasks)
    {
        // 使用 HashSet 去重，解决 currentTasks 可能存在重复项的问题
        var currentTaskSet = new HashSet<string>(currentTasks);

        var removeList = new List<DragItemViewModel>();
        var updateList = new List<DragItemViewModel>();

        var taskDict = tasks
            .GroupBy(t => (t.Name, t.Entry))
            .ToDictionary(group => group.Key, group => group.Last());

        var taskByEntry = tasks
            .Where(t => !string.IsNullOrWhiteSpace(t.Entry))
            .GroupBy(t => t.Entry!)
            .ToDictionary(group => group.Key, group => group.Last());

        foreach (var oldItem in drags)
        {
            var key = (oldItem.InterfaceItem?.Name, oldItem.InterfaceItem?.Entry);

            if (taskDict.TryGetValue(key, out var exact))
            {
                UpdateExistingItem(oldItem, exact);
                updateList.Add(oldItem);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(oldItem.InterfaceItem?.Entry)
                && taskByEntry.TryGetValue(oldItem.InterfaceItem.Entry!, out var byEntry))
            {
                UpdateExistingItem(oldItem, byEntry, true);
                updateList.Add(oldItem);
                continue;
            }

            // 特殊任务（如倒计时、系统通知等）不在 interface 定义中，但需要保留
            if (!string.IsNullOrWhiteSpace(oldItem.InterfaceItem?.Entry)
                && AddTaskDialogViewModel.SpecialActionNames.Contains(oldItem.InterfaceItem.Entry!))
            {
                // 强制更新 Description 为当前的 i18n key，兼容旧版保存的文本格式
                oldItem.InterfaceItem.Description = AddTaskDialogViewModel.GetSpecialTaskDescription(oldItem.InterfaceItem.Entry!);
                updateList.Add(oldItem);
                continue;
            }

            removeList.Add(oldItem);
        }

        var existingKeys = new HashSet<string>(
            updateList.Select(item => $"{item.InterfaceItem?.Name}{NEW_SEPARATOR}{item.InterfaceItem?.Entry}"));

        // 计算用户手动删除的任务集合：
        // 在 currentTaskSet 中存在（用户见过）但不在 existingKeys 中（不在当前任务列表）的任务
        var deletedTaskKeys = new HashSet<string>(
            currentTaskSet.Where(key => !existingKeys.Contains(key)));

        // 用新的 HashSet 重建 currentTasks，确保无重复
        var newCurrentTasks = new HashSet<string>(existingKeys);
        // 保留已删除任务的记录，防止重启后被重新添加
        newCurrentTasks.UnionWith(deletedTaskKeys);

        foreach (var task in tasks)
        {
            var historyKey = $"{task.Name}{NEW_SEPARATOR}{task.Entry}";
            
            if (existingKeys.Contains(historyKey))
            {
                newCurrentTasks.Add(historyKey);
                continue;
            }
            
            // 用户之前见过并手动删除的任务，不再自动添回
            if (deletedTaskKeys.Contains(historyKey))
            {
                continue;
            }
    
            // 真正的新任务：不在 existingKeys 中，也不在 deletedTaskKeys 中
            var clonedTask = task.Clone();
            var newItem = new DragItemViewModel(clonedTask) { OwnerViewModel = taskQueueViewModel };
            if (clonedTask.Option != null)
            {
                clonedTask.Option.ForEach(option => SetDefaultOptionValue(maaInterface, option));
            }
            updateList.Add(newItem);
            existingKeys.Add(historyKey);
            newCurrentTasks.Add(historyKey);
        }

        currentTasks = newCurrentTasks.ToList();
        return (updateList, removeList);
    }


    private void UpdateExistingItem(DragItemViewModel oldItem, MaaInterface.MaaInterfaceTask newItem, bool updateName = false)
    {
        if (oldItem.InterfaceItem == null) return;
        if (updateName) oldItem.InterfaceItem.Name = newItem.Name;
        else if (oldItem.InterfaceItem.Name != newItem.Name) return;

        oldItem.InterfaceItem.Entry = newItem.Entry;
        oldItem.InterfaceItem.Label = newItem.Label;
        oldItem.InterfaceItem.PipelineOverride = newItem.PipelineOverride;
        oldItem.InterfaceItem.Description = newItem.Description;
        oldItem.InterfaceItem.Document = newItem.Document;
        oldItem.InterfaceItem.Repeatable = newItem.Repeatable;
        oldItem.InterfaceItem.Resource = newItem.Resource;
        oldItem.InterfaceItem.Controller = newItem.Controller;
        oldItem.InterfaceItem.Icon = newItem.Icon;

        // 更新图标
        oldItem.InterfaceItem.InitializeIcon();
        oldItem.ResolvedIcon = oldItem.InterfaceItem.ResolvedIcon;
        oldItem.HasIcon = oldItem.InterfaceItem.HasIcon;

        // 更新显示名称（保留自定义重命名/备注）
        oldItem.RefreshDisplayName();

        UpdateAdvancedOptions(oldItem, newItem);
        UpdateOptions(oldItem, newItem);

        // 更新 IsVisible 属性，确保设置图标的可见性正确
        oldItem.IsVisible = oldItem.InterfaceItem is { Advanced.Count: > 0 }
            || oldItem.InterfaceItem is { Option.Count: > 0 }
            || oldItem.InterfaceItem.Repeatable == true
            || !string.IsNullOrWhiteSpace(oldItem.InterfaceItem.Description)
            || oldItem.InterfaceItem.Document is { Count: > 0 };
    }


    private void UpdateAdvancedOptions(DragItemViewModel oldItem, MaaInterface.MaaInterfaceTask newItem)
    {
        if (newItem.Advanced != null)
        {
            var tempDict = oldItem.InterfaceItem?.Advanced?.ToDictionary(t => t.Name) ?? new Dictionary<string, MaaInterface.MaaInterfaceSelectAdvanced>();
            var advanceds = JsonConvert.DeserializeObject<List<MaaInterface.MaaInterfaceSelectAdvanced>>(JsonConvert.SerializeObject(newItem.Advanced));
            oldItem.InterfaceItem!.Advanced = advanceds?.Select(opt =>
            {
                if (tempDict.TryGetValue(opt.Name ?? string.Empty, out var existing)) opt.Data = existing.Data;
                return opt;
            }).ToList();
        }
        else oldItem.InterfaceItem!.Advanced = null;
    }

    private void UpdateOptions(DragItemViewModel oldItem, MaaInterface.MaaInterfaceTask newItem)
    {
        if (newItem.Option != null)
        {
            var existingDict = oldItem.InterfaceItem?.Option?.ToDictionary(t => t.Name ?? string.Empty)
                ?? new Dictionary<string, MaaInterface.MaaInterfaceSelectOption>();

            var newOptions = new List<MaaInterface.MaaInterfaceSelectOption>();

            foreach (var newOpt in newItem.Option)
            {
                var optName = newOpt.Name ?? string.Empty;

                if (existingDict.TryGetValue(optName, out var existing))
                {
                    // 保留原有对象，只更新必要的属性（如果interface 定义变了需要调整）
                    // 这样 UI 控件的事件处理器仍然引用同一个对象，用户的修改能正确反映
                    if ((maaInterface?.Option?.TryGetValue(optName, out var io) ?? false) && io.Cases is { Count: > 0 })
                    {
                        // 只有当 Index 超出范围时才调整
                        if (existing.Index.HasValue && existing.Index.Value >= io.Cases.Count)
                        {
                            existing.Index = io.Cases.Count - 1;
                        }
                    }
                    newOptions.Add(existing);
                }
                else
                {
                    // 新增的选项，创建新对象并设置默认值
                    var opt = new MaaInterface.MaaInterfaceSelectOption
                    {
                        Name = newOpt.Name,
                        Index = newOpt.Index,
                        Data = newOpt.Data != null ? new Dictionary<string, string?>(newOpt.Data) : null
                    };
                    SetDefaultOptionValue(maaInterface, opt);
                    newOptions.Add(opt);
                }
            }

            oldItem.InterfaceItem!.Option = newOptions;
        }
        else oldItem.InterfaceItem!.Option = null;
    }

    private List<MaaInterface.MaaInterfaceSelectOption> MergeSubOptions(List<MaaInterface.MaaInterfaceSelectOption> existingSubOptions)
    {
        return existingSubOptions.Select(subOpt =>
        {
            var newSubOpt = new MaaInterface.MaaInterfaceSelectOption
            {
                Name = subOpt.Name,
                Index = subOpt.Index,
                Data = subOpt.Data?.Count > 0 ? new Dictionary<string, string?>(subOpt.Data) : null
            };
            if ((maaInterface?.Option?.TryGetValue(subOpt.Name ?? string.Empty, out var sio) ?? false) && sio.Cases is { Count: > 0 })
                newSubOpt.Index = Math.Min(subOpt.Index ?? 0, sio.Cases.Count - 1);
            if (subOpt.SubOptions?.Count > 0) newSubOpt.SubOptions = MergeSubOptions(subOpt.SubOptions);
            return newSubOpt;
        }).ToList();
    }

    public static void SetDefaultOptionValue(MaaInterface? @interface, MaaInterface.MaaInterfaceSelectOption option)
    {
        if (!(@interface?.Option?.TryGetValue(option.Name ?? string.Empty, out var io) ?? false)) return;
        var defaultIndex = io.Cases?.FindIndex(c => c.Name == io.DefaultCase) ?? -1;
        if (defaultIndex != -1) option.Index = defaultIndex;
        if (io.IsInput && io.Inputs != null)
        {
            option.Data ??= new Dictionary<string, string?>();
            foreach (var input in io.Inputs)
                if (!string.IsNullOrEmpty(input.Name) && !option.Data.ContainsKey(input.Name))
                    option.Data[input.Name] = input.Default ?? string.Empty;
        }

    }

    private void UpdateViewModels(IList<DragItemViewModel> drags, List<MaaInterface.MaaInterfaceTask> tasks, ObservableCollection<DragItemViewModel> tasksSource)
    {
        // 克隆任务对象，避免多实例共享同一个 MaaInterfaceTask 对象
        var newItems = tasks.Select(t => new DragItemViewModel(t.Clone()) { OwnerViewModel = taskQueueViewModel }).ToList();
        foreach (var item in newItems)
        {
            if (item.InterfaceItem?.Option != null && !drags.Any())
                item.InterfaceItem.Option.ForEach(option => SetDefaultOptionValue(maaInterface, option));
        }

        // 检查当前资源是否有全局选项配置
        var currentResourceName = taskQueueViewModel.CurrentResource;
        var currentResource = taskQueueViewModel.CurrentResources
            .FirstOrDefault(r => r.Name == currentResourceName);

        // 创建最终的任务列表
        var finalItems = new List<DragItemViewModel>();
        
        // 如果当前资源有 option 配置，在最前面添加资源设置项
        if (currentResource?.Option is {Count: > 0})
        {
            var resourceOptionItem = CreateResourceOptionItem(currentResource, drags);
            if (resourceOptionItem != null)
            {
                finalItems.Add(resourceOptionItem);
            }
        }

        // 添加普通任务项
        if (drags.Any())
        {
            // 过滤掉已存在的资源设置项，避免重复
            finalItems.AddRange(drags.Where(d => !d.IsResourceOptionItem));
        }
        else
        {
            finalItems.AddRange(newItems);
        }

        // UI 线程更新集合，确保 TaskList 刷新
        DispatcherHelper.RunOnMainThread(() =>
        {
            tasksSource.Clear();
            foreach (var item in newItems) tasksSource.Add(item);

            taskQueueViewModel.TaskItemViewModels.Clear();
            foreach (var item in finalItems)
            {
                taskQueueViewModel.TaskItemViewModels.Add(item);
            }

            // 根据当前资源更新任务的可见性
            taskQueueViewModel.UpdateTasksForResource(currentResourceName);
        });
    }

    /// <summary>
    /// 创建资源全局选项的任务项
    /// </summary>
    private DragItemViewModel? CreateResourceOptionItem(MaaInterface.MaaInterfaceResource resource, IList<DragItemViewModel>? existingDrags)
    {
        if (resource.Option == null || resource.Option.Count == 0)
            return null;

        // 从配置中加载已保存的资源选项
        var savedResourceOptions = taskQueueViewModel.Processor.InstanceConfiguration.GetValue(
            ConfigurationKeys.ResourceOptionItems,
            new Dictionary<string, List<MaaInterface.MaaInterfaceSelectOption>>());

        // 检查是否已经存在对应的资源设置项
        var existingResourceItem = existingDrags?.FirstOrDefault(d =>
            d.IsResourceOptionItem && d.ResourceItem?.Name == resource.Name);

        if (existingResourceItem != null)
        {
            // 更新已存在的资源设置项的 SelectOptions
            if (resource.SelectOptions != null && existingResourceItem.ResourceItem != null)
            {
                // 合并已保存的选项值
                MergeResourceSelectOptions(existingResourceItem.ResourceItem, resource);
            }
            return existingResourceItem;
        }

        // 如果配置中有保存的选项值，恢复它们
        if (savedResourceOptions.TryGetValue(resource.Name ?? string.Empty, out var savedOptions) && savedOptions != null)
        {
            // 恢复配置中保存的选项值到 resource.SelectOptions
            if (resource.SelectOptions != null)
            {
                var savedDict = savedOptions.ToDictionary(o => o.Name ?? string.Empty);
                foreach (var opt in resource.SelectOptions)
                {
                    if (savedDict.TryGetValue(opt.Name ?? string.Empty, out var savedOpt))
                    {
                        opt.Index = savedOpt.Index;
                        opt.Data = savedOpt.Data;
                        opt.SubOptions = savedOpt.SubOptions;
                    }
                }
            }
        }

        // 创建新的资源设置项
        var resourceItem = new DragItemViewModel(resource) { OwnerViewModel = taskQueueViewModel };

        // 设置 IsVisible 为 true，因为资源设置项有选项需要显示
        resourceItem.IsVisible = true;

        return resourceItem;
    }

    /// <summary>
    /// 合并资源的 SelectOptions（保留用户已选择的值）
    /// </summary>
    private void MergeResourceSelectOptions(MaaInterface.MaaInterfaceResource existingResource, MaaInterface.MaaInterfaceResource newResource)
    {
        if (newResource.SelectOptions == null)
        {
            existingResource.SelectOptions = null;
            return;
        }

        var existingDict = existingResource.SelectOptions?.ToDictionary(o => o.Name ?? string.Empty)
            ?? new Dictionary<string, MaaInterface.MaaInterfaceSelectOption>();

        existingResource.SelectOptions = newResource.SelectOptions.Select(newOpt =>
        {
            if (existingDict.TryGetValue(newOpt.Name ?? string.Empty, out var existingOpt))
            {
                // 保留用户选择的值
                if (existingOpt.Index.HasValue)
                    newOpt.Index = existingOpt.Index;
                if (existingOpt.Data?.Count > 0)
                    newOpt.Data = existingOpt.Data;
                if (existingOpt.SubOptions?.Count > 0)
                    newOpt.SubOptions = existingOpt.SubOptions;
            }
            return newOpt;
        }).ToList();
    }
}
