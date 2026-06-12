using Jellyfin.Plugin.AceStream.Domain;

namespace Jellyfin.Plugin.AceStream.Infrastructure.Engine;

/// <summary>
/// Lazily resolves an <c>.acelive</c> transport-file URL to a live <see cref="Infohash"/>.
/// Results are cached with a configurable TTL so subsequent playback requests skip the round-trip.
/// Cache entries can be evicted on demand to enable rotation recovery.
/// </summary>
public interface IAceLiveResolver
{
    /// <summary>
    /// Resolves the given <c>.acelive</c> URL to a live <see cref="Infohash"/>.
    /// Returns <see langword="null"/> on any infrastructure failure (fail-soft).
    /// </summary>
    /// <param name="url">The <c>.acelive</c> transport-file URL to resolve.</param>
    /// <param name="cancellationToken">Caller's cancellation token; propagates as <see cref="OperationCanceledException"/> when cancelled.</param>
    /// <returns>The resolved <see cref="Infohash"/>, or <see langword="null"/> on failure.</returns>
    Task<Infohash?> ResolveAsync(string url, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the cache entry for <paramref name="url"/> so the next <see cref="ResolveAsync"/>
    /// call issues a fresh engine request. Call this after detecting a stale or broken stream
    /// to enable rotation recovery.
    /// </summary>
    /// <param name="url">The URL whose cached resolution should be discarded.</param>
    void Evict(string url);
}
