namespace CryptoIndicatorApp.Domain.Context;

public enum ContextDirection
{
    Unavailable,
    Neutral,
    Positive,
    Negative
}

public static class ContextDirectionExtensions
{
    public static ContextDirection FromSignedValue(decimal value)
    {
        if (value > 0m)
        {
            return ContextDirection.Positive;
        }

        if (value < 0m)
        {
            return ContextDirection.Negative;
        }

        return ContextDirection.Neutral;
    }
}
