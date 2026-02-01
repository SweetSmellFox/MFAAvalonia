using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using MFAAvalonia;
using Avalonia.Media;
using Avalonia.Threading;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.ViewModels.Windows;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.MessageBox;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.Views.Windows;

public partial class RootView : SukiWindow
{
    public RootView()
    {
        // 添加初始化标志
        _isInitializing = true;

        // 先从配置中加载窗口大小和位置，在窗口显示前设置
        LoadWindowSizeAndPosition();

        // 初始化组件
        InitializeComponent();

        // 设置事件处理
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                HandleWindowStateChange();
            }
        };

        // 为窗口大小变化添加监听，保存窗口大小
        SizeChanged += SaveWindowSizeOnChange;
        // 为窗口位置变化添加监听，保存窗口位置
        PositionChanged += SaveWindowPositionOnChange;

        // 修改Loaded事件处理
        Loaded += (_, _) =>
        {
            LoggerHelper.Info("UI initialization started");

            // 确保在UI线程上执行
            DispatcherHelper.PostOnMainThread(() =>
            {
                // 初始化完成
                _isInitializing = false;

                // 加载UI
                LoadUI();
            });
        };
        if (AppRuntime.IsNewInstance)
        {
            MaaProcessorManager.Instance.LoadInstanceConfig();
            foreach (var instance in MaaProcessorManager.Instance.Instances)
            {
                instance.InitializeData();
            }
        }
    }


    private bool _isInitializing = true;
    private bool _hasValidPosition = false;
    // 缓存最后一个有效的窗口位置和大小
    private PixelPoint _lastValidPosition;
    private double _lastValidWidth;
    private double _lastValidHeight;

    // 防抖机制：避免频繁保存窗口位置导致内存泄漏
    private CancellationTokenSource? _saveWindowPositionCts;
    private bool _pendingSave = false;
    private const int SaveDebounceDelayMs = 500; // 500ms 防抖延迟

    private void HandleWindowStateChange()
    {
        if (ConfigurationManager.Current.GetValue(ConfigurationKeys.ShouldMinimizeToTray, false))
        {
            Instances.RootViewModel.IsWindowVisible = WindowState != WindowState.Minimized;
        }
    }

    public void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

#pragma warning disable CS4014 // 由于此调用不会等待，因此在此调用完成之前将会继续执行当前方法。请考虑将 "await" 运算符应用于调用结果。
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (Instances.RootViewModel.IsRunning)
        {
            e.Cancel = true;
            ConfirmExit(() => OnClosed(e));
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        BeforeClosed();
        base.OnClosed(e);
    }

    public void BeforeClosed(bool noLog, bool stopTask)
    {
        if (!GlobalHotkeyService.IsStopped)
        {
            if (Instances.RootViewModel.IsRunning)
            {
                if (stopTask)
                {
                    foreach (var processor in MaaProcessor.Processors)
                    {
                        processor.Stop(MFATask.MFATaskStatus.STOPPED);
                    }
                }
                else
                    DispatcherHelper.PostOnMainThread(() => Instances.RootViewModel.IsRunning = false);
            }
            // Save current instance tasks
            var currentVM = Instances.InstanceTabBarViewModel.ActiveTab?.TaskQueueViewModel;
            if (currentVM != null)
            {
                ConfigurationManager.CurrentInstance.SetValue(ConfigurationKeys.TaskItems,
                    currentVM.TaskItemViewModels.ToList().Select(model => model.InterfaceItem));
            }

            // 确保窗口大小和位置被立即保存（绕过防抖机制）
            foreach (var processor in MaaProcessor.Processors.ToList())
            {
                processor.Dispose();
            }
            DispatcherHelper.PostOnMainThread(SaveWindowSizeAndPositionImmediately);
            if (!noLog)
                LoggerHelper.Info("MFA Closed!");
            TrayIconManager.DisposeTrayIcon(Application.Current);
            // Instances.TaskQueueViewModel.Processor.SetTasker(); // SetTasker on disposed/stopping processor? Maybe not needed or should be loop?
            // Assuming this was to clean up or reset. Dispose should be enough.
            foreach(var processor in MaaProcessor.Processors) processor.SetTasker();

            CustomClassLoader.Dispose();

            if (!noLog)
                LoggerHelper.DisposeLogger();
            GlobalHotkeyService.Shutdown();
            AppRuntime.ReleaseMutex();
        }
    }

    public void BeforeClosed()
    {
        BeforeClosed(false, true);
    }

    public async Task<bool> ConfirmExit(Action? action = null)
    {
        if (!Instances.RootViewModel.IsRunning)
            return true;

        var result = await SukiMessageBox.ShowDialog(new SukiMessageBoxHost
        {
            Content = LangKeys.ConfirmExitText.ToLocalization(),
            ActionButtonsPreset = SukiMessageBoxButtons.YesNo,
            IconPreset = SukiMessageBoxIcons.Warning,
        }, new SukiMessageBoxOptions()
        {
            Title = LangKeys.ConfirmExitTitle.ToLocalization(),
        });

        if (result is SukiMessageBoxResult.Yes)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception e)
            {
                LoggerHelper.Error(e);
            }
            finally { Instances.ShutdownApplication(); }

            return true;
        }
        return false;
    }


#pragma warning  disable CS4014 // 由于此调用不会等待，因此在此调用完成之前将会继续执行当前方法。请考虑将 "await" 运算符应用于调用结果。
    public void LoadUI()
    {
        if (AppRuntime.IsNewInstance)
        {
            foreach (var rfile in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.backupMFA", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(rfile, FileAttributes.Normal);
                    LoggerHelper.Info("Deleting file: " + rfile);
                    File.Delete(rfile);
                }
                catch (Exception ex)
                {
                    LoggerHelper.Error($"文件删除失败: {rfile}", ex);
                }
            }

            var vm = Instances.InstanceTabBarViewModel.ActiveTab?.TaskQueueViewModel;
            if (vm == null) return;

            if (!vm.Processor.IsV3)
            {
                DispatcherHelper.RunOnMainThread(
                    (Action)(async () =>
                    {
                        await Task.Delay(300);
                        if ((MaaProcessor.Interface?.Controller?.Count ?? 0) == 1 || !ConfigurationManager.CurrentInstance.ContainsKey(ConfigurationKeys.CurrentController))
                            vm.CurrentController = (MaaProcessor.Interface?.Controller?.FirstOrDefault()?.Type).ToMaaControllerTypes(vm.CurrentController);
                        var beforeTask = ConfigurationManager.CurrentInstance.GetValue(ConfigurationKeys.BeforeTask, "None");
                        var startupScriptOnly = beforeTask.Equals("StartupScriptOnly", StringComparison.OrdinalIgnoreCase);
                        var delayFingerprintMatching = beforeTask.Contains("StartupSoftware", StringComparison.OrdinalIgnoreCase);
                        if (!Convert.ToBoolean(GlobalConfiguration.GetValue(ConfigurationKeys.NoAutoStart, bool.FalseString))
                            && (beforeTask.Contains("Startup", StringComparison.OrdinalIgnoreCase) || startupScriptOnly))
                        {
                            // 只有当不是 StartupScriptOnly 时才启动游戏
                            if (!startupScriptOnly)
                            {
                                vm.Processor.TaskQueue.Enqueue(new MFATask
                                {
                                    Name = "启动前",
                                    Type = MFATask.MFATaskType.MFA,
                                    Action = async () => await vm.Processor.WaitSoftware(),
                                });
                            }
                            // StartupScriptOnly 或 StartupSoftwareAndScript 时启动脚本 (onlyStart = false)
                            // StartupSoftware 时只启动游戏不启动脚本 (onlyStart = true)
                            var controllerType = vm.CurrentController;
                            if (controllerType == MaaControllerTypes.PlayCover)
                            {
                                vm.TryReadPlayCoverConfig();
                            }
                            else if (ConfigurationManager.CurrentInstance.GetValue(ConfigurationKeys.RememberAdb, true) && !delayFingerprintMatching)
                            {
                                vm.TryReadAdbDeviceFromConfig(false, false);
                            }
                            var onlyStart = beforeTask.Equals("StartupSoftware", StringComparison.OrdinalIgnoreCase);
                            vm.Processor.Start(onlyStart, checkUpdate: true);
                        }
                    }));
            }
            else
            {
                var controllerType = vm.CurrentController;
                var controllerKey = controllerType switch
                {
                    MaaControllerTypes.Adb => "Emulator",
                    MaaControllerTypes.Win32 => "Window",
                    MaaControllerTypes.PlayCover => "TabPlayCover",
                    _ => "Window"
                };

                vm.AddLogByKey("ConnectingTo", (IBrush?)null, true, true, controllerKey);

                if (controllerType == MaaControllerTypes.PlayCover)
                {
                    vm.TryReadPlayCoverConfig();
                }
                else
                {
                    vm.TryReadAdbDeviceFromConfig();
                }

                vm.Processor.TaskQueue.Enqueue(new MFATask
                {
                    Name = "连接检测",
                    Type = MFATask.MFATaskType.MFA,
                    Action = async () => await vm.Processor.TestConnecting(),
                });
                vm.Processor.Start(true, checkUpdate: true);
            }

            GlobalConfiguration.SetValue(ConfigurationKeys.NoAutoStart, bool.FalseString);

            // 重新初始化控制器选项，确保 ControllerOptions 包含正确的控制器列表
            // 因为 TaskQueueViewModel.Initialize() 可能在 MaaProcessor.Interface初始化之前被调用
            vm.InitializeControllerOptions();

            // 只有当 SelectedController 不为 null 时才锁定控制器
            // Instances.RootViewModel.LockController = (MaaProcessor.Interface?.Controller?.Count ?? 0) == 1
            //     && Instances.TaskQueueViewModel.SelectedController != null;

            ConfigurationManager.Current.SetValue(ConfigurationKeys.EnableEdit, ConfigurationManager.Current.GetValue(ConfigurationKeys.EnableEdit, false));
            DragItemViewModel? tempTask = null;
            foreach (var task in vm.TaskItemViewModels)
            {
                // 优先选择资源选项项
                if (task.IsResourceOptionItem && task.ResourceItem?.SelectOptions is { Count: > 0 })
                {
                    tempTask ??= task;
                }
                else if (task.InterfaceItem?.Advanced is { Count: > 0 }
                         || task.InterfaceItem?.Option is { Count: > 0 }
                         || !string.IsNullOrWhiteSpace(task.InterfaceItem?.Description)
                         || task.InterfaceItem?.Document != null
                         || task.InterfaceItem?.Repeatable == true)
                {
                    // 如果还没有找到资源选项项，则选择第一个有配置的普通任务
                    tempTask ??= task;
                }
            }

            // 只对最终选中的任务设置 EnableSetting = true，这会触发面板显示
            if (tempTask != null)
                tempTask.EnableSetting = true;

            if (!string.IsNullOrWhiteSpace(MaaProcessor.Interface?.Message))
            {
                ToastHelper.Info(MaaProcessor.Interface.Message);
            }

            if (!string.IsNullOrWhiteSpace(MaaProcessor.Interface?.Welcome))
            {
                AnnouncementViewModel.AddAnnouncement(LanguageHelper.GetLocalizedString(MaaProcessor.Interface.Welcome));
            }


            TaskManager.RunTaskAsync(async () =>
            {
                await Task.Delay(1000);
                DispatcherHelper.RunOnMainThread(() =>
                {
                    VersionChecker.CheckMinVersion();
                    if (ConfigurationManager.Current.GetValue(ConfigurationKeys.AutoMinimize, false))
                    {
                        WindowState = WindowState.Minimized;
                    }
                    if (ConfigurationManager.Current.GetValue(ConfigurationKeys.AutoHide, false))
                    {
                        Hide();
                    }
                });

                await Task.Delay(300);
                await AnnouncementViewModel.CheckAnnouncement();
            }, name: "公告和最新版本检测");
        }
        else
        {
            DispatcherHelper.RunOnMainThread(async () =>
            {
                await Task.Delay(1000);
                Instances.DialogManager.CreateDialog().OfType(NotificationType.Error).WithContent(LangKeys.UiDoesNotSupportCurrentResource.ToLocalization())
                    .WithActionButton(LangKeys.Ok.ToLocalization(), _ => { Instances.ShutdownApplication(); }, true).TryShow();
            });
        }
    }


    public void ClearTasks(Action? action = null)
    {
        DispatcherHelper.RunOnMainThread(() =>
        {
            var vm = Instances.InstanceTabBarViewModel.ActiveTab?.TaskQueueViewModel;
            if (vm != null)
            {
                vm.TaskItemViewModels = new();
            }
            action?.Invoke();
        });
    }

    /// <summary>
    /// 加载窗口大小和位置
    /// </summary>
    private void LoadWindowSizeAndPosition()
    {
        try
        {
            // 加载窗口大小
            var widthStr = ConfigurationManager.Current.GetValue(ConfigurationKeys.MainWindowWidth, "");
            var heightStr = ConfigurationManager.Current.GetValue(ConfigurationKeys.MainWindowHeight, "");

            if (!string.IsNullOrEmpty(widthStr) && !string.IsNullOrEmpty(heightStr))
            {
                if (double.TryParse(widthStr, out double width) && double.TryParse(heightStr, out double height))
                {
                    if (width > 100 && height > 100) // 确保有效的窗口大小
                    {
                        Width = width;
                        Height = height;
                        _lastValidWidth = width;
                        _lastValidHeight = height;
                        LoggerHelper.Info($"窗口大小已加载: width={width}, height={height}");
                    }
                }
            }


            // 加载窗口位置
            var posXStr = ConfigurationManager.Current.GetValue(ConfigurationKeys.MainWindowPositionX, "");
            var posYStr = ConfigurationManager.Current.GetValue(ConfigurationKeys.MainWindowPositionY, "");

            if (!string.IsNullOrEmpty(posXStr) && !string.IsNullOrEmpty(posYStr))
            {
                if (int.TryParse(posXStr, out int posX) && int.TryParse(posYStr, out int posY))
                {
                    // 验证位置是否在屏幕范围内
                    if (IsPositionValid(posX, posY))
                    {
                        Position = new PixelPoint(posX, posY);
                        WindowStartupLocation = WindowStartupLocation.Manual;
                        _hasValidPosition = true; // 标记已有有效位置
                        _lastValidPosition = new PixelPoint(posX, posY); // 缓存位置
                        LoggerHelper.Info($"窗口位置已加载: X={posX}, Y={posY}");
                    }
                    else
                    {
                        LoggerHelper.Info($"保存的窗口位置 ({posX}, {posY}) 不在有效屏幕范围内，使用默认居中位置");
                    }
                }
            }

            // 加载窗口最大化状态
            var maximizedStr = ConfigurationManager.Current.GetValue(ConfigurationKeys.MainWindowMaximized, "false");
            if (bool.TryParse(maximizedStr, out bool isMaximized) && isMaximized)
            {
                WindowState = WindowState.Maximized;
                LoggerHelper.Info("窗口将以最大化状态启动");
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"加载窗口大小和位置失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 验证窗口位置是否在有效屏幕范围内
    /// </summary>
    private bool IsPositionValid(int x, int y)
    {
        try
        {
            var screens = Screens;
            if (screens?.All == null || screens.All.Count == 0)
                return true; // 如果无法获取屏幕信息，默认认为有效

            // 检查位置是否在任意屏幕范围内（允许一定的边界容差）
            const int margin = 50; // 至少要有50像素在屏幕内
            foreach (var screen in screens.All)
            {
                var bounds = screen.Bounds;
                if (x >= bounds.X - (Width - margin) && x <= bounds.X + bounds.Width - margin && y >= bounds.Y - (Height - margin) && y <= bounds.Y + bounds.Height - margin)
                {
                    return true;
                }
            }
            return false;
        }
        catch
        {
            return true; // 出错时默认认为有效
        }
    }

    private void SaveWindowSizeOnChange(object? sender, SizeChangedEventArgs e)
    {
        // 初始化过程中不保存窗口大小
        if (!_isInitializing && WindowState == WindowState.Normal)
        {
            SaveWindowSizeAndPosition();
        }
    }

    private void SaveWindowPositionOnChange(object? sender, PixelPointEventArgs e)
    {
        // 初始化过程中不保存窗口位置
        if (!_isInitializing && WindowState == WindowState.Normal)
        {
            // 检查位置是否有效（不是 0,0 或者已经确认有效）
            var position = e.Point;
            if (position.X == 0 && position.Y == 0 && !_hasValidPosition)
            {
                // 忽略初始的 (0, 0) 位置
                return;
            }

            // 一旦位置不是 (0, 0)，标记为有效并缓存位置
            if (position.X != 0 || position.Y != 0)
            {
                _hasValidPosition = true;
                _lastValidPosition = position;
            }

            SaveWindowSizeAndPosition();
        }
    }

    /// <summary>
    /// 保存窗口大小和位置（带防抖机制）
    /// </summary>
    public void SaveWindowSizeAndPosition()
    {
        // 初始化过程中不保存
        if (_isInitializing)
        {
            return;
        }

        // 更新缓存的窗口状态（这部分是轻量级操作，可以立即执行）
        UpdateCachedWindowState();

        // 使用防抖机制延迟实际的配置保存
        ScheduleDebouncedSave();
    }

    /// <summary>
    /// 更新缓存的窗口状态（轻量级操作）
    /// </summary>
    private void UpdateCachedWindowState()
    {
        try
        {
            if (WindowState == WindowState.Normal)
            {
                // 缓存窗口大小
                double width = Width;
                double height = Height;
                if (width > 100 && height > 100)
                {
                    _lastValidWidth = width;
                    _lastValidHeight = height;
                }

                // 缓存窗口位置
                var position = Position;
                if (position.X != 0 || position.Y != 0)
                {
                    _hasValidPosition = true;
                    _lastValidPosition = position;
                }
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"更新缓存窗口状态失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 调度防抖保存
    /// </summary>
    private void ScheduleDebouncedSave()
    {
        // 标记有待保存的更改
        _pendingSave = true;

        // 取消之前的延迟保存任务
        _saveWindowPositionCts?.Cancel();
        _saveWindowPositionCts?.Dispose();
        _saveWindowPositionCts = new CancellationTokenSource();

        var token = _saveWindowPositionCts.Token;

        // 启动新的延迟保存任务
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SaveDebounceDelayMs, token);

                // 如果没有被取消，执行实际保存
                if (!token.IsCancellationRequested && _pendingSave)
                {
                    _pendingSave = false;
                    await Dispatcher.UIThread.InvokeAsync(ExecuteActualSave);
                }
            }
            catch (TaskCanceledException)
            {
                // 任务被取消，忽略
            }
            catch (Exception ex)
            {
                LoggerHelper.Error($"防抖保存任务失败: {ex.Message}");
            }
        }, token);
    }

    /// <summary>
    /// 执行实际的配置保存（在防抖延迟后调用）
    /// </summary>
    private void ExecuteActualSave()
    {
        try
        {
            // 保存最大化状态
            bool isMaximized = WindowState == WindowState.Maximized;
            ConfigurationManager.Current.SetValue(ConfigurationKeys.MainWindowMaximized, isMaximized.ToString().ToLower());

            // 保存缓存的窗口大小
            if (_lastValidWidth > 100 && _lastValidHeight > 100)
            {
                ConfigurationManager.Current.SetValue(ConfigurationKeys.MainWindowWidth, _lastValidWidth.ToString());
                ConfigurationManager.Current.SetValue(ConfigurationKeys.MainWindowHeight, _lastValidHeight.ToString());
            }

            // 保存缓存的窗口位置
            if (_hasValidPosition)
            {
                ConfigurationManager.Current.SetValue(ConfigurationKeys.MainWindowPositionX, _lastValidPosition.X.ToString());
                ConfigurationManager.Current.SetValue(ConfigurationKeys.MainWindowPositionY, _lastValidPosition.Y.ToString());
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"保存窗口大小和位置失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 立即保存窗口大小和位置（用于关闭窗口时）
    /// </summary>
    public void SaveWindowSizeAndPositionImmediately()
    {
        // 取消任何待处理的防抖保存
        _saveWindowPositionCts?.Cancel();
        _saveWindowPositionCts?.Dispose();
        _saveWindowPositionCts = null;
        _pendingSave = false;

        // 更新缓存并立即保存
        UpdateCachedWindowState();
        ExecuteActualSave();
    }

    private void ResourceInfo_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Instances.RootViewModel.TempResourceUpdateAction?.Invoke();
    }
}
