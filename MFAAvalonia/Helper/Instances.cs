using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Utilities.Attributes;
using MFAAvalonia.ViewModels.Pages;
using MFAAvalonia.ViewModels.UsersControls.Settings;
using MFAAvalonia.ViewModels.Windows;
using MFAAvalonia.Views.Pages;
using MFAAvalonia.Views.UserControls.Settings;
using MFAAvalonia.Views.Windows;
using System;
using Microsoft.Extensions.DependencyInjection;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace MFAAvalonia.Helper;

#pragma warning disable CS0169 // The field is never used
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor

[LazyStatic]
public static partial class Instances
{
    #region Core Resolver

    private static readonly ConcurrentDictionary<Type, Lazy<object>> ServiceCache = new();

    /// <summary>
    /// 解析服务（自动缓存 + 循环依赖检测）
    /// </summary>
    private static T Resolve<T>() where T : class
    {
        var serviceType = typeof(T);

        var lazy = ServiceCache.GetOrAdd(serviceType, _ =>
            new Lazy<object>(
                () =>
                {
                    if (Design.IsDesignMode)
                    {
                        try
                        {
                            // 设计时核心逻辑：接口自动匹配实现类，普通类直接创建
                            object designInstance;

                            if (serviceType.IsInterface)
                            {
                                // 1. 接口类型：去掉"I"前缀，查找对应的实现类
                                designInstance = CreateInstanceFromInterface<T>(serviceType);
                            }
                            else
                            {
                                // 2. 普通类（非接口）：直接创建实例
                                designInstance = Activator.CreateInstance<T>()!;
                            }

                            LoggerHelper.Info($"设计时模式：成功创建 {serviceType.Name} 实例（实际类型：{designInstance.GetType().Name}）并缓存");
                            return designInstance;
                        }
                        catch (MissingMethodException ex)
                        {
                            throw new InvalidOperationException(
                                $"设计时模式下，{serviceType.Name}（或其实现类）缺少无参构造函数！请添加默认构造函数。", ex);
                        }
                        catch (TypeLoadException ex)
                        {
                            throw new InvalidOperationException(
                                $"设计时模式下，未找到 {serviceType.Name} 对应的实现类！请检查：1. 实现类命名是否为「去掉I前缀」（如 ISukiToastManager → SukiToastManager）；2. 实现类与接口在同一命名空间；3. 实现类已编译到项目中。", ex);
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException(
                                $"设计时模式下创建 {serviceType.Name} 实例失败：{ex.Message}", ex);
                        }
                    }

                    // 运行时：走原有DI容器解析逻辑（不受影响）
                    try
                    {
                        if (App.Services == null)
                            throw new NullReferenceException("App.Services 未初始化（运行时必须先配置依赖注入）");

                        var runtimeInstance = App.Services.GetRequiredService<T>();
                        return runtimeInstance;
                    }
                    catch (InvalidOperationException ex)
                    {
                        throw new InvalidOperationException(
                            $"运行时解析 {serviceType.Name} 失败。可能原因：1. 服务未注册；2. 循环依赖；3. 初始化时线程竞争。", ex);
                    }
                    catch (NullReferenceException ex)
                    {
                        throw new InvalidOperationException(
                            $"运行时解析 {serviceType.Name} 失败：App.Services 为 null，请检查依赖注入初始化逻辑。", ex);
                    }
                },
                LazyThreadSafetyMode.ExecutionAndPublication
            ));

        return (T)lazy.Value;
    }

    /// <summary>
    /// 从接口类型创建实现类实例（设计时专用）
    /// 规则：去掉接口的"I"前缀，查找同一命名空间下的实现类
    /// </summary>
    private static T CreateInstanceFromInterface<T>(Type interfaceType) where T : class
    {
        // 校验接口命名（必须以"I"开头，且长度>1）
        if (!interfaceType.Name.StartsWith("I", StringComparison.Ordinal) || interfaceType.Name.Length <= 1)
        {
            throw new InvalidOperationException($"接口 {interfaceType.Name} 命名不规范，无法自动匹配实现类（需以'I'开头，如 ISukiToastManager）");
        }

        // 生成实现类名：去掉"I"前缀（如 ISukiToastManager → SukiToastManager）
        string implementationClassName = interfaceType.Name.Substring(1);

        // 查找实现类：在接口所在的程序集中，查找同名（去掉I）的非接口类
        Type? implementationType = interfaceType.Assembly.GetTypes()
            .FirstOrDefault(t =>
                t.Name == implementationClassName
                && // 类名匹配
                !t.IsInterface
                && // 不是接口
                !t.IsAbstract
                && // 不是抽象类
                interfaceType.IsAssignableFrom(t)); // 实现了当前接口

        if (implementationType == null)
        {
            // 尝试容错：忽略大小写匹配（比如 ISukiToastManager → sukiToastManager，可选）
            implementationType = interfaceType.Assembly.GetTypes()
                .FirstOrDefault(t =>
                    string.Equals(t.Name, implementationClassName, StringComparison.OrdinalIgnoreCase) && !t.IsInterface && !t.IsAbstract && interfaceType.IsAssignableFrom(t));
        }

        if (implementationType == null)
        {
            throw new TypeLoadException($"未找到 {interfaceType.Name} 的实现类（期望类名：{implementationClassName}）");
        }

        // 创建实现类实例（要求实现类有无参构造函数）
        var instance = Activator.CreateInstance(implementationType);
        if (instance == null)
        {
            throw new InvalidOperationException($"实现类 {implementationType.Name} 无法创建实例（可能是抽象类或无无参构造函数）");
        }

        return (T)instance;
    }

    #endregion

    /// <summary>
    /// 关闭当前应用程序
    /// </summary>
    public static void ShutdownApplication()
    {
        ShutdownApplication(false);
    }

    public static void ShutdownApplication(bool forceStop)
    {
        Program.ReleaseMutex();
        if (forceStop)
        {
            // 强制退出时，只做最基本的清理，避免卡住
            RootView.BeforeClosed(true, false);
            Environment.Exit(0);
            return;
        }
        // 使用异步投递避免从后台线程同步调用UI线程导致死锁
        // 然后使用 Environment.Exit 确保进程退出
        DispatcherHelper.PostOnMainThread(() => ApplicationLifetime.Shutdown());
    }

    /// <summary>
    /// 重启当前应用程序
    /// </summary>
    public static void RestartApplication(bool noAutoStart = false, bool forgeStop = false)
    {
        Program.ReleaseMutex();
        if (noAutoStart)
            GlobalConfiguration.SetValue(ConfigurationKeys.NoAutoStart, bool.TrueString);
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = GetExecutablePath(),
                UseShellExecute = true
            }
        };

        try
        {
            process.Start();
            if (forgeStop)
                Environment.Exit(0);
            else
                ShutdownApplication();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"重启失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 关闭操作系统（需要管理员权限）
    /// </summary>
    public static void ShutdownSystem()
    {
        RootView.BeforeClosed();
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start("shutdown", "/s /t 0");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("shutdown", "-h now");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("sudo", "shutdown -h now");
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"关机失败: {ex.Message}");
        }
        ShutdownApplication();
    }
    /// <summary>
    /// 跨平台重启操作系统（需要管理员/root权限）
    /// </summary>
    public static void RestartSystem()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows重启命令[8,3](@ref)
                using var process = new Process();
                process.StartInfo.FileName = "shutdown.exe";
                process.StartInfo.Arguments = "/r /t 0 /f"; // /f 强制关闭所有程序
                process.StartInfo.UseShellExecute = true;
                process.StartInfo.Verb = "runas"; // 请求管理员权限
                process.Start();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Linux重启命令[7,3](@ref)
                using var process = new Process();
                process.StartInfo.FileName = "/bin/bash";
                process.StartInfo.Arguments = "-c \"sudo shutdown -r now\"";
                process.StartInfo.RedirectStandardInput = true;
                process.Start();
                process.StandardInput.WriteLine("password"); // 需替换实际密码或配置免密sudo
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // macOS重启命令[3,7](@ref)
                using var process = new Process();
                process.StartInfo.FileName = "/usr/bin/sudo";
                process.StartInfo.Arguments = "shutdown -r now";
                process.StartInfo.UseShellExecute = true;
                process.Start();
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"重启失败: {ex.Message}");
            // 备用方案：尝试通用POSIX命令
            TryFallbackReboot();
        }
    }

    /// <summary>
    /// 备用重启方案（兼容非标准环境）
    /// </summary>
    private static void TryFallbackReboot()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = MFAExtensions.GetFallbackCommand(),
                UseShellExecute = RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                CreateNoWindow = true
            };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi.Arguments = "/c shutdown /r /t 0";
            }
            else
            {
                psi.Arguments = "-c \"sudo reboot\"";
            }

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"备用重启方案失败: {ex.Message}");
        }
    }


    public static string GetExecutablePath()
    {
        // 兼容.NET 5+环境
        return Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? AppContext.BaseDirectory;
    }

    private static IClassicDesktopStyleApplicationLifetime _applicationLifetime;
    private static ISukiToastManager _toastManager;
    private static ISukiDialogManager _dialogManager;

    private static RootView _rootView;
    private static RootViewModel _rootViewModel;

    private static TaskQueueView _taskQueueView;
    private static TaskQueueViewModel _taskQueueViewModel;
    private static SettingsView _settingsView;
    private static SettingsViewModel _settingsViewModel;
    private static ResourcesView _resourcesView;
    private static ResourcesViewModel _resourcesViewModel;
    private static ScreenshotView _screenshotView;
    private static ScreenshotViewModel _screenshotViewModel;

    private static ConnectSettingsUserControl _connectSettingsUserControl;
    private static ConnectSettingsUserControlModel _connectSettingsUserControlModel;
    private static GuiSettingsUserControl _guiSettingsUser;
    private static GuiSettingsUserControlModel _guiSettingsUserControlModel;
    private static ConfigurationMgrUserControl _configurationMgrUserControl;
    private static ExternalNotificationSettingsUserControl _externalNotificationSettingsUserControl;
    private static ExternalNotificationSettingsUserControlModel _externalNotificationSettingsUserControlModel;
    private static TimerSettingsUserControl _timerSettingsUserControl;
    private static TimerSettingsUserControlModel _timerSettingsUserControlModel;
    private static PerformanceUserControl _performanceUserControl;
    private static PerformanceUserControlModel _performanceUserControlModel;
    private static GameSettingsUserControl _gameSettingsUserControl;
    private static GameSettingsUserControlModel _gameSettingsUserControlModel;
    private static VersionUpdateSettingsUserControl _versionUpdateSettingsUserControl;
    private static VersionUpdateSettingsUserControlModel _versionUpdateSettingsUserControlModel;
    private static StartSettingsUserControl _startSettingsUserControl;
    private static StartSettingsUserControlModel _startSettingsUserControlModel;
    private static AboutUserControl _aboutUserControl;
    private static HotKeySettingsUserControl _hotKeySettingsUserControl;
}
