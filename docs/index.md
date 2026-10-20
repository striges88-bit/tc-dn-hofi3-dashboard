# Индекс

Одна строка на источник. Открывать файл из нужной строки, не соседние папки.

| Файл | Зачем | Когда открывать |
| --- | --- | --- |
| [`README.md`](../README.md) | Назначение и как собрать/проверить | Первый заход в репозиторий |
| [`STATUS.md`](../STATUS.md) | Текущая точка и что не доказано | Перед продолжением работы |
| [`AGENTS.md`](../AGENTS.md) | Режим дискуссии и инварианты | Уже в контексте; не дублировать в ответ |
| [`docs/architecture.md`](architecture.md) | Диаграмма C4: контекст, контейнеры, компоненты. Infrastructure ссылается на Application | Перед сменой ссылки между проектами или пайплайна |
| [`docs/decisions/timeline.md`](decisions/timeline.md) | Хронология решений и почему | Перед тем как отменить или обойти решение |
| [`docs/decisions/`](decisions/) | Полный текст одного ADR | Только номер, который назвал таймлайн |
| [`TC-DN-HOFI3.md`](../TC-DN-HOFI3.md) | Канон формулы | Вопрос или правка формулы |
| [`docs/formulas.md`](formulas.md) | Короткий пересказ формулы | Быстрая сверка; при расхождении верен `TC-DN-HOFI3.md` |
| [`docs/data-sources.md`](data-sources.md) | Hot path и медленный контекст | Смена входов Binance |
| [`docs/configuration.md`](configuration.md) | Смысл `appsettings` | Смена настроек |
| [`skills/binance-indicator-dev/SKILL.md`](../skills/binance-indicator-dev/SKILL.md) | Правила реализации | Пайплайн, книга, JSONL, формула, WPF, тесты |
| `tools/LiveDryRun` | Replay того же пайплайна без окна | Проверка на Linux. Ограничения облака — раздел Cursor Cloud в [`AGENTS.md`](../AGENTS.md) |
| [`tasks/todo.md`](../tasks/todo.md) | Рабочий журнал | Верхний незакрытый блок. Файл целиком не читать |
| [`tasks/lessons.md`](../tasks/lessons.md) | Инциденты и правила после них | Перед рискованной правкой и после исправления |
| [`docs/thread-checkpoints-2026-10-02.md`](thread-checkpoints-2026-10-02.md) | Снимок продуктовых решений | Когда таймлайн даёт ссылку. Это не замена `STATUS.md` |
| [`docs/superpowers/`](superpowers/) | Старые планы и исходная спецификация | Только если задача называет конкретный план |
| [`scripts/README.md`](../scripts/README.md) | Какие скрипты запускать | Нужна команда обслуживания |
| [`docs/memory/README.md`](memory/README.md) | Память агента, не runtime приложения | Только если задача про память. Остальное в `docs/memory/` не читать |
| [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) | Что уже проверяет CI | Вопрос, какая проверка механическая |
