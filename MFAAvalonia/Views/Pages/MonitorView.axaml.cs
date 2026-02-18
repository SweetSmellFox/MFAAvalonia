using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Pages;

namespace MFAAvalonia.Views.Pages;

public partial class MonitorView : UserControl
{
    public MonitorView()
    {
        InitializeComponent();
        if (!Design.IsDesignMode)
        {
            DataContext = Instances.MonitorViewModel;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (sender is Control { DataContext: MonitorItemViewModel vm })
            vm.SwitchCommand.Execute(null);
    }
}
