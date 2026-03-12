using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Trailer.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Trailer.Services;

/// <summary>
/// Fetches trailers from Kinopoisk Unofficial API (kinopoiskapiunofficial.tech).
///
/// Flow:
///   1. If kinopoiskId is available → directly fetch /api/v2.2/films/{id}/videos
///   2. Otherwise → search /api/v2.1/films/search-by-keyword?keyword={name} → pick best match → fetch videos
///
/// Video priority:
///   1. site=YOUTUBE, videoType=TRAILER
///   2. site=YOUTUBE, videoType=TEASER
/// </summary>
public class KinopoiskTrailerProvider : IKinopoiskTrailerProvider
{
    private const string KpBaseUrl = "https://kinopoiskapiunofficial.tech";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<KinopoiskTrailerProvider> _logger;

    /// <summary>Initializes a new instance of <see cref="KinopoiskTrailerProvider"/>.</summary>
    public KinopoiskTrailerProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<KinopoiskTrailerProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TrailerResult> GetTrailerAsync(
        string? kinopoiskId,
        string movieName,
        int? year,
        string apiKey,
        CancellationToken cancellationToken)
    {
        // Step 1: resolve Kinopoisk ID if not provided
        if (string.IsNullOrEmpty(kinopoiskId))
        {
            kinopoiskId = await SearchByNameAsync(movieName, year, apiKey, cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrEmpty(kinopoiskId))
            {
                _logger.LogDebug("Kinopoisk search returned no results for '{Name}' ({Year})", movieName, year);
                return new TrailerResult();
            }
        }

        // Step 2: fetch videos for the resolved film ID
        return await FetchVideosAsync(kinopoiskId, apiKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Searches Kinopoisk for a film by name (and optionally year).
    /// Returns the numeric film ID string of the first matching result, or null.
    ///
    /// Endpoint: GET /api/v2.1/films/search-by-keyword?keyword={name}&amp;page=1
    /// Response:  { films: [ { filmId, nameRu, nameEn, year, ... } ] }
    /// </summary>
    private async Task<string?> SearchByNameAsync(
        string movieName,
        int? year,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var url = $"{KpBaseUrl}/api/v2.1/films/search-by-keyword"
                  + $"?keyword={Uri.EscapeDataString(movieName)}&page=1";

        try
        {
            using var client = CreateClient(apiKey);
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!doc.RootElement.TryGetProperty("films", out var films))
                return null;

            foreach (var film in films.EnumerateArray())
            {
                // Filter by year when known to avoid wrong-film matches
                if (year.HasValue
                    && film.TryGetProperty("year", out var yearProp)
                    && yearProp.ValueKind == JsonValueKind.String
                    && int.TryParse(yearProp.GetString(), out var filmYear)
                    && filmYear != year.Value)
                {
                    continue;
                }

                if (film.TryGetProperty("filmId", out var idProp)
                    && idProp.ValueKind == JsonValueKind.Number)
                {
                    return idProp.GetInt32().ToString();
                }
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Kinopoisk search HTTP error for '{Name}'", movieName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kinopoisk search unexpected error for '{Name}'", movieName);
        }

        return null;
    }

    /// <summary>
    /// Fetches videos for a film from Kinopoisk.
    ///
    /// Endpoint: GET /api/v2.2/films/{id}/videos
    /// Response:  { items: [ { url, name, site, videoType } ] }
    ///   videoType: "TRAILER" | "TEASER" | "VIDEO" | "CLIP" | "BEHIND_THE_SCENES" | ...
    ///   site:      "YOUTUBE" | "KINOPOISK_WIDGET" | ...
    /// </summary>
    private async Task<TrailerResult> FetchVideosAsync(
        string kinopoiskId,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var url = $"{KpBaseUrl}/api/v2.2/films/{kinopoiskId}/videos";

        try
        {
            using var client = CreateClient(apiKey);
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!doc.RootElement.TryGetProperty("items", out var items))
                return new TrailerResult();

            JsonElement? bestTrailer = null;
            JsonElement? bestTeaser = null;

            foreach (var video in items.EnumerateArray())
            {
                var site = GetString(video, "site");
                var videoUrl = GetString(video, "url");
                var videoType = GetString(video, "videoType");

                if (!string.Equals(site, "YOUTUBE", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.IsNullOrEmpty(videoUrl))
                    continue;

                if (string.Equals(videoType, "TRAILER", StringComparison.OrdinalIgnoreCase))
                    bestTrailer ??= video;
                else if (string.Equals(videoType, "TEASER", StringComparison.OrdinalIgnoreCase))
                    bestTeaser ??= video;
            }

            var chosen = bestTrailer ?? bestTeaser;
            if (chosen is null)
                return new TrailerResult();

            return new TrailerResult
            {
                TrailerUrl = GetString(chosen.Value, "url"),
                Title = GetString(chosen.Value, "name"),
                Source = TrailerSource.Kinopoisk,
                Language = "ru"
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Kinopoisk videos HTTP error for film {KpId}", kinopoiskId);
            return new TrailerResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kinopoisk videos unexpected error for film {KpId}", kinopoiskId);
            return new TrailerResult();
        }
    }

    /// <summary>
    /// Creates an HttpClient with the Kinopoisk API key header pre-set.
    /// Headers are set per-request to avoid modifying a shared client instance.
    /// </summary>
    private HttpClient CreateClient(string apiKey)
    {
        var client = _httpClientFactory.CreateClient("Kinopoisk");
        // NOTE: DefaultRequestHeaders is shared across requests on a named client.
        // Acceptable here because the API key doesn't change between requests.
        client.DefaultRequestHeaders.Remove("X-API-KEY");
        client.DefaultRequestHeaders.Add("X-API-KEY", apiKey);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop)
               && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }
}
