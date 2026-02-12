using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using System;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

public class SystemNotificationAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(SystemNotificationAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            var title = "MFAAvalonia";
            var message = "任务通知";
            if (!string.IsNullOrWhiteSpace(args.ActionParam))
            {
                var json = ActionParamHelper.Parse(args.ActionParam);
                title = (string?)json["title"] ?? title;
                message = (string?)json["message"] ?? message;
            }

            LoggerHelper.Info($"[SystemNotificationAction] 发送通知: {title} - {message}");
            ToastNotification.Show(title, message);
            return true;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[SystemNotificationAction] Error: {e.Message}");
            return false;
        }
    }
}
