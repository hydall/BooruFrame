using System.Net.Http;
using System.Text.Json;

namespace BooruFrame.Booru;

/// <summary>Danbooru (danbooru.donmai.us) search backend. Response is a JSON array of posts.</summary>
public sealed class DanbooruSource : BooruSourceBase, IBooruSource
{
    public DanbooruSource(HttpClient http, string baseUrl) : base(http, baseUrl) { }

    public override string Name => "Danbooru";

    public async Task<IReadOnlyList<BooruPost>> SearchAsync(
        string tags, BooruCredentials credentials, CancellationToken ct)
    {
        // Danbooru randomizes server-side via random=true.
        var url = $"{BaseUrl}/posts.json?limit=100&random=true&tags={Uri.EscapeDataString(tags)}";
        if (credentials.HasValue)
            url += $"&login={Uri.EscapeDataString(credentials.User!)}&api_key={Uri.EscapeDataString(credentials.ApiKey!)}";

        await using var stream = await Http.GetStreamAsync(url, ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        var posts = new List<BooruPost>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return posts;

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var fileUrl = GetString(el, "file_url");
            if (string.IsNullOrWhiteSpace(fileUrl))
                continue;

            posts.Add(new BooruPost(
                fileUrl,
                RatingExtensions.ParseRating(GetString(el, "rating")),
                GetInt(el, "image_width"),
                GetInt(el, "image_height")));
        }
        return posts;
    }
}
