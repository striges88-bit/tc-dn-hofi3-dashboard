namespace CryptoIndicatorApp.Infrastructure.Binance;

public sealed record BinanceUsdFuturesSymbolMetadata(
    string Symbol,
    string Status,
    string ContractType);
