using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using MaaFramework.Binding;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Windows;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

namespace MFAAvalonia.Android;

[Activity(
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App>
{
    private AndroidVirtualDisplayBackend? _virtualDisplayBackend;
    private AndroidNativeControllerProvider? _controllerProvider;
    private AndroidPythonAgentProvider? _pythonAgentProvider;
    private ShizukuConnectionManager? _shizuku;
    private ShizukuInstallationPrompt? _shizukuInstallationPrompt;
    private ShizukuAuthorizationPrompt? _shizukuAuthorizationPrompt;
    private RootViewModel? _rootViewModel;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        AndroidCrashDiagnostics.Install(this);
        try
        {
            AndroidCrashDiagnostics.SetPhase("asset-bootstrap");
            AndroidAssetBootstrap.EnsureExtracted(this);
            AndroidCrashDiagnostics.SetPhase("maa-logging");
            ConfigureMaaLogging();
            AndroidCrashDiagnostics.SetPhase("shizuku-manager");
            _shizuku = new ShizukuConnectionManager(this);
            _shizukuInstallationPrompt = new ShizukuInstallationPrompt(this);
            _shizukuAuthorizationPrompt = new ShizukuAuthorizationPrompt(this, _shizuku);
            _shizuku.Start();
            AndroidCrashDiagnostics.SetPhase("virtual-display");
            _virtualDisplayBackend = new AndroidVirtualDisplayBackend(this, _shizuku);
            AndroidCrashDiagnostics.SetPhase("controller-provider");
            _controllerProvider = new AndroidNativeControllerProvider(this, _shizuku, _shizukuAuthorizationPrompt, _virtualDisplayBackend);
            PlatformControllerFactory.Create = _controllerProvider.Create;
            AndroidCrashDiagnostics.SetPhase("python-agent-provider");
            _pythonAgentProvider = new AndroidPythonAgentProvider(this);
            if (_pythonAgentProvider.IsAvailable)
            {
                PlatformAgentFactory.Start = _pythonAgentProvider.Start;
                PlatformAgentFactory.LinkStart = _pythonAgentProvider.LinkStart;
                PlatformAgentFactory.LinkStop = _pythonAgentProvider.LinkStop;
            }
            MobileVirtualDisplay.Backend = _virtualDisplayBackend;
            MobileVirtualDisplay.PreviewControlFactory = () =>
                new AndroidVirtualDisplayPreviewHost(this, _virtualDisplayBackend);
            PlatformApplicationRestart.RestartAsync = RestartAfterResourceUpdateAsync;
            AndroidCrashDiagnostics.SetPhase("avalonia-on-create");
            base.OnCreate(savedInstanceState);
            AndroidCrashDiagnostics.SetPhase("application-title");
            BindApplicationTitle();
            AndroidCrashDiagnostics.SetPhase("activity-ready");
        }
        catch (Exception ex)
        {
            AndroidCrashDiagnostics.Record("main-activity-on-create", ex);
            throw;
        }
    }

    protected override void OnPostResume()
    {
        try
        {
            AndroidCrashDiagnostics.SetPhase("activity-post-resume");
            base.OnPostResume();
            _shizuku?.RefreshState();
            AndroidCrashDiagnostics.SetPhase("shizuku-install-prompt");
            _shizukuInstallationPrompt?.ShowIfNotInstalled();
            _shizukuAuthorizationPrompt?.ShowIfNeeded();
            AndroidCrashDiagnostics.SetPhase("activity-running");
        }
        catch (Exception ex)
        {
            AndroidCrashDiagnostics.Record("main-activity-on-post-resume", ex);
            throw;
        }
    }

    private void BindApplicationTitle()
    {
        try
        {
            _rootViewModel = Instances.RootViewModel;
            _rootViewModel.PropertyChanged += OnRootViewModelPropertyChanged;
            ApplyApplicationTitle(_rootViewModel.ApplicationDisplayName);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MFAAvalonia", $"Application title initialization failed: {ex}");
        }
    }

    private void OnRootViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RootViewModel.ApplicationDisplayName) && sender is RootViewModel viewModel)
            ApplyApplicationTitle(viewModel.ApplicationDisplayName);
    }

    private void ApplyApplicationTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return;

        RunOnUiThread(() =>
        {
            Title = title;
            SetTaskDescription(new ActivityManager.TaskDescription(title));
        });
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
        if (_rootViewModel != null)
            _rootViewModel.PropertyChanged -= OnRootViewModelPropertyChanged;
        _rootViewModel = null;
        PlatformApplicationRestart.RestartAsync = null;
        if (_controllerProvider != null)
        {
            PlatformControllerFactory.Create = null;
        }
        _controllerProvider = null;
        if (_pythonAgentProvider != null)
        {
            PlatformAgentFactory.Start = null;
            PlatformAgentFactory.LinkStart = null;
            PlatformAgentFactory.LinkStop = null;
        }
        _pythonAgentProvider = null;
        _shizukuAuthorizationPrompt?.Dispose();
        _shizukuAuthorizationPrompt = null;
        _shizuku?.Dispose();
        _shizuku = null;
        _shizukuInstallationPrompt?.Dispose();
        _shizukuInstallationPrompt = null;
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
