using System;
using System.Threading;
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

        // YouTube Data API — short timeout, calls should be quick.
        serviceCollection.AddHttpClient("YouTube", c => c.Timeout = TimeSpan.FromSeconds(15));

        // Stream resolution (watch-page scrape, player API, Invidious). Bounded so a slow
        // upstream can't stall the whole request chain (several are tried sequentially).
        serviceCollection.AddHttpClient("YouTubeResolve", c => c.Timeout = TimeSpan.FromSeconds(12));

        // Video/thumbnail streaming proxy. No client timeout — long trailers can exceed the
        // default 100s; the request's CancellationToken bounds the lifetime instead.
        serviceCollection.AddHttpClient("TrailerProxy", c => c.Timeout = Timeout.InfiniteTimeSpan);

        // Trailer providers and service
        serviceCollection.AddSingleton<IYouTubeTrailerProvider, YouTubeTrailerProvider>();
        serviceCollection.AddSingleton<ITrailerService, TrailerService>();
    }
}
