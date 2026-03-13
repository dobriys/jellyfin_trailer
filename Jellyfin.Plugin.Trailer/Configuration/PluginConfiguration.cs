using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Trailer.Configuration;

/// <summary>
/// Plugin configuration stored in Jellyfin's plugin config directory.
/// Accessible in the admin dashboard at Dashboard → Plugins → Trailer → Settings.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>TMDb API v3 key from themoviedb.org/settings/api</summary>
    public string TmdbApiKey { get; set; } = string.Empty;

    /// <summary>Kinopoisk Unofficial API token from kinopoiskapiunofficial.tech</summary>
    public string KinopoiskApiKey { get; set; } = string.Empty;

    /// <summary>
    /// BCP 47 language tag used when querying TMDb for trailers.
    /// Examples: "ru-RU", "en-US".
    /// </summary>
    public string PreferredLanguage { get; set; } = "ru-RU";

    /// <summary>
    /// If true and no trailer is found in the preferred language,
    /// retry the TMDb query with language=en-US.
    /// </summary>
    public bool FallbackToEnglish { get; set; } = true;

    /// <summary>
    /// If true and TMDb returns no trailer, the Kinopoisk provider
    /// is tried as a fallback.
    /// </summary>
    public bool EnableKinopoisk { get; set; } = true;

    /// <summary>
    /// YouTube Data API v3 key for searching trailers on a specific YouTube channel.
    /// Get one free at https://console.cloud.google.com → Enable "YouTube Data API v3" → Create API Key.
    /// </summary>
    public string YouTubeApiKey { get; set; } = string.Empty;

    /// <summary>
    /// YouTube channel handle to search for trailers (e.g. "@KinomanTrailers").
    /// The plugin will search this channel for "{movie name} трейлер {year}".
    /// </summary>
    public string YouTubeChannelHandle { get; set; } = "@KinomanTrailers";

    /// <summary>
    /// Duration in minutes to keep trailer results in the in-memory cache.
    /// Set to 0 to disable caching.
    /// </summary>
    public int CacheDurationMinutes { get; set; } = 60;

    /// <summary>
    /// How to open the trailer when the button is clicked.
    /// "modal" — embedded YouTube player inside Jellyfin (default).
    /// "tab"   — open YouTube in a new browser tab.
    /// </summary>
    public string PlayerMode { get; set; } = "modal";
}
