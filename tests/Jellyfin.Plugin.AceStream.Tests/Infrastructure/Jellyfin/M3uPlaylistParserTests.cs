using Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;

namespace Jellyfin.Plugin.AceStream.Tests.Infrastructure.Jellyfin;

public class M3uPlaylistParserTests
{
    private const string Hash1 = "8c9febd01a731ce6139bca444d6aac9aaa764a88";
    private const string Hash2 = "8a25653a2f774f4ae1062d38a30dcd714d304a3a";

    [Fact]
    public void Parse_NullContent_ReturnsEmpty()
    {
        var result = M3uPlaylistParser.Parse(null);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsEmpty()
    {
        var result = M3uPlaylistParser.Parse(string.Empty);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_WhitespaceContent_ReturnsEmpty()
    {
        var result = M3uPlaylistParser.Parse("   \n   \n   ");
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_StandardExtInfWithAcestreamUrl_ReturnsChannel()
    {
        var m3u = $"#EXTM3U\n#EXTINF:-1,TVO\nacestream://{Hash1}";

        var result = M3uPlaylistParser.Parse(m3u);

        var channel = Assert.Single(result);
        Assert.Equal("TVO", channel.Name);
        Assert.Equal(Hash1, channel.Infohash.Value);
    }

    [Fact]
    public void Parse_MultipleChannels_ReturnsAll()
    {
        var m3u = $"#EXTM3U\n#EXTINF:-1,TVO\nacestream://{Hash1}\n#EXTINF:-1,DAZN LaLiga\nacestream://{Hash2}";

        var result = M3uPlaylistParser.Parse(m3u);

        Assert.Equal(2, result.Count);
        Assert.Equal("TVO", result[0].Name);
        Assert.Equal("DAZN LaLiga", result[1].Name);
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

        var channel = Assert.Single(result);
        Assert.Equal("TVO display", channel.Name);
    }

    [Fact]
    public void Parse_ExtInfWithNoDisplayName_UsesHashAsName()
    {
        var m3u = $"#EXTM3U\n#EXTINF:-1\nacestream://{Hash1}";

        var result = M3uPlaylistParser.Parse(m3u);

        var channel = Assert.Single(result);
        Assert.Equal(Hash1, channel.Name);
    }

    [Fact]
    public void Parse_AcestreamUrlWithoutExtInf_UsesHashAsName()
    {
        var m3u = $"#EXTM3U\nacestream://{Hash1}";

        var result = M3uPlaylistParser.Parse(m3u);

        var channel = Assert.Single(result);
        Assert.Equal(Hash1, channel.Name);
    }

    [Fact]
    public void Parse_NonAcestreamUrl_IsSkipped()
    {
        var m3u = $"#EXTM3U\n#EXTINF:-1,HTTP Channel\nhttp://example.com/stream.m3u8\n#EXTINF:-1,TVO\nacestream://{Hash1}";

        var result = M3uPlaylistParser.Parse(m3u);

        var channel = Assert.Single(result);
        Assert.Equal("TVO", channel.Name);
    }

    [Fact]
    public void Parse_InvalidHash_IsSkipped()
    {
        var m3u = $"#EXTM3U\n#EXTINF:-1,Bad\nacestream://notahash\n#EXTINF:-1,TVO\nacestream://{Hash1}";

        var result = M3uPlaylistParser.Parse(m3u);

        var channel = Assert.Single(result);
        Assert.Equal("TVO", channel.Name);
    }

    [Fact]
    public void Parse_AcestreamUrlIsUpperCase_NormalizesHash()
    {
        var m3u = $"#EXTM3U\n#EXTINF:-1,TVO\nacestream://{Hash1.ToUpperInvariant()}";

        var result = M3uPlaylistParser.Parse(m3u);

        var channel = Assert.Single(result);
        Assert.Equal(Hash1, channel.Infohash.Value);
    }

    [Fact]
    public void Parse_WindowsLineEndings_ParsesCorrectly()
    {
        var m3u = $"#EXTM3U\r\n#EXTINF:-1,TVO\r\nacestream://{Hash1}\r\n";

        var result = M3uPlaylistParser.Parse(m3u);

        var channel = Assert.Single(result);
        Assert.Equal("TVO", channel.Name);
    }
}
