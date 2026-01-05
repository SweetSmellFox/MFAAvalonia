using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Configuration;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels;

namespace MFAAvalonia.ViewModels.UsersControls.Settings;

public partial class CardSettingsUserControlModel : ViewModelBase
{
    [ObservableProperty]
    private bool _enableCardSystem = ConfigurationManager.Current.GetValue(ConfigurationKeys.EnableCardSystem, true);

    [ObservableProperty]
    private bool _loginAutoPull = ConfigurationManager.Current.GetValue(ConfigurationKeys.LoginAutoPull, true);

    [ObservableProperty]
    private bool _taskCompleteAutoPull = ConfigurationManager.Current.GetValue(ConfigurationKeys.TaskCompleteAutoPull, true);

    partial void OnEnableCardSystemChanged(bool value)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.EnableCardSystem, value);
        Instances.RootViewModel.EnableCardSystem = value;
        LoggerHelper.Info($"EnableCardSystem changed to: {value}");
    }

    partial void OnLoginAutoPullChanged(bool value)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.LoginAutoPull, value);
        LoggerHelper.Info($"LoginAutoPull changed to: {value}");
    }

    partial void OnTaskCompleteAutoPullChanged(bool value)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskCompleteAutoPull, value);
        LoggerHelper.Info($"TaskCompleteAutoPull changed to: {value}");
    }
}
