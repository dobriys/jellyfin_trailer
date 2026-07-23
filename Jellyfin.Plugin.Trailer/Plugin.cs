using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Trailer;

/// <summary>
/// Jellyfin Trailer Plugin.
/// Adds a "Трейлер" button to movie detail pages that opens a YouTube trailer in a new tab.
/// </summary>
public class Plugin : BasePlugin<Configuration.PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Stable plugin GUID — never change this after first release.
    /// Jellyfin uses it to identify the installed plugin across updates.
    /// </summary>
    public static readonly Guid StaticId = new("70788050-9f96-453d-a823-f41b7bd12712");

    /// <summary>
    /// Singleton instance set in the constructor.
    /// Used by services to access plugin configuration without DI.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>Initializes a new instance of <see cref="Plugin"/>.</summary>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Trailer";

    /// <inheritdoc />
    public override Guid Id => StaticId;

    /// <inheritdoc />
    public override string Description => "Adds a Трейлер button to movie pages that searches YouTube for trailers.";

    /// <summary>
    /// Returns web pages served by this plugin.
    ///
    /// Pages are available at: /web/configurationpage?name=&lt;Name&gt;
    ///
    /// - "Trailer"           → admin configuration HTML page
    /// - "trailerPlugin_js"  → client-side JavaScript injected into the web UI
    /// </summary>
    public IEnumerable<PluginPageInfo> GetPages()
    {
        // Admin configuration page (HTML)
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configurationPage.html"
        };

        // Client-side JavaScript
        // URL: /web/configurationpage?name=trailerPlugin_js
        // The admin adds a one-liner loader script via jellyfin-plugin-custom-javascript.
        yield return new PluginPageInfo
        {
            Name = "trailerPlugin_js",
            EmbeddedResourcePath = $"{GetType().Namespace}.Web.trailerPlugin.js"
        };
    }
}
