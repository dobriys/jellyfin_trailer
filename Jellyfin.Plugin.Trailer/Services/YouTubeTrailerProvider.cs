using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Trailer.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Trailer.Services;

/// <summary>
/// Searches a specific YouTube channel for trailers using the YouTube Data API v3.
///
/// Flow:
///   1. Resolve channel handle (@KinomanTrailers) → channel ID (UC...) — cached permanently
///   2. Search within that channel for "{movieName} трейлер {year}"
///   3. Return the first matching video URL
///
/// YouTube Data API v3 free tier: 10,000 units/day.
///   - channels.list (resolve handle) = 1 unit
///   - search.list = 100 units
///   → ~100 trailer searches per day, plenty for a personal Jellyfin server.
/// </summary>
public class YouTubeTrailerProvider : IYouTubeTrailerProvider
{
    private const string YtApiBase = "https://www.googleapis.com/youtube/v3";

    /// <summary>Cache: channel handle → channel ID (never expires, handles don't change).</summary>
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
        // Step 1: resolve channel handle → channel ID
        var channelId = await ResolveChannelIdAsync(channelHandle, apiKey, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(channelId))
        {
            _logger.LogWarning("YouTube: could not resolve channel handle '{Handle}' to ID", channelHandle);
            return new TrailerResult();
        }

        // Step 2: search within the channel
        var searchQuery = movieName + " трейлер";
        if (year.HasValue)
            searchQuery += " " + year.Value;

        return await SearchChannelAsync(channelId, searchQuery, movieName, apiKey, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves a YouTube channel handle (e.g. "@KinomanTrailers") to a channel ID (e.g. "UC...").
    /// Uses YouTube Data API v3: GET /channels?part=id&amp;forHandle=@handle
    /// Result is cached permanently (handles don't change).
    /// </summary>
    private async Task<string?> ResolveChannelIdAsync(
        string handle,
        string apiKey,
        CancellationToken cancellationToken)
    {
        // Normalize: ensure handle starts with @
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

    /// <summary>
    /// Searches for videos within a specific YouTube channel.
    /// Uses YouTube Data API v3: GET /search?part=snippet&amp;channelId=...&amp;q=...&amp;type=video
    /// </summary>
    private async Task<TrailerResult> SearchChannelAsync(
        string channelId,
        string query,
        string movieName,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var url = $"{YtApiBase}/search?part=snippet"
            + $"&channelId={Uri.EscapeDataString(channelId)}"
            + $"&q={Uri.EscapeDataString(query)}"
            + "&type=video"
            + "&maxResults=3"
            + "&relevanceLanguage=ru"
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

            // Take the first result
            var first = items[0];
            string? videoId = null;
            string? title = null;

            if (first.TryGetProperty("id", out var idObj)
                && idObj.TryGetProperty("videoId", out var vidIdProp)
                && vidIdProp.ValueKind == JsonValueKind.String)
            {
                videoId = vidIdProp.GetString();
            }

            if (first.TryGetProperty("snippet", out var snippet)
                && snippet.TryGetProperty("title", out var titleProp)
                && titleProp.ValueKind == JsonValueKind.String)
            {
                title = titleProp.GetString();
            }

            if (string.IsNullOrEmpty(videoId))
            {
                _logger.LogWarning("YouTube: search result has no videoId for '{Query}'", query);
                return new TrailerResult();
            }

            var videoUrl = $"https://www.youtube.com/watch?v={videoId}";
            _logger.LogInformation("YouTube: found trailer for '{Movie}': {Url} ({Title})", movieName, videoUrl, title);

            return new TrailerResult
            {
                TrailerUrl = videoUrl,
                Title = title,
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
