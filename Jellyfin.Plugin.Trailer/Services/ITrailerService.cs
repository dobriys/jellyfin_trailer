using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Trailer.Models;

namespace Jellyfin.Plugin.Trailer.Services;

/// <summary>
/// Resolves a trailer URL for a Jellyfin library item.
/// Called by <c>TrailerController</c> in response to client requests.
/// </summary>
public interface ITrailerService
{
    /// <summary>
    /// Returns the best available trailer for the specified Jellyfin item.
    /// </summary>
    Task<TrailerResult> GetTrailerAsync(string itemId, CancellationToken cancellationToken);

    /// <summary>
    /// Searches YouTube for trailers for the specified Jellyfin item.
    /// Returns multiple results so the user can pick which one to watch.
    /// </summary>
    Task<List<YouTubeSearchItem>> SearchTrailersAsync(string itemId, CancellationToken cancellationToken);
}
