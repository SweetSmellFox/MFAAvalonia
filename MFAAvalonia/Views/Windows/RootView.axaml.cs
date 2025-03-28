using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.Views.UserControls;
using Newtonsoft.Json;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MFAAvalonia.Views.Windows;

public partial class RootView : SukiWindow
{
    public RootView()
    {
        Loaded += (_, _) => LoadUI();
        InitializeComponent();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                HandleWindowStateChange();
            }
        };
        MaaProcessor.Instance.InitializeData();
    }

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

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        e.Cancel = !ConfirmExit();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskItems, Instances.TaskQueueViewModel.TaskItemViewModels.ToList().Select(model => model.InterfaceItem));
        MaaProcessor.Instance.SetTasker();
        GlobalHotkeyService.Shutdown();
        base.OnClosed(e);
    }

    public bool ConfirmExit()
    {
        if (!Instances.RootViewModel.IsRunning)
            return true;

        bool result = false;
        // var frame = new DispatcherFrame();
        var textBlock = new TextBlock
        {
            Text = "ConfirmExitText".ToLocalization()
        };
        Instances.DialogManager.CreateDialog()
            .WithTitle("ConfirmExitTitle".ToLocalization())
            .WithContent(textBlock).OfType(NotificationType.Warning)
            .WithActionButton("Yes".ToLocalization(), _ =>
            {
                result = true;
                Instances.ApplicationLifetime.Shutdown();
            }, dismissOnClick: true, "Flat", "Accent")
            .WithActionButton("No".ToLocalization(), _ =>
            {
                result = false;
            }, dismissOnClick: true).TryShow();
        return result;
    }

    private void ToggleWindowTopMost(object sender, RoutedEventArgs e)
    {
        Topmost = btnPin.IsChecked == true;
    }

    public static void AddLogByColor(string content,
        string brush = "Gray",
        string weight = "Regular",
        bool showTime = true)
        =>
            Instances.TaskQueueViewModel.AddLog(content, brush, weight, showTime);


    public static void AddLog(string content,
        IBrush? brush = null,
        string weight = "Regular",
        bool showTime = true)
        =>
            Instances.TaskQueueViewModel.AddLog(content, brush, weight, showTime);

    public static void AddLogByKey(string key, IBrush? brush = null, bool transformKey = true, params string[] formatArgsKeys)
        => Instances.TaskQueueViewModel.AddLogByKey(key, brush, transformKey, formatArgsKeys);

#pragma warning  disable CS4014 // 由于此调用不会等待，因此在此调用完成之前将会继续执行当前方法。请考虑将 "await" 运算符应用于调用结果。
    public void LoadUI()
    {
        
        DispatcherHelper.RunOnMainThread(async () =>
        {
            await Task.Delay(300);
            Instances.TaskQueueViewModel.CurrentController = (MaaProcessor.Interface?.Controller?.FirstOrDefault()?.Type).ToMaaControllerTypes(Instances.TaskQueueViewModel.CurrentController);
            if (!Convert.ToBoolean(GlobalConfiguration.GetValue(ConfigurationKeys.NoAutoStart, bool.FalseString))
                && ConfigurationManager.Current.GetValue(ConfigurationKeys.BeforeTask, "None").Contains("Startup", StringComparison.OrdinalIgnoreCase))
            {
                MaaProcessor.Instance.TaskQueue.Enqueue(new MFATask
                {
                    Name = "启动前",
                    Type = MFATask.MFATaskType.MFA,
                    Action = async () => await MaaProcessor.Instance.WaitSoftware(),
                });
                MaaProcessor.Instance.Start(!ConfigurationManager.Current.GetValue(ConfigurationKeys.BeforeTask, "None").Contains("And", StringComparison.OrdinalIgnoreCase), checkUpdate: true);
            }
            else
            {
                var isAdb = Instances.TaskQueueViewModel.CurrentController == MaaControllerTypes.Adb;

                AddLogByKey("ConnectingTo", null, true, isAdb ? "Emulator" : "Window");

                Instances.TaskQueueViewModel.TryReadAdbDeviceFromConfig();
                MaaProcessor.Instance.TaskQueue.Enqueue(new MFATask
                {
                    Name = "连接检测",
                    Type = MFATask.MFATaskType.MFA,
                    Action = async () => await MaaProcessor.Instance.TestConnecting(),
                });
                MaaProcessor.Instance.Start(true, checkUpdate: true);
            }
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
            GlobalConfiguration.SetValue(ConfigurationKeys.NoAutoStart, bool.FalseString);

            Instances.RootViewModel.LockController = (MaaProcessor.Interface?.Controller?.Count ?? 0) < 2;
            ConfigurationManager.Current.SetValue(ConfigurationKeys.EnableEdit, ConfigurationManager.Current.GetValue(ConfigurationKeys.EnableEdit, false));
            foreach (var task in Instances.TaskQueueViewModel.TaskItemViewModels)
            {
                if (task.InterfaceItem?.Option is { Count: > 0 } || task.InterfaceItem?.Document != null || task.InterfaceItem?.Repeatable == true)
                {
                    task.EnableSetting = true;
                    break;
                }
            }
            if (!string.IsNullOrWhiteSpace(MaaProcessor.Interface?.Message))
            {
                ToastHelper.Info(MaaProcessor.Interface.Message);
            }

        });

        TaskManager.RunTaskAsync(async () =>
        {
            await Task.Delay(1000);
            DispatcherHelper.RunOnMainThread(() =>
            {
                Instances.AnnouncementViewModel.CheckAnnouncement();
                if (ConfigurationManager.Current.GetValue(ConfigurationKeys.AutoMinimize, false))
                {
                    WindowState = WindowState.Minimized;
                }
                if (ConfigurationManager.Current.GetValue(ConfigurationKeys.AutoHide, false))
                {
                    Hide();
                }
            });
        });
    }

    public void ClearTasks(Action? action = null)
    {
        DispatcherHelper.RunOnMainThread(() =>
        {
            Instances.TaskQueueViewModel.TaskItemViewModels = new();
            action?.Invoke();
        });
    }
}
