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
    private RootViewModel? _rootViewModel;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        AndroidAssetBootstrap.EnsureExtracted(this);
        ConfigureMaaLogging();
        _shizuku = new ShizukuConnectionManager(this);
        _shizuku.Start();
        _shizukuInstallationPrompt = new ShizukuInstallationPrompt(this);
        _virtualDisplayBackend = new AndroidVirtualDisplayBackend(this);
        _controllerProvider = new AndroidNativeControllerProvider(this, _shizuku, _virtualDisplayBackend);
        PlatformControllerFactory.Create = _controllerProvider.Create;
        _pythonAgentProvider = new AndroidPythonAgentProvider(this);
        if (_pythonAgentProvider.IsAvailable)
        {
            PlatformAgentFactory.Start = _pythonAgentProvider.Start;
            PlatformAgentFactory.LinkStart = _pythonAgentProvider.LinkStart;
        }
        MobileVirtualDisplay.Backend = _virtualDisplayBackend;
        MobileVirtualDisplay.PreviewControlFactory = () =>
            new AndroidVirtualDisplayPreviewHost(this, _virtualDisplayBackend);
        PlatformApplicationRestart.RestartAsync = RestartAfterResourceUpdateAsync;
        base.OnCreate(savedInstanceState);
        BindApplicationTitle();
    }

    protected override void OnPostResume()
    {
        base.OnPostResume();
        _shizukuInstallationPrompt?.ShowIfNotInstalled();
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
            PlatformControllerFactory.Create = null;
        _controllerProvider = null;
        if (_pythonAgentProvider != null)
        {
            PlatformAgentFactory.Start = null;
            PlatformAgentFactory.LinkStart = null;
        }
        _pythonAgentProvider = null;
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
