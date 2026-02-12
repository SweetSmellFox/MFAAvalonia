using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using System;
using System.Diagnostics;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

public class KillProcessAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(KillProcessAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            var processName = "";
            if (!string.IsNullOrWhiteSpace(args.ActionParam))
            {
                var json = ActionParamHelper.Parse(args.ActionParam);
                processName = (string?)json["process_name"] ?? "";
            }

            if (string.IsNullOrWhiteSpace(processName))
            {
                LoggerHelper.Warning("[KillProcessAction] 未指定进程名");
                return false;
            }

            LoggerHelper.Info($"[KillProcessAction] 结束进程: {processName}");
            var processes = Process.GetProcessesByName(processName);
            foreach (var proc in processes)
            {
                try
                {
                    proc.Kill();
                    proc.WaitForExit(5000);
                    LoggerHelper.Info($"[KillProcessAction] 已结束: {proc.ProcessName} (PID: {proc.Id})");
                }
                catch (Exception ex)
                {
                    LoggerHelper.Warning($"[KillProcessAction] 结束进程失败: {ex.Message}");
                }
                finally
                {
                    proc.Dispose();
                }
            }

            return true;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[KillProcessAction] Error: {e.Message}");
            return false;
        }
    }
}
