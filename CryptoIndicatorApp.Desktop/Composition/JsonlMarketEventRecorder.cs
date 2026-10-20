using CryptoIndicatorApp.Application.MarketData;
using CryptoIndicatorApp.Domain.MarketData;
using CryptoIndicatorApp.Infrastructure.Jsonl;

namespace CryptoIndicatorApp.Desktop.Composition;

public sealed class JsonlMarketEventRecorder : IMarketEventRecorder
{
    private readonly JsonlMarketEventStore _store;
    private readonly string _path;

    private JsonlMarketEventRecorder(JsonlMarketEventStore store, string path)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("JSONL path is required.", nameof(path))
            : path;
    }

    public static JsonlMarketEventRecorder Create(string path)
    {
        return new JsonlMarketEventRecorder(new JsonlMarketEventStore(), path);
    }

    public ValueTask AppendAsync(IMarketEvent marketEvent, CancellationToken cancellationToken = default)
    {
        return new ValueTask(_store.AppendAsync(_path, marketEvent, cancellationToken));
    }
}
