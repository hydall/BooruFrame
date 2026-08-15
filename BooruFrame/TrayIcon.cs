using System.Drawing;
using Forms = System.Windows.Forms;

namespace BooruFrame;

/// <summary>
/// System-tray icon with a right-click context menu. Kept separate from the window so the
/// WinForms namespace doesn't collide with WPF types. The main window wires the events to
/// its own commands.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _visibilityItem;
    private readonly Forms.ToolStripMenuItem _playItem;
    private readonly Forms.ToolStripMenuItem _prevItem;
    private readonly Forms.ToolStripMenuItem _nextItem;
    private readonly Forms.ToolStripMenuItem _settingsItem;
    private readonly Forms.ToolStripMenuItem _wallpaperItem;
    private readonly Forms.ToolStripMenuItem _exitItem;

    private bool _playing;
    private bool _windowVisible;
    private bool _wallpaperActive;

    public event Action? VisibilityRequested;
    public event Action? PlayToggleRequested;
    public event Action? NextRequested;
    public event Action? PrevRequested;
    public event Action? SettingsRequested;
    public event Action? WallpaperToggleRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        _visibilityItem = new Forms.ToolStripMenuItem();
        _playItem = new Forms.ToolStripMenuItem();
        _prevItem = new Forms.ToolStripMenuItem();
        _nextItem = new Forms.ToolStripMenuItem();
        _settingsItem = new Forms.ToolStripMenuItem();
        _wallpaperItem = new Forms.ToolStripMenuItem();
        _exitItem = new Forms.ToolStripMenuItem();

        _visibilityItem.Click += (_, _) => VisibilityRequested?.Invoke();
        _playItem.Click += (_, _) => PlayToggleRequested?.Invoke();
        _prevItem.Click += (_, _) => PrevRequested?.Invoke();
        _nextItem.Click += (_, _) => NextRequested?.Invoke();
        _settingsItem.Click += (_, _) => SettingsRequested?.Invoke();
        _wallpaperItem.Click += (_, _) => WallpaperToggleRequested?.Invoke();
        _exitItem.Click += (_, _) => ExitRequested?.Invoke();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_visibilityItem);
        menu.Items.Add(_playItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_prevItem);
        menu.Items.Add(_nextItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_wallpaperItem);
        menu.Items.Add(_settingsItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_exitItem);

        _icon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "BooruFrame",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                VisibilityRequested?.Invoke();
        };

        Localization.Changed += Refresh;
        Refresh();
    }

    public void SetPlaying(bool playing)
    {
        _playing = playing;
        Refresh();
    }

    public void SetWindowVisible(bool visible)
    {
        _windowVisible = visible;
        Refresh();
    }

    public void SetWallpaperActive(bool active)
    {
        _wallpaperActive = active;
        Refresh();
    }

    private void Refresh()
    {
        _visibilityItem.Text = Localization.Get(_windowVisible ? "Tray_Hide" : "Tray_Show");
        _playItem.Text = Localization.Get(_playing ? "L_Stop" : "L_Start");
        _prevItem.Text = Localization.Get("Tray_Prev");
        _nextItem.Text = Localization.Get("L_Next");
        _wallpaperItem.Text = Localization.Get("Tray_Wallpaper");
        _wallpaperItem.Checked = _wallpaperActive;
        _settingsItem.Text = Localization.Get("L_Settings");
        _exitItem.Text = Localization.Get("Tray_Exit");
    }

    public void Dispose()
    {
        Localization.Changed -= Refresh;
        _icon.Dispose();
    }
}
