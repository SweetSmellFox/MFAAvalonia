using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaaFramework.Binding;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.Converters;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.ViewModels.Other;
using MFAAvalonia.ViewModels.UsersControls;
using MFAAvalonia.ViewModels.UsersControls.Settings;
using MFAAvalonia.Views.Windows;
using Newtonsoft.Json;
using SukiUI.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.ViewModels.Pages;

public partial class TaskQueueViewModel : ViewModelBase
{
    private string adbKey = LangKeys.TabADB;
    private string win32Key = LangKeys.TabWin32;
    private string adbFallback = "";
    private string win32Fallback = "";
    private string? adbIconKey = null;
    private string? win32IconKey = null;

    private void UpdateControllerName()
    {
        Adb = adbKey == LangKeys.TabADB ? adbKey.ToLocalization() : LanguageHelper.GetLocalizedDisplayName(adbKey, adbFallback);
        Win32 = win32Key == LangKeys.TabWin32 ? win32Key.ToLocalization() : LanguageHelper.GetLocalizedDisplayName(win32Key, win32Fallback);
        UpdateControllerIcon();
    }

    private void UpdateControllerIcon()
    {
        // 处理 Adb 图标
        if (!string.IsNullOrWhiteSpace(adbIconKey))
        {
            var iconValue = LanguageHelper.GetLocalizedString(adbIconKey);
            AdbIcon = MaaInterface.ReplacePlaceholder(iconValue, MaaProcessor.ResourceBase, true);
            HasAdbIcon = !string.IsNullOrWhiteSpace(AdbIcon);
        }
        else
        {
            AdbIcon = null;
            HasAdbIcon = false;
        }

        // 处理 Win32 图标
        if (!string.IsNullOrWhiteSpace(win32IconKey))
        {
            var iconValue = LanguageHelper.GetLocalizedString(win32IconKey);
            Win32Icon = MaaInterface.ReplacePlaceholder(iconValue, MaaProcessor.ResourceBase, true);
            HasWin32Icon = !string.IsNullOrWhiteSpace(Win32Icon);
        }
        else
        {
            Win32Icon = null;
            HasWin32Icon = false;
        }
    }

    public void InitializeControllerName()
    {
        try
        {
            var adb = MaaProcessor.Interface?.Controller?.Find(c => c.Type != null && c.Type.Equals(MaaControllerTypes.Adb.ToJsonKey(), StringComparison.OrdinalIgnoreCase));
            var win32 = MaaProcessor.Interface?.Controller?.Find(c => c.Type != null && c.Type.Equals(MaaControllerTypes.Win32.ToJsonKey(), StringComparison.OrdinalIgnoreCase));

            if (adb is { Label: not null } or { Name: not null })
            {
                adbKey = adb.Label ?? string.Empty;
                adbFallback = adb.Name ?? string.Empty;
            }
            if (win32 is { Label: not null } or { Name: not null })
            {
                win32Key = win32.Label ?? string.Empty;
                win32Fallback = win32.Name ?? string.Empty;
            }

            // 获取图标
            adbIconKey = adb?.Icon;
            win32IconKey = win32?.Icon;

            LanguageHelper.LanguageChanged += (_, _) =>
            {
                UpdateControllerName();
            };
            UpdateControllerName();
        }
        catch (Exception e)
        {
            LoggerHelper.Error(e);
        }
    }


    protected override void Initialize()
    {
        try
        {
            UpdateControllerName();
        }
        catch (Exception e)
        {
            LoggerHelper.Error(e);
        }
        try
        {
            var col1Str = ConfigurationManager.Current.GetValue(ConfigurationKeys.TaskQueueColumn1Width, DefaultColumn1Width);
            var col2Str = ConfigurationManager.Current.GetValue(ConfigurationKeys.TaskQueueColumn2Width, DefaultColumn2Width);
            var col3Str = ConfigurationManager.Current.GetValue(ConfigurationKeys.TaskQueueColumn3Width, DefaultColumn3Width);

            SuppressPropertyChangedCallbacks = true;

            Column1Width = GridLength.Parse(col1Str);
            Column2Width = GridLength.Parse(col2Str);
            Column3Width = GridLength.Parse(col3Str);

            SuppressPropertyChangedCallbacks = false;

            LoggerHelper.Info("Column width set successfully in the constructor");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"Failed to set column width in the constructor: {ex.Message}");
            SetDefaultColumnWidths();
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

    [ObservableProperty] private string _adb = string.Empty;
    [ObservableProperty] private string _win32 = string.Empty;
    [ObservableProperty] private string? _adbIcon;
    [ObservableProperty] private string? _win32Icon;
    [ObservableProperty] private bool _hasAdbIcon;
    [ObservableProperty] private bool _hasWin32Icon;

    [ObservableProperty] private int _shouldShow = 0;
    [ObservableProperty] private ObservableCollection<object> _devices = [];
    [ObservableProperty] private object? _currentDevice;
    private DateTime? _lastExecutionTime;

    partial void OnShouldShowChanged(int value)
    {
        DispatcherHelper.PostOnMainThread(() => Instances.TaskQueueView.UpdateConnectionLayout(true));
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

        // 如果当前选中的资源不在过滤后的列表中，则选择第一个并提示用户
        if (CurrentResources.Count > 0 && CurrentResources.All(r => r.Name != CurrentResource))
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
        }
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


    private CancellationTokenSource? _refreshCancellationTokenSource;

    [RelayCommand]
    private void Refresh()
    {
        _refreshCancellationTokenSource?.Cancel();
        _refreshCancellationTokenSource = new CancellationTokenSource();
        TaskManager.RunTask(() => AutoDetectDevice(_refreshCancellationTokenSource.Token), _refreshCancellationTokenSource.Token, name: "刷新", handleError: (e) => HandleDetectionError(e, CurrentController == MaaControllerTypes.Adb),
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
        var isAdb = CurrentController == MaaControllerTypes.Adb;

        ToastHelper.Info(GetDetectionMessage(isAdb));
        SetConnected(false);
        token.ThrowIfCancellationRequested();
        var (devices, index) = isAdb ? DetectAdbDevices() : DetectWin32Windows();
        token.ThrowIfCancellationRequested();
        UpdateDeviceList(devices, index);
        token.ThrowIfCancellationRequested();
        HandleControllerSettings(isAdb);
        token.ThrowIfCancellationRequested();
        UpdateConnectionStatus(devices.Count > 0, isAdb);

    }

    private string GetDetectionMessage(bool isAdb) =>
        (isAdb ? "EmulatorDetectionStarted" : "WindowDetectionStarted").ToLocalization();

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
        });
    }

    private void HandleControllerSettings(bool isAdb)
    {
        var controller = MaaProcessor.Interface?.Controller?
            .FirstOrDefault(c => c.Type?.Equals(isAdb ? "adb" : "win32", StringComparison.OrdinalIgnoreCase) == true);

        if (controller == null) return;

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

    private void UpdateConnectionStatus(bool hasDevices, bool isAdb)
    {
        if (!hasDevices)
        {
            ToastHelper.Info((
                isAdb ? LangKeys.NoEmulatorFound : LangKeys.NoWindowFound).ToLocalization(), (
                isAdb ? LangKeys.NoEmulatorFoundDetail : "").ToLocalization());

        }
    }

    private void HandleDetectionError(Exception ex, bool isAdb)
    {
        var targetType = isAdb ? LangKeys.Emulator : LangKeys.Window;
        ToastHelper.Warn(string.Format(
            LangKeys.TaskStackError.ToLocalization(),
            targetType.ToLocalization(),
            ex.Message));

        LoggerHelper.Error(ex);
    }

    public void TryReadAdbDeviceFromConfig(bool InTask = true, bool refresh = false)
    {
        if (refresh
            || CurrentController != MaaControllerTypes.Adb
            || !ConfigurationManager.Current.GetValue(ConfigurationKeys.RememberAdb, true)
            || MaaProcessor.Config.AdbDevice.AdbPath != "adb"
            || !ConfigurationManager.Current.TryGetValue(ConfigurationKeys.AdbDevice, out AdbDeviceInfo savedDevice,
                new UniversalEnumConverter<AdbInputMethods>(), new UniversalEnumConverter<AdbScreencapMethods>()))
        {
            _refreshCancellationTokenSource?.Cancel();
            _refreshCancellationTokenSource = new CancellationTokenSource();
            if (InTask)
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
                DispatcherHelper.PostOnMainThread(() =>
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
                if (InTask)
                    TaskManager.RunTask(() => AutoDetectDevice(_refreshCancellationTokenSource.Token), name: "刷新设备");
                else
                    AutoDetectDevice(_refreshCancellationTokenSource.Token);
            }
        }
        else
        {
            // 不使用指纹匹配，直接使用保存的设备信息
            LoggerHelper.Info("Reading saved ADB device from configuration, fingerprint matching disabled.");
            DispatcherHelper.PostOnMainThread(() =>
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

    public string CurrentResource
    {
        get => field;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                MaaProcessor.Instance.SetTasker();
                SetNewProperty(ref field, value);
                HandlePropertyChanged(ConfigurationKeys.Resource, value);
                UpdateTasksForResource(value);
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
    /// </summary>
    private void InitializeResourceSelectOptions(MaaInterface.MaaInterfaceResource resource)
    {
        if (resource.Option == null || resource.Option.Count == 0)
        {
            resource.SelectOptions = null;
            return;
        }

        resource.SelectOptions = resource.Option.Select(optionName =>
        {
            var selectOption = new MaaInterface.MaaInterfaceSelectOption
            {
                Name = optionName
            };
            TaskLoader.SetDefaultOptionValue(MaaProcessor.Interface, selectOption);
            return selectOption;
        }).ToList();
    }

    /// <summary>
    /// 从配置中恢复资源选项的已保存值
    /// </summary>
    private void RestoreResourceOptionValues(MaaInterface.MaaInterfaceResource resource)
    {
        if (resource.SelectOptions == null)
            return;

        var savedResourceOptions = ConfigurationManager.Current.GetValue(
            ConfigurationKeys.ResourceOptionItems,
            new Dictionary<string, List<MaaInterface.MaaInterfaceSelectOption>>());

        if (savedResourceOptions.TryGetValue(resource.Name ?? string.Empty, out var savedOptions) && savedOptions != null)
        {
            var savedDict = savedOptions.ToDictionary(o => o.Name ?? string.Empty);
            foreach (var opt in resource.SelectOptions)
            {
                if (savedDict.TryGetValue(opt.Name ?? string.Empty, out var savedOpt))
                {
                    opt.Index = savedOpt.Index;
                    opt.Data = savedOpt.Data;
                    opt.SubOptions = savedOpt.SubOptions;
                }
            }
        }
    }

    #endregion
    #region 缩放

// 三列宽度配置
    private const string DefaultColumn1Width = "350";
    private const string DefaultColumn2Width = "1*";
    private const string DefaultColumn3Width = "1*";

// 使用属性，标记为可通知属性，确保UI能正确绑定和监听变化
    [ObservableProperty] private GridLength _column1Width;

    [ObservableProperty] private GridLength _column2Width;

    [ObservableProperty] private GridLength _column3Width;

// 添加记录拖拽开始的状态
    private GridLength _dragStartCol1Width;
    private GridLength _dragStartCol2Width;
    private GridLength _dragStartCol3Width;

    [RelayCommand]
    public void GridSplitterDragStarted(string splitterName)
    {
        try
        {
            _dragStartCol1Width = Column1Width;
            _dragStartCol2Width = Column2Width;
            _dragStartCol3Width = Column3Width;
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"记录拖拽开始状态失败: {ex.Message}");
        }
    }

    [RelayCommand]
    public void GridSplitterDragCompleted(string splitterName)
    {
        try
        {
            // 记录拖拽完成事件和时间戳，方便分析日志
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            // 获取当前列宽
            var col1 = Column1Width;
            var col2 = Column2Width;
            var col3 = Column3Width;

            // 检查是否有变化
            bool col1Changed = !AreGridLengthsEqual(_dragStartCol1Width, col1);
            bool col2Changed = !AreGridLengthsEqual(_dragStartCol2Width, col2);
            bool col3Changed = !AreGridLengthsEqual(_dragStartCol3Width, col3);
            bool changed = col1Changed || col2Changed || col3Changed;

            var oldCol1Str = ConfigurationManager.Current.GetValue(ConfigurationKeys.TaskQueueColumn1Width, DefaultColumn1Width);
            var oldCol2Str = ConfigurationManager.Current.GetValue(ConfigurationKeys.TaskQueueColumn2Width, DefaultColumn2Width);
            var oldCol3Str = ConfigurationManager.Current.GetValue(ConfigurationKeys.TaskQueueColumn3Width, DefaultColumn3Width);

            var newCol1Str = col1.ToString();
            var newCol2Str = col2.ToString();
            var newCol3Str = col3.ToString();

            // 始终设置新值
            ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskQueueColumn1Width, newCol1Str);
            ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskQueueColumn2Width, newCol2Str);
            ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskQueueColumn3Width, newCol3Str);

            // 始终保存配置
            ConfigurationManager.SaveConfiguration(ConfigurationManager.Current.FileName);

        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"保存列宽配置失败: {ex.Message}");
        }
    }

// 添加辅助方法用于精确比较两个GridLength
    private bool AreGridLengthsEqual(GridLength a, GridLength b)
    {
        if (a.GridUnitType != b.GridUnitType)
            return false;

        if (a.GridUnitType == GridUnitType.Auto || b.GridUnitType == GridUnitType.Auto)
            return a.GridUnitType == b.GridUnitType;

        // 对于像素值，允许0.5像素的误差
        if (a.GridUnitType == GridUnitType.Pixel)
            return Math.Abs(a.Value - b.Value) < 0.5;

        // 对于Star值，允许0.01的误差
        if (a.GridUnitType == GridUnitType.Star)
            return Math.Abs(a.Value - b.Value) < 0.01;

        return a.Value == b.Value;
    }
    partial void OnColumn1WidthChanged(GridLength value)
    {
        if (SuppressPropertyChangedCallbacks) return;

        try
        {
            // 获取旧值
            var oldValue = ConfigurationManager.Current.GetValue<string>(ConfigurationKeys.TaskQueueColumn1Width, DefaultColumn1Width);
            var newValue = value.ToString();

            // 使用改进的比较方法
            if (CompareGridLength(oldValue, value))
            {
                ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskQueueColumn1Width, newValue);
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"保存列宽1失败: {ex.Message}");
        }
    }
    partial void OnColumn2WidthChanged(GridLength value)
    {
        if (SuppressPropertyChangedCallbacks) return;

        try
        {
            // 获取旧值
            var oldValue = ConfigurationManager.Current.GetValue<string>(ConfigurationKeys.TaskQueueColumn2Width, DefaultColumn2Width);
            var newValue = value.ToString();

            // 使用改进的比较方法
            if (CompareGridLength(oldValue, value))
            {
                ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskQueueColumn2Width, newValue);
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"保存列宽2失败: {ex.Message}");
        }
    }
    partial void OnColumn3WidthChanged(GridLength value)
    {
        if (SuppressPropertyChangedCallbacks) return;

        try
        {
            // 获取旧值
            var oldValue = ConfigurationManager.Current.GetValue<string>(ConfigurationKeys.TaskQueueColumn3Width, DefaultColumn3Width);
            var newValue = value.ToString();

            // 使用改进的比较方法
            if (CompareGridLength(oldValue, value))
            {
                ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskQueueColumn3Width, newValue);
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"保存列宽3失败: {ex.Message}");
        }
    }
    public bool SuppressPropertyChangedCallbacks
    {
        get;
        set;
    }

// 保存列宽配置到磁盘
    public void SaveColumnWidths()
    {
        if (SuppressPropertyChangedCallbacks) return;

        try
        {
            ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskQueueColumn1Width, Column1Width.ToString());
            ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskQueueColumn2Width, Column2Width.ToString());
            ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskQueueColumn3Width, Column3Width.ToString());
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"保存列宽配置失败: {ex.Message}");
        }
    }

    private void SetDefaultColumnWidths()
    {
        // 设置默认值
        try
        {
            SuppressPropertyChangedCallbacks = true;

            Column1Width = GridLength.Parse(DefaultColumn1Width);
            Column2Width = GridLength.Parse(DefaultColumn2Width);
            Column3Width = GridLength.Parse(DefaultColumn3Width);

            // 恢复属性更改通知
            SuppressPropertyChangedCallbacks = false;

            // 默认值需要保存，但只有在第一次启动时(无配置文件)
            if (!ConfigurationManager.Current.Config.ContainsKey(ConfigurationKeys.TaskQueueColumn1Width))
            {
                SaveColumnWidths();
            }

            LoggerHelper.Info("默认列宽设置成功");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"设置默认列宽失败: {ex.Message}");
        }
    }

// 添加辅助方法用于比较GridLength
    private bool CompareGridLength(string storedValue, GridLength newValue)
    {
        // 先检查字符串是否完全相同
        var newValueStr = newValue.ToString();
        if (string.Equals(storedValue, newValueStr, StringComparison.OrdinalIgnoreCase))
        {
            return false; // 字符串相同，没有变化
        }

        try
        {
            // 尝试解析存储的值
            var storedGridLength = GridLength.Parse(storedValue);

            // 对于像素值，比较数值是否有足够的差异
            if (storedGridLength.GridUnitType == GridUnitType.Pixel && newValue.GridUnitType == GridUnitType.Pixel)
            {
                // 如果差异小于0.5像素，认为没有变化
                return Math.Abs(storedGridLength.Value - newValue.Value) >= 0.5;
            }

            // 对于Star类型，比较是否有足够的差异
            if (storedGridLength.GridUnitType == GridUnitType.Star && newValue.GridUnitType == GridUnitType.Star)
            {
                // 对于比例值，如果差异小于0.01，认为没有变化
                return Math.Abs(storedGridLength.Value - newValue.Value) >= 0.01;
            }

            // 单位类型不同或其他情况，认为有变化
            return true;
        }
        catch
        {
            // 如果解析失败，认为有变化
            return true;
        }
    }

    #endregion
}
