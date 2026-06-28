namespace CryptoIndicatorApp.Domain.Context;

public enum ContextFrame
{
    FiveMinutes = 5,
    FifteenMinutes = 15
}

public static class ContextFrameExtensions
{
    public static TimeSpan ToDuration(this ContextFrame frame)
    {
        return frame switch
        {
            ContextFrame.FiveMinutes => TimeSpan.FromMinutes(5),
            ContextFrame.FifteenMinutes => TimeSpan.FromMinutes(15),
            _ => throw new ArgumentOutOfRangeException(nameof(frame), frame, "Unsupported context frame.")
        };
    }

    public static int VisibleTileCount(this ContextFrame frame, TimeSpan visibleDuration)
    {
        return Math.Max(1, (int)Math.Ceiling(visibleDuration.TotalMilliseconds / frame.ToDuration().TotalMilliseconds));
    }
}
