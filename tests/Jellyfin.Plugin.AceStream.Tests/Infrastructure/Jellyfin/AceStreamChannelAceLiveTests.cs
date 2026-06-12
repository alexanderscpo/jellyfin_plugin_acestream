using Jellyfin.Plugin.AceStream.Application;
using Jellyfin.Plugin.AceStream.Domain;
using Jellyfin.Plugin.AceStream.Infrastructure.Engine;
using Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;
using Jellyfin.Plugin.AceStream.Infrastructure.Proxy;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.AceStream.Tests.Infrastructure.Jellyfin;

/// <summary>
/// Tests for AceLive (.acelive URL) entry handling in AceStreamChannel:
/// item Id scheme, DataVersion contributions, GetChannelItemMediaInfo routing,
/// evict-retry on empty MediaStreams, and fail-soft on null resolver result.
/// </summary>
public class AceStreamChannelAceLiveTests
{
    private const string Hash = "8c9febd01a731ce6139bca444d6aac9aaa764a88";
    private const string AceLiveUrl = "http://example.com/stream.acelive";
    private const string AceLiveName = "Live Channel";

    // ── Test doubles ─────────────────────────────────────────────────────────

    private sealed class FakeCustomChannelRepository : ICustomChannelRepository
    {
        private readonly IReadOnlyList<CustomChannel> _channels;

        public FakeCustomChannelRepository(params CustomChannel[] channels) => _channels = channels;

        public IReadOnlyList<CustomChannel> GetAll() => _channels;
    }

    private sealed class FakeAceLiveEntrySource : IAceLiveEntrySource
    {
        private readonly IReadOnlyList<AceLiveEntry> _entries;

        public FakeAceLiveEntrySource(params AceLiveEntry[] entries) => _entries = entries;

        public IReadOnlyList<AceLiveEntry> GetAceLiveEntries() => _entries;
    }

    private sealed class FakeSearchPort : ISearchPort
    {
        public Task<SearchResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new SearchResult(0, Array.Empty<AceChannel>()));
    }

    private sealed class FakeProxySettings : IProxySettings
    {
        public FakeProxySettings(string baseUrl) => BaseUrl = baseUrl;

        public string BaseUrl { get; }
    }

    private sealed class FakeProbeSettings : IProbeSettings
    {
        public int ProbeAnalyzeDurationMs => 5000;

        public TimeSpan CodecCacheTtl => TimeSpan.FromMinutes(5);
    }

    private sealed class FakeMediaSourceProbe : IMediaSourceProbe
    {
        private readonly bool _populateStreams;
        public int EnrichCallCount { get; private set; }

        public FakeMediaSourceProbe(bool populateStreams = false) => _populateStreams = populateStreams;

        public Task EnrichAsync(MediaSourceInfo source, CancellationToken cancellationToken)
        {
            EnrichCallCount++;
            if (_populateStreams)
            {
                source.MediaStreams = new List<MediaStream>
                {
                    new() { Type = MediaStreamType.Video, Codec = "h264", Index = 0 },
                };
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeAceLiveResolver : IAceLiveResolver
    {
        private readonly Infohash? _infohash;
        public int ResolveCallCount { get; private set; }
        public int EvictCallCount { get; private set; }
        public string? LastEvictedUrl { get; private set; }

        public FakeAceLiveResolver(Infohash? infohash = null) => _infohash = infohash;

        public Task<Infohash?> ResolveAsync(string url, CancellationToken cancellationToken)
        {
            ResolveCallCount++;
            return Task.FromResult(_infohash);
        }

        public void Evict(string url)
        {
            EvictCallCount++;
            LastEvictedUrl = url;
        }
    }

    private static AceStreamChannel Channel(
        IAceLiveEntrySource? aceLiveSource = null,
        ICustomChannelRepository? customChannels = null,
        IAceLiveResolver? resolver = null,
        IMediaSourceProbe? probe = null,
        string proxyUrl = "http://acexy:8080")
    {
        return new AceStreamChannel(
            new FakeSearchPort(),
            new FakeProxySettings(proxyUrl),
            new FakeProbeSettings(),
            probe ?? new FakeMediaSourceProbe(),
            customChannels ?? new FakeCustomChannelRepository(),
            aceLiveSource ?? new FakeAceLiveEntrySource(),
            resolver ?? new FakeAceLiveResolver(),
            NullLogger<AceStreamChannel>.Instance);
    }

    // ── Task 2.9 — Item Id roundtrip + DataVersion ───────────────────────────

    [Fact]
    public async Task CustomFolder_AceLiveEntry_HasAceLivePrefixedId()
    {
        var source = new FakeAceLiveEntrySource(new AceLiveEntry(AceLiveName, AceLiveUrl));
        var channel = Channel(aceLiveSource: source);

        var result = await channel.GetChannelItems(
            new InternalChannelItemQuery { FolderId = "custom" }, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.StartsWith("acelive:", item.Id, StringComparison.Ordinal);
        Assert.Equal(AceLiveName, item.Name);
    }

    [Fact]
    public async Task CustomFolder_AceLiveEntry_IdIsStable()
    {
        var entry = new AceLiveEntry(AceLiveName, AceLiveUrl);
        var source = new FakeAceLiveEntrySource(entry);

        var ch1 = Channel(aceLiveSource: source);
        var ch2 = Channel(aceLiveSource: source);

        var r1 = await ch1.GetChannelItems(new InternalChannelItemQuery { FolderId = "custom" }, CancellationToken.None);
        var r2 = await ch2.GetChannelItems(new InternalChannelItemQuery { FolderId = "custom" }, CancellationToken.None);

        Assert.Equal(r1.Items[0].Id, r2.Items[0].Id);
    }

    [Fact]
    public async Task CustomFolder_AceLiveEntry_IsLiveStream()
    {
        var source = new FakeAceLiveEntrySource(new AceLiveEntry(AceLiveName, AceLiveUrl));
        var channel = Channel(aceLiveSource: source);

        var result = await channel.GetChannelItems(
            new InternalChannelItemQuery { FolderId = "custom" }, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(ChannelItemType.Media, item.Type);
        Assert.True(item.IsLiveStream);
    }

    [Fact]
    public void DataVersion_IncludesAceLiveEntries()
    {
        var withAceLive = Channel(
            aceLiveSource: new FakeAceLiveEntrySource(new AceLiveEntry(AceLiveName, AceLiveUrl)));
        var withoutAceLive = Channel(
            aceLiveSource: new FakeAceLiveEntrySource());

        Assert.NotEqual(withoutAceLive.DataVersion, withAceLive.DataVersion);
    }

    [Fact]
    public void DataVersion_IsStable_ForSameAceLiveEntries()
    {
        var entry = new AceLiveEntry(AceLiveName, AceLiveUrl);

        var a = Channel(aceLiveSource: new FakeAceLiveEntrySource(entry)).DataVersion;
        var b = Channel(aceLiveSource: new FakeAceLiveEntrySource(entry)).DataVersion;

        Assert.Equal(a, b);
    }

    [Fact]
    public void DataVersion_Changes_WhenAceLiveEntryNameChanges()
    {
        var before = Channel(aceLiveSource: new FakeAceLiveEntrySource(new AceLiveEntry("Old", AceLiveUrl))).DataVersion;
        var after = Channel(aceLiveSource: new FakeAceLiveEntrySource(new AceLiveEntry("New", AceLiveUrl))).DataVersion;

        Assert.NotEqual(before, after);
    }

    [Fact]
    public async Task Root_WithAceLiveEntries_IncludesCustomFolder()
    {
        var source = new FakeAceLiveEntrySource(new AceLiveEntry(AceLiveName, AceLiveUrl));
        var channel = Channel(aceLiveSource: source);

        var result = await channel.GetChannelItems(new InternalChannelItemQuery(), CancellationToken.None);

        Assert.Contains(result.Items, item => item.Id == "custom");
    }

    // ── Task 2.10 — Acelive Id routing in GetChannelItemMediaInfo ────────────

    [Fact]
    public async Task GetChannelItemMediaInfo_AceLiveId_ResolvesAndReturnsProxySource()
    {
        var infohash = Infohash.Create(Hash);
        var resolver = new FakeAceLiveResolver(infohash);
        var entry = new AceLiveEntry(AceLiveName, AceLiveUrl);
        var source = new FakeAceLiveEntrySource(entry);
        // populateStreams: true so the probe reports actual streams → no evict-retry fires.
        var probe = new FakeMediaSourceProbe(populateStreams: true);
        var channel = Channel(aceLiveSource: source, resolver: resolver, probe: probe);

        // Get the item id from BuildCustomItems
        var items = await channel.GetChannelItems(
            new InternalChannelItemQuery { FolderId = "custom" }, CancellationToken.None);
        var itemId = items.Items[0].Id;

        var mediaSources = (await channel.GetChannelItemMediaInfo(itemId, CancellationToken.None)).ToList();

        Assert.Single(mediaSources);
        Assert.Equal($"http://acexy:8080/ace/getstream?infohash={Hash}", mediaSources[0].Path);
        Assert.Equal(1, resolver.ResolveCallCount);
    }

    [Fact]
    public async Task GetChannelItemMediaInfo_AceLiveId_NullResolution_ReturnsEmpty()
    {
        var resolver = new FakeAceLiveResolver(infohash: null);
        var entry = new AceLiveEntry(AceLiveName, AceLiveUrl);
        var source = new FakeAceLiveEntrySource(entry);
        var channel = Channel(aceLiveSource: source, resolver: resolver);

        var items = await channel.GetChannelItems(
            new InternalChannelItemQuery { FolderId = "custom" }, CancellationToken.None);
        var itemId = items.Items[0].Id;

        var mediaSources = await channel.GetChannelItemMediaInfo(itemId, CancellationToken.None);

        Assert.Empty(mediaSources);
    }

    [Fact]
    public async Task GetChannelItemMediaInfo_AceLiveId_EmptyMediaStreams_EvoidsAndReResolvesOnce()
    {
        // Probe returns empty MediaStreams on first enrich; resolver always returns the same hash.
        // The channel should evict + re-resolve exactly once.
        var infohash = Infohash.Create(Hash);
        var resolver = new FakeAceLiveResolver(infohash);
        var probe = new FakeMediaSourceProbe(populateStreams: false); // always empty
        var entry = new AceLiveEntry(AceLiveName, AceLiveUrl);
        var source = new FakeAceLiveEntrySource(entry);
        var channel = Channel(aceLiveSource: source, resolver: resolver, probe: probe);

        var items = await channel.GetChannelItems(
            new InternalChannelItemQuery { FolderId = "custom" }, CancellationToken.None);
        var itemId = items.Items[0].Id;

        // Even though both enriches return empty streams, the result should not be empty
        // (we return what we have after one re-resolve attempt, per spec).
        await channel.GetChannelItemMediaInfo(itemId, CancellationToken.None);

        Assert.Equal(1, resolver.EvictCallCount);
        Assert.Equal(AceLiveUrl, resolver.LastEvictedUrl);
        Assert.Equal(2, resolver.ResolveCallCount); // first + one re-resolve
        Assert.Equal(2, probe.EnrichCallCount);     // first + one re-enrich
    }

    [Fact]
    public async Task GetChannelItemMediaInfo_AceLiveId_EvictRetry_StillEmptyAfterRetry_ReturnsEmpty()
    {
        // Re-resolution returns null the second time → degrade to empty.
        var infohash = Infohash.Create(Hash);
        var resolver = new SequencedResolver(infohash, null);
        var probe = new FakeMediaSourceProbe(populateStreams: false);
        var entry = new AceLiveEntry(AceLiveName, AceLiveUrl);
        var source = new FakeAceLiveEntrySource(entry);
        var channel = Channel(aceLiveSource: source, resolver: resolver, probe: probe);

        var items = await channel.GetChannelItems(
            new InternalChannelItemQuery { FolderId = "custom" }, CancellationToken.None);
        var itemId = items.Items[0].Id;

        var mediaSources = await channel.GetChannelItemMediaInfo(itemId, CancellationToken.None);

        Assert.Empty(mediaSources);
    }

    // Resolver that returns a sequence of results per call.
    private sealed class SequencedResolver : IAceLiveResolver
    {
        private readonly Queue<Infohash?> _sequence;

        public SequencedResolver(params Infohash?[] sequence)
            => _sequence = new Queue<Infohash?>(sequence);

        public int EvictCallCount { get; private set; }

        public Task<Infohash?> ResolveAsync(string url, CancellationToken cancellationToken)
            => Task.FromResult(_sequence.Count > 0 ? _sequence.Dequeue() : null);

        public void Evict(string url) => EvictCallCount++;
    }
}
