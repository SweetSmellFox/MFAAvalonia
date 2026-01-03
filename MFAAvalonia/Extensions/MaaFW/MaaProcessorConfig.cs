using MaaFramework.Binding;

namespace MFAAvalonia.Extensions.MaaFW;

/// <summary>
/// MaaFW 配置类
/// </summary>
public class MaaFWConfiguration
{
    public AdbDeviceCoreConfig AdbDevice { get; set; } = new();
    public DesktopWindowCoreConfig DesktopWindow { get; set; } = new();
}

/// <summary>
/// 桌面窗口核心配置
/// </summary>
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

/// <summary>
/// ADB 设备核心配置
/// </summary>
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