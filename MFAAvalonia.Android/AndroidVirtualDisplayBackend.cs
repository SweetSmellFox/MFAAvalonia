using Android.App;
using Android.OS;
using Android.Views;
using MFAAvalonia.Helper;
using System;
using System.Threading.Tasks;

namespace MFAAvalonia.Android;

public sealed class AndroidVirtualDisplayBackend : Java.Lang.Object, IMobileVirtualDisplayBackend
{
    private readonly Activity _activity;
    private readonly ShizukuConnectionManager _shizuku;
    private readonly object _sync = new();
    private Surface? _captureSurface;
    private int _displayId = -1;

    internal AndroidVirtualDisplayBackend(Activity activity, ShizukuConnectionManager shizuku)
    {
        _activity = activity;
        _shizuku = shizuku;
        _shizuku.StateChanged += OnShizukuStateChanged;
    }

    public bool IsRunning => _displayId >= 0;
    public int DisplayId => _displayId;
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
            if (IsRunning)
                return Task.FromResult(MobileVirtualDisplayResult.Succeeded(
                    MobileLocalization.Get("VirtualAlreadyRunning"), DisplayId));

            try
            {
                if (Build.VERSION.SdkInt < BuildVersionCodes.O)
                    return Task.FromResult(MobileVirtualDisplayResult.Failed(
                        MobileLocalization.Get("VirtualRequiresAndroid8")));

                if (!_shizuku.IsUserServiceReady
                    && !_shizuku.WaitForUserServiceReady(TimeSpan.FromSeconds(12)))
                    return Task.FromResult(MobileVirtualDisplayResult.Failed(_shizuku.StatusMessage));

                Width = width;
                Height = height;
                _captureSurface = NativeCaptureInterop.SetupCapturer(width, height);
                _displayId = _shizuku.CreateVirtualDisplay(width, height, dpi, _captureSurface);
                if (_displayId < 0)
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
        try
        {
            var result = _shizuku.StartApp(displayId, packageName);
            if (result != 0)
                return MobileVirtualDisplayResult.Failed(
                    $"{MobileLocalization.Get("VirtualLaunchFailed")}: result={result}");
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
        try
        {
            _shizuku.ReleaseVirtualDisplay();
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn(
                "MfaVirtualDisplay",
                $"Remote virtual display release failed; clearing local state: {ex.Message}");
        }
        finally
        {
            ReleaseLocalCaptureState();
        }
    }

    private void OnShizukuStateChanged()
    {
        if (_shizuku.IsUserServiceReady)
            return;

        lock (_sync)
            ReleaseLocalCaptureState();
    }

    private void ReleaseLocalCaptureState()
    {
        _displayId = -1;
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
        {
            _shizuku.StateChanged -= OnShizukuStateChanged;
            ReleaseDisplay();
        }
        base.Dispose(disposing);
    }
}
