using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MFAAvalonia.ViewModels.Other;

namespace MFAAvalonia.Views.UserControls;

public partial class InstanceTabBar : UserControl
{
    private Panel? _hoveredPanel;

    public InstanceTabBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        // Loaded 后用最低优先级确保 ItemsControl 子项已生成
        Dispatcher.UIThread.Post(() =>
        {
            if (GetAllTabPanels().Count == 0)
            {
                // 子项还没生成，再延迟一次
                Dispatcher.UIThread.Post(UpdateAllSeparators, DispatcherPriority.Background);
            }
            else
            {
                UpdateAllSeparators();
            }
        }, DispatcherPriority.Background);
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
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (sender is Border border && border.DataContext is InstanceTabViewModel tab)
        {
            if (DataContext is InstanceTabBarViewModel viewModel)
            {
                viewModel.ActiveTab = tab;
                Avalonia.Threading.Dispatcher.UIThread.Post(UpdateAllSeparators,
                    Avalonia.Threading.DispatcherPriority.Render);
            }
        }
    }

    private void OnDropdownItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (sender is Border border && border.DataContext is InstanceTabViewModel tab)
        {
            if (DataContext is InstanceTabBarViewModel viewModel)
            {
                viewModel.SelectInstanceCommand.Execute(tab);
                Avalonia.Threading.Dispatcher.UIThread.Post(UpdateAllSeparators,
                    Avalonia.Threading.DispatcherPriority.Render);
            }
        }
    }

    private void OnTabPointerEntered(object? sender, PointerEventArgs e)
    {
        _hoveredPanel = sender as Panel;
        UpdateAllSeparators();
    }

    private void OnTabPointerExited(object? sender, PointerEventArgs e)
    {
        _hoveredPanel = null;
        UpdateAllSeparators();
    }

    private List<Panel> GetAllTabPanels()
    {
        var panels = new List<Panel>();
        var stackPanel = this.GetVisualDescendants()
            .OfType<StackPanel>()
            .FirstOrDefault(sp => sp.Name == "TabStackPanel");
        if (stackPanel == null) return panels;

        foreach (var child in stackPanel.Children)
        {
            if (child is ContentPresenter cp && cp.Child is Panel p)
                panels.Add(p);
            else if (child is Panel directPanel)
                panels.Add(directPanel);
        }

        return panels;
    }

    /// <summary>
    /// 统一计算所有分隔线的可见性：
    /// - 激活标签自身的分隔线隐藏
    /// - 激活标签左侧邻居的分隔线隐藏
    /// - 悬停标签自身的分隔线隐藏
    /// - 悬停标签左侧邻居的分隔线隐藏
    /// </summary>
    private static bool IsPanelActive(Panel panel)
    {
        // 优先检查 DataContext，避免绑定延迟导致 Active 类未生效
        if (panel.DataContext is InstanceTabViewModel tab)
            return tab.IsActive;
        return panel.Classes.Contains("Active");
    }

    private void UpdateAllSeparators()
    {
        var panels = GetAllTabPanels();
        if (panels.Count == 0) return;

        for (var i = 0; i < panels.Count; i++)
        {
            var panel = panels[i];
            var isActive = IsPanelActive(panel);
            var isHovered = panel == _hoveredPanel;

            // 检查右侧邻居是否激活或悬停
            var nextIsActive = i + 1 < panels.Count && IsPanelActive(panels[i + 1]);
            var nextIsHovered = i + 1 < panels.Count && panels[i + 1] == _hoveredPanel;

            // 自身激活/悬停 或 右侧邻居激活/悬停 → 隐藏分隔线
            var shouldHide = isActive || isHovered || nextIsActive || nextIsHovered;

            var separator = panel.Children
                .OfType<Border>()
                .FirstOrDefault(b => b.Classes.Contains("TabSeparator"));
            if (separator != null)
                separator.IsVisible = !shouldHide;
        }
    }
}
