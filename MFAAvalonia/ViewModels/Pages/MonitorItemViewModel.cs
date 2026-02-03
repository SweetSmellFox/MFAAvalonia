using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using Avalonia.Threading;
using System.Linq;
using System.Threading.Tasks;

namespace MFAAvalonia.ViewModels.Pages;

public partial class MonitorItemViewModel : ViewModelBase
{
    public MaaProcessor Processor { get; }

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
    }

    public void UpdateInfo()
    {
        Name = MaaProcessorManager.Instance.GetInstanceName(Processor.InstanceId);
        IsConnected = Processor.ViewModel?.IsConnected ?? false;
        // Check if actually running a task
        IsRunning = Processor.TaskQueue.Count > 0;
    }

    public void UpdateImage()
    {
        if (!IsConnected)
        {
             if (Image != null)
             {
                 var old = Image;
                 DispatcherHelper.PostOnMainThread(() => 
                 {
                     Image = null;
                     old.Dispose();
                     HasImage = false;
                 });
             }
             return;
        }

        Bitmap? newImage = null;
        try 
        {
             newImage = Processor.GetLiveView(false); 
        }
        catch {}

        if (newImage != null)
        {
            DispatcherHelper.PostOnMainThread(() => 
            {
                var old = Image;
                Image = newImage;
                old?.Dispose();
                HasImage = true;
            });
        }
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
        Image?.Dispose();
    }
}
