using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;

namespace MFAAvalonia.ViewModels.Pages;

public partial class MonitorViewModel : ViewModelBase, IDisposable
{
    public ObservableCollection<MonitorItemViewModel> Items { get; } = new();
    private readonly DispatcherTimer _timer;

    public MonitorViewModel()
    {
        RefreshItems();
        
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.5) 
        };
        _timer.Tick += (s, e) => UpdateAll();
        _timer.Start();
        
        MaaProcessor.Processors.CollectionChanged += Processors_CollectionChanged;
    }

    private void Processors_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
       RefreshItems();
    }

    private void RefreshItems()
    {
        DispatcherHelper.PostOnMainThread(() =>
        {
            var processors = MaaProcessor.Processors.ToList();
            
            var toRemove = Items.Where(i => !processors.Contains(i.Processor)).ToList();
            foreach(var item in toRemove)
            {
                item.Dispose();
                Items.Remove(item);
            }

            foreach(var p in processors)
            {
                if (!Items.Any(i => i.Processor == p))
                {
                    Items.Add(new MonitorItemViewModel(p));
                }
            }
        });
    }

    private void UpdateAll()
    {
        foreach(var item in Items)
        {
            item.UpdateInfo();
            // Run image update in background
            System.Threading.Tasks.Task.Run(() => item.UpdateImage());
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        foreach(var item in Items) item.Dispose();
        GC.SuppressFinalize(this);
    }
}
