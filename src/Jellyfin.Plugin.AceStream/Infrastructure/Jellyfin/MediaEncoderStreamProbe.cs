using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;

/// <summary>
/// Probes a live AceStream source with Jellyfin's own <see cref="IMediaEncoder"/> (ffprobe)
/// and copies the discovered codecs onto the source. A live P2P join can be slow or yield no
/// keyframe, so the probe is bounded by a timeout and any failure leaves the source untouched —
/// Jellyfin then falls back to transcoding rather than the playback breaking.
/// </summary>
public sealed class MediaEncoderStreamProbe : IMediaSourceProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);

    private readonly IMediaEncoder _mediaEncoder;
    private readonly ILogger<MediaEncoderStreamProbe> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaEncoderStreamProbe"/> class.
    /// </summary>
    /// <param name="mediaEncoder">Jellyfin's media encoder, used to run ffprobe.</param>
    /// <param name="logger">The logger.</param>
    public MediaEncoderStreamProbe(IMediaEncoder mediaEncoder, ILogger<MediaEncoderStreamProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(mediaEncoder);
        ArgumentNullException.ThrowIfNull(logger);
        _mediaEncoder = mediaEncoder;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task EnrichAsync(MediaSourceInfo source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        try
        {
            var info = await _mediaEncoder.GetMediaInfo(
                new MediaInfoRequest
                {
                    MediaSource = source,
                    MediaType = DlnaProfileType.Video,
                    ExtractChapters = false,
                },
                timeout.Token).ConfigureAwait(false);

            if (info.MediaStreams is not { Count: > 0 })
            {
                _logger.LogWarning("AceStream probe found no streams for {Path}; Jellyfin will transcode.", source.Path);
                return;
            }

            source.MediaStreams = info.MediaStreams;
            source.Bitrate = info.Bitrate;
            if (!string.IsNullOrEmpty(info.Container))
            {
                source.Container = info.Container;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Any cancellation the caller did not request is a probe failure (timeout or a spurious
            // internal cancel): leave the source unchanged rather than breaking playback.
            if (timeout.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "AceStream probe timed out after {Timeout}s for {Path}; Jellyfin will transcode.",
                    ProbeTimeout.TotalSeconds,
                    source.Path);
            }
            else
            {
                _logger.LogWarning(
                    "AceStream probe was cancelled unexpectedly for {Path}; Jellyfin will transcode.",
                    source.Path);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A probe failure must never break playback — degrade to Jellyfin's default.
            _logger.LogWarning(ex, "AceStream probe failed for {Path}; Jellyfin will transcode.", source.Path);
        }
    }
}
