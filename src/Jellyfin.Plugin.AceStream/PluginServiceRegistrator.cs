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

        // Checks live readiness by opening an engine session and polling its stat_url until "dl"
        // so the codec probe is skipped fast on dead channels. Reuses the "AceEngine" named client
        // already registered above; no additional named client registration is needed.
        serviceCollection.AddSingleton<IStreamReadiness, EngineSessionReadiness>();

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

        // Lazily resolves .acelive transport-file URLs to live infohashes with a TTL cache.
        serviceCollection.AddSingleton<IAceLiveResolver, AceLiveResolver>();

        // User-defined channels from the M3U playlist in plugin settings.
        // PluginCustomChannelRepository implements both ICustomChannelRepository (Application port)
        // and IAceLiveEntrySource (Infrastructure side-channel). Register the concrete type once as
        // a singleton, then expose both interfaces pointing to the same instance (ISP-safe and DRY).
        serviceCollection.AddSingleton<PluginCustomChannelRepository>();
        serviceCollection.AddSingleton<ICustomChannelRepository>(
            sp => sp.GetRequiredService<PluginCustomChannelRepository>());
        serviceCollection.AddSingleton<IAceLiveEntrySource>(
            sp => sp.GetRequiredService<PluginCustomChannelRepository>());

        // Jellyfin does not auto-register plugin IChannel implementations; register it explicitly.
        // Use an explicit factory so both ICustomChannelRepository and IAceLiveEntrySource are
        // injected without ambiguity (both resolve to the same PluginCustomChannelRepository
        // singleton, but DI cannot auto-wire two interfaces from one concrete type without guidance).
        serviceCollection.AddSingleton<IChannel>(sp => new AceStreamChannel(
            sp.GetRequiredService<ISearchPort>(),
            sp.GetRequiredService<IProxySettings>(),
            sp.GetRequiredService<IProbeSettings>(),
            sp.GetRequiredService<IMediaSourceProbe>(),
            sp.GetRequiredService<ICustomChannelRepository>(),
            sp.GetRequiredService<IAceLiveEntrySource>(),
            sp.GetRequiredService<IAceLiveResolver>(),
            sp.GetRequiredService<ILogger<AceStreamChannel>>()));
    }
}
