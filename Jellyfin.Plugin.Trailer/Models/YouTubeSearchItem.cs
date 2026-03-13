using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Trailer.Models;

/// <summary>
/// A single YouTube search result returned by <c>GET /Trailer/{itemId}/search</c>.
/// </summary>
public class YouTubeSearchItem
{
    /// <summary>YouTube video ID (11 chars).</summary>
    [JsonPropertyName("videoId")]
    public string VideoId { get; set; } = string.Empty;

    /// <summary>Video title.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Channel name that uploaded the video.</summary>
    [JsonPropertyName("channelTitle")]
    public string ChannelTitle { get; set; } = string.Empty;

    /// <summary>Thumbnail URL (medium quality, 320×180).</summary>
    [JsonPropertyName("thumbnailUrl")]
    public string ThumbnailUrl { get; set; } = string.Empty;

    /// <summary>Publish date in ISO format.</summary>
    [JsonPropertyName("publishedAt")]
    public string PublishedAt { get; set; } = string.Empty;

    /// <summary>Full YouTube watch URL.</summary>
    [JsonPropertyName("videoUrl")]
    public string VideoUrl => string.IsNullOrEmpty(VideoId)
        ? string.Empty
        : $"https://www.youtube.com/watch?v={VideoId}";
}
