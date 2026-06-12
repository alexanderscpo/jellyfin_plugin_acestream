using System.Net;
using System.Text;
using Jellyfin.Plugin.AceStream.Domain;
using Jellyfin.Plugin.AceStream.Infrastructure.Proxy;
using Jellyfin.Plugin.AceStream.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AceStream.Tests.Infrastructure.Proxy;

public class ProxyStreamReadinessTests
{
    private const string Hash = "8c9febd01a731ce6139bca444d6aac9aaa764a88";

    private sealed class FixedProxySettings : IProxySettings
    {
        public FixedProxySettings(string baseUrl) => BaseUrl = baseUrl;

        public string BaseUrl { get; }
    }

    private static HttpResponseMessage Ok(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/octet-stream") };

    private static ProxyStreamReadiness ReadinessOver(ScriptedHttpMessageHandler handler, string baseUrl = "http://acexy:8080")
        => new(
            new FakeHttpClientFactory(handler),
            new FixedProxySettings(baseUrl),
            NullLogger<ProxyStreamReadiness>.Instance,
            readinessTimeout: TimeSpan.FromMilliseconds(200));

    [Fact]
    public async Task IsReadyAsync_BytesFlow_ReturnsTrue_AndRequestsTheProxyStreamUrl()
    {
        var handler = new ScriptedHttpMessageHandler((req, _) =>
        {
            Assert.Equal($"http://acexy:8080/ace/getstream?infohash={Hash}", req.RequestUri!.ToString());
            return Task.FromResult(Ok("MPEG-TS-bytes"));
        });

        var ready = await ReadinessOver(handler).IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.True(ready);
    }

    [Fact]
    public async Task IsReadyAsync_ConnectionHangsWithNoData_ReturnsFalse()
    {
        // Dead channel: acexy holds the connection open and never sends headers/data.
        var handler = new ScriptedHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Ok(string.Empty);
        });

        var ready = await ReadinessOver(handler).IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.False(ready);
    }

    [Fact]
    public async Task IsReadyAsync_EmptyBody_ReturnsFalse()
    {
        // Connected but no bytes arrive before the stream ends.
        var handler = new ScriptedHttpMessageHandler((_, _) => Task.FromResult(Ok(string.Empty)));

        var ready = await ReadinessOver(handler).IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.False(ready);
    }

    [Fact]
    public async Task IsReadyAsync_NonSuccessStatus_ReturnsFalse()
    {
        var handler = new ScriptedHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var ready = await ReadinessOver(handler).IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.False(ready);
    }

    [Fact]
    public async Task IsReadyAsync_ProxyNotConfigured_ReturnsTrueWithoutHttp()
    {
        var handler = new ScriptedHttpMessageHandler((_, _) => throw new InvalidOperationException("should not be called"));

        var ready = await ReadinessOver(handler, baseUrl: string.Empty)
            .IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.True(ready);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task IsReadyAsync_ProxyUnreachable_FailsOpenTrue()
    {
        var handler = new ScriptedHttpMessageHandler((_, _) => throw new HttpRequestException("connection refused"));

        var ready = await ReadinessOver(handler).IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.True(ready);
    }

    [Fact]
    public async Task IsReadyAsync_CallerCancels_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = new ScriptedHttpMessageHandler((_, _) => Task.FromResult(Ok("bytes")));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ReadinessOver(handler).IsReadyAsync(Infohash.Create(Hash), cts.Token));
    }
}
