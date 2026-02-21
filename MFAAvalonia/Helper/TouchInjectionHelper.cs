using System;
using System.Collections.Generic;
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

    /// <summary>注入期间为 true，调用方可据此屏蔽 overlay 上的误触 Click。</summary>
    public static bool IsInjecting { get; private set; }

    #region Click / Swipe
    public static (bool Success, string Info) ClickDiag(nint hwnd, int clientX, int clientY,
        int holdMs = 50, bool moveWindow = false, bool penetrate = false)
    {
        if (!EnsureInitialized())
            return (false, $"InitializeTouchInjection failed: {Marshal.GetLastWin32Error()}");

        var pt = new POINT(clientX, clientY);
        if (!ClientToScreen(hwnd, ref pt))
            return (false, $"ClientToScreen failed: {Marshal.GetLastWin32Error()}");

        List<(nint Hwnd, nint OrigRgn, RECT WinRect)>? overlays = null;
        if (penetrate) overlays = PunchOverlays(hwnd, pt);
        IsInjecting = true;
        try
        {
            return WithWindowOffScreen(hwnd, moveWindow, () =>
            {
                GetCursorPos(out var cur);

                var anchor = MakeContact(0, cur, POINTER_FLAG_DOWN | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT);
                var target = MakeContact(1, pt, POINTER_FLAG_DOWN | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT);
                if (!InjectTouchInput(2, [anchor, target]))
                    return (false, $"DOWN failed: err={Marshal.GetLastWin32Error()}");

                if (holdMs > 0) Thread.Sleep(holdMs);

                GetCursorPos(out cur);
                anchor = MakeContact(0, cur, POINTER_FLAG_UP);
                target = MakeContact(1, pt, POINTER_FLAG_UP);
                InjectTouchInput(2, [anchor, target]);

                return (true, $"OK. target=({pt.X},{pt.Y}) anchor=({cur.X},{cur.Y}) hwnd=0x{hwnd:X}");
            });
        }
        finally
        {
            if (overlays != null) RestoreHoles(overlays);
            IsInjecting = false;
        }
    }
    public static bool Click(nint hwnd, int clientX, int clientY, int holdMs = 50, bool moveWindow = false, bool penetrate = false)
        => ClickDiag(hwnd, clientX, clientY, holdMs, moveWindow, penetrate).Success;

    public static bool Swipe(nint hwnd, int fromX, int fromY, int toX, int toY,
        int durationMs = 200, int steps = 0, bool moveWindow = false, bool penetrate = false)
    {
        if (!EnsureInitialized()) return false;

        var ptFrom = new POINT(fromX, fromY);
        var ptTo = new POINT(toX, toY);
        if (!ClientToScreen(hwnd, ref ptFrom) || !ClientToScreen(hwnd, ref ptTo))
            return false;

        List<(nint Hwnd, nint OrigRgn, RECT WinRect)>? overlays = null;
        if (penetrate) overlays = PunchOverlays(hwnd, ptFrom);
        IsInjecting = true;
        try
        {
            return WithWindowOffScreen(hwnd, moveWindow, () =>
            {
                GetCursorPos(out var cur);

                var anchor = MakeContact(0, cur, POINTER_FLAG_DOWN | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT);
                var target = MakeContact(1, ptFrom, POINTER_FLAG_DOWN | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT);
                if (!InjectTouchInput(2, [anchor, target])) return false;

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

                GetCursorPos(out cur);
                anchor = MakeContact(0, cur, POINTER_FLAG_UP);
                target = MakeContact(1, ptTo, POINTER_FLAG_UP);
                InjectTouchInput(2, [anchor, target]);

                return true;
            });
        }
        finally
        {
            if (overlays != null) RestoreHoles(overlays);
            IsInjecting = false;
        }
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

    private static bool IsTargetOrChild(nint hitWnd, nint targetHwnd)
        => hitWnd == targetHwnd || GetAncestor(hitWnd, GA_ROOT) == targetHwnd;

    /// <summary>发现并在所有遮挡窗口上打 4×4 小洞（支持多个点），返回状态列表供恢复。</summary>
    private static List<(nint Hwnd, nint OrigRgn, RECT WinRect)> PunchOverlays(nint targetHwnd, params POINT[] screenPts)
    {
        var list = new List<(nint, nint, RECT)>();
        nint hit;
        while ((hit = WindowFromPoint(screenPts[0])) != nint.Zero
               && !IsTargetOrChild(hit, targetHwnd) && list.Count < 20)
        {
            var saved = CreateRectRgn(0, 0, 0, 0);
            if (GetWindowRgn(hit, saved) == 0) { DeleteObject(saved); saved = nint.Zero; }
            GetWindowRect(hit, out var wr);
            ApplyHoles(hit, saved, wr, screenPts);
            list.Add((hit, saved, wr));
        }
        return list;
    }

    /// <summary>在指定窗口上打多个 4×4 小洞（屏幕坐标）。</summary>
    private static void ApplyHoles(nint hwnd, nint origRgn, RECT wr, POINT[] screenPts)
    {
        var rgn = CreateRectRgn(0, 0, wr.Right - wr.Left, wr.Bottom - wr.Top);
        if (origRgn != nint.Zero) CombineRgn(rgn, origRgn, rgn, RGN_AND);
        foreach (var pt in screenPts)
        {
            int hx = pt.X - wr.Left, hy = pt.Y - wr.Top;
            var hole = CreateRectRgn(hx - 2, hy - 2, hx + 2, hy + 2);
            CombineRgn(rgn, rgn, hole, RGN_DIFF);
            DeleteObject(hole);
        }
        SetWindowRgn(hwnd, rgn, false);
    }

    /// <summary>恢复所有遮挡窗口的原始 region。</summary>
    private static void RestoreHoles(List<(nint Hwnd, nint OrigRgn, RECT WinRect)> overlays)
    {
        for (int i = overlays.Count - 1; i >= 0; i--)
            SetWindowRgn(overlays[i].Hwnd, overlays[i].OrigRgn == nint.Zero ? nint.Zero : overlays[i].OrigRgn, false);
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
    private const uint GA_ROOT = 2;
    private const int RGN_AND = 1, RGN_DIFF = 4;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool InitializeTouchInjection(uint maxCount, uint dwMode);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool InjectTouchInput(uint count, [MarshalAs(UnmanagedType.LPArray)] POINTER_TOUCH_INFO[] contacts);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ClientToScreen(nint hWnd, ref POINT lpPoint);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint hWnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(POINT point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint gaFlags);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint hWnd, nint hRgn, bool bRedraw);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint hWnd, nint hRgn);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(nint dest, nint src1, nint src2, int mode);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint hObject);

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
