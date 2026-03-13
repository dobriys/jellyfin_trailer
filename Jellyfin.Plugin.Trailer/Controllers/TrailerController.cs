using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Net.Mime;
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
