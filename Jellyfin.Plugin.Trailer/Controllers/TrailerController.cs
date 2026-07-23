using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Net.Mime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Trailer.Models;
using Jellyfin.Plugin.Trailer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Trailer.Controllers;

/// <summary>
/// Provides the trailer lookup REST API consumed by the client-side JavaScript.
/// </summary>
[ApiController]
[Route("Trailer")]
[Produces(MediaTypeNames.Application.Json)]
public class TrailerController : ControllerBase
{
    /// <summary>Invidious instances used for stream resolution and whitelisted in the proxy.</summary>
    private static readonly string[] InvidiousHosts =
    {
        "https://inv.nadeko.net",
        "https://invidious.nerdvpn.de",
        "https://yewtu.be"
    };

    private const string DesktopUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private readonly ITrailerService _trailerService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TrailerController> _logger;

    /// <summary>Initializes a new instance of <see cref="TrailerController"/>.</summary>
    public TrailerController(
        ITrailerService trailerService,
        IHttpClientFactory httpClientFactory,
        ILogger<TrailerController> logger)
    {
        _trailerService = trailerService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Searches YouTube for trailers and returns multiple results.
    /// The client-side JS shows these in a modal list for user selection.
    /// </summary>
    /// <param name="itemId">Jellyfin item GUID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Array of YouTube search items.</returns>
    [HttpGet("{itemId}/search")]
    [Authorize]
    [ProducesResponseType(typeof(List<YouTubeSearchItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<YouTubeSearchItem>>> SearchTrailersAsync(
        [FromRoute][Required] string itemId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return BadRequest("itemId is required");

        _logger.LogDebug("YouTube trailer search for item {ItemId}", itemId);

        var results = await _trailerService.SearchTrailersAsync(itemId, cancellationToken)
            .ConfigureAwait(false);

        return Ok(results);
    }

    /// <summary>
    /// Proxies a YouTube thumbnail through the Jellyfin server.
    /// Used when the client cannot reach i.ytimg.com (DNS blocked).
    /// Only allows URLs from i.ytimg.com — no auth required (public thumbnails).
    /// </summary>
    /// <param name="url">YouTube thumbnail URL (must be from i.ytimg.com).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("thumb")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> ProxyThumbnail(
        [FromQuery][Required] string url,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return BadRequest("Valid absolute URL is required");

        // Only allow YouTube image CDN to prevent abuse
        if (!uri.Host.EndsWith("ytimg.com", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Only ytimg.com URLs are allowed");

        try
        {
            var client = _httpClientFactory.CreateClient("TrailerProxy");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            HttpContext.Response.RegisterForDispose(response);

            if (!response.IsSuccessStatusCode)
                return StatusCode(502, "Upstream returned " + (int)response.StatusCode);

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            // Cache thumbnails for 24 hours
            Response.Headers["Cache-Control"] = "public, max-age=86400";
            return File(stream, contentType);
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, "Failed to fetch thumbnail");
        }
        catch (TaskCanceledException)
        {
            return StatusCode(504, "Thumbnail request timed out");
        }
    }

    /// <summary>
    /// Resolves a direct video stream URL for a YouTube video.
    /// Uses multiple strategies: watch page scraping, player API, Invidious.
    /// Returns a proxied URL that the client can play via HTML5 &lt;video&gt;.
    /// </summary>
    [HttpGet("stream/{videoId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> ResolveStream(
        [FromRoute][Required] string videoId,
        CancellationToken cancellationToken)
    {
        if (!IsValidVideoId(videoId))
            return BadRequest("Valid 11-character YouTube video ID is required");

        // ── Strategy 1: Scrape YouTube watch page (most reliable, like yt-dlp) ──
        var result = await TryWatchPageScrape(videoId, cancellationToken).ConfigureAwait(false);
        if (result != null) return Ok(result);

        // ── Strategy 2: YouTube player API with TV embedded client ──
        result = await TryYouTubePlayerApi(videoId, "TVHTML5_SIMPLY_EMBEDDED_PLAYER", "2.0", cancellationToken).ConfigureAwait(false);
        if (result != null) return Ok(result);

        // ── Strategy 3: YouTube player API with ANDROID client ──
        result = await TryYouTubePlayerApi(videoId, "ANDROID", "19.09.37", cancellationToken).ConfigureAwait(false);
        if (result != null) return Ok(result);

        // ── Strategy 4: Invidious fallback ──
        result = await TryInvidiousApi(videoId, cancellationToken).ConfigureAwait(false);
        if (result != null) return Ok(result);

        return StatusCode(502, "Could not resolve video stream from any source");
    }

    /// <summary>
    /// Debug endpoint — shows what each strategy returns for a video.
    /// Helps diagnose stream resolution failures.
    /// </summary>
    [HttpGet("debug/{videoId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> DebugStream(
        [FromRoute][Required] string videoId,
        CancellationToken cancellationToken)
    {
        if (!IsValidVideoId(videoId))
            return BadRequest("Valid 11-character YouTube video ID is required");

        var results = new Dictionary<string, object?>();

        // Test watch page scrape
        try
        {
            var client = _httpClientFactory.CreateClient("YouTubeResolve");
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://www.youtube.com/watch?v={videoId}");
            req.Headers.Add("User-Agent", DesktopUserAgent);
            req.Headers.Add("Accept-Language", "ru-RU,ru;q=0.9,en;q=0.8");

            using var resp = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
            var html = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var playerJson = ExtractPlayerResponse(html);
            if (playerJson != null)
            {
                using var doc = JsonDocument.Parse(playerJson);
                var playability = doc.RootElement.TryGetProperty("playabilityStatus", out var ps)
                    ? (ps.TryGetProperty("status", out var st) ? st.GetString() : "unknown")
                    : "not found";
                var hasSd = doc.RootElement.TryGetProperty("streamingData", out var sd);
                int formatCount = 0;
                int adaptiveCount = 0;
                if (hasSd)
                {
                    if (sd.TryGetProperty("formats", out var fmts))
                        formatCount = fmts.GetArrayLength();
                    if (sd.TryGetProperty("adaptiveFormats", out var af))
                        adaptiveCount = af.GetArrayLength();
                }

                results["watchPage"] = new
                {
                    status = (int)resp.StatusCode,
                    htmlLength = html.Length,
                    playabilityStatus = playability,
                    muxedFormats = formatCount,
                    adaptiveFormats = adaptiveCount,
                    hasDirectUrls = formatCount > 0 || adaptiveCount > 0
                };
            }
            else
            {
                results["watchPage"] = new { status = (int)resp.StatusCode, htmlLength = html.Length, error = "ytInitialPlayerResponse not found in HTML" };
            }
        }
        catch (Exception ex)
        {
            results["watchPage"] = new { error = ex.Message };
        }

        // Test player API
        foreach (var clientName in new[] { "TVHTML5_SIMPLY_EMBEDDED_PLAYER", "ANDROID" })
        {
            try
            {
                var client = _httpClientFactory.CreateClient("YouTubeResolve");
                var payload = BuildPlayerPayload(videoId, clientName, clientName == "ANDROID" ? "19.09.37" : "2.0");
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://www.youtube.com/youtubei/v1/player")
                {
                    Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
                };
                req.Headers.Add("User-Agent", UserAgentFor(clientName));

                using var resp = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
                var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (resp.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(json);
                    var playability = doc.RootElement.TryGetProperty("playabilityStatus", out var ps)
                        ? (ps.TryGetProperty("status", out var st) ? st.GetString() : "unknown")
                        : "not found";
                    var reason = doc.RootElement.TryGetProperty("playabilityStatus", out var ps2)
                        && ps2.TryGetProperty("reason", out var r) ? r.GetString() : null;

                    results[$"playerApi_{clientName}"] = new { status = (int)resp.StatusCode, playabilityStatus = playability, reason };
                }
                else
                {
                    results[$"playerApi_{clientName}"] = new { status = (int)resp.StatusCode, body = json.Length > 500 ? json[..500] : json };
                }
            }
            catch (Exception ex)
            {
                results[$"playerApi_{clientName}"] = new { error = ex.Message };
            }
        }

        return Ok(results);
    }

    private async Task<object?> TryWatchPageScrape(string videoId, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("YouTubeResolve");

            var watchUrl = $"https://www.youtube.com/watch?v={videoId}";
            using var request = new HttpRequestMessage(HttpMethod.Get, watchUrl);
            request.Headers.Add("User-Agent", DesktopUserAgent);
            request.Headers.Add("Accept-Language", "ru-RU,ru;q=0.9,en;q=0.8");
            request.Headers.Add("Accept", "text/html,application/xhtml+xml");

            _logger.LogInformation("Scraping YouTube watch page for {VideoId}", videoId);

            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("YouTube watch page returned {Status} for {VideoId}", (int)response.StatusCode, videoId);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("YouTube watch page fetched: {Length} chars for {VideoId}", html.Length, videoId);

            var playerJson = ExtractPlayerResponse(html);
            if (playerJson == null)
            {
                _logger.LogWarning("ytInitialPlayerResponse not found in HTML for {VideoId}", videoId);
                return null;
            }

            return ParseStreamingData(playerJson, videoId, "watch-page");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Watch page scrape failed for {VideoId}", videoId);
        }

        return null;
    }

    private async Task<object?> TryYouTubePlayerApi(string videoId, string clientName, string clientVersion, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("YouTubeResolve");

            var payload = BuildPlayerPayload(videoId, clientName, clientVersion);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.youtube.com/youtubei/v1/player")
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.Add("User-Agent", UserAgentFor(clientName));

            _logger.LogInformation("Trying YouTube player API ({Client}) for {VideoId}", clientName, videoId);

            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("YouTube player API ({Client}) returned {Status}", clientName, (int)response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseStreamingData(json, videoId, $"player-api-{clientName}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "YouTube player API ({Client}) failed for {VideoId}", clientName, videoId);
        }

        return null;
    }

    private static string UserAgentFor(string clientName) => clientName switch
    {
        "ANDROID" => "com.google.android.youtube/19.09.37 (Linux; U; Android 11) gzip",
        "TVHTML5_SIMPLY_EMBEDDED_PLAYER" => "Mozilla/5.0 (SMART-TV; Linux; Tizen 5.0) AppleWebKit/537.36 Chrome/79.0.3945.130 Safari/537.36",
        _ => DesktopUserAgent
    };

    private static string BuildPlayerPayload(string videoId, string clientName, string clientVersion)
    {
        // Build the payload manually to handle different client configs
        if (clientName == "ANDROID")
        {
            return JsonSerializer.Serialize(new
            {
                videoId,
                context = new
                {
                    client = new
                    {
                        clientName = "ANDROID",
                        clientVersion,
                        androidSdkVersion = 30,
                        hl = "ru",
                        gl = "RU"
                    }
                }
            });
        }

        return JsonSerializer.Serialize(new
        {
            videoId,
            context = new
            {
                client = new
                {
                    clientName,
                    clientVersion,
                    hl = "ru",
                    gl = "RU"
                }
            }
        });
    }

    /// <summary>Extracts ytInitialPlayerResponse JSON from YouTube watch page HTML.</summary>
    private static string? ExtractPlayerResponse(string html)
    {
        // Look for: var ytInitialPlayerResponse = {...};
        var marker = "var ytInitialPlayerResponse = ";
        var startIdx = html.IndexOf(marker, StringComparison.Ordinal);
        if (startIdx < 0)
        {
            // Try alternate pattern (sometimes in different format)
            marker = "ytInitialPlayerResponse = ";
            startIdx = html.IndexOf(marker, StringComparison.Ordinal);
            if (startIdx < 0) return null;
        }

        startIdx += marker.Length;

        // Find matching closing brace using depth counting
        if (startIdx >= html.Length || html[startIdx] != '{') return null;

        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = startIdx; i < html.Length; i++)
        {
            char c = html[i];

            if (escaped) { escaped = false; continue; }
            if (c == '\\') { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return html.Substring(startIdx, i - startIdx + 1);
            }
        }

        return null;
    }

    /// <summary>Parses streamingData from YouTube player response JSON and returns best muxed stream URL.</summary>
    private object? ParseStreamingData(string json, string videoId, string source)
    {
        using var doc = JsonDocument.Parse(json);

        // Check playability
        if (doc.RootElement.TryGetProperty("playabilityStatus", out var status))
        {
            var statusStr = status.TryGetProperty("status", out var s) ? s.GetString() : "";
            var reason = status.TryGetProperty("reason", out var r) ? r.GetString() : "";
            if (statusStr != "OK")
            {
                _logger.LogWarning("{Source}: video {VideoId} status={Status} reason={Reason}",
                    source, videoId, statusStr, reason);
                return null;
            }
        }

        if (!doc.RootElement.TryGetProperty("streamingData", out var streamingData))
        {
            _logger.LogWarning("{Source}: no streamingData for {VideoId}", source, videoId);
            return null;
        }

        // formats = muxed audio+video (ready for <video>)
        if (streamingData.TryGetProperty("formats", out var formats))
        {
            var (bestUrl, bestQuality) = PickBestMuxed(formats);
            if (!string.IsNullOrEmpty(bestUrl))
            {
                _logger.LogInformation("{Source}: resolved {Quality} for {VideoId}", source, bestQuality, videoId);
                return BuildProxyResult(bestUrl, bestQuality, source);
            }
        }

        _logger.LogWarning("{Source}: no usable muxed formats for {VideoId} (likely signatureCipher-only)", source, videoId);
        return null;
    }

    private async Task<object?> TryInvidiousApi(string videoId, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("YouTubeResolve");

        foreach (var host in InvidiousHosts)
        {
            try
            {
                var apiUrl = $"{host}/api/v1/videos/{videoId}?fields=formatStreams";
                _logger.LogInformation("Trying Invidious {Host} for {VideoId}", host, videoId);

                using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                request.Headers.Add("Accept", "application/json");
                request.Headers.Add("User-Agent", DesktopUserAgent);

                using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Invidious {Host} returned {Status}", host, (int)response.StatusCode);
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("formatStreams", out var streams))
                {
                    var (bestUrl, bestQuality) = PickBestMuxed(streams);
                    if (!string.IsNullOrEmpty(bestUrl))
                    {
                        _logger.LogInformation("Invidious resolved {Quality} for {VideoId} via {Host}",
                            bestQuality, videoId, host);
                        return BuildProxyResult(bestUrl, bestQuality, host);
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogWarning(ex, "Invidious {Host} failed for {VideoId}", host, videoId);
            }
        }

        return null;
    }

    /// <summary>
    /// Picks the highest-resolution muxed format that carries a direct URL
    /// (skips signatureCipher-only formats). Works for both YouTube
    /// <c>streamingData.formats</c> and Invidious <c>formatStreams</c>.
    /// </summary>
    private static (string? Url, string? Quality) PickBestMuxed(JsonElement formats)
    {
        string? bestUrl = null;
        string? bestQuality = null;
        int bestRes = -1;

        foreach (var fmt in formats.EnumerateArray())
        {
            if (!fmt.TryGetProperty("url", out var u) || u.ValueKind != JsonValueKind.String)
                continue;

            var url = u.GetString();
            if (string.IsNullOrEmpty(url))
                continue;

            var quality = fmt.TryGetProperty("qualityLabel", out var q) && q.ValueKind == JsonValueKind.String
                ? q.GetString()
                : null;

            var res = ParseResolution(quality);
            if (res > bestRes)
            {
                bestRes = res;
                bestUrl = url;
                bestQuality = quality;
            }
        }

        return (bestUrl, bestQuality);
    }

    /// <summary>Parses the leading number from a quality label like "720p" → 720. Returns 0 if none.</summary>
    private static int ParseResolution(string? qualityLabel)
    {
        if (string.IsNullOrEmpty(qualityLabel))
            return 0;

        int i = 0;
        while (i < qualityLabel.Length && char.IsDigit(qualityLabel[i]))
            i++;

        return i > 0 && int.TryParse(qualityLabel.AsSpan(0, i), out var n) ? n : 0;
    }

    private static object BuildProxyResult(string directUrl, string? quality, string source)
    {
        var proxyUrl = "/Trailer/proxy?url=" + Uri.EscapeDataString(directUrl);
        return new { streamUrl = proxyUrl, quality, source };
    }

    private static bool IsValidVideoId(string videoId) =>
        !string.IsNullOrWhiteSpace(videoId) && videoId.Length == 11;

    /// <summary>
    /// Proxies a remote video URL through the Jellyfin server.
    /// Used when direct browser access to the video host is blocked (e.g. DNS filtering).
    /// The server fetches the video and streams it to the client.
    /// </summary>
    /// <param name="url">The remote video URL to proxy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Video stream.</response>
    /// <response code="400">Missing or invalid URL.</response>
    /// <response code="502">Upstream server error.</response>
    [HttpGet("proxy")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> ProxyVideo(
        [FromQuery][Required] string url,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return BadRequest("Valid absolute URL is required");

        // Only allow http/https schemes to prevent SSRF
        if (uri.Scheme != "http" && uri.Scheme != "https")
            return BadRequest("Only http/https URLs are supported");

        // Restrict to known video hosts to prevent open proxy abuse
        if (!IsAllowedProxyHost(uri))
            return BadRequest("URL host is not allowed");

        _logger.LogInformation("Proxying video: {Host}{Path}", uri.Host, uri.AbsolutePath);

        try
        {
            var client = _httpClientFactory.CreateClient("TrailerProxy");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add("User-Agent", DesktopUserAgent);

            // Forward Range header for video seeking support
            if (Request.Headers.TryGetValue("Range", out var range))
            {
                request.Headers.TryAddWithoutValidation("Range", range.ToString());
            }

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            HttpContext.Response.RegisterForDispose(response);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Proxy upstream returned HTTP {Status} for {Host}", (int)response.StatusCode, uri.Host);
                return StatusCode(502, "Upstream server returned " + (int)response.StatusCode);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "video/mp4";
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            // Pass upstream range/length metadata straight through. The upstream already
            // honoured the forwarded Range header, so we must NOT let ASP.NET re-slice the
            // (non-seekable) stream — hence enableRangeProcessing stays off.
            if (response.Content.Headers.ContentRange != null)
                Response.Headers["Content-Range"] = response.Content.Headers.ContentRange.ToString();
            if (response.Headers.Contains("Accept-Ranges"))
                Response.Headers["Accept-Ranges"] = "bytes";

            Response.StatusCode = (int)response.StatusCode;
            return File(stream, contentType);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Proxy HTTP error for {Host}", uri.Host);
            return StatusCode(502, "Failed to fetch upstream video");
        }
        catch (TaskCanceledException)
        {
            return StatusCode(504, "Upstream request timed out");
        }
    }

    /// <summary>Whitelist of hosts the video proxy may fetch from.</summary>
    private static bool IsAllowedProxyHost(Uri uri)
    {
        var host = uri.Host;
        if (host.EndsWith(".googlevideo.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".ytimg.com", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Invidious instances may return stream URLs pointing at their own domain.
        foreach (var invidious in InvidiousHosts)
        {
            if (Uri.TryCreate(invidious, UriKind.Absolute, out var invUri)
                && string.Equals(host, invUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
