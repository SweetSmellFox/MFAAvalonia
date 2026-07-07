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
        var countStr = GlobalConfiguration.GetValue(ConfigurationKeys.GlobalEmulatorCount, "0");
        if (int.TryParse(countStr, out var count) && count > 0)
        {
            for (int i = 0; i < count; i++)
            {
                var path = GlobalConfiguration.GetValue(string.Format(ConfigurationKeys.GlobalEmulatorPathKeyFormat, i), string.Empty);
                var args = GlobalConfiguration.GetValue(string.Format(ConfigurationKeys.GlobalEmulatorArgsKeyFormat, i), string.Empty);
                var name = GlobalConfiguration.GetValue(string.Format(ConfigurationKeys.GlobalEmulatorNameKeyFormat, i), $"模拟器 {i + 1}");
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
        GlobalConfiguration.SetValue(ConfigurationKeys.GlobalEmulatorCount, EmulatorEntries.Count.ToString());
        for (int i = 0; i < EmulatorEntries.Count; i++)
        {
            GlobalConfiguration.SetValue(string.Format(ConfigurationKeys.GlobalEmulatorPathKeyFormat, i), EmulatorEntries[i].SoftwarePath);
            GlobalConfiguration.SetValue(string.Format(ConfigurationKeys.GlobalEmulatorArgsKeyFormat, i), EmulatorEntries[i].EmulatorConfig);
            GlobalConfiguration.SetValue(string.Format(ConfigurationKeys.GlobalEmulatorNameKeyFormat, i), EmulatorEntries[i].Name);
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

        await GlobalStartManager.StartAllAndRunTasksManual(GlobalWaitSoftwareTime, EmulatorEntries);
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
