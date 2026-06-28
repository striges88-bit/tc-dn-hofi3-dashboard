using CryptoIndicatorApp.Domain.Statistics;

namespace CryptoIndicatorApp.Domain.Tests;

public class RobustStatsTests
{
    [Fact]
    public void RobustZUsesMedianAndMad()
    {
        var z = RobustStats.RobustZ(20m, new[] { 10m, 11m, 12m, 13m, 100m });

        Assert.Equal(5.3959m, Math.Round(z, 4));
    }

    [Fact]
    public void RobustZUsesDenominatorFloorWhenMadIsZero()
    {
        var z = RobustStats.RobustZ(1.01m, new[] { 1m, 1m, 1m, 1m }, denominatorFloor: 0.1m);

        Assert.Equal(0.1m, Math.Round(z, 4));
    }
}
