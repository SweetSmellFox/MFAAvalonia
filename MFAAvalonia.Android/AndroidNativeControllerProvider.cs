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
    private readonly ShizukuAuthorizationPrompt _authorizationPrompt;
    private readonly AndroidVirtualDisplayBackend _displayBackend;

    public AndroidNativeControllerProvider(Activity activity, ShizukuConnectionManager shizuku,
        ShizukuAuthorizationPrompt authorizationPrompt, AndroidVirtualDisplayBackend displayBackend)
    {
        _activity = activity;
        _shizuku = shizuku;
        _authorizationPrompt = authorizationPrompt;
        _displayBackend = displayBackend;
    }

    public MaaController? Create(MaaControllerTypes controllerType)
    {
        // Mobile interface files still describe their controller as ADB. On Android that
        // logical controller is backed by MaaAndroidNativeControlUnit instead.
        if (controllerType == MaaControllerTypes.None)
            return null;

        if (!_shizuku.IsServiceAvailable || !_shizuku.HasPermission)
        {
            _authorizationPrompt.ShowIfNeeded();
            throw new InvalidOperationException(_shizuku.StatusMessage);
        }
        if (!_shizuku.IsUserServiceReady
            && !_shizuku.WaitForUserServiceReady(TimeSpan.FromSeconds(12)))
        {
            _authorizationPrompt.ShowIfNeeded();
            throw new InvalidOperationException(_shizuku.StatusMessage);
        }

        var nativeLibraryDir = _activity.ApplicationInfo?.NativeLibraryDir
            ?? throw new InvalidOperationException("Android native library directory is unavailable.");
        var bridgePath = Path.Combine(nativeLibraryDir, "libmfabridge.so");
        if (!File.Exists(bridgePath))
            throw new InvalidOperationException(
                "Android Native bridge is not ready. Install and authorize Shizuku, then reconnect the Android controller.");

        var metrics = _activity.Resources?.DisplayMetrics
            ?? throw new InvalidOperationException("Android display metrics are unavailable.");
        // The controller targets landscape games. Some Android hosts keep MFA's activity in
        // portrait and do not auto-rotate virtual displays, so activity metrics cannot be used
        // verbatim here without Android letterboxing the game into a thin horizontal strip.
        var virtualWidth = Math.Max(metrics.WidthPixels, metrics.HeightPixels);
        var virtualHeight = Math.Min(metrics.WidthPixels, metrics.HeightPixels);
        var bridgeResult = NativeBridgeInterop.Configure((uint)virtualWidth, (uint)virtualHeight);
        if (bridgeResult != 0)
            throw new InvalidOperationException(
                $"MFA Android native bridge initialization failed (result={bridgeResult}).");
        if (NativeBridgeInterop.SetInputPort((uint)_shizuku.UserServicePort) != 0)
            throw new InvalidOperationException("MFA Android native input bridge initialization failed.");
        if (!_displayBackend.IsRunning)
        {
            var dpi = (int)metrics.DensityDpi;
            var displayResult = _displayBackend.StartAsync(string.Empty, virtualWidth, virtualHeight, dpi)
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
                width = virtualWidth,
                height = virtualHeight,
            },
            display_id = _displayBackend.DisplayId >= 0 ? _displayBackend.DisplayId : display?.DisplayId ?? 0,
            force_stop = true,
        });

        return new MaaAndroidNativeController(config);
    }
}
