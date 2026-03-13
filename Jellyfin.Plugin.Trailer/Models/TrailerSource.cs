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

    /// <summary>Trailer found on a YouTube channel via YouTube Data API v3.</summary>
    YouTube
}
