# Инструментарий PRJCT-INDICv2

## Проверка

Команды полного цикла находятся в [AGENTS.md](../AGENTS.md#команды); CI выполняет ту же последовательность на Windows. Используйте один SDK на весь цикл. `global.json` допускает новый feature band .NET 10; локальный `.dotnet/dotnet.exe` необязателен. Тесты используют VSTest, миграция на Microsoft.Testing.Platform не выполнялась.

`--no-restore` допустим только с актуальными `obj/project.assets.json` для выбранного SDK, TFM, конфигурации и RID. Restore нужен после чистого checkout/очистки obj, изменения ссылок, `Directory.Packages.props`, NuGet sources/config, SDK/TFM/RID или других restore inputs. При сомнении повторите restore. Успешный старый restore не подтверждает сегодняшнюю доступность vulnerability metadata.

`--no-build` требует успешной сборки текущих исходников и генераторов для той же конфигурации, SDK, TFM и RID. После изменения кода или build inputs повторите сборку. Результаты тестов можно использовать повторно только пока относящиеся к ним артефакты и условия не изменились.

Сохраняйте полный stdout и stderr, исходный exit code и TRX в игнорируемом `TestResults`. Минимальный вывод не отбрасывает ошибки; `--tl:off` выключает terminal logger. Пример сохранения одной команды в PowerShell:

```powershell
New-Item -ItemType Directory -Force TestResults | Out-Null
dotnet build CryptoIndicatorApp.sln --configuration Release --no-restore --verbosity minimal --tl:off 1> TestResults/build.stdout.log 2> TestResults/build.stderr.log
$taskExit = $LASTEXITCODE
$taskExit | Set-Content TestResults/build.exit-code
# Прочитайте оба журнала. В автоматическом скрипте завершитесь с exit $taskExit.
```

CI настроен сохранять объединённые журналы обоих потоков, exit codes и TRX как artifact `verification` на семь дней, включая неуспешный запуск. Новый удалённый запуск ещё не выполнялся. `--verbosity diagnostic` и отключение build servers нужны только для конкретной диагностики. Запущенное WPF-приложение может держать файлы независимо от серверов сборки.

## NuGet

[Directory.Packages.props](../Directory.Packages.props) хранит восемь существующих версий для 25 прямых ссылок. В `.csproj` остаются имена и метаданные ссылок, включая `PrivateAssets=compile` для Binance.Net в Infrastructure. Transitive pinning не включён. Миграция не обновляет пакеты и не ослабляет NuGet audit или предупреждения.

## Локальное испытание Roslyn

Кандидат — [MadQ.RoslynMcp 0.8.1-beta](https://github.com/MadQ/RoslynMcp/releases/tag/v0.8.1-beta), сторонний предварительный выпуск. Версия закреплена в [manifest](../.config/dotnet-tools.json). Восстановление из корня:

```powershell
dotnet tool restore --tool-manifest .config/dotnet-tools.json
```

Запуск: `dotnet tool run madq-roslynmcp -- --workspace sdk` из корня. Настройка только в проектном [`.codex/config.toml`](../.codex/config.toml), без глобального `setup` и hooks. `enabled=true`, сервер необязателен (`required=false`), startup/tool timeout — 30/120 секунд. Реальные вызовы из Codex Desktop проверены после перезапуска 2026-10-29. SDK workspace должен действительно загрузить `.csproj` и ссылки; один флаг или source-only Adhoc не доказывают корректную семантику. [Режимы workspace](https://github.com/MadQ/RoslynMcp/blob/v0.8.1-beta/docs/reference/WORKSPACE_MODES.md).

Только семь операций входят в allowlist: `roslyn_info`, `roslyn_get_project_info`, `roslyn_find_implementations`, `roslyn_find_references`, `roslyn_get_symbol_info`, `roslyn_get_member_body`, `roslyn_get_diagnostics`. Предложенный `roslyn_check_drift` отсутствует в закреплённом пакете и исключён; актуальность проверена напрямую. Это подмножество согласованного набора, без дополнительных прав. Allowlist ограничивает доступные Codex операции, а не системные права стороннего процесса. Сервер возвращает собственные workflow-подсказки; они не отменяют AGENTS. Сборку/тесты выполнять через обычный `dotnet`, hooks не устанавливать.

Приёмка включает загрузку решения, обе производственные реализации `IMarketEventSource`, ссылки через слои, partial-класс, изменение и добавление файла в игнорируемом примере. Реальный вызов из Codex Desktop — отдельный обязательный gate. Видимость сгенерированных WPF-членов записывается отдельно; GUI/measure/arrange отложены.

**Локальный протокольный пилот: PASS; Desktop: PASS.** Пакет восстановлен; `roslyn_info` подтвердил `0.8.1-beta+80b3faca06ace1b29bf348716a4297774b4f7e27`, SDK найден через PATH. Application и Infrastructure загрузились как MSBuildWorkspace, TFM `net10.0`, C# 14, nullable Enable; разрешён Binance.Net 12.12.0. Найдены обе производственные реализации `IMarketEventSource` и тестовая, а также 11 ссылок без следующей страницы. Внешний `IBinanceRestClient` разрешён как namedtype, diagnostics Application — 0 errors/0 warnings.

После перезапуска native-каталог Codex Desktop предоставил ровно семь операций allowlist. Реальные `roslyn_info`, `roslyn_get_project_info` с точным Application.csproj этого checkout и `roslyn_find_implementations` подтвердили закреплённую версию, MSBuildWorkspace=true, net10.0/C# 14 и все три реализации порта без следующей страницы. Полные ответы и список доступных операций сохранены в `TestResults/tooling-20261029/desktop-native.json`. Это отдельная проверка Desktop, а не повтор протокольного smoke.

Временный Probe с собственным `.slnx` в игнорируемом TestResults подтвердил обе части partial-класса, изменение Value 1 → 2 и новый AddedProbe с первого запроса после 500 мс ожидания watcher. После теста исходный Value восстановлен, добавленный файл удалён. Собственное решение нужно, чтобы загрузчик не выбрал родительский `.sln`, в который Probe не включён. Проверяйте имя реально загруженного проекта через get_project_info.

Существующее WPF-поле ChartCanvas найдено в `obj/Debug/net10.0-windows/MainWindow.g.cs`. Это доказательство видимости одного уже сгенерированного члена при текущих артефактах, не гарантия генерации после clean и не GUI-приёмка.

Ответы, stderr, exit codes и receipts сохранены в `TestResults/tooling-20261029/roslyn*`. Примеры одного запуска: handshake 381 мс/180 байт, tools/list 10 мс/88 459 байт; изменённый файл 47 мс/342 байта, новый — 597 мс/380 байт (без времени ожидания watcher). Это не доказательство ускорения разработки или экономии токенов.

При новом подключении или изменении версии повторите узкую Desktop-проверку: семь операций, roslyn_info, get_project_info с точным `.csproj` текущего checkout и find_implementations. При ошибке обязательного сценария отключите только эту секцию (`enabled=false`) и сохраните причину; другой сервер автоматически не подключать. Удаление проектной секции и manifest отменяет подготовку; глобальный Microsoft LSP не менялся.

Глобальные конфиги, Context7, CSharpier, AOT/trimming, общий Windows TFM и архитектурные библиотеки в этот объём не входят. Распространение Roslyn на другие проекты требует отдельного решения после приёмки.
