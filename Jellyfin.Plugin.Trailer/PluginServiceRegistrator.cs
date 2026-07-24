using System;
using Jellyfin.Plugin.Trailer.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Trailer;

/// <summary>
/// Registers plugin services into Jellyfin's dependency injection container.
/// Jellyfin discovers this class via <see cref="IPluginServiceRegistrator"/> on startup.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Named HttpClient instances managed by Jellyfin's IHttpClientFactory.
        // Using named clients avoids socket exhaustion from ad-hoc HttpClient instantiation.

        // YouTube Data API — search calls should be quick.
        serviceCollection.AddHttpClient("YouTube", c => c.Timeout = TimeSpan.FromSeconds(15));

        // Thumbnail proxy — small images, short timeout is plenty.
        serviceCollection.AddHttpClient("TrailerProxy", c => c.Timeout = TimeSpan.FromSeconds(15));

        // Trailer providers and service
        serviceCollection.AddSingleton<IYouTubeTrailerProvider, YouTubeTrailerProvider>();
        serviceCollection.AddSingleton<ITrailerService, TrailerService>();
    }
}
