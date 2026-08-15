namespace BooruFrame.Booru;

/// <summary>Optional API credentials for a booru source.</summary>
public readonly record struct BooruCredentials(string? User, string? ApiKey)
{
    public bool HasValue =>
        !string.IsNullOrWhiteSpace(User) && !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>A booru backend that can search posts by tags.</summary>
public interface IBooruSource
{
    string Name { get; }

    /// <summary>
    /// Search up to a page of posts for the given (already composed) tag string,
    /// asking the server for a randomized order where possible.
    /// </summary>
    Task<IReadOnlyList<BooruPost>> SearchAsync(
        string tags,
        BooruCredentials credentials,
        CancellationToken ct);
}
