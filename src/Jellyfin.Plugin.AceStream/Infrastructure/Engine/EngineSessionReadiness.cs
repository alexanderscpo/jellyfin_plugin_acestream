using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.AceStream.Domain;
using Jellyfin.Plugin.AceStream.Infrastructure.Proxy;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AceStream.Infrastructure.Engine;

/// <summary>
/// <see cref="IStreamReadiness"/> backed by the AceStream engine's session status API.
/// Opens a getstream session, then polls the stat_url until the engine reports "dl"
/// (downloading — bytes available for playback). Fails open on all infrastructure errors
/// so the readiness gate never makes playback worse than skipping the check entirely.
/// Never issues a stop command so the session stays warm for acexy to join.
/// </summary>
public sealed class EngineSessionReadiness : IStreamReadiness
{
    /// <summary>
    /// Per-request timeout applied on top of the caller's token. Guards against an engine that
    /// accepts the connection but stalls: the linked CTS fires after this window and we treat the
    /// request as a transient failure (fail open) rather than letting a TaskCanceledException
    /// escape upward. Mirrors the pattern used in <see cref="EngineSearchClient"/>.
    /// </summary>
    private static readonly TimeSpan DefaultPerRequestTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEngineSettings _settings;
    private readonly ILogger<EngineSessionReadiness> _logger;
    private readonly TimeSpan _readinessTimeout;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _perRequestTimeout;

    /// <summary>
    /// Initializes a new instance of the <see cref="EngineSessionReadiness"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Creates the HTTP client used to call the engine, per request.</param>
    /// <param name="settings">Provides the current engine base URL and readiness timeout.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="readinessTimeout">Override the polling deadline; defaults to <see cref="IEngineSettings.ReadinessTimeoutSeconds"/> when omitted.</param>
    /// <param name="pollInterval">How long to wait between stat polls; defaults to 2 s when omitted.</param>
    /// <param name="perRequestTimeout">Per-request HTTP timeout; defaults to 10 s when omitted.</param>
    public EngineSessionReadiness(
        IHttpClientFactory httpClientFactory,
        IEngineSettings settings,
        ILogger<EngineSessionReadiness> logger,
        TimeSpan? readinessTimeout = null,
        TimeSpan? pollInterval = null,
        TimeSpan? perRequestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
        _readinessTimeout = readinessTimeout ?? TimeSpan.FromSeconds(settings.ReadinessTimeoutSeconds);
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _perRequestTimeout = perRequestTimeout ?? DefaultPerRequestTimeout;
    }

    /// <inheritdoc />
    public async Task<bool> IsReadyAsync(Infohash infohash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(infohash);

        var baseUrl = _settings.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            // No engine configured: fail open so the gate never blocks playback.
            return true;
        }

        // ReadinessTimeoutSeconds == 0 (or negative) is documented as "fail open immediately".
        if (_readinessTimeout <= TimeSpan.Zero)
        {
            _logger.LogDebug(
                "AceStream readiness timeout is {Timeout}; failing open immediately for {Infohash}.",
                _readinessTimeout,
                infohash.Value);
            return true;
        }

        try
        {
            var statUrl = await GetStatUrlAsync(baseUrl, infohash, cancellationToken).ConfigureAwait(false);
            if (statUrl is null)
            {
                _logger.LogWarning(
                    "AceStream engine returned no stat_url for {Infohash}; assuming ready.",
                    infohash.Value);
                return true;
            }

            // Rebuild against configured engine base so we route correctly regardless of what
            // host the engine echoed back in the response.
            var resolvedStatUrl = RebuildUrl(baseUrl, statUrl);
            if (resolvedStatUrl is null)
            {
                _logger.LogWarning(
                    "AceStream engine echoed a relative stat_url for {Infohash}; assuming ready.",
                    infohash.Value);
                return true;
            }

            return await PollUntilReadyAsync(resolvedStatUrl, infohash, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancelled: propagate.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "AceStream readiness check failed for {Infohash}; assuming ready.", infohash.Value);
            return true;
        }
    }

    private async Task<string?> GetStatUrlAsync(string baseUrl, Infohash infohash, CancellationToken cancellationToken)
    {
        var url = $"{baseUrl.TrimEnd('/')}/ace/getstream?infohash={infohash.Value}&format=json";
        var httpClient = _httpClientFactory.CreateClient(EngineSearchClient.HttpClientName);

        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestCts.CancelAfter(_perRequestTimeout);

        try
        {
            var dto = await httpClient
                .GetFromJsonAsync<GetStreamResponseDto>(url, JsonOptions, requestCts.Token)
                .ConfigureAwait(false);

            return dto?.Response?.StatUrl;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Per-request timeout fired; engine stalled. Return null so the caller fails open.
            _logger.LogWarning(
                "AceStream getstream timed out after {Timeout}s for {Infohash}; assuming ready.",
                _perRequestTimeout.TotalSeconds,
                infohash.Value);
            return null;
        }
    }

    private async Task<bool> PollUntilReadyAsync(string statUrl, Infohash infohash, CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient(EngineSearchClient.HttpClientName);
        var deadline = DateTime.UtcNow + _readinessTimeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            StatResultDto? result;
            try
            {
                using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                requestCts.CancelAfter(_perRequestTimeout);

                var dto = await httpClient
                    .GetFromJsonAsync<StatResponseDto>(statUrl, JsonOptions, requestCts.Token)
                    .ConfigureAwait(false);
                result = dto?.Response;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Per-request timeout fired on a stat poll; engine stalled. Fail open.
                _logger.LogWarning(
                    "AceStream stat poll timed out after {Timeout}s for {Infohash}; assuming ready.",
                    _perRequestTimeout.TotalSeconds,
                    infohash.Value);
                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                _logger.LogWarning(ex, "AceStream stat poll failed for {Infohash}; assuming ready.", infohash.Value);
                return true;
            }

            switch (result?.Status)
            {
                case "dl":
                    return true;
                case "err":
                    _logger.LogInformation(
                        "AceStream engine reported error status for {Infohash}; treating as not ready.",
                        infohash.Value);
                    return false;
                default:
                    // "prebuf", "check", or anything else: keep polling.
                    break;
            }

            await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "AceStream channel {Infohash} did not reach 'dl' within {Timeout}s; treating as not ready.",
            infohash.Value,
            _readinessTimeout.TotalSeconds);
        return false;
    }

    /// <summary>
    /// Rebuilds <paramref name="engineUrl"/> (which may use the engine's internal host/port) by
    /// replacing its scheme+host+port with those from <paramref name="root"/> (the configured base URL).
    /// Only the path and query are reused from the engine-echoed URL.
    /// Returns <see langword="null"/> when <paramref name="engineUrl"/> is not an absolute URI
    /// (e.g. a relative URL echoed by the engine), allowing the caller to fail open with a warning
    /// rather than passing a bare relative path to <see cref="HttpClient"/> which would throw
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    internal static string? RebuildUrl(string root, string engineUrl)
    {
        if (!Uri.TryCreate(engineUrl, UriKind.Absolute, out var echoed))
        {
            return null;
        }

        if (!Uri.TryCreate(root.TrimEnd('/'), UriKind.Absolute, out var configured))
        {
            return null;
        }

        var builder = new UriBuilder(configured.Scheme, configured.Host, configured.Port)
        {
            Path = echoed.AbsolutePath,
            Query = echoed.Query.TrimStart('?'),
        };

        return builder.Uri.ToString();
    }

    // ── Private DTOs ──────────────────────────────────────────────────────────

    private sealed class GetStreamResponseDto
    {
        public GetStreamResultDto? Response { get; set; }
    }

    private sealed class GetStreamResultDto
    {
        public string? StatUrl { get; set; }

        public string? PlaybackUrl { get; set; }
    }

    private sealed class StatResponseDto
    {
        public StatResultDto? Response { get; set; }
    }

    private sealed class StatResultDto
    {
        public string? Status { get; set; }

        public long Downloaded { get; set; }
    }
}
