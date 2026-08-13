using Android.Content;
using Android.OS;
using Android.Views;
using Rikka.Shizuku;
using System;

namespace MFAAvalonia.Android;

internal sealed class ShizukuUserServiceConnection : Java.Lang.Object, IServiceConnection
{
    private const int HealthTransaction = 1;
    private const int CreateDisplayTransaction = 2;
    private const int ReleaseDisplayTransaction = 3;
    private const int StartAppTransaction = 4;
    private const string ServiceClassName = "com.fox.MFAAvalonia.MfaShizukuUserService";
    private readonly Action<bool, int, int, string?> _stateChanged;
    private Shizuku.UserServiceArgs? _args;
    private IBinder? _service;

    public ShizukuUserServiceConnection(Action<bool, int, int, string?> stateChanged) => _stateChanged = stateChanged;

    public void Bind(Context context)
    {
        if (_args != null) return;
        _args = new Shizuku.UserServiceArgs(new ComponentName(context.PackageName, ServiceClassName));
        _args.ProcessNameSuffix("mfa_service");
        _args.Daemon(false);
        _args.Debuggable(true);
        // Version 4 additionally normalizes root-mode Shizuku services to the shell
        // identity. Bump it so Shizuku does not reconnect an old root v3 process.
        _args.Version(4);
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
}
