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
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace MFAAvalonia.ViewModels.Other;

public partial class InstanceTabBarViewModel : ViewModelBase
{
    public ObservableCollection<InstanceTabViewModel> Tabs { get; } = new();

    [ObservableProperty]
    private InstanceTabViewModel? _activeTab;

    public InstanceTabBarViewModel()
    {
        ReloadTabs();
    }

    public void ReloadTabs()
    {
        Tabs.Clear();
        foreach (var processor in MaaProcessorManager.Instance.Instances)
        {
            Tabs.Add(new InstanceTabViewModel(processor));
        }
        
        var current = MaaProcessorManager.Instance.Current;
        var tab = Tabs.FirstOrDefault(t => t.InstanceId == current.InstanceId);
        if (tab != null)
        {
            _activeTab = tab;
            OnPropertyChanged(nameof(ActiveTab));
            tab.IsActive = true;
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
            Instances.ReloadConfigurationForSwitch(false);
        }
    }

    [RelayCommand]
    private void AddInstance()
    {
        var processor = MaaProcessorManager.Instance.CreateInstance(false);
        processor.InitializeData();
        
        var tab = new InstanceTabViewModel(processor);
        Tabs.Add(tab);
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
             }, new SukiUI.MessageBox.SukiMessageBoxOptions { Title = "关闭实例" });

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
        if(tab == null) return;
        
        Instances.DialogManager.CreateDialog()
            .WithTitle(LangKeys.TaskRename.ToLocalization())
            .WithViewModel(dialog => new RenameInstanceDialogViewModel(dialog, tab))
            .TryShow();
    }
}
