using Jellyfin.Plugin.AceStream.Domain;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.AceStream.Infrastructure.Proxy;

/// <summary>
/// Builds the Jellyfin <see cref="MediaSourceInfo"/> for an AceStream channel: a live
/// MPEG-TS stream served by the acexy proxy. The plugin never opens this stream itself —
/// Jellyfin connects to the returned <see cref="MediaSourceInfo.Path"/>.
/// </summary>
public static class ProxyMediaSource
{
    private const int DefaultAnalyzeDurationMs = 5000;

    /// <summary>
    /// Builds the media source for a channel by infohash.
    /// </summary>
    /// <param name="proxyBaseUrl">The acexy proxy base URL.</param>
    /// <param name="infohash">The channel identity.</param>
    /// <param name="analyzeDurationMs">
    /// How long ffprobe analyzes the live stream (ms). Non-positive values fall back to a safe
    /// default, since Jellyfin's global default never returns on an infinite stream.
    /// </param>
    /// <returns>A live MPEG-TS media source pointing at the proxy.</returns>
    public static MediaSourceInfo Build(string proxyBaseUrl, Infohash infohash, int analyzeDurationMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proxyBaseUrl);
        ArgumentNullException.ThrowIfNull(infohash);

        var path = BuildStreamUrl(proxyBaseUrl, infohash);
        var analyzeDuration = analyzeDurationMs > 0 ? analyzeDurationMs : DefaultAnalyzeDurationMs;

        return new MediaSourceInfo
        {
            Id = infohash.Value,
            Name = "AceStream",
            Path = path,
            Protocol = MediaProtocol.Http,
            IsRemote = true,
            IsInfiniteStream = true,
            Container = "ts",
            RequiresOpening = false,

            // Jellyfin's global -analyzeduration (200s) never returns when probing an infinite
            // live stream, so the probe times out and we fall back to transcoding. A small
            // per-source bound makes ffprobe identify the codecs in a few seconds (AnalyzeDurationMs
            // overrides the global; it is in ms and Jellyfin multiplies by 1000 for microseconds).
            AnalyzeDurationMs = analyzeDuration,

            // MediaStreams are intentionally left empty here: the bare source carries no codecs.
            // The channel enriches it via IMediaSourceProbe before returning, so Jellyfin's
            // StreamBuilder decides remux vs transcode from the real codecs. Direct stream
            // (remux) and transcode stay open; Jellyfin refines these flags after the decision.
            SupportsDirectStream = true,
            SupportsTranscoding = true,
        };
    }

    /// <summary>
    /// Builds the acexy stream URL for a channel — the single source of truth for the playback path,
    /// shared by the media source and the readiness check so both hit the exact same endpoint.
    /// </summary>
    /// <param name="proxyBaseUrl">The acexy proxy base URL.</param>
    /// <param name="infohash">The channel identity.</param>
    /// <returns>The acexy <c>/ace/getstream</c> URL for the infohash.</returns>
    public static string BuildStreamUrl(string proxyBaseUrl, Infohash infohash)
        => $"{proxyBaseUrl.TrimEnd('/')}/ace/getstream?infohash={infohash.Value}";
}
