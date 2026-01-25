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
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);

            PrivatePathHelper.CleanupDuplicateLibraries(AppContext.BaseDirectory, AppContext.GetData("SubdirectoriesToProbe") as string);

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
                LoggerHelper.Info(".NET version: " + RuntimeInformation.FrameworkDescription);
            }
            catch
            {
            }

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

            // 显示启动失败的错误对话框
            ShowStartupErrorDialog(e);
            
            // 确保进程退出
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// 显示启动失败的错误对话框
    /// </summary>
    private static void ShowStartupErrorDialog(Exception exception)
    {
        try
        {
            // 构建错误消息
            var errorMessage = new StringBuilder();
            errorMessage.AppendLine("MFAAvalonia 启动失败");
            errorMessage.AppendLine();
            errorMessage.AppendLine("错误信息：");
            errorMessage.AppendLine(exception.Message);
            errorMessage.AppendLine();
            errorMessage.AppendLine("详细信息：");
            errorMessage.AppendLine(exception.ToString());

            var message = errorMessage.ToString();

            // 尝试使用 Avalonia 显示错误对话框
            try
            {
                var app = BuildAvaloniaApp();
                var lifetime = new ClassicDesktopStyleApplicationLifetime
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
                app.SetupWithLifetime(lifetime);

                // 在UI 线程上显示对话框
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var window = new Window
                    {
                        Title = "MFAAvalonia 启动失败",
                        Width = 600,
                        Height = 400,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen,
                        CanResize = true
                    };

                    var textBox = new TextBox
                    {
                        Text = message,
                        IsReadOnly = true,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        AcceptsReturn = true,
                Margin = new Avalonia.Thickness(10)
                    };

                    var scrollViewer = new ScrollViewer
                    {
                        Content = textBox
                    };

                    var button = new Button
                    {
                        Content = "确定",
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        Margin = new Avalonia.Thickness(10),Padding = new Avalonia.Thickness(20, 5)
                    };

                    button.Click += (s, e) => window.Close();

                    var panel = new StackPanel();
                    panel.Children.Add(scrollViewer);
                    panel.Children.Add(button);

                    window.Content = panel;

                    await window.ShowDialog(null);
                    lifetime.Shutdown();
                }).Wait();
            }
            catch
            {
                // 如果Avalonia 对话框失败，尝试使用系统原生消息框
                ShowNativeMessageBox(message);
            }
        }
        catch (Exception ex)
        {
            // 最后的备用方案：输出到控制台
            Console.Error.WriteLine("=== MFAAvalonia 启动失败 ===");
            Console.Error.WriteLine(exception.ToString());
            Console.Error.WriteLine();
            Console.Error.WriteLine("显示错误对话框时也发生了错误：");
            Console.Error.WriteLine(ex.ToString());
        }
    }

    /// <summary>
    /// 显示系统原生消息框
    /// </summary>
    private static void ShowNativeMessageBox(string message)
    {
        try
        {
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
            // 如果原生消息框也失败，输出到控制台
            Console.Error.WriteLine("=== MFAAvalonia 启动失败 ===");
            Console.Error.WriteLine(message);
        }
    }

    // Windows MessageBox P/Invoke
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
    

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
