# Implementation contract v2: lint на build и allow-list зависимостей слоёв

- **Дата:** 2026-10-25
- **Статус:** план одобрен Стивеном в обсуждении; код не изменён. Commit/push/PR — только по отдельному явному «да» на этот объём. Merge — только после отдельного прямого вопроса и «да» именно на merge.
- **Baseline:** `main`, commit `77c5695`
- **Ветка:** `chore/build-architecture-gates`
- **Заменяет:** `C:\Users\Steven Owl\Documents\test\docs\tasks\prjct-indic-quality-gates-contract.md` (v1)

## 1. Цель

Стивен назвал две проблемы:

1. Ошибки стиля видны только в CI после `dotnet format`, а не в `dotnet build` и не в IDE.
2. Агенты нарушают стиль и тянут в Domain/Application лишние зависимости.

Готово, когда:

- `dotnet build` падает на `if` без фигурных скобок, на неявном модификаторе доступа и на неверном порядке модификаторов;
- тест падает, если Domain ссылается на что-то кроме фреймворка, а Application — на что-то кроме фреймворка и Domain.

## 2. Почему v2, а не v1

| v1 | Факт в репозитории | Решение v2 |
|---|---|---|
| Новый проект `CryptoIndicatorApp.Architecture.Tests` с 4 тестами | Уже есть `ApplicationBoundaryTests` (Application ↛ Infrastructure), `BinanceBoundaryTests` (Domain ↛ Binance.Net), `HostCompileBoundaryTests` (2 проверки csproj) | Новый проект не создавать. Добавить allow-list в `ApplicationBoundaryTests.cs` |
| Domain→App/Infra/Desktop, App→Desktop, Infra→Desktop | Ссылка дала бы цикл ProjectReference (NU1108) или `net10.0` → `net10.0-windows` (NU1201). Это вывод из документации NuGet, локально не воспроизводился | Allow-list закрывает эти случаи сам, от NU1108/NU1201 план не зависит |
| `UseWPF` в тестовом проекте | По `AGENTS.md` (Cursor Cloud) WPF-тесты на Linux не запускаются | Тест остаётся в `net10.0`-проекте Application.Tests и идёт на Linux |
| Пробел: Application → Binance.Net и любой другой пакет | Сейчас не проверяется | Закрывает allow-list |
| `dotnet_diagnostic.IDE0055.severity = warning` | CI уже запускает `dotnet format --verify-no-changes`: whitespace + style, по умолчанию severity `warn` | Не добавлять, чтобы форматирование не проверялось дважды |
| `EnforceCodeStyleInBuild` + IDE0011/0036/0040 | Формат `option:warning` соблюдается на build с .NET 9. Эвристика по 94 `.cs` показала, что нарушений почти нет | Оставить |

## 3. Инварианты исполнения

1. Прочитать `AGENTS.md` и этот контракт. Работать из корня репозитория.
2. Не менять формулу TC-DN-HOFI3, пороги, фильтры, шаг расчёта, live/replay, production `.csproj`, `.sln`, `.github/workflows/ci.yml`.
3. Не добавлять пакеты и инструменты: CSharpier, NetArchTest, ArchUnitNET, Mono.Cecil.
4. Не добавлять `NoWarn`, `#pragma warning disable`, suppressions и не пинить `AnalysisLevel` (`.cursor/rules/dotnet-build-hygiene.mdc`).
5. Существующие тесты границ не удалять и не переименовывать.
6. Negative controls не попадают в коммиты.

## 4. Коммиты

Три коммита в одном PR. После каждого коммита restore, format, build и test зелёные.

| # | Сообщение | Файлы | Почему отдельно |
|---|---|---|---|
| 1 | `docs: add build and architecture gates contract` | `docs/superpowers/plans/2026-10-25-build-architecture-gates.md` | Docs-коммит не трогает код (`.cursor/rules/one-pr-one-scope.mdc`) |
| 2 | `build: enforce selected IDE code-style rules on build` | `Directory.Build.props`, `.editorconfig`, точечные правки стиля, если build их потребует | Правила и правки идут вместе: без правок коммит красный |
| 3 | `test: restrict Domain and Application references to an allow-list` | `CryptoIndicatorApp.Application.Tests/ApplicationBoundaryTests.cs` | Независимый slice. Идёт после коммита 2, чтобы новый тест уже прошёл новые правила |

Если в коммите 2 нужны правки стиля больше чем в ~10 местах, правки идут отдельным коммитом `style: add braces and accessibility modifiers` перед включением правил. Тогда коммиты разделены так: `style:` — только код, `build:` — только конфигурация.

## 5. Коммит 2 — lint на build

### 5.1. Замер до

```powershell
Measure-Command { dotnet build CryptoIndicatorApp.sln --configuration Release --no-incremental -tl:off -v:quiet } | Select-Object TotalSeconds
```

### 5.2. `Directory.Build.props`

В существующую `PropertyGroup` после `<AnalysisMode>Default</AnalysisMode>`:

```xml
<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
```

Остальные свойства не менять.

### 5.3. `.editorconfig`, секция `[*.cs]`, в конец

```editorconfig
csharp_prefer_braces = true:warning
csharp_preferred_modifier_order = public,private,protected,internal,file,static,extern,new,virtual,abstract,sealed,override,readonly,unsafe,required,volatile,async:warning
dotnet_style_require_accessibility_modifiers = always:warning
```

| Правило | Что ловит |
|---|---|
| `IDE0011` | statement без фигурных скобок |
| `IDE0036` | порядок модификаторов; список равен дефолту Roslyn, меняется только severity |
| `IDE0040` | неявные модификаторы доступа |
| `IDE0161` | file-scoped namespace; уже настроен и теперь тоже проверяется на build |

Строки formatting/usings и комментарий в шапке файла не менять.

### 5.4. Нарушения

1. `dotnet build CryptoIndicatorApp.sln --configuration Release -tl:off` и собрать список `IDE0011/IDE0036/IDE0040/IDE0161`.
2. Исправить только правками стиля: добавить скобки или модификатор, переставить модификаторы. Поведение не менять.
3. Стоп: больше ~10 мест или правка, которая меняет поведение. Тогда вернуть Стивену список и не расширять объём.

### 5.5. Negative control

1. В любом тесте временно заменить блок на `if (true)` + строку `Assert.True(true);` без скобок.
2. `dotnet build ... -tl:off` должен упасть с `error IDE0011`.
3. Откатить файл: `git checkout -- <file>`. Повторить build и получить 0 warnings, 0 errors.

### 5.6. Замер после

Повторить 5.1. Если время выросло больше чем на 20%, остановиться и показать обе цифры Стивену до коммита.

## 6. Коммит 3 — allow-list

### 6.1. Изменение

В `CryptoIndicatorApp.Application.Tests/ApplicationBoundaryTests.cs` существующий тест оставить и добавить:

```csharp
using System.Reflection;
using CryptoIndicatorApp.Application.Sessions;
using CryptoIndicatorApp.Domain.MarketData;

// ...

    [Fact]
    public void DomainAssemblyReferencesOnlyTheFramework()
    {
        Assert.Empty(UnexpectedReferences(typeof(DepthUpdateEvent).Assembly));
    }

    [Fact]
    public void ApplicationAssemblyReferencesOnlyDomainAndTheFramework()
    {
        Assert.Empty(UnexpectedReferences(typeof(ReplayIndicatorSession).Assembly, "CryptoIndicatorApp.Domain"));
    }

    private static string[] UnexpectedReferences(Assembly assembly, string? allowedProject = null)
    {
        return assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal)
                && name != "netstandard"
                && name != allowedProject)
            .ToArray();
    }
```

Порядок `using` подогнать под соседние файлы. Последнее слово за `dotnet format --verify-no-changes`.

Если тест упадёт на легитимной сборке фреймворка (например `Microsoft.Win32.*`), расширить префиксы фреймворка в allow-list и указать это в PR. Не ослаблять проверку до `DoesNotContain`.

### 6.2. Negative control

1. Временно передать `null` вместо `"CryptoIndicatorApp.Domain"` во втором тесте.
2. `dotnet test CryptoIndicatorApp.Application.Tests --configuration Release -tl:off` должен упасть, и в выводе должна быть `CryptoIndicatorApp.Domain`.
3. Откатить и повторить тест: зелёный.

### 6.3. Ограничение

`GetReferencedAssemblies` видит только сборки, которые реально используются в IL. Добавленный, но неиспользуемый пакет тест не заметит. Типы такой пакет не протаскивает, поэтому это приемлемо.

## 7. Финальная проверка (шаги CI из `AGENTS.md`)

```powershell
dotnet restore CryptoIndicatorApp.sln
dotnet format CryptoIndicatorApp.sln --verify-no-changes
dotnet build CryptoIndicatorApp.sln --configuration Release -tl:off
dotnet test CryptoIndicatorApp.sln --configuration Release --no-build
git diff --stat main...HEAD
git status --short
```

Приёмка:

- все команды завершаются с exit 0; build: 0 warnings, 0 errors;
- solution tests: 0 failed; тестов на 2 больше, чем до изменений (baseline снять командой перед стартом, не брать из `STATUS.md`);
- `git diff --stat` содержит только файлы из раздела 4;
- оба negative control выполнены, их следов нет в diff;
- время build до и после записано.

## 8. PR

- Заголовок: `chore: enforce code style on build and restrict layer references`.
- Тело: ссылка на этот контракт; список diagnostics и правок стиля (или «правок не потребовалось»); вывод обоих negative control; время build до и после; точные результаты команд из раздела 7.
- Указать, что не проверено: GUI, live Binance, Linux/Cursor Cloud.
- Дождаться CI. Merge не делать.

## 9. Вне объёма

- Удаление `ApplicationBoundaryTests.ApplicationAssemblyDoesNotReferenceInfrastructure` и `BinanceBoundaryTests`, хотя allow-list их покрывает: делается отдельным решением.
- Обновление xUnit, Test.Sdk, coverlet.
- Naming, `var`, collection expressions и другие IDE-правила.
- Allow-list для Infrastructure, Desktop и LiveDryRun.
- Изменения CI, ADR, `docs/architecture.md`.

## 10. Источники

- `dotnet format`: severity по умолчанию `warn`, `--verify-no-changes` — https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-format
- Code-style analysis on build, формат `option:severity` с .NET 9 — https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview
- `EnforceCodeStyleInBuild` — https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props
- IDE-правила, которые работают на build — https://github.com/dotnet/roslyn/blob/main/src/Analyzers/Core/Analyzers/EnforceOnBuildValues.cs
- Существующие тесты границ: `CryptoIndicatorApp.Application.Tests/ApplicationBoundaryTests.cs`, `CryptoIndicatorApp.Infrastructure.Tests/BinanceBoundaryTests.cs`, `CryptoIndicatorApp.Infrastructure.Tests/HostCompileBoundaryTests.cs`
