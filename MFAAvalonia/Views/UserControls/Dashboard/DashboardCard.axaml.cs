using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System;

namespace MFAAvalonia.Views.UserControls.Dashboard;

public partial class DashboardCard : ContentControl
{
    public static readonly StyledProperty<string> CardIdProperty =
        AvaloniaProperty.Register<DashboardCard, string>(nameof(CardId), string.Empty);

    public string CardId
    {
        get => GetValue(CardIdProperty);
        set => SetValue(CardIdProperty, value);
    }

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<DashboardCard, string>(nameof(Title), string.Empty);

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly StyledProperty<object?> TitleContentProperty =
        AvaloniaProperty.Register<DashboardCard, object?>(nameof(TitleContent));

    public object? TitleContent
    {
        get => GetValue(TitleContentProperty);
        set => SetValue(TitleContentProperty, value);
    }

    public static readonly StyledProperty<object?> HeaderActionsProperty =
        AvaloniaProperty.Register<DashboardCard, object?>(nameof(HeaderActions));

    public object? HeaderActions
    {
        get => GetValue(HeaderActionsProperty);
        set => SetValue(HeaderActionsProperty, value);
    }


    public static readonly StyledProperty<bool> IsCollapsedProperty =
        AvaloniaProperty.Register<DashboardCard, bool>(nameof(IsCollapsed), false);

    public bool IsCollapsed
    {
        get => GetValue(IsCollapsedProperty);
        set => SetValue(IsCollapsedProperty, value);
    }

    public static readonly StyledProperty<bool> IsMaximizedProperty =
        AvaloniaProperty.Register<DashboardCard, bool>(nameof(IsMaximized), false);

    public bool IsMaximized
    {
        get => GetValue(IsMaximizedProperty);
        set => SetValue(IsMaximizedProperty, value);
    }

    public static readonly StyledProperty<double> CollapsedHeightProperty =
        AvaloniaProperty.Register<DashboardCard, double>(nameof(CollapsedHeight), 55);

    public double CollapsedHeight
    {
        get => GetValue(CollapsedHeightProperty);
        set => SetValue(CollapsedHeightProperty, value);
    }

    public static readonly StyledProperty<bool> IsResizeEnabledProperty =
        AvaloniaProperty.Register<DashboardCard, bool>(nameof(IsResizeEnabled), true);

    public bool IsResizeEnabled
    {
        get => GetValue(IsResizeEnabledProperty);
        set => SetValue(IsResizeEnabledProperty, value);
    }

    public static readonly StyledProperty<bool> IsDragEnabledProperty =
        AvaloniaProperty.Register<DashboardCard, bool>(nameof(IsDragEnabled), true);

    public bool IsDragEnabled
    {
        get => GetValue(IsDragEnabledProperty);
        set => SetValue(IsDragEnabledProperty, value);
    }

    public static readonly StyledProperty<int> GridColumnProperty =
        AvaloniaProperty.Register<DashboardCard, int>(nameof(GridColumn), 0);

    public int GridColumn
    {
        get => GetValue(GridColumnProperty);
        set => SetValue(GridColumnProperty, value);
    }

    public static readonly StyledProperty<int> GridRowProperty =
        AvaloniaProperty.Register<DashboardCard, int>(nameof(GridRow), 0);

    public int GridRow
    {
        get => GetValue(GridRowProperty);
        set => SetValue(GridRowProperty, value);
    }

    public static readonly StyledProperty<int> GridColumnSpanProperty =
        AvaloniaProperty.Register<DashboardCard, int>(nameof(GridColumnSpan), 1);

    public int GridColumnSpan
    {
        get => GetValue(GridColumnSpanProperty);
        set => SetValue(GridColumnSpanProperty, value);
    }

    public static readonly StyledProperty<int> GridRowSpanProperty =
        AvaloniaProperty.Register<DashboardCard, int>(nameof(GridRowSpan), 1);

    public int GridRowSpan
    {
        get => GetValue(GridRowSpanProperty);
        set => SetValue(GridRowSpanProperty, value);
    }

    public static readonly StyledProperty<int> ExpandedRowSpanProperty =
        AvaloniaProperty.Register<DashboardCard, int>(nameof(ExpandedRowSpan), 1);

    public int ExpandedRowSpan
    {
        get => GetValue(ExpandedRowSpanProperty);
        set => SetValue(ExpandedRowSpanProperty, value);
    }

    public static readonly StyledProperty<int> ExpandedColumnSpanProperty =
        AvaloniaProperty.Register<DashboardCard, int>(nameof(ExpandedColumnSpan), 1);

    public int ExpandedColumnSpan
    {
        get => GetValue(ExpandedColumnSpanProperty);
        set => SetValue(ExpandedColumnSpanProperty, value);
    }

    public event EventHandler<bool>? CollapseStateChanged;
    public event EventHandler<bool>? MaximizeStateChanged;
    public event EventHandler<PointerPressedEventArgs>? DragStarted;
    public event EventHandler<PointerEventArgs>? DragMoved;
    public event EventHandler<PointerReleasedEventArgs>? DragEnded;
    public event EventHandler<DashboardCardResizeEventArgs>? Resized;
    public event EventHandler<DashboardCardResizeEventArgs>? ResizeStarted;
    public event EventHandler<DashboardCardResizeEventArgs>? ResizeCompleted;

    private Border? _dragHandle;
    private bool _isDragging;

    public DashboardCard()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        UpdateCollapseState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsCollapsedProperty)
        {
            UpdateCollapseState();
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        SetupEventHandlers(e);
    }

    private void SetupEventHandlers(TemplateAppliedEventArgs e)
    {
        if (_dragHandle != null)
        {
            _dragHandle.PointerPressed -= OnHeaderPointerPressed;
            _dragHandle.PointerMoved -= OnHeaderPointerMoved;
            _dragHandle.PointerReleased -= OnHeaderPointerReleased;
        }

        _dragHandle = e.NameScope.Find<Border>("DragHandle");
        if (_dragHandle == null)
        {
            return;
        }

        _dragHandle.PointerPressed += OnHeaderPointerPressed;
        _dragHandle.PointerMoved += OnHeaderPointerMoved;
        _dragHandle.PointerReleased += OnHeaderPointerReleased;
    }

    private void UpdateCollapseState()
    {
        Tag = IsCollapsed ? "Collapsed" : "Expanded";
        ClearValue(HeightProperty);
    }
    
    private void OnCollapseButtonClick(object? sender, RoutedEventArgs e)
    {
        IsCollapsed = !IsCollapsed;
        CollapseStateChanged?.Invoke(this, IsCollapsed);
    }

    private void OnMaximizeButtonClick(object? sender, RoutedEventArgs e)
    {
        IsMaximized = !IsMaximized;
        MaximizeStateChanged?.Invoke(this, IsMaximized);
    }
    
    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsDragEnabled || _dragHandle == null)
        {
            return;
        }

        var point = e.GetCurrentPoint(_dragHandle);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isDragging = true;
        e.Pointer.Capture(_dragHandle);
        DragStarted?.Invoke(this, e);
    }

    private void OnHeaderPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || !IsDragEnabled)
        {
            return;
        }

        DragMoved?.Invoke(this, e);
    }

    private void OnHeaderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        e.Pointer.Capture(null);
        DragEnded?.Invoke(this, e);
    }

    private void OnResizeDragDelta(object? sender, VectorEventArgs e)
    {
        if (!IsResizeEnabled)
        {
            return;
        }

        if (sender is not Thumb thumb || thumb.Tag is not string direction)
        {
            return;
        }

        Resized?.Invoke(this, new DashboardCardResizeEventArgs
        {
            Direction = direction,
            HorizontalChange = e.Vector.X,
            VerticalChange = e.Vector.Y,
            CardId = CardId,
            CurrentColumn = GridColumn,
            CurrentRow = GridRow,
            CurrentColumnSpan = GridColumnSpan,
            CurrentRowSpan = IsCollapsed ? 1 : GridRowSpan
        });
    }

    private void OnResizeDragStarted(object? sender, VectorEventArgs e)
    {
        if (!IsResizeEnabled)
        {
            return;
        }

        if (sender is not Thumb thumb || thumb.Tag is not string direction)
        {
            return;
        }

        ResizeStarted?.Invoke(this, new DashboardCardResizeEventArgs
        {
            Direction = direction,
            HorizontalChange = e.Vector.X,
            VerticalChange = e.Vector.Y,
            CardId = CardId,
            CurrentColumn = GridColumn,
            CurrentRow = GridRow,
            CurrentColumnSpan = GridColumnSpan,
            CurrentRowSpan = IsCollapsed ? 1 : GridRowSpan
        });
    }

    private void OnResizeDragCompleted(object? sender, VectorEventArgs e)
    {
        if (!IsResizeEnabled)
        {
            return;
        }

        if (sender is not Thumb thumb || thumb.Tag is not string direction)
        {
            return;
        }

        ResizeCompleted?.Invoke(this, new DashboardCardResizeEventArgs
        {
            Direction = direction,
            HorizontalChange = e.Vector.X,
            VerticalChange = e.Vector.Y,
            CardId = CardId,
            CurrentColumn = GridColumn,
            CurrentRow = GridRow,
            CurrentColumnSpan = GridColumnSpan,
            CurrentRowSpan = IsCollapsed ? 1 : GridRowSpan
        });
    }
}

public sealed class DashboardCardResizeEventArgs : EventArgs
{
    public string CardId { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public double HorizontalChange { get; init; }
    public double VerticalChange { get; init; }
    public int CurrentColumn { get; init; }
    public int CurrentRow { get; init; }
    public int CurrentColumnSpan { get; init; }
    public int CurrentRowSpan { get; init; }
}
