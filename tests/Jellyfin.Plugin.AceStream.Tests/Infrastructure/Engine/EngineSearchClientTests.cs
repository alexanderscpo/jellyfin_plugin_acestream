using System.Net;
using Jellyfin.Plugin.AceStream.Application;
using Jellyfin.Plugin.AceStream.Domain;
using Jellyfin.Plugin.AceStream.Infrastructure.Engine;
using Jellyfin.Plugin.AceStream.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AceStream.Tests.Infrastructure.Engine;

public class EngineSearchClientTests
{
    // Real /search?query=tv response captured from a live engine (v3.2.11), trimmed to 2 groups.
    private const string RealSearchJson =
        """
        {"result":{"total":276,"time":0.3059,"request_time":0.78,"results":[
          {"name":"TVO","items":[{"name":"TVO","infohash":"8c9febd01a731ce6139bca444d6aac9aaa764a88","categories":["tv"],"availability":1,"availability_updated_at":1780772402,"channel_id":21448,"status":2,"disabled":false,"countries":["de"],"languages":["deu"]}]},
          {"name":"TV1","items":[{"name":"TV1","infohash":"3c86e88beeb52233540afaf9febbb944d958cba6","categories":["tv"],"availability":1,"availability_updated_at":1780759262,"status":2,"disabled":false}]}
        ]}}
        """;

    private sealed class FakeEngineSettings : IEngineSettings
    {
        public string BaseUrl => "http://engine:6878";
    }

    private sealed class EmptyBaseUrlEngineSettings : IEngineSettings
    {
        public string BaseUrl => string.Empty;
    }

    private static EngineSearchClient ClientReturning(
        string json,
        out StubHttpMessageHandler handler,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        handler = new StubHttpMessageHandler(json, statusCode);
        var factory = new FakeHttpClientFactory(handler);
        return new EngineSearchClient(factory, new FakeEngineSettings(), NullLogger<EngineSearchClient>.Instance);
    }

    [Fact]
    public async Task SearchAsync_ParsesTotalAndFlattensChannels()
    {
        var client = ClientReturning(RealSearchJson, out _);

        var result = await client.SearchAsync(new SearchRequest { Query = "tv" }, CancellationToken.None);

        Assert.Equal(276, result.Total);
        Assert.Equal(2, result.Channels.Count);
    }

    [Fact]
    public async Task SearchAsync_MapsAllFieldsOfFirstChannel()
    {
        var client = ClientReturning(RealSearchJson, out _);

        var result = await client.SearchAsync(new SearchRequest { Query = "tv" }, CancellationToken.None);
        var channel = result.Channels[0];

        Assert.Equal("8c9febd01a731ce6139bca444d6aac9aaa764a88", channel.Infohash.Value);
        Assert.Equal("TVO", channel.Name);
        Assert.Equal(ChannelStatus.Working, channel.Status);
        Assert.Equal(1.0, channel.Availability.Value);
        Assert.Equal(21448, channel.ChannelId);
        Assert.Equal(new[] { "tv" }, channel.Categories);
        Assert.Equal(new[] { "de" }, channel.Countries);
        Assert.Equal(new[] { "deu" }, channel.Languages);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1780772402), channel.AvailabilityUpdatedAt);
    }

    [Fact]
    public async Task SearchAsync_HandlesOptionalFieldsAbsent()
    {
        var client = ClientReturning(RealSearchJson, out _);

        var result = await client.SearchAsync(new SearchRequest { Query = "tv" }, CancellationToken.None);
        var channel = result.Channels[1];

        Assert.Equal("TV1", channel.Name);
        Assert.Null(channel.ChannelId);
        Assert.Empty(channel.Countries);
        Assert.Empty(channel.Languages);
    }

    [Fact]
    public async Task SearchAsync_BuildsUrlWithParamsAndClampsPageSize()
    {
        var client = ClientReturning(RealSearchJson, out var handler);

        await client.SearchAsync(
            new SearchRequest { Query = "sport", Category = "tv", Page = 2, PageSize = 999 },
            CancellationToken.None);

        var url = handler.LastRequestUri!.ToString();

        Assert.Contains("/search", url, StringComparison.Ordinal);
        Assert.Contains("query=sport", url, StringComparison.Ordinal);
        Assert.Contains("category=tv", url, StringComparison.Ordinal);
        Assert.Contains("page=2", url, StringComparison.Ordinal);
        Assert.Contains("page_size=200", url, StringComparison.Ordinal); // clamped to engine max
    }

    [Fact]
    public async Task SearchAsync_SkipsMalformedItems()
    {
        const string json =
            """
            {"result":{"total":2,"results":[
              {"name":"Bad","items":[{"name":"Bad","infohash":"not-a-valid-hash","categories":["tv"],"availability":1,"status":2,"disabled":false}]},
              {"name":"Good","items":[{"name":"Good","infohash":"3c86e88beeb52233540afaf9febbb944d958cba6","categories":["tv"],"availability":1,"status":2,"disabled":false}]}
            ]}}
            """;
        var client = ClientReturning(json, out _);

        var result = await client.SearchAsync(new SearchRequest { Query = "x" }, CancellationToken.None);

        Assert.Single(result.Channels);
        Assert.Equal("Good", result.Channels[0].Name);
    }

    [Fact]
    public async Task SearchAsync_CreatesClientFromFactoryPerCall()
    {
        var handler = new StubHttpMessageHandler(RealSearchJson);
        var factory = new FakeHttpClientFactory(handler);
        var client = new EngineSearchClient(factory, new FakeEngineSettings(), NullLogger<EngineSearchClient>.Instance);

        await client.SearchAsync(new SearchRequest { Query = "tv" }, CancellationToken.None);
        await client.SearchAsync(new SearchRequest { Query = "tv" }, CancellationToken.None);

        // A fresh client per call lets IHttpClientFactory rotate the handler; capturing one would defeat it.
        Assert.Equal(2, factory.CreateClientCallCount);
        Assert.Equal(EngineSearchClient.HttpClientName, factory.LastRequestedName);
    }

    [Fact]
    public async Task SearchAsync_EngineReturns503_ReturnsEmptyResult()
    {
        var client = ClientReturning("{}", out _, HttpStatusCode.ServiceUnavailable);

        var result = await client.SearchAsync(new SearchRequest { Query = "tv" }, CancellationToken.None);

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Channels);
    }

    [Fact]
    public async Task SearchAsync_MalformedJsonBody_ReturnsEmptyResult()
    {
        var client = ClientReturning("not-json-at-all", out _);

        var result = await client.SearchAsync(new SearchRequest { Query = "tv" }, CancellationToken.None);

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Channels);
    }

    [Fact]
    public async Task SearchAsync_CallerCancels_ThrowsOperationCanceledException()
    {
        var client = ClientReturning(RealSearchJson, out _);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SearchAsync(new SearchRequest { Query = "tv" }, cts.Token));
    }

    [Fact]
    public async Task SearchAsync_NoBaseUrlConfigured_ReturnsEmptyResult()
    {
        // Arrange: settings with an empty base URL — same missing-config path as empty proxy URL
        // and empty M3U. Should degrade soft (empty result) rather than throwing.
        var handler = new StubHttpMessageHandler(RealSearchJson);
        var factory = new FakeHttpClientFactory(handler);
        var client = new EngineSearchClient(
            factory,
            new EmptyBaseUrlEngineSettings(),
            NullLogger<EngineSearchClient>.Instance);

        // Act
        var result = await client.SearchAsync(new SearchRequest { Query = "tv" }, CancellationToken.None);

        // Assert
        Assert.Equal(0, result.Total);
        Assert.Empty(result.Channels);
    }

    [Fact]
    public async Task SearchAsync_EngineHangs_ReturnsEmptyResult()
    {
        // Engine accepts the connection but never responds — caller token is NOT cancelled.
        // The per-request timeout must fire, and SearchAsync must degrade to an empty result
        // rather than letting the TaskCanceledException escape.
        var handler = new ScriptedHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        });
        var factory = new FakeHttpClientFactory(handler);
        var client = new EngineSearchClient(
            factory,
            new FakeEngineSettings(),
            NullLogger<EngineSearchClient>.Instance,
            searchTimeout: TimeSpan.FromMilliseconds(100));

        var result = await client.SearchAsync(new SearchRequest { Query = "tv" }, CancellationToken.None);

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Channels);
    }
}
