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
/// Playback itself is handled client-side via a youtube-nocookie.com iframe embed,
/// so the server only needs to search YouTube and (optionally) proxy thumbnails.
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
    /// Used as a fallback when the client cannot reach i.ytimg.com directly.
    /// Only allows URLs from ytimg.com — no auth required (public thumbnails).
    /// </summary>
    /// <param name="url">YouTube thumbnail URL (must be from ytimg.com).</param>
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
}
