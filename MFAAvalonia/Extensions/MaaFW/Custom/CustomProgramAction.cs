using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using System;
using System.Diagnostics;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

public class CustomProgramAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(CustomProgramAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            var program = "";
            var arguments = "";
            var waitForExit = false;

            if (!string.IsNullOrWhiteSpace(args.ActionParam))
            {
                var json = ActionParamHelper.Parse(args.ActionParam);
                program = (string?)json["program"] ?? "";
                arguments = (string?)json["arguments"] ?? "";
                waitForExit = (bool?)json["wait_for_exit"] ?? false;
            }

            if (string.IsNullOrWhiteSpace(program))
            {
                LoggerHelper.Warning("[CustomProgramAction] 未指定程序路径");
                return false;
            }

            LoggerHelper.Info($"[CustomProgramAction] 启动程序: {program} {arguments}, 等待退出: {waitForExit}");
            var psi = new ProcessStartInfo
            {
                FileName = program,
                Arguments = arguments,
                UseShellExecute = true
            };

            var process = Process.Start(psi);
            if (process != null && waitForExit)
            {
                process.WaitForExit();
                LoggerHelper.Info($"[CustomProgramAction] 进程已退出, ExitCode: {process.ExitCode}");
            }

            return true;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[CustomProgramAction] Error: {e.Message}");
            return false;
        }
    }
}
