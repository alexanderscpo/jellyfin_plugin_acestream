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
}
