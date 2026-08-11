using Android.App;
using Android.Graphics;
using Android.Views;
using Avalonia.Android;
using Avalonia.Controls;
using Avalonia.Platform;

namespace MFAAvalonia.Android;

internal sealed class AndroidVirtualDisplayPreviewHost : NativeControlHost
{
    private readonly Activity _activity;
    private readonly AndroidVirtualDisplayBackend _backend;
    private SurfaceCallback? _callback;
    private SurfaceView? _surfaceView;

    internal AndroidVirtualDisplayPreviewHost(Activity activity, AndroidVirtualDisplayBackend backend)
    {
        _activity = activity;
        _backend = backend;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _surfaceView = new SurfaceView(_activity);
        _surfaceView.Holder?.SetFormat(Format.Rgba8888);
        _surfaceView.SetZOrderOnTop(false);
        _surfaceView.SetZOrderMediaOverlay(false);
        _callback = new SurfaceCallback(_backend);
        _surfaceView.Holder?.AddCallback(_callback);
        return new AndroidViewControlHandle(_surfaceView);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        NativeCaptureInterop.SetPreviewSurface(null);
        if (_surfaceView?.Holder != null && _callback != null)
            _surfaceView.Holder.RemoveCallback(_callback);
        _callback?.Dispose();
        _callback = null;
        _surfaceView = null;
        base.DestroyNativeControlCore(control);
    }

    private sealed class SurfaceCallback(AndroidVirtualDisplayBackend backend) : Java.Lang.Object,
        ISurfaceHolderCallback
    {
        public void SurfaceCreated(ISurfaceHolder holder)
        {
            if (backend.Width > 0 && backend.Height > 0)
                holder.SetFixedSize(backend.Width, backend.Height);
            NativeCaptureInterop.SetPreviewSurface(holder.Surface);
        }

        public void SurfaceChanged(ISurfaceHolder holder, Format format, int width, int height) =>
            NativeCaptureInterop.SetPreviewSurface(holder.Surface);

        public void SurfaceDestroyed(ISurfaceHolder holder) =>
            NativeCaptureInterop.SetPreviewSurface(null);
    }
}
