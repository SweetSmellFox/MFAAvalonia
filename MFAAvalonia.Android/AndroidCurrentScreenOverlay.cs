using Android.Content;
using Android.Graphics;
using Android.Hardware.Display;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using MFAAvalonia.Helper;
using System;
using System.Linq;

namespace MFAAvalonia.Android;

/// <summary>
/// System overlay used by current-screen mode.
///
/// Its lifetime follows the proven FloatingX system-overlay model (MIT, Petterpx/FloatingX):
/// attach one stable container and toggle visibility instead of repeatedly removing and
/// recreating the system window. MuMu places each application tab on a separate Android Display,
/// so the stable container is created from a WindowContext for the focused non-MFA Display.
/// </summary>
internal static class AndroidCurrentScreenOverlay
{
    private static readonly object Sync = new();
    private static IWindowManager? _windowManager;
    private static Context? _applicationContext;
    private static Context? _context;
    private static FrameLayout? _container;
    private static View? _content;
    private static WindowManagerLayoutParams? _layout;
    private static TextView? _titleView;
    private static TextView? _detailView;
    private static RunProgressSnapshot _snapshot;
    private static bool _attached;
    private static bool _visible;
    private static bool _panelVisible;
    private static int _attachedDisplayId = -1;
    private static bool? _lastHiddenOnMfa;
    private static int _lastFocusedDisplayId = -1;
    private static string? _lastFocusedPackage;
    private static float _downRawX;
    private static float _downRawY;
    private static int _downX;
    private static int _downY;
    private static bool _dragged;

    public static void Show(Context context, RunProgressSnapshot snapshot)
        => Update(context, snapshot, MobileRunConfiguration.ActiveDisplayId, false, null);

    public static void Update(Context context, RunProgressSnapshot snapshot,
        int focusedDisplayId, bool hideOnMfa, string? focusedPackage = null)
    {
        lock (Sync)
        {
            _snapshot = snapshot;
            LogFocusChange(focusedDisplayId, focusedPackage, hideOnMfa);

            if (Build.VERSION.SdkInt >= BuildVersionCodes.M && !Settings.CanDrawOverlays(context))
            {
                SetVisible(false);
                global::Android.Util.Log.Warn("MfaCurrentScreenOverlay",
                    $"Overlay permission is missing for {context.PackageName}.");
                return;
            }

            if (hideOnMfa)
            {
                SetVisible(false);
                return;
            }

            var targetDisplayId = focusedDisplayId >= 0
                ? focusedDisplayId
                : MobileRunConfiguration.ActiveDisplayId;
            EnsureAttached(context, targetDisplayId);
            UpdatePanelText();
            SetVisible(_attached);
        }
    }

    private static void LogFocusChange(int displayId, string? packageName, bool hidden)
    {
        if (_lastFocusedDisplayId == displayId
            && string.Equals(_lastFocusedPackage, packageName, StringComparison.Ordinal)
            && _lastHiddenOnMfa == hidden)
            return;

        _lastFocusedDisplayId = displayId;
        _lastFocusedPackage = packageName;
        _lastHiddenOnMfa = hidden;
        global::Android.Util.Log.Info("MfaCurrentScreenOverlay",
            $"Focus target: display={displayId}, package={packageName ?? "<unknown>"}, hiddenOnMfa={hidden}.");
    }

    private static void EnsureAttached(Context context, int targetDisplayId)
    {
        if (_attached && _container != null && _windowManager != null
            && _attachedDisplayId == targetDisplayId)
            return;

        if (_attached || _container != null)
        {
            if (_attached && _container != null && _windowManager != null)
            {
                try { _windowManager.RemoveView(_container); } catch { }
            }
            DisposeContainer();
            _windowManager = null;
            _context = null;
        }

        _applicationContext = context.ApplicationContext ?? context;
        _context = CreateOverlayContext(_applicationContext, targetDisplayId);
        if (_context == null)
            return;
        _attachedDisplayId = targetDisplayId;
        _windowManager = _context.GetSystemService(Context.WindowService)?.JavaCast<IWindowManager>();
        if (_windowManager == null)
        {
            global::Android.Util.Log.Error("MfaCurrentScreenOverlay", "WindowManager is unavailable.");
            return;
        }

        _container = new FrameLayout(_context) { Visibility = ViewStates.Invisible };
        _container.SetClipChildren(false);
        _container.SetClipToPadding(false);
        _panelVisible = false;
        SetContent(CreateBall(), CreateBallLayout(), false);

        try
        {
            _windowManager.AddView(_container, _layout);
            _attached = true;
            global::Android.Util.Log.Info("MfaCurrentScreenOverlay",
                $"System overlay attached: windowDisplay={GetContextDisplayId(_context)}, " +
                $"focusDisplay={_lastFocusedDisplayId}.");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("MfaCurrentScreenOverlay",
                $"Unable to attach system overlay: {ex}");
            DisposeContainer();
        }
    }

    private static Context? CreateOverlayContext(Context context, int displayId)
    {
        if (displayId < 0)
        {
            global::Android.Util.Log.Warn("MfaCurrentScreenOverlay",
                "Overlay target display is not available yet.");
            return null;
        }

        try
        {
            var manager = context.GetSystemService(Context.DisplayService)?.JavaCast<DisplayManager>();
            var display = manager?.GetDisplay(displayId);
            if (display == null)
            {
                global::Android.Util.Log.Warn("MfaCurrentScreenOverlay",
                    $"Overlay target display {displayId} does not exist.");
                return null;
            }

            var displayContext = context.CreateDisplayContext(display);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                // A DisplayContext alone is not a visual context on Android 11+. Creating the
                // WindowContext is what binds WindowManager.addView(TYPE_APPLICATION_OVERLAY)
                // to MuMu's game tab instead of MFA's own tab.
                return displayContext.CreateWindowContext(
                    (int)WindowManagerTypes.ApplicationOverlay, null);
            }
            return displayContext;
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("MfaCurrentScreenOverlay",
                $"Unable to create overlay context for display {displayId}: {ex}");
            return null;
        }
    }

    private static int GetContextDisplayId(Context context)
    {
        try
        {
            return Build.VERSION.SdkInt >= BuildVersionCodes.R
                ? context.Display?.DisplayId ?? -1
                : -1;
        }
        catch
        {
            return -1;
        }
    }

    private static TextView CreateBall()
    {
        var ball = new TextView(_context!)
        {
            Text = "MFA\n●",
            Gravity = GravityFlags.Center,
            TextSize = 11,
            Typeface = Typeface.Default
        };
        ball.SetTextColor(Color.White);
        ball.SetPadding(0, 0, 0, 0);
        var background = new global::Android.Graphics.Drawables.GradientDrawable();
        background.SetColor(unchecked((int)0xE6168BD2));
        background.SetShape(global::Android.Graphics.Drawables.ShapeType.Oval);
        ball.Background = background;
        ball.Touch += OnBallTouch;
        return ball;
    }

    private static void OnBallTouch(object? sender, View.TouchEventArgs args)
    {
        if (_layout == null || _windowManager == null || _container == null || args.Event == null)
            return;

        switch (args.Event.ActionMasked)
        {
            case MotionEventActions.Down:
                _downRawX = args.Event.RawX;
                _downRawY = args.Event.RawY;
                _downX = _layout.X;
                _downY = _layout.Y;
                _dragged = false;
                args.Handled = true;
                break;
            case MotionEventActions.Move:
                var dx = args.Event.RawX - _downRawX;
                var dy = args.Event.RawY - _downRawY;
                _dragged |= Math.Abs(dx) > 8 || Math.Abs(dy) > 8;
                // The ball uses END | CENTER_VERTICAL gravity: X is the distance from the right edge.
                _layout.X = Math.Max(0, _downX - (int)dx);
                _layout.Y = _downY + (int)dy;
                try { _windowManager.UpdateViewLayout(_container, _layout); } catch { }
                args.Handled = true;
                break;
            case MotionEventActions.Up:
                if (!_dragged)
                    ShowPanel();
                args.Handled = true;
                break;
        }
    }

    private static void ShowPanel()
    {
        lock (Sync)
        {
            if (!_attached)
                return;
            _panelVisible = true;
            SetContent(CreatePanel(), CreatePanelLayout(), true);
        }
    }

    private static View CreatePanel()
    {
        var density = _context!.Resources?.DisplayMetrics?.Density ?? 1f;
        var panel = new LinearLayout(_context) { Orientation = Orientation.Vertical };
        panel.SetPadding((int)(16 * density), (int)(14 * density),
            (int)(16 * density), (int)(14 * density));
        var background = new global::Android.Graphics.Drawables.GradientDrawable();
        background.SetColor(unchecked((int)0xF5222222));
        background.SetCornerRadius(18 * density);
        panel.Background = background;

        _titleView = Text(string.Empty, 17, Color.White);
        _detailView = Text(string.Empty, 13, Color.LightGray);
        panel.AddView(_titleView);
        panel.AddView(_detailView);
        UpdatePanelText();

        var buttons = new LinearLayout(_context) { Orientation = Orientation.Horizontal };
        var back = Button("返回 MFA");
        back.Click += (_, _) => OpenApp();
        var stop = Button("停止任务");
        stop.Click += (_, _) => PlatformRunProgress.RequestStop?.Invoke();
        var close = Button("收起");
        close.Click += (_, _) =>
        {
            lock (Sync)
            {
                _panelVisible = false;
                SetContent(CreateBall(), CreateBallLayout(), true);
            }
        };
        buttons.AddView(back, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        buttons.AddView(stop, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        buttons.AddView(close, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        panel.AddView(buttons);
        return panel;
    }

    private static void UpdatePanelText()
    {
        if (_titleView == null || _detailView == null)
            return;

        _titleView.Text = "MFA · " +
                          (string.IsNullOrWhiteSpace(_snapshot.State) ? "运行中" : _snapshot.State);
        _detailView.Text = string.Join(" · ", new[]
        {
            _snapshot.Total > 0 ? $"{_snapshot.Completed}/{_snapshot.Total}" : null,
            _snapshot.CurrentTask
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static TextView Text(string? value, float size, Color color)
    {
        var text = new TextView(_context!) { Text = value ?? string.Empty, TextSize = size };
        text.SetTextColor(color);
        return text;
    }

    private static global::Android.Widget.Button Button(string text) => new(_context!)
    {
        Text = text,
        TextSize = 12
    };

    private static WindowManagerLayoutParams CreateBallLayout()
    {
        var density = _context!.Resources?.DisplayMetrics?.Density ?? 1f;
        var size = (int)(56 * density);
        return NewLayout(size, size, GravityFlags.End | GravityFlags.CenterVertical,
            (int)(8 * density), 0);
    }

    private static WindowManagerLayoutParams CreatePanelLayout()
    {
        var metrics = _context!.Resources?.DisplayMetrics;
        var density = metrics?.Density ?? 1f;
        var width = Math.Min((int)(340 * density), (int)((metrics?.WidthPixels ?? 600) * .85));
        return NewLayout(width, ViewGroup.LayoutParams.WrapContent, GravityFlags.Center, 0, 0);
    }

    private static WindowManagerLayoutParams NewLayout(
        int width, int height, GravityFlags gravity, int x, int y)
    {
        var type = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? WindowManagerTypes.ApplicationOverlay
            : WindowManagerTypes.SystemAlert;
        var flags = WindowManagerFlags.NotFocusable
                    | WindowManagerFlags.NotTouchModal
                    | WindowManagerFlags.LayoutNoLimits
                    | WindowManagerFlags.LayoutInScreen;
        return new WindowManagerLayoutParams(width, height, type, flags, Format.Rgba8888)
        {
            Gravity = gravity,
            X = x,
            Y = y
        };
    }

    private static void SetContent(View next, WindowManagerLayoutParams layout, bool updateWindow)
    {
        if (_container == null)
        {
            next.Dispose();
            return;
        }

        _container.RemoveAllViews();
        _content?.Dispose();
        _content = next;
        _layout = layout;
        _titleView = _panelVisible ? _titleView : null;
        _detailView = _panelVisible ? _detailView : null;
        var childHeight = layout.Height == ViewGroup.LayoutParams.WrapContent
            ? ViewGroup.LayoutParams.WrapContent
            : ViewGroup.LayoutParams.MatchParent;
        _container.AddView(next, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, childHeight));

        if (updateWindow && _attached && _windowManager != null)
        {
            try
            {
                _windowManager.UpdateViewLayout(_container, layout);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Warn("MfaCurrentScreenOverlay",
                    $"Unable to update overlay layout: {ex.Message}");
            }
        }
    }

    private static void SetVisible(bool visible)
    {
        if (_container == null || !_attached)
        {
            _visible = false;
            return;
        }
        if (_visible == visible && _container.Visibility == (visible ? ViewStates.Visible : ViewStates.Gone))
            return;

        _visible = visible;
        _container.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;
        global::Android.Util.Log.Info("MfaCurrentScreenOverlay",
            visible ? "System overlay visible." : "System overlay hidden.");
    }

    private static void OpenApp()
    {
        var launch = _context?.PackageManager?.GetLaunchIntentForPackage(_context.PackageName ?? string.Empty);
        launch?.AddFlags(ActivityFlags.NewTask | ActivityFlags.ReorderToFront | ActivityFlags.SingleTop);
        if (launch != null)
            _context!.StartActivity(launch);
    }

    public static void Hide()
    {
        lock (Sync)
        {
            if (_attached && _container != null && _windowManager != null)
            {
                try { _windowManager.RemoveView(_container); } catch { }
            }
            DisposeContainer();
            _windowManager = null;
            _context = null;
            _applicationContext = null;
            _lastHiddenOnMfa = null;
            _lastFocusedDisplayId = -1;
            _lastFocusedPackage = null;
        }
    }

    private static void DisposeContainer()
    {
        _container?.RemoveAllViews();
        _content?.Dispose();
        _container?.Dispose();
        _container = null;
        _content = null;
        _layout = null;
        _titleView = null;
        _detailView = null;
        _attached = false;
        _attachedDisplayId = -1;
        _visible = false;
        _panelVisible = false;
    }
}
