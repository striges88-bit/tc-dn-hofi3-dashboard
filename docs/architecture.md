# Архитектура

Четыре слоя C4 для TC-DN-HOFI3. Публичные типы читай в коде. Здесь только то, чего из одного открытого файла не видно.

## Контекст

Оператор смотрит один инструмент USDS-M. Система считает TC-DN-HOFI3 и заявки не ставит. Снаружи публичные REST и WebSocket Binance, плюс локальный JSONL. `tools/Memory` и `tools/PilotB` к этой системе не относятся.

## Контейнеры

Процесс окна `CryptoIndicatorApp.Desktop` собран на `net8.0-windows`. Процесс `tools/LiveDryRun` собран на `net8.0` и гоняет тот же пайплайн. Domain, Application и Infrastructure это библиотеки, не процессы.

## Компоненты

Сборка адаптеров лежит в `CryptoIndicatorApp.Infrastructure/Composition`. Её вызывают и окно, и LiveDryRun. Сессии и `IndicatorPipeline` живут в Application. `TcDnHofi3Engine` и `LocalOrderBook` живут в Domain. `JsonlMarketEventStore` пишет и читает JSONL. `BinanceNetUsdFuturesMarketDataClient` ходит в Binance. `ContextModuleSession` работает только в live. `DashboardViewModel` есть только в окне.

Окно по-прежнему само создаёт клиент для списка символов. Этот вызов не идёт через `Composition`.

## Код

- Application зависит только от Domain. Do not reference `Infrastructure` from `Application`. Обратная ссылка есть. Infrastructure ссылается на Application, потому что адаптеры реализуют порты Application.
- Do not expose Binance DTOs to Domain. Пакет `Binance.Net` закрыт у Infrastructure через `PrivateAssets` со значением `compile`. Окно против типов клиента не компилируется. Сборка окна по-прежнему копирует `Binance.Net.dll` в выход.
- Конверт JSONL приватный внутри `JsonlMarketEventStore`. Схема 1. Снаружи ходят доменные события.
- Поля доменных событий с биржевыми идентификаторами являются контрактом записи JSONL. Вместе со схемой их не вычищать.
