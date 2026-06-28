using CryptoIndicatorApp.Domain.Indicators;

namespace CryptoIndicatorApp.Desktop.Configuration;

public sealed class IndicatorParameterOptions
{
    public int TopLevels { get; set; } = 3;

    public decimal Lambda { get; set; } = 0.8m;

    public int OfiWindowMilliseconds { get; set; } = 250;

    public int StabilityWindowMilliseconds { get; set; } = 1000;

    public int DepthReferenceSeconds { get; set; } = 60;

    public int ZScoreWindowSeconds { get; set; } = 180;

    public decimal ThetaZ { get; set; } = 2.0m;

    public decimal ThetaStable { get; set; } = 0.8m;

    public decimal ThetaTfi { get; set; } = 0.15m;

    public int MinimumZScoreSamples { get; set; } = 30;

    public decimal NofiMadFloor { get; set; } = 0.000001m;

    public IndicatorParameters ToDomain()
    {
        return new IndicatorParameters(
            TopLevels,
            Lambda,
            OfiWindowMilliseconds,
            StabilityWindowMilliseconds,
            DepthReferenceSeconds,
            ZScoreWindowSeconds,
            ThetaZ,
            ThetaStable,
            ThetaTfi,
            MinimumZScoreSamples,
            NofiMadFloor);
    }
}
