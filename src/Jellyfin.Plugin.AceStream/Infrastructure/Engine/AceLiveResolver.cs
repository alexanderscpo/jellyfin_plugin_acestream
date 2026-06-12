using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Jellyfin.Plugin.AceStream.Domain;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AceStream.Infrastructure.Engine;

/// <summary>
/// Lazily resolves an <c>.acelive</c> transport-file URL to a live <see cref="Infohash"/> by
/// calling the engine's <c>GET /ace/getstream?url=…&amp;format=json</c> endpoint.
/// Results are cached with a configurable TTL so subsequent playback requests skip the round-trip.
/// Cache entries are evicted on demand (call <see cref="Evict"/>) so rotation recovery works.
/// </summary>
/// <remarks>
/// Depends only on <see cref="IHttpClientFactory"/>, <see cref="IEngineSettings"/>, and
/// <see cref="TimeProvider"/> — no Jellyfin types; lives purely in Infrastructure.
/// Reuses the <c>"AceEngine"</c> named client registered by <see cref="EngineSearchClient"/>.
/// Concurrency: no coalescing in v1 — duplicate simultaneous plays of the same URL may each
/// issue one engine round-trip (idempotent, cheap relative to the downstream probe).
/// </remarks>
public sealed class AceLiveResolver : IAceLiveResolver
{
    /// <summary>
    /// Default TTL for cached URL-to-Infohash mappings.
    /// Chosen to outlast a typical broadcast rotation cycle while staying short enough to pick
    /// up a stream restart within half an hour.
    /// </summary>
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Per-request timeout applied on top of the caller's token. Guards against an engine that
    /// accepts the connection but stalls: the linked CTS fires after this window and we degrade
    /// to <see langword="null"/> rather than letting a <see cref="TaskCanceledException"/> escape.
    /// Mirrors the pattern in <see cref="EngineSearchClient"/> and <see cref="EngineSessionReadiness"/>.
    /// </summary>
    private static readonly TimeSpan DefaultPerRequestTimeout = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEngineSettings _settings;
    private readonly ILogger<AceLiveResolver> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _ttl;
    private readonly TimeSpan _perRequestTimeout;
    private readonly ConcurrentDictionary<string, (Infohash Infohash, DateTimeOffset Expiry)> _cache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="AceLiveResolver"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Creates the HTTP client used to call the engine, per request.</param>
    /// <param name="settings">Provides the current engine base URL.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="timeProvider">Time source used for TTL comparisons; defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="ttl">Override the cache TTL; defaults to <see cref="DefaultTtl"/> when omitted.</param>
    /// <param name="perRequestTimeout">Per-request HTTP timeout; defaults to <see cref="DefaultPerRequestTimeout"/> when omitted.</param>
    public AceLiveResolver(
        IHttpClientFactory httpClientFactory,
        IEngineSettings settings,
        ILogger<AceLiveResolver> logger,
        TimeProvider? timeProvider = null,
        TimeSpan? ttl = null,
        TimeSpan? perRequestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ttl = ttl ?? DefaultTtl;
        _perRequestTimeout = perRequestTimeout ?? DefaultPerRequestTimeout;
    }

    /// <summary>
    /// Resolves the given <c>.acelive</c> URL to a live <see cref="Infohash"/>.
    /// Returns <see langword="null"/> on any infrastructure failure (fail-soft).
    /// </summary>
    /// <param name="url">The <c>.acelive</c> transport-file URL to resolve.</param>
    /// <param name="cancellationToken">Caller's cancellation token; propagates as <see cref="OperationCanceledException"/> when cancelled.</param>
    /// <returns>The resolved <see cref="Infohash"/>, or <see langword="null"/> on failure.</returns>
    public async Task<Infohash?> ResolveAsync(string url, CancellationToken cancellationToken)
    {
        if (TryGetCached(url, out var cached))
        {
            return cached;
        }

        return await FetchAndCacheAsync(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes the cache entry for <paramref name="url"/> so the next <see cref="ResolveAsync"/>
    /// call issues a fresh engine request. Call this after detecting a stale or broken stream
    /// to enable rotation recovery.
    /// </summary>
    /// <param name="url">The URL whose cached resolution should be discarded.</param>
    public void Evict(string url) => _cache.TryRemove(url, out _);

    private bool TryGetCached(string url, out Infohash? infohash)
    {
        if (_cache.TryGetValue(url, out var entry) &&
            entry.Expiry > _timeProvider.GetUtcNow())
        {
            infohash = entry.Infohash;
            return true;
        }

        infohash = null;
        return false;
    }

    private async Task<Infohash?> FetchAndCacheAsync(string url, CancellationToken cancellationToken)
    {
        var baseUrl = _settings.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogWarning("AceStream engine URL is not configured; cannot resolve {Url}.", url);
            return null;
        }

        var requestUrl = BuildRequestUrl(baseUrl, url);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_perRequestTimeout);

        try
        {
            var httpClient = _httpClientFactory.CreateClient(EngineSearchClient.HttpClientName);
            var dto = await httpClient
                .GetFromJsonAsync<GetStreamByUrlResponseDto>(requestUrl, JsonOptions, timeout.Token)
                .ConfigureAwait(false);

            return ParseAndCache(url, dto);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Per-request timeout fired; engine stalled. Degrade to null rather than throwing.
            _logger.LogWarning(
                "AceLive resolution timed out after {Timeout}s for {Url}; returning null.",
                _perRequestTimeout.TotalSeconds,
                url);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "AceLive resolution failed for {Url}; returning null.", url);
            return null;
        }
    }

    private Infohash? ParseAndCache(string url, GetStreamByUrlResponseDto? dto)
    {
        var raw = dto?.Response?.Infohash;
        if (string.IsNullOrWhiteSpace(raw))
        {
            _logger.LogWarning("AceLive engine returned no infohash for {Url}; returning null.", url);
            return null;
        }

        Infohash infohash;
        try
        {
            infohash = Infohash.Create(raw);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "AceLive engine returned an invalid infohash '{Infohash}' for {Url}; returning null.", raw, url);
            return null;
        }

        var expiry = _timeProvider.GetUtcNow() + _ttl;
        _cache[url] = (infohash, expiry);
        return infohash;
    }

    private static string BuildRequestUrl(string baseUrl, string aceUrl)
        => $"{baseUrl.TrimEnd('/')}/ace/getstream?url={Uri.EscapeDataString(aceUrl)}&format=json";

    // ── Private DTOs ──────────────────────────────────────────────────────────

    private sealed class GetStreamByUrlResponseDto
    {
        public GetStreamByUrlResultDto? Response { get; set; }
    }

    private sealed class GetStreamByUrlResultDto
    {
        public string? Infohash { get; set; }
    }
}
