using System.Collections.Concurrent;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;

/// <summary>
/// <see cref="IMediaSourceProbe"/> decorator that caches discovered codecs per channel so repeat
/// plays skip the whole inner pipeline (readiness check + ffprobe). Codecs are stable, so a short
/// TTL is plenty. Only successful probes are cached: a dead channel or a failed probe is left
/// uncached so the next play retries it. The cache key is the infohash (<see cref="MediaSourceInfo.Id"/>);
/// only the codec fields are cached, never the per-request path or analyze duration.
///
/// Concurrent callers for the same key coalesce: the first caller runs the inner probe and all
/// concurrent callers await the same in-flight task so ffprobe is invoked exactly once per key.
/// Cached codec lists are stored as defensive copies so a consumer mutating the returned list
/// does not corrupt the cache.
/// </summary>
public sealed class CachingMediaSourceProbe : IMediaSourceProbe
{
    private readonly IMediaSourceProbe _inner;
    private readonly IProbeCacheSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CachingMediaSourceProbe> _logger;

    // Cached successful results (key = infohash).
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    // In-flight probes keyed by infohash. ConcurrentDictionary + Lazy<Task> ensures that
    // concurrent misses for the same key share one inner-probe call.
    private readonly ConcurrentDictionary<string, Lazy<Task<CacheEntry?>>> _inFlight =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="CachingMediaSourceProbe"/> class.
    /// </summary>
    /// <param name="inner">The probe pipeline to run on a cache miss.</param>
    /// <param name="settings">Provides the cache time-to-live.</param>
    /// <param name="timeProvider">The clock used for expiry.</param>
    /// <param name="logger">The logger.</param>
    public CachingMediaSourceProbe(
        IMediaSourceProbe inner,
        IProbeCacheSettings settings,
        TimeProvider timeProvider,
        ILogger<CachingMediaSourceProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _inner = inner;
        _settings = settings;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task EnrichAsync(MediaSourceInfo source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var key = source.Id;

        // Fast path: valid cache entry.
        if (!string.IsNullOrEmpty(key)
            && _cache.TryGetValue(key, out var entry)
            && entry.ExpiresAt > _timeProvider.GetUtcNow())
        {
            _logger.LogDebug("AceStream codec cache hit for {Infohash}; skipping readiness and probe.", key);
            Apply(entry, source);
            return;
        }

        if (string.IsNullOrEmpty(key))
        {
            // No key — delegate without caching.
            await _inner.EnrichAsync(source, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Coalesce concurrent misses: GetOrAdd with Lazy<Task> ensures only one inner-probe
        // Task is created per key even when multiple callers race through the miss path.
        var capturedSource = source;
        var capturedCt = cancellationToken;
        var lazy = _inFlight.GetOrAdd(
            key,
            _ => new Lazy<Task<CacheEntry?>>(
                () => RunProbeAsync(key, capturedSource, capturedCt),
                LazyThreadSafetyMode.ExecutionAndPublication));

        CacheEntry? result;
        try
        {
            result = await lazy.Value.ConfigureAwait(false);
        }
        finally
        {
            // Remove the in-flight entry now that the task is done (regardless of success/failure).
            // A new miss will create a fresh Lazy on the next call.
            _inFlight.TryRemove(key, out _);
        }

        if (result is not null)
        {
            Apply(result.Value, source);
        }
    }

    private async Task<CacheEntry?> RunProbeAsync(
        string key,
        MediaSourceInfo source,
        CancellationToken cancellationToken)
    {
        await _inner.EnrichAsync(source, cancellationToken).ConfigureAwait(false);

        var ttl = _settings.CodecCacheTtl;
        if (ttl > TimeSpan.Zero && source.MediaStreams is { Count: > 0 })
        {
            // Defensive copy: store an independent list so consumer mutations do not corrupt cache.
            var entry = new CacheEntry(
                source.MediaStreams.ToList(),
                source.Bitrate,
                source.Container,
                _timeProvider.GetUtcNow() + ttl);

            _cache[key] = entry;
            return entry;
        }

        return null;
    }

    private static void Apply(CacheEntry entry, MediaSourceInfo source)
    {
        // Assign a new list built from the stored copy so each caller gets its own instance.
        source.MediaStreams = entry.Streams.ToList();
        source.Bitrate = entry.Bitrate;
        source.Container = entry.Container;
    }

    private readonly record struct CacheEntry(
        IReadOnlyList<MediaStream> Streams,
        int? Bitrate,
        string? Container,
        DateTimeOffset ExpiresAt);
}
