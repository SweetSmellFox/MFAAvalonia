using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SukiUI.Dialogs;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MFAAvalonia.ViewModels.UsersControls.Settings;

public partial class AddTaskDialogViewModel : ViewModelBase
{
    /// <summary>
    /// 已知的特殊任务 Action 名称集合，用于判断一个任务是否为特殊任务
    /// </summary>
    public static readonly HashSet<string> SpecialActionNames = new()
    {
        "CountdownAction",
        "TimedWaitAction",
        "SystemNotificationAction",
        "CustomProgramAction",
        "KillProcessAction",
        "ComputerOperationAction",
        "WebhookAction",
    };

    private ObservableCollection<AddTaskItemViewModel> _items;

    public ObservableCollection<AddTaskItemViewModel> Items
    {
        get => _items;
        set => SetProperty(ref _items, value);
    }

    public List<AddTaskItemViewModel> Sources { get; set; }

    public ObservableCollection<AddTaskItemViewModel> SpecialTasks { get; set; }

    public ISukiDialog Dialog { get; set; }

    public AddTaskDialogViewModel(ISukiDialog dialog, ICollection<DragItemViewModel> sources)
    {
        Dialog = dialog;
        Sources = sources.Select(s => new AddTaskItemViewModel(s)).ToList();
        _items = new ObservableCollection<AddTaskItemViewModel>(Sources);

        SpecialTasks = new ObservableCollection<AddTaskItemViewModel>
        {
            new() { Name = LangKeys.SpecialTask_Countdown.ToLocalization(), IsSpecialTask = true, SpecialActionName = "CountdownAction", SpecialIcon = "⏳" },
            new() { Name = LangKeys.SpecialTask_TimedWait.ToLocalization(), IsSpecialTask = true, SpecialActionName = "TimedWaitAction", SpecialIcon = "⏰" },
            new() { Name = LangKeys.SpecialTask_Toast.ToLocalization(), IsSpecialTask = true, SpecialActionName = "SystemNotificationAction", SpecialIcon = "💬" },
            new() { Name = LangKeys.SpecialTask_CustomProgram.ToLocalization(), IsSpecialTask = true, SpecialActionName = "CustomProgramAction", SpecialIcon = "▶️" },
            new() { Name = LangKeys.SpecialTask_KillProcess.ToLocalization(), IsSpecialTask = true, SpecialActionName = "KillProcessAction", SpecialIcon = "⛔" },
            new() { Name = LangKeys.SpecialTask_ComputerOperation.ToLocalization(), IsSpecialTask = true, SpecialActionName = "ComputerOperationAction", SpecialIcon = "⚡" },
            new() { Name = LangKeys.SpecialTask_Webhook.ToLocalization(), IsSpecialTask = true, SpecialActionName = "WebhookAction", SpecialIcon = "🔔" },
        };
    }

    [RelayCommand]
    void Add()
    {
        var vm = Instances.InstanceTabBarViewModel.ActiveTab.TaskQueueViewModel;

        // 添加普通任务
        foreach (var item in Sources)
        {
            if (item.AddCount <= 0 || item.Source == null) continue;
            for (int i = 0; i < item.AddCount; i++)
            {
                var output = item.Source.Clone();
                if (output.InterfaceItem.Option != null)
                    output.InterfaceItem.Option.ForEach(option =>
                        TaskLoader.SetDefaultOptionValue(MaaProcessor.Interface, option));
                output.OwnerViewModel = vm;
                vm.TaskItemViewModels.Add(output);
            }
        }

        // 添加特殊任务
        foreach (var special in SpecialTasks)
        {
            if (special.AddCount <= 0) continue;
            for (int i = 0; i < special.AddCount; i++)
            {
                var task = CreateSpecialTask(special);
                if (task != null)
                {
                    task.OwnerViewModel = vm;
                    vm.TaskItemViewModels.Add(task);
                }
            }
        }

        ConfigurationManager.CurrentInstance.SetValue(ConfigurationKeys.TaskItems,
            vm.TaskItemViewModels.ToList().Select(model => model.InterfaceItem));

        var totalAdded = Sources.Sum(s => s.AddCount) + SpecialTasks.Sum(s => s.AddCount);
        if (totalAdded > 0)
            ToastHelper.Info(LangKeys.Tip.ToLocalization(),
                LangKeys.TaskAddedToast.ToLocalizationFormatted(false, totalAdded.ToString()));

        Dialog.Dismiss();
    }

    /// <summary>
    /// 获取特殊任务的默认 custom_action_param JSON
    /// </summary>
    public static JObject GetDefaultActionParam(string actionName)
    {
        return actionName switch
        {
            "CountdownAction" => new JObject { ["seconds"] = 60 },
            "TimedWaitAction" => new JObject { ["hour"] = 0, ["minute"] = 0 },
            "SystemNotificationAction" => new JObject { ["title"] = "MFAAvalonia", ["message"] = "" },
            "CustomProgramAction" => new JObject { ["program"] = "", ["arguments"] = "", ["wait_for_exit"] = false },
            "KillProcessAction" => new JObject { ["process_name"] = "" },
            "ComputerOperationAction" => new JObject { ["operation"] = "shutdown" },
            "WebhookAction" => new JObject { ["url"] = "", ["method"] = "GET", ["body"] = "", ["content_type"] = "application/json" },
            _ => new JObject()
        };
    }

    private static DragItemViewModel? CreateSpecialTask(AddTaskItemViewModel special)
    {
        var actionName = special.SpecialActionName;
        var defaultParam = GetDefaultActionParam(actionName);

        var pipelineOverride = new Dictionary<string, JToken>
        {
            [actionName] = new JObject
            {
                ["action"] = "Custom",
                ["custom_action"] = actionName,
                ["custom_action_param"] = defaultParam
            }
        };

        var interfaceTask = new MaaInterface.MaaInterfaceTask
        {
            Name = special.Name,
            Entry = actionName,
            PipelineOverride = pipelineOverride,
            Check = true,
            // 设置 Description 使 DragItemViewModel.IsVisible = true，从而显示设置按钮
            Description = GetSpecialTaskDescription(actionName),
        };

        return new DragItemViewModel(interfaceTask)
        {
            IsChecked = true,
        };
    }

    /// <summary>
    /// 获取特殊任务的详细描述（i18n key，markdown格式）
    /// </summary>
    public static string GetSpecialTaskDescription(string actionName)
    {
        return actionName switch
        {
            "CountdownAction" => LangKeys.SpecialTask_CountdownDesc,
            "TimedWaitAction" => LangKeys.SpecialTask_TimedWaitDesc,
            "SystemNotificationAction" => LangKeys.SpecialTask_ToastDesc,
            "CustomProgramAction" => LangKeys.SpecialTask_CustomProgramDesc,
            "KillProcessAction" => LangKeys.SpecialTask_KillProcessDesc,
            "ComputerOperationAction" => LangKeys.SpecialTask_ComputerOperationDesc,
            "WebhookAction" => LangKeys.SpecialTask_WebhookDesc,
            _ => LangKeys.SpecialTask
        };
    }

    [RelayCommand]
    void Cancel()
    {
        Dialog.Dismiss();
    }
}
