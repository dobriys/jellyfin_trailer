using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Trailer.Models;

namespace Jellyfin.Plugin.Trailer.Services;

/// <summary>Fetches trailer information from the Kinopoisk Unofficial API.</summary>
public interface IKinopoiskTrailerProvider
{
    /// <summary>
    /// Returns the best available trailer for the given film.
    /// </summary>
    /// <param name="kinopoiskId">
    /// Kinopoisk film ID if already known (from item's ProviderIds).
    /// Pass <c>null</c> to trigger a name+year search.
    /// </param>
    /// <param name="movieName">Movie title used for search when <paramref name="kinopoiskId"/> is null.</param>
    /// <param name="year">Release year used to refine search results.</param>
    /// <param name="apiKey">Kinopoisk Unofficial API token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="TrailerResult"/> with <c>Found=false</c> when nothing is available.</returns>
    Task<TrailerResult> GetTrailerAsync(
        string? kinopoiskId,
        string movieName,
        int? year,
        string apiKey,
        CancellationToken cancellationToken);
}
