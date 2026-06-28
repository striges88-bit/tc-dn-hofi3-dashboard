using CryptoIndicatorApp.Application.Context;
using CryptoIndicatorApp.Desktop.Composition;

namespace CryptoIndicatorApp.Desktop.Tests;

public sealed class BinanceContextCompositionAdapterTests
{
    [Fact]
    public void Binance_context_source_implements_application_boundary()
    {
        Assert.True(typeof(IContextDataSource).IsAssignableFrom(typeof(BinanceContextDataSource)));
    }
}
