using Jellyfin.Plugin.AceStream.Domain;

namespace Jellyfin.Plugin.AceStream.Infrastructure.Engine;

/// <summary>
/// Checks whether an AceStream channel is actually pulling data live (peers connected, bytes
/// arriving), as opposed to the stale <c>availability</c> snapshot from <c>/search</c>. Used to
/// gate the expensive codec probe: a dead channel is skipped fast instead of hanging ffprobe.
/// </summary>
public interface IStreamReadiness
{
    /// <summary>
    /// Determines whether the stream behind <paramref name="infohash"/> is currently downloading.
    /// </summary>
    /// <param name="infohash">The channel identity.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// <see langword="false"/> only when the engine confirms the stream is not pulling data within
    /// the readiness window; <see langword="true"/> when it is ready or readiness cannot be
    /// determined (fail-open, so the gate never makes playback worse than skipping the check).
    /// </returns>
    Task<bool> IsReadyAsync(Infohash infohash, CancellationToken cancellationToken);
}
