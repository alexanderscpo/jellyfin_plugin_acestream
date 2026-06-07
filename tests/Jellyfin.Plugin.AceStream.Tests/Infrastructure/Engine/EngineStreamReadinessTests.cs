using Jellyfin.Plugin.AceStream.Domain;
using Jellyfin.Plugin.AceStream.Infrastructure.Engine;
using Jellyfin.Plugin.AceStream.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AceStream.Tests.Infrastructure.Engine;

public class EngineStreamReadinessTests
{
    private const string Hash = "8c9febd01a731ce6139bca444d6aac9aaa764a88";
    private const string Base = "http://engine:6878";

    // The engine echoes stat/command URLs with its OWN view of the host (here a different host),
    // which the plugin may not be able to reach.
    private const string GetStreamJson =
        $$"""
        {"response":{"infohash":"{{Hash}}","stat_url":"http://10.0.0.9:6878/ace/stat/{{Hash}}/sess","command_url":"http://10.0.0.9:6878/ace/cmd/{{Hash}}/sess","is_live":1},"error":null}
        """;

    private sealed class FixedEngineSettings : IEngineSettings
    {
        public FixedEngineSettings(string baseUrl) => BaseUrl = baseUrl;

        public string BaseUrl { get; }
    }

    private static EngineStreamReadiness ReadinessOver(
        RoutingHttpMessageHandler handler,
        string baseUrl = Base)
    {
        var factory = new FakeHttpClientFactory(handler);
        return new EngineStreamReadiness(
            factory,
            new FixedEngineSettings(baseUrl),
            NullLogger<EngineStreamReadiness>.Instance,
            readinessTimeout: TimeSpan.FromMilliseconds(300),
            pollInterval: TimeSpan.FromMilliseconds(15));
    }

    private static string Stat(long downloaded, int peers) =>
        $$"""{"response":{"status":"dl","downloaded":{{downloaded}},"peers":{{peers}},"speed_down":1},"error":null}""";

    [Fact]
    public async Task IsReadyAsync_StreamDownloading_ReturnsTrue_AndStopsSession()
    {
        var handler = new RoutingHttpMessageHandler(uri =>
            uri.AbsolutePath.Contains("/ace/getstream", StringComparison.Ordinal) ? GetStreamJson
            : uri.AbsolutePath.Contains("/ace/stat/", StringComparison.Ordinal) ? Stat(downloaded: 8_000_000, peers: 2)
            : """{"response":null,"error":null}""");

        var ready = await ReadinessOver(handler).IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.True(ready);
        Assert.Contains(handler.Requests, u => u.AbsolutePath.Contains("/ace/cmd/", StringComparison.Ordinal)
            && u.Query.Contains("method=stop", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IsReadyAsync_NoData_ReturnsFalse()
    {
        var handler = new RoutingHttpMessageHandler(uri =>
            uri.AbsolutePath.Contains("/ace/getstream", StringComparison.Ordinal) ? GetStreamJson
            : uri.AbsolutePath.Contains("/ace/stat/", StringComparison.Ordinal) ? Stat(downloaded: 0, peers: 0)
            : """{"response":null,"error":null}""");

        var ready = await ReadinessOver(handler).IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.False(ready);
    }

    [Fact]
    public async Task IsReadyAsync_BecomesReadyAfterPolls_ReturnsTrue()
    {
        var statCalls = 0;
        var handler = new RoutingHttpMessageHandler(uri =>
        {
            if (uri.AbsolutePath.Contains("/ace/getstream", StringComparison.Ordinal))
            {
                return GetStreamJson;
            }

            if (uri.AbsolutePath.Contains("/ace/stat/", StringComparison.Ordinal))
            {
                statCalls++;
                return statCalls >= 2 ? Stat(downloaded: 4_000_000, peers: 1) : Stat(downloaded: 0, peers: 0);
            }

            return """{"response":null,"error":null}""";
        });

        var ready = await ReadinessOver(handler).IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.True(ready);
        Assert.True(statCalls >= 2);
    }

    [Fact]
    public async Task IsReadyAsync_TargetsConfiguredBaseUrl_NotEngineEchoedHost()
    {
        var handler = new RoutingHttpMessageHandler(uri =>
            uri.AbsolutePath.Contains("/ace/getstream", StringComparison.Ordinal) ? GetStreamJson
            : uri.AbsolutePath.Contains("/ace/stat/", StringComparison.Ordinal) ? Stat(downloaded: 8_000_000, peers: 2)
            : """{"response":null,"error":null}""");

        await ReadinessOver(handler).IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        // The stat call must go to the configured host (engine), not the echoed 10.0.0.9.
        var statRequest = handler.Requests.First(u => u.AbsolutePath.Contains("/ace/stat/", StringComparison.Ordinal));
        Assert.Equal("engine", statRequest.Host);
    }

    [Fact]
    public async Task IsReadyAsync_EngineNotConfigured_ReturnsTrueWithoutHttp()
    {
        var handler = new RoutingHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));

        var ready = await ReadinessOver(handler, baseUrl: string.Empty)
            .IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.True(ready);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task IsReadyAsync_GetStreamHasNoStatUrl_FailsOpenTrue()
    {
        var handler = new RoutingHttpMessageHandler(_ => """{"response":null,"error":"unknown infohash"}""");

        var ready = await ReadinessOver(handler).IsReadyAsync(Infohash.Create(Hash), CancellationToken.None);

        Assert.True(ready);
    }
}
