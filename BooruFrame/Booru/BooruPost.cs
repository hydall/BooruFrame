namespace BooruFrame.Booru;

/// <summary>A single searchable post reduced to what this app needs.</summary>
public sealed record BooruPost(string FileUrl, Rating Rating, int Width, int Height)
{
    // Still-image formats we can display. WebP is decoded via ImageSharp fallback
    // (WPF has no built-in WebP codec); videos (.webm/.mp4) are not listed.
    private static readonly string[] ImageExtensions =
        { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };

    /// <summary>True when the file URL points at a still image we can display (not a video).</summary>
    public bool IsDisplayableImage()
    {
        if (string.IsNullOrWhiteSpace(FileUrl))
            return false;

        // Strip any query string before checking the extension.
        var url = FileUrl;
        var q = url.IndexOf('?');
        if (q >= 0)
            url = url[..q];

        foreach (var ext in ImageExtensions)
        {
            if (url.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
