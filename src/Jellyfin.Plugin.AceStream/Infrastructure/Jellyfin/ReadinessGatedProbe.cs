using Jellyfin.Plugin.AceStream.Domain;
using Jellyfin.Plugin.AceStream.Infrastructure.Proxy;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;

/// <summary>
/// <see cref="IMediaSourceProbe"/> decorator that gates the expensive codec probe on live stream
/// readiness: if the engine reports the channel is not pulling data, the inner probe is skipped
/// (no point hanging ffprobe on a dead P2P stream). When readiness cannot be determined it fails
/// open and probes anyway, so the gate never makes playback worse than not having it.
/// </summary>
public sealed class ReadinessGatedProbe : IMediaSourceProbe
{
    private readonly IMediaSourceProbe _inner;
    private readonly IStreamReadiness _readiness;
    private readonly ILogger<ReadinessGatedProbe> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReadinessGatedProbe"/> class.
    /// </summary>
    /// <param name="inner">The codec probe to run once the stream is ready.</param>
    /// <param name="readiness">Checks whether the stream is pulling data live.</param>
    /// <param name="logger">The logger.</param>
    public ReadinessGatedProbe(IMediaSourceProbe inner, IStreamReadiness readiness, ILogger<ReadinessGatedProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(logger);
        _inner = inner;
        _readiness = readiness;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task EnrichAsync(MediaSourceInfo source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!TryGetInfohash(source.Id, out var infohash))
        {
            // Without a parseable infohash we can't ask the engine; don't block, just probe.
            await _inner.EnrichAsync(source, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!await _readiness.IsReadyAsync(infohash, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation(
                "AceStream channel {Infohash} is not pulling data; skipping codec probe.", infohash.Value);
            return;
        }

        await _inner.EnrichAsync(source, cancellationToken).ConfigureAwait(false);
    }

    private static bool TryGetInfohash(string? id, out Infohash infohash)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            try
            {
                infohash = Infohash.Create(id);
                return true;
            }
            catch (ArgumentException)
            {
                // Fall through to the failure result below.
            }
        }

        infohash = null!;
        return false;
    }
}
