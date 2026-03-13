using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Trailer.Models;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Trailer.Services;

/// <summary>
/// Orchestrates trailer lookup across providers with in-memory caching.
///
/// Resolution order:
///   1. Check in-memory cache (keyed by Jellyfin item ID)
///   2. Resolve item from Jellyfin library → extract TMDb ID
///   3. Query TMDb → preferred language, then English fallback
///   4. If still not found and Kinopoisk is enabled → query Kinopoisk
///   5. Cache and return result (even negative results are cached to avoid hammering APIs)
/// </summary>
public class TrailerService : ITrailerService
{
    // ConcurrentDictionary is safe for concurrent reads and writes without locking.
    // Value tuple: (result, time it was cached).
    private static readonly ConcurrentDictionary<string, (TrailerResult Result, DateTime CachedAt)>
        Cache = new(StringComparer.OrdinalIgnoreCase);

    private readonly ILibraryManager _libraryManager;
    private readonly ITmdbTrailerProvider _tmdbProvider;
    private readonly IKinopoiskTrailerProvider _kinopoiskProvider;
    private readonly IYouTubeTrailerProvider _youtubeProvider;
    private readonly ILogger<TrailerService> _logger;

    /// <summary>Initializes a new instance of <see cref="TrailerService"/>.</summary>
    public TrailerService(
        ILibraryManager libraryManager,
        ITmdbTrailerProvider tmdbProvider,
        IKinopoiskTrailerProvider kinopoiskProvider,
        IYouTubeTrailerProvider youtubeProvider,
        ILogger<TrailerService> logger)
    {
        _libraryManager = libraryManager;
        _tmdbProvider = tmdbProvider;
        _kinopoiskProvider = kinopoiskProvider;
        _youtubeProvider = youtubeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TrailerResult> GetTrailerAsync(string itemId, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance!.Configuration;

        // ---- Cache check -------------------------------------------------------
        if (config.CacheDurationMinutes > 0 && Cache.TryGetValue(itemId, out var cached))
        {
            var age = DateTime.UtcNow - cached.CachedAt;
            if (age < TimeSpan.FromMinutes(config.CacheDurationMinutes))
            {
                _logger.LogDebug("Cache hit for item {ItemId}", itemId);
                return cached.Result;
            }

            Cache.TryRemove(itemId, out _);
        }

        // ---- Resolve Jellyfin item ---------------------------------------------
        if (!Guid.TryParse(itemId, out var guid))
        {
            _logger.LogWarning("Invalid item ID format: '{ItemId}'", itemId);
            return new TrailerResult();
        }

        var item = _libraryManager.GetItemById(guid);
        if (item is null)
        {
            _logger.LogWarning("Item {ItemId} not found in library", itemId);
            return new TrailerResult();
        }

        _logger.LogInformation("Resolving trailer for '{Title}' (id={ItemId}), ProviderIds=[{Ids}]",
            item.Name, itemId, string.Join(", ", item.ProviderIds.Select(kv => $"{kv.Key}={kv.Value}")));

        // ---- TMDb lookup -------------------------------------------------------
        var result = new TrailerResult();

        if (!string.IsNullOrEmpty(config.TmdbApiKey)
            && item.ProviderIds.TryGetValue("Tmdb", out var tmdbId)
            && !string.IsNullOrEmpty(tmdbId))
        {
            result = await _tmdbProvider.GetTrailerAsync(
                tmdbId,
                config.TmdbApiKey,
                config.PreferredLanguage,
                config.FallbackToEnglish,
                cancellationToken).ConfigureAwait(false);

            if (result.Found)
                _logger.LogInformation("TMDb trailer found for '{Title}': {Url}", item.Name, result.TrailerUrl);
        }
        else if (string.IsNullOrEmpty(config.TmdbApiKey))
        {
            _logger.LogInformation("TMDb API key not configured, skipping TMDb lookup");
        }
        else
        {
            _logger.LogInformation("No TMDb ID for item '{Title}'", item.Name);
        }

        // ---- Kinopoisk fallback ------------------------------------------------
        if (!result.Found && config.EnableKinopoisk && !string.IsNullOrEmpty(config.KinopoiskApiKey))
        {
            // Case-insensitive lookup: the КиноПоиск plugin stores the key as lowercase "kinopoisk"
            string? kpId = null;
            foreach (var kv in item.ProviderIds)
            {
                if (string.Equals(kv.Key, "Kinopoisk", StringComparison.OrdinalIgnoreCase))
                {
                    kpId = kv.Value;
                    break;
                }
            }

            _logger.LogInformation("Kinopoisk lookup for '{Title}': kpId={KpId}", item.Name, kpId ?? "(null, will search by name)");

            result = await _kinopoiskProvider.GetTrailerAsync(
                kpId,
                item.Name ?? string.Empty,
                item.ProductionYear,
                config.KinopoiskApiKey,
                cancellationToken).ConfigureAwait(false);

            if (result.Found)
                _logger.LogInformation("Kinopoisk trailer found for '{Title}': {Url}", item.Name, result.TrailerUrl);
        }

        // ---- YouTube channel search --------------------------------------------
        // Search for trailer on a specific YouTube channel (e.g. @KinomanTrailers)
        if (!result.Found
            && !string.IsNullOrEmpty(config.YouTubeApiKey)
            && !string.IsNullOrEmpty(config.YouTubeChannelHandle))
        {
            _logger.LogInformation("YouTube channel search for '{Title}' on {Channel}",
                item.Name, config.YouTubeChannelHandle);

            result = await _youtubeProvider.GetTrailerAsync(
                item.Name ?? string.Empty,
                item.ProductionYear,
                config.YouTubeApiKey,
                config.YouTubeChannelHandle,
                cancellationToken).ConfigureAwait(false);

            if (result.Found)
                _logger.LogInformation("YouTube trailer found for '{Title}': {Url}", item.Name, result.TrailerUrl);
        }

        if (!result.Found)
            _logger.LogWarning("No trailer found for '{Title}' (id={ItemId})", item.Name, itemId);

        // ---- Store in cache ----------------------------------------------------
        if (config.CacheDurationMinutes > 0)
            Cache[itemId] = (result, DateTime.UtcNow);

        return result;
    }

    /// <inheritdoc />
    public async Task<List<YouTubeSearchItem>> SearchTrailersAsync(
        string itemId, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance!.Configuration;

        if (string.IsNullOrEmpty(config.YouTubeApiKey))
        {
            _logger.LogWarning("YouTube API key not configured — cannot search trailers");
            return new List<YouTubeSearchItem>();
        }

        if (!Guid.TryParse(itemId, out var guid))
        {
            _logger.LogWarning("Invalid item ID format: '{ItemId}'", itemId);
            return new List<YouTubeSearchItem>();
        }

        var item = _libraryManager.GetItemById(guid);
        if (item is null)
        {
            _logger.LogWarning("Item {ItemId} not found in library", itemId);
            return new List<YouTubeSearchItem>();
        }

        _logger.LogInformation("YouTube search for trailers: '{Title}' ({Year})",
            item.Name, item.ProductionYear);

        return await _youtubeProvider.SearchAsync(
            item.Name ?? string.Empty,
            item.ProductionYear,
            config.YouTubeApiKey,
            8,
            cancellationToken).ConfigureAwait(false);
    }
}
