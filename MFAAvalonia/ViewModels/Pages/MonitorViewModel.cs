using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    [ObservableProperty]
    private int _sortIndex;

    partial void OnSortIndexChanged(int value) => ApplySort();

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

            if (SortIndex != 0)
                ApplySort();
        });
    }

    private void ApplySort()
    {
        DispatcherHelper.PostOnMainThread(() =>
        {
            var sorted = SortIndex switch
            {
                1 => Items.OrderByDescending(i => i.IsRunning)
                    .ThenByDescending(i => i.IsConnected).ToList(),
                2 => Items.OrderBy(i => i.Name, StringComparer.CurrentCulture).ToList(),
                3 => Items.OrderBy(i => i.CreatedAt).ToList(),
                _ => null
            };

            if (sorted == null) return;

            for (int i = 0; i < sorted.Count; i++)
            {
                var oldIndex = Items.IndexOf(sorted[i]);
                if (oldIndex != i)
                    Items.Move(oldIndex, i);
            }
        });
    }

    private void UpdateAll()
    {
        foreach(var item in Items)
            item.UpdateInfo();
    }

    public void Dispose()
    {
        _timer.Stop();
        foreach(var item in Items) item.Dispose();
        GC.SuppressFinalize(this);
    }
}
