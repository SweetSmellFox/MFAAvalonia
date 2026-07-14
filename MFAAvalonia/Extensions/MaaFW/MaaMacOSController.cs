using MaaFramework.Binding;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using MaaBindingController = MaaFramework.Binding.MaaController;
using static MaaFramework.Binding.Interop.Native.MaaController;

namespace MFAAvalonia.Extensions.MaaFW;

public enum MacOSScreencapMethod : ulong
{
    None = 0,
    ScreenCaptureKit = 1,
}

public enum MacOSInputMethod : ulong
{
    None = 0,
    GlobalEvent = 1,
    PostToPid = 1 << 1,
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
public class MaaMacOSController : MaaBindingController
{
    private readonly DesktopWindowInfo _debugInfo;
    private readonly MacOSScreencapMethod _debugScreencapMethod;
    private readonly MacOSInputMethod _debugInputMethod;

    [ExcludeFromCodeCoverage(Justification = "Debugger display.")]
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string DebuggerDisplay => IsInvalid
        ? $"Invalid {GetType().Name}"
        : $"{GetType().Name} {{ {nameof(_debugInfo.Name)} = {_debugInfo.Name}, {nameof(_debugInfo.ClassName)} = {_debugInfo.ClassName}, ScreencapMethod = {_debugScreencapMethod}, InputMethod = {_debugInputMethod} }}";

    public MaaMacOSController(
        DesktopWindowInfo info,
        MacOSScreencapMethod screencapMethod = MacOSScreencapMethod.ScreenCaptureKit,
        MacOSInputMethod inputMethod = MacOSInputMethod.GlobalEvent,
        LinkOption link = LinkOption.Start,
        CheckStatusOption check = CheckStatusOption.ThrowIfNotSucceeded)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (info.Handle == nint.Zero) throw new ArgumentException("Value cannot be zero.", nameof(info.Handle));
        var rawWindowId = (nuint)info.Handle;
        if (rawWindowId > uint.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(info.Handle), info.Handle, "macOS window id must fit in UInt32.");

        var handle = MaaMacOSControllerCreate((uint)rawWindowId, screencapMethod, inputMethod);
        _ = MaaControllerAddSink(handle, MaaEventCallback, (nint)MaaHandleType.Controller);
        SetHandle(handle, needReleased: true);

        _debugInfo = info;
        _debugScreencapMethod = screencapMethod;
        _debugInputMethod = inputMethod;

        if (link == LinkOption.Start)
            LinkStartOnConstructed(check, info);
    }

    [DllImport("MaaFramework", EntryPoint = "MaaMacOSControllerCreate")]
    private static extern nint MaaMacOSControllerCreate(uint windowId, MacOSScreencapMethod screencapMethod, MacOSInputMethod inputMethod);
}
