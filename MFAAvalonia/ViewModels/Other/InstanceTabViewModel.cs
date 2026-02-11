using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.ViewModels.Pages;
using Avalonia.Controls;
using MFAAvalonia.Views.Pages;

namespace MFAAvalonia.ViewModels.Other;

public partial class InstanceTabViewModel : ViewModelBase
{
    public readonly MaaProcessor Processor;

    public TaskQueueViewModel TaskQueueViewModel => MaaProcessorManager.Instance.GetViewModel(InstanceId);

    private Control? _view;

    public Control View
    {
        get
        {
            if (_view == null)
            {
                _view = new TaskQueueView
                {
                    DataContext = TaskQueueViewModel
                };
            }
            return _view;
        }
    }

    public InstanceTabViewModel(MaaProcessor processor)
    {
        Processor = processor;
        InstanceId = processor.InstanceId;
        UpdateName();

        IsRunning = processor.TaskQueue.Count > 0;
        processor.TaskQueue.CountChanged += OnTaskCountChanged;
    }

    private void OnTaskCountChanged(object? sender, ObservableQueue<MFATask>.CountChangedEventArgs e)
    {
        DispatcherHelper.RunOnMainThread(() =>
        {
            IsRunning = e.NewValue > 0;
        });
    }

    public string InstanceId { get; }

    [ObservableProperty] private string _name = string.Empty;

    [ObservableProperty] private bool _isRunning;

    [ObservableProperty] private bool _isActive;
    public void UpdateName()
    {
        Name = MaaProcessorManager.Instance.GetInstanceName(InstanceId);
    }

    /// <summary>
    /// 删除当前多开实例（用于设置界面的配置管理）
    /// </summary>
    [RelayCommand]
    private void DeleteConfiguration()
    {
        Instances.InstanceTabBarViewModel.CloseInstanceCommand.Execute(this);
    }
}
