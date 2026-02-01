using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.ViewModels.Pages;
using System;
using System.Linq;

namespace MFAAvalonia.ViewModels.Other;

public partial class TimerModel : ViewModelBase
{
    private readonly TaskQueueViewModel _vm;
    private readonly InstanceConfiguration _config;
    private readonly DispatcherTimer _dispatcherTimer;

    public TimerProperties[] Timers { get; set; } = new TimerProperties[8];

    [ObservableProperty] private bool _customConfig;
    [ObservableProperty] private bool _forceScheduledStart;

    partial void OnCustomConfigChanged(bool value)
    {
        _config.SetValue(ConfigurationKeys.CustomConfig, value.ToString());
    }

    partial void OnForceScheduledStartChanged(bool value)
    {
        _config.SetValue(ConfigurationKeys.ForceScheduledStart, value.ToString());
    }

    public TimerModel(TaskQueueViewModel vm)
    {
        _vm = vm;
        _config = vm.Processor.InstanceConfiguration;

        CustomConfig = Convert.ToBoolean(_config.GetValue(ConfigurationKeys.CustomConfig, bool.FalseString));
        ForceScheduledStart = Convert.ToBoolean(_config.GetValue(ConfigurationKeys.ForceScheduledStart, bool.FalseString));

        for (var i = 0; i < 8; i++)
        {
            Timers[i] = new TimerProperties(i, _config, this);
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
            // Use _vm directly, no need to check ActiveTab
            if (ForceScheduledStart && Instances.RootViewModel.IsRunning)
                _vm.StopTask(_vm.StartTask);
            else
                _vm.StartTask();
        }
    }

    public partial class TimerProperties : ViewModelBase
    {
        private readonly InstanceConfiguration _config;
        private readonly TimerModel _parent;

        public TimerProperties(int timeId, InstanceConfiguration config, TimerModel parent)
        {
            TimerId = timeId;
            _config = config;
            _parent = parent;

            _isOn = _config.GetValue($"Timer.Timer{timeId + 1}", bool.FalseString) == bool.TrueString;
            _time = TimeSpan.Parse(_config.GetValue($"Timer.Timer{timeId + 1}Time", $"{timeId * 3}:0"));
            
            var timerConfig = _config.GetValue($"Timer.Timer{timeId + 1}.Config", ConfigurationManager.GetCurrentConfiguration());
            if (timerConfig == null || !ConfigurationManager.Configs.Any(c => c.Name.Equals(timerConfig)))
            {
                _timerConfig = ConfigurationManager.GetCurrentConfiguration();
            }
            else
            {
                _timerConfig = timerConfig;
            }

            ScheduleConfig = new TimerScheduleConfig(_config.GetValue($"Timer.Timer{timeId + 1}.Schedule", string.Empty));
            
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
                _config.SetValue($"Timer.Timer{TimerId + 1}", value.ToString());
            }
        }

        private TimeSpan _time;
        public TimeSpan Time
        {
            get => _time;
            set
            {
                SetProperty(ref _time, value);
                _config.SetValue($"Timer.Timer{TimerId + 1}Time", value.ToString(@"h\:mm"));
            }
        }

        private string? _timerConfig;
        public string? TimerConfig
        {
            get => _timerConfig;
            set
            {
                SetProperty(ref _timerConfig, value ?? ConfigurationManager.GetCurrentConfiguration());
                _config.SetValue($"Timer.Timer{TimerId + 1}.Config", _timerConfig);
            }
        }

        private TimerScheduleConfig _scheduleConfig;
        public TimerScheduleConfig ScheduleConfig
        {
            get => _scheduleConfig;
            set
            {
                SetNewProperty(ref _scheduleConfig, value);
                _config.SetValue($"Timer.Timer{TimerId + 1}.Schedule", _scheduleConfig?.Serialize() ?? string.Empty);
                OnPropertyChanged(nameof(ScheduleDisplayText));
            }
        }

        public string ScheduleDisplayText => _scheduleConfig.GetDisplayText();

        public void UpdateScheduleConfig()
        {
            _config.SetValue($"Timer.Timer{TimerId + 1}.Schedule", _scheduleConfig.Serialize());
            OnPropertyChanged(nameof(ScheduleDisplayText));
        }
    }
}