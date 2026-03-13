using System;
using System.Collections.Concurrent;
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
///
/// Two modes:
///   1. Channel search — searches a specific channel (e.g. @KinomanTrailers)
///   2. General search — searches all of YouTube, returns multiple results for user selection
///
/// YouTube Data API v3 free tier: 10,000 units/day.
///   - channels.list = 1 unit
///   - search.list   = 100 units
///   → ~100 searches per day, plenty for personal use.
/// </summary>
public class YouTubeTrailerProvider : IYouTubeTrailerProvider
{
    private const string YtApiBase = "https://www.googleapis.com/youtube/v3";

    /// <summary>Cache: channel handle → channel ID (never expires).</summary>
    private static readonly ConcurrentDictionary<string, string> ChannelIdCache = new(StringComparer.OrdinalIgnoreCase);

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
    public async Task<TrailerResult> GetTrailerAsync(
        string movieName,
        int? year,
        string apiKey,
        string channelHandle,
        CancellationToken cancellationToken)
    {
        var channelId = await ResolveChannelIdAsync(channelHandle, apiKey, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(channelId))
        {
            _logger.LogWarning("YouTube: could not resolve channel handle '{Handle}' to ID", channelHandle);
            return new TrailerResult();
        }

        var searchQuery = movieName + " трейлер";
        if (year.HasValue)
            searchQuery += " " + year.Value;

        return await SearchChannelAsync(channelId, searchQuery, movieName, apiKey, cancellationToken)
            .ConfigureAwait(false);
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
        if (maxResults < 1) maxResults = 1;
        if (maxResults > 10) maxResults = 10;

        var searchQuery = movieName + " трейлер";
        if (year.HasValue)
            searchQuery += " " + year.Value;

        var url = $"{YtApiBase}/search?part=snippet"
            + $"&q={Uri.EscapeDataString(searchQuery)}"
            + "&type=video"
            + $"&maxResults={maxResults}"
            + "&relevanceLanguage=ru"
            + $"&key={apiKey}";

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
        catch (Exception ex)
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
        string? title = null;
        string? channelTitle = null;
        string? thumbnailUrl = null;
        string? publishedAt = null;

        if (item.TryGetProperty("id", out var idObj)
            && idObj.TryGetProperty("videoId", out var vidIdProp)
            && vidIdProp.ValueKind == JsonValueKind.String)
        {
            videoId = vidIdProp.GetString();
        }

        if (string.IsNullOrEmpty(videoId))
            return null;

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

    /// <summary>Resolves a YouTube channel handle → channel ID (cached permanently).</summary>
    private async Task<string?> ResolveChannelIdAsync(
        string handle, string apiKey, CancellationToken cancellationToken)
    {
        if (!handle.StartsWith('@'))
            handle = "@" + handle;

        if (ChannelIdCache.TryGetValue(handle, out var cached))
            return cached;

        var url = $"{YtApiBase}/channels?part=id&forHandle={Uri.EscapeDataString(handle)}&key={apiKey}";
        _logger.LogInformation("YouTube: resolving channel handle '{Handle}'", handle);

        try
        {
            using var client = _httpClientFactory.CreateClient("YouTube");
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("YouTube channels API HTTP {Status}: {Body}", (int)response.StatusCode, body);
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var root = doc.RootElement;
            if (root.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
            {
                var firstItem = items[0];
                if (firstItem.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                {
                    var channelId = idProp.GetString()!;
                    ChannelIdCache[handle] = channelId;
                    _logger.LogInformation("YouTube: resolved '{Handle}' → {ChannelId}", handle, channelId);
                    return channelId;
                }
            }

            _logger.LogWarning("YouTube: no channel found for handle '{Handle}'", handle);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "YouTube: error resolving channel handle '{Handle}'", handle);
            return null;
        }
    }

    /// <summary>Searches within a specific YouTube channel (single best result).</summary>
    private async Task<TrailerResult> SearchChannelAsync(
        string channelId, string query, string movieName,
        string apiKey, CancellationToken cancellationToken)
    {
        var url = $"{YtApiBase}/search?part=snippet"
            + $"&channelId={Uri.EscapeDataString(channelId)}"
            + $"&q={Uri.EscapeDataString(query)}"
            + "&type=video&maxResults=3&relevanceLanguage=ru"
            + $"&key={apiKey}";

        _logger.LogInformation("YouTube: searching channel {ChannelId} for '{Query}'", channelId, query);

        try
        {
            using var client = _httpClientFactory.CreateClient("YouTube");
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("YouTube search API HTTP {Status}: {Body}", (int)response.StatusCode, body);
                return new TrailerResult();
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var root = doc.RootElement;
            if (!root.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
            {
                _logger.LogInformation("YouTube: no results for '{Query}' on channel {ChannelId}", query, channelId);
                return new TrailerResult();
            }

            var searchItem = ParseSearchItem(items[0]);
            if (searchItem == null || string.IsNullOrEmpty(searchItem.VideoId))
            {
                _logger.LogWarning("YouTube: search result has no videoId for '{Query}'", query);
                return new TrailerResult();
            }

            _logger.LogInformation("YouTube: found trailer for '{Movie}': {Url} ({Title})",
                movieName, searchItem.VideoUrl, searchItem.Title);

            return new TrailerResult
            {
                TrailerUrl = searchItem.VideoUrl,
                Title = searchItem.Title,
                Source = TrailerSource.YouTube,
                Language = "ru"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "YouTube: search error for '{Query}'", query);
            return new TrailerResult();
        }
    }
}
