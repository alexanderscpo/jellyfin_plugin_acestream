using Jellyfin.Plugin.AceStream.Domain;

namespace Jellyfin.Plugin.AceStream.Tests.Domain;

public class ChannelStatusTests
{
    [Theory]
    [InlineData(2, ChannelStatus.Working)]
    [InlineData(1, ChannelStatus.Unreliable)]
    [InlineData(0, ChannelStatus.Unknown)]
    [InlineData(99, ChannelStatus.Unknown)]
    public void ToChannelStatus_MapsEngineCode(int code, ChannelStatus expected)
    {
        Assert.Equal(expected, code.ToChannelStatus());
    }
}
