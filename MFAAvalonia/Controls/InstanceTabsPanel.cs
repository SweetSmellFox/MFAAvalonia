using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using static System.Math;

namespace MFAAvalonia.Controls;

public class InstanceTabsPanel : Panel
{
    private readonly InstanceTabsControl _tabsControl;
    private readonly Dictionary<DragTabItem, LocationInfo> _itemsLocations = new();
    private double _itemWidth;
    private readonly Dictionary<DragTabItem, double> _activeStoryboardTargetLocations = new();
    private DragTabItem? _dragItem;

    public event Action? DragCompleted;

    /// <summary>
    /// 为 + 按钮预留的宽度（在 StackPanel 无限宽度环境下使用）
    /// </summary>
    private const double AddButtonReservedWidth = 48;

    /// <summary>
    /// 溢出按钮预留宽度（26 按钮 + 8 边距）
    /// </summary>
    private const double OverflowButtonReservedWidth = 34;

    /// <summary>
    /// 标签最小可读宽度阈值，低于此值时触发溢出折叠
    /// </summary>
    private const double MinReadableWidth = 80;

    private int _overflowCount;
    private int _visibleStart;
    private int _visibleEnd; // inclusive

    /// <summary>
    /// 溢出（被隐藏）的标签数量
    /// </summary>
    public int OverflowCount => _overflowCount;

    /// <summary>
    /// 溢出数量变化时触发
    /// </summary>
    public event Action<int>? OverflowCountChanged;

    public InstanceTabsPanel(InstanceTabsControl tabsControl)
    {
        _tabsControl = tabsControl;
        // 当控件尺寸变化或选中标签变化时，重新测量标签宽度
        _tabsControl.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty || e.Property == TabControl.SelectedIndexProperty)
                InvalidateMeasure();
        };
    }

    public double ItemWidth { get; internal set; }
    public double ItemOffset { get; internal set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var draggedItem = GetDragItem();

        return draggedItem is not null
            ? DragMeasureImpl(draggedItem, availableSize)
            : MeasureImpl(availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var draggedItem = GetDragItem();

        if (_dragItem is not null && draggedItem is null)
        {
            var oldDragItem = _dragItem;
            _dragItem = null;

            return DragCompletedArrangeImpl(oldDragItem, finalSize);
        }

        _dragItem = draggedItem;

        return draggedItem is not null
            ? DragArrangeImpl(draggedItem, finalSize)
            : ArrangeImpl(finalSize);
    }

    private Size MeasureImpl(Size availableSize)
    {
        int totalCount = Children.Count;
        var (itemWidth, visibleCount) = CalculateLayout(availableSize);
        _itemWidth = itemWidth;

        UpdateVisibleRange(visibleCount, totalCount);

        int overflow = totalCount - visibleCount;
        if (_overflowCount != overflow)
        {
            _overflowCount = overflow;
            OverflowCountChanged?.Invoke(overflow);
        }

        double height = 0;
        double width = 0;
        bool isFirst = true;

        for (int i = 0; i < totalCount; i++)
        {
            var tabItem = Children[i];
            bool visible = i >= _visibleStart && i <= _visibleEnd;

            if (tabItem is DragTabItem dt)
                dt.IsVisible = visible;

            if (!visible) continue;

            tabItem.Measure(new Size(_itemWidth, availableSize.Height));
            width += _itemWidth;
            height = Max(tabItem.DesiredSize.Height, height);

            if (!isFirst)
                width += ItemOffset;

            isFirst = false;
        }

        return new Size(width, height);
    }

    private Size DragMeasureImpl(DragTabItem draggedItem, Size availableSize)
    {
        double height = 0;
        double width = 0;
        bool isFirst = true;

        foreach (var tabItem in VisibleDragTabItems())
        {
            tabItem.Measure(new Size(_itemWidth, availableSize.Height));
            width += _itemWidth;
            height = Max(tabItem.DesiredSize.Height, height);

            if (!isFirst)
                width += ItemOffset;

            isFirst = false;
        }

        double dragRight = draggedItem.X + _itemWidth;
        width = Max(width, dragRight);

        double effectiveWidth = GetEffectiveWidth(availableSize);
        if (effectiveWidth > 0)
            width = Min(width, effectiveWidth);

        return new Size(width, height);
    }

    private Size ArrangeImpl(Size finalSize)
    {
        double x = 0;
        int z = int.MaxValue - 10;
        int logicalIndex = 0;

        _itemsLocations.Clear();

        for (int i = 0; i < Children.Count; i++)
        {
            if (Children[i] is not DragTabItem tabItem)
                continue;

            tabItem.LogicalIndex = logicalIndex++;

            if (!tabItem.IsVisible) continue;

            tabItem.ZIndex = tabItem.IsSelected ? int.MaxValue : --z;

            SetLocation(tabItem, x, _itemWidth);
            _itemsLocations.Add(tabItem, GetLocationInfo(tabItem));

            x += _itemWidth + ItemOffset;
        }

        return finalSize;
    }

    private Size DragArrangeImpl(DragTabItem dragItem, Size finalSize)
    {
        var dragItemsLocations = GetLocations(VisibleDragTabItems(), dragItem);

        // 用控件实际可用宽度作为拖拽上限（而非面板当前宽度），允许标签带动 + 按钮往右移动
        double effectiveWidth = GetEffectiveWidth(new Size(double.PositiveInfinity, finalSize.Height));
        double maxX = (effectiveWidth > 0 ? effectiveWidth : finalSize.Width) - _itemWidth;

        double currentCoord = 0.0;

        foreach (var location in dragItemsLocations)
        {
            var item = location.Item;

            if (!Equals(item, dragItem))
            {
                SendToLocation(item, currentCoord, _itemWidth);
            }
            else
            {
                if (dragItem.X > maxX) dragItem.X = maxX;
                if (dragItem.X < 0) dragItem.X = 0;

                SetLocation(dragItem, dragItem.X, _itemWidth);
            }

            currentCoord += _itemWidth + ItemOffset;
        }

        return finalSize;
    }

    private Size DragCompletedArrangeImpl(DragTabItem dragItem, Size finalSize)
    {
        var dragItemsLocations = GetLocations(VisibleDragTabItems(), dragItem);

        double currentCoord = 0.0;
        int z = int.MaxValue - 10;
        int logicalIndex = 0;

        foreach (var location in dragItemsLocations)
        {
            var item = location.Item;

            SetLocation(item, currentCoord, _itemWidth);
            currentCoord += _itemWidth + ItemOffset;
            item.ZIndex = --z;
            item.LogicalIndex = logicalIndex++;
        }

        dragItem.ZIndex = int.MaxValue;

        DragCompleted?.Invoke();

        return finalSize;
    }

    private double GetEffectiveWidth(Size availableSize, bool includeOverflow = false)
    {
        double width = availableSize.Width;

        // StackPanel 给子控件无限宽度，改用控件实际渲染宽度
        if (double.IsInfinity(width) || double.IsNaN(width))
        {
            width = _tabsControl.Bounds.Width;
            if (width <= 0)
                return -1; // 首次测量，尚无实际宽度
            // 预留 + 按钮空间
            width -= AddButtonReservedWidth;
            // 溢出时额外预留溢出按钮空间
            if (includeOverflow)
                width -= OverflowButtonReservedWidth;
        }

        return Max(0, width);
    }

    /// <summary>
    /// 计算标签宽度和可见数量。当缩放到 MinReadableWidth 以下时触发溢出。
    /// </summary>
    private (double itemWidth, int visibleCount) CalculateLayout(Size availableSize)
    {
        int totalCount = Children.Count;
        if (totalCount == 0) return (0, 0);

        double effectiveWidth = GetEffectiveWidth(availableSize);
        if (effectiveWidth < 0) return (ItemWidth, totalCount);

        // 所有标签以最大宽度排列时的总宽度
        double naturalTotal = totalCount * ItemWidth + ItemOffset * (totalCount - 1);
        if (naturalTotal <= effectiveWidth)
            return (ItemWidth, totalCount);

        // 尝试缩放
        double itemWidth = effectiveWidth / totalCount - ItemOffset * (totalCount - 1) / totalCount;
        if (itemWidth >= MinReadableWidth)
            return (Min(ItemWidth, Max(MinReadableWidth, itemWidth)), totalCount);

        // 需要溢出：用含溢出按钮预留的宽度重新计算
        double overflowWidth = GetEffectiveWidth(availableSize, true);
        if (overflowWidth <= 0) return (MinReadableWidth, 1);

        int maxVisible = Max(1, (int)((overflowWidth + ItemOffset) / (MinReadableWidth + ItemOffset)));
        maxVisible = Min(maxVisible, totalCount);

        double visibleWidth = overflowWidth / maxVisible - ItemOffset * (maxVisible - 1) / maxVisible;
        return (Min(ItemWidth, Max(MinReadableWidth, visibleWidth)), maxVisible);
    }

    /// <summary>
    /// 以激活标签为中心，计算可见标签的起止索引
    /// </summary>
    private void UpdateVisibleRange(int visibleCount, int totalCount)
    {
        if (visibleCount >= totalCount)
        {
            _visibleStart = 0;
            _visibleEnd = totalCount - 1;
            return;
        }

        int activeIndex = _tabsControl.SelectedIndex;
        if (activeIndex < 0) activeIndex = 0;
        if (activeIndex >= totalCount) activeIndex = totalCount - 1;

        int half = (visibleCount - 1) / 2;
        int start = activeIndex - half;
        int end = start + visibleCount - 1;

        if (start < 0) { start = 0; end = visibleCount - 1; }
        if (end >= totalCount) { end = totalCount - 1; start = end - visibleCount + 1; }

        _visibleStart = Max(0, start);
        _visibleEnd = Min(totalCount - 1, end);
    }

    private IEnumerable<LocationInfo> GetLocations(IEnumerable<DragTabItem> allItems, DragTabItem dragItem)
    {
        double OrderSelector(LocationInfo loc)
        {
            if (Equals(loc.Item, dragItem))
            {
                var dragItemInfo = _itemsLocations[dragItem];
                return loc.Start > dragItemInfo.Start ? loc.End : loc.Start;
            }

            return _itemsLocations[loc.Item].Mid;
        }

        var currentLocations = allItems
            .Select(GetLocationInfo)
            .OrderBy(OrderSelector);

        return currentLocations;
    }

    private async void SendToLocation(DragTabItem item, double location, double width)
    {
        bool itemIsAnimating = _activeStoryboardTargetLocations.TryGetValue(item, out double activeTarget);

        if (itemIsAnimating)
        {
            SetLocation(item, item.X, width);
            return;
        }

        if (Abs(item.X - location) < 1.0 || itemIsAnimating && Abs(activeTarget - location) < 1.0)
        {
            return;
        }

        _activeStoryboardTargetLocations[item] = location;

        const int animDuration = 200;

        var animation = new Animation
        {
            Easing = new CubicEaseOut(),
            Duration = TimeSpan.FromMilliseconds(animDuration),
            PlaybackDirection = PlaybackDirection.Normal,
            FillMode = FillMode.None,
            Children =
            {
                new KeyFrame
                {
                    KeyTime = TimeSpan.FromMilliseconds(animDuration),
                    Setters =
                    {
                        new Setter(DragTabItem.XProperty, location),
                    }
                }
            }
        };

        await animation.RunAsync(item);

        SetLocation(item, location, width);

        _activeStoryboardTargetLocations.Remove(item);
    }

    private static void SetLocation(DragTabItem dragTabItem, double x, double width)
    {
        const double y = 0;

        dragTabItem.X = x;
        dragTabItem.Y = y;

        dragTabItem.Arrange(new Rect(new Point(x, y), new Size(width, dragTabItem.DesiredSize.Height)));
    }

    private LocationInfo GetLocationInfo(DragTabItem item)
    {
        double size = item.Bounds.Width;

        if (!_activeStoryboardTargetLocations.TryGetValue(item, out double startLocation))
            startLocation = item.X;

        double midLocation = startLocation + size / 2;
        double endLocation = startLocation + size;

        return new LocationInfo(item, startLocation, midLocation, endLocation);
    }
    private IEnumerable<DragTabItem> VisibleDragTabItems() =>
        Children.OfType<DragTabItem>().Where(dt => dt.IsVisible);

    private DragTabItem? GetDragItem() => (DragTabItem?)Children.FirstOrDefault(c => c is DragTabItem
    {
        IsDragging: true
    });

    private readonly record struct LocationInfo(DragTabItem Item, double Start, double Mid, double End);
}
