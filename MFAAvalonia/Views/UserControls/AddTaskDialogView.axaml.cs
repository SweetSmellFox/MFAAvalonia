using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MFAAvalonia.Extensions;
using MFAAvalonia.ViewModels.UsersControls.Settings;

namespace MFAAvalonia.Views.UserControls;

public partial class AddTaskDialogView : UserControl
{
    public AddTaskDialogView()
    {
        InitializeComponent();
    }

    private void SearchBar_OnSearchStarted(object sender, RoutedEventArgs e)
    {
        string key = SearchBar.Text ?? "";

        if (DataContext is AddTaskDialogViewModel vm)
        {
            vm.Items.Clear();
            if (string.IsNullOrEmpty(key))
            {
                vm.Items.AddRange(vm.Sources);
            }
            else
            {
                key = key.ToLower();
                foreach (var item in vm.Sources)
                {
                    if (item.Name.ToLower().Contains(key))
                        vm.Items.Add(item);
                }
            }
        }
    }

    private void TaskItem_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: AddTaskItemViewModel item })
        {
            item.IncrementCommand.Execute(null);
        }
    }

    private void MinusItem_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (sender is Border { DataContext: AddTaskItemViewModel item })
        {
            item.DecrementCommand.Execute(null);
        }
    }

    private void BadgeItem_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (sender is Border { DataContext: AddTaskItemViewModel item })
        {
            item.ResetCountCommand.Execute(null);
        }
    }
}
