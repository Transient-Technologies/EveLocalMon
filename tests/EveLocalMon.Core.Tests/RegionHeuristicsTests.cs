using EveLocalMon.Core.Reading;

namespace EveLocalMon.Core.Tests;

public class RegionHeuristicsTests
{
    [Fact]
    public void FourHundredIsPlausibleAndFourHundredAndOneIsNot()
    {
        Assert.True(RegionHeuristics.IsPlausibleListWidth(400));
        Assert.False(RegionHeuristics.IsPlausibleListWidth(401));
        Assert.Equal(400, RegionHeuristics.MaxPlausibleListWidth);
    }

    [Fact]
    public void ATypicalMemberListIsPlausible()
    {
        Assert.True(RegionHeuristics.IsPlausibleListWidth(148));
    }
}
