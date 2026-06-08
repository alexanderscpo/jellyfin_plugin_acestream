using Jellyfin.Plugin.AceStream.Application;
using Jellyfin.Plugin.AceStream.Domain;
using Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;
using Jellyfin.Plugin.AceStream.Infrastructure.Proxy;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.AceStream.Tests.Infrastructure.Jellyfin;

public class AceStreamChannelTests
{
    private const string Hash = "8c9febd01a731ce6139bca444d6aac9aaa764a88";
    private const string Hash2 = "8a25653a2f774f4ae1062d38a30dcd714d304a3a";

    private sealed class FakeCustomChannelRepository : ICustomChannelRepository
    {
        private readonly IReadOnlyList<CustomChannel> _channels;

        public FakeCustomChannelRepository(params CustomChannel[] channels) => _channels = channels;

        public IReadOnlyList<CustomChannel> GetAll() => _channels;
    }

    private sealed class FakeSearchPort : ISearchPort
    {
        private readonly SearchResult _result;

        public FakeSearchPort(SearchResult result) => _result = result;

        public SearchRequest? LastRequest { get; private set; }

        public Task<SearchResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeProxySettings : IProxySettings
    {
        public FakeProxySettings(string baseUrl, int probeAnalyzeDurationMs = 5000)
        {
            BaseUrl = baseUrl;
            ProbeAnalyzeDurationMs = probeAnalyzeDurationMs;
        }

        public string BaseUrl { get; }

        public int ProbeAnalyzeDurationMs { get; }
    }

    private sealed class FakeMediaSourceProbe : IMediaSourceProbe
    {
        private readonly string? _videoCodec;

        public FakeMediaSourceProbe(string? videoCodec = null) => _videoCodec = videoCodec;

        public MediaSourceInfo? Probed { get; private set; }

        public Task EnrichAsync(MediaSourceInfo source, CancellationToken cancellationToken)
        {
            Probed = source;
            if (_videoCodec is not null)
            {
                source.MediaStreams = new List<MediaStream>
                {
                    new() { Type = MediaStreamType.Video, Codec = _videoCodec, Index = 0 },
                };
            }

            return Task.CompletedTask;
        }
    }

    private static AceChannel SampleChannel() => new(
        Infohash.Create(Hash),
        "TVO",
        ChannelStatus.Working,
        Availability.Create(1.0),
        new[] { "tv" },
        disabled: false);

    private static AceStreamChannel Channel(
        SearchResult? searchResult = null,
        string proxyUrl = "http://acexy:8080",
        FakeSearchPort? port = null,
        IMediaSourceProbe? probe = null,
        int probeAnalyzeDurationMs = 5000,
        ICustomChannelRepository? customChannels = null)
    {
        var searchPort = port ?? new FakeSearchPort(searchResult ?? new SearchResult(0, Array.Empty<AceChannel>()));
        var proxySettings = new FakeProxySettings(proxyUrl, probeAnalyzeDurationMs);
        return new AceStreamChannel(searchPort, proxySettings, probe ?? new FakeMediaSourceProbe(), customChannels ?? new FakeCustomChannelRepository());
    }

    [Fact]
    public async Task GetChannelItemMediaInfo_EnrichesSourceWithProbedStreams()
    {
        var probe = new FakeMediaSourceProbe(videoCodec: "h264");
        var channel = Channel(probe: probe);

        var sources = (await channel.GetChannelItemMediaInfo(Hash, CancellationToken.None)).ToList();

        // The channel probes the proxy source so Jellyfin sees the real codecs.
        Assert.Equal($"http://acexy:8080/ace/getstream?infohash={Hash}", probe.Probed!.Path);
        var video = Assert.Single(sources[0].MediaStreams);
        Assert.Equal("h264", video.Codec);
        Assert.Equal(MediaStreamType.Video, video.Type);
    }

    [Fact]
    public async Task GetChannelItemMediaInfo_AppliesConfiguredProbeAnalyzeDuration()
    {
        var channel = Channel(probeAnalyzeDurationMs: 3000);

        var sources = (await channel.GetChannelItemMediaInfo(Hash, CancellationToken.None)).ToList();

        Assert.Equal(3000, sources[0].AnalyzeDurationMs);
    }

    [Fact]
    public async Task Root_ReturnsCategoryFolders()
    {
        var result = await Channel().GetChannelItems(new InternalChannelItemQuery(), CancellationToken.None);

        Assert.Equal(AceCategories.Browseable.Count, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(ChannelItemType.Folder, item.Type));
        Assert.All(result.Items, item => Assert.StartsWith("category:", item.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CategoryFolder_ListsMappedChannels_AndForwardsCategory()
    {
        var port = new FakeSearchPort(new SearchResult(523, new[] { SampleChannel() }));
        var channel = Channel(port: port);

        var result = await channel.GetChannelItems(
            new InternalChannelItemQuery { FolderId = "category:tv" }, CancellationToken.None);

        Assert.Equal("tv", port.LastRequest!.Category);
        Assert.Equal(523, result.TotalRecordCount);
        Assert.Single(result.Items);
        Assert.Equal(Hash, result.Items[0].Id);
    }

    [Fact]
    public async Task CategoryFolder_ConvertsStartIndexAndLimitToPage()
    {
        var port = new FakeSearchPort(new SearchResult(523, Array.Empty<AceChannel>()));
        var channel = Channel(port: port);

        await channel.GetChannelItems(
            new InternalChannelItemQuery { FolderId = "category:tv", StartIndex = 100, Limit = 50 },
            CancellationToken.None);

        Assert.Equal(2, port.LastRequest!.Page);
        Assert.Equal(50, port.LastRequest.PageSize);
    }

    [Fact]
    public async Task GetChannelItemMediaInfo_ReturnsProxySourceForInfohash()
    {
        var channel = Channel(proxyUrl: "http://acexy:8080");

        var sources = (await channel.GetChannelItemMediaInfo(Hash, CancellationToken.None)).ToList();

        Assert.Single(sources);
        Assert.Equal($"http://acexy:8080/ace/getstream?infohash={Hash}", sources[0].Path);
    }

    [Fact]
    public async Task GetChannelItemMediaInfo_CategoryId_ReturnsEmpty()
    {
        var channel = Channel();

        var sources = await channel.GetChannelItemMediaInfo("category:tv", CancellationToken.None);

        Assert.Empty(sources);
    }

    [Fact]
    public async Task GetChannelItemMediaInfo_NoProxyConfigured_ReturnsEmpty()
    {
        var channel = Channel(proxyUrl: string.Empty);

        var sources = await channel.GetChannelItemMediaInfo(Hash, CancellationToken.None);

        Assert.Empty(sources);
    }

    [Fact]
    public async Task GetChannelItemMediaInfo_NullId_ReturnsEmpty()
    {
        var channel = Channel();

        var sources = await channel.GetChannelItemMediaInfo(null!, CancellationToken.None);

        Assert.Empty(sources);
    }

    [Fact]
    public async Task GetChannelItemMediaInfo_WhitespaceId_ReturnsEmpty()
    {
        var channel = Channel();

        var sources = await channel.GetChannelItemMediaInfo("   ", CancellationToken.None);

        Assert.Empty(sources);
    }

    [Fact]
    public async Task Root_WithNoCustomChannels_OmitsCustomFolder()
    {
        var result = await Channel().GetChannelItems(new InternalChannelItemQuery(), CancellationToken.None);

        Assert.DoesNotContain(result.Items, item => item.Id == "custom");
        Assert.Equal(AceCategories.Browseable.Count, result.Items.Count);
    }

    [Fact]
    public async Task Root_WithCustomChannels_IncludesCustomFolder()
    {
        var custom = new FakeCustomChannelRepository(new CustomChannel("TVO", Infohash.Create(Hash)));
        var result = await Channel(customChannels: custom).GetChannelItems(new InternalChannelItemQuery(), CancellationToken.None);

        var folder = Assert.Single(result.Items, item => item.Id == "custom");
        Assert.Equal(ChannelItemType.Folder, folder.Type);
        Assert.Equal(AceCategories.Browseable.Count + 1, result.Items.Count);
    }

    [Fact]
    public async Task CustomFolder_ReturnsConfiguredChannels()
    {
        var custom = new FakeCustomChannelRepository(
            new CustomChannel("TVO", Infohash.Create(Hash)),
            new CustomChannel("DAZN LaLiga", Infohash.Create(Hash2)));
        var channel = Channel(customChannels: custom);

        var result = await channel.GetChannelItems(
            new InternalChannelItemQuery { FolderId = "custom" }, CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(Hash, result.Items[0].Id);
        Assert.Equal("TVO", result.Items[0].Name);
        Assert.Equal(Hash2, result.Items[1].Id);
        Assert.Equal("DAZN LaLiga", result.Items[1].Name);
    }

    [Fact]
    public async Task CustomFolder_ItemsAreLiveMedia()
    {
        var custom = new FakeCustomChannelRepository(new CustomChannel("TVO", Infohash.Create(Hash)));
        var channel = Channel(customChannels: custom);

        var result = await channel.GetChannelItems(
            new InternalChannelItemQuery { FolderId = "custom" }, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(ChannelItemType.Media, item.Type);
        Assert.True(item.IsLiveStream);
    }

    [Fact]
    public async Task CustomFolder_CustomChannelPlayback_ReturnsProxySource()
    {
        var custom = new FakeCustomChannelRepository(new CustomChannel("TVO", Infohash.Create(Hash)));
        var channel = Channel(proxyUrl: "http://acexy:8080", customChannels: custom);

        var sources = (await channel.GetChannelItemMediaInfo(Hash, CancellationToken.None)).ToList();

        Assert.Single(sources);
        Assert.Equal($"http://acexy:8080/ace/getstream?infohash={Hash}", sources[0].Path);
    }
}
