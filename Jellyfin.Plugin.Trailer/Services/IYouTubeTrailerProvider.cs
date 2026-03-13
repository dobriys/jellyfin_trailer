using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Trailer.Models;

namespace Jellyfin.Plugin.Trailer.Services;

/// <summary>
/// Searches YouTube for trailers using the YouTube Data API v3.
/// </summary>
public interface IYouTubeTrailerProvider
{
    /// <summary>
    /// Searches for a trailer on the configured YouTube channel.
    /// Returns the single best match.
    /// </summary>
    Task<TrailerResult> GetTrailerAsync(
        string movieName,
        int? year,
        string apiKey,
        string channelHandle,
        CancellationToken cancellationToken);

    /// <summary>
    /// Performs a general YouTube search for trailers and returns multiple results
    /// so the user can pick which one to watch.
    /// </summary>
    /// <param name="movieName">Movie title to search for.</param>
    /// <param name="year">Optional production year for better matching.</param>
    /// <param name="apiKey">YouTube Data API v3 key.</param>
    /// <param name="maxResults">Maximum number of results to return (1–10).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of YouTube search items.</returns>
    Task<List<YouTubeSearchItem>> SearchAsync(
        string movieName,
        int? year,
        string apiKey,
        int maxResults,
        CancellationToken cancellationToken);
}
