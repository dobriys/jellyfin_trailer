using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Trailer.Models;

namespace Jellyfin.Plugin.Trailer.Services;

/// <summary>
/// Searches a specific YouTube channel for trailers using the YouTube Data API v3.
/// </summary>
public interface IYouTubeTrailerProvider
{
    /// <summary>
    /// Searches for a trailer on the configured YouTube channel.
    /// </summary>
    /// <param name="movieName">Movie title to search for.</param>
    /// <param name="year">Optional production year for better matching.</param>
    /// <param name="apiKey">YouTube Data API v3 key.</param>
    /// <param name="channelHandle">YouTube channel handle (e.g. "@KinomanTrailers").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="TrailerResult"/> with a YouTube video URL if found.</returns>
    Task<TrailerResult> GetTrailerAsync(
        string movieName,
        int? year,
        string apiKey,
        string channelHandle,
        CancellationToken cancellationToken);
}
