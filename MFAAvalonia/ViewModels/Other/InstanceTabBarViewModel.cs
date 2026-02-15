using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.MessageBox;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace MFAAvalonia.ViewModels.Other;

public partial class InstanceTabBarViewModel : ViewModelBase
{
    public ObservableCollection<InstanceTabViewModel> Tabs { get; } = new();

    [ObservableProperty] private InstanceTabViewModel? _activeTab;
    [ObservableProperty] private bool _isDropdownOpen;

    [ObservableProperty] private string _searchText = string.Empty;

    public ObservableCollection<InstanceTabViewModel> FilteredTabs { get; } = new();

    partial void OnIsDropdownOpenChanged(bool value)
    {
        if (value)
        {
            SearchText = string.Empty;
            RefreshFilteredTabs();
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        RefreshFilteredTabs();
    }

    private void RefreshFilteredTabs()
    {
        FilteredTabs.Clear();
        var query = SearchText?.Trim() ?? string.Empty;
        foreach (var tab in Tabs)
        {
            if (string.IsNullOrEmpty(query) || tab.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredTabs.Add(tab);
            }
        }
    }

    [RelayCommand]
    private void ToggleDropdown()
    {
        IsDropdownOpen = !IsDropdownOpen;
    }

    [RelayCommand]
    private void SelectInstance(InstanceTabViewModel? tab)
    {
        if (tab != null)
        {
            ActiveTab = tab;
            IsDropdownOpen = false;
        }
    }


    public InstanceTabBarViewModel()
    {
        ReloadTabs();
        MaaProcessor.Processors.CollectionChanged += (_, _) =>
        {
            DispatcherHelper.PostOnMainThread(ReloadTabs);
        };
    }

    public void ReloadTabs()
    {
        var processors = MaaProcessor.Processors.ToList();

        // Use smart sync instead of clear/add to preserve view state
        var toRemove = Tabs.Where(t => !processors.Contains(t.Processor)).ToList();
        foreach (var t in toRemove)
        {
            Tabs.Remove(t);
        }

        foreach (var processor in processors)
        {
            if (Tabs.All(t => t.Processor != processor))
            {
                Tabs.Add(new InstanceTabViewModel(processor));
            }
        }

        var current = MaaProcessorManager.Instance.Current;
        if (current != null)
        {
            var tab = Tabs.FirstOrDefault(t => t.InstanceId == current.InstanceId);
            if (tab != null && ActiveTab != tab)
            {
                ActiveTab = tab;
            }
        }
        else if (Tabs.Count > 0 && ActiveTab == null)
        {
            ActiveTab = Tabs.First();
        }
    }

    partial void OnActiveTabChanged(InstanceTabViewModel? oldValue, InstanceTabViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsActive = false;
        if (newValue != null)
        {
            newValue.IsActive = true;
            if (MaaProcessorManager.Instance.Current != newValue.Processor)
            {
                SwitchToInstance(newValue.Processor);
            }
            else
            {
                // 初始启动时 Current 已匹配，但 ConnectSettingsUserControlModel 的字段初始化器
                // 可能在 Current 还是 "default" 实例时就已读取，导致枚举类型属性（截图模式、触控模式）
                // 被错误地设为 Instance.default 的值。此处补一次同步确保 UI 反映正确的实例配置。
                SyncConnectSettingsForCurrentInstance();
            }
        }
    }
    private void SwitchToInstance(MaaProcessor processor)
    {
        // 切换前保存当前实例的任务状态
        var vm = MaaProcessorManager.Instance.Current?.ViewModel;
        if (vm != null)
        {
            MFAAvalonia.Configuration.ConfigurationManager.CurrentInstance.SetValue(
                MFAAvalonia.Configuration.ConfigurationKeys.TaskItems,
                vm.TaskItemViewModels.ToList().Select(model => model.InterfaceItem));
        }

        if (MaaProcessorManager.Instance.SwitchCurrent(processor.InstanceId))
        {
            // ReloadConfigurationForSwitch(false) 会刷新实例级配置（ConnectSettings 等），无需重复调用 SyncConnectSettingsForCurrentInstance
            Instances.ReloadConfigurationForSwitch(false);
        }
    }

    /// <summary>
    /// 同步更新连接设置 ViewModel，使其反映当前实例的配置。
    /// 使用 IsSyncing 标志跳过所有副作用（SetTasker、写回配置），仅做纯内存属性赋值。
    /// </summary>
    private static void SyncConnectSettingsForCurrentInstance()
    {
        if (!Instances.IsResolved<MFAAvalonia.ViewModels.UsersControls.Settings.ConnectSettingsUserControlModel>())
            return;

        var connect = Instances.ConnectSettingsUserControlModel;
        var config = MFAAvalonia.Configuration.ConfigurationManager.CurrentInstance;

        connect.IsSyncing = true;
        try
        {
            connect.CurrentControllerType = config.GetValue(MFAAvalonia.Configuration.ConfigurationKeys.CurrentController,
               MaaControllerTypes.Adb, MaaControllerTypes.None,
                new MFAAvalonia.Helper.Converters.UniversalEnumConverter<MaaControllerTypes>());
            connect.RememberAdb = config.GetValue(MFAAvalonia.Configuration.ConfigurationKeys.RememberAdb, true);
            connect.UseFingerprintMatching = config.GetValue(MFAAvalonia.Configuration.ConfigurationKeys.UseFingerprintMatching, true);
            connect.AdbControlScreenCapType = config.GetValue(MFAAvalonia.Configuration.ConfigurationKeys.AdbControlScreenCapType,
                MaaFramework.Binding.AdbScreencapMethods.None,
                new System.Collections.Generic.List<MaaFramework.Binding.AdbScreencapMethods>
                {
                    MaaFramework.Binding.AdbScreencapMethods.All,
                    MaaFramework.Binding.AdbScreencapMethods.Default
                }, new MFAAvalonia.Helper.Converters.UniversalEnumConverter<MaaFramework.Binding.AdbScreencapMethods>());
            connect.AdbControlInputType = config.GetValue(MFAAvalonia.Configuration.ConfigurationKeys.AdbControlInputType,
                MaaFramework.Binding.AdbInputMethods.None,
                new System.Collections.Generic.List<MaaFramework.Binding.AdbInputMethods>
                {
                    MaaFramework.Binding.AdbInputMethods.All,
                    MaaFramework.Binding.AdbInputMethods.Default
                }, new MFAAvalonia.Helper.Converters.UniversalEnumConverter<MaaFramework.Binding.AdbInputMethods>());
            connect.Win32ControlScreenCapType = config.GetValue(MFAAvalonia.Configuration.ConfigurationKeys.Win32ControlScreenCapType,
                MaaFramework.Binding.Win32ScreencapMethod.FramePool, MaaFramework.Binding.Win32ScreencapMethod.None,
                new MFAAvalonia.Helper.Converters.UniversalEnumConverter<MaaFramework.Binding.Win32ScreencapMethod>());
            connect.Win32ControlMouseType = config.GetValue(MFAAvalonia.Configuration.ConfigurationKeys.Win32ControlMouseType,
                MaaFramework.Binding.Win32InputMethod.SendMessage, MaaFramework.Binding.Win32InputMethod.None,
                new MFAAvalonia.Helper.Converters.UniversalEnumConverter<MaaFramework.Binding.Win32InputMethod>());
            connect.Win32ControlKeyboardType = config.GetValue(MFAAvalonia.Configuration.ConfigurationKeys.Win32ControlKeyboardType,
                MaaFramework.Binding.Win32InputMethod.SendMessage, MaaFramework.Binding.Win32InputMethod.None,
                new MFAAvalonia.Helper.Converters.UniversalEnumConverter<MaaFramework.Binding.Win32InputMethod>());
            connect.RetryOnDisconnected = config.GetValue(MFAAvalonia.Configuration.ConfigurationKeys.RetryOnDisconnected, false);
            connect.RetryOnDisconnectedWin32 = config.GetValue(MFAAvalonia.Configuration.ConfigurationKeys.RetryOnDisconnectedWin32, false);
            connect.AllowAdbRestart = config.GetValue(MFAAvalonia.Configuration.ConfigurationKeys.AllowAdbRestart, true);
            connect.AllowAdbHardRestart = config.GetValue(MFAAvalonia.Configuration.ConfigurationKeys.AllowAdbHardRestart, true);
            connect.AutoDetectOnConnectionFailed = config.GetValue(MFAAvalonia.Configuration.ConfigurationKeys.AutoDetectOnConnectionFailed, true);
            connect.AutoConnectAfterRefresh = config.GetValue(MFAAvalonia.Configuration.ConfigurationKeys.AutoConnectAfterRefresh, true);
        }
        finally
        {
            connect.IsSyncing = false;
        }
    }

    /// <summary>
    /// 通过实例ID切换到指定多开实例（供全局定时器调用）
    /// </summary>
    public void SwitchToInstanceById(string instanceId)
    {
        var tab = Tabs.FirstOrDefault(t => t.InstanceId == instanceId);
        if (tab != null)
        {
            ActiveTab = tab;
        }
    }


    [RelayCommand]
    private async Task AddInstance()
    {
        var processor = MaaProcessorManager.Instance.CreateInstance(false);
        await Task.Run(() => processor.InitializeData());

        // ReloadTabs 已通过 Processors.CollectionChanged 自动添加了 tab，无需手动添加
        var tab = Tabs.FirstOrDefault(t => t.Processor == processor);
        if (tab != null)
            ActiveTab = tab;
    }

    [RelayCommand]
    private async Task CloseInstance(InstanceTabViewModel? tab)
    {
        if (tab == null) return;

        if (Tabs.Count <= 1)
        {
            ToastHelper.Info("不能关闭最后一个实例");
            return;
        }

        if (tab.IsRunning)
        {
            var result = await SukiUI.MessageBox.SukiMessageBox.ShowDialog(new SukiMessageBoxHost
            {
                Content = "实例正在运行，确定要停止并关闭吗？",
                ActionButtonsPreset = SukiUI.MessageBox.SukiMessageBoxButtons.YesNo,
                IconPreset = SukiUI.MessageBox.SukiMessageBoxIcons.Warning
            }, new SukiUI.MessageBox.SukiMessageBoxOptions
            {
                Title = "关闭实例"
            });

            if (!result.Equals(SukiMessageBoxResult.Yes)) return;

            tab.Processor.Stop(MFATask.MFATaskStatus.STOPPED);
        }

        if (MaaProcessorManager.Instance.RemoveInstance(tab.InstanceId))
        {
            Tabs.Remove(tab);
            if (ActiveTab == tab || ActiveTab == null)
            {
                ActiveTab = Tabs.FirstOrDefault();
            }
        }
    }

    [RelayCommand]
    private void RenameInstance(InstanceTabViewModel? tab)
    {
        if (tab == null) return;

        Instances.DialogManager.CreateDialog()
            .WithTitle(LangKeys.TaskRename.ToLocalization())
            .WithViewModel(dialog => new RenameInstanceDialogViewModel(dialog, tab))
            .TryShow();
    }
}
