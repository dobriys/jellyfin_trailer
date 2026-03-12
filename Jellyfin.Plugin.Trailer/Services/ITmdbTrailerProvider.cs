using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Trailer.Models;

namespace Jellyfin.Plugin.Trailer.Services;

/// <summary>Fetches trailer information from The Movie Database (TMDb).</summary>
public interface ITmdbTrailerProvider
{
    /// <summary>
    /// Returns the best available trailer for the given TMDb movie ID.
    /// </summary>
    /// <param name="tmdbId">TMDb numeric movie ID.</param>
    /// <param name="apiKey">TMDb v3 API key.</param>
    /// <param name="language">BCP 47 language tag, e.g. "ru-RU".</param>
    /// <param name="fallbackToEnglish">Retry with "en-US" if no trailer found in <paramref name="language"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="TrailerResult"/> with <c>Found=false</c> when nothing is available.</returns>
    Task<TrailerResult> GetTrailerAsync(
        string tmdbId,
        string apiKey,
        string language,
        bool fallbackToEnglish,
        CancellationToken cancellationToken);
}
