using CryptoIndicatorApp.Domain.Context;

namespace CryptoIndicatorApp.Desktop.Configuration;

public sealed class ContextOptions
{
    public ContextFrame Frame { get; set; } = ContextFrame.FifteenMinutes;

    public int VisibleMinutes { get; set; } = 150;

    public int NormalizationHistoryHours { get; set; } = 24;

    public int MinimumNormalizationBuckets { get; set; } = 12;

    public decimal NormalizationFloor { get; set; } = 0.00000001m;

    public int OpenInterestHistoryLimit { get; set; } = 288;

    public int OpenInterestRefreshSeconds { get; set; } = 60;

    public TimeSpan VisibleDuration => TimeSpan.FromMinutes(VisibleMinutes > 0 ? VisibleMinutes : 150);

    public TimeSpan NormalizationHistory => TimeSpan.FromHours(NormalizationHistoryHours > 0 ? NormalizationHistoryHours : 24);

    public TimeSpan OpenInterestRefreshInterval => OpenInterestRefreshSeconds > 0
        ? TimeSpan.FromSeconds(OpenInterestRefreshSeconds)
        : TimeSpan.Zero;

    public void Normalize()
    {
        if (Frame is not ContextFrame.FiveMinutes and not ContextFrame.FifteenMinutes)
        {
            Frame = ContextFrame.FifteenMinutes;
        }

        if (VisibleMinutes <= 0)
        {
            VisibleMinutes = 150;
        }

        if (NormalizationHistoryHours <= 0)
        {
            NormalizationHistoryHours = 24;
        }

        if (MinimumNormalizationBuckets <= 0)
        {
            MinimumNormalizationBuckets = 12;
        }

        if (NormalizationFloor <= 0m)
        {
            NormalizationFloor = 0.00000001m;
        }

        if (OpenInterestHistoryLimit <= 0)
        {
            OpenInterestHistoryLimit = 288;
        }

        if (OpenInterestRefreshSeconds < 0)
        {
            OpenInterestRefreshSeconds = 0;
        }
    }
}
