using Jellyfin.Plugin.AceStream.Domain;

namespace Jellyfin.Plugin.AceStream.Tests.Domain;

public class InfohashTests
{
    private const string ValidHash = "0a4848271c91ce2d8965ce416267c25047dc8141";

    [Fact]
    public void Create_WithValidHash_KeepsValue()
    {
        var infohash = Infohash.Create(ValidHash);

        Assert.Equal(ValidHash, infohash.Value);
    }

    [Fact]
    public void Create_NormalizesToLowercase()
    {
        var infohash = Infohash.Create(ValidHash.ToUpperInvariant());

        Assert.Equal(ValidHash, infohash.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithNullOrEmpty_Throws(string? value)
    {
        Assert.Throws<ArgumentException>(() => Infohash.Create(value!));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0a4848271c91ce2d8965ce416267c25047dc8141aa")]
    public void Create_WithInvalidLength_Throws(string value)
    {
        Assert.Throws<ArgumentException>(() => Infohash.Create(value));
    }

    [Fact]
    public void Create_WithNonHexCharacters_Throws()
    {
        var nonHex = new string('z', 40);

        Assert.Throws<ArgumentException>(() => Infohash.Create(nonHex));
    }

    [Fact]
    public void Equality_SameValueDifferentCase_AreEqual()
    {
        var lower = Infohash.Create(ValidHash);
        var upper = Infohash.Create(ValidHash.ToUpperInvariant());

        Assert.Equal(lower, upper);
    }
}
