using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace MFAAvalonia.ViewModels.Pages;

public partial class MonitorItemViewModel : ViewModelBase
{
    public MaaProcessor Processor { get; }
    public DateTime CreatedAt { get; } = DateTime.Now;
    private TaskQueueViewModel? _subscribedViewModel;

    [ObservableProperty]
    private Bitmap? _image;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isRunning;
    
    [ObservableProperty]
    private bool _hasImage;

    public MonitorItemViewModel(MaaProcessor processor)
    {
        Processor = processor;
        UpdateInfo();
        TrySubscribeViewModel();
    }

    private void TrySubscribeViewModel()
    {
        var vm = Processor.ViewModel;
        if (vm == _subscribedViewModel) return;
        if (_subscribedViewModel != null)
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribedViewModel = vm;
        if (vm != null)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            SyncImage(vm.LiveViewImage);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TaskQueueViewModel.LiveViewImage))
            SyncImage(_subscribedViewModel?.LiveViewImage);
    }

    private void SyncImage(Bitmap? source)
    {
        Image = source;
        HasImage = source != null;
    }

    public void UpdateInfo()
    {
        Name = MaaProcessorManager.Instance.GetInstanceName(Processor.InstanceId);
        IsConnected = Processor.ViewModel?.IsConnected ?? false;
        IsRunning = Processor.TaskQueue.Count > 0;
        TrySubscribeViewModel();
    }

    [RelayCommand]
    private void Switch()
    {
        var tab = Instances.InstanceTabBarViewModel.Tabs.FirstOrDefault(t => t.Processor == Processor);
        if (tab != null)
        {
            Instances.InstanceTabBarViewModel.ActiveTab = tab;
        }
    }

    [RelayCommand]
    private void StartTask()
    {
        Processor.Start();
    }

    [RelayCommand]
    private void StopTask()
    {
        Processor.Stop(MFATask.MFATaskStatus.STOPPED);
    }

    [RelayCommand]
    private async Task Connect()
    {
        await Processor.ReconnectAsync();
    }

    public void Dispose()
    {
        if (_subscribedViewModel != null)
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }
}
