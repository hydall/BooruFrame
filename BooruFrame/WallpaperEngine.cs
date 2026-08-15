using System.Runtime.InteropServices;

namespace BooruFrame;

/// <summary>
/// "Wallpaper mode" for a window — the same technique Wallpaper Engine uses.
///
/// It does NOT touch the system wallpaper setting at all. Instead it asks the desktop's
/// owner window (Progman) to split off a background layer (WorkerW) that sits *behind*
/// the desktop icons, then parents our window to that layer. The result is a live
/// animated/cycling picture on the desktop with the normal icons and taskbar on top,
/// while Windows still believes the original wallpaper is untouched — quitting the app
/// instantly reveals the real wallpaper again.
/// </summary>
public sealed class WallpaperEngine : IDisposable
{
    /// <summary>Why the last <see cref="Attach"/> failed, so the app can say something useful.</summary>
    public enum AttachError
    {
        None,
        /// <summary>The desktop's background layer could not be located at all.</summary>
        NoDesktopLayer,
        /// <summary>The layer was found, but Windows refused to move our window into it.</summary>
        ReparentRejected,
    }

    private const int WM_SPAWN_WORKER = 0x052C;

    private static readonly IntPtr HWND_BOTTOM = new(1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    private const uint GA_PARENT = 1;

    private readonly IntPtr _windowHandle;
    private IntPtr _originalParent = IntPtr.Zero;

    public bool IsAttached { get; private set; }

    /// <summary>The desktop background window we are parented to, while attached.</summary>
    public IntPtr Layer { get; private set; }

    /// <summary>Result of the last <see cref="Attach"/> call.</summary>
    public AttachError LastError { get; private set; }

    public WallpaperEngine(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
    }

    /// <summary>Move this window onto the desktop background layer (behind icons, above wallpaper).</summary>
    public bool Attach()
    {
        if (IsAttached || _windowHandle == IntPtr.Zero)
            return IsAttached;

        LastError = AttachError.None;

        var layer = FindDesktopLayer();
        if (layer == IntPtr.Zero)
        {
            LastError = AttachError.NoDesktopLayer;
            return false;
        }

        // Remember where we came from so we can restore on exit.
        _originalParent = GetAncestor(_windowHandle, GA_PARENT);

        // SetParent returns the *previous* parent, which is null for a top-level window — a
        // null return therefore says nothing about success. Ask Windows who our parent is now.
        SetParent(_windowHandle, layer);
        IsAttached = GetAncestor(_windowHandle, GA_PARENT) == layer;

        if (!IsAttached)
        {
            LastError = AttachError.ReparentRejected;
            return false;
        }

        Layer = layer;

        // Keep our window at the bottom of the layer's child list so it can never cover
        // the desktop icons.
        SetWindowPos(_windowHandle, HWND_BOTTOM, 0, 0, 0, 0,
            SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);

        return true;
    }

    /// <summary>Restore the window to a normal top-level window (back to its old parent).</summary>
    public bool Detach()
    {
        if (!IsAttached)
            return true;

        var target = _originalParent != IntPtr.Zero ? _originalParent : GetDesktopWindow();
        SetParent(_windowHandle, target);
        var ok = GetAncestor(_windowHandle, GA_PARENT) == target;

        _originalParent = IntPtr.Zero;
        Layer = IntPtr.Zero;
        IsAttached = false;
        return ok;
    }

    /// <summary>
    /// Find the window our picture can hang on so it lands between the real wallpaper and the
    /// desktop icons. Windows has moved that layer around between builds, so the known layouts
    /// are tried in turn, ending with Progman itself — which always exists.
    /// </summary>
    private static IntPtr FindDesktopLayer()
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
            return IntPtr.Zero;

        // Ask Progman to split the background off into its own WorkerW. Builds disagree about
        // the parameters and simply ignore a variant they don't know, so send both known ones.
        SpawnWorker(progman, IntPtr.Zero, IntPtr.Zero);
        SpawnWorker(progman, new IntPtr(0x0D), new IntPtr(0x01));

        // Layout A (Windows 10 and most of 11): the icon list (SHELLDLL_DefView) lives in one
        // top-level window, and the wallpaper layer is the next WorkerW behind it.
        var behindIcons = FindWorkerWBehindIcons();
        if (behindIcons != IntPtr.Zero)
            return behindIcons;

        // Layout B: nothing was split off — the icons are still inside Progman, which paints
        // the wallpaper itself. Its bottom-most child then sits behind the icons.
        return progman;
    }

    private static void SpawnWorker(IntPtr progman, IntPtr wParam, IntPtr lParam) =>
        SendMessageTimeout(progman, WM_SPAWN_WORKER, wParam, lParam,
            SendMessageTimeoutFlags.SMTO_NORMAL, 1000, out _);

    private static IntPtr FindWorkerWBehindIcons()
    {
        IntPtr found = IntPtr.Zero;

        EnumWindows((hwnd, _) =>
        {
            if (FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero)
                return true; // not the window hosting the icons — keep looking

            var worker = FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
            if (worker == IntPtr.Zero)
                return true;

            found = worker;
            return false; // stop the enumeration
        }, IntPtr.Zero);

        return found;
    }

    public void Dispose() => Detach();

    // ---------------------------------------------------------------- P/Invoke

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [Flags]
    private enum SendMessageTimeoutFlags : uint
    {
        SMTO_NORMAL = 0x0000,
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        SendMessageTimeoutFlags flags,
        uint timeout,
        out IntPtr result);
}
