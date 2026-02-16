using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using MFAAvalonia.Controls.Events;

namespace MFAAvalonia.Controls;

public class DragTabItem : TabItem
{
    private LeftPressedThumb _thumb = null!;
    private Path? _tabShapePath;
    private Button? _closeButton;
    private int _prevZindex;

    private const double CurveRadius = 5;
    private const double TabCornerRadius = 6;
    private const double MinWidthForCloseButton = 80;
    private int _logicalIndex;
    private bool _isDragging;
    private bool _isSiblingDragging;
    private bool _canClose = true;

    public static readonly StyledProperty<double> XProperty =
        AvaloniaProperty.Register<DragTabItem, double>(nameof(X));

    public static readonly StyledProperty<double> YProperty =
        AvaloniaProperty.Register<DragTabItem, double>(nameof(Y));

    public static readonly DirectProperty<DragTabItem, bool> IsDraggingProperty =
        AvaloniaProperty.RegisterDirect<DragTabItem, bool>(nameof(IsDragging),
            o => o.IsDragging, (o, v) => o.IsDragging = v);

    public static readonly DirectProperty<DragTabItem, int> LogicalIndexProperty =
        AvaloniaProperty.RegisterDirect<DragTabItem, int>(nameof(LogicalIndex),
            o => o.LogicalIndex, (o, v) => o.LogicalIndex = v);

    public static readonly DirectProperty<DragTabItem, bool> IsSiblingDraggingProperty =
        AvaloniaProperty.RegisterDirect<DragTabItem, bool>(nameof(IsSiblingDragging),
            o => o.IsSiblingDragging, (o, v) => o.IsSiblingDragging = v);

    public static readonly DirectProperty<DragTabItem, bool> CanCloseProperty =
        AvaloniaProperty.RegisterDirect<DragTabItem, bool>(nameof(CanClose),
            o => o.CanClose, (o, v) => o.CanClose = v, defaultBindingMode: Avalonia.Data.BindingMode.OneWay, enableDataValidation: false);

    public double X
    {
        get => GetValue(XProperty);
        set => SetValue(XProperty, value);
    }

    public double Y
    {
        get => GetValue(YProperty);
        set => SetValue(YProperty, value);
    }

    public int LogicalIndex
    {
        get => _logicalIndex;
        internal set => SetAndRaise(LogicalIndexProperty, ref _logicalIndex, value);
    }

    public bool IsDragging
    {
        get => _isDragging;
        internal set => SetAndRaise(IsDraggingProperty, ref _isDragging, value);
    }

    public bool IsSiblingDragging
    {
        get => _isSiblingDragging;
        internal set => SetAndRaise(IsSiblingDraggingProperty, ref _isSiblingDragging, value);
    }

    public bool CanClose
    {
        get => _canClose;
        internal set
        {
            if (SetAndRaise(CanCloseProperty, ref _canClose, value))
                UpdateCloseButtonVisibility();
        }
    }

    public static readonly RoutedEvent<DragTabDragStartedEventArgs> DragStarted =
        RoutedEvent.Register<DragTabItem, DragTabDragStartedEventArgs>("DragStarted", RoutingStrategies.Bubble);

    public static readonly RoutedEvent<DragTabDragDeltaEventArgs> DragDelta =
        RoutedEvent.Register<DragTabItem, DragTabDragDeltaEventArgs>("DragDelta", RoutingStrategies.Bubble);

    public static readonly RoutedEvent<DragTabDragCompletedEventArgs> DragCompleted =
        RoutedEvent.Register<DragTabItem, DragTabDragCompletedEventArgs>("DragCompleted", RoutingStrategies.Bubble);

    public static readonly RoutedEvent<DragTabDragDeltaEventArgs> PreviewDragDelta =
        RoutedEvent.Register<DragTabItem, DragTabDragDeltaEventArgs>("PreviewDragDelta", RoutingStrategies.Tunnel);

    private const int ZIndexSelected = int.MaxValue;
    private const int ZIndexPointerOver = ZIndexSelected - 1;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        var templateThumb = e.NameScope.Find<LeftPressedThumb>("PART_Thumb");
        if (templateThumb != null)
        {
            _thumb = templateThumb;
            _thumb.DragStarted += ThumbOnDragStarted;
            _thumb.DragDelta += ThumbOnDragDelta;
            _thumb.DragCompleted += ThumbOnDragCompleted;
        }

        _tabShapePath = e.NameScope.Find<Path>("PART_TabShape");

        if (_closeButton != null)
        {
            _closeButton.RemoveHandler(PointerPressedEvent, OnCloseButtonPointerPressed);
        }

        _closeButton = e.NameScope.Find<Button>("PART_CloseButton");
        if (_closeButton != null)
        {
            // 用 Tunnel 策略在事件到达 Button 之前就拦截，确保不会被 Thumb 或 TabItem 抢走
            _closeButton.AddHandler(PointerPressedEvent, OnCloseButtonPointerPressed, RoutingStrategies.Tunnel);
        }

        UpdateTabShapeGeometry();
        UpdateCloseButtonVisibility();
    }

    private void OnCloseButtonPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            return;

        e.Handled = true; // 立即阻止事件继续传播，防止 Thumb 捕获指针

        if (!_canClose) return;

        var tabsControl = this.FindAncestorOfType<InstanceTabsControl>();
        if (tabsControl?.CloseItemCommand is { } cmd)
        {
            var parameter = DataContext;
            if (cmd.CanExecute(parameter))
            {
                cmd.Execute(parameter);
            }
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateTabShapeGeometry();
        UpdateCloseButtonVisibility();
    }

    /// <summary>
    /// 标签太窄时隐藏关闭按钮，避免挤占内容空间。
    /// </summary>
    private void UpdateCloseButtonVisibility()
    {
        if (_closeButton == null) return;
        _closeButton.IsVisible = Bounds.Width >= MinWidthForCloseButton && _canClose;
    }

    /// <summary>
    /// 更新 PART_TabShape 几何形状：全宽标签 + 曲线脚延伸到标签外侧。
    /// </summary>
    private void UpdateTabShapeGeometry()
    {
        if (_tabShapePath == null) return;

        var W = Bounds.Width;
        var H = Bounds.Height;
        if (W <= 0 || H <= 0) return;

        var cw = CurveRadius;
        var cr = TabCornerRadius;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            // 从左下角开始（曲线脚延伸到标签左边界之外）
            ctx.BeginFigure(new Point(-cw, H), true);
            // 左侧内凹曲线脚
            ctx.ArcTo(new Point(0, H - cw), new Size(cw, cw), 0, false, SweepDirection.CounterClockwise);
            // 左侧直线上升
            ctx.LineTo(new Point(0, cr));
            // 左上圆角（外凸）
            ctx.ArcTo(new Point(cr, 0), new Size(cr, cr), 0, false, SweepDirection.Clockwise);
            // 顶部直线
            ctx.LineTo(new Point(W - cr, 0));
            // 右上圆角（外凸）
            ctx.ArcTo(new Point(W, cr), new Size(cr, cr), 0, false, SweepDirection.Clockwise);
            // 右侧直线下降
            ctx.LineTo(new Point(W, H - cw));
            // 右侧内凹曲线脚
            ctx.ArcTo(new Point(W + cw, H), new Size(cw, cw), 0, false, SweepDirection.CounterClockwise);
            ctx.EndFigure(true);
        }

        _tabShapePath.Data = geo;
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);

        if (IsSelected || IsDragging)
            return;

        _prevZindex = ZIndex;
        ZIndex = ZIndexPointerOver;

        // 通知父控件更新 hover clip
        var tabsControl = this.FindAncestorOfType<InstanceTabsControl>();
        tabsControl?.NotifyTabHovered(this);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (IsSelected || IsDragging)
            return;

        ZIndex = _prevZindex;

        // 通知父控件清除 hover clip
        var tabsControl = this.FindAncestorOfType<InstanceTabsControl>();
        tabsControl?.NotifyTabUnhovered(this);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsSelectedProperty)
        {
            if (change.NewValue is true)
            {
                ZIndex = ZIndexSelected;
            }
        }
    }

    private void ThumbOnDragStarted(object? sender, VectorEventArgs args)
    {
        RaiseEvent(new DragTabDragStartedEventArgs(DragStarted, this, args));
    }

    private void ThumbOnDragDelta(object? sender, VectorEventArgs e)
    {
        var previewEventArgs = new DragTabDragDeltaEventArgs(PreviewDragDelta, this, e);
        RaiseEvent(previewEventArgs);
        if (!previewEventArgs.Handled)
        {
            var eventArgs = new DragTabDragDeltaEventArgs(DragDelta, this, e);
            RaiseEvent(eventArgs);
        }
    }

    private void ThumbOnDragCompleted(object? sender, VectorEventArgs e)
    {
        var args = new DragTabDragCompletedEventArgs(DragCompleted, this, e);
        RaiseEvent(args);
    }
}
