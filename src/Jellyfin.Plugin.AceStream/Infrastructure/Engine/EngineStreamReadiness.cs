using System.Net.Http.Json;
using System.Text.Json;
using Jellyfin.Plugin.AceStream.Domain;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AceStream.Infrastructure.Engine;

/// <summary>
/// <see cref="IStreamReadiness"/> backed by the AceStream engine's HTTP API. It briefly opens a
/// playback session (<c>/ace/getstream?...&amp;format=json</c>), polls the returned <c>stat_url</c>
/// until bytes are actually arriving, and always stops its session afterwards. The engine echoes
/// stat/command URLs with its own view of the host, so only their path is reused — the request
/// targets the configured engine base URL, which is the one the plugin can reach.
/// </summary>
public sealed class EngineStreamReadiness : IStreamReadiness
{
    private static readonly TimeSpan DefaultReadinessTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEngineSettings _settings;
    private readonly ILogger<EngineStreamReadiness> _logger;
    private readonly TimeSpan _readinessTimeout;
    private readonly TimeSpan _pollInterval;

    /// <summary>
    /// Initializes a new instance of the <see cref="EngineStreamReadiness"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Creates the HTTP client used to call the engine, per request.</param>
    /// <param name="settings">Provides the current engine base URL.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="readinessTimeout">How long to wait for data before declaring the stream dead.</param>
    /// <param name="pollInterval">How often to poll the engine's stat endpoint.</param>
    public EngineStreamReadiness(
        IHttpClientFactory httpClientFactory,
        IEngineSettings settings,
        ILogger<EngineStreamReadiness> logger,
        TimeSpan? readinessTimeout = null,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
        _readinessTimeout = readinessTimeout ?? DefaultReadinessTimeout;
        _pollInterval = pollInterval ?? DefaultPollInterval;
    }

    /// <inheritdoc />
    public async Task<bool> IsReadyAsync(Infohash infohash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(infohash);

        var baseUrl = _settings.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            // No engine to ask: fail open so the gate never blocks playback.
            return true;
        }

        var root = baseUrl.TrimEnd('/');
        var httpClient = _httpClientFactory.CreateClient(EngineSearchClient.HttpClientName);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_readinessTimeout);

        string? commandUrl = null;
        try
        {
            var start = await httpClient
                .GetFromJsonAsync<GetStreamResponseDto>(
                    $"{root}/ace/getstream?infohash={infohash.Value}&format=json", JsonOptions, timeout.Token)
                .ConfigureAwait(false);

            var session = start?.Response;
            if (string.IsNullOrEmpty(session?.StatUrl))
            {
                // Can't determine readiness (no session) — fail open.
                return true;
            }

            var statUrl = RebuildUrl(root, session.StatUrl);
            commandUrl = string.IsNullOrEmpty(session.CommandUrl) ? null : RebuildUrl(root, session.CommandUrl);

            while (true)
            {
                var stat = await httpClient
                    .GetFromJsonAsync<StatResponseDto>(statUrl, JsonOptions, timeout.Token)
                    .ConfigureAwait(false);

                // Bytes arriving is the only reliable "alive now" signal: the engine reports
                // status "dl" even for dead channels with zero peers and zero downloaded.
                if ((stat?.Response?.Downloaded ?? 0) > 0)
                {
                    return true;
                }

                await Task.Delay(_pollInterval, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "AceStream channel {Infohash} pulled no data within {Timeout}s; treating as not ready.",
                infohash.Value,
                _readinessTimeout.TotalSeconds);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "AceStream readiness check failed for {Infohash}; assuming ready.", infohash.Value);
            return true;
        }
        finally
        {
            await StopSessionAsync(httpClient, commandUrl).ConfigureAwait(false);
        }
    }

    private async Task StopSessionAsync(HttpClient httpClient, string? commandUrl)
    {
        if (string.IsNullOrEmpty(commandUrl))
        {
            return;
        }

        try
        {
            using var stopTimeout = new CancellationTokenSource(StopTimeout);
            using var response = await httpClient
                .GetAsync($"{commandUrl}?method=stop", stopTimeout.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Best-effort cleanup: if it fails, the engine reaps the idle session on its own.
            _logger.LogDebug(ex, "AceStream readiness cleanup (stop session) failed; engine will reap it.");
        }
    }

    private static string RebuildUrl(string root, string engineUrl)
    {
        if (Uri.TryCreate(engineUrl, UriKind.Absolute, out var absolute))
        {
            return $"{root}{absolute.PathAndQuery}";
        }

        return engineUrl.StartsWith('/') ? $"{root}{engineUrl}" : $"{root}/{engineUrl}";
    }

    private sealed class GetStreamResponseDto
    {
        public GetStreamResultDto? Response { get; set; }
    }

    private sealed class GetStreamResultDto
    {
        public string? StatUrl { get; set; }

        public string? CommandUrl { get; set; }
    }

    private sealed class StatResponseDto
    {
        public StatResultDto? Response { get; set; }
    }

    private sealed class StatResultDto
    {
        public long Downloaded { get; set; }

        public int Peers { get; set; }
    }
}
