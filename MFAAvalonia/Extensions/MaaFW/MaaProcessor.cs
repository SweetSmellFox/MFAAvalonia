using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Interop.Native;
using MaaFramework.Binding.Notification;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW.Custom;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.Converters;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.Views.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using Brushes = Avalonia.Media.Brushes;
using MaaAgentClient = MaaFramework.Binding.MaaAgentClient;
using MaaContext = MaaFramework.Binding.MaaContext;
using MaaController = MaaFramework.Binding.MaaController;
using MaaGlobal = MaaFramework.Binding.MaaGlobal;
using MaaResource = MaaFramework.Binding.MaaResource;
using MaaTasker = MaaFramework.Binding.MaaTasker;
using MaaToolkit = MaaFramework.Binding.MaaToolkit;
using Pen = Avalonia.Media.Pen;
using Size = Avalonia.Size;

namespace MFAAvalonia.Extensions.MaaFW;
#pragma warning  disable CS4014 // 由于此调用不会等待，因此在此调用完成之前将会继续执行当前方法.
#pragma warning  disable CS1998 // 此异步方法缺少 "await" 运算符，将以同步方式运行。
#pragma warning disable CA1416 //  可在 'linux', 'macOS/OSX', 'windows' 上访问此调用站点。
public class MaaProcessor
{
    #region 属性

    private static Random Random = new();
    public static string Resource => Path.Combine(AppContext.BaseDirectory, "resource");
    public static string ResourceBase => Path.Combine(Resource, "base");
    public static MaaProcessor Instance { get; } = new();
    public static MaaToolkit Toolkit { get; } = new(true);

    public static MaaGlobal Global { get; } = new();

    private static MaaInterface? _interface;

    // public Dictionary<string, MaaNode> BaseNodes = new();
    //
    // public Dictionary<string, MaaNode> NodeDictionary = new();
    public ObservableQueue<MFATask> TaskQueue { get; } = new();
    public bool IsV2 = false;
    public MaaProcessor()
    {
        TaskQueue.CountChanged += (_, args) =>
        {
            if (args.NewValue > 0)
                Instances.RootViewModel.IsRunning = true;
        };
        CheckInterface(out _, out _, out _);
        var @interface = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "interface.json")));
        var interfaceVersion = @interface["interface_version"]?.ToString();
        if (int.TryParse(interfaceVersion, out var result) && result >= 2)
        {
            IsV2 = true;
        }
    }

    public static MaaInterface? Interface
    {
        get => _interface;
        private set
        {
            _interface = value;

            foreach (var customResource in value?.Resource ?? Enumerable.Empty<MaaInterface.MaaInterfaceResource>())
            {
                var nameKey = customResource.Name?.Trim() ?? string.Empty;
                var paths = MaaInterface.ReplacePlaceholder(customResource.Path ?? new(), AppContext.BaseDirectory);

                value!.Resources[nameKey] = new MaaInterface.MaaInterfaceResource
                {
                    Name = LanguageHelper.GetLocalizedString(nameKey),
                    Path = paths
                };
            }

            if (value != null)
            {
                Instances.SettingsViewModel.ShowResourceIssues = !string.IsNullOrWhiteSpace(value.Url);
                Instances.SettingsViewModel.ResourceGithub = value.Url;
                Instances.SettingsViewModel.ResourceIssues = $"{value.Url}/issues";
            }

        }
    }

    public static MaaFWConfiguration Config { get; } = new();
    public MaaTasker? MaaTasker { get; set; }

    public void SetTasker(MaaTasker? maaTasker = null)
    {
        if (maaTasker == null)
        {
            MaaTasker?.Stop().Wait();
            MaaTasker?.Dispose();
            _agentClient = null;
            _agentStarted = false;
            _agentProcess?.Kill();
            _agentProcess?.Dispose();
            _agentProcess = null;
            Instances.TaskQueueViewModel.SetConnected(false);
        }
        MaaTasker = maaTasker;
    }

    public MaaTasker? GetTasker(CancellationToken token = default)
    {
        var task = GetTaskerAsync(token);
        task.Wait(token);
        return task.Result;
    }

    public async Task<MaaTasker?> GetTaskerAsync(CancellationToken token = default)
    {
        MaaTasker ??= (await InitializeMaaTasker(token)).Item1;
        return MaaTasker;
    }
    public async Task<(MaaTasker?, bool)> GetTaskerAndBoolAsync(CancellationToken token = default)
    {
        var MaaTaskerTuple = await InitializeMaaTasker(token);
        MaaTasker ??= MaaTaskerTuple.Item1;
        return (MaaTasker, MaaTaskerTuple.Item2);
    }
    public ObservableCollection<DragItemViewModel> TasksSource { get; private set; } =
        [];
    public AutoInitDictionary AutoInitDictionary { get; } = new();

    private MaaAgentClient? _agentClient;
    private bool _agentStarted;
    private Process? _agentProcess;
    private MFATask.MFATaskStatus Status = MFATask.MFATaskStatus.NOT_STARTED;

    public Bitmap? GetBitmapImage(bool test = true)
    {
        if (test)
            TryConnectAsync(CancellationToken.None);
        using var buffer = GetImage(MaaTasker?.Controller);
        return buffer.ToBitmap();
    }

    public MaaImageBuffer GetImage(IMaaController? maaController)
    {
        var buffer = new MaaImageBuffer();
        if (maaController == null)
            return buffer;
        var status = maaController.Screencap().Wait();
        if (status != MaaJobStatus.Succeeded)
            return buffer;
        maaController.GetCachedImage(buffer);
        return buffer;
    }

    #endregion

    #region MaaTasker初始化

    private static string ConvertPath(string path)
    {
        if (Path.Exists(path) && !path.Contains("\""))
        {
            return $"\"{path}\"";
        }
        return path;
    }
    public static string FindPythonPath(string? program)
    {
        if (program != "python")
        {
            return program;
        }

        // 根据不同操作系统执行不同的查找逻辑
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return FindPythonPathOnWindows(program);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return FindPythonPathOnMacOS(program);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return FindPythonPathOnLinux(program);
        }
        else
        {
            // 未知操作系统，尝试通用查找
            return FindPythonPathGeneric(program);
        }
    }

    private static string FindPythonPathOnWindows(string program)
    {
        // 先检查 PATH 环境变量
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    var fullPath = Path.Combine(dir, $"{program}.exe");
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
                catch
                {
                    /* 忽略错误目录 */
                }
            }
        }

        // 尝试查找 Python 安装目录 (常见位置)
        var pythonDirs = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Python")
        };

        foreach (var baseDir in pythonDirs)
        {
            if (Directory.Exists(baseDir))
            {
                try
                {
                    // 优先选择版本号最高的目录
                    var pythonDir = Directory.GetDirectories(baseDir)
                        .OrderByDescending(d => d)
                        .FirstOrDefault();

                    if (pythonDir != null)
                    {
                        var pythonPath = Path.Combine(pythonDir, $"{program}.exe");
                        if (File.Exists(pythonPath))
                        {
                            return pythonPath;
                        }
                    }
                }
                catch
                {
                    /* 忽略错误 */
                }
            }
        }

        // 尝试查找 Anaconda/Miniconda
        var condaDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "anaconda3");
        if (Directory.Exists(condaDir))
        {
            var condaPythonPath = Path.Combine(condaDir, $"{program}.exe");
            if (File.Exists(condaPythonPath))
            {
                return condaPythonPath;
            }
        }

        return program; // 未找到，返回原程序名
    }

    private static string FindPythonPathOnMacOS(string program)
    {
        // 检查 PATH 环境变量
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    var fullPath = Path.Combine(dir, program);
                    if (File.Exists(fullPath) && IsExecutable(fullPath))
                    {
                        return fullPath;
                    }
                }
                catch
                {
                    /* 忽略错误目录 */
                }
            }
        }

        // 检查 Homebrew 安装位置
        var homebrewDir = "/usr/local/bin";
        var homebrewPath = Path.Combine(homebrewDir, program);
        if (File.Exists(homebrewPath) && IsExecutable(homebrewPath))
        {
            return homebrewPath;
        }

        // 检查 Python.org 安装位置
        var pythonOrgDir = "/Library/Frameworks/Python.framework/Versions";
        if (Directory.Exists(pythonOrgDir))
        {
            try
            {
                // 选择最新版本
                var versions = Directory.GetDirectories(pythonOrgDir)
                    .Select(Path.GetFileName)
                    .Where(v => v.StartsWith("3")) // 优先选择 Python 3
                    .OrderByDescending(v => new Version(v))
                    .ToList();

                foreach (var version in versions)
                {
                    var pythonPath = Path.Combine(pythonOrgDir, version, "bin", program);
                    if (File.Exists(pythonPath) && IsExecutable(pythonPath))
                    {
                        return pythonPath;
                    }
                }
            }
            catch
            {
                /* 忽略错误 */
            }
        }

        return program; // 未找到，返回原程序名
    }

    private static string FindPythonPathOnLinux(string program)
    {
        // 检查 PATH 环境变量
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    var fullPath = Path.Combine(dir, program);
                    if (File.Exists(fullPath) && IsExecutable(fullPath))
                    {
                        return fullPath;
                    }
                }
                catch
                {
                    /* 忽略错误目录 */
                }
            }
        }

        // 检查常见的 Python 安装位置
        var commonDirs = new[]
        {
            "/usr/bin",
            "/usr/local/bin",
            "/opt/python/bin"
        };

        foreach (var dir in commonDirs)
        {
            var fullPath = Path.Combine(dir, program);
            if (File.Exists(fullPath) && IsExecutable(fullPath))
            {
                return fullPath;
            }
        }

        return program; // 未找到，返回原程序名
    }

    private static string FindPythonPathGeneric(string program)
    {
        // 通用查找逻辑，适用于其他操作系统
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    var fullPath = Path.Combine(dir, program);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
                catch
                {
                    /* 忽略错误目录 */
                }
            }
        }

        return program; // 未找到，返回原程序名
    }

    // 检查文件是否具有可执行权限 (仅适用于 Unix-like 系统)
    private static bool IsExecutable(string path)
    {
        try
        {
            // 在 Windows 上不需要检查可执行权限
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return true;
            }

            // 使用 Linux/macOS 的 file 命令检查文件类型
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "file",
                Arguments = $"--brief --mime-type \"{path}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false
            });

            if (process != null)
            {
                process.WaitForExit();
                var output = process.StandardOutput.ReadToEnd().Trim();
                return output.Contains("executable") || output.Contains("application/x-executable");
            }

            return false;
        }
        catch
        {
            // 如果检查失败，默认认为文件存在即可执行
            return File.Exists(path);
        }
    }
    public static WriteableBitmap? DrawRectangleOnBitmap(Bitmap bitmap, int x, int y, int width, int height)
    {
        using (var context = new RenderTargetBitmap(
                   new PixelSize(bitmap.PixelSize.Width, bitmap.PixelSize.Height),
                   new Vector(96, 96)))
        {
            using (var drawingContext = context.CreateDrawingContext(false))
            {
                // 绘制原始位图
                drawingContext.DrawImage(bitmap, new Rect(new Size(bitmap.PixelSize.Width, bitmap.PixelSize.Height)));

                // 定义黄绿色画笔
                var pen = new Pen(
                    Brushes.YellowGreen,
                    1,
                    null,
                    PenLineCap.Square,
                    PenLineJoin.Miter);

                // 绘制矩形框
                drawingContext.DrawRectangle(
                    null,
                    pen,
                    new Rect(x, y, width, height));
            }

            // 将绘制结果复制回原始位图
            using (var writableBitmap = new WriteableBitmap(
                       context.PixelSize,
                       context.Dpi,
                       PixelFormat.Bgra8888,
                       AlphaFormat.Premul))
            {
                using (var buffer = writableBitmap.Lock())
                {
                    context.CopyPixels(
                        new PixelRect(0, 0, context.PixelSize.Width, context.PixelSize.Height),
                        buffer.Address,
                        buffer.RowBytes * buffer.Size.Height,
                        buffer.RowBytes);
                }

                // 转换回原始格式
                using (var tempBitmap = new WriteableBitmap(
                           bitmap.PixelSize,
                           bitmap.Dpi,
                           PixelFormat.Bgra8888,
                           AlphaFormat.Premul))
                {
                    using (var tempBuffer = tempBitmap.Lock())
                    {
                        writableBitmap.CopyPixels(
                            new PixelRect(0, 0, writableBitmap.PixelSize.Width, writableBitmap.PixelSize.Height),
                            tempBuffer.Address,
                            tempBuffer.RowBytes * tempBuffer.Size.Height,
                            tempBuffer.RowBytes);
                    }

                    return writableBitmap;

                }
            }
        }
    }

    async private Task<(MaaTasker?, bool)> InitializeMaaTasker(CancellationToken token) // 添加 async 和 token
    {
        var InvalidResource = false;

        AutoInitDictionary.Clear();
        LoggerHelper.Info(LangKeys.LoadingResources.ToLocalization());

        MaaResource maaResource = null;
        try
        {
            var resources = Instances.TaskQueueViewModel.CurrentResources
                    .FirstOrDefault(c => c.Name == Instances.TaskQueueViewModel.CurrentResource)?.Path
                ?? [];
            resources = resources.Select(Path.GetFullPath).ToList();

            LoggerHelper.Info($"Resource: {string.Join(",", resources)}");


            maaResource = await TaskManager.RunTaskAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                return new MaaResource(resources);
            }, token: token, name: "资源检测", catchException: true, shouldLog: false, handleError: exception =>
            {
                HandleInitializationError(exception, LangKeys.LoadResourcesFailed.ToLocalization());
                RootView.AddLog(LangKeys.LoadResourcesFailed.ToLocalization(), Brushes.OrangeRed, changeColor: false);
                InvalidResource = true;
            });

            Instances.PerformanceUserControlModel.ChangeGpuOption(maaResource, Instances.PerformanceUserControlModel.GpuOption);

            LoggerHelper.Info(
                $"GPU acceleration: {(Instances.PerformanceUserControlModel.GpuOption.IsDirectML ? Instances.PerformanceUserControlModel.GpuOption.Adapter.AdapterName : Instances.PerformanceUserControlModel.GpuOption.Device.ToString())}{(Instances.PerformanceUserControlModel.GpuOption.IsDirectML ? $",Adapter Id: {Instances.PerformanceUserControlModel.GpuOption.Adapter.AdapterId}" : "")}");

        }
        catch (OperationCanceledException)
        {
            LoggerHelper.Warning("Resource loading was canceled");
            return (null, InvalidResource);
        }
        catch (MaaException)
        {
            return (null, InvalidResource);
        }
        catch (Exception e)
        {

            LoggerHelper.Error("Initialization resource error", e);
            return (null, InvalidResource);
        }

        // 初始化控制器部分同理
        MaaController controller = null;
        try
        {
            controller = await TaskManager.RunTaskAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                return InitializeController(Instances.TaskQueueViewModel.CurrentController == MaaControllerTypes.Adb);
            }, token: token, name: "控制器检测", catchException: true, shouldLog: false, handleError: exception => HandleInitializationError(exception,
                LangKeys.ConnectingEmulatorOrWindow.ToLocalization()
                    .FormatWith(Instances.TaskQueueViewModel.CurrentController == MaaControllerTypes.Adb
                        ? LangKeys.Emulator.ToLocalization()
                        : LangKeys.Window.ToLocalization()), true,
                LangKeys.InitControllerFailed.ToLocalization()));
        }
        catch (OperationCanceledException)
        {
            LoggerHelper.Warning("Controller initialization was canceled");
            return (null, InvalidResource);
        }
        catch (MaaException)
        {
            return (null, InvalidResource);
        }
        catch (Exception e)
        {
            LoggerHelper.Error("Initialization controller error", e);
            return (null, InvalidResource);
        }

        try
        {
            token.ThrowIfCancellationRequested();


            var tasker = new MaaTasker
            {
                Controller = controller,
                Resource = maaResource,
                Toolkit = MaaProcessor.Toolkit,
                Global = MaaProcessor.Global,
                DisposeOptions = DisposeOptions.All,
            };

            try
            {
                var tempMFADir = Path.Combine(AppContext.BaseDirectory, "temp_mfa");
                if (Directory.Exists(tempMFADir))
                    Directory.Delete(tempMFADir, true);

                var tempMaaDir = Path.Combine(AppContext.BaseDirectory, "temp_maafw");
                if (Directory.Exists(tempMaaDir))
                    Directory.Delete(tempMaaDir, true);

                var tempResDir = Path.Combine(AppContext.BaseDirectory, "temp_res");
                if (Directory.Exists(tempResDir))
                    Directory.Delete(tempResDir, true);
            }
            catch (Exception e)
            {
                LoggerHelper.Error(e);
            }
//            tasker.Resource.Register(new JieGardenAction());
            // 获取代理配置（假设Interface在UI线程中访问）
            var agentConfig = Interface?.Agent;
            if (agentConfig is { ChildExec: not null } && !_agentStarted)
            {
                RootView.AddLogByKey(LangKeys.StartingAgent);
                if (_agentClient != null)
                {
                    _agentClient.LinkStop();
                    _agentClient.Dispose();
                    _agentClient.DetachDisposeToResource();
                    _agentClient = null;
                    _agentProcess?.Kill();
                    _agentProcess?.Dispose();
                    _agentProcess = null;
                }

                var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
                var identifier = string.IsNullOrWhiteSpace(Interface?.Agent?.Identifier) ? new string(Enumerable.Repeat(chars, 8).Select(c => c[Random.Next(c.Length)]).ToArray()) : Interface.Agent.Identifier;
                LoggerHelper.Info($"Agent Identifier: {identifier}");
                try
                {
                    _agentClient = MaaAgentClient.Create(identifier, maaResource);
                    _agentClient.AttachDisposeToResource();
                    _agentClient.Releasing += (_, _) => LoggerHelper.Info("退出Agent进程");

                    LoggerHelper.Info($"Agent Client Hash: {_agentClient?.GetHashCode()}");
                    if (!Directory.Exists($"{AppContext.BaseDirectory}"))
                        Directory.CreateDirectory($"{AppContext.BaseDirectory}");
                    var program = MaaInterface.ReplacePlaceholder(agentConfig.ChildExec, AppContext.BaseDirectory);
                    var args = $"{string.Join(" ", MaaInterface.ReplacePlaceholder(agentConfig.ChildArgs ?? Enumerable.Empty<string>(), AppContext.BaseDirectory).Select(ConvertPath))} {_agentClient.Id}";
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = FindPythonPath(program),
                        WorkingDirectory = AppContext.BaseDirectory,
                        Arguments = $"{(program.Contains("python") && args.Contains(".py") && !args.Contains("-u ") ? "-u " : "")}{args}",
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true
                    };

                    _agentProcess = new Process
                    {
                        StartInfo = startInfo
                    };

                    _agentProcess.OutputDataReceived += (sender, args) =>
                    {
                        if (!string.IsNullOrEmpty(args.Data))
                        {
                            var outData = args.Data;
                            try
                            {
                                outData = Regex.Replace(outData, @"\x1B\[[0-9;]*[a-zA-Z]", "");
                            }
                            catch (Exception)
                            {
                            }

                            DispatcherHelper.PostOnMainThread(() =>
                            {
                                RootView.AddLog(outData);
                            });
                        }
                    };

                    _agentProcess.ErrorDataReceived += (sender, args) =>
                    {
                        if (!string.IsNullOrEmpty(args.Data))
                        {
                            var outData = args.Data;
                            try
                            {
                                outData = Regex.Replace(outData, @"\x1B\[[0-9;]*[a-zA-Z]", "");
                            }
                            catch (Exception)
                            {
                            }

                            DispatcherHelper.PostOnMainThread(() =>
                            {
                                RootView.AddLog(outData);
                            });
                        }
                    };

                    _agentProcess.Start();
                    LoggerHelper.Info(
                        $"Agent启动: {MaaInterface.ReplacePlaceholder(agentConfig.ChildExec, AppContext.BaseDirectory)} {string.Join(" ", MaaInterface.ReplacePlaceholder(agentConfig.ChildArgs ?? Enumerable.Empty<string>(), AppContext.BaseDirectory))} {_agentClient.Id} "
                        + $"socket_id: {_agentClient.Id}");
                    _agentProcess.BeginOutputReadLine();
                    _agentProcess.BeginErrorReadLine();

                    TaskManager.RunTaskAsync(async () => await _agentProcess.WaitForExitAsync(token), token: token, name: "Agent程序启动");

                }
                catch (Exception ex)
                {
                    LoggerHelper.Error($"{LangKeys.AgentStartFailed.ToLocalization()}: {ex}");
                    ToastHelper.Error(LangKeys.AgentStartFailed.ToLocalization(), ex.Message);
                }

                _agentClient?.LinkStart();
                _agentStarted = true;
            }
            // RegisterCustomRecognitionsAndActions(tasker);
            Instances.TaskQueueViewModel.SetConnected(true);
            //  tasker.Utility.SetOption_Recording(ConfigurationManager.Maa.GetValue(ConfigurationKeys.Recording, false));
            tasker.Global.SetOption_SaveDraw(ConfigurationManager.Maa.GetValue(ConfigurationKeys.SaveDraw, false));
            // tasker.Global.SetOption_DebugMode(ConfigurationManager.Maa.GetValue(ConfigurationKeys.ShowHitDraw, false));

            tasker.Callback += (o, args) =>
            {
                var jObject = JObject.Parse(args.Details);

                var name = jObject["name"]?.ToString() ?? string.Empty;
                if (args.Message.StartsWith(MaaMsg.Node.Recognition.Succeeded) || args.Message.StartsWith(MaaMsg.Node.Action.Succeeded))
                {
                    if (jObject["reco_id"] != null)
                    {
                        var recoId = Convert.ToInt64(jObject["reco_id"]?.ToString() ?? string.Empty);
                        if (recoId > 0)
                        {
                            //     tasker.GetNodeDetail(nodeId, out _, out var recognitionId, out var actionId, out _);
                            var rect = new MaaRectBuffer();
                            var imageBuffer = new MaaImageBuffer();
                            tasker.GetRecognitionDetail(recoId, out string node,
                                out var algorithm,
                                out var hit,
                                rect,
                                out var detailJson,
                                imageBuffer, new MaaImageListBuffer());
                            var bitmap = imageBuffer.ToBitmap();
                            if (hit && bitmap != null)
                            {
                                bitmap = bitmap.DrawRectangle(rect, Brushes.LightGreen, 1.5f);
                            }

                            DispatcherHelper.PostOnMainThread(() =>
                            {
                                Instances.ScreenshotViewModel.ScreenshotImage = bitmap;
                                Instances.ScreenshotViewModel.TaskName = name;
                            });
                        }

                    }
                    if (jObject["action_id"] != null)
                    {
                        var actionId = Convert.ToInt64(jObject["action_id"]?.ToString() ?? string.Empty);
                        if (actionId > 0)
                        {
                            //     tasker.GetNodeDetail(nodeId, out _, out var recognitionId, out var actionId, out _);
                            var rect = new MaaRectBuffer();
                            var imageBuffer = new MaaImageBuffer();
                            tasker.GetCachedImage(imageBuffer);
                            var bitmap = imageBuffer.ToBitmap();
                            tasker.GetActionDetail(actionId, out _, out _, rect, out var isSucceeded, out _);
                            if (isSucceeded && bitmap != null)
                            {
                                bitmap = bitmap.DrawRectangle(rect, Brushes.LightGreen, 1.5f);
                            }

                            DispatcherHelper.PostOnMainThread(() =>
                            {
                                Instances.ScreenshotViewModel.ScreenshotImage = bitmap;
                                Instances.ScreenshotViewModel.TaskName = name;
                            });
                        }

                    }
                    // if (jObject["task_id"] != null)
                    // {
                    //     var taskId = Convert.ToInt64(jObject["task_id"]?.ToString() ?? string.Empty);
                    //     Console.WriteLine(taskId);
                    //     tasker.GetTaskDetail(taskId, out var nodeName, out var recognitionId, out var actionCompleted);
                    // }

                }

                if ((args.Message.StartsWith(MaaMsg.Node.Action.Prefix) || args.Message.StartsWith(MaaMsg.Node.Recognition.Prefix)) && jObject.ContainsKey("focus"))
                {
                    DisplayFocus(jObject, args.Message, args.Details);
                }
            };

            return (tasker, InvalidResource);
        }
        catch (OperationCanceledException)
        {
            LoggerHelper.Warning("Tasker initialization was canceled");
            return (null, InvalidResource);
        }
        catch (MaaException)
        {
            return (null, InvalidResource);
        }
        catch (Exception e)
        {
            LoggerHelper.Error("Initialization tasker error", e);
            return (null, InvalidResource);
        }
    }

#pragma warning disable CS0649 // 
    private class Focus
    {
        [JsonConverter(typeof(GenericSingleOrListConverter<string>))] [JsonProperty("start")]
        public List<string>? Start;
        [JsonConverter(typeof(GenericSingleOrListConverter<string>))] [JsonProperty("succeeded")]
        public List<string>? Succeeded;
        [JsonConverter(typeof(GenericSingleOrListConverter<string>))] [JsonProperty("failed")]
        public List<string>? Failed;
        [JsonConverter(typeof(GenericSingleOrListConverter<string>))] [JsonProperty("toast")]
        public List<string>? Toast;
        [JsonProperty("aborted")] public bool? Aborted;
    }

    public static (string Text, string? Color) ParseColorText(string input)
    {
        var match = Regex.Match(input.Trim(), @"\[color:(?<color>.*?)\](?<text>.*?)\[/color\]", RegexOptions.IgnoreCase);

        if (match.Success)
        {
            string color = match.Groups["color"].Value.Trim();
            string text = match.Groups["text"].Value;
            return (text, color);
        }

        return (input, null);

    }

    private void DisplayFocus(JObject taskModel, string message, string detail)
    {
        if (taskModel["focus"] == null)
            return;

        var focusToken = taskModel["focus"];
        var focus = new Focus();
        JObject newProtocolFocus = null;

        // 解析focus内容，同时提取新旧协议数据
        if (focusToken.Type == JTokenType.String)
        {
            // 旧协议：字符串形式（等价于start）
            focus.Start = new List<string>
            {
                focusToken.Value<string>()
            };
        }
        else if (focusToken.Type == JTokenType.Object)
        {
            var focusObj = focusToken as JObject;
            // 提取旧协议字段（start/succeeded/failed/toast等）
            focus = focusObj.ToObject<Focus>();
            // 提取新协议字段（消息类型为键的条目）
            newProtocolFocus = new JObject(
                focusObj.Properties()
                    .Where(prop => prop.Name.StartsWith("Node.Action.") || prop.Name.StartsWith("Node.Recognition."))
                    .Select(prop => new JProperty(prop.Name, prop.Value))
            );
        }

        // 处理详情数据（用于新协议占位符替换）
        JObject detailsObj = null;
        if (!string.IsNullOrEmpty(detail))
        {
            try
            {
                detailsObj = JObject.Parse(detail);
            }
            catch
            {
                // 忽略详情解析错误
            }
        }

        // 1. 处理新协议（如果有）
        if (newProtocolFocus is { HasValues: true } && newProtocolFocus.TryGetValue(message, out var templateToken))
        {
            // 处理字符串数组类型
            if (templateToken.Type == JTokenType.Array)
            {
                foreach (var item in templateToken.Children())
                {
                    if (item.Type == JTokenType.String)
                    {
                        var template = item.Value<string>();
                        var displayText = ReplacePlaceholders(template, detailsObj);
                        var (text, color) = ParseColorText(displayText);
                        RootView.AddLog(text, color == null ? null : BrushHelper.ConvertToBrush(color));
                    }
                }
            }
            // 处理单个字符串类型
            else if (templateToken.Type == JTokenType.String)
            {
                var template = templateToken.Value<string>();
                var displayText = ReplacePlaceholders(template, detailsObj);
                var (text, color) = ParseColorText(displayText);
                RootView.AddLog(text, color == null ? null : BrushHelper.ConvertToBrush(color));
            }
        }


        // 2. 处理旧协议（如果有）
        switch (message)
        {
            case MaaMsg.Node.Action.Succeeded:
                if (focus.Succeeded != null)
                {
                    foreach (var line in focus.Succeeded)
                    {
                        var (text, color) = ParseColorText(line);
                        RootView.AddLog(HandleStringsWithVariables(text), color == null ? null : BrushHelper.ConvertToBrush(color));
                    }
                }
                break;
            case MaaMsg.Node.Action.Failed:
                if (focus.Failed != null)
                {
                    foreach (var line in focus.Failed)
                    {
                        var (text, color) = ParseColorText(line);
                        RootView.AddLog(HandleStringsWithVariables(text), color == null ? null : BrushHelper.ConvertToBrush(color));
                    }
                }
                break;
            case MaaMsg.Node.Action.Starting:
                if (focus.Aborted == true)
                {
                    Status = MFATask.MFATaskStatus.FAILED;
                }
                if (focus.Toast != null && focus.Toast.Count > 0)
                {
                    var (text, color) = ParseColorText(focus.Toast[0]);
                    ToastNotification.Show(HandleStringsWithVariables(text));
                }
                if (focus.Start != null)
                {
                    foreach (var line in focus.Start)
                    {
                        var (text, color) = ParseColorText(line);
                        RootView.AddLog(HandleStringsWithVariables(text), color == null ? null : BrushHelper.ConvertToBrush(color));
                    }
                }
                break;
        }
    }

// 辅助方法：替换模板中的占位符
    private string ReplacePlaceholders(string template, JObject? details)
    {
        if (details == null)
            return template;

        string result = template;
        foreach (var prop in details.Properties())
        {
            result = result.Replace($"{{{prop.Name}}}", prop.Value?.ToString() ?? string.Empty);
        }
        return result;
    }
// private void DisplayFocus(MaaNode taskModel, string message)
    // {
    //     switch (message)
    //     {
    //         case MaaMsg.Node.Action.Succeeded:
    //             if (taskModel.FocusSucceeded != null)
    //             {
    //                 for (int i = 0; i < taskModel.FocusSucceeded.Count; i++)
    //                 {
    //                     IBrush brush = null;
    //                     var tip = taskModel.FocusSucceeded[i];
    //                     try
    //                     {
    //                         if (taskModel.FocusSucceededColor != null && taskModel.FocusSucceededColor.Count > i)
    //                             brush = BrushHelper.ConvertToBrush(taskModel.FocusSucceededColor[i]);
    //                     }
    //                     catch (Exception e)
    //                     {
    //                         LoggerHelper.Error(e);
    //                     }
    //
    //                     RootView.AddLog(HandleStringsWithVariables(tip), brush);
    //                 }
    //             }
    //             break;
    //         case MaaMsg.Node.Action.Failed:
    //             if (taskModel.FocusFailed != null)
    //             {
    //                 for (int i = 0; i < taskModel.FocusFailed.Count; i++)
    //                 {
    //                     IBrush brush = null;
    //                     var tip = taskModel.FocusFailed[i];
    //                     try
    //                     {
    //                         if (taskModel.FocusFailedColor != null && taskModel.FocusFailedColor.Count > i)
    //                             brush = BrushHelper.ConvertToBrush(taskModel.FocusFailedColor[i]);
    //                     }
    //                     catch (Exception e)
    //                     {
    //                         LoggerHelper.Error(e);
    //                     }
    //
    //                     RootView.AddLog(HandleStringsWithVariables(tip), brush);
    //                 }
    //             }
    //             break;
    //         case MaaMsg.Node.Action.Starting:
    //             if (!string.IsNullOrWhiteSpace(taskModel.FocusToast))
    //             {
    //                 ToastNotification.Show(taskModel.FocusToast);
    //             }
    //             if (taskModel.FocusTip != null)
    //             {
    //                 for (int i = 0; i < taskModel.FocusTip.Count; i++)
    //                 {
    //                     IBrush? brush = null;
    //                     var tip = taskModel.FocusTip[i];
    //                     try
    //                     {
    //                         if (taskModel.FocusTipColor != null && taskModel.FocusTipColor.Count > i)
    //                         {
    //                             brush = BrushHelper.ConvertToBrush(taskModel.FocusTipColor[i]);
    //                         }
    //                     }
    //                     catch (Exception e)
    //                     {
    //                         LoggerHelper.Error(e);
    //                     }
    //                     RootView.AddLog(HandleStringsWithVariables(tip), brush);
    //                 }
    //             }
    //             break;
    //     }
    // }
    public static string HandleStringsWithVariables(string content)
    {
        try
        {
            return Regex.Replace(content, @"\{(\+\+|\-\-)?(\w+)(\+\+|\-\-)?([\+\-\*/]\w+)?\}", match =>
            {
                var prefix = match.Groups[1].Value;
                var counterKey = match.Groups[2].Value;
                var suffix = match.Groups[3].Value;
                var operation = match.Groups[4].Value;

                int value = Instance.AutoInitDictionary.GetValueOrDefault(counterKey, 0);

                // 前置操作符7
                if (prefix == "++")
                {
                    value = ++Instance.AutoInitDictionary[counterKey];
                }
                else if (prefix == "--")
                {
                    value = --Instance.AutoInitDictionary[counterKey];
                }

                // 后置操作符
                if (suffix == "++")
                {
                    value = Instance.AutoInitDictionary[counterKey]++;
                }
                else if (suffix == "--")
                {
                    value = Instance.AutoInitDictionary[counterKey]--;
                }

                // 算术操作
                if (!string.IsNullOrEmpty(operation))
                {
                    string operationType = operation[0].ToString();
                    string operandKey = operation.Substring(1);

                    if (Instance.AutoInitDictionary.TryGetValue(operandKey, out var operandValue))
                    {
                        value = operationType switch
                        {
                            "+" => value + operandValue,
                            "-" => value - operandValue,
                            "*" => value * operandValue,
                            "/" => value / operandValue,
                            _ => value
                        };
                    }
                }

                return value.ToString();
            });
        }
        catch (Exception e)
        {
            LoggerHelper.Error(e);
            ErrorView.ShowException(e);
            return content;
        }
    }

    private void HandleInitializationError(Exception e,
        string message,
        bool hasWarning = false,
        string waringMessage = "")
    {
        ToastHelper.Error(message);
        if (hasWarning)
            LoggerHelper.Warning(waringMessage);
        LoggerHelper.Error(e.ToString());
    }

    private MaaController InitializeController(bool isAdb)
    {
        ConnectToMAA();
        if (isAdb)
        {
            LoggerHelper.Info($"AdbPath: {Config.AdbDevice.AdbPath}");
            LoggerHelper.Info($"AdbSerial: {Config.AdbDevice.AdbSerial}");
            LoggerHelper.Info($"ScreenCap: {Config.AdbDevice.ScreenCap}");
            LoggerHelper.Info($"Input: {Config.AdbDevice.Input}");
            LoggerHelper.Info($"Config: {Config.AdbDevice.Config}");
        }
        else
        {
            LoggerHelper.Info($"HWnd: {Config.DesktopWindow.HWnd}");
            LoggerHelper.Info($"ScreenCap: {Config.DesktopWindow.ScreenCap}");
            LoggerHelper.Info($"MouseInput: {Config.DesktopWindow.Mouse}");
            LoggerHelper.Info($"KeyboardInput: {Config.DesktopWindow.KeyBoard}");
            LoggerHelper.Info($"Link: {Config.DesktopWindow.Link}");
            LoggerHelper.Info($"Check: {Config.DesktopWindow.Check}");
        }
        return isAdb
            ? new MaaAdbController(
                Config.AdbDevice.AdbPath,
                Config.AdbDevice.AdbSerial,
                Config.AdbDevice.ScreenCap, Config.AdbDevice.Input,
                !string.IsNullOrWhiteSpace(Config.AdbDevice.Config) ? Config.AdbDevice.Config : "{}",
                Path.Combine(AppContext.BaseDirectory, "MaaAgentBinary")
            )
            : new MaaWin32Controller(
                Config.DesktopWindow.HWnd,
                Config.DesktopWindow.ScreenCap, Config.DesktopWindow.Mouse, Config.DesktopWindow.KeyBoard,
                Config.DesktopWindow.Link,
                Config.DesktopWindow.Check);
    }

    public class MaaFWConfiguration
    {
        public AdbDeviceCoreConfig AdbDevice { get; set; } = new();
        public DesktopWindowCoreConfig DesktopWindow { get; set; } = new();
    }

    public class DesktopWindowCoreConfig
    {
        public string Name { get; set; } = string.Empty;
        public nint HWnd { get; set; }

        public Win32InputMethod Mouse { get; set; } = Win32InputMethod.SendMessage;
        public Win32InputMethod KeyBoard { get; set; } = Win32InputMethod.SendMessage;
        public Win32ScreencapMethod ScreenCap { get; set; } = Win32ScreencapMethod.FramePool;
        public LinkOption Link { get; set; } = LinkOption.Start;
        public CheckStatusOption Check { get; set; } = CheckStatusOption.ThrowIfNotSucceeded;
    }

    public class AdbDeviceCoreConfig
    {
        public string Name { get; set; } = string.Empty;
        public string AdbPath { get; set; } = "adb";
        public string AdbSerial { get; set; } = "";
        public string Config { get; set; } = "{}";
        public AdbInputMethods Input { get; set; } = AdbInputMethods.Default;
        public AdbScreencapMethods ScreenCap { get; set; } = AdbScreencapMethods.Default;
        public AdbDeviceInfo? Info { get; set; } = null;
    }

    public static bool CheckInterface(out string Name, out string Version, out string CustomTitle)
    {
        if (!File.Exists($"{AppContext.BaseDirectory}/interface.json"))
        {
            LoggerHelper.Info("未找到interface文件，生成interface.json...");
            Interface = new MaaInterface
                {
                    Version = "1.0",
                    Name = "Debug",
                    Task = [],
                    Resource =
                    [
                        new MaaInterface.MaaInterfaceResource()
                        {
                            Name = "默认",
                            Path =
                            [
                                "{PROJECT_DIR}/resource/base",
                            ],
                        },
                    ],
                    Controller =
                    [
                        new MaaInterface.MaaResourceController()
                        {
                            Name = "adb 默认方式",
                            Type = "adb"
                        },
                    ],
                    Option = new Dictionary<string, MaaInterface.MaaInterfaceOption>
                    {
                        {
                            "测试", new MaaInterface.MaaInterfaceOption()
                            {
                                Cases =
                                [

                                    new MaaInterface.MaaInterfaceOptionCase
                                    {
                                        Name = "测试1",
                                        PipelineOverride = new Dictionary<string, JToken>()
                                    },
                                    new MaaInterface.MaaInterfaceOptionCase
                                    {
                                        Name = "测试2",
                                        PipelineOverride = new Dictionary<string, JToken>()
                                    }
                                ]
                            }
                        }
                    }
                }
                ;
            if (!Directory.Exists(Path.Combine(AppContext.BaseDirectory, "/resource/base")))
                Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "/resource/base"));
            JsonHelper.SaveJson(Path.Combine(AppContext.BaseDirectory, "interface.json"),
                Interface, new MaaInterfaceSelectAdvancedConverter(true), new MaaInterfaceSelectOptionConverter(true));
            Name = Interface?.Name ?? string.Empty;
            Version = Interface?.Version ?? string.Empty;
            CustomTitle = Interface?.CustomTitle ?? string.Empty;
            return true;
        }
        Name = string.Empty;
        Version = string.Empty;
        CustomTitle = string.Empty;
        return false;
    }

    public static (string Name, string Version, string CustomTitle) ReadInterface()
    {
        if (CheckInterface(out string name, out string version, out string customTitle))
        {
            return (name, version, customTitle);
        }

        var defaultValue = new MaaInterface();
        Interface =
            JsonHelper.LoadJson(Path.Combine(AppContext.BaseDirectory, "interface.json"), defaultValue
                , errorHandle: () =>
                {
                    var @interface = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "interface.json")));
                    if (@interface != null)
                    {
                        try
                        {
                            defaultValue.MFAMinVersion = @interface["mfa_min_version"]?.ToString();
                            defaultValue.MFAMaxVersion = @interface["mfa_max_version"]?.ToString();
                            defaultValue.CustomTitle = @interface["custom_title"]?.ToString();
                            defaultValue.Name = @interface["name"]?.ToString();
                            defaultValue.Url = @interface["url"]?.ToString();
                        }
                        catch (Exception e)
                        {
                            LoggerHelper.Warning(e);
                        }
                        RootView.AddLog(LangKeys.FileLoadFailed.ToLocalizationFormatted(false, "interface.json"));
                    }
                }, new MaaInterfaceSelectAdvancedConverter(false),
                new MaaInterfaceSelectOptionConverter(false));


        return (Interface?.Name ?? string.Empty, Interface?.Version ?? string.Empty, Interface?.CustomTitle ?? string.Empty);

    }

    public bool InitializeData(Collection<DragItemViewModel>? dragItem = null)
    {
        var (name, version, customTitle) = ReadInterface();
        if (!string.IsNullOrWhiteSpace(name) && !name.Equals("debug", StringComparison.OrdinalIgnoreCase))
            Instances.RootViewModel.ShowResourceName(name);
        if (!string.IsNullOrWhiteSpace(version) && !version.Equals("debug", StringComparison.OrdinalIgnoreCase))
        {
            Instances.RootViewModel.ShowResourceVersion(version);
            Instances.VersionUpdateSettingsUserControlModel.ResourceVersion = version;
        }
        if (!string.IsNullOrWhiteSpace(customTitle))
            Instances.RootViewModel.ShowCustomTitle(customTitle);

        if (Interface != null)
        {
            AppendVersionLog(Interface.Version);
            TasksSource.Clear();
            LoadTasks(Interface.Task ?? new List<MaaInterface.MaaInterfaceTask>(), dragItem);
        }

        return LoadTask();
    }

    private bool LoadTask()
    {
        try
        {
            var fileCount = 0;
            if (Instances.TaskQueueViewModel.CurrentResources.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(Instances.TaskQueueViewModel.CurrentResource) && !string.IsNullOrWhiteSpace(Instances.TaskQueueViewModel.CurrentResources[0].Name))
                    Instances.TaskQueueViewModel.CurrentResource = Instances.TaskQueueViewModel.CurrentResources[0].Name;
            }
            if (Instances.TaskQueueViewModel.CurrentResources.Any(r => r.Name == Instances.TaskQueueViewModel.CurrentResource))
            {
                var resources = Instances.TaskQueueViewModel.CurrentResources.FirstOrDefault(r => r.Name == Instances.TaskQueueViewModel.CurrentResource);
                if (resources?.Path != null)
                {
                    foreach (var resourcePath in resources.Path)
                    {
                        if (!Path.Exists($"{resourcePath}/pipeline/"))
                            break;
                        var jsonFiles = Directory.GetFiles(Path.GetFullPath($"{resourcePath}/pipeline/"), "*.json", SearchOption.AllDirectories);
                        var jsoncFiles = Directory.GetFiles(Path.GetFullPath($"{resourcePath}/pipeline/"), "*.jsonc", SearchOption.AllDirectories);
                        var allFiles = jsonFiles.Concat(jsoncFiles).ToArray();
                        fileCount = allFiles.Length;
                        // var taskDictionaryA = new Dictionary<string, MaaNode>();
                        // foreach (var file in allFiles)
                        // {
                        //     if (file.Contains("default_pipeline.json", StringComparison.OrdinalIgnoreCase))
                        //         continue;
                        //     var content = File.ReadAllText(file);
                        //     LoggerHelper.Info($"Loading Pipeline: {file}");
                        //     try
                        //     {
                        //         var taskData = JsonConvert.DeserializeObject<Dictionary<string, MaaNode>>(content);
                        //         if (taskData == null || taskData.Count == 0)
                        //             continue;
                        //         foreach (var task in taskData)
                        //         {
                        //             if (!taskDictionaryA.TryAdd(task.Key, task.Value))
                        //             {
                        //                 ToastHelper.Error(LangKeys.DuplicateTaskError.ToLocalizationFormatted(false, task.Key));
                        //                 return false;
                        //             }
                        //         }
                        //     }
                        //     catch (Exception e)
                        //     {
                        //         LoggerHelper.Warning(e);
                        //     }
                        // }

                        // taskDictionary = taskDictionary.MergeMaaNodes(taskDictionaryA);
                    }
                }
            }
            var resourceP = string.IsNullOrWhiteSpace(Instances.TaskQueueViewModel.CurrentResource)
                ? ResourceBase
                : (Instances.TaskQueueViewModel.CurrentResources.FirstOrDefault(c => c.Name == Instances.TaskQueueViewModel.CurrentResource)?.Path?[0]) ?? ResourceBase;
            var resourcePs = string.IsNullOrWhiteSpace(Instances.TaskQueueViewModel.CurrentResource)
                ? [ResourceBase]
                : (Instances.TaskQueueViewModel.CurrentResources.FirstOrDefault(c => c.Name == Instances.TaskQueueViewModel.CurrentResource)?.Path);
            if (resourcePs is {Count:>0})
            {
                foreach (var rp in resourcePs)
                {
                    if (!Directory.Exists(rp))
                        Directory.CreateDirectory(rp);
                }
            }
            if (fileCount == 0)
            {
                if (!string.IsNullOrWhiteSpace($"{resourceP}/pipeline") && !Directory.Exists($"{resourceP}/pipeline"))
                {
                    try
                    {
                        Directory.CreateDirectory($"{resourceP}/pipeline");
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.Error(ex);
                    }
                }

                if (!File.Exists($"{resourceP}/pipeline/sample.json"))
                {
                    try
                    {
                        File.WriteAllText($"{resourceP}/pipeline/sample.json",
                            JsonConvert.SerializeObject(new Dictionary<string, MaaNode>
                            {
                                {
                                    "MFAAvalonia", new MaaNode
                                    {
                                        Action = "DoNothing"
                                    }
                                }
                            }, new JsonSerializerSettings()
                            {
                                Formatting = Formatting.Indented,
                                NullValueHandling = NullValueHandling.Ignore,
                                DefaultValueHandling = DefaultValueHandling.Ignore
                            }));
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.Error(ex);
                    }
                }
            }

            // PopulateTasks(taskDictionary);

            return true;
        }
        catch (Exception ex)
        {
            ToastHelper.Error(LangKeys.PipelineLoadError.ToLocalizationFormatted(false, ex.Message)
            );
            LoggerHelper.Error(ex);
            return false;
        }
    }

    private void PopulateTasks(Dictionary<string, MaaNode> taskDictionary)
    {
        // BaseNodes = taskDictionary;
        // foreach (var task in taskDictionary)
        // {
        //     task.Value.Name = task.Key;
        //     ValidateTaskLinks(taskDictionary, task);
        // }
    }

    private void ValidateTaskLinks(Dictionary<string, MaaNode> taskDictionary,
        KeyValuePair<string, MaaNode> task)
    {
        ValidateNextTasks(taskDictionary, task.Value.Name, task.Value.Next);
        ValidateNextTasks(taskDictionary, task.Value.Name, task.Value.OnError, "on_error");
        ValidateNextTasks(taskDictionary, task.Value.Name, task.Value.Interrupt, "interrupt");
    }

    private void ValidateNextTasks(Dictionary<string, MaaNode> taskDictionary,
        string? taskName,
        object? nextTasks,
        string name = "next")
    {
        if (nextTasks is List<string> tasks)
        {
            foreach (var task in tasks)
            {
                if (!taskDictionary.ContainsKey(task))
                {
                    ToastHelper.Error(LangKeys.Error.ToLocalization(), LangKeys.TaskNotFoundError.ToLocalizationFormatted(false, taskName, name, task));
                    LoggerHelper.Error(LangKeys.TaskNotFoundError.ToLocalizationFormatted(false, taskName, name, task));
                }
            }
        }
    }

    public void ConnectToMAA()
    {
        LoggerHelper.Info("Loading MAA Controller Configuration...");
        ConfigureMaaProcessorForADB();
        ConfigureMaaProcessorForWin32();
    }

    private void ConfigureMaaProcessorForADB()
    {
        if (Instances.TaskQueueViewModel.CurrentController == MaaControllerTypes.Adb)
        {
            var adbInputType = ConfigureAdbInputTypes();
            var adbScreenCapType = ConfigureAdbScreenCapTypes();

            Config.AdbDevice.Input = adbInputType;
            Config.AdbDevice.ScreenCap = adbScreenCapType;
            LoggerHelper.Info(
                $"{LangKeys.AdbInputMode.ToLocalization()}{adbInputType},{LangKeys.AdbCaptureMode.ToLocalization()}{adbScreenCapType}");
        }
    }

    public static string ScreenshotType()
    {
        if (Instances.TaskQueueViewModel.CurrentController == MaaControllerTypes.Adb)
            return ConfigureAdbScreenCapTypes().ToString();
        return ConfigureWin32ScreenCapTypes().ToString();
    }


    private static AdbInputMethods ConfigureAdbInputTypes()
    {
        return Instances.ConnectSettingsUserControlModel.AdbControlInputType switch
        {
            AdbInputMethods.None => Config.AdbDevice.Info?.InputMethods ?? AdbInputMethods.Default,
            _ => Instances.ConnectSettingsUserControlModel.AdbControlInputType
        };
    }

    private static AdbScreencapMethods ConfigureAdbScreenCapTypes()
    {
        return Instances.ConnectSettingsUserControlModel.AdbControlScreenCapType switch
        {
            AdbScreencapMethods.None => Config.AdbDevice.Info?.ScreencapMethods ?? AdbScreencapMethods.Default,
            _ => Instances.ConnectSettingsUserControlModel.AdbControlScreenCapType
        };
    }

    private void ConfigureMaaProcessorForWin32()
    {
        if (Instances.TaskQueueViewModel.CurrentController == MaaControllerTypes.Win32)
        {
            var win32MouseInputType = ConfigureWin32MouseInputTypes();
            var win32KeyboardInputType = ConfigureWin32KeyboardInputTypes();
            var winScreenCapType = ConfigureWin32ScreenCapTypes();

            Config.DesktopWindow.Mouse = win32MouseInputType;
            Config.DesktopWindow.KeyBoard = win32KeyboardInputType;
            Config.DesktopWindow.ScreenCap = winScreenCapType;

            LoggerHelper.Info(
                $"{LangKeys.MouseInput.ToLocalization()}:{win32MouseInputType},{LangKeys.KeyboardInput.ToLocalization()}:{win32KeyboardInputType},{LangKeys.AdbCaptureMode.ToLocalization()}{winScreenCapType}");
        }
    }

    private static Win32ScreencapMethod ConfigureWin32ScreenCapTypes()
    {
        return Instances.ConnectSettingsUserControlModel.Win32ControlScreenCapType;
    }

    private static Win32InputMethod ConfigureWin32MouseInputTypes()
    {
        return Instances.ConnectSettingsUserControlModel.Win32ControlMouseType;
    }

    private static Win32InputMethod ConfigureWin32KeyboardInputTypes()
    {
        return Instances.ConnectSettingsUserControlModel.Win32ControlKeyboardType;
    }

    private bool FirstTask = true;

    const string NEW_SEPARATOR = "<|||>";
    const string OLD_SEPARATOR = ":";

    private void LoadTasks(List<MaaInterface.MaaInterfaceTask> tasks, IList<DragItemViewModel>? oldDrags = null)
    {
        var currentTasks = ConfigurationManager.Current.GetValue(ConfigurationKeys.CurrentTasks, new List<string>());
        if (currentTasks.Count <= 0 || currentTasks.Any(t => t.Contains(OLD_SEPARATOR) && !t.Contains(NEW_SEPARATOR)))
        {
            currentTasks.Clear();
            currentTasks.AddRange(tasks
                .Select(t => $"{t.Name}{NEW_SEPARATOR}{t.Entry}")
                .Distinct()
                .ToList());
            ConfigurationManager.Current.SetValue(ConfigurationKeys.CurrentTasks, currentTasks);
        }

        var items = ConfigurationManager.Current.GetValue(ConfigurationKeys.TaskItems, new List<MaaInterface.MaaInterfaceTask>()) ?? [];
        var drags = (oldDrags?.ToList() ?? []).Union(items.Select(interfaceItem => new DragItemViewModel(interfaceItem))).ToList();

        if (FirstTask)
        {
            InitializeResources();
            FirstTask = false;
        }

        var (updateList, removeList) = SynchronizeTaskItems(ref currentTasks, drags, tasks);
        ConfigurationManager.Current.SetValue(ConfigurationKeys.CurrentTasks, currentTasks);
        updateList.RemoveAll(d => removeList.Contains(d));

        UpdateViewModels(updateList, tasks);
    }

    private void InitializeResources()
    {
        Instances.TaskQueueViewModel.CurrentResources =
            Interface?.Resources.Values.Count > 0
                ? new ObservableCollection<MaaInterface.MaaInterfaceResource>(Interface.Resources.Values.ToList())
                :
                [
                    new MaaInterface.MaaInterfaceResource
                    {
                        Name = "Default",
                        Path = [ResourceBase]
                    }
                ];
        Instances.TaskQueueViewModel.CurrentResource = ConfigurationManager.Current.GetValue(ConfigurationKeys.Resource, string.Empty);
        if (Instances.TaskQueueViewModel.CurrentResources.Count > 0 && Instances.TaskQueueViewModel.CurrentResources.All(r => r.Name != Instances.TaskQueueViewModel.CurrentResource))
            Instances.TaskQueueViewModel.CurrentResource = Instances.TaskQueueViewModel.CurrentResources[0].Name ?? "Default";
    }

    private (List<DragItemViewModel> updateList, List<DragItemViewModel> removeList) SynchronizeTaskItems(ref List<string> currentTasks,
        IList<DragItemViewModel> drags,
        List<MaaInterface.MaaInterfaceTask> tasks)
    {
        var currentTaskSet = new HashSet<(string Name, string Entry)>(
            currentTasks
                .Select(t => t.Split(NEW_SEPARATOR, 2))
                .Where(parts => parts.Length == 2)
                .Select(parts => (parts[0], parts[1]))
        );

        var newDict = tasks
            .GroupBy(t => (t.Name, t.Entry)) // 按键分组
            .ToDictionary(
                group => group.Key, // 键为 (Name, Entry)
                group => group.Last() // 值取分组中的最后一个元素
            ); // 使用 (Name, Entry) 作为键
        var removeList = new List<DragItemViewModel>();
        var updateList = new List<DragItemViewModel>();

        if (drags.Count == 0)
            return (updateList, removeList);

        foreach (var oldItem in drags)
        {
            if (newDict.TryGetValue((oldItem.Name, oldItem.InterfaceItem?.Entry), out var newItem))
            {
                UpdateExistingItem(oldItem, newItem);
                updateList.Add(oldItem);
            }
            else
            {
                var sameNameTasks = tasks.Where(t => t.Entry == oldItem.InterfaceItem?.Entry).ToList();
                if (sameNameTasks.Any())
                {
                    var firstTask = sameNameTasks.First();
                    UpdateExistingItem(oldItem, firstTask, tasks.Any(t => t.Name == firstTask.Name));
                    updateList.Add(oldItem);
                }
                else
                {
                    removeList.Add(oldItem);
                }
            }
        }

        foreach (var task in tasks)
        {
            var key = (task.Name, task.Entry);
            if (!currentTaskSet.Contains(key))
            {
                // 创建新的DragItemViewModel并添加到updateList
                var newDragItem = new DragItemViewModel(task);
                updateList.Add(newDragItem);

                currentTasks.Add($"{task.Name}:{task.Entry}");
            }
        }

        return (updateList, removeList);
    }

    private void UpdateExistingItem(DragItemViewModel oldItem, MaaInterface.MaaInterfaceTask newItem, bool updateName = false)
    {
        if (oldItem.InterfaceItem == null) return;
        if (updateName)
            oldItem.InterfaceItem.Name = newItem.Name;
        else
        {
            if (oldItem.InterfaceItem.Name != newItem.Name)
                return;
        }
        oldItem.InterfaceItem.Entry = newItem.Entry;
        oldItem.InterfaceItem.PipelineOverride = newItem.PipelineOverride;
        oldItem.InterfaceItem.Document = newItem.Document;
        oldItem.InterfaceItem.Repeatable = newItem.Repeatable;

        if (newItem.Advanced != null)
        {
            var tempDict = oldItem.InterfaceItem.Advanced?.ToDictionary(t => t.Name) ?? new Dictionary<string, MaaInterface.MaaInterfaceSelectAdvanced>();
            var maaInterfaceSelectAdvanceds = JsonConvert.DeserializeObject<List<MaaInterface.MaaInterfaceSelectAdvanced>>(JsonConvert.SerializeObject(newItem.Advanced));
            oldItem.InterfaceItem.Advanced = maaInterfaceSelectAdvanceds.Select(opt =>
            {
                if (tempDict.TryGetValue(opt.Name ?? string.Empty, out var existing))
                {
                    opt.Data = existing.Data;
                }
                return opt;
            }).ToList();
        }
        else
        {
            oldItem.InterfaceItem.Advanced = null;
        }

        if (newItem.Option != null)
        {
            var tempDict = oldItem.InterfaceItem.Option?.ToDictionary(t => t.Name) ?? new Dictionary<string, MaaInterface.MaaInterfaceSelectOption>();
            var maaInterfaceSelectOptions = JsonConvert.DeserializeObject<List<MaaInterface.MaaInterfaceSelectOption>>(JsonConvert.SerializeObject(newItem.Option));
            oldItem.InterfaceItem.Option = maaInterfaceSelectOptions.Select(opt =>
            {
                if (tempDict.TryGetValue(opt.Name ?? string.Empty, out var existing))
                {
                    if ((Interface?.Option?.TryGetValue(opt.Name ?? string.Empty, out var interfaceOption) ?? false) && interfaceOption.Cases is { Count: > 0 })
                        opt.Index = Math.Min(existing.Index ?? 0, interfaceOption.Cases.Count - 1);
                }
                else
                {
                    SetDefaultOptionValue(opt);
                }
                return opt;
            }).ToList();
        }
        else
        {
            oldItem.InterfaceItem.Option = null;
        }
    }

    public void SetDefaultOptionValue(MaaInterface.MaaInterfaceSelectOption option)
    {
        if (!(Interface?.Option?.TryGetValue(option.Name ?? string.Empty, out var interfaceOption) ?? false)) return;

        var defaultIndex = interfaceOption.Cases?
                .FindIndex(c => c.Name == interfaceOption.DefaultCase)
            ?? -1;
        if (defaultIndex != -1) option.Index = defaultIndex;
    }

    private void UpdateViewModels(IList<DragItemViewModel> drags, List<MaaInterface.MaaInterfaceTask> tasks)
    {
        var newItems = tasks.Select(t => new DragItemViewModel(t)).ToList();

        foreach (var item in newItems)
        {
            if (item.InterfaceItem?.Option != null && !drags.Any())
            {
                item.InterfaceItem.Option.ForEach(SetDefaultOptionValue);
            }
        }

        TasksSource.AddRange(newItems);

        if (!Instances.TaskQueueViewModel.TaskItemViewModels.Any())
        {
            Instances.TaskQueueViewModel.TaskItemViewModels = new ObservableCollection<DragItemViewModel>(drags.Any() ? drags : newItems);
        }
    }

    public static void AppendVersionLog(string? resourceVersion)
    {
        if (resourceVersion is null)
        {
            return;
        }
        
        // Get framework version
        string frameworkVersion;
        try
        {
            frameworkVersion = MaaUtility.MaaVersion();
        }
        catch (Exception e)
        {
            frameworkVersion = "Unknown";
            LoggerHelper.Error("Failed to get MaaFramework version", e);
        }
        
        // Log all version information
        LoggerHelper.Info($"Resource version: {resourceVersion}");
        LoggerHelper.Info($"MaaFramework version: {frameworkVersion}");
    }

    #endregion

    #region 开始任务

    static void MeasureExecutionTime(Action methodToMeasure)
    {
        var stopwatch = Stopwatch.StartNew();

        methodToMeasure();

        stopwatch.Stop();
        long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;

        switch (elapsedMilliseconds)
        {
            case >= 800:
                RootView.AddLogByKeys(LangKeys.ScreencapErrorTip, BrushHelper.ConvertToBrush("DarkGoldenrod"), false, elapsedMilliseconds.ToString(),
                    ScreenshotType());
                break;

            case >= 400:
                RootView.AddLogByKeys(LangKeys.ScreencapWarningTip, BrushHelper.ConvertToBrush("DarkGoldenrod"), false, elapsedMilliseconds.ToString(),
                    ScreenshotType());
                break;

            default:
                RootView.AddLogByKeys(LangKeys.ScreencapCost, null, false, elapsedMilliseconds.ToString(),
                    ScreenshotType());
                break;
        }
    }

    async static Task MeasureExecutionTimeAsync(Func<Task> methodToMeasure)
    {
        const int sampleCount = 4;
        long totalElapsed = 0;

        long min = 10000;
        long max = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            var sw = Stopwatch.StartNew();
            await methodToMeasure();
            sw.Stop();
            min = Math.Min(min, sw.ElapsedMilliseconds);
            max = Math.Max(max, sw.ElapsedMilliseconds);
            totalElapsed += sw.ElapsedMilliseconds;
        }

        var avgElapsed = totalElapsed / sampleCount;

        switch (avgElapsed)
        {
            case >= 800:
                RootView.AddLogByKeys(LangKeys.ScreencapErrorTip, BrushHelper.ConvertToBrush("DarkGoldenrod"), false, avgElapsed.ToString(),
                    ScreenshotType());
                break;

            case >= 400:
                RootView.AddLogByKeys(LangKeys.ScreencapWarningTip, BrushHelper.ConvertToBrush("DarkGoldenrod"), false, avgElapsed.ToString(),
                    ScreenshotType());
                break;

            default:
                RootView.AddLogByKeys(LangKeys.ScreencapCost, null, false, avgElapsed.ToString(),
                    ScreenshotType());
                break;
        }
    }

    public async Task RestartAdb()
    {
        var adbPath = Config.AdbDevice.AdbPath;

        if (string.IsNullOrEmpty(adbPath))
        {
            return;
        }

        ProcessStartInfo processStartInfo = new ProcessStartInfo
        {
            FileName = MFAExtensions.GetFallbackCommand(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        Process process = new Process
        {
            StartInfo = processStartInfo,
        };

        process.Start();
        await process.StandardInput.WriteLineAsync($"{adbPath} kill-server");
        await process.StandardInput.WriteLineAsync($"{adbPath} start-server");
        await process.StandardInput.WriteLineAsync("exit");
        await process.WaitForExitAsync();
    }

    public async Task ReconnectByAdb()
    {
        var adbPath = Config.AdbDevice.AdbPath;
        var address = Config.AdbDevice.AdbSerial;

        if (string.IsNullOrEmpty(adbPath) || adbPath == "adb")
        {
            return;
        }

        ProcessStartInfo processStartInfo = new ProcessStartInfo
        {
            FileName = MFAExtensions.GetFallbackCommand(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        var process = new Process
        {
            StartInfo = processStartInfo,
        };

        process.Start();
        await process.StandardInput.WriteLineAsync($"{adbPath} disconnect {address}");
        await process.StandardInput.WriteLineAsync("exit");
        await process.WaitForExitAsync();
    }

    public async Task HardRestartAdb()
    {
        var adbPath = Config.AdbDevice.AdbPath;
        if (string.IsNullOrEmpty(adbPath)) return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            WindowsKillAdbProcesses(adbPath);
        }
        else
        {
            UnixKillAdbProcesses(adbPath);
        }
    }

    [SupportedOSPlatform("windows")]
    private void WindowsKillAdbProcesses(string adbPath)
    {
        const string WmiQueryString = "SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process";
        using var searcher = new ManagementObjectSearcher(WmiQueryString);
        using var results = searcher.Get();

        var query = from p in Process.GetProcesses()
                    join mo in results.Cast<ManagementObject>()
                        on p.Id equals (int)(uint)mo["ProcessId"]
                    where ((string)mo["ExecutablePath"])?.Equals(adbPath, StringComparison.OrdinalIgnoreCase) == true
                    select p;

        KillProcesses(query);
    }

    private static void UnixKillAdbProcesses(string adbPath)
    {
        var processes = Process.GetProcessesByName("adb")
            .Where(p =>
            {
                try
                {
                    return GetUnixProcessPath(p.Id)?.Equals(adbPath, StringComparison.Ordinal) == true;
                }
                catch
                {
                    return false;
                }
            });

        KillProcesses(processes);
    }

    private static void KillProcesses(IEnumerable<Process> processes)
    {
        foreach (var process in processes)
        {
            try
            {
                process.Kill();
                process.WaitForExit();
            }
            catch
            {
                // 记录日志或忽略异常
            }
        }
    }

    private static string GetUnixProcessPath(int pid)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var exePath = $"/proc/{pid}/exe";
            return File.Exists(exePath) ? new FileInfo(exePath).LinkTarget : null;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var output = ExecuteShellCommand($"ps -p {pid} -o comm=");
            return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
        }
        return null;
    }


    /// <summary>
    /// 跨平台终止指定进程
    /// </summary>
    /// <param name="processName">进程名称（不带后缀）</param>
    /// <param name="commandLineKeyword">命令行关键词（可选）</param>
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public static void CloseProcessesByName(string processName, string? commandLineKeyword = null)
    {
        var processes = Process.GetProcesses()
            .Where(p => IsTargetProcess(p, processName, commandLineKeyword))
            .ToList();

        foreach (var process in processes)
        {
            SafeTerminateProcess(process);
        }
    }

    #region 跨平台核心逻辑

    private static bool IsTargetProcess(Process process, string processName, string? keyword)
    {
        try
        {
            // 验证进程名称
            if (!IsProcessNameMatch(process, processName))
                return false;

            // 验证命令行关键词
            return string.IsNullOrWhiteSpace(keyword) || GetCommandLine(process).Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false; // 忽略无法访问的进程
        }
    }

    private static bool IsProcessNameMatch(Process process, string targetName)
    {
        var actualName = Path.GetFileNameWithoutExtension(process.ProcessName);
        return actualName.Equals(targetName, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region 命令行获取（平台相关）

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static string GetCommandLine(Process process)
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? GetWindowsCommandLine(process) : GetUnixCommandLine(process.Id);
    }

    [SupportedOSPlatform("windows")]
    private static string GetWindowsCommandLine(Process process)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}");
            return searcher.Get()
                    .Cast<ManagementObject>()
                    .FirstOrDefault()?["CommandLine"]?.ToString()
                ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static string GetUnixCommandLine(int pid)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                var cmdlinePath = $"/proc/{pid}/cmdline";
                return File.Exists(cmdlinePath) ? File.ReadAllText(cmdlinePath, Encoding.UTF8).Replace('\0', ' ') : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
        else // macOS
        {
            var output = ExecuteShellCommand($"ps -p {pid} -o command=");
            return output?.Trim() ?? string.Empty;
        }
    }

    #endregion

    #region 进程终止（带权限处理）

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static void SafeTerminateProcess(Process process)
    {
        try
        {
            if (process.HasExited) return;

            if (NeedElevation(process))
            {
                ElevateKill(process.Id);
            }
            else
            {
                process.Kill();
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Error] 终止进程失败: {process.ProcessName} ({process.Id}) - {ex.Message}");
        }
        finally
        {
            process.Dispose();
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static bool NeedElevation(Process process)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            var uid = GetUnixUserId();
            var processUid = GetProcessUid(process.Id);
            return uid != processUid;
        }
        catch
        {
            return true; // 无法获取时默认需要提权
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static void ElevateKill(int pid)
    {
        ExecuteShellCommand($"sudo kill -9 {pid}");
    }

    #endregion

    #region Unix辅助方法

    [DllImport("libc", EntryPoint = "getuid")]
    private static extern uint GetUid();

    private static uint GetUnixUserId() => GetUid();

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static uint GetProcessUid(int pid)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var statusPath = $"/proc/{pid}/status";
            var uidLine = File.ReadLines(statusPath)
                .FirstOrDefault(l => l.StartsWith("Uid:"));
            return uint.Parse(uidLine?.Split('\t')[1] ?? "0");
        }
        else // macOS
        {
            var output = ExecuteShellCommand($"ps -p {pid} -o uid=");
            return uint.TryParse(output?.Trim(), out var uid) ? uid : 0;
        }
    }

    private static string? ExecuteShellCommand(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            return process?.StandardOutput.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }

    #endregion
    public async Task TestConnecting()
    {
        await GetTaskerAsync();
        var task = MaaTasker?.Controller?.LinkStart();
        task?.Wait();
        Instances.TaskQueueViewModel.SetConnected(task?.Status == MaaJobStatus.Succeeded);
    }

    public void Start(bool onlyStart = false, bool checkUpdate = false)
    {
        if (InitializeData())
        {
            var tasks = Instances.TaskQueueViewModel.TaskItemViewModels.ToList().FindAll(task => task.IsChecked || task.IsCheckedWithNull == null);
            StartTask(tasks, onlyStart, checkUpdate);
        }
    }

    public void Start(List<DragItemViewModel> dragItemViewModels, bool onlyStart = false, bool checkUpdate = false)
    {
        if (InitializeData())
        {
            var tasks = dragItemViewModels;
            StartTask(tasks, onlyStart, checkUpdate);
        }
    }

    public CancellationTokenSource? CancellationTokenSource { get; set; } = new();

    private DateTime? _startTime;

    public async Task StartTask(List<DragItemViewModel>? tasks, bool onlyStart = false, bool checkUpdate = false)
    {
        Status = MFATask.MFATaskStatus.NOT_STARTED;
        CancellationTokenSource = new CancellationTokenSource();

        _startTime = DateTime.Now;

        var token = CancellationTokenSource.Token;

        if (!onlyStart)
        {
            tasks ??= new List<DragItemViewModel>();
            var taskAndParams = tasks.Select(CreateNodeAndParam).ToList();
            InitializeConnectionTasksAsync(token);
            AddCoreTasksAsync(taskAndParams, token);
        }

        AddPostTasksAsync(onlyStart, checkUpdate, token);
        await TaskManager.RunTaskAsync(async () =>
        {
            await ExecuteTasks(token);
            Stop(Status, true, onlyStart);
        }, token: token, name: "启动任务");

    }

    async private Task ExecuteTasks(CancellationToken token)
    {
        while (TaskQueue.Count > 0 && !token.IsCancellationRequested)
        {
            var task = TaskQueue.Dequeue();
            var status = await task.Run(token);
            if (status != MFATask.MFATaskStatus.SUCCEEDED)
            {
                Status = status;
                return;
            }
        }
        if (Status == MFATask.MFATaskStatus.NOT_STARTED)
            Status = !token.IsCancellationRequested ? MFATask.MFATaskStatus.SUCCEEDED : MFATask.MFATaskStatus.STOPPED;
    }

    public class NodeAndParam
    {
        public string? Name { get; set; }
        public string? Entry { get; set; }
        public int? Count { get; set; }

        // public Dictionary<string, MaaNode>? Tasks
        // {
        //     get;
        //     set;
        // }
        public string? Param { get; set; }
    }


    private void UpdateTaskDictionary(ref Dictionary<string, JToken> taskModels,
        List<MaaInterface.MaaInterfaceSelectOption>? options,
        List<MaaInterface.MaaInterfaceSelectAdvanced>? advanceds)
    {
        // Instance.NodeDictionary = Instance.NodeDictionary.MergeMaaNodes(taskModels);
        if (options != null)
        {
            foreach (var selectOption in options)
            {
                if (Interface?.Option?.TryGetValue(selectOption.Name ?? string.Empty,
                        out var interfaceOption)
                    == true
                    && selectOption.Index is int index
                    && interfaceOption.Cases is { } cases
                    && cases[index]?.PipelineOverride != null)
                {
                    var param = interfaceOption.Cases[selectOption.Index.Value].PipelineOverride;
                    //       Instance.NodeDictionary = Instance.NodeDictionary.MergeMaaNodes(param);
                    taskModels = taskModels.MergeJTokens(param);
                }
            }
        }

        if (advanceds != null)
        {
            foreach (var selectAdvanced in advanceds)
            {
                if (!string.IsNullOrWhiteSpace(selectAdvanced.PipelineOverride) && selectAdvanced.PipelineOverride != "{}")
                {
                    var param = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(selectAdvanced.PipelineOverride);
                    //       Instance.NodeDictionary = Instance.NodeDictionary.MergeMaaNodes(param);
                    taskModels = taskModels.MergeJTokens(param);
                }
            }
        }
    }

    private string SerializeTaskParams(Dictionary<string, JToken> taskModels)
    {
        var settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        };

        try
        {
            return JsonConvert.SerializeObject(taskModels, settings);
        }
        catch (Exception)
        {
            return "{}";
        }
    }

    private NodeAndParam CreateNodeAndParam(DragItemViewModel task)
    {
        var taskModels = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(JsonConvert.SerializeObject(task.InterfaceItem?.PipelineOverride ?? new Dictionary<string, JToken>(), new JsonSerializerSettings()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        }));
        UpdateTaskDictionary(ref taskModels, task.InterfaceItem?.Option, task.InterfaceItem?.Advanced);

        var taskParams = SerializeTaskParams(taskModels);
        // var settings = new JsonSerializerSettings
        // {
        //     Formatting = Formatting.Indented,
        //     NullValueHandling = NullValueHandling.Ignore,
        //     DefaultValueHandling = DefaultValueHandling.Ignore
        // };
        // var json = JsonConvert.SerializeObject(Instance.BaseNodes, settings);
        //
        // var tasks = JsonConvert.DeserializeObject<Dictionary<string, MaaNode>>(json, settings);
        // tasks = tasks.MergeMaaNodes(taskModels);
        // Console.WriteLine(taskParams);
        return new NodeAndParam
        {
            Name = task.InterfaceItem?.Name,
            Entry = task.InterfaceItem?.Entry,
            Count = task.InterfaceItem?.Repeatable == true ? (task.InterfaceItem?.RepeatCount ?? 1) : 1,
            // Tasks = tasks,
            Param = taskParams
        };
    }

    private void InitializeConnectionTasksAsync(CancellationToken token)
    {
        TaskQueue.Enqueue(CreateMFATask("启动脚本", async () =>
        {
            await TaskManager.RunTaskAsync(async () => await RunScript(), token: token, name: "启动附加开始脚本");
        }));

        TaskQueue.Enqueue(CreateMFATask("连接设备", async () =>
        {
            await HandleDeviceConnectionAsync(token);
        }));

        TaskQueue.Enqueue(CreateMFATask("性能基准", async () =>
        {
            await MeasureScreencapPerformanceAsync(token);
        }));
    }

    public async Task MeasureScreencapPerformanceAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await MeasureExecutionTimeAsync(async () => await TaskManager.RunTaskAsync(() => MaaTasker?.Controller.Screencap().Wait(), token: token, name: "截图测试"));
    }

    async private Task HandleDeviceConnectionAsync(CancellationToken token, bool showMessage = true)
    {
        var controllerType = Instances.TaskQueueViewModel.CurrentController;
        var isAdb = controllerType == MaaControllerTypes.Adb;
        if (showMessage)
            RootView.AddLogByKeys(LangKeys.ConnectingTo, null, true, isAdb ? LangKeys.Emulator : LangKeys.Window);
        else
            ToastHelper.Info(LangKeys.Tip.ToLocalization(), LangKeys.ConnectingTo.ToLocalizationFormatted(true, isAdb ? LangKeys.Emulator : LangKeys.Window));
        if (Instances.TaskQueueViewModel.CurrentDevice == null)
            Instances.TaskQueueViewModel.TryReadAdbDeviceFromConfig(false, true);
        var tuple = await TryConnectAsync(token);
        var connected = tuple.Item1;
        if (!connected && isAdb && !tuple.Item2)
        {
            connected = await HandleAdbConnectionAsync(token, showMessage);
        }

        if (!connected)
        {
            HandleConnectionFailureAsync(isAdb, token);
            throw new Exception("Connection failed after all retries");
        }

        Instances.TaskQueueViewModel.SetConnected(true);
    }

    async private Task<bool> HandleAdbConnectionAsync(CancellationToken token, bool showMessage = true)
    {
        bool connected = false;
        var retrySteps = new List<Func<CancellationToken, Task<bool>>>
        {
            async t => await RetryConnectionAsync(t, showMessage, StartSoftware, LangKeys.TryToStartEmulator, Instances.ConnectSettingsUserControlModel.RetryOnDisconnected,
                () => Instances.TaskQueueViewModel.TryReadAdbDeviceFromConfig(false, true)),
            async t => await RetryConnectionAsync(t, showMessage, ReconnectByAdb, LangKeys.TryToReconnectByAdb),
            async t => await RetryConnectionAsync(t, showMessage, RestartAdb, LangKeys.RestartAdb, Instances.ConnectSettingsUserControlModel.AllowAdbRestart),
            async t => await RetryConnectionAsync(t, showMessage, HardRestartAdb, LangKeys.HardRestartAdb, Instances.ConnectSettingsUserControlModel.AllowAdbHardRestart)
        };

        foreach (var step in retrySteps)
        {
            if (token.IsCancellationRequested) break;
            connected = await step(token);
            if (connected) break;
        }

        return connected;
    }

    async private Task<bool> RetryConnectionAsync(CancellationToken token, bool showMessage, Func<Task> action, string logKey, bool enable = true, Action? other = null)
    {
        if (!enable) return false;
        token.ThrowIfCancellationRequested();
        if (showMessage)
            RootView.AddLog(LangKeys.ConnectFailed.ToLocalization() + "\n" + logKey.ToLocalization());
        else
            ToastHelper.Info(LangKeys.ConnectFailed.ToLocalization(), logKey.ToLocalization());
        await action();
        if (token.IsCancellationRequested)
        {
            Stop(MFATask.MFATaskStatus.STOPPED);
            return false;
        }
        other?.Invoke();
        var tuple = await TryConnectAsync(token);
        return tuple.Item1;
    }

    async private Task<(bool, bool)> TryConnectAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var tuple = await GetTaskerAndBoolAsync(token);
        return (tuple.Item1 is { IsInitialized: true }, tuple.Item2);
    }

    private void HandleConnectionFailureAsync(bool isAdb, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        LoggerHelper.Warning(LangKeys.ConnectFailed.ToLocalization());
        RootView.AddLogByKey(LangKeys.ConnectFailed);
        Instances.TaskQueueViewModel.SetConnected(false);
        ToastHelper.Warn(LangKeys.Warning_CannotConnect.ToLocalizationFormatted(true, isAdb ? LangKeys.Emulator : LangKeys.Window));
        Stop(MFATask.MFATaskStatus.STOPPED);
    }

    private void AddCoreTasksAsync(List<NodeAndParam> taskAndParams, CancellationToken token)
    {
        foreach (var task in taskAndParams)
        {
            TaskQueue.Enqueue(CreateMaaFWTask(task.Name,
                async () =>
                {
                    token.ThrowIfCancellationRequested();
                    // if (task.Tasks != null)
                    //     NodeDictionary = task.Tasks;
                    await TryRunTasksAsync(MaaTasker, task.Entry, task.Param, token);
                }, task.Count ?? 1
            ));
        }
    }

    async private Task TryRunTasksAsync(MaaTasker? maa, string? task, string? param, CancellationToken token)
    {
        if (maa == null || task == null) return;

        var job = maa.AppendTask(task, param ?? "{}");
        await TaskManager.RunTaskAsync((Action)(() =>
        {
            if (Instances
                .GameSettingsUserControlModel.ContinueRunningWhenError)
                job.Wait();
            else
                job.Wait().ThrowIfNot(MaaJobStatus.Succeeded);
        }), token, (ex) => throw ex, name: "队列任务", catchException: true, shouldLog: false);
    }

    async private Task RunScript(string str = "Prescript")
    {
        bool enable = str switch
        {
            "Prescript" => !string.IsNullOrWhiteSpace(ConfigurationManager.Current.GetValue(ConfigurationKeys.Prescript, string.Empty)),
            "Post-script" => !string.IsNullOrWhiteSpace(ConfigurationManager.Current.GetValue(ConfigurationKeys.Postscript, string.Empty)),
            _ => false,
        };
        if (!enable)
        {
            return;
        }

        Func<Task<bool>> func = str switch
        {
            "Prescript" => async () => await ExecuteScript(ConfigurationManager.Current.GetValue(ConfigurationKeys.Prescript, string.Empty)),
            "Post-script" => async () => await ExecuteScript(ConfigurationManager.Current.GetValue(ConfigurationKeys.Postscript, string.Empty)),
            _ => async () => false,
        };

        if (!await func())
        {
            LoggerHelper.Error($"Failed to execute the {str}.");
        }
    }

    async private static Task<bool> ExecuteScript(string scriptPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(scriptPath))
            {
                return false;
            }

            string fileName;
            string arguments;

            if (scriptPath.StartsWith('\"'))
            {
                var parts = scriptPath.Split("\"", 3);
                fileName = parts[1];
                arguments = parts.Length > 2 ? parts[2] : string.Empty;
            }
            else
            {
                fileName = scriptPath;
                arguments = string.Empty;
            }

            bool createNoWindow = arguments.Contains("-noWindow");
            bool minimized = arguments.Contains("-minimized");

            if (createNoWindow)
            {
                arguments = arguments.Replace("-noWindow", string.Empty).Trim();
            }

            if (minimized)
            {
                arguments = arguments.Replace("-minimized", string.Empty).Trim();
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WindowStyle = minimized ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal,
                    CreateNoWindow = createNoWindow,
                    UseShellExecute = !createNoWindow,
                },
            };
            process.Start();
            await process.WaitForExitAsync();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void AddPostTasksAsync(bool onlyStart, bool checkUpdate, CancellationToken token)
    {
        if (!onlyStart)
        {
            TaskQueue.Enqueue(CreateMFATask("结束脚本", async () =>
            {
                await TaskManager.RunTaskAsync(async () => await RunScript("Post-script"), token: token, name: "启动附加结束脚本");
            }));
        }
        if (checkUpdate)
        {
            TaskQueue.Enqueue(CreateMFATask("检查更新", async () =>
            {
                VersionChecker.Check();
            }, isUpdateRelated: true));
        }
    }
    private MFATask CreateMaaFWTask(string? name, Func<Task> action, int count = 1)
    {
        return new MFATask
        {
            Name = name,
            Count = count,
            Type = MFATask.MFATaskType.MAAFW,
            Action = action
        };
    }

    private MFATask CreateMFATask(string? name, Func<Task> action, bool isUpdateRelated = false)
    {
        return new MFATask
        {
            IsUpdateRelated = isUpdateRelated,
            Name = name,
            Type = MFATask.MFATaskType.MFA,
            Action = action
        };
    }

    #endregion

    #region 停止任务

    public void Stop(MFATask.MFATaskStatus status, bool finished = false, bool onlyStart = false, Action? action = null)
    {
        LoggerHelper.Info("Stop Status: " + Status);
        if (Status == MFATask.MFATaskStatus.STOPPING)
            return;
        Status = MFATask.MFATaskStatus.STOPPING;
        try
        {
            var isUpdateRelated = TaskQueue.Any(task => task.IsUpdateRelated);
            if (!ShouldProcessStop(finished))
            {
                ToastHelper.Warn(LangKeys.NoTaskToStop.ToLocalization());

                TaskQueue.Clear();
                return;
            }

            CancelOperations();

            TaskQueue.Clear();

            Instances.RootViewModel.IsRunning = false;

            ExecuteStopCore(finished, () =>
            {
                var stopResult = MaaJobStatus.Succeeded;

                if (status != MFATask.MFATaskStatus.FAILED)
                    stopResult = AbortCurrentTasker();

                HandleStopResult(status, stopResult, onlyStart, action, isUpdateRelated);
            });

        }
        catch (Exception ex)
        {
            HandleStopException(ex);
        }
    }

    private void CancelOperations()
    {
        if (!_agentStarted)
        {
            _agentProcess?.Kill();
            _agentProcess?.Dispose();
            _agentProcess = null;
        }
        _emulatorCancellationTokenSource?.SafeCancel();
        CancellationTokenSource.SafeCancel();
    }

    private bool ShouldProcessStop(bool finished)
    {
        return CancellationTokenSource?.IsCancellationRequested == false
            || finished;
    }

    private void ExecuteStopCore(bool finished, Action stopAction)
    {
        TaskManager.RunTaskAsync(() =>
        {
            if (!finished)
                RootView.AddLogByKey(LangKeys.Stopping);

            stopAction.Invoke();

            Instances.RootViewModel.Idle = true;
        }, null, "停止任务");
    }

    private MaaJobStatus AbortCurrentTasker()
    {
        if (MaaTasker == null)
            return MaaJobStatus.Succeeded;
        var status = MaaTasker.Stop().Wait();

        LoggerHelper.Info("Stopping tasker, status: " + status);
        return status;
    }

    private void HandleStopResult(MFATask.MFATaskStatus status, MaaJobStatus success, bool onlyStart, Action? action = null, bool isUpdateRelated = false)
    {
        if (success == MaaJobStatus.Succeeded)
        {
            DisplayTaskCompletionMessage(status, onlyStart, action);
        }
        else if (success == MaaJobStatus.Invalid)
        {
            RootView.AddLog(LangKeys.StoppingInternalTask.ToLocalization());
        }
        else
        {
            ToastHelper.Error(LangKeys.StoppingFailed.ToLocalization());
        }
        if (isUpdateRelated)
        {
            VersionChecker.Check();
        }
    }

    private void DisplayTaskCompletionMessage(MFATask.MFATaskStatus status, bool onlyStart = false, Action? action = null)
    {
        if (status == MFATask.MFATaskStatus.FAILED)
        {
            ToastHelper.Info(LangKeys.TaskFailed.ToLocalization());
            RootView.AddLogByKey(LangKeys.TaskFailed);
            ExternalNotificationHelper.ExternalNotificationAsync(Instances.ExternalNotificationSettingsUserControlModel.EnabledCustom
                ? Instances.ExternalNotificationSettingsUserControlModel.CustomFailureText
                : LangKeys.TaskFailed.ToLocalization());
        }
        else if (status == MFATask.MFATaskStatus.STOPPED)
        {
            ToastHelper.Info(LangKeys.TaskStopped.ToLocalization());
            RootView.AddLogByKey(LangKeys.TaskAbandoned);
        }
        else
        {
            if (!onlyStart)
            {
                Instances.TaskQueueViewModel.TaskItemViewModels.Where(t => t.IsCheckedWithNull == null).ToList().ForEach(d => d.IsCheckedWithNull = false);
                ToastNotification.Show(LangKeys.TaskCompleted.ToLocalization());
            }

            if (_startTime != null)
            {
                var elapsedTime = DateTime.Now - (DateTime)_startTime;
                RootView.AddLogByKeys(LangKeys.TaskAllCompletedWithTime, null, true, ((int)elapsedTime.TotalHours).ToString(),
                    ((int)elapsedTime.TotalMinutes % 60).ToString(), ((int)elapsedTime.TotalSeconds % 60).ToString());
            }
            else
            {
                RootView.AddLogByKey(LangKeys.TaskAllCompleted);
            }
            if (!onlyStart)
            {
                ExternalNotificationHelper.ExternalNotificationAsync(Instances.ExternalNotificationSettingsUserControlModel.EnabledCustom
                    ? Instances.ExternalNotificationSettingsUserControlModel.CustomSuccessText
                    : LangKeys.TaskAllCompleted.ToLocalization());
                HandleAfterTaskOperation();
            }
        }
        action?.Invoke();
        _startTime = null;
    }

    public void HandleAfterTaskOperation()
    {
        var afterTask = ConfigurationManager.Current.GetValue(ConfigurationKeys.AfterTask, "None");
        switch (afterTask)
        {
            case "CloseMFA":
                Instances.ShutdownApplication();
                break;
            case "CloseEmulator":
                CloseSoftware();
                break;
            case "CloseEmulatorAndMFA":
                CloseSoftwareAndMFA();
                break;
            case "ShutDown":
                Instances.ShutdownApplication();
                break;
            case "CloseEmulatorAndRestartMFA":
                CloseSoftwareAndRestartMFA();
                break;
            case "RestartPC":
                Instances.RestartSystem();
                break;
        }
    }

    public static void CloseSoftwareAndRestartMFA()
    {
        CloseSoftware();
        Instances.RestartApplication();
    }

    public static void CloseSoftware(Action? action = null)
    {
        if (Instances.TaskQueueViewModel.CurrentController == MaaControllerTypes.Adb)
        {
            EmulatorHelper.KillEmulatorModeSwitcher();
        }
        else
        {
            if (_softwareProcess != null && !_softwareProcess.HasExited)
            {
                _softwareProcess.Kill();
            }
            else
            {
                CloseProcessesByName(Config.DesktopWindow.Name, ConfigurationManager.Current.GetValue(ConfigurationKeys.EmulatorConfig, string.Empty));
                _softwareProcess = null;
            }

        }
        Instance.Stop(MFATask.MFATaskStatus.STOPPED);
        action?.Invoke();
    }

    public static void CloseSoftwareAndMFA()
    {
        CloseSoftware(Instances.ShutdownApplication);
    }

    private void HandleStopException(Exception ex)
    {
        LoggerHelper.Error($"Stop operation failed: {ex.Message}");
        ToastHelper.Error(LangKeys.StoppingFailed.ToLocalization());
    }

    #endregion

    #region 启动软件

    public async Task WaitSoftware()
    {
        if (ConfigurationManager.Current.GetValue(ConfigurationKeys.BeforeTask, "None").Contains("Startup", StringComparison.OrdinalIgnoreCase))
        {
            await StartSoftware();
        }

        Instances.TaskQueueViewModel.TryReadAdbDeviceFromConfig(false);
    }

    private CancellationTokenSource? _emulatorCancellationTokenSource;

    private static Process? _softwareProcess;

    public async Task StartSoftware()
    {
        _emulatorCancellationTokenSource = new CancellationTokenSource();
        await StartRunnableFile(ConfigurationManager.Current.GetValue(ConfigurationKeys.SoftwarePath, string.Empty),
            ConfigurationManager.Current.GetValue(ConfigurationKeys.WaitSoftwareTime, 60.0), _emulatorCancellationTokenSource.Token);
    }

    async private Task StartRunnableFile(string exePath, double waitTimeInSeconds, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return;
        var processName = Path.GetFileNameWithoutExtension(exePath);
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true,
            CreateNoWindow = false
        };
        if (Process.GetProcessesByName(processName).Length == 0)
        {
            if (!string.IsNullOrWhiteSpace(ConfigurationManager.Current.GetValue(ConfigurationKeys.EmulatorConfig, string.Empty)))
            {
                startInfo.Arguments = ConfigurationManager.Current.GetValue(ConfigurationKeys.EmulatorConfig, string.Empty);
                _softwareProcess =
                    Process.Start(startInfo);
            }
            else
                _softwareProcess = Process.Start(startInfo);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(ConfigurationManager.Current.GetValue(ConfigurationKeys.EmulatorConfig, string.Empty)))
            {
                startInfo.Arguments = ConfigurationManager.Current.GetValue(ConfigurationKeys.EmulatorConfig, string.Empty);
                _softwareProcess = Process.Start(startInfo);
            }
            else
                _softwareProcess = Process.Start(startInfo);
        }

        for (double remainingTime = waitTimeInSeconds + 1; remainingTime > 0; remainingTime -= 1)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (remainingTime % 10 == 0)
            {
                RootView.AddLogByKeys(LangKeys.WaitSoftwareTime, null, true,
                    Instances.TaskQueueViewModel.CurrentController == MaaControllerTypes.Adb
                        ? LangKeys.Emulator
                        : LangKeys.Window,
                    remainingTime.ToString()
                );
            }
            else if (remainingTime.Equals(waitTimeInSeconds))
            {
                RootView.AddLogByKeys(LangKeys.WaitSoftwareTime, null, true,
                    Instances.TaskQueueViewModel.CurrentController == MaaControllerTypes.Adb
                        ? LangKeys.Emulator
                        : LangKeys.Window,
                    remainingTime.ToString()
                );
            }


            await Task.Delay(1000, token);
        }

    }

    #endregion
}
