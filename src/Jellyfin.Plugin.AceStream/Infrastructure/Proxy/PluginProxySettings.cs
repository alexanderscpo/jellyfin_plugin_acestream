namespace Jellyfin.Plugin.AceStream.Infrastructure.Proxy;

/// <summary>
/// <see cref="IProxySettings"/> backed by the live plugin configuration.
/// </summary>
public sealed class PluginProxySettings : IProxySettings
{
    /// <inheritdoc />
    public string BaseUrl => Plugin.Instance?.Configuration.ProxyUrl ?? string.Empty;
}
