using Jellyfin.Plugin.AceStream.Domain;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AceStream.Infrastructure.Proxy;

/// <summary>
/// <see cref="IStreamReadiness"/> backed by the acexy proxy — the same path playback uses. It opens
/// the proxy stream URL and waits for the first byte: if data arrives the channel is live; if the
/// connection delivers nothing within the readiness window it is dead now. Because the check goes
/// through acexy (which multiplexes by infohash), it shares the engine session with playback instead
/// of opening a separate one. Fails open: if readiness can't be determined, playback is never blocked.
/// </summary>
public sealed class ProxyStreamReadiness : IStreamReadiness
{
    /// <summary>
    /// The name of the <see cref="IHttpClientFactory"/> client used to read the proxy stream.
    /// </summary>
    internal const string HttpClientName = "AceProxy";

    private static readonly TimeSpan DefaultReadinessTimeout = TimeSpan.FromSeconds(8);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IProxySettings _settings;
    private readonly ILogger<ProxyStreamReadiness> _logger;
    private readonly TimeSpan _readinessTimeout;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProxyStreamReadiness"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Creates the HTTP client used to read the proxy, per request.</param>
    /// <param name="settings">Provides the current proxy base URL.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="readinessTimeout">How long to wait for the first byte before declaring the stream dead.</param>
    public ProxyStreamReadiness(
        IHttpClientFactory httpClientFactory,
        IProxySettings settings,
        ILogger<ProxyStreamReadiness> logger,
        TimeSpan? readinessTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
        _readinessTimeout = readinessTimeout ?? DefaultReadinessTimeout;
    }

    /// <inheritdoc />
    public async Task<bool> IsReadyAsync(Infohash infohash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(infohash);

        var baseUrl = _settings.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            // No proxy to ask: fail open so the gate never blocks playback.
            return true;
        }

        var url = ProxyMediaSource.BuildStreamUrl(baseUrl, infohash);
        var httpClient = _httpClientFactory.CreateClient(HttpClientName);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_readinessTimeout);

        try
        {
            using var response = await httpClient
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var buffer = new byte[1];
            var read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);

            // A byte through the playback path is the only reliable "alive now" signal.
            return read > 0;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "AceStream channel {Infohash} delivered no data within {Timeout}s; treating as not ready.",
                infohash.Value,
                _readinessTimeout.TotalSeconds);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            _logger.LogWarning(ex, "AceStream readiness check failed for {Infohash}; assuming ready.", infohash.Value);
            return true;
        }
    }
}
