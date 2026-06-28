using System.Runtime.CompilerServices;
using CryptoIndicatorApp.Application.MarketData;
using CryptoIndicatorApp.Domain.Indicators;
using CryptoIndicatorApp.Domain.MarketData;

namespace CryptoIndicatorApp.Application.Pipeline;

public sealed class IndicatorPipeline
{
    private readonly Func<IMarketEvent, IndicatorSample?> _processMarketEvent;

    public IndicatorPipeline(Func<IMarketEvent, IndicatorSample?> processMarketEvent)
    {
        _processMarketEvent = processMarketEvent ?? throw new ArgumentNullException(nameof(processMarketEvent));
    }

    public static IndicatorPipeline CreateForTcDnHofi3(string symbol, IndicatorParameters? parameters = null)
    {
        var engine = new TcDnHofi3Engine(symbol, parameters);
        return new IndicatorPipeline(engine.Process);
    }

    public async IAsyncEnumerable<IndicatorSample> RunAsync(
        IAsyncEnumerable<IMarketEvent> marketEvents,
        IMarketEventRecorder? recorder = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(marketEvents);

        await foreach (var marketEvent in marketEvents.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (recorder is not null)
            {
                await recorder.AppendAsync(marketEvent, cancellationToken).ConfigureAwait(false);
            }

            var sample = _processMarketEvent(marketEvent);
            if (sample is not null)
            {
                yield return sample;
            }
        }
    }
}
