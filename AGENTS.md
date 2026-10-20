# AGENTS.md

Карта, не справочник. Дальше открывать только строку из [`docs/index.md`](docs/index.md), которая относится к задаче. Правила этого файла важнее общих инструкций, если они расходятся.

## Дискуссия

Читать и предлагать можно. Менять файлы, коммитить и пушить нельзя, пока Стивен явно не одобрил конкретный объём, например «делай» или «имплементируй» на этот объём. Одобрение покрывает только названный объём.

## Репозиторий

Вопрос про сам репозиторий — зачем файл, как собрано, какая история — отвечать из git (`log`, `blame`, история) и файлов репозитория. Коннекторы (Notion, Google Docs/Drive, Granola, встречи, GitHub issues) открывать только если файл в репозитории ссылается на этот источник или Стивен явно его назвал. Отдельный субагент на каждый источник не запускать.

## Инварианты

- Это аналитика. Автоматическая торговля, выставление заявок и исполнение портфельного риска не добавлять без отдельного одобренного дизайна.
- Формулу TC-DN-HOFI3, пороги, фильтры и шаг расчёта не менять без явного одобрения.
- Слои: Domain, Application, Infrastructure, Desktop. Application зависит только от Domain и не ссылается на Infrastructure. Infrastructure ссылается на Application: адаптеры портов лежат в `Composition`, их вызывают Desktop и `tools/LiveDryRun`. Это [решение 0009](docs/decisions/0009-infrastructure-references-application.md). Границы: [`docs/architecture.md`](docs/architecture.md).
- Live и replay подают одни и те же внутренние события в один индикаторный пайплайн.

## Проверка

Изменение поведения сопровождается самым узким тестом и командой, которой его прогнали. CI на `main` собирает решение и гоняет тесты: [`.github/workflows/ci.yml`](.github/workflows/ci.yml). В ответе указать, что не проверено.

## Куда идти

| Зачем | Куда |
| --- | --- |
| Куда смотреть дальше | [`docs/index.md`](docs/index.md) |
| Почему решение принято | [`docs/decisions/timeline.md`](docs/decisions/timeline.md) |
| Как менять пайплайн, формулу, replay или UI | [`skills/binance-indicator-dev/SKILL.md`](skills/binance-indicator-dev/SKILL.md) |
| Что уже ломалось | [`tasks/lessons.md`](tasks/lessons.md) |
| На чём остановились | [`STATUS.md`](STATUS.md) |

`docs/memory/` не открывать, если задача не про память. Перед push, PR или правкой ADR/формулы открыть строку Memory в индексе.

## Cursor Cloud

Облачный агент — Linux. `dotnet` в этом окружении выставляет `EnableWindowsTargeting=true`, иначе проекты `net8.0-windows` не восстанавливаются. Окно WPF и `CryptoIndicatorApp.Desktop.Tests` здесь не запускаются: рантайма `Microsoft.WindowsDesktop.App` для Linux нет. Живой Binance из этого региона отвечает `restricted location`; локальный replay идёт через `tools/LiveDryRun`. `powershell.exe` — это PowerShell 7. Скрипты памяти, которые вызывают `where.exe`, читают `LOCALAPPDATA` или строят Windows-URI, на Linux падают. Узкая проверка: `dotnet test` для Domain, Application и Memory, затем `dotnet run --project tools/LiveDryRun -- --symbol BTCUSDT --input <jsonl> --replay-only`.
