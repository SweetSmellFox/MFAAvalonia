using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.ViewModels.Pages;
using System;
using System.Collections.ObjectModel;
using System.Linq;

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

    public TimerProperties[] Timers { get; set; } = new TimerProperties[8];

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

        for (var i = 0; i < 8; i++)
        {
            Timers[i] = new TimerProperties(i, this);
        }

        _dispatcherTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _dispatcherTimer.Tick += CheckTimerElapsed;
        _dispatcherTimer.Start();
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
            if (!timer.IsOn
                || timer.Time.Hours != currentTime.Hour
                || !timer.ScheduleConfig.ShouldTrigger(currentTime))
                continue;

            if (timer.Time.Minutes == currentTime.Minute)
            {
                ExecuteTimerTask(timer);
            }
            else if (timer.Time.Minutes == currentTime.Minute + 2)
            {
                SwitchInstance(timer);
            }
        }
    }

    /// <summary>
    /// 切换到定时器指定的多开实例（原"切换配置"）
    /// </summary>
    private void SwitchInstance(TimerProperties timer)
    {
        var targetInstanceId = timer.TimerConfig;
        if (string.IsNullOrEmpty(targetInstanceId))
            return;

        var manager = MaaProcessorManager.Instance;
        if (manager.Current.InstanceId == targetInstanceId)
            return;

        // 确保目标实例已加载（懒加载按需触发）
        manager.EnsureInstanceLoaded(targetInstanceId);

        if (manager.TryGetInstance(targetInstanceId, out _))
        {
            DispatcherHelper.RunOnMainThread(() =>
            {
                Instances.InstanceTabBarViewModel.SwitchToInstanceById(targetInstanceId);
            });
        }
    }

    /// <summary>
    /// 执行定时任务：在指定的多开实例上启动任务
    /// </summary>
    private void ExecuteTimerTask(TimerProperties timer)
    {
        var targetInstanceId = timer.TimerConfig;
        var manager = MaaProcessorManager.Instance;

        // 如果没有指定实例，使用当前活跃实例
        TaskQueueViewModel? vm = null;
        if (!string.IsNullOrEmpty(targetInstanceId))
        {
            // 确保目标实例已加载（懒加载按需触发）
            manager.EnsureInstanceLoaded(targetInstanceId);
            vm = manager.GetViewModel(targetInstanceId);
        }
        else
        {
            vm = manager.GetViewModel(manager.Current.InstanceId);
        }

        if (vm == null) return;

        DispatcherHelper.RunOnMainThread(() =>
        {
            if (ForceScheduledStart && Instances.RootViewModel.IsRunning)
                vm.StopTask(vm.StartTask);
            else
                vm.StartTask();
        });
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
            _time = TimeSpan.Parse(GlobalConfiguration.GetTimerTime(timeId, $"{timeId * 3}:0"));

            var timerConfig = GlobalConfiguration.GetTimerConfig(timeId, string.Empty);
            _timerConfig = timerConfig;

            ScheduleConfig = new TimerScheduleConfig(GlobalConfiguration.GetTimerSchedule(timeId, string.Empty));

            TimerName = $"{LangKeys.Timer.ToLocalization()} {TimerId + 1}";
            LanguageHelper.LanguageChanged += OnLanguageChanged;
        }

        public int TimerId { get; set; }

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

        public string ScheduleDisplayText => _scheduleConfig.GetDisplayText();

        public void UpdateScheduleConfig()
        {
            GlobalConfiguration.SetTimerSchedule(TimerId, _scheduleConfig.Serialize());
            OnPropertyChanged(nameof(ScheduleDisplayText));
        }
    }
}
