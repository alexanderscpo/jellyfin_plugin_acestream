using Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;

namespace Jellyfin.Plugin.AceStream.Tests.Infrastructure.Jellyfin;

public class M3uParseCacheTests
{
    private const string Hash1 = "8c9febd01a731ce6139bca444d6aac9aaa764a88";

    private static string SingleChannelPlaylist(string name = "TVO") =>
        $"#EXTM3U\n#EXTINF:-1,{name}\nacestream://{Hash1}";

    [Fact]
    public void GetOrParse_SamePlaylistString_DoesNotReparseSecondTime()
    {
        int parseCalls = 0;
        ParsedPlaylist CountingParser(string? content)
        {
            parseCalls++;
            return M3uPlaylistParser.Parse(content);
        }

        var cache = new M3uParseCache(CountingParser);
        var playlist = SingleChannelPlaylist();

        cache.GetOrParse(playlist);
        cache.GetOrParse(playlist);

        Assert.Equal(1, parseCalls);
    }

    [Fact]
    public void GetOrParse_DifferentPlaylistContent_ReparsesBoth()
    {
        int parseCalls = 0;
        ParsedPlaylist CountingParser(string? content)
        {
            parseCalls++;
            return M3uPlaylistParser.Parse(content);
        }

        var cache = new M3uParseCache(CountingParser);

        cache.GetOrParse(SingleChannelPlaylist("TVO"));
        cache.GetOrParse(SingleChannelPlaylist("DAZN")); // different content

        Assert.Equal(2, parseCalls);
    }

    [Fact]
    public void GetOrParse_ChangedPlaylist_InvalidatesCacheAndReparses()
    {
        int parseCalls = 0;
        ParsedPlaylist CountingParser(string? content)
        {
            parseCalls++;
            return M3uPlaylistParser.Parse(content);
        }

        var cache = new M3uParseCache(CountingParser);
        var first = SingleChannelPlaylist("TVO");
        var second = SingleChannelPlaylist("DAZN");

        cache.GetOrParse(first);
        cache.GetOrParse(first); // still cached
        cache.GetOrParse(second); // new content -> re-parse

        Assert.Equal(2, parseCalls);
    }

    [Fact]
    public void GetOrParse_NullOrEmpty_ReturnsEmpty()
    {
        var cache = new M3uParseCache(M3uPlaylistParser.Parse);

        var nullResult = cache.GetOrParse(null);
        var emptyResult = cache.GetOrParse(string.Empty);

        Assert.Empty(nullResult.Channels);
        Assert.Empty(nullResult.AceLive);
        Assert.Empty(emptyResult.Channels);
        Assert.Empty(emptyResult.AceLive);
    }

    [Fact]
    public void GetOrParse_SameInstance_ReturnsSameObject()
    {
        var cache = new M3uParseCache(M3uPlaylistParser.Parse);
        var playlist = SingleChannelPlaylist();

        var first = cache.GetOrParse(playlist);
        var second = cache.GetOrParse(playlist);

        Assert.Same(first, second);
    }

    // ── Task 2.5 — ParsedPlaylist caching (acelive entries cached alongside channels) ──

    [Fact]
    public void GetOrParse_PlaylistWithAceLiveEntry_ReturnsBothChannelsAndAceLive()
    {
        var playlist = $"#EXTM3U\n#EXTINF:-1,TVO\nacestream://{Hash1}\n#EXTINF:-1,Live\nhttp://host/stream.acelive";
        var cache = new M3uParseCache(M3uPlaylistParser.Parse);

        var result = cache.GetOrParse(playlist);

        Assert.Single(result.Channels);
        Assert.Single(result.AceLive);
        Assert.Equal("Live", result.AceLive[0].Name);
    }

    [Fact]
    public void GetOrParse_AceLivePlaylist_CachesOnSecondCall()
    {
        int parseCalls = 0;
        ParsedPlaylist CountingParser(string? content)
        {
            parseCalls++;
            return M3uPlaylistParser.Parse(content);
        }

        var cache = new M3uParseCache(CountingParser);
        var playlist = "http://host/stream.acelive";

        cache.GetOrParse(playlist);
        cache.GetOrParse(playlist);

        Assert.Equal(1, parseCalls);
    }
}
