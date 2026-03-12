using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Trailer.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Trailer.Services;

/// <summary>
/// Fetches trailers from TMDb API v3 endpoint:
/// <c>GET /movie/{tmdbId}/videos?api_key=...&amp;language=ru-RU</c>
///
/// Priority for selecting the best video:
///   1. YouTube, type=Trailer, official=true
///   2. YouTube, type=Trailer (any)
///   3. YouTube, type=Teaser or other (last resort)
/// </summary>
public class TmdbTrailerProvider : ITmdbTrailerProvider
{
    private const string TmdbBaseUrl = "https://api.themoviedb.org/3";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TmdbTrailerProvider> _logger;

    /// <summary>Initializes a new instance of <see cref="TmdbTrailerProvider"/>.</summary>
    public TmdbTrailerProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<TmdbTrailerProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TrailerResult> GetTrailerAsync(
        string tmdbId,
        string apiKey,
        string language,
        bool fallbackToEnglish,
        CancellationToken cancellationToken)
    {
        var result = await FetchAsync(tmdbId, apiKey, language, cancellationToken).ConfigureAwait(false);

        if (!result.Found
            && fallbackToEnglish
            && !language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "No {Language} trailer for TMDb {Id}, retrying with en-US",
                language, tmdbId);
            result = await FetchAsync(tmdbId, apiKey, "en-US", cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private async Task<TrailerResult> FetchAsync(
        string tmdbId,
        string apiKey,
        string language,
        CancellationToken cancellationToken)
    {
        var url = $"{TmdbBaseUrl}/movie/{tmdbId}/videos?api_key={apiKey}&language={language}";

        try
        {
            using var client = _httpClientFactory.CreateClient("TMDb");
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!doc.RootElement.TryGetProperty("results", out var results))
                return new TrailerResult();

            // Scan all results and pick by priority
            JsonElement? officialTrailer = null;
            JsonElement? anyTrailer = null;
            JsonElement? anyYoutube = null;

            foreach (var video in results.EnumerateArray())
            {
                if (!TryGetString(video, "site", out var site) ||
                    !string.Equals(site, "YouTube", StringComparison.OrdinalIgnoreCase))
                    continue;

                TryGetString(video, "type", out var type);
                var isTrailer = string.Equals(type, "Trailer", StringComparison.OrdinalIgnoreCase);
                var isOfficial = video.TryGetProperty("official", out var offProp)
                                 && offProp.ValueKind == JsonValueKind.True;

                if (isTrailer && isOfficial)
                    officialTrailer ??= video;
                else if (isTrailer)
                    anyTrailer ??= video;
                else
                    anyYoutube ??= video;
            }

            var chosen = officialTrailer ?? anyTrailer ?? anyYoutube;
            if (chosen is null)
                return new TrailerResult();

            TryGetString(chosen.Value, "key", out var key);
            TryGetString(chosen.Value, "name", out var name);

            if (string.IsNullOrEmpty(key))
                return new TrailerResult();

            return new TrailerResult
            {
                TrailerUrl = $"https://www.youtube.com/watch?v={key}",
                Title = name,
                Source = TrailerSource.Tmdb,
                Language = language.Length >= 2 ? language[..2].ToLowerInvariant() : language
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "TMDb HTTP error for movie {TmdbId} lang={Language}", tmdbId, language);
            return new TrailerResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TMDb unexpected error for movie {TmdbId}", tmdbId);
            return new TrailerResult();
        }
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string? value)
    {
        if (element.TryGetProperty(propertyName, out var prop)
            && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString();
            return true;
        }

        value = null;
        return false;
    }
}
