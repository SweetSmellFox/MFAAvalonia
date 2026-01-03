using Avalonia.Controls;
using Avalonia.Threading;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Notification;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Configuration;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.Helper.Converters;
using MFAAvalonia.ViewModels.Pages;
using MFAAvalonia.Views.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using Brushes = Avalonia.Media.Brushes;
using MaaAgentClient = MaaFramework.Binding.MaaAgentClient;
using MaaController = MaaFramework.Binding.MaaController;
using MFAAvalonia.Utilities.CardClass;
using MaaGlobal = MaaFramework.Binding.MaaGlobal;
using MaaResource = MaaFramework.Binding.MaaResource;
using MaaTasker = MaaFramework.Binding.MaaTasker;
using MaaToolkit = MaaFramework.Binding.MaaToolkit;

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

    // public Dictionary<string, MaaNode> BaseNodes = new();
    //
    // public Dictionary<string, MaaNode> NodeDictionary = new();
    public ObservableQueue<MFATask> TaskQueue { get; } = new();
    public bool IsV3 = false;

    /// <summary>
    /// JSON 加载设置，忽略注释（支持 JSONC 格式）
    /// </summary>
    private static readonly JsonLoadSettings JsoncLoadSettings = new()
    {
        CommentHandling = CommentHandling.Ignore
    };

    /// <summary>
    /// 获取 interface 文件路径，优先返回 .jsonc，其次 .json
    /// </summary>
    public static string? GetInterfaceFilePath()
    {
        var jsoncPath = Path.Combine(AppContext.BaseDirectory, "interface.jsonc");
        if (File.Exists(jsoncPath))
            return jsoncPath;

        var jsonPath = Path.Combine(AppContext.BaseDirectory, "interface.json");
        if (File.Exists(jsonPath))
            return jsonPath;

        return null;
    }

    public MaaProcessor()
    {
        TaskQueue.CountChanged += (_, args) =>
        {
            if (args.NewValue > 0)
                Instances.RootViewModel.IsRunning = true;
        };
        CheckInterface(out _, out _, out _, out _, out _);
        try
        {
            var filePath = GetInterfaceFilePath();
            if (filePath != null)
            {
                var content = File.ReadAllText(filePath);
                // 使用 JsonLoadSettings 忽略注释，支持 JSONC 格式
                var @interface = JObject.Parse(content, JsoncLoadSettings);
                var interfaceVersion = @interface["interface_version"]?.ToString();
                if (int.TryParse(interfaceVersion, out var result) && result >= 3)
                {
                    IsV3 = true;
                }
            }
        }
        catch (Exception e)
        {
            LoggerHelper.Error(e);
        }
    }

    public static MaaInterface? Interface
    {
        get => field;
        private set
        {
            field = value;

            foreach (var customResource in value?.Resource ?? Enumerable.Empty<MaaInterface.MaaInterfaceResource>())
            {
                var nameKey = customResource.Name?.Trim() ?? string.Empty;
                var paths = MaaInterface.ReplacePlaceholder(customResource.Path ?? new(), AppContext.BaseDirectory);
                customResource.ResolvedPath = paths;
                value!.Resources[nameKey] = customResource;
            }

            // 为 Option 字典中的每个项设置 Name（因为 Name 是 JsonIgnore 的）
            if (value?.Option != null)
            {
                foreach (var kvp in value.Option)
                {
                    kvp.Value.Name = kvp.Key;
                }
            }

            // 为 Advanced 字典中的每个项设置 Name（因为 Name 是 JsonIgnore 的）
            if (value?.Advanced != null)
            {
                foreach (var kvp in value.Advanced)
                {
                    kvp.Value.Name = kvp.Key;
                }
            }

            if (value != null)
            {
                // 将 VM 更新延迟到初始化完成之后，避免循环依赖导致死锁
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        Instances.SettingsViewModel.ShowResourceIssues = !string.IsNullOrWhiteSpace(value.Url) || !string.IsNullOrWhiteSpace(value.Github);
                        Instances.SettingsViewModel.ResourceGithub = (!string.IsNullOrWhiteSpace(value.Github) ? value.Github : value.Url) ?? "";
                        Instances.SettingsViewModel.ResourceIssues = $"{(!string.IsNullOrWhiteSpace(value.Github) ? value.Github : value.Url)}/issues";

                        Instances.TaskQueueViewModel.InitializeControllerOptions();
                        
                        // 异步加载 Contact 和 Description 内容
                        _ = LoadContactAndDescriptionAsync(value);
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.Error($"[MaaProcessor] 更新 ViewModel 失败: {ex.Message}");
                    }
                }, DispatcherPriority.Background);

                // 加载多语言配置
                if (value.Languages is { Count: > 0 })
                {
                    LanguageHelper.LoadLanguagesFromInterface(value.Languages, AppContext.BaseDirectory);
                }
            }

        }
    }

    /// <summary>
    /// 异步加载 Contact 和 Description 内容
    /// </summary>
    async private static Task LoadContactAndDescriptionAsync(MaaInterface maaInterface)
    {
        var projectDir = AppContext.BaseDirectory;

        // 加载 Description
        if (!string.IsNullOrWhiteSpace(maaInterface.Description))
        {
            var description = await maaInterface.Description.ResolveContentAsync(projectDir);
            Instances.SettingsViewModel.ResourceDescription = description;
            Instances.SettingsViewModel.HasResourceDescription = !string.IsNullOrWhiteSpace(description);
        }
        else
        {
            Instances.SettingsViewModel.ResourceDescription = string.Empty;
            Instances.SettingsViewModel.HasResourceDescription = false;
        }

        // 加载 Contact
        if (!string.IsNullOrWhiteSpace(maaInterface.Contact))
        {
            var contact = await maaInterface.Contact.ResolveContentAsync(projectDir);
            Instances.SettingsViewModel.ResourceContact = contact;
            Instances.SettingsViewModel.HasResourceContact = !string.IsNullOrWhiteSpace(contact);
        }
        else
        {
            Instances.SettingsViewModel.ResourceContact = string.Empty;
            Instances.SettingsViewModel.HasResourceContact = false;
        }

        // 加载 License
        if (!string.IsNullOrWhiteSpace(maaInterface.License))
        {
            var license = await maaInterface.License.ResolveContentAsync(projectDir);
            Instances.SettingsViewModel.ResourceLicense = license;
            Instances.SettingsViewModel.HasResourceLicense = !string.IsNullOrWhiteSpace(license);
        }
        else
        {
            Instances.SettingsViewModel.ResourceLicense = string.Empty;
            Instances.SettingsViewModel.HasResourceLicense = false;
        }
    }

    public static MaaFWConfiguration Config { get; } = new();
    public MaaTasker? MaaTasker { get; set; }

    public void SetTasker(MaaTasker? maaTasker = null)
    {
        if (maaTasker == null && MaaTasker != null)
        {
            var oldTasker = MaaTasker;
            MaaTasker = null; // 先设置为 null，防止重复释放

            try
            {
                // 使用超时机制避免无限等待，最多等待 5 秒
                var stopTask = Task.Run(() =>
                {
                    try
                    {
                        oldTasker.Stop().Wait();
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.Warning($"MaaTasker Stop inner failed: {ex.Message}");
                    }
                });

                if (!stopTask.Wait(TimeSpan.FromSeconds(5)))
                {
                    LoggerHelper.Warning("MaaTasker Stop timed out after 5 seconds");
                }
            }
            catch (Exception e)
            {
                LoggerHelper.Warning($"MaaTasker Stop failed: {e.Message}");
            }

            _agentStarted = false;
            SafeKillAgentProcess(oldTasker);
            Instances.TaskQueueViewModel.SetConnected(false);
        }
        else if (maaTasker != null)
        {
            MaaTasker = maaTasker;
        }
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

    public async Task<(MaaTasker?, bool, bool)> GetTaskerAndBoolAsync(CancellationToken token = default)
    {
        var tuple = MaaTasker != null ? (MaaTasker, false, false) : await InitializeMaaTasker(token);
        MaaTasker ??= tuple.Item1;
        return (MaaTasker, tuple.Item2, tuple.Item3);
    }

    public ObservableCollection<DragItemViewModel> TasksSource { get; private set; } =
        [];
    public AutoInitDictionary AutoInitDictionary { get; } = new();
    private FocusHandler? _focusHandler;
    private TaskLoader? _taskLoader;

    private MaaAgentClient? _agentClient;
    private bool _agentStarted;
    private Process? _agentProcess;
    private MFATask.MFATaskStatus Status = MFATask.MFATaskStatus.NOT_STARTED;

    public Bitmap? GetBitmapImage(bool test = true)
    {
        if (test)
            TryConnectAsync(CancellationToken.None);
        using var buffer = GetImage(MaaTasker?.Controller);
        return buffer?.ToBitmap();
    }

    /// <summary>
    /// 获取截图的MaaImageBuffer。调用者必须负责释放返回的 buffer。
    /// </summary>
    /// <param name="maaController">控制器实例</param>
    /// <returns>包含截图的 MaaImageBuffer，如果失败则返回 null</returns>
    public MaaImageBuffer? GetImage(IMaaController? maaController)
    {
        if (maaController == null)
            return null;

        var buffer = new MaaImageBuffer();
        try
        {
            var status = maaController.Screencap().Wait();
            if (status != MaaJobStatus.Succeeded)
            {
                buffer.Dispose();
                return null;
            }

            if (!maaController.GetCachedImage(buffer))
            {
                buffer.Dispose();
                return null;
            }

            return buffer;
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"GetImage failed: {ex.Message}");
            buffer.Dispose();
            return null;
        }
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

    private bool IsPathLike(string? input)
    {
        if (string.IsNullOrEmpty(input)) return false;

        bool hasPathSeparator = input.Contains(Path.DirectorySeparatorChar) || input.Contains(Path.AltDirectorySeparatorChar);
        bool isAbsolutePath = Path.IsPathRooted(input);
        bool isRelativePath = input.StartsWith("./") || input.StartsWith("../") || (hasPathSeparator && !input.StartsWith("-"));
        bool hasFileExtension = Path.HasExtension(input) && !input.StartsWith("-");

        return hasPathSeparator || isAbsolutePath || isRelativePath || hasFileExtension;
    }

    async private Task<(MaaTasker?, bool, bool)> InitializeMaaTasker(CancellationToken token) // 添加 async 和 token
    {
        var InvalidResource = false;
        var ShouldRetry = true;
        AutoInitDictionary.Clear();
        LoggerHelper.Info(LangKeys.LoadingResources.ToLocalization());

        if (Design.IsDesignMode)
        {
            return (null, false, false);
        }
        MaaResource maaResource = null;
        try
        {
            var currentResource = Instances.TaskQueueViewModel.CurrentResources
                .FirstOrDefault(c => c.Name == Instances.TaskQueueViewModel.CurrentResource);
            // 优先使用 ResolvedPath（运行时路径），如果没有则使用 Path
            var resources = currentResource?.ResolvedPath ?? currentResource?.Path ?? [];
            resources = resources.Select(Path.GetFullPath).ToList();

            LoggerHelper.Info($"Resource: {string.Join(",", resources)}");


            maaResource = await TaskManager.RunTaskAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                return new MaaResource(resources);
            }, token: token, name: "资源检测", catchException: true, shouldLog: false, handleError: exception =>
            {
                HandleInitializationError(exception, LangKeys.LoadResourcesFailed.ToLocalization(), LangKeys.LoadResourcesFailedDetail.ToLocalization());
                RootView.AddLog(LangKeys.LoadResourcesFailed.ToLocalization(), Brushes.OrangeRed, changeColor: false);
                InvalidResource = true;
                throw exception;
            });

            Instances.PerformanceUserControlModel.ChangeGpuOption(maaResource, Instances.PerformanceUserControlModel.GpuOption);

            LoggerHelper.Info(
                $"GPU acceleration: {(Instances.PerformanceUserControlModel.GpuOption.IsDirectML ? Instances.PerformanceUserControlModel.GpuOption.Adapter.AdapterName : Instances.PerformanceUserControlModel.GpuOption.Device.ToString())}{(Instances.PerformanceUserControlModel.GpuOption.IsDirectML ? $",Adapter Id: {Instances.PerformanceUserControlModel.GpuOption.Adapter.AdapterId}" : "")}");

        }
        catch (OperationCanceledException)
        {
            ShouldRetry = false;
            LoggerHelper.Warning("Resource loading was canceled");
            return (null, InvalidResource, ShouldRetry);
        }
        catch (MaaJobStatusException)
        {
            ShouldRetry = false;
            return (null, InvalidResource, ShouldRetry);
        }
        catch (Exception e)
        {
            ShouldRetry = false;
            LoggerHelper.Error("Initialization resource error", e);
            return (null, InvalidResource, ShouldRetry);
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

            var displayShortSide = Interface?.Controller?.Find(c => c.Type != null && c.Type.Equals(Instances.TaskQueueViewModel.CurrentController.ToJsonKey(), StringComparison.OrdinalIgnoreCase))?.DisplayShortSide;

            var displayLongSide = Interface?.Controller?.Find(c => c.Type != null && c.Type.Equals(Instances.TaskQueueViewModel.CurrentController.ToJsonKey(), StringComparison.OrdinalIgnoreCase))?.DisplayLongSide;
            var displayRaw = Interface?.Controller?.Find(c => c.Type != null && c.Type.Equals(Instances.TaskQueueViewModel.CurrentController.ToJsonKey(), StringComparison.OrdinalIgnoreCase))?.DisplayRaw;
            if (displayLongSide != null && displayShortSide == null && displayRaw == null)
                controller.SetOption_ScreenshotTargetLongSide(Convert.ToInt32(displayLongSide.Value));
            if (displayShortSide != null && displayLongSide == null && displayRaw == null)
                controller.SetOption_ScreenshotTargetShortSide(Convert.ToInt32(displayShortSide.Value));
            if (displayRaw != null && displayShortSide == null && displayLongSide == null)
                controller.SetOption_ScreenshotUseRawSize(displayRaw.Value);
        }
        catch (OperationCanceledException)
        {
            LoggerHelper.Warning("Controller initialization was canceled");
            return (null, InvalidResource, ShouldRetry);
        }
        catch (MaaException)
        {
            return (null, InvalidResource, ShouldRetry); // 控制器异常可以重试
        }
        catch (Exception e)
        {
            LoggerHelper.Error("Initialization controller error", e);
            return (null, InvalidResource, ShouldRetry); // 控制器错误可以重试
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

            tasker.Releasing += (_, _) =>
            {
                tasker.Callback -= HandleCallBack;
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
            // 注册内置的自定义 Action（用于内存泄漏测试）
            //tasker.Resource.Register(new Custom.MemoryLeakTestAction());
            // 获取代理配置（假设Interface在UI线程中访问）
            var agentConfig = Interface?.Agent;
            if (agentConfig is { ChildExec: not null } && !_agentStarted)
            {
                RootView.AddLogByKey(LangKeys.StartingAgent);
                if (_agentClient != null)
                {
                    SafeKillAgentProcess();
                }

                var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
                var identifier = string.IsNullOrWhiteSpace(Interface?.Agent?.Identifier) ? new string(Enumerable.Repeat(chars, 8).Select(c => c[Random.Next(c.Length)]).ToArray()) : Interface.Agent.Identifier;
                LoggerHelper.Info($"Agent Identifier: {identifier}");
                try
                {
                    _agentClient = MaaAgentClient.Create(identifier, tasker);
                    var timeOut = Interface?.Agent?.Timeout ?? 120;
                    _agentClient.SetTimeout(TimeSpan.FromSeconds(timeOut < 0 ? int.MaxValue : timeOut));
                    _agentClient.Releasing += (_, _) =>
                    {
                        LoggerHelper.Info("退出Agent进程");
                        _agentClient = null;
                    };

                    LoggerHelper.Info($"Agent Client Hash: {_agentClient?.GetHashCode()}");
                    if (!Directory.Exists($"{AppContext.BaseDirectory}"))
                        Directory.CreateDirectory($"{AppContext.BaseDirectory}");
                    var program = MaaInterface.ReplacePlaceholder(agentConfig.ChildExec, AppContext.BaseDirectory, true);
                    if (IsPathLike(program))
                        program = Path.GetFullPath(program, AppContext.BaseDirectory);
                    var rawArgs = agentConfig.ChildArgs ?? [];
                    var replacedArgs = MaaInterface.ReplacePlaceholder(rawArgs, AppContext.BaseDirectory, true)
                        .Select(arg =>
                        {
                            if (IsPathLike(arg))
                            {
                                try
                                {
                                    return Path.GetFullPath(arg, AppContext.BaseDirectory);
                                }
                                catch (Exception)
                                {
                                    // 若路径解析失败（如伪路径），返回原参数
                                    return arg;
                                }
                            }
                            return arg;
                        })
                        .Select(ConvertPath).ToList();

                    var executablePath = PathFinder.FindPath(program);

                    // 检查可执行文件是否存在
                    if (!File.Exists(executablePath))
                    {
                        var errorMsg = LangKeys.AgentExecutableNotFound.ToLocalizationFormatted(false, executablePath);
                        throw new FileNotFoundException(errorMsg, executablePath);
                    }

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = executablePath,
                        WorkingDirectory = AppContext.BaseDirectory,
                        Arguments = $"{(program!.Contains("python") && replacedArgs.Contains(".py") && !replacedArgs.Any(arg => arg.Contains("-u")) ? "-u " : "")}{string.Join(" ", replacedArgs)} {_agentClient.Id}",
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true
                    };

                    LoggerHelper.Info(
                        $"Agent Command: {program} {(program!.Contains("python") && replacedArgs.Contains(".py") && !replacedArgs.Any(arg => arg.Contains("-u")) ? "-u " : "")}{string.Join(" ", replacedArgs)} {_agentClient.Id} "
                        + $"socket_id: {_agentClient.Id}");
                    IMaaAgentClient.AgentServerStartupMethod method = (s, directory) =>
                    {
                        _agentProcess = Process.Start(startInfo);
                        if (_agentProcess == null)
                            LoggerHelper.Error("Agent start failed!");
                        else
                        {
                            _agentProcess.Exited += (_, _) =>
                            {
                                LoggerHelper.Info("Agent process exited!");
                                LoggerHelper.Info("MaaTasker exited!");
                                _agentProcess = null;
                            };
                            _agentProcess?.OutputDataReceived += (sender, args) =>
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
                                        if (TaskQueueViewModel.CheckShouldLog(outData))
                                        {
                                            RootView.AddLog(outData);
                                        }
                                        else
                                        {
                                            LoggerHelper.Info("agent:" + outData);
                                        }
                                    });
                                }
                            };

                            _agentProcess?.ErrorDataReceived += (sender, args) =>
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
                                        if (TaskQueueViewModel.CheckShouldLog(outData))
                                        {
                                            RootView.AddLog(outData);
                                        }
                                        else
                                        {
                                            LoggerHelper.Info("agent:" + outData);
                                        }
                                    });
                                }
                            };
                            _agentProcess?.BeginOutputReadLine();
                            _agentProcess?.BeginErrorReadLine();
                            if (_agentProcess != null)
                                TaskManager.RunTaskAsync(async () => await _agentProcess.WaitForExitAsync(token), token: token, name: "Agent程序启动");

                        }
                        return _agentProcess;
                    };
                    // 添加重连逻辑，最多重试3次
                    const int maxRetries = 3;
                    bool linkStartSuccess = false;
                    Exception? lastException = null;

                    for (int retryCount = 0; retryCount < maxRetries && !linkStartSuccess && !token.IsCancellationRequested; retryCount++)
                    {
                        try
                        {
                            // 在每次迭代开始时检测token
                            token.ThrowIfCancellationRequested();

                            if (retryCount > 0)
                            {
                                LoggerHelper.Info($"Agent LinkStart retry attempt {retryCount + 1}/{maxRetries}");

                                RootView.AddLog(LangKeys.AgentConnectionRetry.ToLocalizationFormatted(false, $"{retryCount + 1}/{maxRetries}"), Brushes.Orange, changeColor: false);
                                // 等待一段时间后重试
                                await Task.Delay(1000 * retryCount, token);

                                // 重新启动进程
                                if (_agentProcess != null && !_agentProcess.HasExited)
                                {
                                    try
                                    {
                                        _agentProcess.Kill(true);
                                        _agentProcess.WaitForExit(3000);
                                    }
                                    catch (Exception killEx)
                                    {
                                        LoggerHelper.Warning($"Failed to kill agent process: {killEx.Message}");
                                    }
                                    _agentProcess.Dispose();
                                    _agentProcess = null;
                                }
                            }

                            linkStartSuccess = _agentClient.LinkStart(method, token);
                        }
                        catch (OperationCanceledException)
                        {
                            // 任务被取消，直接退出重试循环
                            LoggerHelper.Info("Agent LinkStart was canceled by user");
                            throw;
                        }
                        catch (SEHException sehEx)
                        {
                            lastException = sehEx;
                            LoggerHelper.Warning($"SEHException during LinkStart (attempt {retryCount + 1}): {sehEx.Message}");

                            if (retryCount < maxRetries - 1)
                            {
                                // 在重试前检测token
                                if (token.IsCancellationRequested)
                                {
                                    LoggerHelper.Info("Agent retry canceled by user");
                                    token.ThrowIfCancellationRequested();
                                }

                                // 清理当前状态，准备重试
                                SafeKillAgentProcess();

                                // 重新创建 AgentClient
                                try
                                {
                                    _agentClient = MaaAgentClient.Create(identifier, tasker);
                                    timeOut = Interface?.Agent?.Timeout ?? 120;
                                    _agentClient.SetTimeout(TimeSpan.FromSeconds(timeOut < 0 ? int.MaxValue : timeOut));
                                    _agentClient.Releasing += (_, _) =>
                                    {
                                        LoggerHelper.Info("退出Agent进程");
                                    };
                                }
                                catch (Exception recreateEx)
                                {
                                    LoggerHelper.Error($"Failed to recreate AgentClient: {recreateEx.Message}");
                                    throw;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;
                            LoggerHelper.Warning($"Exception during LinkStart (attempt {retryCount + 1}): {ex.Message}");

                            // 对于非 SEHException，不进行重试
                            break;
                        }
                    }
                    // 循环结束后检查是否因为取消而退出
                    if (token.IsCancellationRequested && !linkStartSuccess)
                    {
                        LoggerHelper.Info("Agent LinkStart loop exited due to cancellation");
                        token.ThrowIfCancellationRequested();
                    }
                    if (!linkStartSuccess)
                    {
                        // 尝试获取进程的错误输出
                        var errorMessage = lastException?.Message ?? "Failed to LinkStart agentClient!";
                        var agentProcess = _agentProcess;
                        if (agentProcess != null)
                        {
                            try
                            {
                                var errorDetails = new StringBuilder();
                                errorDetails.AppendLine(errorMessage);

                                // 如果进程已经退出，尝试读取错误输出
                                if (agentProcess.HasExited)
                                {
                                    var exitCode = agentProcess.ExitCode;
                                    var stderr = await agentProcess.StandardError.ReadToEndAsync(token);
                                    var stdout = await agentProcess.StandardOutput.ReadToEndAsync(token);

                                    errorDetails.AppendLine($"Agent process exited with code: {exitCode}");

                                    if (!string.IsNullOrWhiteSpace(stderr))
                                    {
                                        errorDetails.AppendLine($"StandardError: {stderr}");
                                        LoggerHelper.Error($"Agent StandardError: {stderr}");
                                        RootView.AddLog($"Agent Error: {stderr}", Brushes.OrangeRed, changeColor: false);
                                    }

                                    if (!string.IsNullOrWhiteSpace(stdout))
                                    {
                                        errorDetails.AppendLine($"StandardOutput: {stdout}");
                                        LoggerHelper.Info($"Agent StandardOutput: {stdout}");
                                    }
                                    errorMessage = errorDetails.ToString();
                                }
                                else
                                {
                                    // 进程还在运行但 LinkStart 失败，等待一小段时间让进程退出
                                    if (agentProcess.WaitForExit(3000))
                                    {
                                        var exitCode = agentProcess.ExitCode;
                                        var stderr = await agentProcess.StandardError.ReadToEndAsync(token);
                                        var stdout = await agentProcess.StandardOutput.ReadToEndAsync(token);

                                        errorDetails.AppendLine($"Agent process exited with code: {exitCode}");

                                        if (!string.IsNullOrWhiteSpace(stderr))
                                        {
                                            errorDetails.AppendLine($"StandardError: {stderr}");
                                            LoggerHelper.Error($"Agent StandardError: {stderr}");
                                            RootView.AddLog($"Agent Error: {stderr}", Brushes.OrangeRed, changeColor: false);

                                            if (!string.IsNullOrWhiteSpace(stdout))
                                            {
                                                errorDetails.AppendLine($"StandardOutput: {stdout}");
                                                LoggerHelper.Info($"Agent StandardOutput: {stdout}");

                                                errorMessage = errorDetails.ToString();
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception readEx)
                            {
                                LoggerHelper.Warning($"Failed to read agent process output: {readEx.Message}");
                            }
                        }
                        throw new Exception(errorMessage);
                    }
                }
                catch (OperationCanceledException)
                {
                    // 任务被用户取消，直接向上抛出，不显示错误信息
                    LoggerHelper.Info("Agent initialization was canceled by user");
                    SafeKillAgentProcess();
                    throw;
                }
                catch (Exception ex)
                {
                    RootView.AddLogByKey(LangKeys.AgentStartFailed, Brushes.OrangeRed, changeColor: false);
                    LoggerHelper.Error(ex);
                    ToastHelper.Error(LangKeys.AgentStartFailed.ToLocalization(), ex.Message);
                    SafeKillAgentProcess();
                    ShouldRetry = false; // Agent 启动失败不应该重连
                    return (null, InvalidResource, ShouldRetry);
                }


                _agentStarted = true;
            }
            RegisterCustomRecognitionsAndActions(tasker);
            Instances.TaskQueueViewModel.SetConnected(true);
            //  tasker.Utility.SetOption_Recording(ConfigurationManager.Maa.GetValue(ConfigurationKeys.Recording, false));
            tasker.Global.SetOption_SaveDraw(ConfigurationManager.Maa.GetValue(ConfigurationKeys.SaveDraw, false));
            tasker.Global.SetOption(GlobalOption.SaveOnError, ConfigurationManager.Maa.GetValue(ConfigurationKeys.SaveOnError, true));
            tasker.Global.SetOption_DebugMode(ConfigurationManager.Maa.GetValue(ConfigurationKeys.ShowHitDraw, false));

            LoggerHelper.Info("Maafw debug mode: " + ConfigurationManager.Maa.GetValue(ConfigurationKeys.ShowHitDraw, false));
            // 注意：只订阅一次回调，避免嵌套订阅导致内存泄漏
            tasker.Callback += HandleCallBack;

            return (tasker, InvalidResource, ShouldRetry);
        }
        catch (OperationCanceledException)
        {
            LoggerHelper.Warning("Tasker initialization was canceled");
            return (null, InvalidResource, ShouldRetry);
        }
        catch (MaaException)
        {
            return (null, InvalidResource, ShouldRetry);
        }
        catch (Exception e)
        {
            LoggerHelper.Error("Initialization tasker error", e);
            return (null, InvalidResource, ShouldRetry);
        }
    }

    public void HandleCallBack(object? sender, MaaCallbackEventArgs args)
    {
        var jObject = JObject.Parse(args.Details);
        MaaTasker? tasker = null;
        if (sender is MaaTasker t)
            tasker = t;
        if (sender is MaaContext context)
            tasker = context.Tasker;
        if (tasker != null && Instances.GameSettingsUserControlModel.ShowHitDraw)
        {
            var name = jObject["name"]?.ToString() ?? string.Empty;
            if (args.Message.StartsWith(MaaMsg.Node.Recognition.Succeeded) || args.Message.StartsWith(MaaMsg.Node.Action.Succeeded))
            {
                if (jObject["reco_id"] != null)
                {
                    var recoId = Convert.ToInt64(jObject["reco_id"]?.ToString() ?? string.Empty);
                    if (recoId > 0)
                    {
                        Bitmap? bitmapToSet = null;
                        try
                        {
                            //使用 using 确保资源正确释放
                            using var rect = new MaaRectBuffer();
                            var imageBuffer = new MaaImageBuffer();
                            using var imageListBuffer = new MaaImageListBuffer();
                            tasker.GetRecognitionDetail(recoId, out string node,
                                out var algorithm,
                                out var hit,
                                rect,
                                out var detailJson,
                                imageBuffer, imageListBuffer);
                            var bitmap = imageBuffer.ToBitmap();
                            if (bitmap != null)
                            {
                                if (hit)
                                {
                                    var newBitmap = bitmap.DrawRectangle(rect, Brushes.LightGreen, 1.5f);
                                    // 如果 DrawRectangle 返回了新的 Bitmap，释放原始的
                                    if (!ReferenceEquals(newBitmap, bitmap))
                                    {
                                        bitmap.Dispose();
                                    }
                                    bitmap = newBitmap;
                                }
                                bitmapToSet = bitmap;
                            }
                        }
                        catch (Exception ex)
                        {
                            LoggerHelper.Warning($"HandleCallBack recognition error: {ex.Message}");
                            bitmapToSet?.Dispose();
                            bitmapToSet = null;
                        }


                        if (bitmapToSet != null)
                        {
                            var finalBitmap = bitmapToSet;
                            DispatcherHelper.PostOnMainThread(() =>
                            {
                                // 释放旧的截图
                                var oldImage = Instances.ScreenshotViewModel.ScreenshotImage;
                                Instances.ScreenshotViewModel.ScreenshotImage = finalBitmap;
                                Instances.ScreenshotViewModel.TaskName = name;
                                oldImage?.Dispose();
                            });
                        }
                    }
                }

                if (jObject["action_id"] != null)
                {
                    var actionId = Convert.ToInt64(jObject["action_id"]?.ToString() ?? string.Empty);
                    if (actionId > 0)
                    {
                        Bitmap? bitmapToSet = null;
                        try
                        {
                            // 使用 using 确保资源正确释放
                            using var rect = new MaaRectBuffer();
                            using var imageBuffer = new MaaImageBuffer();
                            tasker.GetCachedImage(imageBuffer);
                            var bitmap = imageBuffer.ToBitmap();
                            tasker.GetActionDetail(actionId, out _, out _, rect, out var isSucceeded, out _);
                            if (bitmap != null)
                            {
                                if (isSucceeded)
                                {
                                    var newBitmap = bitmap.DrawRectangle(rect, Brushes.LightGreen, 1.5f);
                                    // 如果 DrawRectangle 返回了新的 Bitmap，释放原始的
                                    if (!ReferenceEquals(newBitmap, bitmap))
                                    {
                                        bitmap.Dispose();
                                    }
                                    bitmap = newBitmap;
                                }
                                bitmapToSet = bitmap;
                            }
                        }
                        catch (Exception ex)
                        {
                            LoggerHelper.Warning($"HandleCallBack action error: {ex.Message}");
                            bitmapToSet?.Dispose();
                            bitmapToSet = null;
                        }


                        if (bitmapToSet != null)
                        {
                            var finalBitmap = bitmapToSet;
                            DispatcherHelper.PostOnMainThread(() =>
                            {
                                // 释放旧的截图
                                var oldImage = Instances.ScreenshotViewModel.ScreenshotImage;
                                Instances.ScreenshotViewModel.ScreenshotImage = finalBitmap;
                                Instances.ScreenshotViewModel.TaskName = name;
                                oldImage?.Dispose();
                            });
                        }
                    }
                }
            }
        }
        if (jObject.ContainsKey("focus"))
        {
            _focusHandler ??= new FocusHandler(AutoInitDictionary);
            _focusHandler.UpdateDictionary(AutoInitDictionary);
            _focusHandler.DisplayFocus(jObject, args.Message, args.Details);
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

    private void HandleInitializationError(Exception e,
        string title,
        string message,
        bool hasWarning = false,
        string waringMessage = "")
    {
        ToastHelper.Error(title, message);
        if (hasWarning)
            LoggerHelper.Warning(waringMessage);
        LoggerHelper.Error(e.ToString());
    }

    private MaaController InitializeController(bool isAdb)
    {
        ConnectToMAA();
        if (isAdb)
        {
            LoggerHelper.Info($"Name: {Config.AdbDevice.Name}");
            LoggerHelper.Info($"AdbPath: {Config.AdbDevice.AdbPath}");
            LoggerHelper.Info($"AdbSerial: {Config.AdbDevice.AdbSerial}");
            LoggerHelper.Info($"ScreenCap: {Config.AdbDevice.ScreenCap}");
            LoggerHelper.Info($"Input: {Config.AdbDevice.Input}");
            LoggerHelper.Info($"Config: {Config.AdbDevice.Config}");
        }
        else
        {
            LoggerHelper.Info($"Name: {Config.DesktopWindow.Name}");
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
                Path.Combine(AppContext.BaseDirectory, "libs", "MaaAgentBinary")
            )
            : new MaaWin32Controller(
                Config.DesktopWindow.HWnd,
                Config.DesktopWindow.ScreenCap, Config.DesktopWindow.Mouse, Config.DesktopWindow.KeyBoard,
                Config.DesktopWindow.Link,
                Config.DesktopWindow.Check);
    }

    public static bool CheckInterface(out string Name, out string NameFallBack, out string Version, out string CustomTitle, out string CustomTitleFallBack)
    {
        // 支持 interface.json 和 interface.jsonc
        if (GetInterfaceFilePath() == null)
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
            };
            string resourceDir = Path.Combine(AppContext.BaseDirectory, "resource", "base");
            if (!Directory.Exists(resourceDir))
                Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, resourceDir));
            JsonHelper.SaveJson(Path.Combine(AppContext.BaseDirectory, "interface.json"),
                Interface, new MaaInterfaceSelectAdvancedConverter(true), new MaaInterfaceSelectOptionConverter(true));
            Name = Interface?.Label ?? string.Empty;
            NameFallBack = Interface?.Name ?? string.Empty;
            Version = Interface?.Version ?? string.Empty;
            CustomTitle = Interface?.Title ?? string.Empty;
            CustomTitleFallBack = Interface?.CustomTitle ?? string.Empty;
            return true;
        }
        Name = string.Empty;
        Version = string.Empty;
        CustomTitle = string.Empty;
        NameFallBack = string.Empty;
        CustomTitleFallBack = string.Empty;
        return false;
    }

// 防止 interface 加载失败时 Toast 重复显示
    private static bool _interfaceLoadErrorShown = false;

    public static (string Key, string Fallback, string Version, string CustomTitle, string CustomFallback) ReadInterface()
    {
        if (CheckInterface(out string name, out string back, out string version, out string customTitle, out var fallBack))
        {
            return (name, back, version, customTitle, fallBack);
        }

        var interfacePath = GetInterfaceFilePath() ?? Path.Combine(AppContext.BaseDirectory, "interface.json");
        var interfaceFileName = Path.GetFileName(interfacePath);
        var defaultValue = new MaaInterface();
        var error = "";
        Interface =
            JsonHelper.LoadJson(interfacePath, defaultValue
                , errorHandle: () =>
                {
                    try
                    {
                        if (File.Exists(interfacePath))
                        {
                            var content = File.ReadAllText(interfacePath);
                            // 使用 JsonLoadSettings 忽略注释，支持 JSONC 格式
                            var @interface = JObject.Parse(content, JsoncLoadSettings);
                            if (@interface != null)
                            {
                                defaultValue.MFAMinVersion = @interface["mfa_min_version"]?.ToString();
                                defaultValue.MFAMaxVersion = @interface["mfa_max_version"]?.ToString();
                                defaultValue.CustomTitle = @interface["custom_title"]?.ToString();
                                defaultValue.Title = @interface["title"]?.ToString();
                                defaultValue.Name = @interface["name"]?.ToString();
                                defaultValue.Url = @interface["url"]?.ToString();
                                defaultValue.Github = @interface["github"]?.ToString();
                            }
                        }
                        // 在 UI 层面显示 Toast 错误提示（只显示一次）
                        if (!_interfaceLoadErrorShown)
                        {
                            _interfaceLoadErrorShown = true;
                            error = LangKeys.FileLoadFailed.ToLocalizationFormatted(false, interfaceFileName);
                            var errorDetail = LangKeys.FileLoadFailedDetail.ToLocalizationFormatted(false, interfaceFileName);
                            // 延迟添加 UI 日志，确保 TaskQueueViewModel 已初始化
                            RootView.AddLog($"error:{error}");
                            ToastHelper.Error(error, errorDetail, duration: 15);
                        }
                    }
                    catch (Exception e)
                    {
                        LoggerHelper.Error(e);
                        // 即使解析失败也显示 Toast 错误提示（只显示一次）
                        if (!_interfaceLoadErrorShown)
                        {
                            _interfaceLoadErrorShown = true;
                            error = LangKeys.FileLoadFailed.ToLocalizationFormatted(false, interfaceFileName);
                            RootView.AddLog($"error:{error}");
                            ToastHelper.Error(
                                error,
                                e.Message,
                                duration: 10);
                        }
                    }
                }, new MaaInterfaceSelectAdvancedConverter(false),
                new MaaInterfaceSelectOptionConverter(false));


        return (Interface?.Label ?? string.Empty, Interface?.Name ?? string.Empty, Interface?.Version ?? string.Empty, Interface?.Title ?? string.Empty, Interface?.CustomTitle ?? string.Empty);

    }

    public bool InitializeData(Collection<DragItemViewModel>? dragItem = null)
    {
        var (name, back, version, customTitle, fallback) = ReadInterface();
        if ((!string.IsNullOrWhiteSpace(name) && !name.Equals("debug", StringComparison.OrdinalIgnoreCase)) || !string.IsNullOrWhiteSpace(back))
            Instances.RootViewModel.ShowResourceKeyAndFallBack(name, back);
        if (!string.IsNullOrWhiteSpace(version) && !version.Equals("debug", StringComparison.OrdinalIgnoreCase))
        {
            Instances.RootViewModel.ShowResourceVersion(version);
            Instances.VersionUpdateSettingsUserControlModel.ResourceVersion = version;

            // 首次初始化时，根据资源版本自动设置更新来源
            // 优先级：内测(Alpha=0) > 公测(Beta=1) > 稳定(Stable=2)
            // 只有当资源版本的优先级高于当前设置时才修改
            if (!ConfigurationManager.Current.GetValue(ConfigurationKeys.ResourceUpdateChannelInitialized, false))
            {
                var resourceVersionType = version.ToVersionType();
                var currentChannelIndex = Instances.VersionUpdateSettingsUserControlModel.ResourceUpdateChannelIndex;
                var currentChannelType = currentChannelIndex.ToVersionType();

                // 如果资源版本类型的优先级更高（数值更小），则更新设置
                if (resourceVersionType < currentChannelType)
                {
                    var newIndex = (int)resourceVersionType;
                    Instances.VersionUpdateSettingsUserControlModel.ResourceUpdateChannelIndex = newIndex;
                    LoggerHelper.Info($"根据资源版本 '{version}' 自动将更新来源设置为 {resourceVersionType}");
                }
                // 标记已初始化
                ConfigurationManager.Current.SetValue(ConfigurationKeys.ResourceUpdateChannelInitialized, true);
            }
        }

        if (!string.IsNullOrWhiteSpace(customTitle) || !string.IsNullOrWhiteSpace(fallback))
            Instances.RootViewModel.ShowCustomTitleAndFallBack(customTitle, fallback);

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
                // 优先使用 ResolvedPath（运行时路径），如果没有则使用 Path
                var resourcePaths = resources?.ResolvedPath ?? resources?.Path;
                if (resourcePaths != null)
                {
                    foreach (var resourcePath in resourcePaths)
                    {
                        var pipeline = Path.Combine(resourcePath, "pipeline");
                        if (!Path.Exists(pipeline))
                            break;
                        var jsonFiles = Directory.GetFiles(Path.GetFullPath(pipeline), "*.json", SearchOption.AllDirectories);
                        var jsoncFiles = Directory.GetFiles(Path.GetFullPath(pipeline), "*.jsonc", SearchOption.AllDirectories);
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
            // 优先使用 ResolvedPath（运行时路径），如果没有则使用 Path
            var currentRes = Instances.TaskQueueViewModel.CurrentResources.FirstOrDefault(c => c.Name == Instances.TaskQueueViewModel.CurrentResource);
            var resourceP = string.IsNullOrWhiteSpace(Instances.TaskQueueViewModel.CurrentResource)
                ? ResourceBase
                : (currentRes?.ResolvedPath?[0] ?? currentRes?.Path?[0]) ?? ResourceBase;
            var resourcePs = string.IsNullOrWhiteSpace(Instances.TaskQueueViewModel.CurrentResource)
                ? [ResourceBase]
                : (currentRes?.ResolvedPath ?? currentRes?.Path);

            if (resourcePs is { Count: > 0 })
            {
                foreach (var rp in resourcePs)
                {
                    if (string.IsNullOrWhiteSpace(rp))
                        continue;

                    try
                    {
                        // 验证路径是否有效
                        var fullPath = Path.GetFullPath(rp);
                        if (!Directory.Exists(fullPath))
                            Directory.CreateDirectory(fullPath);
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.Warning($"Failed to create resource directory '{rp}': {ex.Message}");
                    }
                }
            }
            if (fileCount == 0)
            {
                var pipeline = Path.Combine(resourceP, "pipeline");
                if (!string.IsNullOrWhiteSpace(pipeline) && !Directory.Exists(pipeline))
                {
                    try
                    {
                        Directory.CreateDirectory(pipeline);
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.Error(ex);
                    }
                }

                if (!File.Exists(Path.Combine(pipeline, "sample.json")))
                {
                    try
                    {
                        File.WriteAllText(Path.Combine(pipeline, "sample.json"),
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
    public const string NEW_SEPARATOR = "<|||>";
    public const string OLD_SEPARATOR = ":";

    private void LoadTasks(List<MaaInterface.MaaInterfaceTask> tasks, IList<DragItemViewModel>? oldDrags = null)
    {
        _taskLoader ??= new TaskLoader(Interface);
        _taskLoader.LoadTasks(tasks, TasksSource, ref FirstTask, oldDrags);
    }
    private string? _tempResourceVersion;

    public void AppendVersionLog(string? resourceVersion)
    {
        if (resourceVersion is null || _tempResourceVersion == resourceVersion)
        {
            return;
        }
        _tempResourceVersion = resourceVersion;
        var frameworkVersion = "";
        try
        {
            frameworkVersion = NativeBindingContext.LibraryVersion;
        }
        catch (Exception e)
        {
            frameworkVersion = "Unknown";
            LoggerHelper.Error("Failed to get MaaFramework version", e);
        }

        // Log all version information
        LoggerHelper.Info($"Resource version: {_tempResourceVersion}");
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
        await ProcessHelper.RestartAdbAsync(Config.AdbDevice.AdbPath);
    }

    public async Task ReconnectByAdb()
    {
        await ProcessHelper.ReconnectByAdbAsync(Config.AdbDevice.AdbPath, Config.AdbDevice.AdbSerial);
    }


    public async Task HardRestartAdb()
    {
        ProcessHelper.HardRestartAdb(Config.AdbDevice.AdbPath);
    }

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
        // 保存当前的任务列表，以便在重新加载时保留用户调整的顺序和 check 状态
        var currentTasks = new Collection<DragItemViewModel>(Instances.TaskQueueViewModel.TaskItemViewModels.ToList());

        if (InitializeData(currentTasks))
        {
            // 排除不支持当前资源包的任务（IsResourceSupported 为 false 的任务）
            // 排除 resource option 项（它们不参与任务执行，只提供参数）
            var tasks = Instances.TaskQueueViewModel.TaskItemViewModels.ToList()
                .FindAll(task => (task.IsChecked || task.IsCheckedWithNull == null) && task.IsResourceSupported && !task.IsResourceOptionItem);
            StartTask(tasks, onlyStart, checkUpdate);
        }
    }

    public void Start(List<DragItemViewModel> dragItemViewModels, bool onlyStart = false, bool checkUpdate = false)
    {
        // 保存当前的任务列表，以便在重新加载时保留用户调整的顺序和 check 状态
        var currentTasks = new Collection<DragItemViewModel>(Instances.TaskQueueViewModel.TaskItemViewModels.ToList());

        if (InitializeData(currentTasks))
        {
            var tasks = dragItemViewModels;
            StartTask(tasks, onlyStart, checkUpdate);
        }
    }
    public CancellationTokenSource? CancellationTokenSource
    {
        get;
        private set;
    } = new();
    private DateTime? _startTime;
    private List<DragItemViewModel> _tempTasks = [];

    public async Task StartTask(List<DragItemViewModel>? tasks, bool onlyStart = false, bool checkUpdate = false)
    {
        Status = MFATask.MFATaskStatus.NOT_STARTED;
        CancellationTokenSource = new CancellationTokenSource();

        _startTime = DateTime.Now;

        var token = CancellationTokenSource.Token;

        if (!onlyStart)
        {
            tasks ??= new List<DragItemViewModel>();
            _tempTasks = tasks;
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

    private void UpdateTaskDictionary(ref MaaToken taskModels,
        List<MaaInterface.MaaInterfaceSelectOption>? options,
        List<MaaInterface.MaaInterfaceSelectAdvanced>? advanceds)
    {
        // Instance.NodeDictionary = Instance.NodeDictionary.MergeMaaNodes(taskModels);
        if (options != null)
        {
            ProcessOptions(ref taskModels, options);
        }

        if (advanceds != null)
        {
            foreach (var selectAdvanced in advanceds)
            {
                if (!string.IsNullOrWhiteSpace(selectAdvanced.PipelineOverride) && selectAdvanced.PipelineOverride != "{}")
                {
                    var param = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(selectAdvanced.PipelineOverride);
                    //       Instance.NodeDictionary = Instance.NodeDictionary.MergeMaaNodes(param);
                    taskModels.Merge(param);
                }
            }
        }
    }

    /// <summary>
    /// 处理 option 列表（支持 select/switch/input 类型及子配置项）
    /// </summary>
    /// <param name="taskModels">任务参数</param>
    /// <param name="allOptions">任务的所有 option 列表</param>
    /// <param name="optionNamesToProcess">要处理的 option 名称列表（null 表示处理所有直接引用的 options）</param>
    /// <param name="processedOptions">已处理的 option 名称（避免重复处理）</param>
    private void ProcessOptions(
        ref MaaToken taskModels,
        List<MaaInterface.MaaInterfaceSelectOption> allOptions,
        List<string>? optionNamesToProcess = null,
        HashSet<string>? processedOptions = null)
    {
        processedOptions ??= new HashSet<string>();

        // 确定要处理的 options
        IEnumerable<MaaInterface.MaaInterfaceSelectOption> optionsToProcess;

        if (optionNamesToProcess == null)
        {
            // 根级调用：处理所有 options（按顺序）
            optionsToProcess = allOptions;
        }
        else
        {
            // 递归调用：只处理指定名称的 options
            optionsToProcess = allOptions.Where(o => optionNamesToProcess.Contains(o.Name ?? string.Empty));
        }

        foreach (var selectOption in optionsToProcess)
        {
            var optionName = selectOption.Name ?? string.Empty;

            // 避免重复处理同一个 option
            if (processedOptions.Contains(optionName))
                continue;

            processedOptions.Add(optionName);

            if (Interface?.Option?.TryGetValue(optionName, out var interfaceOption) != true)
                continue;

            // 处理 input 类型
            if (interfaceOption.IsInput)
            {
                // 从 Data 重新生成 PipelineOverride（因为 PipelineOverride 是 JsonIgnore 的）
                string? pipelineOverride = selectOption.PipelineOverride;

                if ((string.IsNullOrWhiteSpace(pipelineOverride) || pipelineOverride == "{}")
                    && selectOption.Data != null
                    && interfaceOption.PipelineOverride != null)
                {
                    // 从 Data 重新生成
                    pipelineOverride = interfaceOption.GenerateProcessedPipeline(
                        selectOption.Data.Where(kv => kv.Value != null)
                            .ToDictionary(kv => kv.Key, kv => kv.Value!));
                }

                if (!string.IsNullOrWhiteSpace(pipelineOverride) && pipelineOverride != "{}")
                {
                    var param = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(pipelineOverride);
                    taskModels.Merge(param);
                }
            }
            // 处理 select/switch 类型
            else if (selectOption.Index is int index
                     && interfaceOption.Cases is { } cases
                     && index >= 0
                     && index < cases.Count)
            {
                var selectedCase = cases[index];

                // 合并当前 case 的 pipeline_override
                if (selectedCase.PipelineOverride != null)
                {
                    taskModels.Merge(selectedCase.PipelineOverride);
                }

                // 只递归处理被选中 case 的子配置项（且未被处理过的）
                if (selectedCase.Option != null && selectedCase.Option.Count > 0)
                {
                    // 过滤掉已处理的
                    var unprocessedSubOptionNames = selectedCase.Option
                        .Where(name => !processedOptions.Contains(name))
                        .ToList();

                    if (unprocessedSubOptionNames.Count > 0 && selectOption.SubOptions != null)
                    {
                        // 从 selectOption.SubOptions 中获取子选项（已保存的用户选择值）
                        var subOptionsToProcess = selectOption.SubOptions
                            .Where(s => unprocessedSubOptionNames.Contains(s.Name ?? string.Empty))
                            .ToList();

                        ProcessOptions(ref taskModels, subOptionsToProcess, unprocessedSubOptionNames, processedOptions);
                    }
                    else if (unprocessedSubOptionNames.Count > 0)
                    {
                        // 如果没有 SubOptions，使用默认值处理
                        ProcessOptions(ref taskModels, allOptions, unprocessedSubOptionNames, processedOptions);
                    }
                }
            }
        }
    }

    private string SerializeTaskParams(MaaToken taskModels)
    {
        // var settings = new JsonSerializerSettings
        // {
        //     Formatting = Formatting.Indented,
        //     NullValueHandling = NullValueHandling.Ignore,
        //     DefaultValueHandling = DefaultValueHandling.Ignore
        // };

        try
        {
            return taskModels.ToString();
            //     return JsonConvert.SerializeObject(taskModels.Tokens, settings);
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
        })).ToMaaToken();

        // 首先合并当前资源的全局选项参数
        MergeResourceOptionParams(ref taskModels);

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

        return new NodeAndParam
        {
            Name = task.InterfaceItem?.DisplayName,
            Entry = task.InterfaceItem?.Entry,
            Count = task.InterfaceItem?.Repeatable == true ? (task.InterfaceItem?.RepeatCount ?? 1) : 1,
            // Tasks = tasks,
            Param = taskParams
        };
    }

    /// <summary>
    /// 合并当前资源的全局选项参数到任务参数中
    /// </summary>
    private void MergeResourceOptionParams(ref MaaToken taskModels)
    {
        // 获取当前资源
        var currentResourceName = Instances.TaskQueueViewModel.CurrentResource;
        var currentResource = Instances.TaskQueueViewModel.CurrentResources
            .FirstOrDefault(r => r.Name == currentResourceName);

        if (currentResource?.SelectOptions == null || currentResource.SelectOptions.Count == 0)
            return;

        // 查找任务列表中的资源设置项，获取用户选择的值
        var resourceOptionItem = Instances.TaskQueueViewModel.TaskItemViewModels
            .FirstOrDefault(t => t.IsResourceOptionItem && t.ResourceItem?.Name == currentResourceName);

        var selectOptions = resourceOptionItem?.ResourceItem?.SelectOptions ?? currentResource.SelectOptions;

        // 处理资源的全局选项
        ProcessOptions(ref taskModels, selectOptions);
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
        if (Instances.TaskQueueViewModel.CurrentDevice == null && Instances.ConnectSettingsUserControlModel.AutoDetectOnConnectionFailed)
            Instances.TaskQueueViewModel.TryReadAdbDeviceFromConfig(false, true);
        var tuple = await TryConnectAsync(token);
        var connected = tuple.Item1;
        var shouldRetry = tuple.Item3; // 获取是否应该重试的标志

        // 只有在应该重试的情况下才进行重连
        if (!connected && isAdb && !tuple.Item2 && shouldRetry)
        {
            connected = await HandleAdbConnectionAsync(token, showMessage);
        }

        if (!connected)
        {
            if (!tuple.Item2 && shouldRetry)
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
                () =>
                {
                    if (Instances.ConnectSettingsUserControlModel.AutoDetectOnConnectionFailed) Instances.TaskQueueViewModel.TryReadAdbDeviceFromConfig(false, true);
                }),
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
        // 如果不应该重试（Agent启动失败或资源加载失败），直接返回 false
        if (!tuple.Item3)
        {
            return false;
        }
        return tuple.Item1;
    }

    async private Task<(bool, bool, bool)> TryConnectAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var tuple = await GetTaskerAndBoolAsync(token);
        return (tuple.Item1 is { IsInitialized: true }, tuple.Item2, tuple.Item3);
    }

    private void HandleConnectionFailureAsync(bool isAdb, CancellationToken token)
    {
        // 如果 token 已取消，不需要再调用 Stop，因为已经在其他地方处理了
        if (token.IsCancellationRequested)
        {
            LoggerHelper.Info("HandleConnectionFailureAsync: token is already canceled, skipping Stop call");
            return;
        }
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
        await ScriptRunner.RunScriptAsync(str);
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

    private Lock stop = new Lock();

    public void Stop(MFATask.MFATaskStatus status, bool finished = false, bool onlyStart = false, Action? action = null)
    {
        // 在后台线程执行停止操作，避免阻塞 UI 线程
        TaskManager.RunTask(() =>
        {
            lock (stop)
            {
                LoggerHelper.Info("Stop Status: " + Status);
                if (Status == MFATask.MFATaskStatus.STOPPING)
                    return;
                Status = MFATask.MFATaskStatus.STOPPING;
                DispatcherHelper.PostOnMainThread(() => Instances.TaskQueueViewModel.ToggleEnable = false);
                try
                {
                    var isUpdateRelated = TaskQueue.Any(task => task.IsUpdateRelated);
                    Instances.TaskQueueViewModel.SetCurrentTaskName(string.Empty);
                    if (!ShouldProcessStop(finished))
                    {
                        ToastHelper.Warn(LangKeys.NoTaskToStop.ToLocalization());

                        TaskQueue.Clear();
                        return;
                    }

                    CancelOperations(status == MFATask.MFATaskStatus.STOPPED && !_agentStarted && (_agentClient != null || _agentProcess != null));

                    TaskQueue.Clear();

                    DispatcherHelper.PostOnMainThread(() => Instances.RootViewModel.IsRunning = false);

                    ExecuteStopCore(finished, async () =>
                    {
                        var stopResult = MaaJobStatus.Succeeded;

                        if (MaaTasker is { IsRunning: true, IsStopping: false } && status != MFATask.MFATaskStatus.FAILED && status != MFATask.MFATaskStatus.SUCCEEDED)
                        {

                            // 持续尝试停止直到返回 Succeeded
                            const int maxRetries = 10;
                            const int retryDelayMs = 500;

                            for (int i = 0; i < maxRetries; i++)
                            {
                                LoggerHelper.Info($"Stopping tasker attempt {i + 1}");
                                stopResult = AbortCurrentTasker();
                                LoggerHelper.Info($"Stopping tasker attempt {i + 1} returned {stopResult}, retrying...");

                                if (stopResult == MaaJobStatus.Succeeded)
                                    break;

                                await Task.Delay(retryDelayMs);
                            }

                        }

                        HandleStopResult(status, stopResult, onlyStart, action, isUpdateRelated);
                        DispatcherHelper.PostOnMainThread(() => Instances.TaskQueueViewModel.ToggleEnable = true);
                    });
                }
                catch (Exception ex)
                {
                    DispatcherHelper.PostOnMainThread(() => Instances.TaskQueueViewModel.ToggleEnable = true);
                    HandleStopException(ex);
                }
            }
        }, "停止任务");
    }


    private void CancelOperations(bool killAgent = false)
    {
        _emulatorCancellationTokenSource?.SafeCancel();
        CancellationTokenSource.SafeCancel();
        if (killAgent)
        {
            SafeKillAgentProcess();
        }
    }

    [SupportedOSPlatform("windows")]
    private static void KillProcessTree(int parentPid)
    {
        using var searcher = new ManagementObjectSearcher(
            $"SELECT ProcessId FROM Win32_Process WHERE ParentProcessId = {parentPid}");

        foreach (var item in searcher.Get())
        {
            var childPid = Convert.ToInt32(item["ProcessId"]);
            KillProcessTree(childPid); // 递归终止子进程的子进程

            try
            {
                var childProcess = Process.GetProcessById(childPid);
                if (!childProcess.HasExited)
                {
                    childProcess.Kill();
                    childProcess.WaitForExit(3000);
                }
                childProcess.Dispose();
            }
            catch (ArgumentException) { } // 进程已退出
        }
    }

    /// 强制终止 Agent 进程（用于窗口关闭等紧急情况）
    /// </summary>
    /// <param name="taskerToDispose">原tasker</param>
    private void SafeKillAgentProcess(MaaTasker? taskerToDispose = null)
    {
        // 获取当前引用的本地副本，避免在检查和使用之间被其他线程修改
        var agentClient = _agentClient;
        var agentProcess = _agentProcess;
        // 如果传入了 taskerToDispose，使用它；否则使用当前的 MaaTasker
        var maaTasker = taskerToDispose ?? MaaTasker;

        // 先清除引用，防止在后续操作中被其他线程访问
        _agentClient = null;
        _agentProcess = null;

        // 重要：必须按照正确的顺序释放资源，避免原生代码访问冲突
        // 步骤 1: 先解除 AgentClient 与资源的绑定（在Dispose MaaTasker 之前）
        // 这样 MaaTasker.Dispose() 就不会触发 MaaAgentClient.OnResourceReleasing 事件
        if (agentClient != null)
        {
            // 停止 AgentClient 连接
            LoggerHelper.Info($"Stopping AgentClient connection");
            try
            {
                bool shouldStop = false;
                try
                {
                    shouldStop = !agentClient.IsStateless && !agentClient.IsInvalid;
                }
                catch (ObjectDisposedException)
                {
                    // 对象已被释放，跳过
                }

                if (shouldStop)
                {
                    try
                    {
                        agentClient.LinkStop();
                        LoggerHelper.Info("AgentClient LinkStop succeeded");
                    }
                    catch (Exception e)
                    {
                        LoggerHelper.Warning($"AgentClient LinkStop failed: {e.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                LoggerHelper.Warning($"AgentClient LinkStop check failed: {e.Message}");
            }
        }

        // 步骤 2: 终止 Agent 进程（在释放 MaaTasker 之前）
        if (agentProcess != null)
        {
            LoggerHelper.Info($"Terminating Agent process");
            try
            {
                var hasExited = true;
                try
                {
                    hasExited = agentProcess.HasExited;
                }
                catch (InvalidOperationException)
                {
                    hasExited = true;
                }
                catch (Exception ex)
                {
                    LoggerHelper.Warning($"Failed to check if agent process has exited: {ex.Message}");
                    hasExited = true;
                }

                if (!hasExited)
                {
                    try
                    {
                        LoggerHelper.Info($"Kill AgentProcess: {agentProcess.ProcessName}");
                        agentProcess.Kill(true);
                        agentProcess.WaitForExit(5000);
                        LoggerHelper.Info("Agent process killed successfully");
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.Warning($"Failed to kill agent process: {ex.Message}");
                    }
                }
                else
                {
                    LoggerHelper.Info("AgentProcess has already exited");
                }
            }
            catch (Exception e)
            {
                LoggerHelper.Error($"Error handling agent process: {e.Message}");
            }
            finally
            {
                try
                {
                    agentProcess.Dispose();
                }
                catch (Exception e)
                {
                    LoggerHelper.Warning($"AgentProcess Dispose failed: {e.Message}");
                }
            }
        }
        // 步骤 3: 停止并释放 MaaTasker（由于已经解除了 AgentClient 的绑定，不会触发 AgentClient 释放）
        if (maaTasker != null)
        {
            // 先停止 MaaTasker，等待内部任务完成，避免在任务执行过程中直接 Dispose 导致 handle is null 错误
            if (maaTasker.IsRunning && !maaTasker.IsStopping)
            {
                LoggerHelper.Info($"Stopping MaaTasker before dispose");
                try
                {

                    var stopResult = maaTasker.Stop().Wait();
                    LoggerHelper.Info($"MaaTasker Stop result: {stopResult}");
                }
                catch (ObjectDisposedException)
                {
                    LoggerHelper.Info("MaaTasker was already disposed during Stop");
                }
                catch (Exception e)
                {
                    LoggerHelper.Warning($"MaaTasker Stop failed: {e.Message}");
                }
            }

            LoggerHelper.Info($"Disposing MaaTasker");
            try
            {
                maaTasker.Dispose();
                LoggerHelper.Info("MaaTasker disposed successfully");
            }
            catch (ObjectDisposedException)
            {
                LoggerHelper.Info("MaaTasker was already disposed");
            }
            catch (Exception e)
            {
                LoggerHelper.Warning($"MaaTasker Dispose failed: {e.Message}");
            }
        }
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
            if (!finished) DispatcherHelper.PostOnMainThread(() => RootView.AddLogByKey(LangKeys.Stopping));

            stopAction.Invoke();

            DispatcherHelper.PostOnMainThread(() => Instances.RootViewModel.Idle = true);
        }, null, "停止maafw任务");
    }

    private MaaJobStatus AbortCurrentTasker()
    {
        if (MaaTasker == null)
            return MaaJobStatus.Succeeded;
        var status = MaaTasker.Stop().Wait();

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
        _tempTasks = [];
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
            TaskManager.RunTask(() =>
            {
                Task.Delay(400).ContinueWith(_ =>
                {
                    ToastHelper.Info(LangKeys.TaskStopped.ToLocalization());
                    RootView.AddLogByKey(LangKeys.TaskAbandoned);
                });
            });
        }
        else
        {
            if (!onlyStart)
            {
                var list = _tempTasks.Count > 0 ? _tempTasks : Instances.TaskQueueViewModel.TaskItemViewModels.ToList();
                list.Where(t => t.IsCheckedWithNull == null && !t.IsResourceSupported).ToList().ForEach(d => d.IsCheckedWithNull = false);

                if (_startTime != null)
                {
                    var elapsedTime = DateTime.Now - (DateTime)_startTime;
                    ToastNotification.Show(LangKeys.TaskCompleted.ToLocalization(), LangKeys.TaskAllCompletedWithTime.ToLocalizationFormatted(false, ((int)elapsedTime.TotalHours).ToString(),
                        ((int)elapsedTime.TotalMinutes % 60).ToString(), ((int)elapsedTime.TotalSeconds % 60).ToString()));
                }
                else
                {
                    ToastNotification.Show(LangKeys.TaskCompleted.ToLocalization());
                }
            }

            if (_startTime != null)
            {
                var elapsedTime = DateTime.Now - (DateTime)_startTime;
                RootView.AddLogByKeys(LangKeys.TaskAllCompletedWithTime, null, true, ((int)elapsedTime.TotalHours).ToString(),
                    ((int)elapsedTime.TotalMinutes % 60).ToString(), ((int)elapsedTime.TotalSeconds % 60).ToString());
                
                // 如果执行时间大于2分钟，触发抽卡
                if (elapsedTime.TotalMinutes > 2)
                {
                    try
                    {
                        LoggerHelper.Info($"任务执行时间 {elapsedTime.TotalMinutes:F1} 分钟，触发自动抽卡");
                        TaskManager.RunTaskAsync(async () => await CCMgr.Instance.PullOne(), null, "自动抽卡");
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.Error($"自动抽卡失败: {ex.Message}");
                    }
                }
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
                Instances.ShutdownSystem();
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
                ProcessHelper.CloseProcessesByName(Config.DesktopWindow.Name, ConfigurationManager.Current.GetValue(ConfigurationKeys.EmulatorConfig, string.Empty));
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

    #region 自定义识别和动作注册

    /// <summary>
    /// 注册自定义识别器和动作
    /// </summary>
    /// <param name="tasker">MaaTasker 实例</param>
    private void RegisterCustomRecognitionsAndActions(MaaTasker tasker)
    {
        if (Interface == null) return;

        try
        {

            // 获取当前资源的自定义目录
            var currentResource = Instances.TaskQueueViewModel.CurrentResources
                .FirstOrDefault(c => c.Name == Instances.TaskQueueViewModel.CurrentResource);
            var originalPaths = currentResource?.ResolvedPath ?? currentResource?.Path;

            if (originalPaths == null || originalPaths.Count == 0)
            {
                LoggerHelper.Info("No resource paths found, skipping custom class loading");
                return;
            }

            // 创建副本，避免修改原始列表
            var resourcePaths = new List<string>(originalPaths);
            // LoggerHelper.Info(LangKeys.RegisteringCustomRecognizer.ToLocalization());
            // LoggerHelper.Info(LangKeys.RegisteringCustomAction.ToLocalization());
            resourcePaths.Add(Path.Combine(AppContext.BaseDirectory, "resource"));
            // 遍历所有资源路径，查找 custom 目录
            foreach (var resourcePath in resourcePaths)
            {
                var customDir = Path.Combine(resourcePath, "custom");
                if (!Directory.Exists(customDir))
                {
                    LoggerHelper.Info($"Custom directory not found: {customDir}");
                    continue;
                }

                var customClasses = CustomClassLoader.GetCustomClasses(customDir, new[]
                {
                    nameof(IMaaCustomRecognition),
                    nameof(IMaaCustomAction)
                });

                foreach (var customClass in customClasses)
                {
                    try
                    {
                        if (customClass.Value is IMaaCustomRecognition recognition)
                        {
                            tasker.Resource.Register(recognition);
                            LoggerHelper.Info($"Registered IMaaCustomRecognition: {customClass.Name}");
                        }
                        else if (customClass.Value is IMaaCustomAction action)
                        {
                            tasker.Resource.Register(action);
                            LoggerHelper.Info($"Registered IMaaCustomAction: {customClass.Name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.Error($"Failed to register custom class {customClass.Name}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"Error during custom recognition/action registration: {ex.Message}");
        }
    }

    #endregion
}
