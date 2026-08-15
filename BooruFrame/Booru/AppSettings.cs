using System.IO;
using System.Text.Json;

namespace BooruFrame.Booru;

/// <summary>Small persisted app preferences (language, scale mode, interval).</summary>
public sealed class AppSettings
{
    /// <summary>Empty means "not chosen yet" — resolved from the system language on first run.</summary>
    public string Language { get; set; } = "";
    public string ScaleMode { get; set; } = "Fit"; // Fit | Stretch | Cover
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>Seconds after which an error toast auto-hides; 0 = keep until closed.</summary>
    public int HideErrorsSeconds { get; set; } = 0;

    /// <summary>Hide the three-dot loading animation.</summary>
    public bool HideLoadingAnimation { get; set; } = false;

    /// <summary>How many image changes must pass before the same image may appear again; 0 = off.</summary>
    public int RepeatCooldown { get; set; } = 10;

    public string Tags { get; set; } = "";

    /// <summary>Orientation filter: Any | Landscape | Portrait | Square (client-side by Width/Height).</summary>
    public string AspectRatio { get; set; } = "Any";

    /// <summary>Run the window as the desktop wallpaper (behind icons, above the real wallpaper).</summary>
    public bool WallpaperMode { get; set; } = false;

    /// <summary>
    /// Desktop-frame mode: the picture stops behaving like a real window — no taskbar button,
    /// no Alt+Tab entry, and the frame drops to the bottom of the z-order (onto the desktop
    /// background) as soon as it loses focus. Off by default: normally it is a regular window.
    /// </summary>
    public bool DesktopFrameMode { get; set; } = false;

    // --- main-window placement (null means "not saved yet") ---
    //
    // All four are physical screen pixels and describe the window's *restored* rectangle,
    // even when it was closed maximized. Pixels rather than WPF units: the app is per-monitor
    // DPI aware, so a WPF unit is a different distance on every screen and cannot be restored
    // reliably. Kept as double for the sake of older settings files written in WPF units.
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public string WindowState { get; set; } = "Normal"; // Normal | Maximized

    /// <summary>Display the window was last on, e.g. <c>\\.\DISPLAY2</c>; empty = unknown.</summary>
    public string MonitorDevice { get; set; } = "";

    // That display's bounds at the time, in physical pixels. If the monitor is still
    // connected but has moved or changed resolution, the window is carried along with it
    // instead of being restored onto whatever now happens to occupy the old coordinates.
    public double? MonitorLeft { get; set; }
    public double? MonitorTop { get; set; }
    public double? MonitorWidth { get; set; }
    public double? MonitorHeight { get; set; }

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BooruFrame");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options);
                if (s is not null)
                    return s;
            }
        }
        catch
        {
            // ignore — use defaults
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch
        {
            // non-fatal
        }
    }
}
