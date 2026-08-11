using Android.App;
using Android.Content;
using Android.Hardware.Display;
using Android.OS;
using Android.Views;
using MFAAvalonia.Helper;
using System;
using System.Threading.Tasks;

namespace MFAAvalonia.Android;

public sealed class AndroidVirtualDisplayBackend : Java.Lang.Object, IMobileVirtualDisplayBackend
{
    private readonly Activity _activity;
    private readonly object _sync = new();
    private VirtualDisplay? _virtualDisplay;
    private Surface? _captureSurface;

    public AndroidVirtualDisplayBackend(Activity activity)
    {
        _activity = activity;
    }

    public bool IsRunning => _virtualDisplay != null;
    public int DisplayId => _virtualDisplay?.Display?.DisplayId ?? -1;
    public long CapturedFrameCount
    {
        get
        {
            if (!IsRunning)
                return 0;

            try
            {
                return NativeCaptureInterop.GetFrameCount();
            }
            catch
            {
                return 0;
            }
        }
    }
    public int Width { get; private set; }
    public int Height { get; private set; }

    // The Android preview now renders its HardwareBuffer directly to a SurfaceView.
    // This event remains for the cross-platform backend contract and fallback implementations.
    public event Action<byte[]>? FrameReady;

    public Task<MobileVirtualDisplayResult> StartAsync(string packageName, int width, int height, int dpi)
    {
        lock (_sync)
        {
            if (_virtualDisplay != null)
                return Task.FromResult(MobileVirtualDisplayResult.Succeeded(
                    MobileLocalization.Get("VirtualAlreadyRunning"), DisplayId));

            try
            {
                if (Build.VERSION.SdkInt < BuildVersionCodes.O)
                    return Task.FromResult(MobileVirtualDisplayResult.Failed(
                        MobileLocalization.Get("VirtualRequiresAndroid8")));

                var displayManager = (DisplayManager?)_activity.GetSystemService(Context.DisplayService);
                if (displayManager == null)
                    return Task.FromResult(MobileVirtualDisplayResult.Failed(
                        MobileLocalization.Get("VirtualNoDisplayManager")));

                Width = width;
                Height = height;
                _captureSurface = NativeCaptureInterop.SetupCapturer(width, height);
                var flags = VirtualDisplayFlags.Public
                            | VirtualDisplayFlags.Presentation
                            | VirtualDisplayFlags.OwnContentOnly
                            | (VirtualDisplayFlags)(1 << 6); // SUPPORTS_TOUCH
                _virtualDisplay = displayManager.CreateVirtualDisplay(
                    "MFA_VIRTUAL_DISPLAY", width, height, dpi, _captureSurface, flags);

                if (_virtualDisplay?.Display == null)
                {
                    ReleaseDisplay();
                    return Task.FromResult(MobileVirtualDisplayResult.Failed(
                        MobileLocalization.Get("VirtualCreateFailed")));
                }

                if (!string.IsNullOrWhiteSpace(packageName))
                {
                    var launchResult = LaunchPackageOnDisplay(packageName, DisplayId);
                    if (!launchResult.Success)
                    {
                        ReleaseDisplay();
                        return Task.FromResult(launchResult);
                    }
                }

                global::Android.Util.Log.Info("MfaVirtualDisplay",
                    $"Native virtual display ready: {width}x{height}, display={DisplayId}");
                return Task.FromResult(MobileVirtualDisplayResult.Succeeded(
                    $"{MobileLocalization.Get("VirtualStarted")} Display {DisplayId}", DisplayId));
            }
            catch (Exception ex)
            {
                ReleaseDisplay();
                return Task.FromResult(MobileVirtualDisplayResult.Failed(
                    MobileLocalization.Format("VirtualOperationFailed", ex.Message)));
            }
        }
    }

    public Task<MobileVirtualDisplayResult> StopAsync()
    {
        lock (_sync)
        {
            ReleaseDisplay();
            return Task.FromResult(MobileVirtualDisplayResult.Succeeded(
                MobileLocalization.Get("VirtualStoppedDone"), -1));
        }
    }

    private MobileVirtualDisplayResult LaunchPackageOnDisplay(string packageName, int displayId)
    {
        var intent = _activity.PackageManager?.GetLaunchIntentForPackage(packageName);
        if (intent == null)
            return MobileVirtualDisplayResult.Failed(
                $"{MobileLocalization.Get("VirtualPackageNotFound")}: {packageName}");

        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ExcludeFromRecents);
        var options = ActivityOptions.MakeBasic();
        if (options == null)
            return MobileVirtualDisplayResult.Failed(MobileLocalization.Get("VirtualNoActivityOptions"));
        options.SetLaunchDisplayId(displayId);

        try
        {
            _activity.StartActivity(intent, options.ToBundle());
            return MobileVirtualDisplayResult.Succeeded(
                MobileLocalization.Get("VirtualAppLaunched"), displayId);
        }
        catch (Exception ex)
        {
            return MobileVirtualDisplayResult.Failed(
                $"{MobileLocalization.Get("VirtualLaunchFailed")}: {ex.Message}");
        }
    }

    private void ReleaseDisplay()
    {
        _virtualDisplay?.Release();
        _virtualDisplay?.Dispose();
        _virtualDisplay = null;
        _captureSurface?.Release();
        _captureSurface?.Dispose();
        _captureSurface = null;
        NativeCaptureInterop.ReleaseCapturer();
        Width = 0;
        Height = 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ReleaseDisplay();
        base.Dispose(disposing);
    }
}
