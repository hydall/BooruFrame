using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BooruFrame.Booru;

/// <summary>
/// Loads and saves site presets as a small JSON file under %APPDATA%\BooruFrame.
/// This is a settings file, not an image cache — no image data ever touches the disk.
/// </summary>
public static class PresetStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BooruFrame");

    private static string FilePath => Path.Combine(Dir, "presets.json");

    public static List<BooruPreset> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var list = JsonSerializer.Deserialize<List<BooruPreset>>(json, Options);
                if (list is { Count: > 0 })
                    return list;
            }
        }
        catch
        {
            // Corrupt/unreadable config — fall back to defaults.
        }
        return Defaults();
    }

    public static void Save(IEnumerable<BooruPreset> presets)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(presets, Options));
        }
        catch
        {
            // Non-fatal: settings just won't persist this session.
        }
    }

    public static List<BooruPreset> Defaults() => new()
    {
        new BooruPreset { Name = "Danbooru", Engine = BooruEngine.Danbooru, BaseUrl = "https://danbooru.donmai.us", Enabled = true },
        new BooruPreset { Name = "Safebooru", Engine = BooruEngine.Gelbooru, BaseUrl = "https://safebooru.org", Enabled = true },
        new BooruPreset { Name = "Gelbooru", Engine = BooruEngine.Gelbooru, BaseUrl = "https://gelbooru.com", Enabled = false },
        new BooruPreset { Name = "Rule34", Engine = BooruEngine.Gelbooru, BaseUrl = "https://api.rule34.xxx", Enabled = false },
    };
}
