namespace MFAAvalonia.Helper;

public enum MobileRunMode
{
    VirtualDisplay,
    CurrentScreen
}

public enum MobileRunResolution
{
    P720,
    P1080
}

public static class MobileRunConfiguration
{
    public static MobileRunMode Mode { get; set; } = MobileRunMode.VirtualDisplay;
    public static MobileRunResolution Resolution { get; set; } = MobileRunResolution.P720;
}
