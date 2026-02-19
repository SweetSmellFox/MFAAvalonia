using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;

namespace MFAAvalonia.Controls;

public class LeftPressedThumb : TemplatedControl
{
    public static readonly RoutedEvent<VectorEventArgs> DragStartedEvent =
        RoutedEvent.Register<LeftPressedThumb, VectorEventArgs>(nameof(DragStarted), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<VectorEventArgs> DragDeltaEvent =
        RoutedEvent.Register<LeftPressedThumb, VectorEventArgs>(nameof(DragDelta), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<VectorEventArgs> DragCompletedEvent =
        RoutedEvent.Register<LeftPressedThumb, VectorEventArgs>(nameof(DragCompleted), RoutingStrategies.Bubble);

    private Point? _lastPoint;
    private Point? _pressedPoint;
    private bool _thresholdExceeded;
    private const double DragThreshold = 4.0;

    public event EventHandler<VectorEventArgs>? DragStarted
    {
        add => AddHandler(DragStartedEvent, value);
        remove => RemoveHandler(DragStartedEvent, value);
    }

    public event EventHandler<VectorEventArgs>? DragDelta
    {
        add => AddHandler(DragDeltaEvent, value);
        remove => RemoveHandler(DragDeltaEvent, value);
    }

    public event EventHandler<VectorEventArgs>? DragCompleted
    {
        add => AddHandler(DragCompletedEvent, value);
        remove => RemoveHandler(DragCompletedEvent, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new LeftPressedThumbPeer(this);

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_lastPoint.HasValue)
        {
            var ev = new VectorEventArgs
            {
                RoutedEvent = DragCompletedEvent,
                Vector = _lastPoint.Value,
            };

            _lastPoint = null;
            _pressedPoint = null;
            _thresholdExceeded = false;
            RaiseEvent(ev);
        }

        base.OnPointerCaptureLost(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!IsLeftButtonPressed(e))
            return;

        if (_lastPoint.HasValue)
        {
            // 未超过阈值时，检查累计移动距离
            if (!_thresholdExceeded && _pressedPoint.HasValue)
            {
                var totalDelta = e.GetPosition(this) - _pressedPoint.Value;
                if (Math.Abs(totalDelta.X) < DragThreshold && Math.Abs(totalDelta.Y) < DragThreshold)
                    return;
                _thresholdExceeded = true;
            }

            var ev = new VectorEventArgs
            {
                RoutedEvent = DragDeltaEvent,
                Vector = e.GetPosition(this) - _lastPoint.Value,
            };

            RaiseEvent(ev);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!IsLeftButtonPressed(e))
            return;

        e.Handled = true;
        _lastPoint = e.GetPosition(this);
        _pressedPoint = _lastPoint;
        _thresholdExceeded = false;

        var ev = new VectorEventArgs
        {
            RoutedEvent = DragStartedEvent,
            Vector = (Vector)_lastPoint,
        };

        e.PreventGestureRecognition();
        RaiseEvent(ev);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_lastPoint.HasValue)
        {
            e.Handled = true;
            _lastPoint = null;
            _pressedPoint = null;
            _thresholdExceeded = false;

            var ev = new VectorEventArgs
            {
                RoutedEvent = DragCompletedEvent,
                Vector = e.GetPosition(this),
            };

            RaiseEvent(ev);
        }
    }

    private static bool IsLeftButtonPressed(PointerEventArgs args)
    {
        var point = args.GetCurrentPoint(null);
        return point.Properties.IsLeftButtonPressed;
    }

    private class LeftPressedThumbPeer : ControlAutomationPeer
    {
        public LeftPressedThumbPeer(LeftPressedThumb owner) : base(owner) { }
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Thumb;
        protected override bool IsContentElementCore() => false;
    }
}
