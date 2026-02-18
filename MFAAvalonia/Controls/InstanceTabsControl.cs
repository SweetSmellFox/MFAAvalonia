using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using MFAAvalonia.Controls.Events;

namespace MFAAvalonia.Controls;

public class InstanceTabsControl : TabControl
{
    private const double DefaultTabWidth = 140;

    private readonly InstanceTabsPanel _tabsPanel;
    private DragTabItem? _draggedItem;
    private DragTabItem? _hoveredTab;
    private bool _dragging;
    private ICommand _addItemCommand;
    private ICommand _closeItemCommand;
    private Border? _tabBarBackground;
    private bool _clipDirty = true;

    public static readonly StyledProperty<double> AdjacentHeaderItemOffsetProperty =
        AvaloniaProperty.Register<InstanceTabsControl, double>(nameof(AdjacentHeaderItemOffset), defaultValue: 0);

    public static readonly StyledProperty<double> TabItemWidthProperty =
        AvaloniaProperty.Register<InstanceTabsControl, double>(nameof(TabItemWidth), defaultValue: DefaultTabWidth);

    public static readonly StyledProperty<bool> ShowDefaultAddButtonProperty =
        AvaloniaProperty.Register<InstanceTabsControl, bool>(nameof(ShowDefaultAddButton), defaultValue: true);

    public static readonly DirectProperty<InstanceTabsControl, ICommand> AddItemCommandProperty =
        AvaloniaProperty.RegisterDirect<InstanceTabsControl, ICommand>(
            nameof(AddItemCommand),
            o => o.AddItemCommand,
            (o, v) => o.AddItemCommand = v);

    public static readonly DirectProperty<InstanceTabsControl, ICommand> CloseItemCommandProperty =
        AvaloniaProperty.RegisterDirect<InstanceTabsControl, ICommand>(
            nameof(CloseItemCommand),
            o => o.CloseItemCommand,
            (o, v) => o.CloseItemCommand = v);

    public InstanceTabsControl()
    {
        AddHandler(DragTabItem.DragStarted, ItemDragStarted, handledEventsToo: true);
        AddHandler(DragTabItem.DragDelta, ItemDragDelta);
        AddHandler(DragTabItem.DragCompleted, ItemDragCompleted, handledEventsToo: true);

        _tabsPanel = new InstanceTabsPanel(this)
        {
            ItemWidth = TabItemWidth,
            ItemOffset = AdjacentHeaderItemOffset
        };

        _tabsPanel.DragCompleted += TabsPanelOnDragCompleted;

        ItemsPanel = new FuncTemplate<Panel?>(() => _tabsPanel);

        _addItemCommand = new SimpleActionCommand(() => { });
        _closeItemCommand = new SimpleActionCommand(() => { });
    }

    public double AdjacentHeaderItemOffset
    {
        get => GetValue(AdjacentHeaderItemOffsetProperty);
        set => SetValue(AdjacentHeaderItemOffsetProperty, value);
    }

    public double TabItemWidth
    {
        get => GetValue(TabItemWidthProperty);
        set => SetValue(TabItemWidthProperty, value);
    }

    public bool ShowDefaultAddButton
    {
        get => GetValue(ShowDefaultAddButtonProperty);
        set => SetValue(ShowDefaultAddButtonProperty, value);
    }

    public ICommand AddItemCommand
    {
        get => _addItemCommand;
        set => SetAndRaise(AddItemCommandProperty, ref _addItemCommand, value);
    }

    public ICommand CloseItemCommand
    {
        get => _closeItemCommand;
        set => SetAndRaise(CloseItemCommandProperty, ref _closeItemCommand, value);
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) =>
        new DragTabItem();

    private void UpdateAllTabsCanClose()
    {
        var canClose = Items.Count > 1;
        foreach (var tab in DragTabItems())
        {
            tab.CanClose = canClose;
        }
    }

    /// <summary>
    /// 设置外部的 TabBarBackground Border（用于覆盖整个标签栏区域，包括下拉按钮）。
    /// 如果设置了外部 Border，则优先使用它进行 Clip 计算。
    /// </summary>
    public void SetExternalTabBarBackground(Border border)
    {
        _tabBarBackground = border;
        InvalidateClip();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_tabBarBackground == null)
            _tabBarBackground = e.NameScope.Find<Border>("PART_TabBarBackground");
        LayoutUpdated += OnLayoutUpdated;
    }

    private void InvalidateClip() => _clipDirty = true;

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (!_clipDirty) return;
        _clipDirty = false;
        UpdateTabBarBackgroundClip();
        UpdateNonSelectedTabClips();
    }

    /// <summary>
    /// 由 DragTabItem.OnPointerEntered 调用（PointerEntered 是 Direct 事件，不冒泡）。
    /// </summary>
    internal void NotifyTabHovered(DragTabItem tab)
    {
        if (tab == _hoveredTab) return;
        _hoveredTab = tab;
        InvalidateClip();
    }

    /// <summary>
    /// 由 DragTabItem.OnPointerExited 调用。
    /// </summary>
    internal void NotifyTabUnhovered(DragTabItem tab)
    {
        if (tab != _hoveredTab) return;
        _hoveredTab = null;
        InvalidateClip();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemCountProperty)
        {
            Dispatcher.UIThread.Post(UpdateAllTabsCanClose, DispatcherPriority.Loaded);
        }
        else if (change.Property == AdjacentHeaderItemOffsetProperty)
        {
            _tabsPanel.ItemOffset = AdjacentHeaderItemOffset;
        }
        else if (change.Property == TabItemWidthProperty)
        {
            _tabsPanel.ItemWidth = TabItemWidth;
        }
        else if (change.Property == SelectedItemProperty || change.Property == SelectedIndexProperty)
        {
            InvalidateClip();
        }
    }

    /// <summary>
    /// 曲线脚的半径（与 DragTabItem 模板中 ArcSegment Size="5 5" 一致）
    /// </summary>
    private const double CurveRadius = 5;

    /// <summary>
    /// 标签顶部圆角半径（与 DragTabItem CornerRadius="6,6,0,0" 一致）
    /// </summary>
    private const double TabCornerRadius = 6;

    /// <summary>
    /// 创建标签形状的 StreamGeometry（带曲线脚和圆角顶部），
    /// 坐标相对于 referenceControl。
    /// 曲线脚延伸到标签边界之外，主体匹配全宽 Border（CornerRadius="6,6,0,0"）。
    /// </summary>
    private StreamGeometry? CreateTabShapeGeometry(DragTabItem tab, Control referenceControl, double totalHeight, double extraExtend = 0)
    {
        var tabBounds = tab.Bounds;
        var transform = tab.TranslatePoint(new Point(0, 0), referenceControl);
        if (transform == null) return null;

        var L = transform.Value.X - extraExtend;
        var R = L + tabBounds.Width + extraExtend * 2;
        var T = transform.Value.Y;
        var H = totalHeight;
        var cw = CurveRadius;
        var cr = TabCornerRadius;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            // 从左下角开始（曲线脚延伸到标签左边界之外）
            ctx.BeginFigure(new Point(L - cw, H), true);
            // 左侧内凹曲线脚
            ctx.ArcTo(new Point(L, H - cw), new Size(cw, cw), 0, false, SweepDirection.CounterClockwise);
            // 左侧直线上升到左上圆角
            ctx.LineTo(new Point(L, T + cr));
            // 左上圆角（外凸，与 Border CornerRadius 一致）
            ctx.ArcTo(new Point(L + cr, T), new Size(cr, cr), 0, false, SweepDirection.Clockwise);
            // 顶部直线
            ctx.LineTo(new Point(R - cr, T));
            // 右上圆角（外凸）
            ctx.ArcTo(new Point(R, T + cr), new Size(cr, cr), 0, false, SweepDirection.Clockwise);
            // 右侧直线下降到曲线脚
            ctx.LineTo(new Point(R, H - cw));
            // 右侧内凹曲线脚
            ctx.ArcTo(new Point(R + cw, H), new Size(cw, cw), 0, false, SweepDirection.CounterClockwise);
            ctx.EndFigure(true);
        }

        return geo;
    }

    /// <summary>
    /// 更新 TabBarBackground 的 Clip：
    /// - 选中标签区域完全挖掉（透出窗口渐变背景）
    /// - 悬停的非选中标签区域也挖掉（标签自身用 TabBarHoverBackground 填充，比 TabBarBackground 更亮）
    /// </summary>
    internal void UpdateTabBarBackgroundClip()
    {
        if (_tabBarBackground == null) return;

        var totalBounds = _tabBarBackground.Bounds;
        if (totalBounds.Width <= 0 || totalBounds.Height <= 0) return;

        DragTabItem? selectedTab = null;
        if (SelectedItem != null)
        {
            var container = ContainerFromItem(SelectedItem);
            if (container is DragTabItem dt)
                selectedTab = dt;
        }

        if (selectedTab == null)
        {
            _tabBarBackground.Clip = null;
            return;
        }

        var selectedShape = CreateTabShapeGeometry(selectedTab, _tabBarBackground, totalBounds.Height);
        if (selectedShape == null)
        {
            _tabBarBackground.Clip = null;
            return;
        }

        var fullRect = new RectangleGeometry(new Rect(0, 0, totalBounds.Width, totalBounds.Height));
        Geometry clipGeo = new CombinedGeometry(GeometryCombineMode.Exclude, fullRect, selectedShape);

        // hover 的非选中标签也从背景中挖掉（hover 标签自身用 PART_TabShape 填充）
        if (_hoveredTab != null && _hoveredTab != selectedTab)
        {
            var hoveredShape = CreateTabShapeGeometry(_hoveredTab, _tabBarBackground, totalBounds.Height, 1);
            if (hoveredShape != null)
            {
                clipGeo = new CombinedGeometry(GeometryCombineMode.Exclude, clipGeo, hoveredShape);
            }
        }

        _tabBarBackground.Clip = clipGeo;
    }
    /// <summary>
    /// 用选中标签和 hover 标签的曲线形状裁剪其他标签，
    /// 使相邻标签的分隔线被曲线脚区域裁剪掉。
    /// </summary>
    private void UpdateNonSelectedTabClips()
    {
        if (_tabBarBackground == null) return;

        DragTabItem? selectedTab = null;
        if (SelectedItem != null)
        {
            var container = ContainerFromItem(SelectedItem);
            if (container is DragTabItem dt)
                selectedTab = dt;
        }

        var hoveredTab = _hoveredTab != null && _hoveredTab != selectedTab ? _hoveredTab : null;

        foreach (var tab in DragTabItems())
        {
            // 选中标签不裁剪
            if (tab == selectedTab)
            {
                tab.Clip = null;
                continue;
            }

            var tabW = tab.Bounds.Width;
            var tabH = tab.Bounds.Height;
            if (tabW <= 0 || tabH <= 0)
            {
                tab.Clip = null;
                continue;
            }

            var cw = CurveRadius;
            var tabFullRect = new RectangleGeometry(new Rect(-cw, 0, tabW + cw * 2, tabH));
            Geometry? clipGeo = null;

            // 用选中标签的曲线形状裁剪
            if (selectedTab != null)
            {
                var selectedShape = CreateTabShapeGeometry(selectedTab, tab, tabH);
                if (selectedShape != null)
                {
                    clipGeo = new CombinedGeometry(GeometryCombineMode.Exclude, tabFullRect, selectedShape);
                }
            }

            // 用 hover 标签的曲线形状裁剪（hover 标签自身不裁剪）
            if (hoveredTab != null && tab != hoveredTab)
            {
                var hoveredShape = CreateTabShapeGeometry(hoveredTab, tab, tabH, 1);
                if (hoveredShape != null)
                {
                    var baseGeo = clipGeo ?? (Geometry)tabFullRect;
                    clipGeo = new CombinedGeometry(GeometryCombineMode.Exclude, baseGeo, hoveredShape);
                }
            }

            tab.Clip = clipGeo;
        }
    }

    private IEnumerable<DragTabItem> DragTabItems()
    {
        foreach (object item in Items)
        {
            var container = ContainerFromItem(item);
            if (container is DragTabItem dragTabItem)
                yield return dragTabItem;
        }
    }

    private void ItemDragStarted(object? sender, DragTabDragStartedEventArgs e)
    {
        _draggedItem = e.TabItem;
        e.Handled = true;

        _draggedItem.IsSelected = true;

        object? item = ItemFromContainer(_draggedItem);
        if (item != null)
        {
            if (item is TabItem tabItem)
                tabItem.IsSelected = true;
            SelectedItem = item;
        }
    }

    private void ItemDragDelta(object? sender, DragTabDragDeltaEventArgs e)
    {
        if (_draggedItem is null) return;

        if (!_dragging)
        {
            _dragging = true;
            SetDraggingItem(_draggedItem);
        }

        _draggedItem.X += e.DragDeltaEventArgs.Vector.X;
        _draggedItem.Y += e.DragDeltaEventArgs.Vector.Y;

        Dispatcher.UIThread.Post(() =>
        {
            _tabsPanel.InvalidateMeasure();
            InvalidateClip();
        }, DispatcherPriority.Loaded);

        e.Handled = true;
    }

    private void ItemDragCompleted(object? sender, DragTabDragCompletedEventArgs e)
    {
        foreach (var item in DragTabItems())
        {
            item.IsDragging = false;
            item.IsSiblingDragging = false;
        }

        Dispatcher.UIThread.Post(() => _tabsPanel.InvalidateMeasure(), DispatcherPriority.Loaded);

        _dragging = false;
    }

    private void SetDraggingItem(DragTabItem draggedItem)
    {
        foreach (var item in DragTabItems())
        {
            item.IsDragging = false;
            item.IsSiblingDragging = true;
        }

        draggedItem.IsDragging = true;
        draggedItem.IsSiblingDragging = false;
    }

    private void TabsPanelOnDragCompleted()
    {
        MoveTabModelsIfNeeded();
        _draggedItem = null;
    }

    private void MoveTabModelsIfNeeded()
    {
        if (_draggedItem == null) return;

        object? item = ItemFromContainer(_draggedItem);
        if (item == null) return;

        DragTabItem container = _draggedItem;

        if (ItemsSource is IList list)
        {
            if (container.LogicalIndex != list.IndexOf(item))
            {
                list.Remove(item);
                list.Insert(container.LogicalIndex, item);

                SelectedItem = item;

                int i = 0;
                foreach (var dragTabItem in DragTabItems())
                    dragTabItem.LogicalIndex = i++;

                // 拖拽排序后保存顺序
                TabOrderChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// 拖拽排序完成后触发，用于持久化标签顺序
    /// </summary>
    public event Action? TabOrderChanged;

    private class SimpleActionCommand(Action action) : ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action.Invoke();
        public event EventHandler? CanExecuteChanged;
    }
}
