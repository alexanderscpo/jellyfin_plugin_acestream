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

    /// <summary>
    /// Gets or sets how long (in minutes) discovered codecs are cached per channel so repeat plays
    /// skip the readiness check and ffprobe. Codecs are stable, so a few minutes is plenty; set to
    /// <c>0</c> to disable the cache entirely.
    /// </summary>
    public int CodecCacheTtlMinutes { get; set; } = 5;

    /// <summary>
    /// Gets or sets how long (in seconds) to poll the engine session for "dl" status before
    /// declaring the channel not ready. A cold AceStream session takes 15–25 s of prebuffering;
    /// keep this above 30 s for best results. Set to 0 to fall back to fail-open immediately.
    /// </summary>
    public int ReadinessTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets an M3U playlist with <c>acestream://</c> URIs that appear as a "Custom" folder
    /// in the channel browser. Standard <c>#EXTINF</c> headers are supported; entries with
    /// non-AceStream URLs or invalid infohashes are silently ignored.
    /// </summary>
    public string M3uPlaylist { get; set; } = string.Empty;
}
