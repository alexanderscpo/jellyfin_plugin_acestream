using Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;

namespace Jellyfin.Plugin.AceStream.Tests.Infrastructure.Jellyfin;

public class RelativeTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Describe_UnderAMinute_IsJustNow()
    {
        Assert.Equal("just now", RelativeTime.Describe(Now.AddSeconds(-30), Now));
    }

    [Fact]
    public void Describe_Minutes()
    {
        Assert.Equal("5m ago", RelativeTime.Describe(Now.AddMinutes(-5), Now));
    }

    [Fact]
    public void Describe_Hours()
    {
        Assert.Equal("3h ago", RelativeTime.Describe(Now.AddHours(-3), Now));
    }

    [Fact]
    public void Describe_Days()
    {
        Assert.Equal("2d ago", RelativeTime.Describe(Now.AddDays(-2), Now));
    }

    [Fact]
    public void Describe_FutureClampsToJustNow()
    {
        Assert.Equal("just now", RelativeTime.Describe(Now.AddMinutes(5), Now));
    }
}
