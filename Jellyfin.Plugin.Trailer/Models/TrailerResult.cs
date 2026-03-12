using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Trailer.Models;

/// <summary>
/// Response DTO returned by <c>GET /Trailer/{itemId}</c>.
/// Serialized as camelCase JSON by ASP.NET Core defaults.
/// </summary>
public class TrailerResult
{
    /// <summary>
    /// Full YouTube URL, e.g. <c>https://www.youtube.com/watch?v=XXXXXXXXXXX</c>.
    /// Null when <see cref="Found"/> is false.
    /// </summary>
    [JsonPropertyName("trailerUrl")]
    public string? TrailerUrl { get; set; }

    /// <summary>Human-readable trailer title from the provider.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Which provider returned this result.</summary>
    [JsonPropertyName("source")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TrailerSource Source { get; set; } = TrailerSource.None;

    /// <summary>ISO 639-1 language code of the trailer, e.g. "ru" or "en".</summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    /// <summary>True when a trailer URL was successfully resolved.</summary>
    [JsonPropertyName("found")]
    public bool Found => !string.IsNullOrEmpty(TrailerUrl);
}
