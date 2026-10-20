using CryptoIndicatorApp.Application.MarketData;
using CryptoIndicatorApp.Domain.MarketData;
using CryptoIndicatorApp.Infrastructure.Jsonl;

namespace CryptoIndicatorApp.Desktop.Composition;

public sealed class JsonlMarketEventSource : IMarketEventSource
{
    private readonly JsonlMarketEventStore _store;
    private readonly string _path;

    private JsonlMarketEventSource(JsonlMarketEventStore store, string path)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("JSONL path is required.", nameof(path))
            : path;
    }

    public static JsonlMarketEventSource Create(string path)
    {
        return new JsonlMarketEventSource(new JsonlMarketEventStore(), path);
    }

    public IAsyncEnumerable<IMarketEvent> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return _store.ReadAsync(_path, cancellationToken);
    }
}
