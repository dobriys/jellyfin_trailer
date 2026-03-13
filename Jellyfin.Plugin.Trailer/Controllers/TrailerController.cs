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
    /// Returns public plugin configuration consumed by the client-side JavaScript.
    /// Exposes only settings that are safe to read without admin rights.
    /// </summary>
    /// <response code="200">Configuration returned.</response>
    /// <response code="401">Authentication required.</response>
    [HttpGet("config")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult GetClientConfig()
    {
        var config = Plugin.Instance!.Configuration;
        return Ok(new { playerMode = config.PlayerMode });
    }

    /// <summary>
    /// Returns the trailer URL for the specified Jellyfin library item.
    /// </summary>
    /// <remarks>
    /// Called by <c>trailerPlugin.js</c> as <c>GET /Trailer/{itemId}</c>.
    ///
    /// The response always has HTTP 200; use the <c>found</c> field to check
    /// whether a trailer was actually resolved.
    /// </remarks>
    /// <param name="itemId">Jellyfin item GUID (e.g. <c>3fa85f64-5717-4562-b3fc-2c963f66afa6</c>).</param>
    /// <param name="cancellationToken">Cancellation token injected by ASP.NET Core.</param>
    /// <returns>
    /// A <see cref="TrailerResult"/> with <c>found=true</c> and a YouTube URL,
    /// or <c>found=false</c> when no trailer is available.
    /// </returns>
    /// <response code="200">Trailer lookup completed (may have found=false).</response>
    /// <response code="400">The provided item ID is not a valid GUID.</response>
    /// <response code="401">Authentication required.</response>
    [HttpGet("{itemId}")]
    [Authorize]
    [ProducesResponseType(typeof(TrailerResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TrailerResult>> GetTrailerAsync(
        [FromRoute][Required] string itemId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return BadRequest("itemId is required");

        _logger.LogDebug("Trailer requested for item {ItemId}", itemId);

        var result = await _trailerService.GetTrailerAsync(itemId, cancellationToken)
            .ConfigureAwait(false);

        // Always return 200 so the JS can distinguish
        //   found=false (trailer unavailable)  vs  network/HTTP errors.
        return Ok(result);
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
    /// Uses YouTube's internal player API directly (no third-party dependencies).
    /// Returns a proxied URL that the client can play via HTML5 &lt;video&gt;.
    /// </summary>
    /// <param name="videoId">YouTube video ID (11 chars).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("stream/{videoId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> ResolveStream(
        [FromRoute][Required] string videoId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(videoId) || videoId.Length != 11)
            return BadRequest("Valid 11-character YouTube video ID is required");

        // ── Strategy 1: YouTube's internal player API (most reliable) ──
        var result = await TryYouTubePlayerApi(videoId, cancellationToken).ConfigureAwait(false);
        if (result != null)
            return Ok(result);

        // ── Strategy 2: Invidious API fallback ──
        result = await TryInvidiousApi(videoId, cancellationToken).ConfigureAwait(false);
        if (result != null)
            return Ok(result);

        return StatusCode(502, "Could not resolve video stream");
    }

    private async Task<object?> TryYouTubePlayerApi(string videoId, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("TrailerProxy");

            // YouTube's internal player API — works server-to-server without auth
            var playerUrl = "https://www.youtube.com/youtubei/v1/player";

            var payload = new
            {
                videoId,
                context = new
                {
                    client = new
                    {
                        clientName = "ANDROID",
                        clientVersion = "19.09.37",
                        androidSdkVersion = 30,
                        hl = "ru",
                        gl = "RU"
                    }
                }
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, playerUrl)
            {
                Content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.Add("User-Agent", "com.google.android.youtube/19.09.37 (Linux; U; Android 11) gzip");

            _logger.LogInformation("Resolving stream via YouTube player API for {VideoId}", videoId);

            var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("YouTube player API returned {Status}", (int)response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            // Check playability
            if (doc.RootElement.TryGetProperty("playabilityStatus", out var status))
            {
                var statusStr = status.TryGetProperty("status", out var s) ? s.GetString() : "";
                if (statusStr != "OK")
                {
                    _logger.LogWarning("YouTube player API: video {VideoId} status={Status}", videoId, statusStr);
                    return null;
                }
            }

            // Extract streaming data — look for combined (muxed) formats
            if (!doc.RootElement.TryGetProperty("streamingData", out var streamingData))
            {
                _logger.LogWarning("YouTube player API: no streamingData for {VideoId}", videoId);
                return null;
            }

            string? bestUrl = null;
            string? bestQuality = null;

            // formats = muxed audio+video (ready to play in <video>)
            if (streamingData.TryGetProperty("formats", out var formats))
            {
                foreach (var fmt in formats.EnumerateArray())
                {
                    var url = fmt.TryGetProperty("url", out var u) ? u.GetString() : null;
                    if (string.IsNullOrEmpty(url)) continue;

                    var quality = fmt.TryGetProperty("qualityLabel", out var q) ? q.GetString() : "";

                    if (bestUrl == null || (quality != null && quality.Contains("720")))
                    {
                        bestUrl = url;
                        bestQuality = quality;
                    }
                }
            }

            if (!string.IsNullOrEmpty(bestUrl))
            {
                _logger.LogInformation("YouTube player API resolved {Quality} for {VideoId}", bestQuality, videoId);
                var proxyUrl = "/Trailer/proxy?url=" + Uri.EscapeDataString(bestUrl);
                return new { streamUrl = proxyUrl, quality = bestQuality, source = "youtube-api" };
            }

            _logger.LogWarning("YouTube player API: no muxed formats for {VideoId}", videoId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "YouTube player API failed for {VideoId}", videoId);
        }

        return null;
    }

    private async Task<object?> TryInvidiousApi(string videoId, CancellationToken ct)
    {
        string[] hosts =
        {
            "https://inv.nadeko.net",
            "https://invidious.nerdvpn.de",
            "https://yewtu.be"
        };

        var client = _httpClientFactory.CreateClient("TrailerProxy");

        foreach (var host in hosts)
        {
            try
            {
                var apiUrl = $"{host}/api/v1/videos/{videoId}?fields=formatStreams";
                _logger.LogInformation("Trying Invidious {Host} for {VideoId}", host, videoId);

                using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                request.Headers.Add("Accept", "application/json");
                request.Headers.Add("User-Agent", "Jellyfin-Trailer-Plugin/1.0");

                var response = await client.SendAsync(request, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Invidious {Host} returned {Status}", host, (int)response.StatusCode);
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("formatStreams", out var streams))
                {
                    string? bestUrl = null;
                    string? bestQuality = null;

                    foreach (var stream in streams.EnumerateArray())
                    {
                        var url = stream.TryGetProperty("url", out var u) ? u.GetString() : null;
                        if (string.IsNullOrEmpty(url)) continue;

                        var quality = stream.TryGetProperty("qualityLabel", out var q) ? q.GetString() : "";

                        if (bestUrl == null || (quality != null && quality.Contains("720")))
                        {
                            bestUrl = url;
                            bestQuality = quality;
                        }
                    }

                    if (!string.IsNullOrEmpty(bestUrl))
                    {
                        _logger.LogInformation("Invidious resolved {Quality} for {VideoId} via {Host}",
                            bestQuality, videoId, host);
                        var proxyUrl = "/Trailer/proxy?url=" + Uri.EscapeDataString(bestUrl);
                        return new { streamUrl = proxyUrl, quality = bestQuality, source = host };
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
    /// Proxies a remote video URL through the Jellyfin server.
    /// Used when direct browser access to the video host is blocked (e.g. Yandex S3).
    /// The server fetches the video and streams it to the client.
    /// </summary>
    /// <param name="url">The remote video URL to proxy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Video stream.</response>
    /// <response code="400">Missing or invalid URL.</response>
    /// <response code="502">Upstream server error.</response>
    [HttpGet("proxy")]
    [Authorize]
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

        _logger.LogInformation("Proxying video: {Url}", url);

        try
        {
            var client = _httpClientFactory.CreateClient("TrailerProxy");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Proxy upstream returned HTTP {Status} for {Url}", (int)response.StatusCode, url);
                return StatusCode(502, "Upstream server returned " + (int)response.StatusCode);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "video/mp4";
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            // Stream the video directly to the client
            return File(stream, contentType, enableRangeProcessing: true);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Proxy HTTP error for {Url}", url);
            return StatusCode(502, "Failed to fetch upstream video");
        }
        catch (TaskCanceledException)
        {
            return StatusCode(504, "Upstream request timed out");
        }
    }
}
