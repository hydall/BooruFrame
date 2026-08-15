namespace BooruFrame.Booru;

/// <summary>
/// Builds the final search tag string for a preset, combining the live query with the
/// preset's include/exclude tags and content filter. Rating syntax is engine-aware,
/// following khoadng/Boorusama's query composers.
/// </summary>
public static class QueryComposer
{
    private static readonly char[] Separators = { ' ', ',', '\t', '\n', '\r' };

    /// <summary>Full search tags: user query + preset include/exclude + rating filter.</summary>
    public static string Compose(BooruPreset preset, string userTags)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(userTags))
            parts.Add(userTags.Trim());

        parts.AddRange(Tokens(preset.IncludeTags));

        foreach (var t in Tokens(preset.ExcludeTags))
            parts.Add(t.StartsWith('-') ? t : "-" + t);

        parts.AddRange(RatingTokens(preset));

        return string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    /// <summary>Only the tags this preset injects (no user query) — used for the editor preview.</summary>
    public static string Injection(BooruPreset preset)
    {
        var parts = new List<string>();
        parts.AddRange(Tokens(preset.IncludeTags));
        foreach (var t in Tokens(preset.ExcludeTags))
            parts.Add(t.StartsWith('-') ? t : "-" + t);
        parts.AddRange(RatingTokens(preset));
        return string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static IEnumerable<string> Tokens(string? raw) =>
        (raw ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries);

    private static IEnumerable<string> RatingTokens(BooruPreset preset)
    {
        var danbooru = preset.Engine == BooruEngine.Danbooru;

        switch (preset.Filter)
        {
            case RatingFilter.Moderate:
                yield return danbooru ? "-rating:e" : "-rating:explicit";
                break;

            case RatingFilter.Aggressive:
                yield return danbooru ? "rating:g" : "rating:general";
                break;

            case RatingFilter.Custom:
                foreach (var r in preset.ExcludedRatings)
                {
                    var code = danbooru ? r.ShortCode() : r.Word();
                    if (!string.IsNullOrEmpty(code))
                        yield return "-rating:" + code;
                }
                break;
        }
    }
}
