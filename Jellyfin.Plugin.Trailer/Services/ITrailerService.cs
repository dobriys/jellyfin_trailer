using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Trailer.Models;

namespace Jellyfin.Plugin.Trailer.Services;

/// <summary>
/// Searches YouTube for trailers for a Jellyfin library item.
/// Called by <c>TrailerController</c> in response to client requests.
/// </summary>
public interface ITrailerService
{
    /// <summary>
    /// Searches YouTube for trailers for the specified Jellyfin item.
    /// Returns multiple results so the user can pick which one to watch.
    /// </summary>
    Task<List<YouTubeSearchItem>> SearchTrailersAsync(string itemId, CancellationToken cancellationToken);
}
