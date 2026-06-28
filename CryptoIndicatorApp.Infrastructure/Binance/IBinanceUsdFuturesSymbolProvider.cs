namespace CryptoIndicatorApp.Infrastructure.Binance;

public interface IBinanceUsdFuturesSymbolProvider
{
    Task<IReadOnlyList<string>> GetActivePerpetualSymbolsAsync(
        CancellationToken cancellationToken = default);
}
