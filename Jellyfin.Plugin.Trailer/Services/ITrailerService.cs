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
    /// <param name="itemId">Jellyfin item ID (GUID string).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="TrailerResult"/> — <c>Found</c> is false when no trailer is available
    /// or the item does not exist.
    /// </returns>
    Task<TrailerResult> GetTrailerAsync(string itemId, CancellationToken cancellationToken);
}
