using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.ViewModels.Other;
using System.Collections.ObjectModel;

namespace MFAAvalonia.ViewModels.UsersControls.Settings;

public partial class TimerSettingsUserControlModel : ViewModelBase
{
    /// <summary>
    /// 全局定时器模型（所有多开实例共享）
    /// </summary>
    public TimerModel TimerModels => TimerModel.Instance;

    /// <summary>
    /// 实例列表，供 UI ComboBox 绑定（UI 上仍显示为"配置"）
    /// </summary>
    public ObservableCollection<TimerModel.InstanceEntry> InstanceList => TimerModel.Instance.InstanceList;

    public void RefreshInstances()
    {
        TimerModel.Instance.RefreshInstanceList();
        OnPropertyChanged(nameof(InstanceList));
    }
}
