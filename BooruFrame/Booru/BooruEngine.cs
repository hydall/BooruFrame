using System.Net.Http;

namespace BooruFrame.Booru;

/// <summary>
/// The API dialect a site speaks. Many booru sites share one of these two engines on
/// different hosts (e.g. Safebooru and Rule34 both speak the Gelbooru API).
/// </summary>
public enum BooruEngine
{
    Danbooru,
    Gelbooru,
}

public static class BooruEngineFactory
{
    /// <summary>Build a search source for the given engine bound to a specific host.</summary>
    public static IBooruSource Create(BooruEngine engine, HttpClient http, string baseUrl) => engine switch
    {
        BooruEngine.Danbooru => new DanbooruSource(http, baseUrl),
        _ => new GelbooruSource(http, baseUrl),
    };

    /// <summary>A sensible default host for a freshly created preset of this engine.</summary>
    public static string DefaultBaseUrl(BooruEngine engine) => engine switch
    {
        BooruEngine.Danbooru => "https://danbooru.donmai.us",
        _ => "https://gelbooru.com",
    };
}
