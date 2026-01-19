#nullable enable
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace MFAAvalonia.Helper;

public static class EmulatorHelper
{
    [DllImport("User32.dll", EntryPoint = "FindWindow")]
    [SupportedOSPlatform("windows")]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("User32.dll", CharSet = CharSet.Auto)]
    [SupportedOSPlatform("windows")]
    private static extern int GetWindowThreadProcessId(IntPtr hwnd, out int id);

    /// <summary>
    /// 一个根据连接配置判断使用关闭模拟器的方式的方法
    /// </summary>
    /// <returns>是否关闭成功</returns>
    public static bool KillEmulatorModeSwitcher()
    {
        try
        {
            string emulatorMode = "None";
            var windowName = MaaProcessor.Instance.Config.AdbDevice.Name;
            if (windowName.Contains("MuMuPlayer12") || windowName.Contains("MuMu"))
                emulatorMode = "MuMuEmulator12";
            else if (windowName.Contains("Nox"))
                emulatorMode = "Nox";
            else if (windowName.Contains("LDPlayer") || windowName.Contains("雷电"))
                emulatorMode = "LDPlayer";
            else if (windowName.Contains("XYAZ"))
                emulatorMode = "XYAZ";
            else if (windowName.Contains("BlueStacks"))
                emulatorMode = "BlueStacks";

            LoggerHelper.Info($"Emulator mode detected: {emulatorMode}");

            return emulatorMode switch
            {
                "Nox" => KillEmulatorNox(),
                "LDPlayer" => KillEmulatorLdPlayer(),
                "XYAZ" => KillEmulatorXyaz(),
                "BlueStacks" => KillEmulatorBlueStacks(),
                "MuMuEmulator12" => KillEmulatorMuMuEmulator12(),
                _ => KillEmulatorByWindow(),
            };
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"Failed to close emulator: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// 一个用于调用 MuMu12 模拟器控制台关闭 MuMu12 的方法
    /// </summary>
    /// <returns>是否关闭成功</returns>
    private static bool KillEmulatorMuMuEmulator12()
    {
        string address = MaaProcessor.Instance.Config.AdbDevice.AdbSerial;
        int emuIndex;
        if (address == "127.0.0.1:16384")
        {
            emuIndex = 0;
        }
        else if (address.Contains(':'))
        {
            string portStr = address.Split(':')[1];
            if (int.TryParse(portStr, out int port))
            {
                switch (port)
                {
                    case >= 16384:
                        emuIndex = (port - 16384) / 32;
                        break;
                    case 7555:
                        emuIndex = 0;
                        LoggerHelper.Warning("Port 7555 is deprecated for MuMu6, please use 16384 or above.");
                        break;
                    case >= 5555:
                        emuIndex = (port - 5555) / 2;
                        break;
                    default:
                        LoggerHelper.Error($"Port {port} is not valid for MuMuEmulator12");
                        return false;
                }
            }
            else
            {
                LoggerHelper.Error($"Failed to parse port from address {address}");
                return false;
            }
        }
        else if (address.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = address.Split('-');
            if (parts.Length >= 2 && int.TryParse(parts[1], out int port))
            {
                emuIndex = (port - 5554) / 2;
            }
            else
            {
                LoggerHelper.Error($"Failed to parse port from emulator style address {address}");
                return false;
            }
        }
        else
        {
            LoggerHelper.Error($"Unsupported address format: {address}");
            return false;
        }

        // 尝试找到正在运行的模拟器进程
        Process[] processes = Process.GetProcessesByName("MuMuNxDevice"); // 新版
        if (processes.Length == 0)
        {
            processes = Process.GetProcessesByName("MuMuPlayer"); // 兼容旧版
        }

        if (processes.Length == 0)
        {
            return false;
        }

        ProcessModule? processModule;
        try
        {
            processModule = processes[0].MainModule;
        }
        catch (Exception e)
        {
            LoggerHelper.Error("Failed to get the main module of the emulator process.");
            LoggerHelper.Error(e.Message);
            return false;
        }

        string? emulatorExePath = processModule?.FileName;
        if (emulatorExePath == null)
        {
            return false;
        }

        // 从 exe 路径回推安装目录
        // 新版路径推导: nx_device\12.0\shell\MuMuNxDevice.exe → 上三级目录 = 安装目录
        // 旧版路径推导: shell\MuMuPlayer.exe → 上一级目录 = 安装目录
        var installPath = Path.GetFullPath(Path.GetFileName(emulatorExePath).Equals("MuMuNxDevice.exe", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(Path.GetDirectoryName(emulatorExePath)!, "..", "..", "..")
            : Path.Combine(Path.GetDirectoryName(emulatorExePath)!, ".."));

        // 新旧路径分别尝试 MuMuManager.exe
        string newConsolePath = Path.Combine(installPath, "nx_main", "MuMuManager.exe");
        string oldConsolePath = Path.Combine(installPath, "shell", "MuMuManager.exe");

        string? consolePath = null;
        if (File.Exists(newConsolePath))
        {
            consolePath = newConsolePath;
        }
        else if (File.Exists(oldConsolePath))
        {
            consolePath = oldConsolePath;
        }

        if (consolePath != null)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(consolePath)
            {
                Arguments = $"api -v {emuIndex} shutdown_player",
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            var process = Process.Start(startInfo);
            if (process != null && process.WaitForExit(5000))
            {
                LoggerHelper.Info($"Emulator at index {emuIndex} closed through console. Console path: {consolePath}");
                return true;
            }

            LoggerHelper.Warning($"Console process at index {emuIndex} did not exit within the specified timeout. Killing emulator by window. Console path: {consolePath}");
            return KillEmulatorByWindow();
        }

        LoggerHelper.Error("MuMuManager.exe not found in expected locations (new or old). Trying to kill emulator by window.");
        return KillEmulatorByWindow();
    }

    /// <summary>
    /// 一个用于调用雷电模拟器控制台关闭雷电模拟器的方法
    /// </summary>
    /// <returns>是否关闭成功</returns>
    private static bool KillEmulatorLdPlayer()
    {
        string address = MaaProcessor.Instance.Config.AdbDevice.AdbSerial;
        int emuIndex;
        if (address.Contains(':'))
        {
            string portStr = address.Split(':')[1];
            if (!int.TryParse(portStr, out int port))
            {
                LoggerHelper.Error($"Failed to parse port from address {address}");
                return false;
            }
            emuIndex = (port - 5555) / 2;
        }
        else if (address.Contains('-'))
        {
            string portStr = address.Split('-')[1];
            if (!int.TryParse(portStr, out int port))
            {
                LoggerHelper.Error($"Failed to parse port from address {address}");
                return false;
            }
            emuIndex = (port - 5554) / 2;
        }
        else
        {
            LoggerHelper.Error($"Unsupported address format: {address}");
            return false;
        }

        Process[] processes = Process.GetProcessesByName("dnplayer");
        if (processes.Length <= 0)
        {
            return false;
        }

        ProcessModule? processModule;
        try
        {
            processModule = processes[0].MainModule;
        }
        catch (Exception e)
        {
            LoggerHelper.Error("Failed to get the main module of the emulator process.");
            LoggerHelper.Error(e.Message);
            return false;
        }

        if (processModule == null)
        {
            return false;
        }

        string? emuLocation = processModule.FileName;
        emuLocation = Path.GetDirectoryName(emuLocation);
        if (emuLocation == null)
        {
            return false;
        }

        string consolePath = Path.Combine(emuLocation, "ldconsole.exe");

        if (File.Exists(consolePath))
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(consolePath)
            {
                Arguments = $"quit --index {emuIndex}",
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            var process = Process.Start(startInfo);
            if (process != null && process.WaitForExit(5000))
            {
                LoggerHelper.Info($"Emulator at index {emuIndex} closed through console. Console path: {consolePath}");
                return true;
            }

            LoggerHelper.Warning($"Console process at index {emuIndex} did not exit within the specified timeout. Killing emulator by window. Console path: {consolePath}");
            return KillEmulatorByWindow();
        }

        LoggerHelper.Error($"`{consolePath}` not found, try to kill emulator by window.");
        return KillEmulatorByWindow();
    }

    /// <summary>
    /// 一个用于调用夜神模拟器控制台关闭夜神模拟器的方法
    /// </summary>
    /// <returns>是否关闭成功</returns>
    private static bool KillEmulatorNox()
    {
        string address = MaaProcessor.Instance.Config.AdbDevice.AdbSerial;
        int emuIndex;
        if (address == "127.0.0.1:62001")
        {
            emuIndex = 0;
        }
        else if (address.Contains(':'))
        {
            string portStr = address.Split(':')[1];
            if (!int.TryParse(portStr, out int port))
            {
                LoggerHelper.Error($"Failed to parse port from address {address}");
                return false;
            }
            emuIndex = port - 62024;
        }
        else
        {
            LoggerHelper.Error($"Unsupported address format: {address}");
            return false;
        }

        Process[] processes = Process.GetProcessesByName("Nox");
        if (processes.Length <= 0)
        {
            return false;
        }

        ProcessModule? processModule;
        try
        {
            processModule = processes[0].MainModule;
        }
        catch (Exception e)
        {
            LoggerHelper.Error("Failed to get the main module of the emulator process.");
            LoggerHelper.Error(e.Message);
            return false;
        }

        if (processModule == null)
        {
            return false;
        }

        string? emuLocation = processModule.FileName;
        emuLocation = Path.GetDirectoryName(emuLocation);
        if (emuLocation == null)
        {
            return false;
        }

        string consolePath = Path.Combine(emuLocation, "NoxConsole.exe");

        if (File.Exists(consolePath))
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(consolePath)
            {
                Arguments = $"quit -index:{emuIndex}",
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            var process = Process.Start(startInfo);
            if (process != null && process.WaitForExit(5000))
            {
                LoggerHelper.Info($"Emulator at index {emuIndex} closed through console. Console path: {consolePath}");
                return true;
            }

            LoggerHelper.Warning($"Console process at index {emuIndex} did not exit within the specified timeout. Killing emulator by window. Console path: {consolePath}");
            return KillEmulatorByWindow();
        }

        LoggerHelper.Error($"`{consolePath}` not found, try to kill emulator by window.");
        return KillEmulatorByWindow();
    }

    /// <summary>
    /// 一个用于调用逍遥模拟器控制台关闭逍遥模拟器的方法
    /// </summary>
    /// <returns>是否关闭成功</returns>
    private static bool KillEmulatorXyaz()
    {
        string address = MaaProcessor.Instance.Config.AdbDevice.AdbSerial;
        if (!address.Contains(':'))
        {
            LoggerHelper.Error($"Unsupported address format: {address}");
            return false;
        }

        string portStr = address.Split(':')[1];
        if (!int.TryParse(portStr, out int port))
        {
            LoggerHelper.Error($"Failed to parse port from address {address}");
            return false;
        }
        var emuIndex = (port - 21503) / 10;

        Process[] processes = Process.GetProcessesByName("MEmu");
        if (processes.Length <= 0)
        {
            return false;
        }

        ProcessModule? processModule;
        try
        {
            processModule = processes[0].MainModule;
        }
        catch (Exception e)
        {
            LoggerHelper.Error("Failed to get the main module of the emulator process.");
            LoggerHelper.Error(e.Message);
            return false;
        }

        if (processModule == null)
        {
            return false;
        }

        string? emuLocation = processModule.FileName;
        emuLocation = Path.GetDirectoryName(emuLocation);
        if (emuLocation == null)
        {
            return false;
        }

        string consolePath = Path.Combine(emuLocation, "memuc.exe");

        if (File.Exists(consolePath))
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(consolePath)
            {
                Arguments = $"stop -i {emuIndex}",
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            var process = Process.Start(startInfo);
            if (process != null && process.WaitForExit(5000))
            {
                LoggerHelper.Info($"Emulator at index {emuIndex} closed through console. Console path: {consolePath}");
                return true;
            }

            LoggerHelper.Warning($"Console process at index {emuIndex} did not exit within the specified timeout. Killing emulator by window. Console path: {consolePath}");
            return KillEmulatorByWindow();
        }

        LoggerHelper.Error($"`{consolePath}` not found, try to kill emulator by window.");
        return KillEmulatorByWindow();
    }

    /// <summary>
    /// 一个用于关闭蓝叠模拟器的方法
    /// </summary>
    /// <returns>是否关闭成功</returns>
    private static bool KillEmulatorBlueStacks()
    {
        Process[] processes = Process.GetProcessesByName("HD-Player");
        if (processes.Length <= 0)
        {
            return false;
        }

        ProcessModule? processModule;
        try
        {
            processModule = processes[0].MainModule;
        }
        catch (Exception e)
        {
            LoggerHelper.Error("Failed to get the main module of the emulator process.");
            LoggerHelper.Error(e.Message);
            return false;
        }

        if (processModule == null)
        {
            return false;
        }

        string? emuLocation = processModule.FileName;
        emuLocation = Path.GetDirectoryName(emuLocation);
        if (emuLocation == null)
        {
            return false;
        }

        string consolePath = Path.Combine(emuLocation, "bsconsole.exe");

        if (File.Exists(consolePath))
        {
            LoggerHelper.Info($"`{consolePath}` has been found. This may be the BlueStacks China emulator, try to kill the emulator by window.");
            return KillEmulatorByWindow();
        }

        LoggerHelper.Info($"`{consolePath}` not found. This may be the BlueStacks International emulator, try to kill the emulator by the port.");
        if (KillEmulator())
        {
            return true;
        }

        LoggerHelper.Info("Failed to kill emulator by the port, try to kill emulator process with PID.");

        if (processes.Length > 1)
        {
            LoggerHelper.Warning("The number of elements in processes exceeds one, abort closing the emulator");
            return false;
        }

        try
        {
            processes[0].Kill();
            return processes[0].WaitForExit(20000);
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"Failed to kill emulator process with PID {processes[0].Id}. Exception: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Kills emulator by Window hwnd.
    /// </summary>
    /// <returns>Whether the operation is successful.</returns>
    private static bool KillEmulatorByWindow()
    {
        int pid = 0;
        var windowNames = new[]
        {
            "明日方舟",
            "明日方舟 - MuMu模拟器",
            "BlueStacks App Player",
            "BlueStacks",
            "Google Play Games on PC Emulator"
        };

        if (OperatingSystem.IsWindows())
        {
            foreach (string windowName in windowNames)
            {
                var hwnd = FindWindow(null, windowName);
                if (hwnd == IntPtr.Zero)
                {
                    continue;
                }

                GetWindowThreadProcessId(hwnd, out pid);
                break;
            }
        }
        else
        {
            // Linux/macOS: 尝试通过进程名查找
            var processNames = new[]
            {
                "MuMuPlayer",
                "Nox",
                "dnplayer",
                "MEmu",
                "HD-Player"
            };
            foreach (var processName in processNames)
            {
                var processes = Process.GetProcessesByName(processName);
                if (processes.Length > 0)
                {
                    pid = processes[0].Id;
                    break;
                }
            }
        }

        if (pid == 0)
        {
            return KillEmulator();
        }

        try
        {
            var emulator = Process.GetProcessById(pid);
            emulator.CloseMainWindow();
            if (!emulator.WaitForExit(5000))
            {
                emulator.Kill();
                if (emulator.WaitForExit(5000))
                {
                    LoggerHelper.Info($"Emulator with process ID {pid} killed successfully.");
                    KillEmulator();
                    return true;
                }

                LoggerHelper.Error($"Failed to kill emulator with process ID {pid}.");
                return false;
            }

            // 尽管已经成功 CloseMainWindow()，再次尝试 killEmulator()
            // Refer to https://github.com/MaaAssistantArknights/MaaAssistantArknights/pull/1878
            KillEmulator();

            // 已经成功 CloseMainWindow()，所以不管 killEmulator() 的结果如何，都返回 true
            return true;
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"Kill emulator by window error: {ex.Message}");
        }

        return KillEmulator();
    }

    /// <summary>
    /// Kills emulator.
    /// </summary>
    /// <returns>Whether the operation is successful.</returns>
    public static bool KillEmulator()
    {
        //  int pid = 0;
        string address = MaaProcessor.Instance.Config.AdbDevice.AdbSerial;
        var port = address.StartsWith("127") && address.Length > 10 ? address[10..] : "5555";
        LoggerHelper.Info($"address: {address}, port: {port}");

        if (OperatingSystem.IsWindows())
        {
            return KillEmulatorWindows(address, port);
        }
        else if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            return KillEmulatorUnix(port);
        }
        return false;
    }

    /// <summary>
    /// Windows 平台关闭模拟器
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static bool KillEmulatorWindows(string address, string port)
    {
        int pid = 0;
        string portCmd = $"netstat -ano|findstr \"{port}\"";
        Process checkCmd = new Process
        {
            StartInfo =
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };

        try
        {
            checkCmd.Start();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"Failed to start cmd.exe: {ex.Message}");
            checkCmd.Close();
            return false;
        }

        checkCmd.StandardInput.WriteLine(portCmd);
        checkCmd.StandardInput.WriteLine("exit");
        Regex reg = new Regex("\\s+", RegexOptions.Compiled);

        while (true)
        {
            var line = checkCmd.StandardOutput.ReadLine();
            line = line?.Trim();

            if (line == null)
            {
                break;
            }

            if (!line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            line = reg.Replace(line, ",");

            try
            {
                string[] arr = line.Split(',');
                if (arr.Length >= 2
                    && Convert.ToBoolean(string.Compare(arr[1], address, StringComparison.Ordinal)))
                {
                    continue;
                }

                pid = int.Parse(arr[4]);
                break;
            }
            catch (Exception e)
            {
                LoggerHelper.Error($"Failed to parse cmd.exe output: {e.Message}");
            }
        }

        checkCmd.Close();

        if (pid == 0)
        {
            LoggerHelper.Error("Failed to get emulator PID");
            return false;
        }

        try
        {
            Process emulator = Process.GetProcessById(pid);
            emulator.Kill();
            return true;
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"Failed to kill emulator process: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Linux/macOS 平台关闭模拟器
    /// </summary>
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static bool KillEmulatorUnix(string port)
    {
        int pid = 0;
        string portCmd = $"lsof -i :{port} | grep LISTEN | awk '{{print $2}}' | head -n 1";

        Process checkCmd = new Process
        {
            StartInfo =
            {
                FileName = "/bin/bash",
                Arguments = "-c",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };

        try
        {
            checkCmd.Start();
            checkCmd.StandardInput.WriteLine(portCmd);
            checkCmd.StandardInput.WriteLine("exit");

            var output = checkCmd.StandardOutput.ReadToEnd().Trim();
            if (int.TryParse(output, out var result))
            {
                pid = result;
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"Process error: {ex.Message}");
            return false;
        }
        finally
        {
            checkCmd.Close();
        }

        if (pid == 0)
        {
            LoggerHelper.Error("Failed to get emulator PID");
            return false;
        }

        try
        {
            // Unix系统信号终止
            var killProcess = Process.Start("kill", $"-9 {pid}");
            killProcess?.WaitForExit();
            return true;
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"Failed to kill emulator process: {ex.Message}");
            return false;
        }
    }
}
