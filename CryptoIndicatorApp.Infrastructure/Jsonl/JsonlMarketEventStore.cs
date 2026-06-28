using System.Runtime.CompilerServices;
using System.Text.Json;
using CryptoIndicatorApp.Domain.MarketData;

namespace CryptoIndicatorApp.Infrastructure.Jsonl;

public sealed class JsonlMarketEventStore
{
    private const int CurrentSchemaVersion = 1;
    private const string Source = "binance-usdsm-futures";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task AppendAsync(string path, IMarketEvent marketEvent, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(marketEvent);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var envelope = CreateEnvelope(marketEvent);
        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        await File.AppendAllTextAsync(path, json + Environment.NewLine, cancellationToken);
    }

    public async IAsyncEnumerable<IMarketEvent> ReadAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);

        var rowNumber = 0;
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            rowNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                throw new JsonlReplayException($"Malformed JSONL row {rowNumber}: empty line.");
            }

            yield return ParseRow(line, rowNumber);
        }
    }

    private static JsonlEnvelope CreateEnvelope(IMarketEvent marketEvent)
    {
        return marketEvent switch
        {
            DepthSnapshotEvent snapshot => new JsonlEnvelope(
                CurrentSchemaVersion,
                Source,
                "depth",
                "depthSnapshot",
                snapshot.Symbol,
                snapshot.ExchangeTime,
                snapshot.ReceiveTime,
                new DepthSnapshotPayload(snapshot.LastUpdateId, snapshot.Bids, snapshot.Asks)),

            DepthUpdateEvent update => new JsonlEnvelope(
                CurrentSchemaVersion,
                Source,
                "depth",
                "depthUpdate",
                update.Symbol,
                update.ExchangeTime,
                update.ReceiveTime,
                new DepthUpdatePayload(
                    update.FirstUpdateId,
                    update.FinalUpdateId,
                    update.PreviousFinalUpdateId,
                    update.Bids,
                    update.Asks)),

            AggTradeEvent trade => new JsonlEnvelope(
                CurrentSchemaVersion,
                Source,
                "aggTrade",
                "aggTrade",
                trade.Symbol,
                trade.ExchangeTime,
                trade.ReceiveTime,
                new AggTradePayload(
                    trade.AggregateTradeId,
                    trade.Price,
                    trade.Quantity,
                    trade.FirstTradeId,
                    trade.LastTradeId,
                    trade.TradeTime,
                    trade.IsBuyerMaker)),

            _ => throw new ArgumentException(
                $"Unsupported market event type '{marketEvent.GetType().Name}'.",
                nameof(marketEvent))
        };
    }

    private static IMarketEvent ParseRow(string line, int rowNumber)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            var schemaVersion = root.GetProperty("schemaVersion").GetInt32();
            if (schemaVersion != CurrentSchemaVersion)
            {
                throw new JsonlReplayException(
                    $"Unsupported schema version {schemaVersion} at JSONL row {rowNumber}.");
            }

            var eventType = root.GetProperty("eventType").GetString();
            var symbol = root.GetProperty("symbol").GetString()
                ?? throw new JsonlReplayException($"Invalid JSONL row {rowNumber}: missing symbol.");
            var exchangeTime = root.GetProperty("exchangeTime").GetDateTimeOffset();
            var receiveTime = root.GetProperty("receiveTime").GetDateTimeOffset();
            var payload = root.GetProperty("payload");

            return eventType switch
            {
                "depthSnapshot" => new DepthSnapshotEvent(
                    symbol,
                    payload.GetProperty("lastUpdateId").GetInt64(),
                    ReadBookLevels(payload.GetProperty("bids")),
                    ReadBookLevels(payload.GetProperty("asks")),
                    exchangeTime,
                    receiveTime),

                "depthUpdate" => new DepthUpdateEvent(
                    symbol,
                    payload.GetProperty("firstUpdateId").GetInt64(),
                    payload.GetProperty("finalUpdateId").GetInt64(),
                    payload.GetProperty("previousFinalUpdateId").GetInt64(),
                    ReadBookLevels(payload.GetProperty("bids")),
                    ReadBookLevels(payload.GetProperty("asks")),
                    exchangeTime,
                    receiveTime),

                "aggTrade" => new AggTradeEvent(
                    symbol,
                    payload.GetProperty("aggregateTradeId").GetInt64(),
                    payload.GetProperty("price").GetDecimal(),
                    payload.GetProperty("quantity").GetDecimal(),
                    payload.GetProperty("firstTradeId").GetInt64(),
                    payload.GetProperty("lastTradeId").GetInt64(),
                    payload.GetProperty("tradeTime").GetDateTimeOffset(),
                    payload.GetProperty("isBuyerMaker").GetBoolean(),
                    exchangeTime,
                    receiveTime),

                _ => throw new JsonlReplayException(
                    $"Unsupported event type '{eventType}' at JSONL row {rowNumber}.")
            };
        }
        catch (JsonException ex)
        {
            throw new JsonlReplayException($"Malformed JSONL row {rowNumber}: {ex.Message}", ex);
        }
        catch (KeyNotFoundException ex)
        {
            throw new JsonlReplayException($"Invalid JSONL row {rowNumber}: missing required field.", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new JsonlReplayException($"Invalid JSONL row {rowNumber}: invalid field value.", ex);
        }
    }

    private static IReadOnlyList<BookLevel> ReadBookLevels(JsonElement levels)
    {
        var result = new List<BookLevel>();
        foreach (var level in levels.EnumerateArray())
        {
            if (level.ValueKind == JsonValueKind.Array)
            {
                result.Add(new BookLevel(
                    level[0].GetDecimal(),
                    level[1].GetDecimal()));
                continue;
            }

            result.Add(new BookLevel(
                level.GetProperty("price").GetDecimal(),
                level.GetProperty("quantity").GetDecimal()));
        }

        return result;
    }

    private sealed record JsonlEnvelope(
        int SchemaVersion,
        string Source,
        string Stream,
        string EventType,
        string Symbol,
        DateTimeOffset ExchangeTime,
        DateTimeOffset ReceiveTime,
        object Payload);

    private sealed record DepthSnapshotPayload(
        long LastUpdateId,
        IReadOnlyList<BookLevel> Bids,
        IReadOnlyList<BookLevel> Asks);

    private sealed record DepthUpdatePayload(
        long FirstUpdateId,
        long FinalUpdateId,
        long PreviousFinalUpdateId,
        IReadOnlyList<BookLevel> Bids,
        IReadOnlyList<BookLevel> Asks);

    private sealed record AggTradePayload(
        long AggregateTradeId,
        decimal Price,
        decimal Quantity,
        long FirstTradeId,
        long LastTradeId,
        DateTimeOffset TradeTime,
        bool IsBuyerMaker);
}
