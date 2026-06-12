using Jellyfin.Plugin.AceStream.Domain;
using Jellyfin.Plugin.AceStream.Infrastructure.Engine;

namespace Jellyfin.Plugin.AceStream.Tests.Infrastructure.Engine;

public class EngineStatusMapperTests
{
    [Theory]
    [InlineData(2, ChannelStatus.Working)]
    [InlineData(1, ChannelStatus.Unreliable)]
    [InlineData(0, ChannelStatus.Unknown)]
    [InlineData(99, ChannelStatus.Unknown)]
    public void Map_MapsEngineCode(int code, ChannelStatus expected)
    {
        Assert.Equal(expected, EngineStatusMapper.Map(code));
    }
}
