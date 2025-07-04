using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Pages;
using MFAAvalonia.ViewModels.UsersControls;
using MFAAvalonia.ViewModels.UsersControls.Settings;
using MFAAvalonia.ViewModels.Windows;
using MFAAvalonia.Views.Pages;
using MFAAvalonia.Views.UserControls;
using MFAAvalonia.Views.UserControls.Settings;
using MFAAvalonia.Views.Windows;
using Microsoft.Extensions.DependencyInjection;
using SharpHook;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MFAAvalonia;

public partial class App : Application
{
    /// <summary>
    /// Gets services.
    /// </summary>
    public static IServiceProvider Services { get; private set; }

    public override void Initialize()
    {
        LoggerHelper.InitializeLogger();
        AvaloniaXamlLoader.Load(this);
        LanguageHelper.Initialize();
        ConfigurationManager.Initialize();
        var cracker = new AvaloniaMemoryCracker();
        cracker.Cracker();
        GlobalHotkeyService.Initialize();
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException; //Task线程内未捕获异常处理事件
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException; //非UI线程内未捕获异常处理事件
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException; //UI线程内未捕获异常处理事件

    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownRequested += OnShutdownRequested;
            var services = new ServiceCollection();

            services.AddSingleton(desktop);

            ConfigureServices(services);

            var views = ConfigureViews(services);

            Services = services.BuildServiceProvider();

            DataTemplates.Add(new ViewLocator(views));

            var window = views.CreateView<RootViewModel>(Services) as Window;

            desktop.MainWindow = window;

            TrayIconManager.InitializeTrayIcon(this, Instances.RootView, Instances.RootViewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnShutdownRequested(object sender, ShutdownRequestedEventArgs e)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskItems, Instances.TaskQueueViewModel.TaskItemViewModels.ToList().Select(model => model.InterfaceItem));

        MaaProcessor.Instance.SetTasker();
        GlobalHotkeyService.Shutdown();
    }

    private static ViewsHelper ConfigureViews(ServiceCollection services)
    {

        return new ViewsHelper()

            // Add main view
            .AddView<RootView, RootViewModel>(services)

            // Add pages
            .AddView<TaskQueueView, TaskQueueViewModel>(services)
            .AddView<ResourcesView, ResourcesViewModel>(services)
            .AddView<SettingsView, SettingsViewModel>(services)

            // Add additional views
            .AddView<AddTaskDialogView, AddTaskDialogViewModel>(services)
            .AddView<AdbEditorDialogView, AdbEditorDialogViewModel>(services)
            .AddView<CustomThemeDialogView, CustomThemeDialogViewModel>(services)
            .AddView<ConnectSettingsUserControl, ConnectSettingsUserControlModel>(services)
            .AddView<GameSettingsUserControl, GameSettingsUserControlModel>(services)
            .AddView<GuiSettingsUserControl, GuiSettingsUserControlModel>(services)
            .AddView<StartSettingsUserControl, StartSettingsUserControlModel>(services)
            .AddView<ExternalNotificationSettingsUserControl, ExternalNotificationSettingsUserControlModel>(services)
            .AddView<TimerSettingsUserControl, TimerSettingsUserControlModel>(services)
            .AddView<PerformanceUserControl, PerformanceUserControlModel>(services)
            .AddView<VersionUpdateSettingsUserControl, VersionUpdateSettingsUserControlModel>(services)
            .AddOnlyView<AboutUserControl, SettingsViewModel>(services)
            .AddOnlyView<HotKeySettingsUserControl, SettingsViewModel>(services)
            .AddOnlyView<ConfigurationMgrUserControl, SettingsViewModel>(services);
    }

    private static void ConfigureServices(ServiceCollection services)
    {
        services.AddSingleton<ISukiToastManager, SukiToastManager>();
        services.AddSingleton<ISukiDialogManager, SukiDialogManager>();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            e.Handled = true;
            LoggerHelper.Error(e.Exception);
            ErrorView.ShowException(e.Exception);
        }
        catch (Exception ex)
        {
            //此时程序出现严重异常，将强制结束退出
            LoggerHelper.Error(ex.ToString());
            ErrorView.ShowException(ex, true);
        }
    }


    void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var sbEx = new StringBuilder();
        if (e.IsTerminating)
            sbEx.Append("非UI线程发生致命错误");
        else
            sbEx.Append("非UI线程异常：");
        if (e.ExceptionObject is Exception ex)
        {
            ErrorView.ShowException(ex);
            sbEx.Append(ex.Message);
        }
        else
        {
            sbEx.Append(e.ExceptionObject);
        }
        LoggerHelper.Error(sbEx);
    }

    void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            var shouldIgnore = false;
            var errorMessage = string.Empty;
            
            foreach (var innerEx in e.Exception.InnerExceptions ?? Enumerable.Empty<Exception>())
            {
                if (innerEx is Tmds.DBus.Protocol.DBusException dbusEx && dbusEx.ErrorName == "org.freedesktop.DBus.Error.ServiceUnknown" && dbusEx.Message.Contains("com.canonical.AppMenu.Registrar"))
                {
                    shouldIgnore = true;
                    errorMessage = "检测到DBus服务(com.canonical.AppMenu.Registrar)不可用，这在非Unity桌面环境中是正常现象";
                    break;
                }
                if (innerEx is HookException)
                {
                    shouldIgnore = true;
                    errorMessage = "macOS中的全局快捷键Hook异常，可能是由于权限不足或系统限制导致的";
                }
            }

            if (shouldIgnore)
            {
                LoggerHelper.Warning(errorMessage);
                LoggerHelper.Warning(e.Exception);
            }
            else
            {
                // 处理其他异常（按原有逻辑记录错误）
                LoggerHelper.Error(e.Exception);
                ErrorView.ShowException(e.Exception);

                foreach (var item in e.Exception.InnerExceptions ?? Enumerable.Empty<Exception>())
                {
                    LoggerHelper.Error(string.Format("异常类型：{0}{1}来自：{2}{3}异常内容：{4}",
                        item.GetType(), Environment.NewLine, item.Source,
                        Environment.NewLine, item.Message));
                }
            }

            // 设置异常为已观察，防止程序崩溃
            e.SetObserved();
        }
        catch (Exception ex)
        {
            // 防止处理异常时再次抛出异常
            LoggerHelper.Error("处理未观察任务异常时发生错误:", ex);
            e.SetObserved();
        }
    }
}
