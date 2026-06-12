using Jellyfin.Plugin.AceStream.Domain;

namespace Jellyfin.Plugin.AceStream.Tests.Domain;

public class AceChannelTests
{
    private const string HashA = "0a4848271c91ce2d8965ce416267c25047dc8141";
    private const string HashB = "3c86e88beeb52233540afaf9febbb944d958cba6";

    private static AceChannel Channel(
        string infohash,
        string name = "Channel",
        ChannelStatus status = ChannelStatus.Working,
        bool disabled = false)
    {
        return new AceChannel(
            Infohash.Create(infohash),
            name,
            status,
            Availability.Create(1.0),
            new[] { "tv" },
            disabled);
    }

    [Fact]
    public void Identity_SameInfohash_AreEqual()
    {
        var a = Channel(HashA, name: "Name A");
        var b = Channel(HashA, name: "Totally Different Name");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Identity_DifferentInfohash_AreNotEqual()
    {
        Assert.NotEqual(Channel(HashA), Channel(HashB));
    }

    [Fact]
    public void IsReliable_WorkingAndNotDisabled_IsTrue()
    {
        Assert.True(Channel(HashA, status: ChannelStatus.Working, disabled: false).IsReliable);
    }

    [Theory]
    [InlineData(ChannelStatus.Unreliable, false)]
    [InlineData(ChannelStatus.Unknown, false)]
    [InlineData(ChannelStatus.Working, true)]
    public void IsReliable_DependsOnStatus(ChannelStatus status, bool expected)
    {
        Assert.Equal(expected, Channel(HashA, status: status).IsReliable);
    }

    [Fact]
    public void IsReliable_Disabled_IsFalse()
    {
        Assert.False(Channel(HashA, status: ChannelStatus.Working, disabled: true).IsReliable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => Channel(HashA, name: name));
    }

    [Fact]
    public void OptionalFields_DefaultToEmpty()
    {
        var channel = Channel(HashA);

        Assert.Null(channel.ChannelId);
        Assert.Empty(channel.Countries);
        Assert.Empty(channel.Languages);
    }

    // operator == / != tests
    [Fact]
    public void EqualityOperator_SameInfohash_ReturnsTrue()
    {
        var a = Channel(HashA, name: "Alpha");
        var b = Channel(HashA, name: "Beta");

        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void EqualityOperator_DifferentInfohash_ReturnsFalse()
    {
        var a = Channel(HashA);
        var b = Channel(HashB);

        Assert.False(a == b);
        Assert.True(a != b);
    }

    [Fact]
    public void EqualityOperator_BothNull_ReturnsTrue()
    {
        AceChannel? left = null;
        AceChannel? right = null;

#pragma warning disable CS8604 // Nullability — intentional null comparison test
        Assert.True(left == right);
        Assert.False(left != right);
#pragma warning restore CS8604
    }

    [Fact]
    public void EqualityOperator_LeftNull_ReturnsFalse()
    {
        AceChannel? left = null;
        var right = Channel(HashA);

        Assert.False(left == right);
        Assert.True(left != right);
    }

    [Fact]
    public void EqualityOperator_RightNull_ReturnsFalse()
    {
        var left = Channel(HashA);
        AceChannel? right = null;

        Assert.False(left == right);
        Assert.True(left != right);
    }
}
