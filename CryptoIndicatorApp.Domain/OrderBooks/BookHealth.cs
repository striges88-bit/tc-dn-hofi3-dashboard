namespace CryptoIndicatorApp.Domain.OrderBooks;

public sealed record BookHealth(
    bool IsSynced,
    bool IsStale,
    bool IsCrossed,
    long? LastUpdateId,
    int ResyncCount,
    string? Reason)
{
    public static BookHealth Empty { get; } = new(
        IsSynced: false,
        IsStale: false,
        IsCrossed: false,
        LastUpdateId: null,
        ResyncCount: 0,
        Reason: "no_snapshot");
}
