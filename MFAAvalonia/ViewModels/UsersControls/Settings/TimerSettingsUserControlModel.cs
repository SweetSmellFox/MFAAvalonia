using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using System;
using System.Linq;

namespace MFAAvalonia.ViewModels.UsersControls.Settings;

public partial class TimerSettingsUserControlModel : ViewModelBase
{
    [ObservableProperty] private bool _customConfig = Convert.ToBoolean(GlobalConfiguration.GetValue(ConfigurationKeys.CustomConfig, bool.FalseString));
    [ObservableProperty] private bool _forceScheduledStart = Convert.ToBoolean(GlobalConfiguration.GetValue(ConfigurationKeys.ForceScheduledStart, bool.FalseString));

    public TimerModel TimerModels { get; set; } = new();

    partial void OnCustomConfigChanged(bool value)
    {
        GlobalConfiguration.SetValue(ConfigurationKeys.CustomConfig, value.ToString());
    }

    partial void OnForceScheduledStartChanged(bool value)
    {
        GlobalConfiguration.SetValue(ConfigurationKeys.ForceScheduledStart, value.ToString());
    }

    public IAvaloniaReadOnlyList<MFAConfiguration> ConfigurationList { get; set; } = ConfigurationManager.Configs;

    public partial class TimerModel
    {
        public partial class TimerProperties : ViewModelBase
        {
            public TimerProperties(int timeId, bool isOn, string time, string? timerConfig, string? scheduleConfig)
            {
                TimerId = timeId;
                _isOn = isOn;
                _time = TimeSpan.Parse(time);
                TimerName = $"{LangKeys.Timer.ToLocalization()} {TimerId + 1}";
                if (timerConfig == null || !ConfigurationManager.Configs.Any(c => c.Name.Equals(timerConfig)))
                {
                    _timerConfig = ConfigurationManager.GetCurrentConfiguration();
                }
                else
                {
                    _timerConfig = timerConfig;
                }
                ScheduleConfig = new TimerScheduleConfig(scheduleConfig ?? string.Empty);
                LanguageHelper.LanguageChanged += OnLanguageChanged;
            }


            public int TimerId { get; set; }

            [ObservableProperty] private string _timerName;

            private void OnLanguageChanged(object sender, EventArgs e)
            {
                TimerName = $"{LangKeys.Timer.ToLocalization()} {TimerId + 1}";
            }

            private bool _isOn;

            /// <summary>
            /// Gets or sets a value indicating whether the timer is set.
            /// </summary>
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

            /// <summary>
            /// Gets or sets the timer.
            /// </summary>
            public TimeSpan Time
            {
                get => _time;
                set
                {
                    SetProperty(ref _time, value);
                    GlobalConfiguration.SetTimerTime(TimerId, value.ToString(@"h\:mm"));
                }
            }

            private string? _timerConfig;

            /// <summary>
            /// Gets or sets the config of the timer.
            /// </summary>
            public string? TimerConfig
            {
                get => _timerConfig;
                set
                {
                    SetProperty(ref _timerConfig, value ?? ConfigurationManager.GetCurrentConfiguration());
                    GlobalConfiguration.SetTimerConfig(TimerId, _timerConfig);
                }
            }

            private TimerScheduleConfig _scheduleConfig;

            /// <summary>
            /// Gets or sets the schedule config of the timer.
            /// </summary>
            public TimerScheduleConfig ScheduleConfig
            {
                get => _scheduleConfig;
                set
                {
                    // 不使用 SetProperty 的返回值判断，因为 SchedulePicker 会修改同一个对象的内部属性
                    // 然后重新设置相同的对象引用，此时需要确保配置被保存
                    SetNewProperty(ref _scheduleConfig, value);
                    GlobalConfiguration.SetTimerSchedule(TimerId, _scheduleConfig?.Serialize() ?? string.Empty);
                    OnPropertyChanged(nameof(ScheduleDisplayText));
                }
            }

            /// <summary>
            /// Gets the display text for the schedule.
            /// </summary>
            public string ScheduleDisplayText => _scheduleConfig.GetDisplayText();

            /// <summary>
            /// Updates the schedule config and saves it.
            /// </summary>
            public void UpdateScheduleConfig()
            {
                GlobalConfiguration.SetTimerSchedule(TimerId, _scheduleConfig.Serialize());
                OnPropertyChanged(nameof(ScheduleDisplayText));
            }
        }


        public TimerProperties[] Timers { get; set; } = new TimerProperties[8];
        private readonly DispatcherTimer _dispatcherTimer;
        public TimerModel()
        {
            for (var i = 0; i < 8; i++)
            {
                Timers[i] = new TimerProperties(
                    i,
                    GlobalConfiguration.GetTimer(i, bool.FalseString) == bool.TrueString,
                    GlobalConfiguration.GetTimerTime(i, $"{i * 3}:0"),
                    GlobalConfiguration.GetTimerConfig(i, ConfigurationManager.GetCurrentConfiguration()),
                    GlobalConfiguration.GetTimerSchedule(i, string.Empty));
            }
            _dispatcherTimer = new();
            _dispatcherTimer.Interval = TimeSpan.FromMinutes(1);
            _dispatcherTimer.Tick += CheckTimerElapsed;
            _dispatcherTimer.Start();
        }

        private void CheckTimerElapsed(object? sender, EventArgs e)
        {
            var currentTime = DateTime.Now;
            foreach (var timer in Timers)
            {
                //检查时间是否匹配，并且检查触发模式是否满足
                if (timer.IsOn
                    && timer.Time.Hours == currentTime.Hour
                    && timer.Time.Minutes == currentTime.Minute
                    && timer.ScheduleConfig.ShouldTrigger(currentTime))
                {
                    ExecuteTimerTask(timer.TimerId);
                }
                if (timer.IsOn
                    && timer.Time.Hours == currentTime.Hour
                    && timer.Time.Minutes == currentTime.Minute + 2
                    && timer.ScheduleConfig.ShouldTrigger(currentTime))
                {
                    SwitchConfiguration(timer.TimerId);
                }
            }
        }

        private void SwitchConfiguration(int timerId)
        {
            var timer = Timers.FirstOrDefault(t => t.TimerId == timerId, null);
            if (timer != null)
            {
                var config = timer.TimerConfig ?? ConfigurationManager.GetCurrentConfiguration();
                if (config != ConfigurationManager.GetCurrentConfiguration())
                {
                    ConfigurationManager.SwitchConfiguration(config);
                }
            }
        }

        private void ExecuteTimerTask(int timerId)
        {
            var timer = Timers.FirstOrDefault(t => t.TimerId == timerId, null);
            if (timer != null)
            {
                var vm = Instances.InstanceTabBarViewModel.ActiveTab?.TaskQueueViewModel;
                if (vm != null)
                {
                    if (Instances.TimerSettingsUserControlModel.ForceScheduledStart && Instances.RootViewModel.IsRunning)
                        vm.StopTask(vm.StartTask);
                    else
                        vm.StartTask();
                }
            }
        }
    }
}

