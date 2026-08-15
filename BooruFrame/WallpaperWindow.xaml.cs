using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace BooruFrame;

/// <summary>
/// The desktop-wallpaper surface: a window of its own that lives on the desktop background
/// layer (WorkerW, behind the icons) and shows nothing but the current picture.
///
/// Keeping it separate from the main window is what lets the app stay usable while the
/// wallpaper runs — the main window is never dragged behind the icons, it simply sits in the
/// tray and opens as an ordinary window whenever the user asks for it.
/// </summary>
public partial class WallpaperWindow : Window
{
    private WallpaperEngine? _engine;
    private bool _watchingDisplays;

    /// <summary>True once the window really sits on the desktop background layer.</summary>
    public bool IsAttached { get; private set; }

    /// <summary>Why the attach failed, when <see cref="IsAttached"/> is false.</summary>
    public WallpaperEngine.AttachError AttachError { get; private set; }

    public WallpaperWindow()
    {
        InitializeComponent();
    }

    /// <summary>Show a picture, optionally crossfading from the previous one.</summary>
    public void SetImage(BitmapSource bmp, bool animate) =>
        ImageCrossfade.Show(ImgHigh, ImgLow, bmp, animate);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        _engine = new WallpaperEngine(hwnd);

        IsAttached = _engine.Attach();
        AttachError = _engine.LastError;
        if (!IsAttached)
            return;

        SpanVirtualScreen();

        // Plugging a monitor in or changing a resolution resizes the desktop underneath us.
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _watchingDisplays = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Leave WorkerW while the handle is still alive, so the real wallpaper comes back.
        _engine?.Detach();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_watchingDisplays)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _watchingDisplays = false;
        }

        _engine?.Dispose();
        _engine = null;
        IsAttached = false;
        base.OnClosed(e);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(new Action(SpanVirtualScreen));

    /// <summary>
    /// Cover the entire virtual desktop (all monitors), in real pixels.
    ///
    /// WPF's Left/Top/Width/Height are no use here: the window is a child of the desktop
    /// background window, so its position is measured from that window's top-left corner and
    /// not from the screen's, and on a mixed-DPI desktop no single WPF scale factor is right
    /// for every monitor. Both problems go away in pixels.
    /// </summary>
    private void SpanVirtualScreen()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || _engine is not { IsAttached: true })
            return;

        WindowPlacement.CoverScreenRect(hwnd, _engine.Layer, WindowPlacement.VirtualScreen());
    }
}
