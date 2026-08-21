# Лог исполнения программы: файл событий в памяти + копирование из «О программе»

## Контекст

`ProgramViewModel` уже ведёт два независимых слоя прогресса выполнения программы:

- **Ack-слой** (`CurrentSegmentIndex`, `SegmentProgress`, `OverallProgress`,
  `CurrentlyExecutingKeyPointId`) — обновляется по подтверждению (`ok`) контроллером приёма
  G-code строки в буфер. Ack не означает, что движение физически завершилось — контроллер может
  принять несколько строк вперёд, пока станок ещё едет по предыдущей.
- **Физический слой** (`TimeProgressTracker`, `PhysicalOverallProgress`,
  `PhysicalPointRemainingFraction`, `PhysicallyExecutingKeyPointId`,
  `PhysicalPointHasTimeWarning`) — источник истины по реальной позиции станка (`DeviceStatus.WPos`,
  см. `project_status_parser_mpos_fix`), обновляется по статус-репортам (~100мс) и по таймеру
  200мс (`_progressTimer`). Прогресс внутри трекера измеряется временем, не дистанцией (Revision 2
  `2026-08-19-physical-program-progress-design.md`): доля прошедшего времени точки против
  расчётной (`EstimatedDurationSeconds` суммы шагов сегмента).
- Уже существует edge-triggered сигнал превышения расчётного времени точки более чем на 15%
  (`TimeProgressTracker.SegmentTimeOverage`, обработчик `ProgramViewModel.OnSegmentTimeOverage`) —
  сейчас он только кладёт предупреждение в `KeyPointMessage` конкретной точки.

`AboutViewModel`/«О программе» уже умеет собирать диагностический снапшот (`DiagnosticsReportBuilder`,
`ReportText`, копирование через `TopLevel.Clipboard` в `MainView.axaml.cs`,
`OnCopyDiagnosticsClick`) — новая кнопка копирования лога программы следует этому же образцу.

Задача: во время выполнения программы вести текстовый лог событий (начало/окончание движения к
точке, начало/окончание паузы, рассинхронизация между слоями), с текущим значением обоих
прогресс-показателей на каждое событие, и дать возможность скопировать лог последнего запуска
через кнопку в диалоге «О программе».

## Требования

1. Лог создаётся заново при каждом «холодном» старте программы (нажатие «Пуск» из состояния,
   отличного от возобновления после паузы) и хранится **в памяти**, без записи на диск и без
   отдельного per-platform хранилища — переживает завершение прогона, перезаписывается только
   следующим холодным стартом.
2. События, которые пишутся в лог:
   - Начало движения к точке / окончание движения к точке — определяются по переходу физически
     активного сегмента (`TimeProgressTracker`), а не по ack. Переход на новый сегмент даёт две
     соседние строки с одинаковой временной меткой: «окончание движения к точке N-1» (если была
     активная точка) и «начало движения к точке N».
   - Начало паузы / возобновление после паузы — по существующим переходам `PlaybackState` в/из
     `Paused` (`OnPlaybackStateChanged`, `_pausedAt`).
   - Рассинхронизация — двух типов, каждая своей строкой, срабатывает по фронту (не на каждый
     тик пересчёта):
     - Ack-буфер обогнал физическое положение более чем на одну точку: `CurrentSegmentIndex`
       (ack) минус `CurrentSegmentIndex` трекера (физика) `> 1`.
     - Существующий `SegmentTimeOverage` (перебор расчётного времени точки >15%) — та же
       проверка, что уже пишет предупреждение в `KeyPointMessage`, дополнительно кладёт строку в
       лог.
   - Бэкенды: «Программа запущена» (имя программы, число точек) в момент создания лога и
     «Программа завершена» (текст терминального состояния — Завершено/Остановлено/Ошибка) на
     переходе `PlaybackState` в `Completed`/`Stopped`/`Faulted`.
3. Каждая строка события несёт текущее значение общего прогресса (`PhysicalOverallProgress`) и
   прогресса текущего шага (доля прошедшего времени точки, `1 - PhysicalPointRemainingFraction`)
   в момент события.
4. Кнопка «Скопировать лог программы» в диалоге «О программе» — видна только если лог хотя бы
   одного запуска уже существует в этой сессии; копирует текст в буфер обмена тем же механизмом,
   что и существующая кнопка копирования диагностики.

## Решение

### 1. `ProgramExecutionLog` — новый класс

Новый файл `ArctZ/Services/Program/ProgramExecutionLog.cs`. Простой класс без зависимостей от
Avalonia/UI/времени системных часов (время передаётся параметром в каждый вызов — тестируемость
синтетическими метками, по образцу `TimeProgressTracker`). Копит строки во внутреннем
`List<string>`, отдаёт готовый текст через `string Text { get; }` (join по `Environment.NewLine`).

Формат строки: `[ММ:СС.ссс] <Событие> — общий N%, шаг M%`, где время — смещение от момента
создания лога (старта программы), проценты — округлённые `PhysicalOverallProgress * 100` и
шаговая доля `* 100`.

Публичный API (по одному методу на тип события — самодокументирует полноту покрытия требований):

```csharp
public ProgramExecutionLog(string programName, int keyPointCount, DateTimeOffset startedAt);

public void LogMovementEnded(string pointLabel, double overallProgress, double stepProgress, DateTimeOffset now);
public void LogMovementStarted(string pointLabel, double overallProgress, double stepProgress, DateTimeOffset now);
public void LogPauseStarted(double overallProgress, double stepProgress, DateTimeOffset now);
public void LogPauseEnded(double overallProgress, double stepProgress, DateTimeOffset now);
public void LogAckDesync(int ackSegmentIndex, int physicalSegmentIndex, double overallProgress, double stepProgress, DateTimeOffset now);
public void LogTimeOverage(string pointLabel, double actualSeconds, double estimatedSeconds, double overallProgress, double stepProgress, DateTimeOffset now);
public void LogProgramEnded(string outcomeLabel, double overallProgress, double stepProgress, DateTimeOffset now);

public string Text { get; }
```

Конструктор сразу кладёт строку «Программа запущена: «{programName}», {keyPointCount} точек».

### 2. `ProgramViewModel` — создание и наполнение

- Новое поле `private ProgramExecutionLog? _executionLog;`.
- Создаётся в `PlayAsync`, в ветке холодного старта (там же, где сейчас выставляется
  `TotalSegments = KeyPoints.Count + (ReturnToStartOnFinish ? 1 : 0);`) — **не** в ветке
  возобновления после паузы (`if (PlaybackState == PlaybackState.Paused) { ... return; }` в начале
  метода), чтобы Resume не начинал новый лог поверх текущего прогона.
- Объект **не обнуляется** по завершении прогона (ни в `ClearProgressTracker`, ни где-либо ещё) —
  так `_executionLog` всегда содержит лог последнего запуска, пока не начнётся следующий холодный
  старт. Публичное свойство `public string? ExecutionLogText => _executionLog?.Text;`.

Точки подключения событий:

- **Движение к точке.** `OnProgressTrackerChanged` (уже вызывается на каждый `Changed` трекера)
  дополняется сравнением нового `PhysicallyExecutingKeyPointId` с сохранённым предыдущим значением
  (новое приватное поле `_lastLoggedPhysicalKeyPointId`). При изменении — если предыдущее значение
  было не `null`, `LogMovementEnded` с его `Label`; затем, если новое не `null`, `LogMovementStarted`
  с его `Label`. Оба вызова используют текущие `PhysicalOverallProgress`/шаговую долю и `_now()`.
- **Пауза и завершение программы — порядок операций внутри `OnPlaybackStateChanged`.**
  `ClearProgressTracker()` (вызывается в существующей ветке `Stopped`/`Faulted`, до всех прочих
  преобразований метода) обнуляет `_progressTracker`, после чего `PhysicalOverallProgress`/шаговая
  доля падают до 0 — если логировать после этого вызова, финальная строка Stopped/Faulted покажет
  0% вместо фактических значений на момент остановки. Поэтому `OnPlaybackStateChanged` в самом
  начале (до первой мутации состояния) захватывает `overallProgress`/`stepProgress` в локальные
  переменные из ещё не тронутого `_progressTracker`, и все вызовы `_executionLog?.Log...` внутри
  этого исполнения метода используют эти захваченные локальные значения, а не читают
  `PhysicalOverallProgress` заново постфактум:
  - Ветка `if (value == PlaybackState.Paused)` (после `_pausedAt = _now();`) —
    `_executionLog?.LogPauseStarted(...)` с захваченными значениями.
  - Ветка `else if (value == PlaybackState.Running && _pausedAt is { } pausedAt)` (перед сбросом
    `_pausedAt = null`) — `_executionLog?.LogPauseEnded(...)` с захваченными значениями.
  - Ветка `if (value is PlaybackState.Completed or PlaybackState.Stopped or PlaybackState.Faulted)`
    — `_executionLog?.LogProgramEnded(StatusLabel, ...)` с захваченными значениями (для
    `Completed` `_progressTracker` не обнуляется, так что захват здесь избыточен, но одинаков для
    всех трёх терминальных состояний — не нужно различать их по этому признаку).
- **Рассинхронизация ack/физика.** В `OnProgressTrackerChanged`, наряду со сравнением для событий
  движения: если `CurrentSegmentIndex` (ack, может быть `null`) и `_progressTracker.CurrentSegmentIndex`
  оба не `null` и разница `> 1` — и до этого не была залогирована (новое приватное `bool
  _ackDesyncLogged`, сбрасывается в `false` при создании нового `_progressTracker` в `RunPassAsync`)
  — `LogAckDesync(...)`, затем `_ackDesyncLogged = true`. Когда разница возвращается к `<= 1`,
  `_ackDesyncLogged` сбрасывается в `false` без отдельной строки в лог (фиксируем только
  возникновение рассинхронизации, не её исчезновение — симметрично с уже существующим
  `SegmentTimeOverage`, который тоже не сигнализирует явное «пришло в норму»).
- **Перебор времени точки.** В существующем `OnSegmentTimeOverage(segmentIndex, actualSeconds,
  estimatedSeconds)` — рядом с уже существующим `AddKeyPointMessage(...)`: резолвим `Label` точки
  (уже есть — `keyPointId` резолвится через `TargetKeyPoint`, ищем в `KeyPoints`), вызываем
  `_executionLog?.LogTimeOverage(label, actualSeconds, estimatedSeconds, ...)`.

### 3. `AboutViewModel`/`OpenAbout` — проброс в диалог

- `AboutViewModel` получает новый конструкторский параметр `string? executionLogText`, хранит его,
  выставляет `public string ExecutionLogText { get; }` (пустая строка, если `null`) и
  `public bool HasExecutionLog => !string.IsNullOrEmpty(ExecutionLogText);`.
- По образцу `IsCopied`/`MarkCopied()` — независимый `[ObservableProperty] bool
  _isExecutionLogCopied` и `MarkExecutionLogCopied()`.
- `ProgramViewModel.OpenAbout()` передаёt `ExecutionLogText` четвёртым аргументом в конструктор
  `AboutViewModel`.

### 4. UI — кнопка в «О программе»

- В `MainView.axaml`, рядом с существующей кнопкой копирования диагностики (около строк 661-667):
  аналогичная пара `Button`/иконка/текст с `IsVisible="{Binding About.IsExecutionLogCopied}"` /
  `!About.IsExecutionLogCopied}"`, сама кнопка целиком — `IsVisible="{Binding About.HasExecutionLog}"`
  (лог ни разу не создавался в этой сессии — кнопки нет вовсе, не disabled-состояние).
- `MainView.axaml.cs` — новый `OnCopyExecutionLogClick`, копия `OnCopyDiagnosticsClick`
  (получает `about.ExecutionLogText`, ставит в `TopLevel.Clipboard`, зовёт
  `about.MarkExecutionLogCopied()`).

## Вне рамок

- Запись лога на диск — по итогам уточняющих вопросов лог хранится только в памяти, без
  персистентности между запусками приложения.
- История логов нескольких последних запусков — хранится только лог самого последнего холодного
  старта.
- Явная строка «рассинхронизация устранена» — фиксируется только возникновение, не исчезновение
  (симметрично с уже существующим поведением `SegmentTimeOverage`).
- Изменение существующего ack-слоя, `TimeProgressTracker`, `KeyPointMessage`-предупреждений — не
  трогаются, лог только читает их значения.
- Экспорт/шаринг лога (сохранение в файл, отправка) — только копирование в буфer обмена.

## Тестирование

- Юнит-тесты на `ProgramExecutionLog` (новый файл в `ArctZ.Tests/Services/Program/`):
  формат каждой строки для каждого метода, накопление нескольких строк по порядку, корректное
  смещение времени от `startedAt`.
- Расширение `ProgramViewModelPlaybackTests` (или отдельный новый файл, если текущий разрастётся):
  - Холодный старт `PlayAsync` создаёт лог с заголовком; `ExecutionLogText` не `null` сразу после
    старта.
  - Resume после паузы **не** создаёт новый лог (тот же `_executionLog`, строки накапливаются).
  - Типовой прогон через несколько точек — лог содержит парные
    `LogMovementEnded`/`LogMovementStarted` в ожидаемом порядке для каждого перехода.
  - Pause/Resume — соответствующие строки, с прогрессом на момент паузы.
  - Сценарий, воспроизводящий существующий тест на `SegmentTimeOverage` (перебор времени точки),
    дополнительно проверяет строку `LogTimeOverage` в `ExecutionLogText`.
  - Синтетический сценарий ack/физика с разницей `>1` — строка `LogAckDesync` появляется один раз
    (не дублируется на каждый последующий тик, пока рассинхронизация сохраняется).
  - `ExecutionLogText` переживает переход в `Completed`/`Stopped`/`Faulted` (лог не обнуляется по
    завершении прогона) и содержит финальную строку с корректным `StatusLabel`.
  - Stop/Fault в процессе движения к точке (до завершения прохода) — финальная строка
    `LogProgramEnded` несёт фактический прогресс на момент остановки, не 0% (регрессионный тест на
    захват значений до `ClearProgressTracker()`).
  - Новый холодный старт после завершённого прогона — `ExecutionLogText` заменяется новым логом
    (старый недоступен).
- Обязательный живой UI-тест по правилам проекта (`CLAUDE.md`, раздел «Тестирование UI»): собрать
  и запустить `ArctZ.Desktop`, прогнать программу с несколькими точками и паузой в процессе,
  открыть «О программе», подтвердить через `AskUserQuestion` по каждому пункту отдельно: кнопка
  «Скопировать лог программы» видна и содержит ожидаемые события в правильном порядке; кнопка
  отсутствует до первого запуска программы в сессии; повторный запуск программы заменяет
  предыдущий лог новым.
