using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using MaaFramework.Binding;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using System;
using System.IO;
using System.Threading.Tasks;

namespace MFAAvalonia.Android;

[Activity(
    Label = "MFAAvalonia.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App>
{
    private AndroidVirtualDisplayBackend? _virtualDisplayBackend;
    private AndroidNativeControllerProvider? _controllerProvider;
    private ShizukuConnectionManager? _shizuku;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        AndroidAssetBootstrap.EnsureExtracted(this);
        ConfigureMaaLogging();
        _shizuku = new ShizukuConnectionManager(this);
        _shizuku.Start();
        _virtualDisplayBackend = new AndroidVirtualDisplayBackend(this);
        _controllerProvider = new AndroidNativeControllerProvider(this, _shizuku, _virtualDisplayBackend);
        PlatformControllerFactory.Create = _controllerProvider.Create;
        MobileVirtualDisplay.Backend = _virtualDisplayBackend;
        MobileVirtualDisplay.PreviewControlFactory = () =>
            new AndroidVirtualDisplayPreviewHost(this, _virtualDisplayBackend);
        PlatformApplicationRestart.RestartAsync = RestartAfterResourceUpdateAsync;
        base.OnCreate(savedInstanceState);
    }

    private Task RestartAfterResourceUpdateAsync()
    {
        RunOnUiThread(() =>
        {
            var launchIntent = PackageManager?.GetLaunchIntentForPackage(PackageName ?? string.Empty);
            var alarmManager = GetSystemService(AlarmService) as AlarmManager;
            if (launchIntent == null || alarmManager == null)
            {
                Recreate();
                return;
            }

            launchIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask);
            var pendingIntent = PendingIntent.GetActivity(
                this,
                0,
                launchIntent,
                PendingIntentFlags.CancelCurrent | PendingIntentFlags.Immutable);
            if (pendingIntent == null)
            {
                Recreate();
                return;
            }

            alarmManager.Set(
                AlarmType.ElapsedRealtime,
                SystemClock.ElapsedRealtime() + 500,
                pendingIntent);

            FinishAffinity();
            global::Android.OS.Process.KillProcess(global::Android.OS.Process.MyPid());
        });

        return Task.CompletedTask;
    }

    private static void ConfigureMaaLogging()
    {
        try
        {
            var logDirectory = Path.Combine(
                AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                "debug");
            Directory.CreateDirectory(logDirectory);
            MaaProcessor.Global.SetOption_LogDir(logDirectory);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MFAAvalonia", $"Maa log initialization failed: {ex}");
        }
    }

    protected override void OnDestroy()
    {
        PlatformApplicationRestart.RestartAsync = null;
        if (_controllerProvider != null)
            PlatformControllerFactory.Create = null;
        _controllerProvider = null;
        _shizuku?.Dispose();
        _shizuku = null;
        if (ReferenceEquals(MobileVirtualDisplay.Backend, _virtualDisplayBackend))
            MobileVirtualDisplay.Backend = null;
        MobileVirtualDisplay.PreviewControlFactory = null;
        _virtualDisplayBackend?.Dispose();
        _virtualDisplayBackend = null;
        base.OnDestroy();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
