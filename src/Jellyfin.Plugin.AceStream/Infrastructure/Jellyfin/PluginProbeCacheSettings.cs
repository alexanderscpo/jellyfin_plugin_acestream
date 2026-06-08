namespace Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;

/// <summary>
/// <see cref="IProbeCacheSettings"/> backed by the live plugin configuration.
/// </summary>
public sealed class PluginProbeCacheSettings : IProbeCacheSettings
{
    /// <inheritdoc />
    public TimeSpan CodecCacheTtl => TimeSpan.FromMinutes(Math.Max(0, Plugin.Instance?.Configuration.CodecCacheTtlMinutes ?? 5));
}
