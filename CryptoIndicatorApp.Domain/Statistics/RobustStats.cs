namespace CryptoIndicatorApp.Domain.Statistics;

public static class RobustStats
{
    private const decimal MadScale = 1.4826m;

    public static decimal Median(IEnumerable<decimal> values)
    {
        var ordered = values.Order().ToArray();
        if (ordered.Length == 0)
        {
            throw new ArgumentException("At least one value is required.", nameof(values));
        }

        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 1
            ? ordered[middle]
            : (ordered[middle - 1] + ordered[middle]) / 2m;
    }

    public static decimal MedianAbsoluteDeviation(IEnumerable<decimal> values)
    {
        var snapshot = values.ToArray();
        var median = Median(snapshot);
        return Median(snapshot.Select(value => Math.Abs(value - median)));
    }

    public static decimal RobustZ(decimal value, IEnumerable<decimal> history, decimal denominatorFloor = 0.000000001m)
    {
        if (denominatorFloor < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(denominatorFloor), "Denominator floor cannot be negative.");
        }

        var snapshot = history.ToArray();
        var median = Median(snapshot);
        var mad = MedianAbsoluteDeviation(snapshot);
        var denominator = Math.Max(MadScale * mad, denominatorFloor);
        return (value - median) / denominator;
    }
}
