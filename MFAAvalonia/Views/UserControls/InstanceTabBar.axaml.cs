using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MFAAvalonia.Controls;
using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Other;
using System.Linq;

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
            tabsControl.TabOrderChanged += OnTabOrderChanged;

            // 溢出按钮点击 → 打开下拉框
            tabsControl.OverflowButtonClicked += () =>
            {
                if (DataContext is InstanceTabBarViewModel vm)
                    vm.ToggleDropdownCommand.Execute(null);
            };

            // 将外部的 TabBarBackground Border 传给 InstanceTabsControl 用于 Clip 计算
            var tabBarBg = this.FindControl<Border>("TabBarBackgroundBorder");
            if (tabBarBg != null)
                tabsControl.SetExternalTabBarBackground(tabBarBg);

            // 模板应用后，将 PART_AddItemButton 设为预设菜单 Popup 的 PlacementTarget
            tabsControl.TemplateApplied += (_, _) =>
            {
                var addBtn = tabsControl.GetTemplateChildren()
                    .OfType<Button>()
                    .FirstOrDefault(b => b.Name == "PART_AddItemButton");
                var popup = this.FindControl<Popup>("PresetMenuPopup");
                if (addBtn != null && popup != null)
                    popup.PlacementTarget = addBtn;
            };
        }
    }

    private void OnTabOrderChanged()
    {
        if (DataContext is InstanceTabBarViewModel vm)
            vm.SaveTabOrder();
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
        var renameItem = new MenuItem { Header = LangKeys.TaskRename.ToLocalization() };
        renameItem.Click += (_, _) =>
        {
            if (DataContext is InstanceTabBarViewModel vm)
            {
                var tab = container.DataContext as InstanceTabViewModel;
                if (tab != null)
                    vm.RenameInstanceCommand.Execute(tab);
            }
        };

        var closeItem = new MenuItem { Header = LangKeys.ButtonClose.ToLocalization() };
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

    private void OnDropdownCloseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is InstanceTabViewModel tab)
        {
            if (DataContext is InstanceTabBarViewModel vm)
                vm.CloseInstanceCommand.Execute(tab);
        }
    }
}
