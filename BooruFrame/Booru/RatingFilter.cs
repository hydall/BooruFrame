namespace BooruFrame.Booru;

/// <summary>
/// Content filtering presets, mirroring Boorusama's BooruConfigRatingFilter
/// (none / hideExplicit / hideNSFW / custom). Query tag composition lives in
/// <see cref="QueryComposer"/> because the syntax differs per engine.
/// </summary>
public enum RatingFilter
{
    /// <summary>Show everything.</summary>
    None,

    /// <summary>Moderate — hide explicit content.</summary>
    Moderate,

    /// <summary>Aggressive — keep only general/safe.</summary>
    Aggressive,

    /// <summary>Custom — exclude a user-chosen set of ratings.</summary>
    Custom,
}
