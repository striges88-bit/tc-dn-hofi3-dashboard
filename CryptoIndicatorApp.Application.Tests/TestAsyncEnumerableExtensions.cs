namespace CryptoIndicatorApp.Application.Tests;

internal static class TestAsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(
        this IAsyncEnumerable<T> source,
        CancellationToken cancellationToken = default)
    {
        var result = new List<T>();
        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            result.Add(item);
        }

        return result;
    }
}
