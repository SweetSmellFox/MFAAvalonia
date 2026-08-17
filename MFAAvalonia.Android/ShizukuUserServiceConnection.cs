using Android.Content;
using Android.OS;
using Android.Views;
using Rikka.Shizuku;
using System;
using System.Threading;

namespace MFAAvalonia.Android;

internal sealed class ShizukuUserServiceConnection : Java.Lang.Object, IServiceConnection
{
    private const int HealthTransaction = 1;
    private const int CreateDisplayTransaction = 2;
    private const int ReleaseDisplayTransaction = 3;
    private const int StartAppTransaction = 4;
    private const int CreatePrimaryCaptureTransaction = 5;
    private const int GetDisplayInfoTransaction = 6;
    private const int ResolveCaptureDisplayTransaction = 7;
    private const int GetFocusedDisplayTransaction = 8;
    private const int SetGameKeepAliveTransaction = 9;
    private const int OverlayInputStateTransaction = 1;
    private const string ServiceClassName = "com.fox.MFAAvalonia.MfaShizukuUserService";
    private static int _serviceVersion = 100;
    private readonly Action<bool, int, int, string?> _stateChanged;
    private Shizuku.UserServiceArgs? _args;
    private IBinder? _service;
    private readonly OverlayInputStateBinder _overlayInputStateBinder = new();

    public ShizukuUserServiceConnection(Action<bool, int, int, string?> stateChanged) => _stateChanged = stateChanged;

    public void Bind(Context context)
    {
        if (_args != null) return;
        _args = new Shizuku.UserServiceArgs(new ComponentName(context.PackageName, ServiceClassName));
        _args.ProcessNameSuffix("mfa_service");
        _args.Daemon(false);
        _args.Debuggable(true);
        // Match MaaFwApp: every binding gets a fresh version and tag, so Shizuku
        // never reconnects a stale UserService after an app update or reconnect.
        _args.Version(Interlocked.Increment(ref _serviceVersion));
        _args.Tag($"mfa-android-controller-{Guid.NewGuid():N}");
        Shizuku.BindUserService(_args, this);
    }

    public void OnServiceConnected(ComponentName? name, IBinder? service)
    {
        if (service == null)
        {
            _stateChanged(false, -1, -1, "Shizuku UserService returned an empty binder.");
            return;
        }

        try
        {
            using var data = Parcel.Obtain();
            using var reply = Parcel.Obtain();
            data.WriteInt(global::Android.OS.Process.MyPid());
            data.WriteStrongBinder(_overlayInputStateBinder);
            service.Transact(HealthTransaction, data, reply, 0);
            var uid = reply.ReadInt();
            var port = reply.ReadInt();
            if (port is <= 0 or > 65535)
                throw new InvalidOperationException($"Shizuku UserService returned an invalid input port: {port}.");
            _service = service;
            _stateChanged(true, uid, port, null);
        }
        catch (Exception ex)
        {
            _stateChanged(false, -1, -1, ex.Message);
        }
    }

    public void OnServiceDisconnected(ComponentName? name)
    {
        _service = null;
        _stateChanged(false, -1, -1, "Shizuku UserService disconnected.");
    }

    public int CreateVirtualDisplay(int width, int height, int dpi, Surface surface)
    {
        var service = _service
            ?? throw new InvalidOperationException("Shizuku UserService is not connected.");
        using var data = Parcel.Obtain();
        using var reply = Parcel.Obtain();
        data.WriteInt(width);
        data.WriteInt(height);
        data.WriteInt(dpi);
        surface.WriteToParcel(data, ParcelableWriteFlags.None);
        service.Transact(CreateDisplayTransaction, data, reply, 0);
        var displayId = reply.ReadInt();
        var error = reply.ReadString();
        var flags = reply.ReadInt();
        if (displayId < 0)
            throw new InvalidOperationException(
                $"Shizuku virtual display creation failed: {error ?? "unknown error"} " +
                $"(last flags=0x{flags:X}).");
        global::Android.Util.Log.Info("MfaVirtualDisplay",
            $"UserService created display {displayId} with flags 0x{flags:X}.");
        return displayId;
    }

    public void ReleaseVirtualDisplay()
    {
        var service = _service;
        if (service == null)
            return;
        using var data = Parcel.Obtain();
        service.Transact(ReleaseDisplayTransaction, data, null, 0);
    }

    public void CreatePrimaryDisplayCapture(int displayId, int width, int height, Surface surface)
    {
        var service = _service
            ?? throw new InvalidOperationException("Shizuku UserService is not connected.");
        using var data = Parcel.Obtain();
        using var reply = Parcel.Obtain();
        data.WriteInt(displayId);
        data.WriteInt(width);
        data.WriteInt(height);
        surface.WriteToParcel(data, ParcelableWriteFlags.None);
        service.Transact(CreatePrimaryCaptureTransaction, data, reply, 0);
        var error = reply.ReadString();
        if (!string.IsNullOrEmpty(error))
            throw new InvalidOperationException($"Primary display capture failed: {error}");
    }

    public (int Width, int Height, int Rotation, int LayerStack) GetDisplayInfo(int displayId)
    {
        var service = _service
            ?? throw new InvalidOperationException("Shizuku UserService is not connected.");
        using var data = Parcel.Obtain();
        using var reply = Parcel.Obtain();
        data.WriteInt(displayId);
        service.Transact(GetDisplayInfoTransaction, data, reply, 0);
        var result = (reply.ReadInt(), reply.ReadInt(), reply.ReadInt(), reply.ReadInt());
        if (result.Item1 <= 0 || result.Item2 <= 0)
            throw new InvalidOperationException(
                $"Display {displayId} returned an invalid logical size: {result.Item1}x{result.Item2}.");
        return result;
    }

    public int ResolveCurrentScreenDisplayId(int fallbackDisplayId)
    {
        var service = _service
            ?? throw new InvalidOperationException("Shizuku UserService is not connected.");
        using var data = Parcel.Obtain();
        using var reply = Parcel.Obtain();
        data.WriteInt(fallbackDisplayId);
        service.Transact(ResolveCaptureDisplayTransaction, data, reply, 0);
        var displayId = reply.ReadInt();
        return displayId >= 0 ? displayId : fallbackDisplayId;
    }

    public (int DisplayId, string? PackageName) GetFocusedDisplayTarget(int fallbackDisplayId)
    {
        var service = _service
            ?? throw new InvalidOperationException("Shizuku UserService is not connected.");
        using var data = Parcel.Obtain();
        using var reply = Parcel.Obtain();
        data.WriteInt(fallbackDisplayId);
        service.Transact(GetFocusedDisplayTransaction, data, reply, 0);
        var displayId = reply.ReadInt();
        var packageName = reply.ReadString();
        return (displayId >= 0 ? displayId : fallbackDisplayId, packageName);
    }

    public void SetGameProcessKeepAlive(int displayId, bool enabled)
    {
        var service = _service
            ?? throw new InvalidOperationException("Shizuku UserService is not connected.");
        using var data = Parcel.Obtain();
        data.WriteInt(displayId);
        data.WriteInt(enabled ? 1 : 0);
        service.Transact(SetGameKeepAliveTransaction, data, null, 0);
    }

    public int StartApp(int displayId, string target, bool forceStop)
    {
        var service = _service
            ?? throw new InvalidOperationException("Shizuku UserService is not connected.");
        using var data = Parcel.Obtain();
        using var reply = Parcel.Obtain();
        data.WriteInt(displayId);
        data.WriteInt(forceStop ? 1 : 0);
        data.WriteString(target);
        service.Transact(StartAppTransaction, data, reply, 0);
        return reply.ReadInt();
    }

    public void Unbind()
    {
        if (_args == null) return;
        ReleaseVirtualDisplay();
        _service = null;
        Shizuku.UnbindUserService(_args, this, true);
        _args.Dispose();
        _args = null;
    }

    private sealed class OverlayInputStateBinder : Binder
    {
        protected override bool OnTransact(int code, Parcel data, Parcel? reply, int flags)
        {
            if (code != OverlayInputStateTransaction)
                return base.OnTransact(code, data, reply, flags);

            var applied = AndroidCurrentScreenOverlay.SetScriptInputActive(data.ReadInt() != 0);
            reply?.WriteInt(applied ? 1 : 0);
            return true;
        }
    }
}
