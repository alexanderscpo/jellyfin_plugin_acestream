using System.Net;
using System.Text;
using Jellyfin.Plugin.AceStream.Domain;
using Jellyfin.Plugin.AceStream.Infrastructure.Engine;
using Jellyfin.Plugin.AceStream.Infrastructure.Proxy;
using Jellyfin.Plugin.AceStream.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AceStream.Tests.Infrastructure.Engine;

public class EngineSessionReadinessTests
{
    private const string Hash = "8c9febd01a731ce6139bca444d6aac9aaa764a88";

    // Engine echoes its own host in URLs; configured host is what matters for routing.
    private const string EngineBaseUrl = "http://172.39.0.2:6878";

    private const string GetStreamPath = $"/ace/getstream?infohash={Hash}&format=json";

    // stat_url echoed by engine — host differs from configured; path is reused.
    private const string EchoedStatUrl = "http://10.0.0.9:6878/ace/stat/HASH/sess";

    private static readonly string GetStreamResponseWithStat = $$"""
        {"response":{"infohash":"{{Hash}}","stat_url":"{{EchoedStatUrl}}","playback_url":"http://10.0.0.9:6878/ace/manifest.m3u8/{{Hash}}/ses","is_live":1},"error":null}
        """;

    private static readonly string StatDl = """
        {"response":{"status":"dl","downloaded":8000000,"peers":2,"speed_down":1024},"error":null}
        """;

    private static readonly string StatPrebuf = """
        {"response":{"status":"prebuf","downloaded":0,"peers":1,"speed_down":0},"error":null}
        """;

    private static readonly string StatErr = """
        {"response":{"status":"err","downloaded":0,"peers":0,"speed_down":0},"error":null}
        """;

    private sealed class FixedEngineSettings : IEngineSettings
    {
        public FixedEngineSettings(string baseUrl) => BaseUrl = baseUrl;

        public string BaseUrl { get; }

        public int ReadinessTimeoutSeconds => 30;
    }

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static EngineSessionReadiness ReadinessOver(
        ScriptedHttpMessageHandler handler,
        string baseUrl = EngineBaseUrl)
        => new(
            new FakeHttpClientFactory(handler),
            new FixedEngineSettings(baseUrl),
            NullLogger<EngineSessionReadiness>.Instance,
            readinessTimeout: TimeSpan.FromMilliseconds(300),
            pollInterval: TimeSpan.FromMilliseconds(15));

    // ── 1 ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsReadyAsync_StatusDl_OnFirstPoll_ReturnsTrue()
    {
        var handler = new ScriptedHttpMessageHandler((req, _) =>
        {
            // First request: getstream
            if (req.RequestUri!.AbsolutePath.Contains("getstream"))
            {
                return Task.FromResult(Json(GetStreamResponseWithStat));
            }

            // Second request: stat → dl immediately
            return Task.FromResult(Json(StatDl));
        });

        var ready = await ReadinessOver(handler).IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.True(ready);
    }

    // ── 2 ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsReadyAsync_StatusPrebuf_ThenDl_OnThirdPoll_ReturnsTrue()
    {
        var callCount = 0;
        var handler = new ScriptedHttpMessageHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("getstream"))
            {
                return Task.FromResult(Json(GetStreamResponseWithStat));
            }

            callCount++;
            // First two stat polls → prebuf, third → dl
            var body = callCount < 3 ? StatPrebuf : StatDl;
            return Task.FromResult(Json(body));
        });

        var ready = await ReadinessOver(handler).IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.True(ready);
        Assert.True(callCount >= 3);
    }

    // ── 3 ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsReadyAsync_StatusErr_ReturnsFalse_Immediately()
    {
        var handler = new ScriptedHttpMessageHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("getstream"))
            {
                return Task.FromResult(Json(GetStreamResponseWithStat));
            }

            return Task.FromResult(Json(StatErr));
        });

        var ready = await ReadinessOver(handler).IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.False(ready);
    }

    // ── 4 ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsReadyAsync_NeverReachDl_WithinDeadline_ReturnsFalse()
    {
        // Always returns prebuf — never transitions to dl.
        var handler = new ScriptedHttpMessageHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("getstream"))
            {
                return Task.FromResult(Json(GetStreamResponseWithStat));
            }

            return Task.FromResult(Json(StatPrebuf));
        });

        var ready = await ReadinessOver(handler).IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.False(ready);
    }

    // ── 5 ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsReadyAsync_HttpErrorOnGetStream_FailsOpenTrue()
    {
        var handler = new ScriptedHttpMessageHandler((_, _) =>
            throw new HttpRequestException("engine unreachable"));

        var ready = await ReadinessOver(handler).IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.True(ready);
    }

    // ── 6 ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsReadyAsync_MalformedJson_FailsOpenTrue()
    {
        var handler = new ScriptedHttpMessageHandler((_, _) =>
            Task.FromResult(Json("not json at all {{{")));

        var ready = await ReadinessOver(handler).IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.True(ready);
    }

    // ── 7 ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsReadyAsync_MissingStatUrl_FailsOpenTrue()
    {
        // getstream response with null stat_url
        const string NoStatUrl = """{"response":{"infohash":"HASH","stat_url":null,"playback_url":null},"error":null}""";

        var handler = new ScriptedHttpMessageHandler((_, _) => Task.FromResult(Json(NoStatUrl)));

        var ready = await ReadinessOver(handler).IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.True(ready);
    }

    // ── 8 ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsReadyAsync_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new ScriptedHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Json(string.Empty);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ReadinessOver(handler).IsReadyAsync(Infohash.Create(Hash), cts.Token));
    }

    // ── 9 ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsReadyAsync_EmptyEngineBaseUrl_ReturnsTrueWithoutHttp()
    {
        var handler = new ScriptedHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("should not be called"));

        var ready = await ReadinessOver(handler, baseUrl: string.Empty)
            .IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.True(ready);
        Assert.Equal(0, handler.RequestCount);
    }

    // ── 10 ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsReadyAsync_NoStopCommandEverIssued()
    {
        var requestedUris = new List<string>();

        var handler = new ScriptedHttpMessageHandler((req, _) =>
        {
            requestedUris.Add(req.RequestUri!.ToString());

            if (req.RequestUri.AbsolutePath.Contains("getstream"))
            {
                return Task.FromResult(Json(GetStreamResponseWithStat));
            }

            return Task.FromResult(Json(StatDl));
        });

        await ReadinessOver(handler).IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.DoesNotContain(requestedUris, u => u.Contains("/ace/cmd/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(requestedUris, u => u.Contains("method=stop", StringComparison.OrdinalIgnoreCase));
    }

    // ── Fix 1: per-request timeout ────────────────────────────────────────────

    /// <summary>
    /// Engine hangs indefinitely on getstream; caller token is live; per-request bound fires.
    /// Must fail open (return true) without throwing.
    /// </summary>
    [Fact]
    public async Task IsReadyAsync_EngineHangs_OnGetStream_PerRequestTimeoutFires_FailsOpenTrue()
    {
        // Handler delays forever (honoring its cancellation token so the test is not slow).
        var handler = new ScriptedHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Json(string.Empty);
        });

        // Inject a very short per-request timeout so the test does not wait long.
        var sut = new EngineSessionReadiness(
            new FakeHttpClientFactory(handler),
            new FixedEngineSettings(EngineBaseUrl),
            NullLogger<EngineSessionReadiness>.Instance,
            readinessTimeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(15),
            perRequestTimeout: TimeSpan.FromMilliseconds(80));

        var ready = await sut.IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.True(ready);
    }

    /// <summary>
    /// Engine hangs indefinitely on a stat poll; caller token is live; per-request bound fires.
    /// Must fail open (return true) without throwing.
    /// </summary>
    [Fact]
    public async Task IsReadyAsync_EngineHangs_OnStatPoll_PerRequestTimeoutFires_FailsOpenTrue()
    {
        var statCallCount = 0;
        var handler = new ScriptedHttpMessageHandler(async (req, ct) =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("getstream"))
            {
                return Json(GetStreamResponseWithStat);
            }

            statCallCount++;
            // First stat poll hangs forever (honoring ct so the test is not slow).
            await Task.Delay(Timeout.Infinite, ct);
            return Json(string.Empty);
        });

        var sut = new EngineSessionReadiness(
            new FakeHttpClientFactory(handler),
            new FixedEngineSettings(EngineBaseUrl),
            NullLogger<EngineSessionReadiness>.Instance,
            readinessTimeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(15),
            perRequestTimeout: TimeSpan.FromMilliseconds(80));

        var ready = await sut.IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.True(ready);
        Assert.True(statCallCount >= 1, "Expected at least one stat poll to have been attempted.");
    }

    // ── Fix 2 + 4: zero/negative timeout and settings-sourced path ────────────

    private sealed class ConfigurableEngineSettings : IEngineSettings
    {
        public ConfigurableEngineSettings(string baseUrl, int readinessTimeoutSeconds)
        {
            BaseUrl = baseUrl;
            ReadinessTimeoutSeconds = readinessTimeoutSeconds;
        }

        public string BaseUrl { get; }

        public int ReadinessTimeoutSeconds { get; }
    }

    /// <summary>
    /// When IEngineSettings.ReadinessTimeoutSeconds == 0 and no override is provided,
    /// the effective timeout is zero and the probe should fail open immediately without
    /// making any HTTP calls.
    /// </summary>
    [Fact]
    public async Task IsReadyAsync_SettingsTimeoutZero_FailsOpenImmediately_NoHttpCalls()
    {
        var handler = new ScriptedHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("should not be called"));

        var sut = new EngineSessionReadiness(
            new FakeHttpClientFactory(handler),
            new ConfigurableEngineSettings(EngineBaseUrl, readinessTimeoutSeconds: 0),
            NullLogger<EngineSessionReadiness>.Instance);

        var ready = await sut.IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.True(ready);
        Assert.Equal(0, handler.RequestCount);
    }

    /// <summary>
    /// When IEngineSettings.ReadinessTimeoutSeconds is negative, the behavior should be the
    /// same as zero: fail open immediately without making any HTTP calls.
    /// </summary>
    [Fact]
    public async Task IsReadyAsync_SettingsTimeoutNegative_FailsOpenImmediately_NoHttpCalls()
    {
        var handler = new ScriptedHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("should not be called"));

        var sut = new EngineSessionReadiness(
            new FakeHttpClientFactory(handler),
            new ConfigurableEngineSettings(EngineBaseUrl, readinessTimeoutSeconds: -1),
            NullLogger<EngineSessionReadiness>.Instance);

        var ready = await sut.IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.True(ready);
        Assert.Equal(0, handler.RequestCount);
    }

    /// <summary>
    /// When no readinessTimeout override is provided and settings return a positive value,
    /// the settings-sourced timeout is used and the probe runs normally.
    /// </summary>
    [Fact]
    public async Task IsReadyAsync_SettingsTimeoutUsed_WhenOverrideAbsent_ProbeRuns()
    {
        var handler = new ScriptedHttpMessageHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("getstream"))
            {
                return Task.FromResult(Json(GetStreamResponseWithStat));
            }

            return Task.FromResult(Json(StatDl));
        });

        // No readinessTimeout override; settings provide 2 seconds.
        var sut = new EngineSessionReadiness(
            new FakeHttpClientFactory(handler),
            new ConfigurableEngineSettings(EngineBaseUrl, readinessTimeoutSeconds: 2),
            NullLogger<EngineSessionReadiness>.Instance,
            pollInterval: TimeSpan.FromMilliseconds(15));

        var ready = await sut.IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.True(ready);
    }

    // ── Fix 5: unparseable/relative stat_url → fail open ─────────────────────

    /// <summary>
    /// When the engine echoes a stat_url that cannot be parsed as an absolute URI (e.g. a truly
    /// relative path like "ace/stat/HASH/sess" with no leading slash), RebuildUrl in the old code
    /// returned the raw string as-is. HttpClient.GetFromJsonAsync then threw
    /// InvalidOperationException because it can't use a relative URI without a BaseAddress.
    /// The class must now handle this gracefully and fail open.
    /// </summary>
    [Fact]
    public async Task IsReadyAsync_UnparseableStatUrl_FailsOpenTrue()
    {
        // stat_url with no scheme and no leading slash — TryCreate(UriKind.Absolute) returns false.
        const string RelativeStatUrlResponse = """
            {"response":{"infohash":"HASH","stat_url":"ace/stat/HASH/sess","playback_url":null},"error":null}
            """;

        var handler = new ScriptedHttpMessageHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("getstream"))
            {
                return Task.FromResult(Json(RelativeStatUrlResponse));
            }

            // Should never be reached; the stat poll must not happen.
            throw new InvalidOperationException("Unexpected HTTP call with unparseable stat_url");
        });

        var ready = await ReadinessOver(handler).IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.True(ready);
    }
}
