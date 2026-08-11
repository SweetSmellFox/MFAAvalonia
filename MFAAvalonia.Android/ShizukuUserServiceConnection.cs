using Android.Content;
using Android.OS;
using Rikka.Shizuku;
using System;

namespace MFAAvalonia.Android;

internal sealed class ShizukuUserServiceConnection : Java.Lang.Object, IServiceConnection
{
    private const int HealthTransaction = 1;
    private const string ServiceClassName = "com.fox.MFAAvalonia.MfaShizukuUserService";
    private readonly Action<bool, int, int, string?> _stateChanged;
    private Shizuku.UserServiceArgs? _args;

    public ShizukuUserServiceConnection(Action<bool, int, int, string?> stateChanged) => _stateChanged = stateChanged;

    public void Bind(Context context)
    {
        if (_args != null) return;
        _args = new Shizuku.UserServiceArgs(new ComponentName(context.PackageName, ServiceClassName));
        _args.ProcessNameSuffix("mfa_service");
        _args.Daemon(false);
        _args.Debuggable(true);
        _args.Version(2);
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
            _stateChanged(true, uid, port, null);
        }
        catch (Exception ex)
        {
            _stateChanged(false, -1, -1, ex.Message);
        }
    }

    public void OnServiceDisconnected(ComponentName? name) =>
        _stateChanged(false, -1, -1, "Shizuku UserService disconnected.");

    public void Unbind()
    {
        if (_args == null) return;
        Shizuku.UnbindUserService(_args, this, true);
        _args.Dispose();
        _args = null;
    }
}
