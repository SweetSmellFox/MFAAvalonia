using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Helper.ValueType;

namespace MFAAvalonia.ViewModels.UsersControls.Settings;

public partial class AddTaskItemViewModel : ObservableObject
{
    [ObservableProperty] private int _addCount;

    [ObservableProperty] private string _name = "";

    [ObservableProperty] private string _icon = "";

    [ObservableProperty] private bool _hasIcon;

    [ObservableProperty] private bool _hasCount;

    public DragItemViewModel? Source { get; set; }

    public bool IsSpecialTask { get; set; }

    public string? SpecialActionName { get; set; }

    public string SpecialIcon { get; set; } = "";

    public AddTaskItemViewModel()
    {
    }

    public AddTaskItemViewModel(DragItemViewModel source)
    {
        Source = source;
        Name = source.Name;
        HasIcon = source.HasIcon;
        Icon = source.ResolvedIcon;
    }

    partial void OnAddCountChanged(int value)
    {
        HasCount = value > 0;
    }

    [RelayCommand]
    private void Increment()
    {
        AddCount++;
    }

    [RelayCommand]
    private void Decrement()
    {
        if (AddCount > 0)
            AddCount--;
    }

    [RelayCommand]
    private void ResetCount()
    {
        AddCount = 0;
    }
}
