using Avalonia.Collections;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.ViewModels.Other;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace MFAAvalonia.ViewModels.Pages;

public partial class SettingsViewModel : ViewModelBase
{
    private bool _hotkeysInitialized;

    protected override void Initialize()
    {
        _hotKeyShowGui = MFAHotKey.Parse(GlobalConfiguration.GetValue(ConfigurationKeys.ShowGui, ""));
        _hotKeyLinkStart = MFAHotKey.Parse(GlobalConfiguration.GetValue(ConfigurationKeys.LinkStart, ""));

        DispatcherHelper.PostOnMainThread(InitializeHotkeysAfterStartup);
    }

    private void InitializeHotkeysAfterStartup()
    {
        if (_hotkeysInitialized)
        {
            return;
        }

        _hotkeysInitialized = true;

        SetHotKey(ref _hotKeyShowGui, _hotKeyShowGui, ConfigurationKeys.ShowGui,
            Instances.RootViewModel.ToggleVisibleCommand);

        SetHotKey(ref _hotKeyLinkStart, _hotKeyLinkStart, ConfigurationKeys.LinkStart,
            new RelayCommand(() => Instances.InstanceTabBarViewModel.ActiveTab?.TaskQueueViewModel?.ToggleCommand.Execute(null)));
    }

    #region 多开实例管理

    /// <summary>
    /// 多开实例列表（用于设置界面的配置管理）
    /// </summary>
    public ObservableCollection<InstanceTabViewModel> ConfigurationList => Instances.InstanceTabBarViewModel.Tabs;

    [ObservableProperty] private InstanceTabViewModel? _currentConfiguration;

    partial void OnCurrentConfigurationChanged(InstanceTabViewModel? value)
    {
        if (value != null)
        {
            Instances.InstanceTabBarViewModel.ActiveTab = value;
        }
    }

    public void RefreshCurrentConfiguration()
    {
        CurrentConfiguration = Instances.InstanceTabBarViewModel.ActiveTab;
    }

    [ObservableProperty] private string _newConfigurationName = string.Empty;

    [RelayCommand]
    private void AddConfiguration()
    {
        // 如果用户输入了名称，检查是否已存在
        if (!string.IsNullOrWhiteSpace(NewConfigurationName))
        {
            var configExists = ConfigurationList.Any(tab =>
                tab.Name.Equals(NewConfigurationName, System.StringComparison.OrdinalIgnoreCase));
            
            if (configExists)
            {
                ToastHelper.Error(LangKeys.ConfigNameAlreadyExists.ToLocalizationFormatted(false, NewConfigurationName));
                return;
            }
        }
        
        // 添加新的多开实例
        Instances.InstanceTabBarViewModel.AddInstanceCommand.Execute(null);
        
        // 如果用户输入了名称，则设置实例名称
        if (!string.IsNullOrWhiteSpace(NewConfigurationName))
        {
            var newTab = Instances.InstanceTabBarViewModel.ActiveTab;
            if (newTab != null)
            {
                MaaProcessorManager.Instance.SetInstanceName(newTab.InstanceId, NewConfigurationName);
                newTab.UpdateName();
            }
            NewConfigurationName = string.Empty;
        }
        
        ToastHelper.Success(LangKeys.ConfigAddedSuccessfully.ToLocalizationFormatted(false,
            Instances.InstanceTabBarViewModel.ActiveTab?.Name ?? ""));
    }

    #endregion 多开实例管理

    #region HotKey
    [ObservableProperty] private bool _enableHotKey = true;
    private MFAHotKey _hotKeyShowGui = MFAHotKey.NOTSET;

    public MFAHotKey HotKeyShowGui
    {
        get => _hotKeyShowGui;
        set => SetHotKey(ref _hotKeyShowGui, value, ConfigurationKeys.ShowGui, Instances.RootViewModel.ToggleVisibleCommand);
    }

    private MFAHotKey _hotKeyLinkStart = MFAHotKey.NOTSET;

    public MFAHotKey HotKeyLinkStart
    {
        get => _hotKeyLinkStart;
        set => SetHotKey(ref _hotKeyLinkStart, value, ConfigurationKeys.LinkStart, new RelayCommand(() => Instances.InstanceTabBarViewModel.ActiveTab?.TaskQueueViewModel?.ToggleCommand.Execute(null)));
    }

    public void SetHotKey(ref MFAHotKey value, MFAHotKey? newValue, string type, ICommand command)
    {
        if (newValue != null)
        {
            if (!GlobalHotkeyService.Register(newValue.Gesture, command))
            {
                newValue = MFAHotKey.ERROR;
            }
            GlobalConfiguration.SetValue(type, newValue.ToString());
            SetProperty(ref value, newValue);
        }
    }

    #endregion HotKey
    
    #region 资源

    [ObservableProperty] private bool _showResourceIssues;
    [ObservableProperty] private string _resourceIssues = string.Empty;
    [ObservableProperty] private string _resourceGithub = string.Empty;

    [ObservableProperty] private string _resourceContact = string.Empty;
    [ObservableProperty] private string _resourceDescription = string.Empty;
    [ObservableProperty] private string _resourceLicense = string.Empty;
    [ObservableProperty] private bool _hasResourceContact;
    [ObservableProperty] private bool _hasResourceDescription;
    [ObservableProperty] private bool _hasResourceLicense;

    #endregion
}
