using Android.App;
using Android.Content;
using Android.OS;
using MaaFramework.Binding;
using MFAAvalonia.Extensions.MaaFW;
using System;
using System.IO;
using System.Text.Json;

namespace MFAAvalonia.Android;

internal sealed class AndroidNativeControllerProvider
{
    private readonly Activity _activity;
    private readonly ShizukuConnectionManager _shizuku;
    private readonly AndroidVirtualDisplayBackend _displayBackend;

    public AndroidNativeControllerProvider(Activity activity, ShizukuConnectionManager shizuku,
        AndroidVirtualDisplayBackend displayBackend)
    {
        _activity = activity;
        _shizuku = shizuku;
        _displayBackend = displayBackend;
    }

    public MaaController? Create(MaaControllerTypes controllerType)
    {
        // Mobile interface files still describe their controller as ADB. On Android that
        // logical controller is backed by MaaAndroidNativeControlUnit instead.
        if (controllerType == MaaControllerTypes.None)
            return null;

        if (!_shizuku.IsServiceAvailable || !_shizuku.HasPermission)
            throw new InvalidOperationException(_shizuku.StatusMessage);
        if (!_shizuku.IsUserServiceReady
            && !_shizuku.WaitForUserServiceReady(TimeSpan.FromSeconds(12)))
            throw new InvalidOperationException(_shizuku.StatusMessage);

        var nativeLibraryDir = _activity.ApplicationInfo?.NativeLibraryDir
            ?? throw new InvalidOperationException("Android native library directory is unavailable.");
        var bridgePath = Path.Combine(nativeLibraryDir, "libmfabridge.so");
        if (!File.Exists(bridgePath))
            throw new InvalidOperationException(
                "Android Native bridge is not ready. Install and authorize Shizuku, then reconnect the Android controller.");

        var metrics = _activity.Resources?.DisplayMetrics
            ?? throw new InvalidOperationException("Android display metrics are unavailable.");
        if (NativeBridgeInterop.Configure((uint)metrics.WidthPixels, (uint)metrics.HeightPixels) != 0)
            throw new InvalidOperationException("MFA Android native bridge initialization failed.");
        if (NativeBridgeInterop.SetInputPort((uint)_shizuku.UserServicePort) != 0)
            throw new InvalidOperationException("MFA Android native input bridge initialization failed.");
        if (!_displayBackend.IsRunning)
        {
            var dpi = (int)metrics.DensityDpi;
            var displayResult = _displayBackend.StartAsync(string.Empty, metrics.WidthPixels, metrics.HeightPixels, dpi)
                .GetAwaiter().GetResult();
            if (!displayResult.Success)
                throw new InvalidOperationException(displayResult.Message);
        }
        var display = Build.VERSION.SdkInt >= BuildVersionCodes.R
            ? _activity.Display
            : null;

        var config = JsonSerializer.Serialize(new
        {
            library_path = bridgePath,
            screen_resolution = new
            {
                width = metrics.WidthPixels,
                height = metrics.HeightPixels,
            },
            display_id = _displayBackend.DisplayId >= 0 ? _displayBackend.DisplayId : display?.DisplayId ?? 0,
            force_stop = true,
        });

        return new MaaAndroidNativeController(config);
    }
}
