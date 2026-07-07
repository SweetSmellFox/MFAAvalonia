using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using System.Collections.Generic;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.ViewModels.Pages;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace MFAAvalonia.ViewModels.Other;

/// <summary>
/// 全局定时器模型，所有多开实例共享同一个定时器。
/// 切换配置实际效果为切换多开实例，选用配置实际效果为选用多开实例。
/// </summary>
public partial class TimerModel : ViewModelBase
{
    private static readonly Lazy<TimerModel> LazyInstance = new(() => new TimerModel());
    public static TimerModel Instance => LazyInstance.Value;

    private readonly DispatcherTimer _dispatcherTimer;

    public ObservableCollection<TimerProperties> Timers { get; } = new();

    [ObservableProperty] private bool _customConfig;
    [ObservableProperty] private bool _forceScheduledStart;

    /// <summary>
    /// 实例列表，供 UI ComboBox 绑定（UI 上仍显示为"配置"）
    /// </summary>
    public ObservableCollection<InstanceEntry> InstanceList { get; } = new();

    partial void OnCustomConfigChanged(bool value)
    {
        GlobalConfiguration.SetValue(ConfigurationKeys.CustomConfig, value.ToString());
    }

    partial void OnForceScheduledStartChanged(bool value)
    {
        GlobalConfiguration.SetValue(ConfigurationKeys.ForceScheduledStart, value.ToString());
    }

    private TimerModel()
    {
        CustomConfig = GlobalConfiguration.GetValue(ConfigurationKeys.CustomConfig, bool.FalseString) == bool.TrueString;
        ForceScheduledStart = GlobalConfiguration.GetValue(ConfigurationKeys.ForceScheduledStart, bool.FalseString) == bool.TrueString;

        var count = GlobalConfiguration.GetTimerCount(8);
        for (var i = 0; i < count; i++)
        {
            Timers.Add(new TimerProperties(i, this));
        }

        _dispatcherTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _dispatcherTimer.Tick += CheckTimerElapsed;
        _dispatcherTimer.Start();
    }

    [RelayCommand]
    public void AddTimer()
    {
        var newId = Timers.Count;
        var timer = new TimerProperties(newId, this);
        if (string.IsNullOrEmpty(timer.TimerConfig) && InstanceList.Count > 0)
        {
            timer.TimerConfig = MaaProcessorManager.Instance.Current.InstanceId;
        }
        Timers.Add(timer);
        GlobalConfiguration.SetTimerCount(Timers.Count);
    }

    public void RemoveTimer(TimerProperties timer)
    {
        if (Timers.Count <= 1) return;
        var index = Timers.IndexOf(timer);
        if (index < 0) return;

        Timers.RemoveAt(index);
        GlobalConfiguration.RemoveTimerConfig(Timers.Count); // 清除末尾配置

        // 重新编号并保存后续定时器的配置
        for (var i = index; i < Timers.Count; i++)
        {
            var t = Timers[i];
            t.TimerId = i;
            t.SaveAll();
        }

        GlobalConfiguration.SetTimerCount(Timers.Count);
    }

    /// <summary>
    /// 刷新实例列表（当多开实例增删时调用）
    /// 包含所有实例（含尚未懒加载的），供定时器 ComboBox 使用
    /// </summary>
    public void RefreshInstanceList()
    {
        var manager = MaaProcessorManager.Instance;
        var allInstances = manager.GetAllInstanceIdsAndNames();

        var currentIds = InstanceList.Select(e => e.InstanceId).ToHashSet();
        var newIds = allInstances.Select(x => x.Id).ToHashSet();

        // 移除已不存在的
        var toRemove = InstanceList.Where(e => !newIds.Contains(e.InstanceId)).ToList();
        foreach (var item in toRemove)
            InstanceList.Remove(item);

        // 添加新的
        foreach (var (id, name) in allInstances)
        {
            if (!currentIds.Contains(id))
            {
                InstanceList.Add(new InstanceEntry(id, name));
            }
        }

        // 更新名称
        foreach (var entry in InstanceList)
        {
            var match = allInstances.FirstOrDefault(x => x.Id == entry.InstanceId);
            if (match.Id != null)
                entry.Name = match.Name;
        }

        // 为未选择实例的定时器设置默认值（当前激活实例）
        if (InstanceList.Count > 0)
        {
            var currentInstanceId = manager.Current.InstanceId;
            foreach (var timer in Timers)
            {
                if (string.IsNullOrEmpty(timer.TimerConfig)
                    || InstanceList.All(e => e.InstanceId != timer.TimerConfig))
                {
                    timer.TimerConfig = currentInstanceId;
                }
            }
        }
    }

    /// <summary>
    /// 检查当天是否有定时任务需要触发
    /// </summary>
    public bool HasScheduledTasksToday()
    {
        var now = DateTime.Now;
        return Timers.Any(t => t.IsOn && t.ScheduleConfig.ShouldTrigger(now));
    }

    private void CheckTimerElapsed(object? sender, EventArgs e)
    {
        var currentTime = DateTime.Now;
        foreach (var timer in Timers)
        {
            if (!timer.IsOn || !timer.ScheduleConfig.ShouldTrigger(currentTime))
                continue;

            var scheduledTime = currentTime.Date.Add(timer.Time);

            // 到达定时时间：执行任务
            if (currentTime.Hour == scheduledTime.Hour && currentTime.Minute == scheduledTime.Minute)
            {
                // 防止同一分钟内重复触发
                if (timer.LastTriggered.HasValue
                    && timer.LastTriggered.Value.Date == currentTime.Date
                    && timer.LastTriggered.Value.Hour == currentTime.Hour
                    && timer.LastTriggered.Value.Minute == currentTime.Minute)
                    continue;

                timer.LastTriggered = currentTime;
                ExecuteTimerTask(timer);
            }
        }
    }

    /// <summary>
    /// 执行定时任务：
    /// - 如果启用了全局启动设置，先启动所有模拟器，再依次执行所有实例任务
    /// - 否则按原有逻辑执行单个实例
    /// </summary>
    private async void ExecuteTimerTask(TimerProperties timer)
    {
        var manager = MaaProcessorManager.Instance;

        // 检查全局启动设置
        var globalStartEnabled = GlobalConfiguration.GetValue(ConfigurationKeys.GlobalStartEnabled, bool.FalseString) == bool.TrueString;
        if (globalStartEnabled)
        {
            await StartAllEmulatorsAndRunTasks();
            return;
        }

        // 原有逻辑
        if (CustomConfig && !string.IsNullOrEmpty(timer.TimerConfig))
        {
            var targetInstanceId = timer.TimerConfig;
            manager.EnsureInstanceLoaded(targetInstanceId);
            var vm = manager.GetViewModel(targetInstanceId);
            if (vm == null) return;

            await DispatcherHelper.RunOnMainThreadAsync(() =>
            {
                Instances.InstanceTabBarViewModel.SwitchToInstanceById(targetInstanceId);
            });

            await Task.Delay(5000);

            DispatcherHelper.RunOnMainThread(() => ExecuteAction(timer, vm));
        }
        else
        {
            var vm = manager.GetViewModel(manager.Current.InstanceId);
            if (vm == null) return;

            DispatcherHelper.RunOnMainThread(() => ExecuteAction(timer, vm));
        }
    }

    /// <summary>
    /// 启动所有全局配置的模拟器，然后依次启动所有实例任务
    /// </summary>
    private static async Task StartAllEmulatorsAndRunTasks()
    {
        try
        {
            var waitTimeStr = GlobalConfiguration.GetValue(ConfigurationKeys.GlobalWaitSoftwareTime, "60");
            var waitTime = double.TryParse(waitTimeStr, out var parsed) ? parsed : 60.0;

            // 读取所有模拟器条目（不依赖count，遍历所有存在的条目）
            var emulators = new List<(string name, string path, string args)>();
            for (int i = 0; i < 20; i++)
            {
                var path = GlobalConfiguration.GetValue($"GlobalEmulator_{i}_Path", string.Empty);
                if (string.IsNullOrWhiteSpace(path)) continue;
                var args = GlobalConfiguration.GetValue($"GlobalEmulator_{i}_Args", string.Empty);
                var name = GlobalConfiguration.GetValue($"GlobalEmulator_{i}_Name", $"模拟器 {i + 1}");
                emulators.Add((name, path, args));
            }

            if (emulators.Count == 0)
            {
                LoggerHelper.Warning("全局启动：没有配置模拟器");
                return;
            }

            LoggerHelper.Info($"全局启动：准备启动 {emulators.Count} 个模拟器");

            // 并行启动所有模拟器
            foreach (var emu in emulators)
            {
                if (!System.IO.File.Exists(emu.path))
                {
                    LoggerHelper.Warning($"全局启动：{emu.name} 路径不存在: {emu.path}");
                    continue;
                }

                LoggerHelper.Info($"全局启动：启动 {emu.name} - {emu.path} {emu.args}");
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = emu.path,
                    UseShellExecute = true,
                    CreateNoWindow = false
                };
                if (!string.IsNullOrWhiteSpace(emu.args))
                    startInfo.Arguments = emu.args;
                System.Diagnostics.Process.Start(startInfo);
            }

            // 等待所有模拟器启动
            LoggerHelper.Info($"全局启动：等待模拟器启动 {waitTime} 秒...");
            await Task.Delay(TimeSpan.FromSeconds(waitTime));

            // 依次启动所有实例任务
            var manager = MaaProcessorManager.Instance;
            var allInstances = manager.GetAllInstanceIdsAndNames().ToList();
            LoggerHelper.Info($"全局启动：开始执行 {allInstances.Count} 个实例任务");

            for (int i = 0; i < allInstances.Count; i++)
            {
                var instanceId = allInstances[i].Id;
                manager.EnsureInstanceLoaded(instanceId);
                var vm = manager.GetViewModel(instanceId);
                if (vm != null && !vm.IsRunning)
                {
                    var idx = i;
                    await DispatcherHelper.RunOnMainThreadAsync(() =>
                    {
                        Instances.InstanceTabBarViewModel.SwitchToInstanceById(allInstances[idx].Id);
                        vm.StartTask();
                    });
                    await Task.Delay(3000);
                }
            }

            LoggerHelper.Info("全局启动：所有实例已启动");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"全局启动失败: {ex.Message}", ex);
        }
    }

    private void ExecuteAction(TimerProperties timer, TaskQueueViewModel vm)
    {
        if (timer.TimerAction == TimerActionType.StopTask)
        {
            if (!vm.IsRunning) return;

            if (timer.StopConnectedProcess && timer.StopMFA)
            {
                // 先关闭连接的进程，再关闭 MFA
                vm.StopTask(() => MaaProcessor.CloseSoftware(vm.Processor, Instances.ShutdownApplication));
            }
            else if (timer.StopConnectedProcess)
            {
                vm.StopTask(() => MaaProcessor.CloseSoftware(vm.Processor));
            }
            else if (timer.StopMFA)
            {
                vm.StopTask(Instances.ShutdownApplication);
            }
            else
            {
                vm.StopTask();
            }
        }
        else
        {
            if (ForceScheduledStart && vm.IsRunning)
                vm.StopTask(vm.StartTask);
            else
                vm.StartTask();
        }
    }

    /// <summary>
    /// 实例条目，用于 UI ComboBox 绑定
    /// </summary>
    public partial class InstanceEntry : ObservableObject
    {
        public string InstanceId { get; }

        [ObservableProperty] private string _name;

        public InstanceEntry(string instanceId, string name)
        {
            InstanceId = instanceId;
            _name = name;
        }
    }

    public partial class TimerProperties : ViewModelBase
    {
        private readonly TimerModel _parent;

        public TimerProperties(int timeId, TimerModel parent)
        {
            TimerId = timeId;
            _parent = parent;

            _isOn = GlobalConfiguration.GetTimer(timeId, bool.FalseString) == bool.TrueString;
            var timeStr = GlobalConfiguration.GetTimerTime(timeId, $"{(timeId * 3) % 24}:0");
            _time = TimeSpan.TryParse(timeStr, out var parsed) ? parsed : TimeSpan.Zero;
            _timerConfig = GlobalConfiguration.GetTimerConfig(timeId, string.Empty);
            var actionStr = GlobalConfiguration.GetTimerAction(timeId, "0");
            _timerAction = int.TryParse(actionStr, out var actionVal) ? (TimerActionType)actionVal : TimerActionType.StartTask;
            _stopConnectedProcess = GlobalConfiguration.GetTimerStopConnectedProcess(timeId, bool.FalseString) == bool.TrueString;
            _stopMFA = GlobalConfiguration.GetTimerStopMFA(timeId, bool.FalseString) == bool.TrueString;

            ScheduleConfig = new TimerScheduleConfig(GlobalConfiguration.GetTimerSchedule(timeId, string.Empty));

            TimerName = $"{LangKeys.Timer.ToLocalization()} {TimerId + 1}";
            LanguageHelper.LanguageChanged += OnLanguageChanged;
        }

        public int TimerId { get; set; }

        /// <summary>
        /// 上次触发时间，防止同一分钟内重复执行
        /// </summary>
        public DateTime? LastTriggered { get; set; }

        [ObservableProperty] private string _timerName;

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            TimerName = $"{LangKeys.Timer.ToLocalization()} {TimerId + 1}";
        }

        private bool _isOn;
        public bool IsOn
        {
            get => _isOn;
            set
            {
                SetProperty(ref _isOn, value);
                GlobalConfiguration.SetTimer(TimerId, value.ToString());
            }
        }

        private TimeSpan _time;
        public TimeSpan Time
        {
            get => _time;
            set
            {
                SetProperty(ref _time, value);
                GlobalConfiguration.SetTimerTime(TimerId, value.ToString(@"h\:mm"));
            }
        }

        /// <summary>
        /// 存储的是实例ID（原来存配置名，现在存多开实例ID）
        /// </summary>
        private string? _timerConfig;
        public string? TimerConfig
        {
            get => _timerConfig;
            set
            {
                SetProperty(ref _timerConfig, value ?? string.Empty);
                GlobalConfiguration.SetTimerConfig(TimerId, _timerConfig ?? string.Empty);
            }
        }

        private TimerScheduleConfig _scheduleConfig;
        public TimerScheduleConfig ScheduleConfig
        {
            get => _scheduleConfig;
            set
            {
                SetNewProperty(ref _scheduleConfig, value);
                GlobalConfiguration.SetTimerSchedule(TimerId, _scheduleConfig?.Serialize() ?? string.Empty);
                OnPropertyChanged(nameof(ScheduleDisplayText));
            }
        }

        private TimerActionType _timerAction;
        public TimerActionType TimerAction
        {
            get => _timerAction;
            set
            {
                SetProperty(ref _timerAction, value);
                GlobalConfiguration.SetTimerAction(TimerId, ((int)value).ToString());
            }
        }

        public int TimerActionIndex
        {
            get => (int)_timerAction;
            set => TimerAction = (TimerActionType)value;
        }

        private bool _stopConnectedProcess;
        public bool StopConnectedProcess
        {
            get => _stopConnectedProcess;
            set
            {
                SetProperty(ref _stopConnectedProcess, value);
                GlobalConfiguration.SetTimerStopConnectedProcess(TimerId, value.ToString());
            }
        }

        private bool _stopMFA;
        public bool StopMFA
        {
            get => _stopMFA;
            set
            {
                SetProperty(ref _stopMFA, value);
                GlobalConfiguration.SetTimerStopMFA(TimerId, value.ToString());
            }
        }

        [RelayCommand]
        private void Remove() => _parent.RemoveTimer(this);

        public string ScheduleDisplayText => _scheduleConfig.GetDisplayText();

        public void UpdateScheduleConfig()
        {
            GlobalConfiguration.SetTimerSchedule(TimerId, _scheduleConfig.Serialize());
            OnPropertyChanged(nameof(ScheduleDisplayText));
        }

        /// <summary>
        /// 重新编号后保存所有配置到新ID
        /// </summary>
        public void SaveAll()
        {
            TimerName = $"{LangKeys.Timer.ToLocalization()} {TimerId + 1}";
            GlobalConfiguration.SetTimer(TimerId, _isOn.ToString());
            GlobalConfiguration.SetTimerTime(TimerId, _time.ToString(@"h\:mm"));
            GlobalConfiguration.SetTimerConfig(TimerId, _timerConfig ?? string.Empty);
            GlobalConfiguration.SetTimerAction(TimerId, ((int)_timerAction).ToString());
            GlobalConfiguration.SetTimerSchedule(TimerId, _scheduleConfig?.Serialize() ?? string.Empty);
            GlobalConfiguration.SetTimerStopConnectedProcess(TimerId, _stopConnectedProcess.ToString());
            GlobalConfiguration.SetTimerStopMFA(TimerId, _stopMFA.ToString());
        }
    }
}
