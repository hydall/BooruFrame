using System.Runtime.InteropServices;

namespace BooruFrame;

/// <summary>
/// A rectangle in physical screen pixels. Window geometry is deliberately kept in pixels
/// rather than WPF's device-independent units: the app is per-monitor DPI aware, so a
/// point expressed in WPF units means different things on different screens and cannot be
/// saved and restored reliably. Pixels are the same everywhere.
/// </summary>
internal readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Intersects(PixelRect other) =>
        Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;

    public bool Contains(PixelRect other) =>
        other.Left >= Left && other.Top >= Top && other.Right <= Right && other.Bottom <= Bottom;

    /// <summary>Area shared with <paramref name="other"/>; 0 when they don't overlap.</summary>
    public long OverlapArea(PixelRect other)
    {
        var w = Math.Min(Right, other.Right) - Math.Max(Left, other.Left);
        var h = Math.Min(Bottom, other.Bottom) - Math.Max(Top, other.Top);
        return w <= 0 || h <= 0 ? 0 : (long)w * h;
    }
}

/// <summary>One connected display, in physical screen pixels.</summary>
internal sealed record MonitorInfo(
    IntPtr Handle,
    string Device,
    PixelRect Bounds,
    PixelRect WorkArea,
    bool IsPrimary);

/// <summary>
/// Win32 window placement and monitor queries, all in physical pixels.
///
/// Placement is read and written through Get/SetWindowPlacement instead of WPF's
/// Left/Top/Width/Height, because that is the only API that also carries the "restore"
/// rectangle of a maximized window — which is what tells Windows *which monitor* to
/// maximize a window onto when it is restored at start-up.
/// </summary>
internal static class WindowPlacement
{
    private const int SW_HIDE = 0;
    private const int SW_SHOWNORMAL = 1;
    private const int SW_SHOWMINIMIZED = 2;
    private const int SW_SHOWMAXIMIZED = 3;

    private const int WPF_RESTORETOMAXIMIZED = 0x0002;

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint MONITORINFOF_PRIMARY = 1;

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    // ---------------------------------------------------------------- monitors

    public static List<MonitorInfo> All()
    {
        var monitors = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data) =>
        {
            if (Describe(monitor) is { } info)
                monitors.Add(info);
            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    public static MonitorInfo? Primary() =>
        All().FirstOrDefault(m => m.IsPrimary);

    public static MonitorInfo? ByDevice(string device) =>
        string.IsNullOrEmpty(device)
            ? null
            : All().FirstOrDefault(m => string.Equals(m.Device, device, StringComparison.OrdinalIgnoreCase));

    /// <summary>The monitor showing most of <paramref name="rect"/>, or null if it is off-screen.</summary>
    public static MonitorInfo? Covering(PixelRect rect)
    {
        MonitorInfo? best = null;
        long bestArea = 0;
        foreach (var m in All())
        {
            var area = m.Bounds.OverlapArea(rect);
            if (area > bestArea)
            {
                best = m;
                bestArea = area;
            }
        }
        return best;
    }

    public static MonitorInfo? OfWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return null;
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        return monitor == IntPtr.Zero ? null : Describe(monitor);
    }

    /// <summary>Dots-per-inch of a monitor; 96 (100%) when Windows won't say.</summary>
    public static uint DpiOf(MonitorInfo? monitor)
    {
        const uint MDT_EFFECTIVE_DPI = 0;
        try
        {
            if (monitor is not null &&
                GetDpiForMonitor(monitor.Handle, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 &&
                dpiX > 0)
            {
                return dpiX;
            }
        }
        catch
        {
            // Pre-8.1 shcore.dll — fall through to the default.
        }
        return 96;
    }

    /// <summary>The bounding box of every monitor together.</summary>
    public static PixelRect VirtualScreen() => new(
        GetSystemMetrics(SM_XVIRTUALSCREEN),
        GetSystemMetrics(SM_YVIRTUALSCREEN),
        GetSystemMetrics(SM_CXVIRTUALSCREEN),
        GetSystemMetrics(SM_CYVIRTUALSCREEN));

    private static MonitorInfo? Describe(IntPtr monitor)
    {
        var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
        if (!GetMonitorInfo(monitor, ref info))
            return null;

        return new MonitorInfo(
            monitor,
            info.szDevice ?? "",
            ToPixelRect(info.rcMonitor),
            ToPixelRect(info.rcWork),
            (info.dwFlags & MONITORINFOF_PRIMARY) != 0);
    }

    // ---------------------------------------------------------------- placement

    /// <summary>
    /// Read where the window would sit if it were restored, plus whether it is maximized.
    /// Works for hidden and minimized windows too, which is the whole point of the API.
    /// </summary>
    public static bool TryGet(IntPtr hwnd, out PixelRect normal, out bool maximized)
    {
        normal = default;
        maximized = false;

        if (hwnd == IntPtr.Zero)
            return false;

        var placement = new WindowPlacementInfo { length = Marshal.SizeOf<WindowPlacementInfo>() };
        if (!GetWindowPlacement(hwnd, ref placement))
            return false;

        var (dx, dy) = WorkspaceOrigin();
        var r = placement.rcNormalPosition;
        normal = new PixelRect(r.Left + dx, r.Top + dy, r.Right - r.Left, r.Bottom - r.Top);

        maximized = placement.showCmd == SW_SHOWMAXIMIZED ||
                    (placement.showCmd == SW_SHOWMINIMIZED &&
                     (placement.flags & WPF_RESTORETOMAXIMIZED) != 0);
        return !normal.IsEmpty;
    }

    /// <summary>
    /// Put the window's restore rectangle at <paramref name="normal"/> and move the window
    /// there. Whether the window is on screen is not touched: a window waiting in the tray
    /// stays hidden, a visible one stays visible and keeps its state.
    /// </summary>
    public static bool SetRestoreBounds(IntPtr hwnd, PixelRect normal)
    {
        if (hwnd == IntPtr.Zero || normal.IsEmpty)
            return false;

        var placement = new WindowPlacementInfo { length = Marshal.SizeOf<WindowPlacementInfo>() };
        if (!GetWindowPlacement(hwnd, ref placement))
            return false;

        // showCmd is an instruction, not just a report: handing back SW_SHOWNORMAL for a
        // window that isn't on screen yet would put it there. Say "hidden" for those.
        var onScreen = IsWindowVisible(hwnd);
        var showCmd = onScreen ? placement.showCmd : SW_HIDE;

        var (dx, dy) = WorkspaceOrigin();
        placement.showCmd = showCmd;
        placement.rcNormalPosition = new Rect
        {
            Left = normal.Left - dx,
            Top = normal.Top - dy,
            Right = normal.Right - dx,
            Bottom = normal.Bottom - dy,
        };

        var ok = SetWindowPlacement(hwnd, ref placement);

        // SetWindowPlacement only promises to remember the rectangle. Move the window onto it
        // as well, so that maximizing later has no doubt about which monitor is meant — but
        // never while the window is maximized or minimized, where that rectangle is not where
        // the window belongs.
        if (showCmd != SW_SHOWMAXIMIZED && showCmd != SW_SHOWMINIMIZED)
        {
            SetWindowPos(hwnd, IntPtr.Zero, normal.Left, normal.Top, normal.Width, normal.Height,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }

        return ok;
    }

    /// <summary>Move/resize a window (or a child of another window) to an exact pixel rectangle.</summary>
    private static void SetBounds(IntPtr hwnd, PixelRect rect)
    {
        if (hwnd == IntPtr.Zero || rect.IsEmpty)
            return;
        SetWindowPos(hwnd, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    /// <summary>
    /// Cover an exact rectangle of the screen with a window that has been re-parented into
    /// another one. Such a window is positioned in its parent's client coordinates rather
    /// than in screen coordinates — and Windows is not consistent about it for pop-ups — so
    /// the rectangle is translated, applied, and then measured and corrected if it landed
    /// somewhere else.
    /// </summary>
    public static void CoverScreenRect(IntPtr hwnd, IntPtr parent, PixelRect screenRect)
    {
        if (hwnd == IntPtr.Zero || screenRect.IsEmpty)
            return;

        var (x, y) = ToClient(parent, screenRect.Left, screenRect.Top);
        SetBounds(hwnd, new PixelRect(x, y, screenRect.Width, screenRect.Height));

        if (!TryGetWindowRect(hwnd, out var landed) ||
            (landed.Left == screenRect.Left && landed.Top == screenRect.Top))
            return;

        SetBounds(hwnd, new PixelRect(
            x + (screenRect.Left - landed.Left),
            y + (screenRect.Top - landed.Top),
            screenRect.Width,
            screenRect.Height));
    }

    /// <summary>Translate a point from screen pixels into a parent window's client pixels.</summary>
    private static (int X, int Y) ToClient(IntPtr parent, int x, int y)
    {
        var pt = new Point { X = x, Y = y };
        if (parent != IntPtr.Zero && ScreenToClient(parent, ref pt))
            return (pt.X, pt.Y);
        return (x, y);
    }

    /// <summary>Where a window really is, in screen pixels.</summary>
    private static bool TryGetWindowRect(IntPtr hwnd, out PixelRect rect)
    {
        rect = default;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r))
            return false;
        rect = ToPixelRect(r);
        return true;
    }

    /// <summary>
    /// WINDOWPLACEMENT rectangles are "workspace" coordinates: screen coordinates shifted by
    /// the primary monitor's work area origin (non-zero only when the taskbar sits at the top
    /// or on the left). Everything else here uses screen coordinates, so convert on the way in
    /// and out.
    /// </summary>
    private static (int X, int Y) WorkspaceOrigin()
    {
        var primary = Primary();
        return primary is null ? (0, 0) : (primary.WorkArea.Left, primary.WorkArea.Top);
    }

    private static PixelRect ToPixelRect(Rect r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    // ---------------------------------------------------------------- P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacementInfo
    {
        public int length;
        public int flags;
        public int showCmd;
        public Point ptMinPosition;
        public Point ptMaxPosition;
        public Rect rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr hwnd, ref WindowPlacementInfo placement);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPlacement(IntPtr hwnd, ref WindowPlacementInfo placement);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hwnd, ref Point point);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, uint dpiType, out uint dpiX, out uint dpiY);
}
