using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

public class ComputerOperationAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(ComputerOperationAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            var operation = "shutdown";
            if (!string.IsNullOrWhiteSpace(args.ActionParam))
            {
                var json = ActionParamHelper.Parse(args.ActionParam);
                operation = (string?)json["operation"] ?? "shutdown";
            }

            LoggerHelper.Info($"[ComputerOperationAction] 执行操作: {operation}");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                switch (operation.ToLower())
                {
                    case "shutdown":
                        Process.Start("shutdown", "/s /t 0");
                        break;
                    case "restart":
                        Process.Start("shutdown", "/r /t 0");
                        break;
                    case "sleep":
                        SetSuspendState(false, true, true);
                        break;
                    case "hibernate":
                        SetSuspendState(true, true, true);
                        break;
                    default:
                        LoggerHelper.Warning($"[ComputerOperationAction] 未知操作: {operation}");
                        return false;
                }
            }
            else
            {
                switch (operation.ToLower())
                {
                    case "shutdown":
                        Process.Start("shutdown", "-h now");
                        break;
                    case "restart":
                        Process.Start("shutdown", "-r now");
                        break;
                    default:
                        LoggerHelper.Warning($"[ComputerOperationAction] 当前平台不支持操作: {operation}");
                        return false;
                }
            }

            return true;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[ComputerOperationAction] Error: {e.Message}");
            return false;
        }
    }

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
}
