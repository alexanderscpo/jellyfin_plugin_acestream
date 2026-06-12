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
    private static readonly TimeSpan DefaultReadinessTimeout = TimeSpan.FromSeconds(30);
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

    /// <summary>
    /// Initializes a new instance of the <see cref="EngineSessionReadiness"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Creates the HTTP client used to call the engine, per request.</param>
    /// <param name="settings">Provides the current engine base URL and readiness timeout.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="readinessTimeout">Override the polling deadline; defaults to <see cref="IEngineSettings.ReadinessTimeoutSeconds"/> when omitted.</param>
    /// <param name="pollInterval">How long to wait between stat polls; defaults to 2 s when omitted.</param>
    public EngineSessionReadiness(
        IHttpClientFactory httpClientFactory,
        IEngineSettings settings,
        ILogger<EngineSessionReadiness> logger,
        TimeSpan? readinessTimeout = null,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
        _readinessTimeout = readinessTimeout ?? TimeSpan.FromSeconds(settings.ReadinessTimeoutSeconds);
        _pollInterval = pollInterval ?? DefaultPollInterval;
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

        var dto = await httpClient
            .GetFromJsonAsync<GetStreamResponseDto>(url, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return dto?.Response?.StatUrl;
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
                var dto = await httpClient
                    .GetFromJsonAsync<StatResponseDto>(statUrl, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                result = dto?.Response;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
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
    /// </summary>
    internal static string RebuildUrl(string root, string engineUrl)
    {
        if (!Uri.TryCreate(engineUrl, UriKind.Absolute, out var echoed))
        {
            return engineUrl;
        }

        if (!Uri.TryCreate(root.TrimEnd('/'), UriKind.Absolute, out var configured))
        {
            return engineUrl;
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
