namespace Jellyfin.Plugin.AceStream.Infrastructure.Proxy;

/// <summary>
/// Provides the current acexy proxy base URL. Read per request so configuration changes
/// take effect without rebuilding long-lived consumers (e.g. the singleton channel).
/// </summary>
public interface IProxySettings
{
    /// <summary>
    /// Gets the proxy base URL (e.g. <c>http://acexy:8080</c>).
    /// </summary>
    string BaseUrl { get; }

    /// <summary>
    /// Gets how long (in milliseconds) ffprobe analyzes a live stream when discovering codecs.
    /// </summary>
    int ProbeAnalyzeDurationMs { get; }
}
