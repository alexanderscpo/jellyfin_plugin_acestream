using Jellyfin.Plugin.AceStream.Domain;
using Jellyfin.Plugin.AceStream.Infrastructure.Jellyfin;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Channels;

namespace Jellyfin.Plugin.AceStream.Tests.Infrastructure.Jellyfin;

public class ChannelItemMapperTests
{
    private const string Hash = "8c9febd01a731ce6139bca444d6aac9aaa764a88";

    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    private static AceChannel Channel(
        ChannelStatus status = ChannelStatus.Working,
        double availability = 1.0,
        string[]? countries = null,
        string[]? languages = null,
        DateTimeOffset? availabilityUpdatedAt = null)
    {
        return new AceChannel(
            Infohash.Create(Hash),
            "TVO",
            status,
            Availability.Create(availability),
            new[] { "tv" },
            disabled: false,
            channelId: 21448,
            countries: countries,
            languages: languages,
            availabilityUpdatedAt: availabilityUpdatedAt);
    }

    [Fact]
    public void Id_IsInfohash_ForStableFavorites()
    {
        var item = ChannelItemMapper.ToChannelItemInfo(Channel(), Now);

        Assert.Equal(Hash, item.Id);
    }

    [Fact]
    public void Name_AndType_AreMapped()
    {
        var item = ChannelItemMapper.ToChannelItemInfo(Channel(), Now);

        Assert.Equal("TVO", item.Name);
        Assert.Equal(ChannelItemType.Media, item.Type);
    }

    [Fact]
    public void Overview_CarriesChannelInformation()
    {
        var item = ChannelItemMapper.ToChannelItemInfo(
            Channel(status: ChannelStatus.Working, availability: 1.0, countries: new[] { "de" }, languages: new[] { "deu" }),
            Now);

        Assert.Contains("Working", item.Overview, StringComparison.Ordinal);
        Assert.Contains("100%", item.Overview, StringComparison.Ordinal);
        Assert.Contains("de", item.Overview, StringComparison.Ordinal);
        Assert.Contains("deu", item.Overview, StringComparison.Ordinal);
    }

    [Fact]
    public void Overview_IncludesAvailabilityFreshness()
    {
        var item = ChannelItemMapper.ToChannelItemInfo(
            Channel(availabilityUpdatedAt: Now.AddHours(-3)),
            Now);

        Assert.Contains("Verified 3h ago", item.Overview, StringComparison.Ordinal);
    }

    [Fact]
    public void Overview_OmitsFreshness_WhenNotReported()
    {
        var item = ChannelItemMapper.ToChannelItemInfo(Channel(availabilityUpdatedAt: null), Now);

        Assert.DoesNotContain("Verified", item.Overview, StringComparison.Ordinal);
    }

    [Fact]
    public void Categories_BecomeTags()
    {
        var item = ChannelItemMapper.ToChannelItemInfo(Channel(), Now);

        Assert.Contains("tv", item.Tags);
    }
}
