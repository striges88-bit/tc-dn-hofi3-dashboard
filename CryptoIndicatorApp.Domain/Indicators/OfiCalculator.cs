using CryptoIndicatorApp.Domain.MarketData;

namespace CryptoIndicatorApp.Domain.Indicators;

public static class OfiCalculator
{
    public static decimal CalculateLevelOfi(
        BookLevel previousBid,
        BookLevel previousAsk,
        BookLevel currentBid,
        BookLevel currentAsk)
    {
        var bidContribution = 0m;
        if (currentBid.Price >= previousBid.Price)
        {
            bidContribution += currentBid.Quantity;
        }

        if (currentBid.Price <= previousBid.Price)
        {
            bidContribution -= previousBid.Quantity;
        }

        var askContribution = 0m;
        if (currentAsk.Price <= previousAsk.Price)
        {
            askContribution -= currentAsk.Quantity;
        }

        if (currentAsk.Price >= previousAsk.Price)
        {
            askContribution += previousAsk.Quantity;
        }

        return bidContribution + askContribution;
    }

    public static decimal CalculateWeightedHofi(IReadOnlyList<decimal> levelOfis, decimal lambda)
    {
        if (levelOfis.Count == 0)
        {
            return 0m;
        }

        var weights = CalculateWeights(levelOfis.Count, lambda);
        var total = 0m;
        for (var index = 0; index < levelOfis.Count; index++)
        {
            total += levelOfis[index] * weights[index];
        }

        return total;
    }

    public static decimal[] CalculateWeights(int levels, decimal lambda)
    {
        if (levels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(levels), "Levels must be positive.");
        }

        var raw = Enumerable.Range(0, levels)
            .Select(index => (decimal)Math.Exp(-(double)lambda * index))
            .ToArray();
        var sum = raw.Sum();
        return raw.Select(value => value / sum).ToArray();
    }
}
