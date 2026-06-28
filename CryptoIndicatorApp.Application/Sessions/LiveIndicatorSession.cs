using CryptoIndicatorApp.Application.MarketData;
using CryptoIndicatorApp.Application.Pipeline;
using CryptoIndicatorApp.Domain.Indicators;

namespace CryptoIndicatorApp.Application.Sessions;

public sealed class LiveIndicatorSession
{
    private readonly IMarketEventSource _source;
    private readonly IMarketEventRecorder? _recorder;
    private readonly IndicatorPipeline _pipeline;

    public LiveIndicatorSession(
        string symbol,
        IMarketEventSource source,
        IMarketEventRecorder? recorder,
        IndicatorParameters? parameters = null)
        : this(source, recorder, IndicatorPipeline.CreateForTcDnHofi3(symbol, parameters))
    {
    }

    public LiveIndicatorSession(
        IMarketEventSource source,
        IMarketEventRecorder? recorder,
        IndicatorPipeline pipeline)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _recorder = recorder;
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public IAsyncEnumerable<IndicatorSample> RunAsync(CancellationToken cancellationToken = default)
    {
        return _pipeline.RunAsync(_source.ReadAllAsync(cancellationToken), _recorder, cancellationToken);
    }
}
