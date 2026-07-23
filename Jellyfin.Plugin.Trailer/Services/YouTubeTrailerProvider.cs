using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Trailer.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Trailer.Services;

/// <summary>
/// Searches YouTube for trailers using the YouTube Data API v3.
/// Returns multiple results so the user can pick which one to watch.
///
/// YouTube Data API v3 free tier: 10,000 units/day.
///   - search.list = 100 units → ~100 searches per day, plenty for personal use.
/// </summary>
public class YouTubeTrailerProvider : IYouTubeTrailerProvider
{
    private const string YtApiBase = "https://www.googleapis.com/youtube/v3";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<YouTubeTrailerProvider> _logger;

    /// <summary>Initializes a new instance of <see cref="YouTubeTrailerProvider"/>.</summary>
    public YouTubeTrailerProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<YouTubeTrailerProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<YouTubeSearchItem>> SearchAsync(
        string movieName,
        int? year,
        string apiKey,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var results = new List<YouTubeSearchItem>();
        maxResults = Math.Clamp(maxResults, 1, 10);

        var searchQuery = movieName + " трейлер";
        if (year.HasValue)
            searchQuery += " " + year.Value;

        var url = $"{YtApiBase}/search?part=snippet"
            + $"&q={Uri.EscapeDataString(searchQuery)}"
            + "&type=video"
            + $"&maxResults={maxResults}"
            + "&relevanceLanguage=ru"
            + $"&key={Uri.EscapeDataString(apiKey)}";

        _logger.LogInformation("YouTube general search for '{Query}', maxResults={Max}", searchQuery, maxResults);

        try
        {
            using var client = _httpClientFactory.CreateClient("YouTube");
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("YouTube search API HTTP {Status}: {Body}", (int)response.StatusCode, body);
                return results;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var root = doc.RootElement;
            if (!root.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
            {
                _logger.LogInformation("YouTube: no results for '{Query}'", searchQuery);
                return results;
            }

            foreach (var item in items.EnumerateArray())
            {
                var searchItem = ParseSearchItem(item);
                if (searchItem != null)
                {
                    results.Add(searchItem);
                    _logger.LogInformation("YouTube result: {Title} ({Channel}) — {VideoId}",
                        searchItem.Title, searchItem.ChannelTitle, searchItem.VideoId);
                }
            }

            _logger.LogInformation("YouTube: found {Count} results for '{Query}'", results.Count, searchQuery);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogError(ex, "YouTube: general search error for '{Query}'", searchQuery);
        }

        return results;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>Parses a single YouTube search result item from the API response.</summary>
    private static YouTubeSearchItem? ParseSearchItem(JsonElement item)
    {
        string? videoId = null;

        if (item.TryGetProperty("id", out var idObj)
            && idObj.TryGetProperty("videoId", out var vidIdProp)
            && vidIdProp.ValueKind == JsonValueKind.String)
        {
            videoId = vidIdProp.GetString();
        }

        if (string.IsNullOrEmpty(videoId))
            return null;

        string? title = null;
        string? channelTitle = null;
        string? thumbnailUrl = null;
        string? publishedAt = null;

        if (item.TryGetProperty("snippet", out var snippet))
        {
            if (snippet.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
                title = t.GetString();

            if (snippet.TryGetProperty("channelTitle", out var ch) && ch.ValueKind == JsonValueKind.String)
                channelTitle = ch.GetString();

            if (snippet.TryGetProperty("publishedAt", out var pa) && pa.ValueKind == JsonValueKind.String)
                publishedAt = pa.GetString();

            // Prefer medium (320×180) thumbnail, fall back to default (120×90)
            if (snippet.TryGetProperty("thumbnails", out var thumbs))
            {
                if (thumbs.TryGetProperty("medium", out var med) && med.TryGetProperty("url", out var medUrl))
                    thumbnailUrl = medUrl.GetString();
                else if (thumbs.TryGetProperty("default", out var def) && def.TryGetProperty("url", out var defUrl))
                    thumbnailUrl = defUrl.GetString();
            }
        }

        return new YouTubeSearchItem
        {
            VideoId = videoId!,
            Title = title ?? string.Empty,
            ChannelTitle = channelTitle ?? string.Empty,
            ThumbnailUrl = thumbnailUrl ?? string.Empty,
            PublishedAt = publishedAt ?? string.Empty
        };
    }
}
