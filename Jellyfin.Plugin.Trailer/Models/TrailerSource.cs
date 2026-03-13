namespace Jellyfin.Plugin.Trailer.Models;

/// <summary>Indicates which provider supplied the trailer URL.</summary>
public enum TrailerSource
{
    /// <summary>No trailer was found.</summary>
    None,

    /// <summary>Trailer was sourced from The Movie Database (TMDb).</summary>
    Tmdb,

    /// <summary>Trailer was sourced from the Kinopoisk Unofficial API.</summary>
    Kinopoisk,

    /// <summary>Fallback YouTube search URL (no direct trailer found).</summary>
    YouTubeSearch
}
