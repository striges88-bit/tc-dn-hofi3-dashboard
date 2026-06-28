# Configuration

The desktop app uses JSON configuration through `CryptoIndicatorApp.Desktop/appsettings.json`.

Use `config/appsettings.example.json` as a sanitized reference for repository readers. Do not add `.env` files unless the application actually reads them.

## Main Sections

- `Dashboard.Symbol`: selected USDS-M Futures symbol.
- `Dashboard.Mode`: `Live` or `Replay`.
- `Dashboard.ReplayPath`: local JSONL replay file path.
- `Dashboard.RecordingPath`: local JSONL output path template.
- `Dashboard.Proxy`: optional HTTP proxy settings for public Binance access.
- `Dashboard.Context`: slow liquidation/open-interest context settings.
- `Dashboard.Indicator`: TC-DN-HOFI3 parameters.

## Secrets

No Binance API keys are required for the current public market-data scope. If future features need secrets, store them outside Git using environment variables or user secrets.
