using Jellyfin.Plugin.AceStream.Application;
using Jellyfin.Plugin.AceStream.Infrastructure.Engine;
using Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;
using Jellyfin.Plugin.AceStream.Infrastructure.Proxy;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AceStream;

/// <summary>
/// Registers the plugin's services (ports and their adapters) into Jellyfin's DI container.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // The engine URL is read per request from configuration (PluginEngineSettings),
        // so the search adapter builds absolute URLs and does not depend on HttpClient.BaseAddress.
        serviceCollection.AddSingleton<IEngineSettings, PluginEngineSettings>();
        serviceCollection.AddSingleton<IProxySettings, PluginProxySettings>();

        // Register a named client (not a typed client) and resolve it per request via
        // IHttpClientFactory. The search adapter is a singleton, so a typed/captured client would
        // pin one HttpMessageHandler forever and defeat the factory's handler rotation.
        serviceCollection.AddHttpClient(EngineSearchClient.HttpClientName);
        serviceCollection.AddSingleton<ISearchPort, EngineSearchClient>();

        // Checks live readiness through acexy (the same path playback uses, so it shares the engine
        // session) so a dead P2P channel is skipped fast instead of hanging the codec probe — the
        // /search availability is only a stale snapshot.
        serviceCollection.AddHttpClient(ProxyStreamReadiness.HttpClientName);
        serviceCollection.AddSingleton<IStreamReadiness, ProxyStreamReadiness>();

        // Probe pipeline (outermost first): cache -> readiness gate -> ffprobe.
        //  - MediaEncoderStreamProbe runs ffprobe (via Jellyfin's IMediaEncoder) for the real codecs.
        //  - ReadinessGatedProbe skips it when the channel is delivering no data.
        //  - CachingMediaSourceProbe caches a successful result so repeat plays skip both.
        serviceCollection.AddSingleton<IProbeSettings, PluginProbeSettings>();
        serviceCollection.AddSingleton<MediaEncoderStreamProbe>();
        serviceCollection.AddSingleton<IMediaSourceProbe>(sp =>
        {
            var gated = new ReadinessGatedProbe(
                sp.GetRequiredService<MediaEncoderStreamProbe>(),
                sp.GetRequiredService<IStreamReadiness>(),
                sp.GetRequiredService<ILogger<ReadinessGatedProbe>>());

            return new CachingMediaSourceProbe(
                gated,
                sp.GetRequiredService<IProbeSettings>(),
                TimeProvider.System,
                sp.GetRequiredService<ILogger<CachingMediaSourceProbe>>());
        });

        // User-defined channels from the M3U playlist in plugin settings.
        serviceCollection.AddSingleton<ICustomChannelRepository, PluginCustomChannelRepository>();

        // Jellyfin does not auto-register plugin IChannel implementations; register it explicitly.
        serviceCollection.AddSingleton<IChannel, AceStreamChannel>();
    }
}
