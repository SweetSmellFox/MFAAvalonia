using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MFAAvalonia.Controls;
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

        var tabsControl = this.FindControl<InstanceTabsControl>("TabsControl");
        if (tabsControl != null)
        {
            tabsControl.ContainerPrepared += OnContainerPrepared;

            // 将外部的 TabBarBackground Border 传给 InstanceTabsControl 用于 Clip 计算
            var tabBarBg = this.FindControl<Border>("TabBarBackgroundBorder");
            if (tabBarBg != null)
                tabsControl.SetExternalTabBarBackground(tabBarBg);
        }
    }

    private void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is DragTabItem dragTabItem)
        {
            dragTabItem.ContextMenu = CreateTabContextMenu(dragTabItem);
        }
    }

    private ContextMenu CreateTabContextMenu(DragTabItem container)
    {
        var renameItem = new MenuItem { Header = "重命名" };
        renameItem.Click += (_, _) =>
        {
            if (DataContext is InstanceTabBarViewModel vm)
            {
                var tab = container.DataContext as InstanceTabViewModel;
                if (tab != null)
                    vm.RenameInstanceCommand.Execute(tab);
            }
        };

        var closeItem = new MenuItem { Header = "关闭" };
        closeItem.Click += (_, _) =>
        {
            if (DataContext is InstanceTabBarViewModel vm)
            {
                var tab = container.DataContext as InstanceTabViewModel;
                if (tab != null)
                    vm.CloseInstanceCommand.Execute(tab);
            }
        };

        return new ContextMenu
        {
            Items = { renameItem, closeItem }
        };
    }

    private void OnDropdownItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (sender is Border border && border.DataContext is InstanceTabViewModel tab)
        {
            if (DataContext is InstanceTabBarViewModel viewModel)
            {
                viewModel.SelectInstanceCommand.Execute(tab);
            }
        }
    }
}
