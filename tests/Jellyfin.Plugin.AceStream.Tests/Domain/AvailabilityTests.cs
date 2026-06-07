using Jellyfin.Plugin.AceStream.Domain;

namespace Jellyfin.Plugin.AceStream.Tests.Domain;

public class AvailabilityTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Create_WithinRange_KeepsValue(double value)
    {
        Assert.Equal(value, Availability.Create(value).Value);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void Create_OutOfRange_Throws(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Availability.Create(value));
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        Assert.Equal(Availability.Create(0.75), Availability.Create(0.75));
    }
}
