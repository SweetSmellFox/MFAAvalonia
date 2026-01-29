using System;

namespace MFAAvalonia.Extensions.MaaFW;
public enum MaaControllerTypes
{
    None = 0,
    Win32 = 1,
    Adb = 2,
    PlayCover = 4,
    Gamepad = 8,
}

public static class MaaControllerHelper
{
    extension(MaaControllerTypes controllerType)
    {
        public string ToResourceKey()
        {
            return controllerType switch
            {
                MaaControllerTypes.Win32 => "TabWin32",
                MaaControllerTypes.Adb => "TabADB",
                MaaControllerTypes.PlayCover => "TabPlayCover",
                MaaControllerTypes.Gamepad => "TabGamepad",
                _ => "TabADB"
            };
        }
        public string ToJsonKey()
        {
            return controllerType switch
            {
                MaaControllerTypes.Win32 => "win32",
                MaaControllerTypes.Adb => "adb",
                MaaControllerTypes.PlayCover => "playcover",
                MaaControllerTypes.Gamepad => "gamepad",
                _ => "adb"
            };
        }
    }

    public static MaaControllerTypes ToMaaControllerTypes(this string? type, MaaControllerTypes defaultValue = MaaControllerTypes.Adb)
    {
        if (string.IsNullOrWhiteSpace(type))
            return defaultValue;
        if (type.Contains("playcover", StringComparison.OrdinalIgnoreCase))
            return MaaControllerTypes.PlayCover;
        if (type.Contains("gamepad", StringComparison.OrdinalIgnoreCase))
            return MaaControllerTypes.Gamepad;
        if (type.Contains("win32", StringComparison.OrdinalIgnoreCase))
            return MaaControllerTypes.Win32;
        if (type.Contains("adb", StringComparison.OrdinalIgnoreCase))
            return MaaControllerTypes.Adb;
        return defaultValue;
    }
}

