using CryptoIndicatorApp.Domain.MarketData;

namespace CryptoIndicatorApp.Infrastructure.Tests;

public class BinanceBoundaryTests
{
    [Fact]
    public void DomainAssemblyDoesNotReferenceBinanceNet()
    {
        var references = typeof(DepthUpdateEvent).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference => reference.Name == "Binance.Net");
    }
}
