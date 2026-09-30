using Sherlock.MCP.Runtime.Inspection;

namespace Sherlock.MCP.Tests;

public class NuGetVersionsTests
{
    [Fact]
    public void SortDescending_StableRanksAbovePrereleaseOfSameVersion()
    {
        Assert.Equal(["1.0.0", "1.0.0-rc.1", "1.0.0-beta"], NuGetVersions.SortDescending(["1.0.0-beta", "1.0.0", "1.0.0-rc.1"]));
    }

    [Fact]
    public void SortDescending_NumericPrereleaseIdentifiersCompareNumerically()
    {
        Assert.Equal(["2.0.0-beta.10", "2.0.0-beta.2", "2.0.0-beta"], NuGetVersions.SortDescending(["2.0.0-beta.2", "2.0.0-beta", "2.0.0-beta.10"]));
    }

    [Fact]
    public void SortDescending_HigherCoreVersionBeatsStability()
    {
        Assert.Equal(["2.0.0-alpha", "1.9.9", "1.2.0"], NuGetVersions.SortDescending(["1.2.0", "2.0.0-alpha", "1.9.9"]));
    }

    [Theory]
    [InlineData("1.0", "1.0.0")]
    [InlineData("1.0.0+build.5", "1.0.0")]
    [InlineData("1.0.0-BETA", "1.0.0-beta")]
    public void Compare_EquivalentVersions_TieBreakOnlyOnRawText(string left, string right)
    {
        var sign = Math.Sign(NuGetVersions.Compare(left, right));

        Assert.Equal(Math.Sign(StringComparer.OrdinalIgnoreCase.Compare(left, right)), sign);
    }

    [Fact]
    public void Compare_NumericIdentifierRanksBelowAlphanumeric()
    {
        Assert.True(NuGetVersions.Compare("1.0.0-1", "1.0.0-alpha") < 0);
    }
}
