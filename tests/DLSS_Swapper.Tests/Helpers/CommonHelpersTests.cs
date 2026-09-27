using DLSS_Swapper.Helpers;

namespace DLSS_Swapper.Tests.Helpers;

public class CommonHelpersTests
{
    [Theory]
    [InlineData("", "", 0)]
    [InlineData("abc", "", 3)]
    [InlineData("", "abc", 3)]
    [InlineData("abc", "abc", 0)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("flaw", "lawn", 2)]
    [InlineData("sunday", "saturday", 3)]
    public void LevenshteinDistance_MatchesKnownValues(string s, string t, int expected)
    {
        Assert.Equal(expected, CommonHelpers.LevenshteinDistance(s, t));
    }

    [Theory]
    [InlineData("Half-Life 2", "Half-Life 2")]
    [InlineData("Cyberpunk 2077", "Cyberpunk 2077")]
    [InlineData("ELDEN RING", "ELDEN RING")]
    public void LevenshteinDistance_IsZeroForIdenticalStrings(string s, string t)
    {
        Assert.Equal(0, CommonHelpers.LevenshteinDistance(s, t));
    }

    [Fact]
    public void LevenshteinDistance_CountsASingleInsertionAsOne()
    {
        // "Cyberpunk2077" -> "Cyberpunk 2077" is one inserted space.
        Assert.Equal(1, CommonHelpers.LevenshteinDistance("Cyberpunk2077", "Cyberpunk 2077"));
    }

    [Fact]
    public void LevenshteinDistance_IsSymmetric()
    {
        // NVAPIHelper.FindGameProfile orders candidate profiles by this distance, so an asymmetric
        // result would make the chosen profile depend on argument order.
        Assert.Equal(
            CommonHelpers.LevenshteinDistance("Half-Life 2", "Death Stranding"),
            CommonHelpers.LevenshteinDistance("Death Stranding", "Half-Life 2"));
    }

    [Fact]
    public void LevenshteinDistance_IsAtMostTheLongerLength()
    {
        // Substituting every character is always an upper bound.
        const string a = "Resident Evil 4 Remake";
        const string b = "x";

        Assert.True(CommonHelpers.LevenshteinDistance(a, b) <= a.Length);
    }

    [Fact]
    public void LevenshteinDistance_HandlesNullSecondArgument()
    {
        Assert.Equal(3, CommonHelpers.LevenshteinDistance("abc", null!));
    }

    [Fact]
    public void LevenshteinDistance_HandlesBothNull()
    {
        Assert.Equal(0, CommonHelpers.LevenshteinDistance(null!, null!));
    }
}
