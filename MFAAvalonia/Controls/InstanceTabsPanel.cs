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

    public InstanceTabsPanel(InstanceTabsControl tabsControl)
    {
        _tabsControl = tabsControl;
        // 当控件尺寸变化时（如窗口缩放），重新测量标签宽度
        _tabsControl.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
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
        _itemWidth = GetAvailableWidth(availableSize);

        double height = 0;
        double width = 0;
        bool isFirst = true;

        foreach (var tabItem in Children)
        {
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

        foreach (var tabItem in Children)
        {
            tabItem.Measure(new Size(_itemWidth, availableSize.Height));
            width += _itemWidth;
            height = Max(tabItem.DesiredSize.Height, height);

            if (!isFirst)
                width += ItemOffset;

            isFirst = false;
        }

        // 拖拽时面板需要扩展以容纳被拖拽标签的位置（带动 + 按钮往右移动）
        double dragRight = draggedItem.X + _itemWidth;
        width = Max(width, dragRight);

        // 使用有效宽度约束（不能超出控件可用空间）
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

        foreach (Control? child in Children)
        {
            if (child is not DragTabItem tabItem)
                continue;

            tabItem.ZIndex = tabItem.IsSelected ? int.MaxValue : --z;
            tabItem.LogicalIndex = logicalIndex++;

            SetLocation(tabItem, x, _itemWidth);
            _itemsLocations.Add(tabItem, GetLocationInfo(tabItem));

            x += _itemWidth + ItemOffset;
        }

        return finalSize;
    }

    private Size DragArrangeImpl(DragTabItem dragItem, Size finalSize)
    {
        var dragItemsLocations = GetLocations(Children.OfType<DragTabItem>(), dragItem);

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
        var dragItemsLocations = GetLocations(Children.OfType<DragTabItem>(), dragItem);

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

    private double GetEffectiveWidth(Size availableSize)
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
        }

        return Max(0, width);
    }

    private double GetAvailableWidth(Size availableSize)
    {
        int tabsCount = Children.Count;

        if (tabsCount == 0)
            return 0;

        double effectiveWidth = GetEffectiveWidth(availableSize);

        // 首次测量或无约束时，使用最大宽度
        if (effectiveWidth < 0)
            return ItemWidth;

        // 所有标签以最大宽度排列时的总宽度
        double naturalTotal = tabsCount * ItemWidth + ItemOffset * (tabsCount - 1);

        // 如果自然宽度未超出可用空间，保持最大宽度（添加按钮跟随标签往右移动）
        if (naturalTotal <= effectiveWidth)
            return ItemWidth;

        // 超出时才缩放标签以适应空间
        double itemWidth = effectiveWidth / tabsCount - ItemOffset * (tabsCount - 1) / tabsCount;

        return Min(ItemWidth, Max(40, itemWidth)); // 最小 40px 防止标签太窄
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

    private DragTabItem? GetDragItem() => (DragTabItem?)Children.FirstOrDefault(c => c is DragTabItem
    {
        IsDragging: true
    });

    private readonly record struct LocationInfo(DragTabItem Item, double Start, double Mid, double End);
}
