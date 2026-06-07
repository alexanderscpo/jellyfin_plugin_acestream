using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;

/// <summary>
/// Probes a live AceStream source and fills its <see cref="MediaSourceInfo.MediaStreams"/>
/// with the real codecs. Jellyfin's <c>StreamBuilder</c> then decides direct-stream (remux)
/// vs transcode per stream from that truth, instead of full-transcoding a stream of unknown
/// codecs. Implementations must be resilient: a failed probe leaves the source unchanged so
/// playback degrades to Jellyfin's default rather than breaking.
/// </summary>
public interface IMediaSourceProbe
{
    /// <summary>
    /// Probes the live stream behind <paramref name="source"/> and enriches it in place.
    /// </summary>
    /// <param name="source">The bare proxy media source to enrich.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when probing finished (whether or not it found streams).</returns>
    Task EnrichAsync(MediaSourceInfo source, CancellationToken cancellationToken);
}
