using Android.Content.PM;
using Android.Content;
using Rikka.Shizuku;
using System;
using System.Threading;

namespace MFAAvalonia.Android;

internal sealed class ShizukuConnectionManager : Java.Lang.Object,
    Shizuku.IOnBinderReceivedListener,
    Shizuku.IOnBinderDeadListener,
    Shizuku.IOnRequestPermissionResultListener
{
    private const int PermissionRequestCode = 0x4d46;
    private bool _started;
    private readonly Context _context;
    private readonly ShizukuUserServiceConnection _userService;
    private readonly ManualResetEventSlim _userServiceReady = new(false);

    public ShizukuConnectionManager(Context context)
    {
        _context = context.ApplicationContext ?? context;
        _userService = new ShizukuUserServiceConnection(OnUserServiceStateChanged);
    }

    public bool IsServiceAvailable { get; private set; }
    public bool HasPermission { get; private set; }
    public bool IsUserServiceReady { get; private set; }
    public int UserServiceUid { get; private set; } = -1;
    public int UserServicePort { get; private set; } = -1;
    public string? UserServiceError { get; private set; }

    public string StatusMessage => !IsServiceAvailable
        ? "Shizuku service is unavailable. Install and start Shizuku first."
        : !HasPermission
            ? "Shizuku permission has not been granted to MFA."
            : !IsUserServiceReady
                ? $"Shizuku UserService is not ready. {UserServiceError}".Trim()
                : $"Shizuku UserService is ready (uid {UserServiceUid}, port {UserServicePort}).";

    public event Action? StateChanged;

    public bool WaitForUserServiceReady(TimeSpan timeout)
    {
        if (IsUserServiceReady)
            return true;

        if (IsServiceAvailable && HasPermission)
            BindUserService();

        return _userServiceReady.Wait(timeout) && IsUserServiceReady;
    }

    public void Start()
    {
        if (_started)
            return;

        _started = true;
        Shizuku.AddBinderReceivedListenerSticky(this);
        Shizuku.AddBinderDeadListener(this);
        Shizuku.AddRequestPermissionResultListener(this);
        RefreshState(requestPermission: true);
    }

    public void OnBinderReceived() => RefreshState(requestPermission: true);

    public void OnBinderDead()
    {
        IsServiceAvailable = false;
        HasPermission = false;
        IsUserServiceReady = false;
        UserServiceUid = -1;
        UserServicePort = -1;
        _userServiceReady.Reset();
        StateChanged?.Invoke();
    }

    public void OnRequestPermissionResult(int requestCode, int grantResult)
    {
        if (requestCode != PermissionRequestCode)
            return;

        HasPermission = grantResult == (int)Permission.Granted;
        if (HasPermission) BindUserService();
        StateChanged?.Invoke();
    }

    private void RefreshState(bool requestPermission)
    {
        try
        {
            IsServiceAvailable = Shizuku.PingBinder();
            HasPermission = IsServiceAvailable
                && Shizuku.CheckSelfPermission() == (int)Permission.Granted;
            StateChanged?.Invoke();

            if (HasPermission) BindUserService();

            if (requestPermission && IsServiceAvailable && !HasPermission
                && !Shizuku.ShouldShowRequestPermissionRationale())
                Shizuku.RequestPermission(PermissionRequestCode);
        }
        catch
        {
            IsServiceAvailable = false;
            HasPermission = false;
            StateChanged?.Invoke();
        }
    }

    private void BindUserService()
    {
        try { _userService.Bind(_context); }
        catch (Exception ex) { OnUserServiceStateChanged(false, -1, -1, ex.Message); }
    }

    private void OnUserServiceStateChanged(bool ready, int uid, int port, string? error)
    {
        IsUserServiceReady = ready;
        UserServiceUid = uid;
        UserServicePort = port;
        UserServiceError = error;
        if (ready)
            _userServiceReady.Set();
        else
            _userServiceReady.Reset();
        global::Android.Util.Log.Info("MFAAvalonia",
            ready ? $"Shizuku UserService ready, uid={uid}, port={port}" : $"Shizuku UserService unavailable: {error}");
        StateChanged?.Invoke();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _started)
        {
            Shizuku.RemoveBinderReceivedListener(this);
            Shizuku.RemoveBinderDeadListener(this);
            Shizuku.RemoveRequestPermissionResultListener(this);
            _started = false;
            _userService.Unbind();
            _userServiceReady.Dispose();
        }

        base.Dispose(disposing);
    }
}
