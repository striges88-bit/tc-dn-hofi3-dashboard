using CryptoIndicatorApp.Application.MarketData;
using CryptoIndicatorApp.Application.Pipeline;
using CryptoIndicatorApp.Domain.Indicators;

namespace CryptoIndicatorApp.Application.Sessions;

public sealed class ReplayIndicatorSession
{
    private readonly IMarketEventSource _source;
    private readonly IndicatorPipeline _pipeline;

    public ReplayIndicatorSession(
        string symbol,
        IMarketEventSource source,
        IndicatorParameters? parameters = null)
        : this(source, IndicatorPipeline.CreateForTcDnHofi3(symbol, parameters))
    {
    }

    public ReplayIndicatorSession(IMarketEventSource source, IndicatorPipeline pipeline)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public IAsyncEnumerable<IndicatorSample> RunAsync(CancellationToken cancellationToken = default)
    {
        return _pipeline.RunAsync(_source.ReadAllAsync(cancellationToken), recorder: null, cancellationToken);
    }
}
