using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Other;
using MFAAvalonia.ViewModels.Pages;
using SukiUI.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace MFAAvalonia.ViewModels.UsersControls.Settings;

/// <summary>
/// 全局启动设置 - 同时管理所有实例的模拟器启动
/// </summary>
public partial class GlobalStartSettingsUserControlModel : ViewModelBase
{
    [ObservableProperty]
    private bool _globalStartEnabled = GlobalConfiguration.GetValue(ConfigurationKeys.GlobalStartEnabled, bool.FalseString) == bool.TrueString;

    partial void OnGlobalStartEnabledChanged(bool value)
    {
        GlobalConfiguration.SetValue(ConfigurationKeys.GlobalStartEnabled, value.ToString());
        // 开关切换时也保存条目，防止丢失
        if (EmulatorEntries.Count > 0)
            SaveEmulatorEntries();
    }

    [ObservableProperty]
    private double _globalWaitSoftwareTime = double.TryParse(GlobalConfiguration.GetValue(ConfigurationKeys.GlobalWaitSoftwareTime, "60"), out var val) ? val : 60.0;

    partial void OnGlobalWaitSoftwareTimeChanged(double value)
    {
        GlobalConfiguration.SetValue(ConfigurationKeys.GlobalWaitSoftwareTime, value.ToString());
    }

    /// <summary>
    /// 所有实例的启动配置列表
    /// </summary>
    public ObservableCollection<EmulatorStartEntry> EmulatorEntries { get; } = new();

    protected override void Initialize()
    {
        base.Initialize();
        LoadEmulatorEntries();
    }

    private void LoadEmulatorEntries()
    {
        // 如果内存中已有数据，不重新加载（防止开关切换丢失条目）
        if (EmulatorEntries.Count > 0)
            return;

        // 从 GlobalConfiguration 加载已保存的条目
        var countStr = GlobalConfiguration.GetValue("GlobalEmulatorCount", "0");
        if (int.TryParse(countStr, out var count) && count > 0)
        {
            for (int i = 0; i < count; i++)
            {
                var path = GlobalConfiguration.GetValue($"GlobalEmulator_{i}_Path", string.Empty);
                var args = GlobalConfiguration.GetValue($"GlobalEmulator_{i}_Args", string.Empty);
                var name = GlobalConfiguration.GetValue($"GlobalEmulator_{i}_Name", $"模拟器 {i + 1}");
                EmulatorEntries.Add(new EmulatorStartEntry(this, i) { Name = name, SoftwarePath = path, EmulatorConfig = args });
            }
        }
        else
        {
            // 首次使用，添加默认3个条目
            EmulatorEntries.Add(new EmulatorStartEntry(this, 0) { Name = "配置 1", SoftwarePath = string.Empty, EmulatorConfig = "-v 0" });
            EmulatorEntries.Add(new EmulatorStartEntry(this, 1) { Name = "配置 2", SoftwarePath = string.Empty, EmulatorConfig = "-v 1" });
            EmulatorEntries.Add(new EmulatorStartEntry(this, 2) { Name = "配置 3", SoftwarePath = string.Empty, EmulatorConfig = "-v 2" });
            SaveEmulatorEntries();
        }
    }

    public void SaveEmulatorEntries()
    {
        GlobalConfiguration.SetValue("GlobalEmulatorCount", EmulatorEntries.Count.ToString());
        for (int i = 0; i < EmulatorEntries.Count; i++)
        {
            GlobalConfiguration.SetValue($"GlobalEmulator_{i}_Path", EmulatorEntries[i].SoftwarePath);
            GlobalConfiguration.SetValue($"GlobalEmulator_{i}_Args", EmulatorEntries[i].EmulatorConfig);
            GlobalConfiguration.SetValue($"GlobalEmulator_{i}_Name", EmulatorEntries[i].Name);
        }
    }

    [RelayCommand]
    private void AddEmulator()
    {
        EmulatorEntries.Add(new EmulatorStartEntry(this, EmulatorEntries.Count) { Name = $"模拟器 {EmulatorEntries.Count + 1}" });
        SaveEmulatorEntries();
    }

    public void RemoveEmulator(EmulatorStartEntry entry)
    {
        EmulatorEntries.Remove(entry);
        // 重新编号
        for (int i = 0; i < EmulatorEntries.Count; i++)
            EmulatorEntries[i].Index = i;
        SaveEmulatorEntries();
    }

    [RelayCommand]
    private async Task StartAllEmulators()
    {
        if (!GlobalStartEnabled)
        {
            ToastHelper.Warn("提示", "请先启用全局配置");
            return;
        }

        var manager = MaaProcessorManager.Instance;
        var allInstances = manager.GetAllInstanceIdsAndNames().ToList();

        LoggerHelper.Info($"全局启动：准备启动 {EmulatorEntries.Count} 个模拟器");

        // 并行启动所有模拟器
        var tasks = new List<Task>();
        foreach (var entry in EmulatorEntries)
        {
            if (string.IsNullOrWhiteSpace(entry.SoftwarePath)) continue;
            tasks.Add(Task.Run(() => StartSingleEmulator(entry)));
        }

        await Task.WhenAll(tasks);

        // 等待模拟器启动完成
        LoggerHelper.Info($"全局启动：等待模拟器启动 {GlobalWaitSoftwareTime} 秒...");
        await Task.Delay(TimeSpan.FromSeconds(GlobalWaitSoftwareTime));

        // 启动所有实例的任务
        LoggerHelper.Info("全局启动：开始执行所有实例任务");
        for (int i = 0; i < Math.Min(EmulatorEntries.Count, allInstances.Count); i++)
        {
            var instanceId = allInstances[i].Id;
            manager.EnsureInstanceLoaded(instanceId);
            var vm = manager.GetViewModel(instanceId);
            if (vm != null && !vm.IsRunning)
            {
                var idx = i;
                DispatcherHelper.RunOnMainThread(() =>
                {
                    Instances.InstanceTabBarViewModel.SwitchToInstanceById(allInstances[idx].Id);
                    vm.StartTask();
                });
                await Task.Delay(3000); // 间隔3秒启动下一个
            }
        }

        LoggerHelper.Info("全局启动：所有实例已启动");
    }

    private static void StartSingleEmulator(EmulatorStartEntry entry)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(entry.SoftwarePath)) return;
            if (!System.IO.File.Exists(entry.SoftwarePath))
            {
                LoggerHelper.Warning($"全局启动：模拟器路径不存在: {entry.SoftwarePath}");
                return;
            }

            LoggerHelper.Info($"全局启动：启动 {entry.Name} - {entry.SoftwarePath} {entry.EmulatorConfig}");

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = entry.SoftwarePath,
                UseShellExecute = true,
                CreateNoWindow = false
            };

            if (!string.IsNullOrWhiteSpace(entry.EmulatorConfig))
                startInfo.Arguments = entry.EmulatorConfig;

            System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"全局启动：启动 {entry.Name} 失败: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// 单个模拟器启动配置条目
/// </summary>
public partial class EmulatorStartEntry : ObservableObject
{
    private readonly GlobalStartSettingsUserControlModel _parent;

    public EmulatorStartEntry(GlobalStartSettingsUserControlModel parent, int index)
    {
        _parent = parent;
        Index = index;
    }

    public int Index { get; set; }

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _softwarePath = string.Empty;
    [ObservableProperty] private string _emulatorConfig = string.Empty;

    partial void OnNameChanged(string value) => _parent.SaveEmulatorEntries();
    partial void OnSoftwarePathChanged(string value) => _parent.SaveEmulatorEntries();
    partial void OnEmulatorConfigChanged(string value) => _parent.SaveEmulatorEntries();

    [RelayCommand]
    private async Task BrowsePath()
    {
        var storageProvider = Instances.StorageProvider;
        if (storageProvider == null) return;

        var options = new FilePickerOpenOptions
        {
            Title = "选择模拟器可执行文件",
            FileTypeFilter = [new FilePickerFileType("可执行文件") { Patterns = ["*.exe", "*"] }]
        };

        var result = await storageProvider.OpenFilePickerAsync(options);
        if (result is { Count: > 0 } && result[0].TryGetLocalPath() is { } path)
            SoftwarePath = path;
    }

    [RelayCommand]
    private void Remove() => _parent.RemoveEmulator(this);
}
