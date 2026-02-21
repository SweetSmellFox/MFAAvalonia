using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace MFAAvalonia.Helper;

/// <summary>
/// 双指触摸注入：id=0 在光标位置（primary，防止光标跳转），id=1 在游戏目标位置。
/// 每次操作结束后全部释放，不保留持久 anchor。
/// </summary>
[SupportedOSPlatform("windows")]
public static class TouchInjectionHelper
{
    private static bool _initialized;

    #region Click / Swipe
    public static (bool Success, string Info) ClickDiag(nint hwnd, int clientX, int clientY,
        int holdMs = 50, bool moveWindow = false)
    {
        if (!EnsureInitialized())
            return (false, $"InitializeTouchInjection failed: {Marshal.GetLastWin32Error()}");

        return WithWindowOffScreen(hwnd, moveWindow, () =>
        {
            var pt = new POINT(clientX, clientY);
            if (!ClientToScreen(hwnd, ref pt))
                return (false, $"ClientToScreen failed: {Marshal.GetLastWin32Error()}");

            GetCursorPos(out var cur);

            // anchor(id=0) DOWN at cursor
            var anchor = MakeContact(0, cur, POINTER_FLAG_DOWN | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT);
            if (!InjectTouchInput(1, [anchor]))
                return (false, $"anchor DOWN failed: err={Marshal.GetLastWin32Error()}");

            // target(id=1) DOWN at game position
            anchor.PointerInfo.pointerFlags = POINTER_FLAG_UPDATE | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT;
            var target = MakeContact(1, pt, POINTER_FLAG_DOWN | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT);
            if (!InjectTouchInput(2, [anchor, target]))
            {
                anchor.PointerInfo.pointerFlags = POINTER_FLAG_UP;
                InjectTouchInput(1, [anchor]);
                return (false, $"target DOWN failed: err={Marshal.GetLastWin32Error()}");
            }

            if (holdMs > 0) Thread.Sleep(holdMs);

            // 释放前刷新光标位置，防止跳回操作开始时的位置
            GetCursorPos(out cur);
            anchor = MakeContact(0, cur, POINTER_FLAG_UPDATE | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT);

            // 先释放 target（anchor 仍在，保持 primary，光标不跳）
            target.PointerInfo.pointerFlags = POINTER_FLAG_UP;
            InjectTouchInput(2, [anchor, target]);

            // 再释放 anchor（位置已是当前光标）
            anchor.PointerInfo.pointerFlags = POINTER_FLAG_UP;
            InjectTouchInput(1, [anchor]);

            var hitWnd = WindowFromPoint(pt);
            return (true, $"OK. target=({pt.X},{pt.Y}) anchor=({cur.X},{cur.Y}) hwnd=0x{hwnd:X} hitWnd=0x{hitWnd:X}");
        });
    }
    public static bool Click(nint hwnd, int clientX, int clientY, int holdMs = 50, bool moveWindow = false)
        => ClickDiag(hwnd, clientX, clientY, holdMs, moveWindow).Success;

    public static bool Swipe(nint hwnd, int fromX, int fromY, int toX, int toY,
        int durationMs = 200, int steps = 0, bool moveWindow = false)
    {
        if (!EnsureInitialized()) return false;

        return WithWindowOffScreen(hwnd, moveWindow, () =>
        {
            var ptFrom = new POINT(fromX, fromY);
            var ptTo = new POINT(toX, toY);
            if (!ClientToScreen(hwnd, ref ptFrom) || !ClientToScreen(hwnd, ref ptTo))
                return false;

            GetCursorPos(out var cur);

            // anchor(id=0) DOWN at cursor
            var anchor = MakeContact(0, cur, POINTER_FLAG_DOWN | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT);
            if (!InjectTouchInput(1, [anchor])) return false;

            // target(id=1) DOWN at start
            anchor.PointerInfo.pointerFlags = POINTER_FLAG_UPDATE | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT;
            var target = MakeContact(1, ptFrom, POINTER_FLAG_DOWN | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT);
            if (!InjectTouchInput(2, [anchor, target]))
            {
                anchor.PointerInfo.pointerFlags = POINTER_FLAG_UP;
                InjectTouchInput(1, [anchor]);
                return false;
            }

            // 时间驱动平滑滑动
            int frame = 0;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < durationMs)
            {
                Thread.Sleep(16);
                float t = Math.Min(1f, (float)sw.ElapsedMilliseconds / durationMs);
                int x = ptFrom.X + (int)((ptTo.X - ptFrom.X) * t);
                int y = ptFrom.Y + (int)((ptTo.Y - ptFrom.Y) * t);

                GetCursorPos(out cur);
                anchor = MakeContact(0, cur, POINTER_FLAG_UPDATE | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT);
                target = MakeContact(1, new POINT(x, y), POINTER_FLAG_UPDATE | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT);

                if (!InjectTouchInput(2, [anchor, target]))
                {
                    LoggerHelper.Info($"[Touch] Swipe frame={frame} fail err={Marshal.GetLastWin32Error()}");
                    break;
                }
                frame++;
            }
            LoggerHelper.Info($"[Touch] Swipe done frames={frame} elapsed={sw.ElapsedMilliseconds}ms");

            // 先释放 target（anchor 仍在，保持 primary，光标不跳）
            GetCursorPos(out cur);
            target = MakeContact(1, ptTo, POINTER_FLAG_UP);
            anchor = MakeContact(0, cur, POINTER_FLAG_UPDATE | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT);
            InjectTouchInput(2, [anchor, target]);

            // 再释放 anchor
            anchor.PointerInfo.pointerFlags = POINTER_FLAG_UP;
            InjectTouchInput(1, [anchor]);

            return true;
        });
    }

    #endregion

    #region Helpers

    private static POINTER_TOUCH_INFO MakeContact(uint id, POINT pt, uint flags) => new()
    {
        PointerInfo = new POINTER_INFO
        {
            pointerType = PT_TOUCH, pointerId = id,
            ptPixelLocation = pt, pointerFlags = flags,
        },
        TouchFlags = TOUCH_FLAG_NONE,
        TouchMask = TOUCH_MASK_CONTACTAREA,
        ContactArea = new RECT(pt.X - 2, pt.Y - 2, pt.X + 2, pt.Y + 2),
    };

    private static T WithWindowOffScreen<T>(nint hwnd, bool enabled, Func<T> action)
    {
        if (!enabled) return action();
        GetWindowRect(hwnd, out var orig);
        SetWindowPos(hwnd, nint.Zero, -10000, -10000, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        try { return action(); }
        finally { SetWindowPos(hwnd, nint.Zero, orig.Left, orig.Top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE); }
    }

    private static bool EnsureInitialized()
    {
        if (_initialized) return true;
        _initialized = InitializeTouchInjection(10, TOUCH_FEEDBACK_NONE);
        return _initialized;
    }

    #endregion

    #region Native Interop

    private const uint PT_TOUCH = 2, TOUCH_FLAG_NONE = 0, TOUCH_MASK_CONTACTAREA = 4, TOUCH_FEEDBACK_NONE = 3;
    private const uint POINTER_FLAG_INRANGE = 0x00000002, POINTER_FLAG_INCONTACT = 0x00000004;
    private const uint POINTER_FLAG_DOWN = 0x00010000, POINTER_FLAG_UPDATE = 0x00020000, POINTER_FLAG_UP = 0x00040000;
    private const uint SWP_NOSIZE = 1, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool InitializeTouchInjection(uint maxCount, uint dwMode);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool InjectTouchInput(uint count, [MarshalAs(UnmanagedType.LPArray)] POINTER_TOUCH_INFO[] contacts);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ClientToScreen(nint hWnd, ref POINT lpPoint);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint hWnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(POINT point);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT(int x, int y) { public int X = x; public int Y = y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT(int l, int t, int r, int b) { public int Left = l, Top = t, Right = r, Bottom = b; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTER_INFO
    {
        public uint pointerType, pointerId, frameId, pointerFlags;
        public nint sourceDevice, hwndTarget;
        public POINT ptPixelLocation, ptHimetricLocation, ptPixelLocationRaw, ptHimetricLocationRaw;
        public uint dwTime, historyCount;
        public int InputData;
        public uint dwKeyStates;
        public ulong PerformanceCount;
        public int ButtonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTER_TOUCH_INFO
    {
        public POINTER_INFO PointerInfo;
        public uint TouchFlags, TouchMask;
        public RECT ContactArea, ContactAreaRaw;
        public uint Orientation, Pressure;
    }

    #endregion
}
