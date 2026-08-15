using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Imaging;

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
        if (IsAttached)
            SpanVirtualScreen();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Leave WorkerW while the handle is still alive, so the real wallpaper comes back.
        _engine?.Detach();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _engine?.Dispose();
        _engine = null;
        IsAttached = false;
        base.OnClosed(e);
    }

    /// <summary>Cover the entire virtual desktop (all monitors).</summary>
    private void SpanVirtualScreen()
    {
        var dpi = GetDpiScale();
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth / dpi;
        Height = SystemParameters.VirtualScreenHeight / dpi;
    }

    private double GetDpiScale()
    {
        try
        {
            return PresentationSource.FromVisual(this) is { } src
                ? src.CompositionTarget.TransformToDevice.M11
                : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }
}
