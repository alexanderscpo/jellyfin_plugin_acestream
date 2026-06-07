using Jellyfin.Plugin.AceStream.Application;
using Jellyfin.Plugin.AceStream.Infrastructure.Engine;
using Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;
using Jellyfin.Plugin.AceStream.Infrastructure.Proxy;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

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

        // Probes the live stream (via Jellyfin's IMediaEncoder) so the channel can hand
        // Jellyfin the real codecs and let it choose remux over transcode.
        serviceCollection.AddSingleton<IMediaSourceProbe, MediaEncoderStreamProbe>();

        // Jellyfin does not auto-register plugin IChannel implementations; register it explicitly.
        serviceCollection.AddSingleton<IChannel, AceStreamChannel>();
    }
}
