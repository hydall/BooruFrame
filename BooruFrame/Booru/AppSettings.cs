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

    // --- window placement (null means "not saved yet") ---
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public string WindowState { get; set; } = "Normal"; // Normal | Maximized
    public string MonitorDevice { get; set; } = "";

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
