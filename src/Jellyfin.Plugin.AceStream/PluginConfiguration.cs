using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AceStream;

/// <summary>
/// Persisted configuration for the AceStream plugin.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the base URL of the acexy proxy used for playback (multiplexing + pid).
    /// </summary>
    public string ProxyUrl { get; set; } = "http://acexy:8080";

    /// <summary>
    /// Gets or sets the base URL of the AceStream engine used for search/browse (/search).
    /// </summary>
    public string EngineUrl { get; set; } = "http://172.39.0.2:6878";

    /// <summary>
    /// Gets or sets how long (in milliseconds) ffprobe analyzes a live stream when discovering
    /// its codecs. Jellyfin's global default never returns on an infinite stream, so this is
    /// bounded; smaller is faster to start, larger is more robust on slow channels.
    /// </summary>
    public int ProbeAnalyzeDurationMs { get; set; } = 5000;
}
