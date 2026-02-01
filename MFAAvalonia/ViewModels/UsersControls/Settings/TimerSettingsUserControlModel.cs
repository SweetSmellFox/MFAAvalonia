using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.ViewModels.Other;
using System;
using System.Linq;

namespace MFAAvalonia.ViewModels.UsersControls.Settings;

public partial class TimerSettingsUserControlModel : ViewModelBase
{
    [ObservableProperty] private bool _customConfig = Convert.ToBoolean(ConfigurationManager.CurrentInstance.GetValue(ConfigurationKeys.CustomConfig, bool.FalseString));
    [ObservableProperty] private bool _forceScheduledStart = Convert.ToBoolean(ConfigurationManager.CurrentInstance.GetValue(ConfigurationKeys.ForceScheduledStart, bool.FalseString));

    [ObservableProperty] private TimerModel? _timerModels;

    partial void OnCustomConfigChanged(bool value)
    {
        ConfigurationManager.CurrentInstance.SetValue(ConfigurationKeys.CustomConfig, value.ToString());
    }

    partial void OnForceScheduledStartChanged(bool value)
    {
        ConfigurationManager.CurrentInstance.SetValue(ConfigurationKeys.ForceScheduledStart, value.ToString());
    }

    public void UpdateCurrentInstance(TimerModel model)
    {
        CustomConfig = Convert.ToBoolean(ConfigurationManager.CurrentInstance.GetValue(ConfigurationKeys.CustomConfig, bool.FalseString));
        ForceScheduledStart = Convert.ToBoolean(ConfigurationManager.CurrentInstance.GetValue(ConfigurationKeys.ForceScheduledStart, bool.FalseString));
        TimerModels = model;
    }

    public IAvaloniaReadOnlyList<MFAConfiguration> ConfigurationList { get; set; } = ConfigurationManager.Configs;
}
