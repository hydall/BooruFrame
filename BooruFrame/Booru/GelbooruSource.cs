using System.Net.Http;
using System.Text.Json;

namespace BooruFrame.Booru;

/// <summary>
/// Gelbooru (gelbooru.com) search backend. The JSON API returns either a bare array
/// or an object of the shape {"@attributes": {...}, "post": [...]} — we handle both.
/// </summary>
public sealed class GelbooruSource : BooruSourceBase, IBooruSource
{
    public GelbooruSource(HttpClient http, string baseUrl) : base(http, baseUrl) { }

    public override string Name => "Gelbooru";

    public async Task<IReadOnlyList<BooruPost>> SearchAsync(
        string tags, BooruCredentials credentials, CancellationToken ct)
    {
        // Gelbooru randomizes via the "sort:random" meta tag.
        var withRandom = string.IsNullOrWhiteSpace(tags) ? "sort:random" : tags + " sort:random";

        var url = $"{BaseUrl}/index.php?page=dapi&s=post&q=index&json=1&limit=100&pid=0&tags={Uri.EscapeDataString(withRandom)}";
        if (credentials.HasValue)
            url += $"&user_id={Uri.EscapeDataString(credentials.User!)}&api_key={Uri.EscapeDataString(credentials.ApiKey!)}";

        await using var stream = await Http.GetStreamAsync(url, ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        var posts = new List<BooruPost>();
        var root = doc.RootElement;

        JsonElement array;
        if (root.ValueKind == JsonValueKind.Array)
        {
            array = root;
        }
        else if (root.ValueKind == JsonValueKind.Object
                 && root.TryGetProperty("post", out var postEl)
                 && postEl.ValueKind == JsonValueKind.Array)
        {
            array = postEl;
        }
        else
        {
            return posts; // no results (Gelbooru omits "post" when empty)
        }

        foreach (var el in array.EnumerateArray())
        {
            var fileUrl = GetString(el, "file_url");
            if (string.IsNullOrWhiteSpace(fileUrl))
                continue; // Gelbooru may return empty file_url — skip incomplete posts

            posts.Add(new BooruPost(
                fileUrl,
                RatingExtensions.ParseRating(GetString(el, "rating")),
                GetInt(el, "width"),
                GetInt(el, "height")));
        }
        return posts;
    }
}
