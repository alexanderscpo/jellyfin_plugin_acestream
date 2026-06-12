using System.Net;
using System.Text;
using Jellyfin.Plugin.AceStream.Infrastructure.Engine;
using Jellyfin.Plugin.AceStream.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AceStream.Tests.Infrastructure.Engine;

public class AceLiveResolverTests
{
    private const string ValidInfohash = "aabbccddeeff00112233445566778899aabbccdd";

    private static readonly string SuccessJson =
        $$"""{"response":{"infohash":"{{ValidInfohash}}"},"error":null}""";

    private sealed class FakeEngineSettings : IEngineSettings
    {
        public string BaseUrl => "http://engine:6878";

        public int ReadinessTimeoutSeconds => 30;
    }

    private sealed class EmptyBaseUrlEngineSettings : IEngineSettings
    {
        public string BaseUrl => string.Empty;

        public int ReadinessTimeoutSeconds => 30;
    }

    private static HttpResponseMessage JsonResponse(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static AceLiveResolver ResolverWith(
        ScriptedHttpMessageHandler handler,
        IEngineSettings? settings = null,
        MutableTimeProvider? timeProvider = null,
        TimeSpan? ttl = null,
        TimeSpan? perRequestTimeout = null)
    {
        var factory = new FakeHttpClientFactory(handler);
        return new AceLiveResolver(
            factory,
            settings ?? new FakeEngineSettings(),
            NullLogger<AceLiveResolver>.Instance,
            timeProvider,
            ttl,
            perRequestTimeout);
    }

    // ── Task 1.1 — Cache miss: successful resolution ─────────────────────────

    [Fact]
    public async Task ResolveAsync_CacheMiss_CallsGetstream_ReturnsInfohash()
    {
        var url = "http://example.com/stream.acelive";
        var handler = new ScriptedHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(SuccessJson)));
        var factory = new FakeHttpClientFactory(handler);
        var resolver = new AceLiveResolver(factory, new FakeEngineSettings(), NullLogger<AceLiveResolver>.Instance);

        var result = await resolver.ResolveAsync(url, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ValidInfohash, result.Value);
        Assert.Equal(1, factory.CreateClientCallCount);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ResolveAsync_CacheMiss_RequestUrlContainsGetStreamWithEncodedUrlAndFormatJson()
    {
        var aceUrl = "http://example.com/stream.acelive";
        Uri? capturedUri = null;
        var handler = new ScriptedHttpMessageHandler((req, _) =>
        {
            capturedUri = req.RequestUri;
            return Task.FromResult(JsonResponse(SuccessJson));
        });
        var resolver = ResolverWith(handler);

        await resolver.ResolveAsync(aceUrl, CancellationToken.None);

        Assert.NotNull(capturedUri);
        var rawUrl = capturedUri!.ToString();
        Assert.Contains("/ace/getstream", rawUrl, StringComparison.Ordinal);
        Assert.Contains("url=" + Uri.EscapeDataString(aceUrl), rawUrl, StringComparison.Ordinal);
        Assert.Contains("format=json", rawUrl, StringComparison.Ordinal);
    }

    // ── Task 1.2 — Cache hit within TTL ──────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_CacheHitWithinTtl_DoesNotCallHttp()
    {
        var aceUrl = "http://example.com/stream.acelive";
        var ttl = TimeSpan.FromMinutes(30);
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new MutableTimeProvider(start);
        var handler = new ScriptedHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(SuccessJson)));
        var resolver = ResolverWith(handler, timeProvider: timeProvider, ttl: ttl);

        // First call — populates cache
        await resolver.ResolveAsync(aceUrl, CancellationToken.None);

        // Advance to just within TTL
        timeProvider.Advance(ttl - TimeSpan.FromSeconds(1));

        // Second call — should hit cache
        var result = await resolver.ResolveAsync(aceUrl, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ValidInfohash, result.Value);
        Assert.Equal(1, handler.RequestCount); // no second HTTP call
    }

    // ── Task 1.3 — Cache expired forces re-resolution ────────────────────────

    [Fact]
    public async Task ResolveAsync_CacheExpired_ReResolvesViaHttp()
    {
        var aceUrl = "http://example.com/stream.acelive";
        var ttl = TimeSpan.FromMinutes(30);
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new MutableTimeProvider(start);
        var handler = new ScriptedHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(SuccessJson)));
        var resolver = ResolverWith(handler, timeProvider: timeProvider, ttl: ttl);

        // First call
        await resolver.ResolveAsync(aceUrl, CancellationToken.None);

        // Advance past TTL
        timeProvider.Advance(ttl + TimeSpan.FromSeconds(1));

        // Second call — cache expired, must hit HTTP
        var result = await resolver.ResolveAsync(aceUrl, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, handler.RequestCount);
    }

    // ── Task 1.4 — Evict ─────────────────────────────────────────────────────

    [Fact]
    public async Task Evict_RemovesCacheEntry_NextCallReHitsHttp()
    {
        var aceUrl = "http://example.com/stream.acelive";
        var handler = new ScriptedHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(SuccessJson)));
        var resolver = ResolverWith(handler);

        // Populate cache
        await resolver.ResolveAsync(aceUrl, CancellationToken.None);
        Assert.Equal(1, handler.RequestCount);

        // Evict
        resolver.Evict(aceUrl);

        // Next call must re-hit HTTP
        await resolver.ResolveAsync(aceUrl, CancellationToken.None);
        Assert.Equal(2, handler.RequestCount);
    }

    // ── Task 1.5 — Failure modes ─────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_EngineBaseUrlBlank_ReturnsNull()
    {
        var handler = new ScriptedHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(SuccessJson)));
        var resolver = ResolverWith(handler, settings: new EmptyBaseUrlEngineSettings());

        var result = await resolver.ResolveAsync("http://example.com/stream.acelive", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ResolveAsync_HttpRequestException_ReturnsNull()
    {
        var handler = new ScriptedHttpMessageHandler((_, _) =>
            throw new HttpRequestException("Engine down"));
        var resolver = ResolverWith(handler);

        var result = await resolver.ResolveAsync("http://example.com/stream.acelive", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_Http404_ReturnsNull()
    {
        var handler = new ScriptedHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse("{}", HttpStatusCode.NotFound)));
        var resolver = ResolverWith(handler);

        var result = await resolver.ResolveAsync("http://example.com/stream.acelive", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_MalformedJson_ReturnsNull()
    {
        var handler = new ScriptedHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse("not-json")));
        var resolver = ResolverWith(handler);

        var result = await resolver.ResolveAsync("http://example.com/stream.acelive", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_MissingInfohashInResponse_ReturnsNull()
    {
        var handler = new ScriptedHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse("""{"response":{}}""")));
        var resolver = ResolverWith(handler);

        var result = await resolver.ResolveAsync("http://example.com/stream.acelive", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_PerRequestTimeoutFires_ReturnsNull_DoesNotThrow()
    {
        // Handler stalls indefinitely; caller token is NOT cancelled.
        // The per-request timeout must fire, and ResolveAsync must return null without throwing.
        var handler = new ScriptedHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var resolver = ResolverWith(handler, perRequestTimeout: TimeSpan.FromMilliseconds(100));

        var result = await resolver.ResolveAsync("http://example.com/stream.acelive", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_CallerCancelled_PropagatesOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = new ScriptedHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(SuccessJson)));
        var resolver = ResolverWith(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => resolver.ResolveAsync("http://example.com/stream.acelive", cts.Token));
    }
}
