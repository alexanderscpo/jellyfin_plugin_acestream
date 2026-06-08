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
/// </summary>
public sealed class CachingMediaSourceProbe : IMediaSourceProbe
{
    private readonly IMediaSourceProbe _inner;
    private readonly IProbeCacheSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CachingMediaSourceProbe> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

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
        if (!string.IsNullOrEmpty(key)
            && _cache.TryGetValue(key, out var entry)
            && entry.ExpiresAt > _timeProvider.GetUtcNow())
        {
            _logger.LogDebug("AceStream codec cache hit for {Infohash}; skipping readiness and probe.", key);
            Apply(entry, source);
            return;
        }

        await _inner.EnrichAsync(source, cancellationToken).ConfigureAwait(false);

        var ttl = _settings.CodecCacheTtl;
        if (ttl > TimeSpan.Zero && !string.IsNullOrEmpty(key) && source.MediaStreams is { Count: > 0 })
        {
            _cache[key] = new CacheEntry(
                source.MediaStreams,
                source.Bitrate,
                source.Container,
                _timeProvider.GetUtcNow() + ttl);
        }
    }

    private static void Apply(CacheEntry entry, MediaSourceInfo source)
    {
        source.MediaStreams = entry.Streams;
        source.Bitrate = entry.Bitrate;
        source.Container = entry.Container;
    }

    private readonly record struct CacheEntry(
        IReadOnlyList<MediaStream> Streams,
        int? Bitrate,
        string? Container,
        DateTimeOffset ExpiresAt);
}
