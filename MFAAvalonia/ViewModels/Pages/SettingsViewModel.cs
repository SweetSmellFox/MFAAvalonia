using Avalonia.Collections;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using SukiUI;
using SukiUI.Enums;
using SukiUI.Models;
using System;
using System.IO;
using System.Linq;
using System.Windows.Input;

namespace MFAAvalonia.ViewModels.Pages;

public partial class SettingsViewModel : ViewModelBase
{
    protected override void Initialize()
    {
        // 延迟加载快捷键，避免在 ViewModel 初始化阶段通过 Instances 互锁
        Dispatcher.UIThread.Post(() =>
        {
            HotKeyShowGui = MFAHotKey.Parse(GlobalConfiguration.GetValue(ConfigurationKeys.ShowGui, ""));
            HotKeyLinkStart = MFAHotKey.Parse(GlobalConfiguration.GetValue(ConfigurationKeys.LinkStart, ""));
        }, DispatcherPriority.Background);
    }

    #region 配置

    public IAvaloniaReadOnlyList<MFAConfiguration> ConfigurationList { get; set; } = ConfigurationManager.Configs;

    [ObservableProperty] private string? _currentConfiguration = ConfigurationManager.GetCurrentConfiguration();

    partial void OnCurrentConfigurationChanged(string value)
    {
        ConfigurationManager.SetDefaultConfig(value);
        Instances.RestartApplication();
    }

    [ObservableProperty] private string _newConfigurationName = string.Empty;

    [RelayCommand]
    private void AddConfiguration()
    {
        if (string.IsNullOrWhiteSpace(NewConfigurationName))
        {
            NewConfigurationName = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }

        var configDPath = Path.Combine(AppContext.BaseDirectory, "config");
        var configPath = Path.Combine(configDPath, $"{ConfigurationManager.GetActualConfiguration()}.json");
        var newConfigPath = Path.Combine(configDPath, $"mfa_{NewConfigurationName}.json");
        bool configExists = Directory.GetFiles(configDPath, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Any(name => name.Equals(NewConfigurationName, StringComparison.OrdinalIgnoreCase));

        if (configExists)
        {
            ToastHelper.Error(LangKeys.ConfigNameAlreadyExists.ToLocalizationFormatted(false, NewConfigurationName));
            return;
        }
        if (File.Exists(configPath))
        {
            var content = File.ReadAllText(configPath);
            File.WriteAllText(newConfigPath, content);

            ConfigurationManager.Add(NewConfigurationName);
            ToastHelper.Success(LangKeys.ConfigAddedSuccessfully.ToLocalizationFormatted(false, NewConfigurationName));
        }
    }

    #endregion 配置

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
        set => SetHotKey(ref _hotKeyLinkStart, value, ConfigurationKeys.LinkStart, null);
    }

    public void SetHotKey(ref MFAHotKey value, MFAHotKey? newValue, string type, ICommand? command)
    {
        if (newValue != null)
        {
            // 如果 command 为 null，尝试从对应的 ViewModel 动态获取，避免初始化时的循环依赖
            var targetCommand = command;
            if (targetCommand == null)
            {
                if (type == ConfigurationKeys.LinkStart)
                {
                    targetCommand = Instances.TaskQueueViewModel.ToggleCommand;
                }
            }

            if (targetCommand != null)
            {
                if (!GlobalHotkeyService.Register(newValue.Gesture, targetCommand))
                {
                    newValue = MFAHotKey.ERROR;
                }
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
