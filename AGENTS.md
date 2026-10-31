# AGENTS.md

Карта, не справочник. Дальше открывать только строку из [`docs/index.md`](docs/index.md), которая относится к задаче. Правила этого файла важнее общих инструкций, если они расходятся.

## Дискуссия

Читать и предлагать можно. Менять файлы, коммитить и пушить нельзя, пока Стивен явно не одобрил конкретный объём, например «делай» или «имплементируй» на этот объём. Одобрение покрывает только названный объём.

Merge — отдельно: `git merge`, `gh pr merge` и любой merge PR только после отдельного прямого вопроса и явного «да» именно на merge. Одобрение на правку, коммит, push, PR или зелёный CI merge не покрывает.

## Репозиторий

Вопрос про сам репозиторий — зачем файл, как собрано, какая история — отвечать из git (`log`, `blame`, история) и файлов репозитория. Коннекторы (Notion, Google Docs/Drive, Granola, встречи, GitHub issues) открывать только если файл в репозитории ссылается на этот источник или Стивен явно его назвал. Отдельный субагент на каждый источник не запускать.

## Идентичность и текущий этап

- Этот checkout — основной проект `PRJCT-INDICv2`. Старый `PRJCT-INDIC` и его чаты — отдельный проект; Стивен намерен его архивировать. Не переносить оттуда текущие задачи, разрешения или доказательства без проверки применимости.
- Engram этого checkout: `prjct-indic-v2`, привязка в [`.engram/config.json`](.engram/config.json). Адрес `prjct-indic` остаётся за старым проектом; прежние записи не мигрировать автоматически. Перед записью сверять имя и путь текущего проекта.
- Текущий этап — базовая подготовка и ознакомление. Актуальная задача находится в [верхнем блоке журнала](tasks/todo.md#active-task); история ниже не является очередью работ.
- WPF-компоновка, визуальная приёмка, четырёхцветный график и replay контекста ликвидаций/OI отложены. Возможная переработка формулы и архитектуры требует отдельного согласованного дизайна; действующие инварианты сохраняются.

## Инварианты

- Это аналитика. Автоматическая торговля, выставление заявок и исполнение портфельного риска не добавлять без отдельного одобренного дизайна.
- Формулу TC-DN-HOFI3, пороги, фильтры и шаг расчёта не менять без явного одобрения.
- Слои: Domain, Application, Infrastructure, Desktop. Application зависит только от Domain и не ссылается на Infrastructure. Infrastructure ссылается на Application: адаптеры портов лежат в `Composition`, их вызывают Desktop и `tools/LiveDryRun`. Это [решение 0009](docs/decisions/0009-infrastructure-references-application.md). Границы: [`docs/architecture.md`](docs/architecture.md).
- Live и replay подают одни и те же внутренние события в один индикаторный пайплайн.

## Проверка

Изменение поведения сопровождается самым узким тестом и командой, которой его прогнали. CI на каждом pull request и на push в `main` собирает решение и гоняет тесты: [`.github/workflows/ci.yml`](.github/workflows/ci.yml). В ответе указать, что не проверено.

Сборка роняет нарушения IDE0011, IDE0036 и IDE0040: [`.editorconfig`](.editorconfig) задаёт их на `warning`, а `TreatWarningsAsErrors` превращает в ошибки. Границы слоёв проверяют allow-list тесты [`ApplicationBoundaryTests.cs`](CryptoIndicatorApp.Application.Tests/ApplicationBoundaryTests.cs).

### Команды

```powershell
dotnet restore CryptoIndicatorApp.sln
dotnet format CryptoIndicatorApp.sln --verify-no-changes
dotnet build CryptoIndicatorApp.sln --configuration Release -tl:off
dotnet test CryptoIndicatorApp.sln --configuration Release --no-build
```

Это те же шаги, что и в CI. `-tl:off` — читаемый вывод вместо terminal logger.

## Куда идти

| Зачем | Куда |
| --- | --- |
| Куда смотреть дальше | [`docs/index.md`](docs/index.md) |
| Почему решение принято | [`docs/decisions/timeline.md`](docs/decisions/timeline.md) |
| Как менять пайплайн, формулу, replay или UI | [`skills/binance-indicator-dev/SKILL.md`](skills/binance-indicator-dev/SKILL.md) |
| Что уже ломалось | [`tasks/lessons.md`](tasks/lessons.md) |
| На чём остановились | [`STATUS.md`](STATUS.md) |

В `docs/memory/` оставлены `glossary.md`, `rules.md` и `entities.md`. Не открывать их, если задача не про эти файлы. Перед push, PR или правкой ADR/формулы открыть строку Memory в индексе.

## Cursor Cloud

Облачный агент — Linux. `dotnet` в этом окружении выставляет `EnableWindowsTargeting=true`, иначе проекты `net10.0-windows` не восстанавливаются. `global.json` требует SDK 10. Окно WPF и `CryptoIndicatorApp.Desktop.Tests` здесь не запускаются: рантайма `Microsoft.WindowsDesktop.App` для Linux нет. Живой Binance из этого региона отвечает `restricted location`; локальный replay идёт через `tools/LiveDryRun`. `powershell.exe` — это PowerShell 7. Узкая проверка: `dotnet test` для Domain и Application, затем `dotnet run --project tools/LiveDryRun -- --symbol BTCUSDT --input <jsonl> --replay-only`.
