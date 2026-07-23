using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Trailer.Configuration;

/// <summary>
/// Plugin configuration stored in Jellyfin's plugin config directory.
/// Accessible in the admin dashboard at Dashboard → Plugins → Trailer → Settings.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// YouTube Data API v3 key used to search for trailers.
    /// Get one free at https://console.cloud.google.com → Enable "YouTube Data API v3" → Create API Key.
    /// </summary>
    public string YouTubeApiKey { get; set; } = string.Empty;
}
