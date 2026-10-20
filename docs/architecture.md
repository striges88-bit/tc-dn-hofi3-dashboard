# Архитектура

Карта C4 для TC-DN-HOFI3 в том виде, в каком она собрана сейчас. Публичные типы читай в коде. Здесь связи, которых не видно из одного файла.

Решение 2026-05-28: один процесс WPF на .NET 8. Application зависит только от Domain. Desktop собирает Infrastructure. Текст: [checkpoint](thread-checkpoints-2026-10-02.md#approved-product-and-architecture). Рядом с окном есть консольный `tools/LiveDryRun`: тот же пайплайн, отдельные копии адаптеров.

## Контекст

Оператор смотрит один инструмент USDS-M. Система считает TC-DN-HOFI3 и заявки не ставит. Снаружи публичные REST и WebSocket Binance и локальный файл JSONL. `tools/Memory` и `tools/PilotB` к этой системе не относятся.

```mermaid
C4Context
    title Контекст TC-DN-HOFI3
    Person(operator, "Оператор", "Смотрит один инструмент USDS-M")
    System(dashboard, "Дашборд TC-DN-HOFI3", "Считает индикатор. Заявки не ставит.")
    System_Ext(binance, "Binance USDS-M", "Публичные REST и WebSocket")
    System_Ext(jsonl, "Локальный JSONL", "Сырые рыночные события, схема 1")
    Rel(operator, dashboard, "Смотрит график и статус")
    Rel(dashboard, binance, "Читает книгу, сделки, OI и ликвидации")
    Rel(dashboard, jsonl, "Пишет live и читает replay")
```

## Контейнеры

Два процесса гоняют один индикаторный пайплайн. Domain, Application и Infrastructure — библиотеки внутри этих процессов, не отдельные сервисы.

```mermaid
C4Container
    title Контейнеры TC-DN-HOFI3
    Person(operator, "Оператор", "Один инструмент USDS-M")
    System_Ext(binance, "Binance USDS-M", "Публичные REST и WebSocket")
    System_Ext(jsonl, "Файл JSONL", "Сырые события, схема 1")
    System_Boundary(hosts, "Дашборд") {
        Container(desktop, "Desktop", "WPF, net8.0-windows", "Окно. Адаптеры в Desktop/Composition")
        Container(dryrun, "LiveDryRun", "консоль, net8.0", "Тот же пайплайн без окна. Свои адаптеры в Program.cs")
    }
    Rel(operator, desktop, "Старт, стоп, символ, график")
    Rel(desktop, binance, "Live и список символов")
    Rel(desktop, jsonl, "Запись и replay")
    Rel(dryrun, binance, "Live dry-run и контекст")
    Rel(dryrun, jsonl, "Запись и replay")
```

`tools/LiveDryRun` гоняет replay там, где окно WPF не запускается. Ограничения Linux: [AGENTS.md](../AGENTS.md), раздел Cursor Cloud.

## Компоненты

Стрелка на схеме — прямая `ProjectReference`. Application не ссылается на Infrastructure. Infrastructure не ссылается на Application: у него в проекте только Domain.

```mermaid
flowchart LR
    desktop["Desktop"] --> application["Application"]
    desktop --> infrastructure["Infrastructure"]
    desktop --> domain["Domain"]
    dryrun["LiveDryRun"] --> application
    dryrun --> infrastructure
    application --> domain
    infrastructure --> domain
```

```mermaid
C4Component
    title Компоненты окна Desktop
    Container_Boundary(desktop, "CryptoIndicatorApp.Desktop") {
        Component(view, "ViewModels и WPF", "DashboardViewModel", "Экран оператора")
        Component(composition, "Desktop/Composition", "C#", "Адаптеры портов Application")
        Component(sessions, "Application", "Sessions, IndicatorPipeline", "Live, replay и контекст")
        Component(engine, "Domain", "TcDnHofi3Engine, LocalOrderBook", "Формула и книга")
        Component(binance, "Infrastructure/Binance", "BinanceNetUsdFuturesMarketDataClient", "REST и WebSocket")
        Component(jsonl, "Infrastructure/Jsonl", "JsonlMarketEventStore", "Чтение и запись JSONL")
    }
    Rel(view, sessions, "Запускает сессию")
    Rel(composition, sessions, "Реализует порты")
    Rel(composition, binance, "Live и контекст")
    Rel(composition, jsonl, "Запись и replay")
    Rel(sessions, engine, "События в формулу")
```

В `CryptoIndicatorApp.Desktop/Composition` лежат `BinanceLiveMarketEventSource`, `BinanceContextDataSource`, `JsonlMarketEventRecorder`, `JsonlMarketEventSource` и `BinanceProxyOptionsMapper`.

У `tools/LiveDryRun` общего проекта адаптеров нет. `Program.cs` держит свои `LiveSourceAdapter`, `JsonlRecorderAdapter`, `JsonlSourceAdapter` и `ContextSmokeDataSource`. Они вызывают те же типы Infrastructure и те же сессии Application.

Список символов окно собирает само: `MainWindow` создаёт `BinanceNetUsdFuturesMarketDataClient` напрямую, не через `Desktop/Composition`.

## Поток

```mermaid
flowchart LR
    live["Live Binance"] --> map["Infrastructure"]
    replay["JSONL replay"] --> map
    map --> pipeline["Application pipeline"]
    pipeline --> engine["Domain engine"]
    engine --> state["Состояние окна или вывод LiveDryRun"]
```

Live и replay подают одни и те же внутренние события в один `IndicatorPipeline`. Контекст открытого интереса и ликвидаций идёт отдельной сессией `ContextModuleSession`, не через JSONL replay.

## Границы

- Не вызывать REST на горячем пути расчёта глубины и сделок. Снимок книги и ресинк — только для сборки локальной книги.
- Не ссылаться на Infrastructure из Application. Адаптеры JSONL и Binance собираются в Desktop или в другом внешнем хосте.
- Не показывать DTO клиента Binance в Domain. Пакет `Binance.Net` подключён в Infrastructure и транзитивно виден проектам, которые на него ссылаются.
- Конверт JSONL приватный внутри `JsonlMarketEventStore`. Схема 1. Снаружи ходят доменные события, включая биржевые идентификаторы обновлений и сделок.
- Сгенерированные экспорты памяти и графа не коммитить, пока их схема не одобрена.
