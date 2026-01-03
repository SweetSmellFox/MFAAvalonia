using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Specialized;
using System.Linq;

namespace MFAAvalonia.Extensions;

public static class ScrollViewerExtensions
{
    // 滚动方向控制 - 支持任意 Control（包括 ListBox 等）
    public static readonly AttachedProperty<PanningMode> PanningModeProperty =
        AvaloniaProperty.RegisterAttached<Control, PanningMode>(
            "PanningMode", typeof(ScrollViewerExtensions), PanningMode.Both);

    // 自动滚动控制 - 支持任意 Control（包括 ListBox 等）
    public static readonly AttachedProperty<bool> AutoScrollProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "AutoScroll", typeof(ScrollViewerExtensions), false);

    static ScrollViewerExtensions()
    {
        PanningModeProperty.Changed.AddClassHandler<Control>(OnPanningModeChanged);
        AutoScrollProperty.Changed.AddClassHandler<Control>(OnAutoScrollChanged);
    }

    #region 属性设置器

    public static void SetPanningMode(Control element, PanningMode value) =>
        element.SetValue(PanningModeProperty, value);

    public static PanningMode GetPanningMode(Control element) =>
        element.GetValue(PanningModeProperty);

    public static void SetAutoScroll(Control element, bool value) =>
        element.SetValue(AutoScrollProperty, value);

    public static bool GetAutoScroll(Control element) =>
        element.GetValue(AutoScrollProperty);

    #endregion

    #region 辅助方法

    /// <summary>
    /// 从控件中获取 ScrollViewer（支持 ScrollViewer 本身或包含 ScrollViewer 的控件如 ListBox）
    /// </summary>
    private static ScrollViewer? GetScrollViewer(Control control)
    {
        if (control is ScrollViewer sv)
            return sv;

        // 对于 ListBox、DataGrid 等控件，查找内部的 ScrollViewer
        return control.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
    }

    /// <summary>
    /// 延迟获取 ScrollViewer（等待控件模板应用并添加到可视树后）
    /// </summary>
    private static void WithScrollViewer(Control control, Action<ScrollViewer> action)
    {
        var scrollViewer = GetScrollViewer(control);
        if (scrollViewer != null)
        {
            action(scrollViewer);
            return;
        }

        // 需要同时等待模板应用和添加到可视树
        // 因为 GetVisualDescendants() 只有在控件添加到可视树后才能工作

        void TryGetScrollViewer()
        {
            var sv = GetScrollViewer(control);
            if (sv != null)
            {
                action(sv);
            }
        }

        void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            control.AttachedToVisualTree -= OnAttachedToVisualTree;
            // 使用 Post 确保模板已完全应用
            Dispatcher.UIThread.Post(TryGetScrollViewer, DispatcherPriority.Loaded);
        }

        if (control is TemplatedControl templatedControl)
        {
            void OnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
            {
                templatedControl.TemplateApplied -= OnTemplateApplied;
                // 模板应用后，检查是否已在可视树中
                if (control.IsAttachedToVisualTree())
                {
                    Dispatcher.UIThread.Post(TryGetScrollViewer, DispatcherPriority.Loaded);
                }
                else
                {
                    // 还没有添加到可视树，等待添加
                    control.AttachedToVisualTree += OnAttachedToVisualTree;
                }
            }

            templatedControl.TemplateApplied += OnTemplateApplied;
        }
        else
        {
            // 非模板控件，只等待添加到可视树
            control.AttachedToVisualTree += OnAttachedToVisualTree;
        }
    }

    #endregion

    #region 逻辑处理

    private static void OnPanningModeChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        WithScrollViewer(control, scrollViewer =>
        {
            var mode = (PanningMode)(args.NewValue ?? PanningMode.Both);

            scrollViewer.HorizontalScrollBarVisibility = mode switch
            {
                PanningMode.VerticalOnly => ScrollBarVisibility.Disabled,
                PanningMode.HorizontalOnly => ScrollBarVisibility.Auto,
                _ => ScrollBarVisibility.Auto
            };

            scrollViewer.VerticalScrollBarVisibility = mode switch
            {
                PanningMode.HorizontalOnly => ScrollBarVisibility.Disabled,
                PanningMode.VerticalOnly => ScrollBarVisibility.Auto,
                _ => ScrollBarVisibility.Auto
            };
        });
    }

    private static void OnAutoScrollChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        var alwaysScrollToEnd = args.NewValue is true;

        if (control is ScrollViewer scrollViewer)
        {
            SetupScrollViewerAutoScroll(scrollViewer, alwaysScrollToEnd);
        }
        else if (control is ListBox listBox)
        {
            SetupListBoxAutoScroll(listBox, alwaysScrollToEnd);
        }
        else
        {
            // 尝试查找内部的 ScrollViewer
            WithScrollViewer(control, sv => SetupScrollViewerAutoScroll(sv, alwaysScrollToEnd));
        }
    }

    private static void SetupScrollViewerAutoScroll(ScrollViewer scrollViewer, bool alwaysScrollToEnd)
    {
        // 获取或创建状态对象
        var state = scrollViewer.Tag as AutoScrollState;
        if (alwaysScrollToEnd)
        {
            if (state == null)
            {
                state = new AutoScrollState();
                scrollViewer.Tag = state;
            }

            // 初始状态：假设在底部
            state.ShouldAutoScroll = true;

            // 初始滚动到底部
            scrollViewer.ScrollToEnd();

            // 移除旧的处理器（如果有）
            scrollViewer.ScrollChanged -= OnScrollChanged;
            // 添加新的处理器
            scrollViewer.ScrollChanged += OnScrollChanged;
        }
        else
        {
            scrollViewer.ScrollChanged -= OnScrollChanged;
            if (state != null)
            {
                scrollViewer.Tag = null;
            }
        }
    }

    private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroll)
            return;

        var state = scroll.Tag as AutoScrollState;
        if (state == null)
            return;

        // 当内容高度没有变化时（用户滚动），检查是否在底部来更新自动滚动状态
        if (Math.Abs(e.ExtentDelta.Y) < 0.1)
        {
            // 检查是否在底部（允许1像素误差）
            state.ShouldAutoScroll = Math.Abs(scroll.Offset.Y - scroll.ScrollBarMaximum.Y) < 1;
        }

        // 当内容高度变化时（新内容添加或移除），如果应该自动滚动则滚动到底部
        if (state.ShouldAutoScroll && Math.Abs(e.ExtentDelta.Y) >= 0.1)
        {
            scroll.ScrollToEnd();
        }
    }

    private static void SetupListBoxAutoScroll(ListBox listBox, bool alwaysScrollToEnd)
    {
        var state = listBox.Tag as ListBoxAutoScrollState;

        if (alwaysScrollToEnd)
        {
            if (state == null)
            {
                state = new ListBoxAutoScrollState();
                listBox.Tag = state;
            }

            // 移除旧的处理器
            if (state.Handler != null && listBox.Items is INotifyCollectionChanged oldCollection)
            {
                oldCollection.CollectionChanged -= state.Handler;
            }

            // 创建新的处理器
            state.Handler = (sender, arg) =>
            {
                if (arg.Action == NotifyCollectionChangedAction.Add && arg.NewItems != null)
                {
                    // 滚动到新添加的项
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (listBox.Items.Count > 0)
                        {
                            listBox.ScrollIntoView(listBox.Items[^1]!);
                        }
                    }, DispatcherPriority.Background);
                }
            };

            // 添加处理器
            if (listBox.Items is INotifyCollectionChanged collection)
            {
                collection.CollectionChanged += state.Handler;
            }
        }
        else
        {
            if (state?.Handler != null && listBox.Items is INotifyCollectionChanged collection)
            {
                collection.CollectionChanged -= state.Handler;
            }
            listBox.Tag = null;
        }
    }

    #endregion

    /// <summary>
    /// ScrollViewer 自动滚动状态
    /// </summary>
    internal class AutoScrollState
    {
        public bool ShouldAutoScroll { get; set; } = true;
    }

    /// <summary>
    /// ListBox 自动滚动状态
    /// </summary>
    internal class ListBoxAutoScrollState
    {
        public NotifyCollectionChangedEventHandler? Handler { get; set; }
    }

    public enum PanningMode
    {
        VerticalOnly,
        HorizontalOnly,
        Both
    }
}