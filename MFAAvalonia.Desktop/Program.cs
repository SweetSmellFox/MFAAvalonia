using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using MaaFramework.Binding;
using MFAAvalonia;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Windows;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.Desktop;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);PrivatePathHelper.CleanupDuplicateLibraries(AppContext.BaseDirectory, AppContext.GetData("SubdirectoriesToProbe") as string);

            PrivatePathHelper.SetupNativeLibraryResolver();

            List<string> resultDirectories = new();

            string baseDirectory = AppContext.BaseDirectory;

            string runtimesPath = Path.Combine(baseDirectory, "runtimes");

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
                catch
                {
                }
                NativeBindingContext.AppendNativeLibrarySearchPaths(resultDirectories);
            }

            var mutexName = "MFAAvalonia_"
                + RootViewModel.Version
                + "_"
                + Directory.GetCurrentDirectory().Replace("\\", "_")
                    .Replace("/", "_")
                    .Replace(":", string.Empty);

            AppRuntime.Initialize(args, mutexName);

            try
            {
                LoggerHelper.Info("Args: " + JsonConvert.SerializeObject(AppRuntime.Args, Formatting.Indented));
                LoggerHelper.Info("MFA version: " + RootViewModel.Version);
                LoggerHelper.Info(".NET version: " + RuntimeInformation.FrameworkDescription);}
            catch
            {}
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
        }
        catch (Exception e)
        {
            try
            {
                LoggerHelper.Error($"启动失败，总异常捕获：{e}");
            }
            catch
            {
            }

            // 使用 App 类的统一错误处理方法（确保只显示一次）
            App.ShowStartupErrorAndExit(e, "程序启动");
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
