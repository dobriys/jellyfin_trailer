using System.ComponentModel.DataAnnotations;
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
    private readonly ILogger<TrailerController> _logger;

    /// <summary>Initializes a new instance of <see cref="TrailerController"/>.</summary>
    public TrailerController(ITrailerService trailerService, ILogger<TrailerController> logger)
    {
        _trailerService = trailerService;
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
}
