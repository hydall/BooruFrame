namespace BooruFrame.Booru;

/// <summary>
/// A saved site profile (like a Boorusama booru config): engine + host + optional
/// credentials, plus per-profile content filtering and tags injected into every search.
/// </summary>
public sealed class BooruPreset
{
    public string Name { get; set; } = "New site";
    public BooruEngine Engine { get; set; } = BooruEngine.Danbooru;
    public string BaseUrl { get; set; } = "";
    public string? User { get; set; }
    public string? ApiKey { get; set; }

    /// <summary>Whether images are randomized from this preset.</summary>
    public bool Enabled { get; set; } = true;

    // ---- per-preset search settings (Boorusama-style) ----

    public RatingFilter Filter { get; set; } = RatingFilter.None;

    /// <summary>For <see cref="RatingFilter.Custom"/>: ratings to exclude from search.</summary>
    public List<Rating> ExcludedRatings { get; set; } = new();

    /// <summary>Tags appended to every search from this preset.</summary>
    public string IncludeTags { get; set; } = "";

    /// <summary>Tags excluded (as -tag) from every search from this preset.</summary>
    public string ExcludeTags { get; set; } = "";

    /// <summary>Label shown in the presets list.</summary>
    public string Display => $"{Name}  ·  {Engine}";

    public BooruCredentials Credentials => new(User, ApiKey);

    public BooruPreset Clone() => new()
    {
        Name = Name,
        Engine = Engine,
        BaseUrl = BaseUrl,
        User = User,
        ApiKey = ApiKey,
        Enabled = Enabled,
        Filter = Filter,
        ExcludedRatings = new List<Rating>(ExcludedRatings),
        IncludeTags = IncludeTags,
        ExcludeTags = ExcludeTags,
    };
}
