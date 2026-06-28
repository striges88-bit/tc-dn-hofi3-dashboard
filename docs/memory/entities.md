# Entities

## Domain

- `MarketEvent`: project-owned internal market event contract.
- `LocalOrderBook`: local bid/ask state and sequence health.
- `TcDnHofi3Engine`: indicator calculation boundary.
- `IndicatorSample`: calculated output consumed by UI/application layers.
- `ContextTile`: aggregated slow-context display bucket.

## Application

- `IndicatorPipeline`: shared live/replay event processing path.
- `LiveIndicatorSession`: live source orchestration.
- `ReplayIndicatorSession`: replay source orchestration.
- `ContextModuleSession`: slow context refresh and aggregation orchestration.

## Infrastructure

- `BinanceNetUsdFuturesMarketDataClient`: Binance public market-data adapter.
- `BinanceUsdFuturesLiveMarketEventSource`: live stream/snapshot source.
- `JsonlMarketEventStore`: JSONL read/write boundary.

## Desktop

- `DashboardViewModel`: user-facing dashboard state.
- `MainWindow`: WPF dashboard rendering.
- `DashboardConfiguration`: appsettings binding and validation boundary.
