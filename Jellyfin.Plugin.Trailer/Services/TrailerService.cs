using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Trailer.Models;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Trailer.Services;

/// <summary>
/// Resolves the Jellyfin library item, then queries YouTube for candidate
/// trailers that the client shows in a picker.
/// </summary>
public class TrailerService : ITrailerService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IYouTubeTrailerProvider _youtubeProvider;
    private readonly ILogger<TrailerService> _logger;

    /// <summary>Initializes a new instance of <see cref="TrailerService"/>.</summary>
    public TrailerService(
        ILibraryManager libraryManager,
        IYouTubeTrailerProvider youtubeProvider,
        ILogger<TrailerService> logger)
    {
        _libraryManager = libraryManager;
        _youtubeProvider = youtubeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<YouTubeSearchItem>> SearchTrailersAsync(
        string itemId, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance!.Configuration;

        if (string.IsNullOrEmpty(config.YouTubeApiKey))
        {
            _logger.LogWarning("YouTube API key not configured — cannot search trailers");
            return new List<YouTubeSearchItem>();
        }

        if (!Guid.TryParse(itemId, out var guid))
        {
            _logger.LogWarning("Invalid item ID format: '{ItemId}'", itemId);
            return new List<YouTubeSearchItem>();
        }

        var item = _libraryManager.GetItemById(guid);
        if (item is null)
        {
            _logger.LogWarning("Item {ItemId} not found in library", itemId);
            return new List<YouTubeSearchItem>();
        }

        _logger.LogInformation("YouTube search for trailers: '{Title}' ({Year})",
            item.Name, item.ProductionYear);

        return await _youtubeProvider.SearchAsync(
            item.Name ?? string.Empty,
            item.ProductionYear,
            config.YouTubeApiKey,
            8,
            cancellationToken).ConfigureAwait(false);
    }
}
