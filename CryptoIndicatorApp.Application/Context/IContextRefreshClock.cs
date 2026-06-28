using System.Runtime.CompilerServices;

namespace CryptoIndicatorApp.Application.Context;

public interface IContextRefreshClock
{
    IAsyncEnumerable<DateTimeOffset> TicksAsync(
        TimeSpan interval,
        CancellationToken cancellationToken = default);
}

internal sealed class PeriodicContextRefreshClock : IContextRefreshClock
{
    public static PeriodicContextRefreshClock Instance { get; } = new();

    private PeriodicContextRefreshClock()
    {
    }

    public async IAsyncEnumerable<DateTimeOffset> TicksAsync(
        TimeSpan interval,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (interval <= TimeSpan.Zero)
        {
            yield break;
        }

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            yield return DateTimeOffset.UtcNow;
        }
    }
}
