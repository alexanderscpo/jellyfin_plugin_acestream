using Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;

namespace Jellyfin.Plugin.AceStream.Tests.Infrastructure.Jellyfin;

public class M3uPlaylistParserTests
{
    private const string Hash1 = "8c9febd01a731ce6139bca444d6aac9aaa764a88";
    private const string Hash2 = "8a25653a2f774f4ae1062d38a30dcd714d304a3a";

    // ── Task 2.1 — AceLive line recognition ──────────────────────────────────

    [Fact]
    public void Parse_AceLiveLine_WithExtInf_ReturnsAceLiveEntry()
    {
        var m3u = "#EXTM3U\n#EXTINF:-1,My Channel\nhttp://example.com/stream.acelive";

        var result = M3uPlaylistParser.Parse(m3u);

        Assert.Empty(result.Channels);
        var entry = Assert.Single(result.AceLive);
        Assert.Equal("My Channel", entry.Name);
        Assert.Equal("http://example.com/stream.acelive", entry.Url);
    }

    [Fact]
    public void Parse_AceLiveLine_WithoutExtInf_DerivesNameFromUrl()
    {
        var m3u = "https://cdn.example.com/feeds/sport.acelive";

        var result = M3uPlaylistParser.Parse(m3u);

        Assert.Empty(result.Channels);
        var entry = Assert.Single(result.AceLive);
        Assert.False(string.IsNullOrEmpty(entry.Name));
        Assert.Equal("https://cdn.example.com/feeds/sport.acelive", entry.Url);
    }

    [Fact]
    public void Parse_AceLiveExtension_CaseInsensitive()
    {
        var m3u = "http://host/stream.ACELIVE";

        var result = M3uPlaylistParser.Parse(m3u);

        var entry = Assert.Single(result.AceLive);
        Assert.Equal("http://host/stream.ACELIVE", entry.Url);
    }

    [Fact]
    public void Parse_MixedPlaylist_ExactlyOneEachKind()
    {
        var m3u = $"#EXTM3U\n#EXTINF:-1,TVO\nacestream://{Hash1}\n#EXTINF:-1,Live\nhttp://host/stream.acelive\nhttp://example.com/video.mp4";

        var result = M3uPlaylistParser.Parse(m3u);

        Assert.Single(result.Channels);
        Assert.Equal(Hash1, result.Channels[0].Infohash.Value);
        Assert.Single(result.AceLive);
        Assert.Equal("Live", result.AceLive[0].Name);
    }

    [Fact]
    public void Parse_NullContent_ReturnsEmpty()
    {
        var result = M3uPlaylistParser.Parse(null);
        Assert.Empty(result.Channels);
        Assert.Empty(result.AceLive);
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsEmpty()
    {
        var result = M3uPlaylistParser.Parse(string.Empty);
        Assert.Empty(result.Channels);
        Assert.Empty(result.AceLive);
    }

    [Fact]
    public void Parse_WhitespaceContent_ReturnsEmpty()
    {
        var result = M3uPlaylistParser.Parse("   \n   \n   ");
        Assert.Empty(result.Channels);
        Assert.Empty(result.AceLive);
    }

    [Fact]
    public void Parse_StandardExtInfWithAcestreamUrl_ReturnsChannel()
    {
        var m3u = $"#EXTM3U\n#EXTINF:-1,TVO\nacestream://{Hash1}";

        var result = M3uPlaylistParser.Parse(m3u);

        var channel = Assert.Single(result.Channels);
        Assert.Equal("TVO", channel.Name);
        Assert.Equal(Hash1, channel.Infohash.Value);
    }

    [Fact]
    public void Parse_MultipleChannels_ReturnsAll()
    {
        var m3u = $"#EXTM3U\n#EXTINF:-1,TVO\nacestream://{Hash1}\n#EXTINF:-1,DAZN LaLiga\nacestream://{Hash2}";

        var result = M3uPlaylistParser.Parse(m3u);

        Assert.Equal(2, result.Channels.Count);
        Assert.Equal("TVO", result.Channels[0].Name);
        Assert.Equal("DAZN LaLiga", result.Channels[1].Name);
    }

    [Fact]
    public void Parse_ExtInfWithAttributes_UsesDisplayName()
    {
        var m3u = $"""
            #EXTM3U
            #EXTINF:-1 tvg-id="" tvg-name="TVO" tvg-logo="" group-title="TV",TVO display
            acestream://{Hash1}
            """;

        var result = M3uPlaylistParser.Parse(m3u);

        var channel = Assert.Single(result.Channels);
        Assert.Equal("TVO display", channel.Name);
    }

    [Fact]
    public void Parse_ExtInfWithNoDisplayName_UsesHashAsName()
    {
        var m3u = $"#EXTM3U\n#EXTINF:-1\nacestream://{Hash1}";

        var result = M3uPlaylistParser.Parse(m3u);

        var channel = Assert.Single(result.Channels);
        Assert.Equal(Hash1, channel.Name);
    }

    [Fact]
    public void Parse_AcestreamUrlWithoutExtInf_UsesHashAsName()
    {
        var m3u = $"#EXTM3U\nacestream://{Hash1}";

        var result = M3uPlaylistParser.Parse(m3u);

        var channel = Assert.Single(result.Channels);
        Assert.Equal(Hash1, channel.Name);
    }

    [Fact]
    public void Parse_NonAcestreamUrl_IsSkipped()
    {
        var m3u = $"#EXTM3U\n#EXTINF:-1,HTTP Channel\nhttp://example.com/stream.m3u8\n#EXTINF:-1,TVO\nacestream://{Hash1}";

        var result = M3uPlaylistParser.Parse(m3u);

        var channel = Assert.Single(result.Channels);
        Assert.Equal("TVO", channel.Name);
    }

    [Fact]
    public void Parse_InvalidHash_IsSkipped()
    {
        var m3u = $"#EXTM3U\n#EXTINF:-1,Bad\nacestream://notahash\n#EXTINF:-1,TVO\nacestream://{Hash1}";

        var result = M3uPlaylistParser.Parse(m3u);

        var channel = Assert.Single(result.Channels);
        Assert.Equal("TVO", channel.Name);
    }

    [Fact]
    public void Parse_AcestreamUrlIsUpperCase_NormalizesHash()
    {
        var m3u = $"#EXTM3U\n#EXTINF:-1,TVO\nacestream://{Hash1.ToUpperInvariant()}";

        var result = M3uPlaylistParser.Parse(m3u);

        var channel = Assert.Single(result.Channels);
        Assert.Equal(Hash1, channel.Infohash.Value);
    }

    [Fact]
    public void Parse_WindowsLineEndings_ParsesCorrectly()
    {
        var m3u = $"#EXTM3U\r\n#EXTINF:-1,TVO\r\nacestream://{Hash1}\r\n";

        var result = M3uPlaylistParser.Parse(m3u);

        var channel = Assert.Single(result.Channels);
        Assert.Equal("TVO", channel.Name);
    }
}
