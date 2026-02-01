using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using MFAAvalonia.ViewModels.Other;

namespace MFAAvalonia.Views.UserControls;

public partial class InstanceTabBar : UserControl
{
    public InstanceTabBar()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        var scrollViewer = this.FindControl<ScrollViewer>("TabScrollViewer");
        if (scrollViewer != null)
        {
            scrollViewer.PointerWheelChanged += (sender, e) =>
            {
                if (e.KeyModifiers == KeyModifiers.None && e.Delta.Y != 0)
                {
                    var offset = scrollViewer.Offset.X - e.Delta.Y * 50;
                    scrollViewer.Offset = new Vector(offset, scrollViewer.Offset.Y);
                    e.Handled = true;
                }
            };
        }
    }

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 仅处理左键点击
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (sender is Border border && border.DataContext is InstanceTabViewModel tab)
        {
            if (DataContext is InstanceTabBarViewModel viewModel)
            {
                viewModel.ActiveTab = tab;
            }
        }
    }
}
