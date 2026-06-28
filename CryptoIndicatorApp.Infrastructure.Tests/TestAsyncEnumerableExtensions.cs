namespace CryptoIndicatorApp.Infrastructure.Tests;

internal static class TestAsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<T> TakeAsync<T>(
        this IAsyncEnumerable<T> source,
        int count,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var emitted = 0;
        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            if (emitted++ >= count)
            {
                yield break;
            }

            yield return item;

            if (emitted == count)
            {
                yield break;
            }
        }
    }

    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var results = new List<T>();
        await foreach (var item in source)
        {
            results.Add(item);
        }

        return results;
    }
}
