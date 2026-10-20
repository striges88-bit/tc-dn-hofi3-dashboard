namespace CryptoIndicatorApp.Desktop.Tests;

internal static class TestAsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<T> TakeAsync<T>(
        this IAsyncEnumerable<T> values,
        int count,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var emitted = 0;
        await foreach (var value in values.WithCancellation(cancellationToken))
        {
            yield return value;
            emitted++;

            if (emitted == count)
            {
                yield break;
            }
        }
    }

    public static async Task<IReadOnlyList<T>> ToListAsync<T>(
        this IAsyncEnumerable<T> values,
        CancellationToken cancellationToken = default)
    {
        var result = new List<T>();
        await foreach (var value in values.WithCancellation(cancellationToken))
        {
            result.Add(value);
        }

        return result;
    }
}
