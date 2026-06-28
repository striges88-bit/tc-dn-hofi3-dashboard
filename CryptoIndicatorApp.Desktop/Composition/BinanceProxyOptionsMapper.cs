using CryptoIndicatorApp.Desktop.Configuration;
using CryptoIndicatorApp.Infrastructure.Binance;

namespace CryptoIndicatorApp.Desktop.Composition;

internal static class BinanceProxyOptionsMapper
{
    public static BinanceProxyOptions ToInfrastructure(ProxyOptions? proxyOptions)
    {
        if (proxyOptions is null)
        {
            return new BinanceProxyOptions();
        }

        return new BinanceProxyOptions
        {
            Enabled = proxyOptions.Enabled,
            Type = proxyOptions.Type,
            Host = proxyOptions.Host,
            Port = proxyOptions.Port
        };
    }
}
