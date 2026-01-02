using Avalonia;
using Avalonia.Collections;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.Converters;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.ViewModels.Other;
using MFAAvalonia.ViewModels.UsersControls;
using MFAAvalonia.ViewModels.UsersControls.Settings;
using Newtonsoft.Json;
using SukiUI.Dialogs;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.ViewModels.Pages;

public partial class TaskQueueViewModel : ViewModelBase
{
    [ObservableProperty] private bool _isCompactMode = false;

    // 竖屏模式下的设置弹窗状态
    [ObservableProperty] private bool _isSettingsPopupOpen = false;

    partial void OnIsCompactModeChanged(bool value)
    {
        if (!value && IsSettingsPopupOpen)
            IsSettingsPopupOpen = false;
    }

    [RelayCommand]
    private void CloseSettingsPopup()
    {
        IsSettingsPopupOpen = false;
    }
    /// <summary>
    /// 在竖屏模式下打开设置弹窗
    /// </summary>
    public void OpenSettingsPopup()
    {
        if (IsCompactMode)
        {
            IsSettingsPopupOpen = true;
        }
    }
    /// <summary>
    /// 控制器选项列表
    /// </summary>
    [ObservableProperty] private ObservableCollection<MaaInterface.MaaResourceController> _controllerOptions = [];

    /// <summary>
    /// 当前选中的控制器
    /// </summary>
    [ObservableProperty] private MaaInterface.MaaResourceController? _selectedController;

    partial void OnSelectedControllerChanged(MaaInterface.MaaResourceController? value)
    {
        if (value != null && value.ControllerType != CurrentController)
        {
            CurrentController = value.ControllerType;
        }
    }

    /// <summary>
    /// 初始化控制器列表
    /// 从MaaInterface.Controller加载，如果为空则使用默认的Adb和Win32
    /// </summary>
    public void InitializeControllerOptions()
    {
        try
        {
            var controllers = MaaProcessor.Interface?.Controller;
            if (controllers is { Count: > 0 })
            {
                // 从interface配置中加载控制器列表
                foreach (var controller in controllers)
                {
                    controller.InitializeDisplayName();
                }
                ControllerOptions = new ObservableCollection<MaaInterface.MaaResourceController>(controllers);
            }
            else
            {
                // 使用默认的Adb和Win32控制器
                var defaultControllers = CreateDefaultControllers();
                ControllerOptions = new ObservableCollection<MaaInterface.MaaResourceController>(defaultControllers);
            }

            // 根据当前控制器类型选择对应的控制器
            SelectedController = ControllerOptions.FirstOrDefault(c => c.ControllerType == CurrentController)
                ?? ControllerOptions.FirstOrDefault();
        }
        catch (Exception e)
        {
            LoggerHelper.Error(e);
            // 出错时使用默认控制器
            var defaultControllers = CreateDefaultControllers();
            ControllerOptions = new ObservableCollection<MaaInterface.MaaResourceController>(defaultControllers);
            SelectedController = ControllerOptions.FirstOrDefault();
        }
    }

    /// <summary>
    /// 创建默认的Adb和Win32控制器
    /// </summary>
    private List<MaaInterface.MaaResourceController> CreateDefaultControllers()
    {
        var adbController = new MaaInterface.MaaResourceController
        {
            Name = "Adb",
            Type = MaaControllerTypes.Adb.ToJsonKey()
        };
        adbController.InitializeDisplayName();
        List<MaaInterface.MaaResourceController> controllers = [adbController];
        if (OperatingSystem.IsWindows())
        {
            var win32Controller = new MaaInterface.MaaResourceController
            {
                Name = "Win32",
                Type = MaaControllerTypes.Win32.ToJsonKey()
            };
            win32Controller.InitializeDisplayName();
            controllers.Add(win32Controller);
        }
        if (OperatingSystem.IsMacOS())
        {
            var playCoverController = new MaaInterface.MaaResourceController
            {
                Name = "PlayCover",
                Type = MaaControllerTypes.PlayCover.ToJsonKey()
            };
            playCoverController.InitializeDisplayName();
            controllers.Add(playCoverController);
        }
        return controllers;
    }

    protected override void Initialize()
    {
        try
        {
            InitializeControllerOptions();
        }
        catch (Exception e)
        {
            LoggerHelper.Error(e);
        }
    }


    #region 介绍

    [ObservableProperty] private string _introduction = string.Empty;

    #endregion

    #region 任务

    [ObservableProperty] private bool _isCommon = true;
    [ObservableProperty] private bool _showSettings;
    [ObservableProperty] private bool _toggleEnable = true;

    [ObservableProperty] private ObservableCollection<DragItemViewModel> _taskItemViewModels = [];

    partial void OnTaskItemViewModelsChanged(ObservableCollection<DragItemViewModel> value)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskItems, value.ToList().Select(model => model.InterfaceItem));
    }

    [RelayCommand]
    private void Toggle()
    {
        if (Instances.RootViewModel.IsRunning)
            StopTask();
        else
            StartTask();
    }

    public void StartTask()
    {
        if (Instances.RootViewModel.IsRunning)
        {
            ToastHelper.Warn(LangKeys.ConfirmExitTitle.ToLocalization());
            LoggerHelper.Warning(LangKeys.ConfirmExitTitle.ToLocalization());
            return;
        }

        if (CurrentResources.Count == 0 || string.IsNullOrWhiteSpace(CurrentResource) || CurrentResources.All(r => r.Name != CurrentResource))
        {
            ToastHelper.Warn(LangKeys.CannotStart.ToLocalization(), "ResourceNotSelected".ToLocalization());
            LoggerHelper.Warning(LangKeys.CannotStart.ToLocalization());
            return;
        }

        if (CurrentController != MaaControllerTypes.PlayCover && CurrentDevice == null)
        {
            ToastHelper.Warn(LangKeys.CannotStart.ToLocalization(), "DeviceNotSelected".ToLocalization());
            LoggerHelper.Warning(LangKeys.CannotStart.ToLocalization());
            return;
        }

        MaaProcessor.Instance.Start();
    }

    public void StopTask(Action? action = null)
    {
        MaaProcessor.Instance.Stop(MFATask.MFATaskStatus.STOPPED, action: action);
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var task in TaskItemViewModels)
            task.IsChecked = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var task in TaskItemViewModels)
            task.IsChecked = false;
    }

    [RelayCommand]
    private void AddTask()
    {
        Instances.DialogManager.CreateDialog().WithTitle(LangKeys.AdbEditor.ToLocalization()).WithViewModel(dialog => new AddTaskDialogViewModel(dialog, MaaProcessor.Instance.TasksSource)).TryShow();
    }

    [RelayCommand]
    private void ResetTasks()
    {
        // 清空当前任务列表
        TaskItemViewModels.Clear();

        // 从 TasksSource 重新填充任务（TasksSource 包含 interface 中定义的原始任务）
        foreach (var item in MaaProcessor.Instance.TasksSource)
        {
            // 克隆任务以避免引用问题
            TaskItemViewModels.Add(item.Clone());
        }

        // 更新任务的资源支持状态
        UpdateTasksForResource(CurrentResource);

        // 保存配置
        ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskItems, TaskItemViewModels.ToList().Select(model => model.InterfaceItem));
    }

    #endregion

    #region 日志

    /// <summary>
    /// 日志最大数量限制，超出后自动清理旧日志
    /// </summary>
    private const int MaxLogCount = 150;

    /// <summary>
    /// 每次清理时移除的日志数量
    /// </summary>
    private const int LogCleanupBatchSize = 30;

    /// <summary>
    /// 使用 DisposableObservableCollection 自动管理 LogItemViewModel 的生命周期
    /// 当元素被移除或集合被清空时，会自动调用 Dispose() 释放事件订阅
    /// </summary>
    public DisposableObservableCollection<LogItemViewModel> LogItemViewModels { get; } = new();

    /// <summary>
    /// 清理超出限制的旧日志，防止内存泄漏
    /// DisposableObservableCollection 会自动调用被移除元素的 Dispose()
    /// </summary>
    private void TrimExcessLogs()
    {
        if (LogItemViewModels.Count <= MaxLogCount) return;

        // 计算需要移除的数量
        var removeCount = Math.Min(LogCleanupBatchSize, LogItemViewModels.Count - MaxLogCount + LogCleanupBatchSize);

        // 使用 RemoveRange 批量移除，DisposableObservableCollection 会自动 Dispose
        LogItemViewModels.RemoveRange(0, removeCount);

        // 清理字体缓存，释放未使用的字体资源
        // 这可以防止因渲染特殊Unicode字符而加载的大量字体占用内存
        try
        {
            FontService.Instance.ClearFontCache();
            LoggerHelper.Info("[内存优化] 已清理字体缓存");
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"清理字体缓存失败: {ex.Message}");
        }
    }

    public static string FormatFileSize(long size)
    {
        string unit;
        double value;
        if (size >= 1024L * 1024 * 1024 * 1024)
        {
            value = (double)size / (1024L * 1024 * 1024 * 1024);
            unit = "TB";
        }
        else if (size >= 1024 * 1024 * 1024)
        {
            value = (double)size / (1024 * 1024 * 1024);
            unit = "GB";
        }
        else if (size >= 1024 * 1024)
        {
            value = (double)size / (1024 * 1024);
            unit = "MB";
        }
        else if (size >= 1024)
        {
            value = (double)size / 1024;
            unit = "KB";
        }
        else
        {
            value = size;
            unit = "B";
        }

        return $"{value:F} {unit}";
    }

    public static string FormatDownloadSpeed(double speed)
    {
        string unit;
        double value = speed;
        if (value >= 1024L * 1024 * 1024 * 1024)
        {
            value /= 1024L * 1024 * 1024 * 1024;
            unit = "TB/s";
        }
        else if (value >= 1024L * 1024 * 1024)
        {
            value /= 1024L * 1024 * 1024;
            unit = "GB/s";
        }
        else if (value >= 1024 * 1024)
        {
            value /= 1024 * 1024;
            unit = "MB/s";
        }
        else if (value >= 1024)
        {
            value /= 1024;
            unit = "KB/s";
        }
        else
        {
            unit = "B/s";
        }

        return $"{value:F} {unit}";
    }
    public void OutputDownloadProgress(long value = 0, long maximum = 1, int len = 0, double ts = 1)
    {
        string sizeValueStr = FormatFileSize(value);
        string maxSizeValueStr = FormatFileSize(maximum);
        string speedValueStr = FormatDownloadSpeed(len / ts);

        string progressInfo = $"[{sizeValueStr}/{maxSizeValueStr}({100 * value / maximum}%) {speedValueStr}]";
        OutputDownloadProgress(progressInfo);
    }

    public void ClearDownloadProgress()
    {
        DispatcherHelper.RunOnMainThread(() =>
        {
            if (LogItemViewModels.Count > 0 && LogItemViewModels[0].IsDownloading)
            {
                LogItemViewModels.RemoveAt(0);
            }
        });
    }

    public void OutputDownloadProgress(string output, bool downloading = true)
    {
        // DispatcherHelper.RunOnMainThread(() =>
        // {
        //     var log = new LogItemViewModel(downloading ? LangKeys.NewVersionFoundDescDownloading.ToLocalization() + "\n" + output : output, Instances.RootView.FindResource("SukiAccentColor") as IBrush,
        //         dateFormat: "HH':'mm':'ss")
        //     {
        //         IsDownloading = true,
        //     };
        //     if (LogItemViewModels.Count > 0 && LogItemViewModels[0].IsDownloading)
        //     {
        //         if (!string.IsNullOrEmpty(output))
        //         {
        //             LogItemViewModels[0] = log;
        //         }
        //         else
        //         {
        //             LogItemViewModels.RemoveAt(0);
        //         }
        //     }
        //     else if (!string.IsNullOrEmpty(output))
        //     {
        //         LogItemViewModels.Insert(0, log);
        //     }
        // });
    }


    public static readonly string INFO = "info:";
    public static readonly string[] ERROR = ["err:", "error:"];
    public static readonly string[] WARNING = ["warn:", "warning:"];
    public static readonly string TRACE = "trace:";
    public static readonly string DEBUG = "debug:";
    public static readonly string CRITICAL = "critical:";

    public static bool CheckShouldLog(string content)
    {
        const StringComparison comparison = StringComparison.Ordinal; // 指定匹配规则（避免大小写问题，按需调整）

        if (content.StartsWith(TRACE, comparison))
        {
            return true;
        }

        if (content.StartsWith(DEBUG, comparison))
        {
            return true;
        }

        if (content.StartsWith(INFO, comparison))
        {
            return true;
        }

        var warnPrefix = WARNING.FirstOrDefault(prefix =>
            !string.IsNullOrEmpty(prefix) && content.StartsWith(prefix, comparison)
        );
        if (warnPrefix != null)
        {
            return true;
        }

        var errorPrefix = ERROR.FirstOrDefault(prefix =>
            !string.IsNullOrEmpty(prefix) && content.StartsWith(prefix, comparison)
        );

        if (errorPrefix != null)
        {
            return true;
        }

        if (content.StartsWith(CRITICAL, comparison))
        {
            return true;
        }
        return false;
    }

    public void AddLog(string content,
        IBrush? brush,
        string weight = "Regular",
        bool changeColor = true,
        bool showTime = true)
    {
        brush ??= Brushes.Black;

        var backGroundBrush = Brushes.Transparent;
        const StringComparison comparison = StringComparison.Ordinal; // 指定匹配规则（避免大小写问题，按需调整）

        if (content.StartsWith(TRACE, comparison))
        {
            brush = Brushes.MediumAquamarine;
            content = content.Substring(TRACE.Length).TrimStart();
            changeColor = false;
        }

        if (content.StartsWith(DEBUG, comparison))
        {
            brush = Brushes.DeepSkyBlue;
            content = content.Substring(DEBUG.Length).TrimStart();
            changeColor = false;
        }

        if (content.StartsWith(INFO, comparison))
        {
            content = content.Substring(INFO.Length).TrimStart();
        }

        var warnPrefix = WARNING.FirstOrDefault(prefix =>
            !string.IsNullOrEmpty(prefix) && content.StartsWith(prefix, comparison)
        );
        if (warnPrefix != null)
        {
            brush = Brushes.Orange;
            content = content.Substring(warnPrefix.Length).TrimStart();
            changeColor = false;
        }

        var errorPrefix = ERROR.FirstOrDefault(prefix =>
            !string.IsNullOrEmpty(prefix) && content.StartsWith(prefix, comparison)
        );

        if (errorPrefix != null)
        {
            brush = Brushes.OrangeRed;
            content = content.Substring(errorPrefix.Length).TrimStart();
            changeColor = false;
        }

        if (content.StartsWith(CRITICAL, comparison))
        {
            var color = DispatcherHelper.RunOnMainThread(() => MFAExtensions.FindSukiUiResource<Color>(
                "SukiLightBorderBrush"
            ));
            if (color != null)
                brush = DispatcherHelper.RunOnMainThread(() => new SolidColorBrush(color.Value));
            else
                brush = Brushes.White;
            backGroundBrush = Brushes.OrangeRed;
            content = content.Substring(CRITICAL.Length).TrimStart();
        }

        DispatcherHelper.PostOnMainThread(() =>
        {
            LogItemViewModels.Add(new LogItemViewModel(content, brush, weight, "HH':'mm':'ss",
                showTime: showTime, changeColor: changeColor)
            {
                BackgroundColor = backGroundBrush
            });
            LoggerHelper.Info($"[Record] {content}");

            // 自动清理超出限制的旧日志
            TrimExcessLogs();
        });
    }

    public void AddLog(string content,
        string color = "",
        string weight = "Regular",
        bool changeColor = true,
        bool showTime = true)
    {
        var brush = BrushHelper.ConvertToBrush(color, Brushes.Black);
        AddLog(content, brush, weight, changeColor, showTime);
    }

    public void AddLogByKey(string key, IBrush? brush = null, bool changeColor = true, bool transformKey = true, params string[] formatArgsKeys)
    {
        brush ??= Brushes.Black;
        Task.Run(() =>
        {
            DispatcherHelper.PostOnMainThread(() =>
            {
                var log = new LogItemViewModel(key, brush, "Regular", true, "HH':'mm':'ss", changeColor: changeColor, showTime: true, transformKey: transformKey, formatArgsKeys);
                LogItemViewModels.Add(log);
                LoggerHelper.Info(log.Content);
                // 自动清理超出限制的旧日志
                TrimExcessLogs();
            });
        });
    }

    public void AddLogByKey(string key, string color = "", bool changeColor = true, bool transformKey = true, params string[] formatArgsKeys)
    {
        var brush = BrushHelper.ConvertToBrush(color, Brushes.Black);
        AddLogByKey(key, brush, changeColor, transformKey, formatArgsKeys);
    }

    public void AddMarkdown(string key, IBrush? brush = null, bool changeColor = true, bool transformKey = true, params string[] formatArgsKeys)
    {
        brush ??= Brushes.Black;
        Task.Run(() =>
        {
            DispatcherHelper.PostOnMainThread(() =>
            {
                var log = new LogItemViewModel(key, brush, "Regular", true, "HH':'mm':'ss", changeColor: changeColor, showTime: true, transformKey: transformKey, formatArgsKeys)
                {
                    UseMarkdown = true
                };
                LogItemViewModels.Add(log);
                LoggerHelper.Info(log.Content);
                // 自动清理超出限制的旧日志
                TrimExcessLogs();
            });
        });
    }

    #endregion

    #region 连接

    [ObservableProperty] private int _shouldShow = 0;
    [ObservableProperty] private ObservableCollection<object> _devices = [];
    [ObservableProperty] private object? _currentDevice;
    private DateTime? _lastExecutionTime;

    partial void OnShouldShowChanged(int value)
    {
        // DispatcherHelper.PostOnMainThread(() => Instances.TaskQueueView.UpdateConnectionLayout(true));
    }

    partial void OnCurrentDeviceChanged(object? value)
    {
        ChangedDevice(value);
    }

    public void ChangedDevice(object? value)
    {
        var igoreToast = false;
        if (value != null)
        {
            var now = DateTime.Now;
            if (_lastExecutionTime == null)
            {
                _lastExecutionTime = now;
            }
            else
            {
                if (now - _lastExecutionTime < TimeSpan.FromSeconds(2))
                    igoreToast = true;
                else
                    _lastExecutionTime = now;
            }
        }
        if (value is DesktopWindowInfo window)
        {
            if (!igoreToast) ToastHelper.Info(LangKeys.WindowSelectionMessage.ToLocalizationFormatted(false, ""), window.Name);
            MaaProcessor.Config.DesktopWindow.Name = window.Name;
            MaaProcessor.Config.DesktopWindow.HWnd = window.Handle;
            MaaProcessor.Instance.SetTasker();
        }
        else if (value is AdbDeviceInfo device)
        {
            if (!igoreToast) ToastHelper.Info(LangKeys.EmulatorSelectionMessage.ToLocalizationFormatted(false, ""), device.Name);
            MaaProcessor.Config.AdbDevice.Name = device.Name;
            MaaProcessor.Config.AdbDevice.AdbPath = device.AdbPath;
            MaaProcessor.Config.AdbDevice.AdbSerial = device.AdbSerial;
            MaaProcessor.Config.AdbDevice.Config = device.Config;
            MaaProcessor.Config.AdbDevice.Info = device;
            MaaProcessor.Instance.SetTasker();
            ConfigurationManager.Current.SetValue(ConfigurationKeys.AdbDevice, device);
        }
    }

    [ObservableProperty] private MaaControllerTypes _currentController =
        ConfigurationManager.Current.GetValue(ConfigurationKeys.CurrentController, MaaControllerTypes.Adb, MaaControllerTypes.None, new UniversalEnumConverter<MaaControllerTypes>());

    partial void OnCurrentControllerChanged(MaaControllerTypes value)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.CurrentController, value.ToString());
        UpdateResourcesForController();
        if (value == MaaControllerTypes.PlayCover)
        {
            TryReadPlayCoverConfig();
        }
        Refresh();
    }

    /// <summary>
    /// 根据当前控制器更新资源列表
    /// </summary>
    public void UpdateResourcesForController()
    {
        // 获取所有资源
        var allResources = MaaProcessor.Interface?.Resources.Values.ToList() ?? new List<MaaInterface.MaaInterfaceResource>();

        if (allResources.Count == 0)
        {
            allResources.Add(new MaaInterface.MaaInterfaceResource
            {
                Name = "Default",
                Path = [MaaProcessor.ResourceBase]
            });
        }

        // 获取当前控制器的名称
        var currentControllerName = GetCurrentControllerName();

        // 根据控制器过滤资源
        var filteredResources = TaskLoader.FilterResourcesByController(allResources, currentControllerName);

        foreach (var resource in filteredResources)
        {
            resource.InitializeDisplayName();
        }

        // 更新资源列表
        CurrentResources = new ObservableCollection<MaaInterface.MaaInterfaceResource>(filteredResources);

        if (CurrentResources.Count == 0)
        {
            CurrentResource = string.Empty;
            return;
        }

        // 当前资源为空或不在列表时，选择第一个
        if (string.IsNullOrWhiteSpace(CurrentResource) || CurrentResources.All(r => r.Name != CurrentResource))
        {
            var oldResource = CurrentResource;
            var newResource = CurrentResources[0].Name ?? "Default";
            CurrentResource = newResource;

            // 仅当旧资源非空时才提示（避免首次加载时提示）
            if (!string.IsNullOrEmpty(oldResource))
            {
                var controllerDisplayName = currentControllerName ?? CurrentController.ToResourceKey().ToLocalization();
                ToastHelper.Warn(
                    LangKeys.ResourceAutoSwitched.ToLocalization(),
                    LangKeys.ResourceNotSupportController.ToLocalizationFormatted(
                        false, oldResource, controllerDisplayName, newResource),
                    6);
            }

            return;
        }

        // 资源仍然有效时，强制刷新绑定以更新下拉框显示
        OnPropertyChanged(nameof(CurrentResource));
    }

    /// <summary>
    /// 获取当前控制器的名称
    /// </summary>
    private string? GetCurrentControllerName()
    {
        var controllerTypeKey = CurrentController.ToJsonKey();

        // 从 interface 的 controller 配置中查找匹配的控制器
        var controller = MaaProcessor.Interface?.Controller?.Find(c =>
            c.Type != null && c.Type.Equals(controllerTypeKey, StringComparison.OrdinalIgnoreCase));

        return controller?.Name;
    }

    [ObservableProperty] private bool _isConnected;
    public void SetConnected(bool isConnected)
    {
        // 使用异步投递避免从非UI线程修改属性时导致死锁
        DispatcherHelper.PostOnMainThread(() => IsConnected = isConnected);
    }

    [RelayCommand]
    private void CustomAdb()
    {
        var deviceInfo = CurrentDevice as AdbDeviceInfo;

        Instances.DialogManager.CreateDialog().WithTitle("AdbEditor").WithViewModel(dialog => new AdbEditorDialogViewModel(deviceInfo, dialog)).Dismiss().ByClickingBackground().TryShow();
    }

    [RelayCommand]
    private void EditPlayCover()
    {
        Instances.DialogManager.CreateDialog().WithTitle("PlayCoverEditor")
            .WithViewModel(dialog => new PlayCoverEditorDialogViewModel(MaaProcessor.Config.PlayCover, dialog))
            .Dismiss().ByClickingBackground().TryShow();
    }

    private CancellationTokenSource? _refreshCancellationTokenSource;

    [RelayCommand]
    private async Task Reconnect()
    {
        if (CurrentResources.Count == 0 || string.IsNullOrWhiteSpace(CurrentResource) || CurrentResources.All(r => r.Name != CurrentResource))
        {
            ToastHelper.Warn(LangKeys.CannotStart.ToLocalization(), "ResourceNotSelected".ToLocalization());
            LoggerHelper.Warning(LangKeys.CannotStart.ToLocalization());
            return;
        }

        if (CurrentController != MaaControllerTypes.PlayCover && CurrentDevice == null)
        {
            ToastHelper.Warn(LangKeys.CannotStart.ToLocalization(), "DeviceNotSelected".ToLocalization());
            LoggerHelper.Warning(LangKeys.CannotStart.ToLocalization());
            return;
        }

        using var tokenSource = new CancellationTokenSource();
        await MaaProcessor.Instance.ReconnectAsync(tokenSource.Token);
        await MaaProcessor.Instance.TestConnecting();
    }

    [RelayCommand]
    private void Refresh()
    {
        if (CurrentController == MaaControllerTypes.PlayCover)
        {
            SetConnected(false);
            return;
        }

        _refreshCancellationTokenSource?.Cancel();
        _refreshCancellationTokenSource = new CancellationTokenSource();
        var controllerType = CurrentController;
        TaskManager.RunTask(() => AutoDetectDevice(_refreshCancellationTokenSource.Token), _refreshCancellationTokenSource.Token, name: "刷新", handleError: (e) => HandleDetectionError(e, controllerType),
            catchException: true, shouldLog: true);
    }

    [RelayCommand]
    private void CloseE()
    {
        MaaProcessor.CloseSoftware();
    }

    [RelayCommand]
    private void Clear()
    {
        // DisposableObservableCollection 会自动调用所有元素的 Dispose()
        LogItemViewModels.Clear();
    }

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
    [RelayCommand]
    private void Export()
    {
        FileLogExporter.CompressRecentLogs(Instances.RootView.StorageProvider);
    }

    public void AutoDetectDevice(CancellationToken token = default)
    {
        if (CurrentController == MaaControllerTypes.PlayCover)
        {
            DispatcherHelper.RunOnMainThread(() =>
            {
                Devices = [];
                CurrentDevice = null;
            });
            SetConnected(false);
            return;
        }

        var controllerType = CurrentController;
        var isAdb = controllerType == MaaControllerTypes.Adb;

        ToastHelper.Info(GetDetectionMessage(controllerType));
        SetConnected(false);
        token.ThrowIfCancellationRequested();
        var (devices, index) = isAdb ? DetectAdbDevices() : DetectWin32Windows();
        token.ThrowIfCancellationRequested();
        UpdateDeviceList(devices, index);
        token.ThrowIfCancellationRequested();
        HandleControllerSettings(controllerType);
        token.ThrowIfCancellationRequested();
        UpdateConnectionStatus(devices.Count > 0, controllerType);
    }

    private string GetDetectionMessage(MaaControllerTypes controllerType) =>
        controllerType == MaaControllerTypes.Adb
            ? "EmulatorDetectionStarted".ToLocalization()
            : "WindowDetectionStarted".ToLocalization();

    private (ObservableCollection<object> devices, int index) DetectAdbDevices()
    {
        var devices = MaaProcessor.Toolkit.AdbDevice.Find();
        var index = CalculateAdbDeviceIndex(devices);
        return (new(devices), index);
    }

    private int CalculateAdbDeviceIndex(IList<AdbDeviceInfo> devices)
    {
        if (CurrentDevice is AdbDeviceInfo info)
        {
            LoggerHelper.Info($"Current device: {JsonConvert.SerializeObject(info)}");

            // 使用指纹匹配设备
            var matchedDevices = devices
                .Where(device => device.MatchesFingerprint(info))
                .ToList();

            LoggerHelper.Info($"Found {matchedDevices.Count} devices matching fingerprint");

            // 多匹配时排序：先比AdbSerial前缀（冒号前），再比设备名称
            if (matchedDevices.Any())
            {
                matchedDevices.Sort((a, b) =>
                {
                    var aPrefix = a.AdbSerial.Split(':', 2)[0];
                    var bPrefix = b.AdbSerial.Split(':', 2)[0];
                    int prefixCompare = string.Compare(aPrefix, bPrefix, StringComparison.Ordinal);
                    return prefixCompare != 0 ? prefixCompare : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
                });
                return devices.IndexOf(matchedDevices.First());
            }
        }

        var config = ConfigurationManager.Current.GetValue(ConfigurationKeys.EmulatorConfig, string.Empty);
        if (string.IsNullOrWhiteSpace(config)) return 0;

        var targetNumber = ExtractNumberFromEmulatorConfig(config);
        return devices.Select((d, i) =>
                TryGetIndexFromConfig(d.Config, out var index) && index == targetNumber ? i : -1)
            .FirstOrDefault(i => i >= 0);
    }


    public static int ExtractNumberFromEmulatorConfig(string emulatorConfig)
    {
        var match = Regex.Match(emulatorConfig, @"\d+");

        if (match.Success)
        {
            return int.Parse(match.Value);
        }

        return 0;
    }

    private bool TryGetIndexFromConfig(string configJson, out int index)
    {
        index = DeviceDisplayConverter.GetFirstEmulatorIndex(configJson);
        return index != -1;
    }

    private static bool TryExtractPortFromAdbSerial(string adbSerial, out int port)
    {
        port = -1;
        var parts = adbSerial.Split(':', 2); // 分割为IP和端口（最多分割1次）
        LoggerHelper.Info(JsonConvert.SerializeObject(parts));
        return parts.Length == 2 && int.TryParse(parts[1], out port);
    }

    private (ObservableCollection<object> devices, int index) DetectWin32Windows()
    {
        Thread.Sleep(500);
        var windows = MaaProcessor.Toolkit.Desktop.Window.Find().Where(win => !string.IsNullOrWhiteSpace(win.Name)).ToList();
        var (index, filtered) = CalculateWindowIndex(windows);
        return (new(filtered), index);
    }

    private (int index, List<DesktopWindowInfo> afterFiltered) CalculateWindowIndex(List<DesktopWindowInfo> windows)
    {
        var controller = MaaProcessor.Interface?.Controller?
            .FirstOrDefault(c => c.Type?.Equals("win32", StringComparison.OrdinalIgnoreCase) == true);

        if (controller?.Win32 == null)
            return (windows.FindIndex(win => !string.IsNullOrWhiteSpace(win.Name)), windows);

        var filtered = windows.Where(win =>
            !string.IsNullOrWhiteSpace(win.Name)).ToList();

        filtered = ApplyRegexFilters(filtered, controller.Win32);
        return (filtered.Count > 0 ? filtered.IndexOf(filtered.First()) : 0, filtered.ToList());
    }


    private List<DesktopWindowInfo> ApplyRegexFilters(List<DesktopWindowInfo> windows, MaaInterface.MaaResourceControllerWin32 win32)
    {
        var filtered = windows;
        if (!string.IsNullOrWhiteSpace(win32.WindowRegex))
        {
            var regex = new Regex(win32.WindowRegex);
            filtered = filtered.Where(w => regex.IsMatch(w.Name)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(win32.ClassRegex))
        {
            var regex = new Regex(win32.ClassRegex);
            filtered = filtered.Where(w => regex.IsMatch(w.ClassName)).ToList();
        }
        return filtered;
    }

    private void UpdateDeviceList(ObservableCollection<object> devices, int index)
    {
        DispatcherHelper.RunOnMainThread(() =>
        {
            Devices = devices;
            if (devices.Count > index)
                CurrentDevice = devices[index];
            else
                CurrentDevice = null;
        });
    }

    private void HandleControllerSettings(MaaControllerTypes controllerType)
    {
        if (controllerType == MaaControllerTypes.PlayCover)
            return;

        var controller = MaaProcessor.Interface?.Controller?
            .FirstOrDefault(c => c.Type?.Equals(controllerType.ToJsonKey(), StringComparison.OrdinalIgnoreCase) == true);

        if (controller == null) return;

        var isAdb = controllerType == MaaControllerTypes.Adb;
        HandleInputSettings(controller, isAdb);
        HandleScreenCapSettings(controller, isAdb);
    }

    private void HandleInputSettings(MaaInterface.MaaResourceController controller, bool isAdb)
    {
        if (isAdb)
        {
            var input = controller.Adb?.Input;
            if (input == null) return;
            Instances.ConnectSettingsUserControlModel.AdbControlInputType = input switch
            {
                1 => AdbInputMethods.AdbShell,
                2 => AdbInputMethods.MinitouchAndAdbKey,
                4 => AdbInputMethods.Maatouch,
                8 => AdbInputMethods.EmulatorExtras,
                _ => Instances.ConnectSettingsUserControlModel.AdbControlInputType
            };
        }
        else
        {
            var mouse = controller.Win32?.Mouse;
            if (mouse != null)
            {
                var parsed = ParseWin32InputMethod(mouse);
                if (parsed != null)
                    Instances.ConnectSettingsUserControlModel.Win32ControlMouseType = parsed.Value;
            }
            var keyboard = controller.Win32?.Keyboard;
            if (keyboard != null)
            {
                var parsed = ParseWin32InputMethod(keyboard);
                if (parsed != null)
                    Instances.ConnectSettingsUserControlModel.Win32ControlKeyboardType = parsed.Value;
            }
            var input = controller.Win32?.Input;
            if (keyboard == null && mouse == null && input != null)
            {
                var parsed = ParseWin32InputMethod(input);
                if (parsed != null)
                {
                    Instances.ConnectSettingsUserControlModel.Win32ControlKeyboardType = parsed.Value;
                    Instances.ConnectSettingsUserControlModel.Win32ControlMouseType = parsed.Value;
                }
            }
        }
    }

    /// <summary>
    /// 解析 Win32InputMethod，支持旧版 long 格式和新版 string 格式
    /// </summary>
    private static Win32InputMethod? ParseWin32InputMethod(object? value)
    {
        if (value == null) return null;

        // 新版 string 格式（枚举名）
        if (value is string strValue)
        {
            if (Enum.TryParse<Win32InputMethod>(strValue, ignoreCase: true, out var result))
                return result;
            return null;
        }

        // 旧版 long 格式
        var longValue = Convert.ToInt64(value);
        return longValue switch
        {
            1 => Win32InputMethod.Seize,
            2 => Win32InputMethod.SendMessage,
            4 => Win32InputMethod.PostMessage,
            8 => Win32InputMethod.LegacyEvent,
            16 => Win32InputMethod.PostThreadMessage,
            32 => Win32InputMethod.SendMessageWithCursorPos,
            64 => Win32InputMethod.PostMessageWithCursorPos,
            _ => null
        };
    }

    /// <summary>
    /// 解析 Win32ScreencapMethod，支持旧版 long 格式和新版 string 格式
    /// </summary>
    private static Win32ScreencapMethod? ParseWin32ScreencapMethod(object? value)
    {
        if (value == null) return null;

        // 新版 string 格式（枚举名）
        if (value is string strValue)
        {
            if (Enum.TryParse<Win32ScreencapMethod>(strValue, ignoreCase: true, out var result))
                return result;
            return null;
        }

        // 旧版 long 格式
        var longValue = Convert.ToInt64(value);
        return longValue switch
        {
            1 => Win32ScreencapMethod.GDI,
            2 => Win32ScreencapMethod.FramePool,
            4 => Win32ScreencapMethod.DXGI_DesktopDup,
            8 => Win32ScreencapMethod.DXGI_DesktopDup_Window,
            16 => Win32ScreencapMethod.PrintWindow,
            32 => Win32ScreencapMethod.ScreenDC,
            _ => null
        };
    }

    private void HandleScreenCapSettings(MaaInterface.MaaResourceController controller, bool isAdb)
    {
        if (isAdb)
        {
            var screenCap = controller.Adb?.ScreenCap;
            if (screenCap == null) return;
            Instances.ConnectSettingsUserControlModel.AdbControlScreenCapType = screenCap switch
            {
                1 => AdbScreencapMethods.EncodeToFileAndPull,
                2 => AdbScreencapMethods.Encode,
                4 => AdbScreencapMethods.RawWithGzip,
                8 => AdbScreencapMethods.RawByNetcat,
                16 => AdbScreencapMethods.MinicapDirect,
                32 => AdbScreencapMethods.MinicapStream,
                64 => AdbScreencapMethods.EmulatorExtras,
                _ => Instances.ConnectSettingsUserControlModel.AdbControlScreenCapType
            };
        }
        else
        {
            var screenCap = controller.Win32?.ScreenCap;
            if (screenCap == null) return;
            var parsed = ParseWin32ScreencapMethod(screenCap);
            if (parsed != null)
                Instances.ConnectSettingsUserControlModel.Win32ControlScreenCapType = parsed.Value;
        }
    }

    private void UpdateConnectionStatus(bool hasDevices, MaaControllerTypes controllerType)
    {
        if (!hasDevices)
        {
            var isAdb = controllerType == MaaControllerTypes.Adb;
            ToastHelper.Info((
                isAdb ? LangKeys.NoEmulatorFound : LangKeys.NoWindowFound).ToLocalization(), (
                isAdb ? LangKeys.NoEmulatorFoundDetail : "").ToLocalization());
        }
    }

    public void TryReadPlayCoverConfig()
    {
        if (ConfigurationManager.Current.TryGetValue(ConfigurationKeys.PlayCoverConfig, out PlayCoverCoreConfig savedConfig))
        {
            MaaProcessor.Config.PlayCover = savedConfig;
        }
    }

    private void HandleDetectionError(Exception ex, MaaControllerTypes controllerType)
    {
        var targetKey = controllerType switch
        {
            MaaControllerTypes.Adb => LangKeys.Emulator,
            MaaControllerTypes.Win32 => LangKeys.Window,
            MaaControllerTypes.PlayCover => LangKeys.TabPlayCover,
            _ => LangKeys.Window
        };
        ToastHelper.Warn(string.Format(
            LangKeys.TaskStackError.ToLocalization(),
            targetKey.ToLocalization(),
            ex.Message));

        LoggerHelper.Error(ex);
    }

    public void TryReadAdbDeviceFromConfig(bool inTask = true, bool refresh = false)
    {
        if (CurrentController == MaaControllerTypes.PlayCover)
        {
            SetConnected(false);
            return;
        }

        if (refresh
            || CurrentController != MaaControllerTypes.Adb
            || !ConfigurationManager.Current.GetValue(ConfigurationKeys.RememberAdb, true)
            || MaaProcessor.Config.AdbDevice.AdbPath != "adb"
            || !ConfigurationManager.Current.TryGetValue(ConfigurationKeys.AdbDevice, out AdbDeviceInfo savedDevice,
                new UniversalEnumConverter<AdbInputMethods>(), new UniversalEnumConverter<AdbScreencapMethods>()))
        {
            _refreshCancellationTokenSource?.Cancel();
            _refreshCancellationTokenSource = new CancellationTokenSource();
            if (inTask)
                TaskManager.RunTask(() => AutoDetectDevice(_refreshCancellationTokenSource.Token), name: "刷新设备");
            else
                AutoDetectDevice(_refreshCancellationTokenSource.Token);
            return;
        }
        // 检查是否启用指纹匹配功能
        var useFingerprintMatching = ConfigurationManager.Current.GetValue(ConfigurationKeys.UseFingerprintMatching, true);

        if (useFingerprintMatching)
        {
            // 使用指纹匹配设备，而不是直接使用保存的设备信息
            // 因为雷电模拟器等的AdbSerial每次启动都会变化
            LoggerHelper.Info("Reading saved ADB device from configuration, using fingerprint matching.");
            LoggerHelper.Info($"Saved device fingerprint: {savedDevice.GenerateDeviceFingerprint()}");

            // 搜索当前可用的设备
            var currentDevices = MaaProcessor.Toolkit.AdbDevice.Find();

            // 尝试通过指纹匹配找到对应的设备（当任一方index为-1时不比较index）
            AdbDeviceInfo? matchedDevice = null;
            foreach (var device in currentDevices)
            {
                if (device.MatchesFingerprint(savedDevice))
                {
                    matchedDevice = device;
                    LoggerHelper.Info($"Found matching device by fingerprint: {device.Name} ({device.AdbSerial})");
                    break;
                }
            }

            if (matchedDevice != null)
            {
                // 使用新搜索到的设备信息（AdbSerial等可能已更新）
                DispatcherHelper.RunOnMainThread(() =>
                {
                    Devices = new ObservableCollection<object>(currentDevices);
                    CurrentDevice = matchedDevice;
                });
                ChangedDevice(matchedDevice);
            }
            else
            {
                // 没有找到匹配的设备，执行自动检测
                LoggerHelper.Info("No matching device found by fingerprint, performing auto detection.");
                _refreshCancellationTokenSource?.Cancel();
                _refreshCancellationTokenSource = new CancellationTokenSource();
                if (inTask)
                    TaskManager.RunTask(() => AutoDetectDevice(_refreshCancellationTokenSource.Token), name: "刷新设备");
                else
                    AutoDetectDevice(_refreshCancellationTokenSource.Token);
            }
        }
        else
        {
            // 不使用指纹匹配，直接使用保存的设备信息
            LoggerHelper.Info("Reading saved ADB device from configuration, fingerprint matching disabled.");
            DispatcherHelper.RunOnMainThread(() =>
            {
                Devices = [savedDevice];
                CurrentDevice = savedDevice;
            });
            ChangedDevice(savedDevice);
        }
    }

    #endregion

    #region 资源

    [ObservableProperty] private ObservableCollection<MaaInterface.MaaInterfaceResource> _currentResources = [];
    private string _currentResource = string.Empty;

    public string CurrentResource
    {
        get => _currentResource;
        set
        {
            if (string.Equals(_currentResource, value, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                MaaProcessor.Instance.SetTasker();
            }

            SetNewProperty(ref _currentResource, value);
            HandlePropertyChanged(ConfigurationKeys.Resource, value);

            if (!string.IsNullOrWhiteSpace(value))
            {
                UpdateTasksForResource(value);
            }
            else
            {
                UpdateTasksForResource(null);
            }
        }
    }

    /// <summary>
    /// 根据当前资源更新任务列表的可见性和资源选项项
    /// </summary>
    /// <param name="resourceName">资源包名称</param>
    public void UpdateTasksForResource(string? resourceName)
    {
        // 查找当前资源
        var currentResource = CurrentResources.FirstOrDefault(r => r.Name == resourceName);
        var hasResourceOption = currentResource?.Option != null && currentResource.Option.Count > 0;

        // 查找当前的资源选项项
        var existingResourceOptionItem = TaskItemViewModels.FirstOrDefault(t => t.IsResourceOptionItem);

        if (hasResourceOption)
        {
            // 初始化资源的 SelectOptions
            InitializeResourceSelectOptions(currentResource!);

            if (existingResourceOptionItem == null)
            {
                // 需要添加资源选项项
                var resourceOptionItem = new DragItemViewModel(currentResource!);
                resourceOptionItem.IsVisible = true;

                // 从配置中恢复已保存的选项值
                RestoreResourceOptionValues(currentResource!);

                TaskItemViewModels.Insert(0, resourceOptionItem);
            }
            else if (existingResourceOptionItem.ResourceItem?.Name != currentResource!.Name)
            {
                // 资源选项项属于不同的资源，需要替换
                var index = TaskItemViewModels.IndexOf(existingResourceOptionItem);
                TaskItemViewModels.Remove(existingResourceOptionItem);

                var resourceOptionItem = new DragItemViewModel(currentResource);
                resourceOptionItem.IsVisible = true;

                // 从配置中恢复已保存的选项值
                RestoreResourceOptionValues(currentResource);

                TaskItemViewModels.Insert(index >= 0 ? index : 0, resourceOptionItem);
            }
            else
            {
                // 同一资源，更新 SelectOptions
                existingResourceOptionItem.ResourceItem = currentResource;
            }
        }
        else
        {
            // 当前资源没有 option，移除资源选项项
            if (existingResourceOptionItem != null)
            {
                if (existingResourceOptionItem.EnableSetting)
                {
                    existingResourceOptionItem.EnableSetting = false;
                }
                TaskItemViewModels.Remove(existingResourceOptionItem);
            }
        }

        // 更新每个任务的资源支持状态
        foreach (var task in TaskItemViewModels)
        {
            if (!task.IsResourceOptionItem)
            {
                task.UpdateResourceSupport(resourceName);
            }
        }
    }

    /// <summary>
    /// 初始化资源的 SelectOptions
    /// 只初始化顶级选项，子选项会在运行时由 UpdateSubOptions 动态创建
    /// 会保留已有的值并从配置中恢复保存的值
    /// </summary>
    private void InitializeResourceSelectOptions(MaaInterface.MaaInterfaceResource resource)
    {
        if (resource.Option == null || resource.Option.Count == 0)
        {
            resource.SelectOptions = null;
            return;
        }

        // 收集所有子选项名称（这些选项不应该在顶级初始化）
        var subOptionNames = new HashSet<string>();
        foreach (var optionName in resource.Option)
        {
            if (MaaProcessor.Interface?.Option?.TryGetValue(optionName, out var interfaceOption) == true)
            {
                if (interfaceOption.Cases != null)
                {
                    foreach (var caseOption in interfaceOption.Cases)
                    {
                        if (caseOption.Option != null)
                        {
                            foreach (var subOptionName in caseOption.Option)
                            {
                                subOptionNames.Add(subOptionName);
                            }
                        }
                    }
                }
            }
        }

        // 获取已保存的配置
        var savedResourceOptions = ConfigurationManager.Current.GetValue(
            ConfigurationKeys.ResourceOptionItems,
            new Dictionary<string, List<MaaInterface.MaaInterfaceSelectOption>>());

        Dictionary<string, MaaInterface.MaaInterfaceSelectOption>? savedDict = null;
        if (savedResourceOptions.TryGetValue(resource.Name ?? string.Empty, out var savedOptions) && savedOptions != null)
        {
            savedDict = savedOptions.ToDictionary(o => o.Name ?? string.Empty);
        }

        // 保留已有的 SelectOptions 值
        var existingDict = resource.SelectOptions?.ToDictionary(o => o.Name ?? string.Empty)
            ?? new Dictionary<string, MaaInterface.MaaInterfaceSelectOption>();

        // 只初始化顶级选项（不是子选项的选项）
        resource.SelectOptions = resource.Option
            .Where(optionName => !subOptionNames.Contains(optionName))
            .Select(optionName =>
            {
                // 优先使用已有的值（保留运行时的修改）
                if (existingDict.TryGetValue(optionName, out var existingOpt))
                {
                    return existingOpt;
                }

                // 其次使用配置中保存的值
                if (savedDict?.TryGetValue(optionName, out var savedOpt) == true)
                {
                    // 克隆保存的选项，避免引用问题
                    var clonedOpt = new MaaInterface.MaaInterfaceSelectOption
                    {
                        Name = savedOpt.Name,
                        Index = savedOpt.Index,
                        Data = savedOpt.Data != null ? new Dictionary<string, string?>(savedOpt.Data) : null,
                        SubOptions = savedOpt.SubOptions != null ? CloneSubOptions(savedOpt.SubOptions) : null
                    };
                    return clonedOpt;
                }

                // 最后创建新的并设置默认值
                var selectOption = new MaaInterface.MaaInterfaceSelectOption
                {
                    Name = optionName
                };
                TaskLoader.SetDefaultOptionValue(MaaProcessor.Interface, selectOption);
                return selectOption;
            }).ToList();
    }

    /// <summary>
    /// 克隆子选项列表
    /// </summary>
    private List<MaaInterface.MaaInterfaceSelectOption> CloneSubOptions(List<MaaInterface.MaaInterfaceSelectOption> subOptions)
    {
        return subOptions.Select(opt => new MaaInterface.MaaInterfaceSelectOption
        {
            Name = opt.Name,
            Index = opt.Index,
            Data = opt.Data != null ? new Dictionary<string, string?>(opt.Data) : null,
            SubOptions = opt.SubOptions != null ? CloneSubOptions(opt.SubOptions) : null
        }).ToList();
    }

    /// <summary>
    /// 从配置中恢复资源选项的已保存值（已整合到 InitializeResourceSelectOptions 中，保留此方法以兼容）
    /// </summary>
    private void RestoreResourceOptionValues(MaaInterface.MaaInterfaceResource resource)
    {
        // 配置恢复逻辑已整合到 InitializeResourceSelectOptions 中
        // 此方法保留以兼容现有调用，但不再需要执行任何操作
    }

    #endregion

    #region 实时画面

    /// <summary>
    /// Live View 刷新率变化事件，参数为计算后的间隔（秒）
    /// </summary>
    public event Action<double>? LiveViewRefreshRateChanged;

    /// <summary>
    /// Live View 是否启用
    /// </summary>
    [ObservableProperty] private bool _enableLiveView =
        ConfigurationManager.Current.GetValue(ConfigurationKeys.EnableLiveView, true);

    /// <summary>
    /// Live View 刷新率（FPS），范围 1-60，默认 10
    /// </summary>
    [ObservableProperty] private double _liveViewRefreshRate =
        ConfigurationManager.Current.GetValue(ConfigurationKeys.LiveViewRefreshRate, 10.0);

    partial void OnEnableLiveViewChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLiveViewVisible));
    }

    partial void OnLiveViewRefreshRateChanged(double value)
    {
        // 限制范围在 1 到 60 FPS 之间，不允许 0 以防止除零错误
        if (value < 1)
        {
            LiveViewRefreshRate = 1;
            return;
        }
        if (value > 120)
        {
            LiveViewRefreshRate = 120;
            return;
        }

        ConfigurationManager.Current.SetValue(ConfigurationKeys.LiveViewRefreshRate, value);
        // 将 FPS 转换为间隔（秒）并触发事件
        var interval = 1.0 / value;
        LiveViewRefreshRateChanged?.Invoke(interval);
    }

    /// <summary>
    /// 获取当前刷新间隔（秒），用于定时器
    /// </summary>
    public double GetLiveViewRefreshInterval() => 1.0 / LiveViewRefreshRate;

    [ObservableProperty] private Bitmap? _liveViewImage;
    [ObservableProperty] private bool _isLiveViewExpanded = true;
    private WriteableBitmap? _liveViewWriteableBitmap;
    private int _liveViewProcessing;
    [ObservableProperty] private double _liveViewFps;
    private DateTime _liveViewFpsWindowStart = DateTime.UtcNow;
    private int _liveViewFrameCount;
    [ObservableProperty] private string _currentTaskName = "";

    private int _liveViewImageCount;
    private int _liveViewImageNewestCount;

    private static int _liveViewSemaphoreCurrentCount = 2;
    private const int LiveViewSemaphoreMaxCount = 5;
    private static int _liveViewSemaphoreFailCount = 0;
    private static readonly SemaphoreSlim _liveViewSemaphore = new(_liveViewSemaphoreCurrentCount, LiveViewSemaphoreMaxCount);

    private readonly WriteableBitmap?[] _liveViewImageCache = new WriteableBitmap?[LiveViewSemaphoreMaxCount];

    /// <summary>
    /// Live View 是否可见（已连接且有图像）
    /// </summary>
    public bool IsLiveViewVisible => EnableLiveView && IsConnected && LiveViewImage != null;

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLiveViewVisible));
    }

    partial void OnLiveViewImageChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(IsLiveViewVisible));
    }

    [RelayCommand]
    private void ToggleLiveViewExpanded()
    {
        IsLiveViewExpanded = !IsLiveViewExpanded;
    }

    /// <summary>
    /// 更新当前任务名称
    /// </summary>
    public void SetCurrentTaskName(string taskName)
    {
        DispatcherHelper.PostOnMainThread(() => CurrentTaskName = taskName);
    }

    /// <summary>
    /// 更新 Live View 图像（仿 WPF：直接写入缓冲）
    /// </summary>
    public async Task UpdateLiveViewImageAsync(MaaImageBuffer? buffer)
    {
        if (!await _liveViewSemaphore.WaitAsync(0))
        {
            if (++_liveViewSemaphoreFailCount < 3)
            {
                buffer?.Dispose();
                return;
            }

            _liveViewSemaphoreFailCount = 0;

            if (_liveViewSemaphoreCurrentCount < LiveViewSemaphoreMaxCount)
            {
                _liveViewSemaphoreCurrentCount++;
                _liveViewSemaphore.Release();
                LoggerHelper.Info($"LiveView Semaphore Full, increase semaphore count to {_liveViewSemaphoreCurrentCount}");
            }

            buffer?.Dispose();
            return;
        }

        try
        {
            var count = Interlocked.Increment(ref _liveViewImageCount);
            var index = count % _liveViewImageCache.Length;

            if (buffer == null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LiveViewImage = null;
                    _liveViewWriteableBitmap?.Dispose();
                    _liveViewWriteableBitmap = null;
                    Array.Fill(_liveViewImageCache, null);
                    _liveViewImageNewestCount = 0;
                    _liveViewImageCount = 0;
                });
                return;
            }

            if (!buffer.TryGetRawData(out var rawData, out var width, out var height, out _))
            {
                return;
            }

            if (width <= 0 || height <= 0)
            {
                return;
            }

            if (count <= _liveViewImageNewestCount)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _liveViewImageCache[index] = WriteBgrToBitmap(rawData, width, height, buffer.Channels, _liveViewImageCache[index]);
                LiveViewImage = _liveViewImageCache[index];
            });

            Interlocked.Exchange(ref _liveViewImageNewestCount, count);
            _liveViewSemaphoreFailCount = 0;

            var now = DateTime.UtcNow;
            Interlocked.Increment(ref _liveViewFrameCount);
            var totalSeconds = (now - _liveViewFpsWindowStart).TotalSeconds;
            if (totalSeconds >= 1)
            {
                var frameCount = Interlocked.Exchange(ref _liveViewFrameCount, 0);
                _liveViewFpsWindowStart = now;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LiveViewFps = frameCount / totalSeconds;
                });
            }
        }
        finally
        {
            buffer?.Dispose();
            _liveViewSemaphore.Release();
        }
    }

    private static WriteableBitmap WriteBgrToBitmap(IntPtr bgrData, int width, int height, int channels, WriteableBitmap? targetBitmap)
    {
        const int dstBytesPerPixel = 4;

        targetBitmap ??= new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using var framebuffer = targetBitmap.Lock();
        unsafe
        {
            var dstStride = framebuffer.RowBytes;
            var dstPtr = (byte*)framebuffer.Address;

            if (channels == 4)
            {
                var srcStride = width * dstBytesPerPixel;
                var rowCopy = Math.Min(srcStride, dstStride);
                var srcPtr = (byte*)bgrData;
                for (var y = 0; y < height; y++)
                {
                    Buffer.MemoryCopy(srcPtr + y * srcStride, dstPtr + y * dstStride, dstStride, rowCopy);
                }

                return targetBitmap;
            }

            if (channels == 3)
            {
                var srcStride = width * 3;
                var rowBuffer = ArrayPool<byte>.Shared.Rent(width * dstBytesPerPixel);
                try
                {
                    var srcPtr = (byte*)bgrData;
                    for (var y = 0; y < height; y++)
                    {
                        var srcRow = srcPtr + y * srcStride;
                        for (var x = 0; x < width; x++)
                        {
                            var srcIndex = x * 3;
                            var dstIndex = x * dstBytesPerPixel;
                            rowBuffer[dstIndex] = srcRow[srcIndex];
                            rowBuffer[dstIndex + 1] = srcRow[srcIndex + 1];
                            rowBuffer[dstIndex + 2] = srcRow[srcIndex + 2];
                            rowBuffer[dstIndex + 3] = 255;
                        }

                        fixed (byte* rowPtr = rowBuffer)
                        {
                            Buffer.MemoryCopy(rowPtr, dstPtr + y * dstStride, dstStride, width * dstBytesPerPixel);
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rowBuffer);
                }

                return targetBitmap;
            }
        }

        return targetBitmap;
    }

    #endregion
    
    #region 配置切换

    /// <summary>
    /// 配置列表（直接引用 ConfigurationManager.Configs，与设置页面共享同一数据源）
    /// </summary>
    public IAvaloniaReadOnlyList<MFAConfiguration> ConfigurationList => ConfigurationManager.Configs;

    /// <summary>
    /// 当前配置名称
    /// </summary>
    public string? CurrentConfiguration
    {
        get => ConfigurationManager.GetCurrentConfiguration();
        set
        {
            if (value != null && value != ConfigurationManager.GetCurrentConfiguration())
            {
                ConfigurationManager.SetDefaultConfig(value);
                Instances.RestartApplication();
            }
        }
    }

    #endregion
}
