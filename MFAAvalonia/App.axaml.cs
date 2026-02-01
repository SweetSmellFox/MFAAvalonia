using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Other;
using MFAAvalonia.ViewModels.Pages;
using MFAAvalonia.ViewModels.UsersControls;
using MFAAvalonia.ViewModels.UsersControls.Settings;
using MFAAvalonia.ViewModels.Windows;
using MFAAvalonia.Views.Mobile;
using MFAAvalonia.Views.Pages;
using MFAAvalonia.Views.UserControls;
using MFAAvalonia.Views.UserControls.Settings;
using MFAAvalonia.Views.Windows;
using Microsoft.Extensions.DependencyInjection;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;

namespace MFAAvalonia;

public partial class App : Application
{
    /// <summary>
    /// Gets services.
    /// </summary>
    public static IServiceProvider Services { get; private set; }

    /// <summary>
    /// 内存优化器实例（保存引用以便在退出时释放）
    /// </summary>
    private static AvaloniaMemoryCracker? _memoryCracker;

        /// <summary>
        /// 标记是否已经显示过启动错误对话框（确保只显示一次）
        /// 使用 int 类型以便使用 Interlocked 原子操作
        /// 0 = 未显示，1 = 已显示
        /// </summary>
        private static int _hasShownStartupError = 0;

    public override void Initialize()
    {
        try
        {
            base.Initialize();
            LoggerHelper.InitializeLogger();
            AvaloniaXamlLoader.Load(this);
            LanguageHelper.Initialize();
            ConfigurationManager.Initialize();
            FontService.Initialize();

            // 保存引用以便在退出时正确释放
            _memoryCracker = new AvaloniaMemoryCracker();
            _memoryCracker.Cracker();

            GlobalHotkeyService.Initialize();
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException; //Task线程内未捕获异常处理事件
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException; //非UI线程内未捕获异常处理事件
            Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException; //UI线程内未捕获异常处理事件
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"应用初始化失败：{ex}");
            ShowStartupErrorAndExit(ex, "应用初始化");
        }
    }


    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.ShutdownRequested += OnShutdownRequested;
                var services = new ServiceCollection();

                services.AddSingleton(desktop);

                ConfigureServices(services);

                var views = ConfigureViews(services);

                Services = services.BuildServiceProvider();

                MaaProcessorManager.Instance.LoadInstanceConfig();

                DataTemplates.Add(new ViewLocator(views));

                var window = views.CreateView<RootViewModel>(Services) as Window;

                desktop.MainWindow = window;

                TrayIconManager.InitializeTrayIcon(this, Instances.RootView, Instances.RootViewModel);

                if (GlobalConfiguration.HasFileAccessError)
                {
                    var message = $"全局配置文件被占用或无权限访问：{GlobalConfiguration.ConfigPath}\n{GlobalConfiguration.LastFileAccessErrorMessage}";
                    DispatcherHelper.PostOnMainThread(() =>
                        Instances.DialogManager.CreateDialog()
                            .OfType(NotificationType.Error)
                            .WithContent(message)
                            .WithActionButton(LangKeys.Ok.ToLocalization(), _ => { }, true)
                            .TryShow());
                }
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            {
                var services = new ServiceCollection();

                services.AddSingleton(singleView);

                ConfigureServices(services);

                var views = ConfigureViews(services);

                Services = services.BuildServiceProvider();

                MaaProcessorManager.Instance.LoadInstanceConfig();

                DataTemplates.Add(new ViewLocator(views));

                var mainView = views.CreateView<RootViewModel>(Services);

                singleView.MainView = mainView;

                if (GlobalConfiguration.HasFileAccessError)
                {
                    var message = $"全局配置文件被占用或无权限访问：{GlobalConfiguration.ConfigPath}\n{GlobalConfiguration.LastFileAccessErrorMessage}";
                    DispatcherHelper.PostOnMainThread(() =>
                        Instances.DialogManager.CreateDialog()
                            .OfType(NotificationType.Error)
                            .WithContent(message)
                            .WithActionButton(LangKeys.Ok.ToLocalization(), _ => { }, true)
                            .TryShow());
                }
            }

            base.OnFrameworkInitializationCompleted();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"框架初始化失败：{ex}");
            ShowStartupErrorAndExit(ex, "框架初始化");
        }
    }


    private void OnShutdownRequested(object sender, ShutdownRequestedEventArgs e)
    {
        TrayIconManager.DisposeTrayIcon(this);

        foreach (var p in MaaProcessor.Processors)
        {
            if (p.ViewModel != null)
            {
                p.InstanceConfiguration.SetValue(ConfigurationKeys.TaskItems,
                    p.ViewModel.TaskItemViewModels.ToList().Select(model => model.InterfaceItem));
            }
            p.SetTasker();
        }
        GlobalHotkeyService.Shutdown();

        // 强制清理所有应用资源（包括字体）
        ForceCleanupAllResources();

        // 释放内存优化器
        _memoryCracker?.Dispose();
        _memoryCracker = null;

        // 取消全局异常事件订阅，避免内存泄漏
        TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
        Dispatcher.UIThread.UnhandledException -= OnDispatcherUnhandledException;
    }

    /// <summary>
    /// 手动清理内存缓存（用于降低内存占用）
    /// 此方法会清除字体缓存等非必要的内存占用
    /// </summary>
    public static void ClearMemoryCaches()
    {
        try
        {
            // 清除字体缓存（保留当前使用的字体）
            FontService.Instance.ClearFontCache();

            LoggerHelper.Info("[内存管理]已清除应用程序缓存");
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"[内存管理]清除缓存时发生错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 强制清理所有资源（用于应用退出）
    /// </summary>
    private static void ForceCleanupAllResources()
    {
        try
        {
            // 强制清理所有字体资源
            FontService.Instance.ForceCleanupAllFontResources();

            LoggerHelper.Info("[内存管理]已强制清理所有应用资源");
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"[内存管理]强制清理资源时发生错误: {ex.Message}");
        }
    }

    private static ViewsHelper ConfigureViews(ServiceCollection services)
    {

        var views = new ViewsHelper();

        if (OperatingSystem.IsAndroid())
        {
            views.AddView<RootViewMobile, RootViewModel>(services);
        }
        else
        {
            views.AddView<RootView, RootViewModel>(services);
        }

        return views

            // Add pages
            .AddView<InstanceContainerView,InstanceTabBarViewModel>(services)
            .AddView<TaskQueueView, TaskQueueViewModel>(services)
            .AddView<ResourcesView, ResourcesViewModel>(services)
            .AddView<SettingsView, SettingsViewModel>(services)
            .AddView<ScreenshotView, ScreenshotViewModel>(services)

            // Add additional views
            .AddView<AddTaskDialogView, AddTaskDialogViewModel>(services)
            .AddView<TaskRemarkDialogView, TaskRemarkDialogViewModel>(services)
            .AddView<AdbEditorDialogView, AdbEditorDialogViewModel>(services)
            .AddView<PlayCoverEditorDialog, PlayCoverEditorDialogViewModel>(services)
            .AddView<RenameInstanceDialog, RenameInstanceDialogViewModel>(services)
            .AddView<MultiInstanceEditorDialogView, MultiInstanceEditorDialogViewModel>(services)
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
            // 如果已经显示过启动错误，不再显示其他错误对话框
            if (System.Threading.Interlocked.CompareExchange(ref _hasShownStartupError, 0, 0) == 1)
            {
                LoggerHelper.Error($"启动失败后的UI线程异常（已忽略显示）: {e.Exception}");
                e.Handled = true;
                return;
            }

            if (TryIgnoreException(e.Exception, out string errorMessage))
            {
                LoggerHelper.Warning(errorMessage);
                LoggerHelper.Error(e.Exception.ToString());
                e.Handled = true;
                return;
            }

            e.Handled = true;
            LoggerHelper.Error(e.Exception);
            ErrorView.ShowException(e.Exception);
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("处理UI线程异常时发生错误: " + ex.ToString());
            if (System.Threading.Interlocked.CompareExchange(ref _hasShownStartupError, 0, 0) == 0)
            {
                ErrorView.ShowException(ex, true);
            }
        }
    }

    void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            // 如果已经显示过启动错误，不再显示其他错误对话框
            if (System.Threading.Interlocked.CompareExchange(ref _hasShownStartupError, 0, 0) == 1)
            {
                LoggerHelper.Error($"启动失败后的非UI线程异常（已忽略显示）: {e.ExceptionObject}");
                return;
            }

            if (e.ExceptionObject is Exception ex && TryIgnoreException(ex, out string errorMessage))
            {
                LoggerHelper.Warning(errorMessage);
                LoggerHelper.Error(ex.ToString());
                return;
            }

            var sbEx = new StringBuilder();
            if (e.IsTerminating)
                sbEx.Append("非UI线程发生致命错误");
            else
                sbEx.Append("非UI线程异常：");

            if (e.ExceptionObject is Exception ex2)
            {
                ErrorView.ShowException(ex2);
                sbEx.Append(ex2);
            }
            else
            {
                sbEx.Append(e.ExceptionObject);
            }
            LoggerHelper.Error(sbEx.ToString());
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("处理非UI线程异常时发生错误: " + ex.ToString());
        }
    }

    void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            // 如果已经显示过启动错误，不再显示其他错误对话框
            if (System.Threading.Interlocked.CompareExchange(ref _hasShownStartupError, 0, 0) == 1)
            {
                LoggerHelper.Error($"启动失败后的任务异常（已忽略显示）: {e.Exception}");
                e.SetObserved();
                return;
            }

            if (TryIgnoreException(e.Exception, out string errorMessage))
            {
                LoggerHelper.Warning(errorMessage);
                LoggerHelper.Info(e.Exception.ToString());
            }
            else
            {
                LoggerHelper.Error(e.Exception);
                ErrorView.ShowException(e.Exception);

                foreach (var item in e.Exception.InnerExceptions ?? Enumerable.Empty<Exception>())
                {
                    LoggerHelper.Error(string.Format("异常类型：{0}{1}来自：{2}{3}异常内容：{4}",
                        item.GetType(), Environment.NewLine, item.Source,
                        Environment.NewLine, item.Message));
                }
            }

            e.SetObserved();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error("处理未观察任务异常时发生错误: " + ex.ToString());
            e.SetObserved();
        }
    }

// 统一的异常过滤方法，返回是否应该忽略以及对应的错误消息
    private bool TryIgnoreException(Exception ex, out string errorMessage)
    {
        errorMessage = string.Empty;

        // 递归检查InnerException
        if (ex.InnerException != null && TryIgnoreException(ex.InnerException, out errorMessage))
            return true;

        // 检查AggregateException的所有InnerExceptions
        if (ex is AggregateException aggregateEx)
        {
            foreach (var innerEx in aggregateEx.InnerExceptions)
            {
                if (TryIgnoreException(innerEx, out errorMessage))
                    return true;
            }
        }
        if (ex is IOException exception && exception.Message.Contains("EOF"))
        {
            errorMessage = "SSL验证证书错误";
            LoggerHelper.Warning(exception);
            return true;
        }

        // 检查特定类型的异常并设置对应的错误消息
        if (ex is OperationCanceledException)
        {
            errorMessage = "已忽略任务取消异常";
            return true;
        }

        if (ex is InvalidOperationException && ex.Message.Contains("Stop"))
        {
            errorMessage = "已忽略与Stop相关的异常: " + ex.Message;
            return true;
        }

        if (ex.GetType().FullName == "SharpHook.HookException")
        {
            errorMessage = "macOS中的全局快捷键Hook异常，可能是由于权限不足或系统限制导致的";
            return true;
        }

        if (ex is AuthenticationException)
        {
            errorMessage = "SSL验证证书错误";
            return true;
        }

        if (ex is SocketException)
        {
            errorMessage = "代理设置的SSL验证错误";
            return true;
        }

        // 检查 DBus 异常（仅在 Linux 上可用）
        if (TryHandleDBusException(ex, out errorMessage))
        {
            return true;
        }

//忽略 SEHException，这通常是由于外部组件（如 MaaFramework）的问题导致的
// 这些异常已经在业务逻辑中处理了（如显示连接失败消息），不应该再次显示给用户
        if (ex is SEHException)
        {
            errorMessage = "已忽略外部组件异常(SEHException): " + ex.Message;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 尝试处理 DBus 异常（仅在 Linux 上可用）
    /// 使用反射来避免在 Windows 上加载 Tmds.DBus.Protocol 程序集
    /// </summary>
    private static bool TryHandleDBusException(Exception ex, out string errorMessage)
    {
        errorMessage = string.Empty;
        try
        {
            // 检查异常类型名称，避免直接引用 Tmds.DBus.Protocol 类型
            var exType = ex.GetType();
            if (exType.FullName == "Tmds.DBus.Protocol.DBusException")
            {
                // 使用反射获取 ErrorName 和 Message 属性
                var errorNameProp = exType.GetProperty("ErrorName");
                var errorName = errorNameProp?.GetValue(ex) as string;

                if (errorName == "org.freedesktop.DBus.Error.ServiceUnknown" && ex.Message.Contains("com.canonical.AppMenu.Registrar"))
                {
                    errorMessage = "检测到DBus服务(com.canonical.AppMenu.Registrar)不可用，这在非Unity桌面环境中是正常现象";
                    return true;
                }
            }
        }
        catch
        {
            // 如果反射失败，忽略错误
        }

        return false;
    }
    
    /// <summary>
    /// 显示启动错误并退出应用（确保只显示一次）
    /// 使用 Interlocked 原子操作确保线程安全
    /// </summary>
    public static void ShowStartupErrorAndExit(Exception exception, string stage = "启动")
    {
        // 使用原子操作检查并设置标志，确保只有一个线程能进入
        if (System.Threading.Interlocked.CompareExchange(ref _hasShownStartupError, 1, 0) != 0)
        {
            // 已经有其他线程在显示错误对话框，直接退出
            System.Threading.Thread.Sleep(100); // 等待一下让第一个线程显示对话框
            Environment.Exit(1);
            return;
        }

        try
        {
            var message = $"MFAAvalonia {stage}失败\n\n错误信息：\n{exception.Message}\n\n详细信息：\n{exception}";
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows: 使用 MessageBox
                var shortMessage = message.Length > 500
                    ? message.Substring(0, 500) + "...\n\n详细信息请查看日志文件。"
                    : message;
                
                MessageBox(IntPtr.Zero, shortMessage, "MFAAvalonia 启动失败", 0x10); // MB_ICONERROR
            }
            else
            {
                // Linux/macOS: 输出到控制台
                Console.Error.WriteLine("=== MFAAvalonia 启动失败 ===");
                Console.Error.WriteLine(message);
            }
        }
        catch
        {
            Console.Error.WriteLine("=== MFAAvalonia 启动失败 ===");
            Console.Error.WriteLine(exception.ToString());
        }
        
        Environment.Exit(1);
    }

    // Windows MessageBox P/Invoke
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
