using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Lang.Avalonia.MarkupExtensions;
using MFAAvalonia.Controls;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Other;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
        var instanceDropdown = this.FindControl<Popup>("InstanceDropdown");
        var dropdownButton = this.FindControl<Button>("DropdownButton");
        if (tabsControl != null)
        {
            tabsControl.ContainerPrepared += OnContainerPrepared;
            tabsControl.TabOrderChanged += OnTabOrderChanged;

            // 溢出按钮点击 → 打开下拉框
            tabsControl.OverflowButtonClicked += () =>
            {
                if (instanceDropdown != null)
                    instanceDropdown.PlacementTarget = tabsControl.OverflowButton ?? dropdownButton;

                if (DataContext is InstanceTabBarViewModel vm)
                    vm.ToggleDropdownCommand.Execute(null);
            };

            if (dropdownButton != null && instanceDropdown != null)
            {
                dropdownButton.Click += (_, _) =>
                {
                    // 左侧展开按钮点击时，确保下拉框从左侧按钮下方弹出
                    instanceDropdown.PlacementTarget = dropdownButton;
                };
            }

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
        var addItem = new MenuItem();
        addItem.Header = "新建标签页";
        addItem.Icon = new FluentIcons.Avalonia.Fluent.FluentIcon
        {
            Icon = FluentIcons.Common.Icon.Add,
            IconSize = FluentIcons.Common.IconSize.Size16,
            IconVariant = FluentIcons.Common.IconVariant.Regular
        };
        addItem.Click += async (_, _) =>
        {
            if (DataContext is InstanceTabBarViewModel vm)
                await vm.AddInstanceCommand.ExecuteAsync(null);
        };

        var copyItem = new MenuItem();
        copyItem.Header = "复制标签页";
        copyItem.Icon = new FluentIcons.Avalonia.Fluent.FluentIcon
        {
            Icon = FluentIcons.Common.Icon.Copy,
            IconSize = FluentIcons.Common.IconSize.Size16,
            IconVariant = FluentIcons.Common.IconVariant.Regular
        };
        copyItem.Click += async (_, _) =>
        {
            if (DataContext is not InstanceTabBarViewModel vm) return;
            if (container.DataContext is not InstanceTabViewModel tab) return;
            await DuplicateInstanceAsync(vm, tab);
        };

        var copyIdItem = new MenuItem
        {
            Header = "CopyInstanceId".ToLocalization(),
            Icon = new FluentIcons.Avalonia.Fluent.FluentIcon
            {
                Icon = FluentIcons.Common.Icon.Clipboard,
                IconSize = FluentIcons.Common.IconSize.Size16,
                IconVariant = FluentIcons.Common.IconVariant.Regular
            }
        };
        copyIdItem.Click += async (_, _) =>
        {
            if (container.DataContext is not InstanceTabViewModel tab) return;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;

            await clipboard.SetTextAsync(tab.InstanceId);
            ToastHelper.Info(LangKeys.CopiedToClipboard.ToLocalization());
        };

        var renameItem = new MenuItem();
        renameItem.Header = "重命名";
        renameItem.Icon = new FluentIcons.Avalonia.Fluent.FluentIcon
        {
            Icon = FluentIcons.Common.Icon.Edit,
            IconSize = FluentIcons.Common.IconSize.Size16,
            IconVariant = FluentIcons.Common.IconVariant.Regular
        };
        renameItem.Click += (_, _) =>
        {
            if (DataContext is InstanceTabBarViewModel vm)
            {
                var tab = container.DataContext as InstanceTabViewModel;
                if (tab != null)
                    vm.RenameInstanceCommand.Execute(tab);
            }
        };

        var closeItem = new MenuItem();
        closeItem.Header = "关闭标签页";
        closeItem.Icon = new FluentIcons.Avalonia.Fluent.FluentIcon
        {
            Icon = FluentIcons.Common.Icon.Dismiss,
            IconSize = FluentIcons.Common.IconSize.Size16,
            IconVariant = FluentIcons.Common.IconVariant.Regular
        };
        closeItem.Click += async (_, _) =>
        {
            if (DataContext is not InstanceTabBarViewModel vm) return;
            if (container.DataContext is not InstanceTabViewModel tab) return;
            await vm.CloseInstanceCommand.ExecuteAsync(tab);
        };

        var closeOthersItem = new MenuItem
        {
            Header = "关闭其他标签页",
            Icon = new FluentIcons.Avalonia.Fluent.FluentIcon
            {
                Icon = FluentIcons.Common.Icon.DismissCircle,
                IconSize = FluentIcons.Common.IconSize.Size16,
                IconVariant = FluentIcons.Common.IconVariant.Regular
            }
        };
        closeOthersItem.Click += async (_, _) =>
        {
            if (DataContext is not InstanceTabBarViewModel vm) return;
            if (container.DataContext is not InstanceTabViewModel currentTab) return;

            var toClose = vm.Tabs.Where(t => t != currentTab).ToList();
            foreach (var tab in toClose)
                await vm.CloseInstanceCommand.ExecuteAsync(tab);
        };

        var closeRightItem = new MenuItem
        {
            Header = "关闭右侧标签页",
            Icon = new FluentIcons.Avalonia.Fluent.FluentIcon
            {
                Icon = FluentIcons.Common.Icon.ArrowExit,
                IconSize = FluentIcons.Common.IconSize.Size16,
                IconVariant = FluentIcons.Common.IconVariant.Regular
            }
        };
        closeRightItem.Click += async (_, _) =>
        {
            if (DataContext is not InstanceTabBarViewModel vm) return;
            if (container.DataContext is not InstanceTabViewModel currentTab) return;

            var currentIndex = vm.Tabs.IndexOf(currentTab);
            if (currentIndex < 0) return;

            var toClose = vm.Tabs.Skip(currentIndex + 1).ToList();
            foreach (var tab in toClose)
                await vm.CloseInstanceCommand.ExecuteAsync(tab);
        };

        var menu = new ContextMenu
        {
            Items =
            {
                addItem,
                copyItem,
                copyIdItem,
                renameItem,
                new Separator(),
                closeItem,
                closeOthersItem,
                closeRightItem
            }
        };

        menu.Opening += (_, _) =>
        {
            if (DataContext is not InstanceTabBarViewModel vm)
            {
                closeOthersItem.IsEnabled = false;
                closeRightItem.IsEnabled = false;
                return;
            }

            if (container.DataContext is not InstanceTabViewModel currentTab)
            {
                closeOthersItem.IsEnabled = false;
                closeRightItem.IsEnabled = false;
                return;
            }

            var currentIndex = vm.Tabs.IndexOf(currentTab);
            var hasRight = currentIndex >= 0 && currentIndex < vm.Tabs.Count - 1;

            closeOthersItem.IsEnabled = vm.Tabs.Count > 1;
            closeRightItem.IsEnabled = hasRight;
        };

        return menu;
    }

    private static async Task DuplicateInstanceAsync(InstanceTabBarViewModel vm, InstanceTabViewModel sourceTab)
    {
        var sourceVm = sourceTab.TaskQueueViewModel;
        if (sourceVm != null)
        {
            sourceTab.Processor.InstanceConfiguration.SetValue(
                Configuration.ConfigurationKeys.TaskItems,
                sourceVm.TaskItemViewModels
                    .Where(m => !m.IsResourceOptionItem)
                    .Select(model => model.InterfaceItem)
                    .ToList());
        }

        var newId = MaaProcessorManager.CreateInstanceId();
        sourceTab.Processor.InstanceConfiguration.CopyToNewInstance(newId);
        new InstanceConfiguration(newId).RemoveValue(Configuration.ConfigurationKeys.InstancePresetKey);

        var processor = MaaProcessorManager.Instance.CreateInstance(newId, false);
        await Task.Run(() => processor.InitializeData());

        vm.ReloadTabs();
        var tab = vm.Tabs.FirstOrDefault(t => t.Processor == processor);
        if (tab != null)
            vm.ActiveTab = tab;
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
        e.Handled = true;

        if (sender is Button btn && btn.DataContext is InstanceTabViewModel tab)
        {
            if (DataContext is InstanceTabBarViewModel vm)
                vm.CloseInstanceCommand.Execute(tab);
        }
    }

    private void OnRecentClosedItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (sender is Border border && border.DataContext is RecentClosedInstanceItem item)
        {
            if (DataContext is InstanceTabBarViewModel viewModel)
            {
                viewModel.ReopenRecentClosedCommand.Execute(item);
            }
        }
    }
}
