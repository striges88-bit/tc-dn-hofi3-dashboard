namespace CryptoIndicatorApp.Domain.Indicators;

public sealed record IndicatorParameters(
    int TopLevels = 3,
    decimal Lambda = 0.8m,
    int OfiWindowMilliseconds = 250,
    int StabilityWindowMilliseconds = 1000,
    int DepthReferenceSeconds = 60,
    int ZScoreWindowSeconds = 180,
    decimal ThetaZ = 2.0m,
    decimal ThetaStable = 0.8m,
    decimal ThetaTfi = 0.15m,
    int MinimumZScoreSamples = 30,
    decimal NofiMadFloor = 0.000001m);
