using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using System;
using System.Threading;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

public class TimedWaitAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(TimedWaitAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            var hour = 0;
            var minute = 0;
            if (!string.IsNullOrWhiteSpace(args.ActionParam))
            {
                var json = ActionParamHelper.Parse(args.ActionParam);
                hour = (int?)json["hour"] ?? 0;
                minute = (int?)json["minute"] ?? 0;
            }

            var now = DateTime.Now;
            var target = now.Date.AddHours(hour).AddMinutes(minute);

            // 如果目标时间已过，则等待到明天的该时间
            if (target <= now)
                target = target.AddDays(1);

            var waitTime = target - now;
            LoggerHelper.Info($"[TimedWaitAction] 等待到 {target:HH:mm}，还需等待 {waitTime.TotalMinutes:F1} 分钟");

            // 分段等待，每30秒检查一次，避免长时间阻塞
            while (DateTime.Now < target)
            {
                var remaining = target - DateTime.Now;
                var sleepMs = Math.Min((int)remaining.TotalMilliseconds, 30000);
                if (sleepMs <= 0) break;
                Thread.Sleep(sleepMs);
            }

            LoggerHelper.Info("[TimedWaitAction] 已到达目标时间，继续执行");
            return true;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[TimedWaitAction] Error: {e.Message}");
            return false;
        }
    }
}
