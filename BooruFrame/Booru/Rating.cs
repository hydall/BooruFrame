namespace BooruFrame.Booru;

/// <summary>
/// Unified rating model, following the approach of khoadng/Boorusama.
/// Danbooru/Gelbooru expose single-letter codes (g/s/q/e); older Danbooru only s/q/e.
/// </summary>
public enum Rating
{
    Unknown,
    General,
    Sensitive,
    Questionable,
    Explicit,
}

public static class RatingExtensions
{
    /// <summary>Parse a rating from an API value (single letter or full word, case-insensitive).</summary>
    public static Rating ParseRating(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Rating.Unknown;

        return value.Trim().ToLowerInvariant() switch
        {
            "g" or "general" => Rating.General,
            "s" or "sensitive" or "safe" => Rating.Sensitive,
            "q" or "questionable" => Rating.Questionable,
            "e" or "explicit" => Rating.Explicit,
            _ => Rating.Unknown,
        };
    }

    /// <summary>NSFW = explicit or questionable (matches Boorusama's isNSFW()).</summary>
    public static bool IsNsfw(this Rating rating) =>
        rating is Rating.Explicit or Rating.Questionable;

    /// <summary>Single-letter code used by Danbooru/Moebooru (g/s/q/e).</summary>
    public static string ShortCode(this Rating rating) => rating switch
    {
        Rating.General => "g",
        Rating.Sensitive => "s",
        Rating.Questionable => "q",
        Rating.Explicit => "e",
        _ => "",
    };

    /// <summary>Full word used by the Gelbooru API (general/sensitive/questionable/explicit).</summary>
    public static string Word(this Rating rating) => rating switch
    {
        Rating.General => "general",
        Rating.Sensitive => "sensitive",
        Rating.Questionable => "questionable",
        Rating.Explicit => "explicit",
        _ => "",
    };
}
