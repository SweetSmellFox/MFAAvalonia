using Avalonia;
using Avalonia.Controls;
using MaaFramework.Binding;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Windows;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace MFAAvalonia;

sealed class Program
{
    public static Dictionary<string, string> ParseArguments(string[] args)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            // 识别以 "-" 或 "--" 开头的键
            if (args[i].StartsWith("-"))
            {
                string key = args[i].TrimStart('-').ToLower();
                // 检查下一个元素是否为值（非键）
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    parameters[key] = args[i + 1];
                    i++; // 跳过已处理的值
                }
                else
                {
                    parameters[key] = ""; // 标记无值的键
                }
            }
        }
        return parameters;
    }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    public static Dictionary<string, string> Args { get; private set; } = new();
    private static Mutex? _mutex;
    private static bool _mutexReleased = false;
    private static readonly object _mutexLock = new();
    private static int _mutexOwnerThreadId = -1;
    public static bool IsNewInstance = true;

    public static void ReleaseMutex()
    {

        if (_mutexReleased || _mutex == null)
        {
            return;
        }

        // 检查当前线程是否是获取 Mutex 的线程
        if (Environment.CurrentManagedThreadId != _mutexOwnerThreadId)
        {
            // 如果不是，尝试通过 UI 线程（主线程）来释放
            // 因为在 Avalonia 中，UI 线程就是主线程
            try
            {

                // 同步调用到 UI 线程执行释放
                _ = DispatcherHelper.RunOnMainThreadAsync(ReleaseMutexInternal);

            }
            catch (Exception)
            {
                // Dispatcher 可能已经关闭，直接关闭 Mutex 句柄
                try
                {
                    _mutex?.Close();
                    _mutex = null;
                    _mutexReleased = true;
                }
                catch
                {
                    // 忽略
                }
            }
            return;
        }

        ReleaseMutexInternal();

    }

    private static void ReleaseMutexInternal()
    {
        lock (_mutexLock)
        {
            if (_mutexReleased || _mutex == null)
            {
                return;
            }

            try
            {
                _mutex.ReleaseMutex();
                _mutex.Close();
                _mutex = null;
                _mutexReleased = true;
            }
            catch (ApplicationException)
            {
                // Mutex was not owned by the current thread, just close it
                try
                {
                    _mutex?.Close();
                    _mutex = null;
                    _mutexReleased = true;
                }
                catch (Exception)
                {
                    // 忽略关闭时的异常
                }
            }
            catch (Exception e)
            {
                LoggerHelper.Error(e);
            }
        }
    }

    [STAThread]
    public static void Main(string[] args)
    {

        try
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);

            PrivatePathHelper.CleanupDuplicateLibraries(AppContext.BaseDirectory, AppContext.GetData("SubdirectoriesToProbe") as string);

            PrivatePathHelper.SetupNativeLibraryResolver();

            List<string> resultDirectories = new List<string>();

            // 获取应用程序基目录
            string baseDirectory = AppContext.BaseDirectory;

            // 构建runtimes文件夹路径
            string runtimesPath = Path.Combine(baseDirectory, "runtimes");

            // 检查runtimes文件夹是否存在
            if (!Directory.Exists(runtimesPath))
            {
                try
                {
                    LoggerHelper.Warning("runtimes文件夹不存在");
                }
                catch
                {
                }
            }
            else
            {
                // 搜索runtimes文件夹及其子目录中所有名为"MaaFramework"的文件（不限扩展名）
                var maaFiles = Directory.EnumerateFiles(
                    runtimesPath,
                    "*MaaFramework*",
                    SearchOption.AllDirectories
                );

                foreach (var filePath in maaFiles)
                {
                    var fileDirectory = Path.GetDirectoryName(filePath);
                    if (!resultDirectories.Contains(fileDirectory) && fileDirectory?.Contains(VersionChecker.GetNormalizedArchitecture()) == true)
                    {
                        resultDirectories.Add(fileDirectory);
                    }
                }
                try
                {
                    LoggerHelper.Info("MaaFramework runtimes: " + JsonConvert.SerializeObject(resultDirectories, Formatting.Indented));
                }
                catch { }
                NativeBindingContext.AppendNativeLibrarySearchPaths(resultDirectories);
            }

            var parsedArgs = ParseArguments(args);
            var mutexName = "MFAAvalonia_"
                + RootViewModel.Version
                + "_"
                + Directory.GetCurrentDirectory().Replace("\\", "_")
                    .Replace("/", "_")
                    .Replace(":", string.Empty);
            _mutex = new Mutex(true, mutexName, out IsNewInstance);
            _mutexOwnerThreadId = Environment.CurrentManagedThreadId;

            try
            {
                LoggerHelper.Info("Args: " + JsonConvert.SerializeObject(parsedArgs, Formatting.Indented));
                LoggerHelper.Info("MFA version: " + RootViewModel.Version);
                LoggerHelper.Info(".NET version: " + RuntimeInformation.FrameworkDescription);
            }
            catch { }
            Args = parsedArgs;

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
        }
        catch (Exception e)
        {

            try
            {
                LoggerHelper.Error($"总异常捕获：{e}");
            }
            catch { }
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
