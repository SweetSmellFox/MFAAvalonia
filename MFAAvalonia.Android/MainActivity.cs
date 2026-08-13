using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using AndroidX.Core.Content;
using Avalonia;
using Avalonia.Android;
using MaaFramework.Binding;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Windows;
using MFAAvalonia.Utilities;
using Markdown.Avalonia.Utils;
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
    private string? _pendingUpdateApkPath;
    private TaskCompletionSource? _pendingUpdateInstall;
    private bool _awaitingUnknownSourcesPermission;
    private bool _unknownSourcesActivityPaused;

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
            PlatformApplicationRestart.InstallApkAsync = InstallResourceUpdateApkAsync;
            UrlUtilities.PlatformOpenUrl = OpenUrl;
            DefaultHyperlinkCommand.PlatformOpenUrl = OpenUrl;
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
            if (_awaitingUnknownSourcesPermission && _unknownSourcesActivityPaused)
            {
                _awaitingUnknownSourcesPermission = false;
                _unknownSourcesActivityPaused = false;
                try
                {
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.O
                        && PackageManager?.CanRequestPackageInstalls() != true)
                    {
                        CompletePendingUpdateInstall(new UnauthorizedAccessException(
                            "Please allow this app to install unknown apps, then retry the update."));
                        return;
                    }
                    ContinuePendingUpdateInstall();
                }
                catch (Exception ex)
                {
                    CompletePendingUpdateInstall(ex);
                }
                return;
            }
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

    protected override void OnPause()
    {
        if (_awaitingUnknownSourcesPermission)
            _unknownSourcesActivityPaused = true;
        base.OnPause();
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

    private Task InstallResourceUpdateApkAsync(string apkPath)
    {
        if (!File.Exists(apkPath))
            throw new FileNotFoundException("Downloaded Android update APK was not found.", apkPath);

        if (_pendingUpdateInstall != null)
            throw new InvalidOperationException("An Android APK installation is already pending.");

        _pendingUpdateApkPath = apkPath;
        _pendingUpdateInstall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RunOnUiThread(() =>
        {
            try
            {
                ContinuePendingUpdateInstall();
            }
            catch (Exception ex)
            {
                CompletePendingUpdateInstall(ex);
            }
        });

        return _pendingUpdateInstall.Task;
    }

    private void OpenUrl(string url)
    {
        var uri = global::Android.Net.Uri.Parse(url);
        if (uri == null || string.IsNullOrWhiteSpace(uri.Scheme))
            throw new UriFormatException($"Invalid URL: {url}");

        RunOnUiThread(() =>
        {
            var intent = new Intent(Intent.ActionView, uri);
            StartActivity(intent);
        });
    }

    private void ContinuePendingUpdateInstall()
    {
        if (_pendingUpdateInstall == null || string.IsNullOrWhiteSpace(_pendingUpdateApkPath))
            return;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O
            && PackageManager?.CanRequestPackageInstalls() != true)
        {
            if (_awaitingUnknownSourcesPermission)
            {
                CompletePendingUpdateInstall(new UnauthorizedAccessException(
                    "Please allow this app to install unknown apps, then retry the update."));
                return;
            }

            _awaitingUnknownSourcesPermission = true;
            _unknownSourcesActivityPaused = false;
            var settingsIntent = new Intent(
                Settings.ActionManageUnknownAppSources,
                global::Android.Net.Uri.Parse($"package:{PackageName}"));
            StartActivity(settingsIntent);
            return;
        }

        var apkFile = new Java.IO.File(_pendingUpdateApkPath);
        var authority = $"{PackageName}.fileprovider";
        var contentUri = FileProvider.GetUriForFile(this, authority, apkFile);
        var installIntent = new Intent(Intent.ActionView);
        installIntent.SetDataAndType(contentUri, "application/vnd.android.package-archive");
        installIntent.ClipData = ClipData.NewRawUri("MFA resource update", contentUri);
        installIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);

        var installers = PackageManager?.QueryIntentActivities(
            installIntent,
            PackageInfoFlags.MatchDefaultOnly);
        if (installers == null || installers.Count == 0)
            throw new ActivityNotFoundException("No Android package installer can handle the downloaded APK.");

        foreach (var installer in installers)
        {
            var packageName = installer.ActivityInfo?.PackageName;
            if (!string.IsNullOrWhiteSpace(packageName))
                GrantUriPermission(packageName, contentUri, ActivityFlags.GrantReadUriPermission);
        }

        global::Android.Util.Log.Info(
            "MFAAvalonia",
            $"Launching package installer for resource update: uri={contentUri}, handlers={installers.Count}");
        StartActivity(installIntent);
        CompletePendingUpdateInstall();
    }

    private void CompletePendingUpdateInstall(Exception? error = null)
    {
        var completion = _pendingUpdateInstall;
        _pendingUpdateInstall = null;
        _pendingUpdateApkPath = null;
        _awaitingUnknownSourcesPermission = false;
        _unknownSourcesActivityPaused = false;
        if (error == null)
            completion?.TrySetResult();
        else
            completion?.TrySetException(error);
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
        PlatformApplicationRestart.InstallApkAsync = null;
        UrlUtilities.PlatformOpenUrl = null;
        DefaultHyperlinkCommand.PlatformOpenUrl = null;
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
