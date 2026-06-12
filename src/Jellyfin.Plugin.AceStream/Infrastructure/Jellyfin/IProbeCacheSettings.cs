namespace Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;

/// <summary>
/// Provides codec-probe and codec-cache configuration. Read per use so configuration changes
/// take effect without restarting.
/// </summary>
public interface IProbeCacheSettings
{
    /// <summary>
    /// Gets how long (in milliseconds) ffprobe analyzes a live stream when discovering codecs.
    /// </summary>
    int ProbeAnalyzeDurationMs { get; }

    /// <summary>
    /// Gets how long discovered codecs are cached per channel. <see cref="TimeSpan.Zero"/> (or less)
    /// disables caching.
    /// </summary>
    TimeSpan CodecCacheTtl { get; }
}
