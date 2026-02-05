using System;
using System.Runtime.InteropServices;
using MFAAvalonia.Configuration;
using MFAAvalonia.Helper;

namespace MFAAvalonia.Helper;

public static class SystemSleepHelper
{
    [Flags]
    private enum ExecutionState : uint
    {
        ES_AWAYMODE_REQUIRED = 0x00000040,
        ES_CONTINUOUS = 0x80000000,
        ES_DISPLAY_REQUIRED = 0x00000002,
        ES_SYSTEM_REQUIRED = 0x00000001
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

    public static void ApplyPreventSleep()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            var prevent = GlobalConfiguration.GetValue(ConfigurationKeys.PreventSleep, "false").ToLower() == "true";
            ApplyPreventSleep(prevent);
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"Failed to apply prevent sleep settings: {ex.Message}");
        }
    }

    public static void ApplyPreventSleep(bool prevent)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            if (prevent)
            {
                // Prevent system sleep, keep display on
                SetThreadExecutionState(ExecutionState.ES_CONTINUOUS | ExecutionState.ES_SYSTEM_REQUIRED | ExecutionState.ES_DISPLAY_REQUIRED);
                LoggerHelper.Info("System sleep prevented.");
            }
            else
            {
                // Allow sleep
                SetThreadExecutionState(ExecutionState.ES_CONTINUOUS);
                LoggerHelper.Info("System sleep allowed.");
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"Failed to set execution state: {ex.Message}");
        }
    }
}
