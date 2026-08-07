# Настройка поведения по завершении программы

## Проблема

Сейчас программа всегда завершается одинаково: доходит до последней ключевой
точки, `PlaybackState` уходит в `Completed`. Нужна возможность настроить, что
происходит по завершении, на уровне программы (сохраняется вместе с ней):

1. **Завершение** (текущее поведение) — опционально с возвратом в начальную
   позицию.
2. **Начать с начала (по циклу)** — программа выполняется заново с первой
   точки; количество повторов 2–50 либо неограниченно.
3. **В обратном порядке (пинг-понг)** — вперёд, затем назад по тем же точкам;
   количество повторов (пар вперёд+назад) 1–50 либо неограниченно.

Галочка «Встать в начальную позицию» — общая для всех трёх режимов, срабатывает
один раз, в самом конце, только при естественном завершении (не при ручном
«Стоп»).

## Модель данных

`JibProgram` (`ArctZ/Services/Program/JibProgram.cs`) получает новое поле и
новый enum:

```csharp
public enum ProgramCompletionMode { Stop, Loop, PingPong }

public sealed class JibProgram
{
    ...
    public ProgramCompletionMode CompletionMode { get; set; } = ProgramCompletionMode.Stop;
    public bool ReturnToStartOnFinish { get; set; } = false;
    public int? RepeatCount { get; set; } = null; // null = неограниченно; используется только в Loop/PingPong
}
```

Диапазоны (валидируются в UI-редакторе, не в модели):
- `Loop`: `RepeatCount` в `[2, 50]` либо `null`.
- `PingPong`: `RepeatCount` в `[1, 50]` либо `null`.
- `Stop`: `RepeatCount` не используется (может хранить последнее введённое
  значение — неважно).

Обратной совместимости для старых сохранённых `.json`-файлов заниматься не
нужно: `JsonFileProgramStorage` использует `System.Text.Json` с
`PreferredObjectCreationHandling = Populate`, отсутствующие в файле поля
получают значения по умолчанию из инициализаторов (`Stop`/`false`/`null`).

## UI

### Точка входа

Новый пункт `MenuItem Header="Настройки завершения"` в существующем
`MenuFlyout` программы (`ArctZ/Views/MainView.axaml`, между «Сохранить» и
«Библиотека»), вызывающий команду `EditCompletionSettingsCommand`.

### Модальное окно

Новый оверлей-редактор по образцу существующего `KeyPointEditor`
(`IsVisible="{Binding IsEditingCompletionSettings}"`, `Border` со
`HudScrimBrush`, дочерний `Border` по центру):

- Выбор режима: три `RadioButton` (Stop / Loop / PingPong) с русскими
  подписями «Завершение» / «По циклу» / «В обратном порядке».
- Под выбором режима, видимо только при `Loop`/`PingPong`:
  - `TextBox` «Количество повторов» (целое число).
  - `CheckBox` «Неограниченно» — когда включён, `TextBox` повторов
    `IsEnabled="False"`.
- Ниже, вне зависимости от режима: `CheckBox` «Встать в начальную позицию по
  завершении».
- Кнопки «Отмена» / «Сохранить» — как у остальных модалок.

### Новая ViewModel

`CompletionSettingsViewModel` (по образцу `KeyPointEditorViewModel`):

```csharp
public partial class CompletionSettingsViewModel : ViewModelBase
{
    [ObservableProperty] private ProgramCompletionMode _mode;
    [ObservableProperty] private int _repeatCount;       // отображаемое значение, когда не unlimited
    [ObservableProperty] private bool _isRepeatUnlimited;
    [ObservableProperty] private bool _returnToStartOnFinish;

    // при смене Mode — клампинг RepeatCount в допустимый для нового режима диапазон
    // Save: клампинг ещё раз на всякий случай, затем колбэк с (Mode, RepeatCount презентация -> int?, ReturnToStartOnFinish)
}
```

`ProgramViewModel` хранит текущие значения как обычные поля (аналогично
`ProgramName`), участвующие в `BuildProgram()`/`LoadProgramAsync()`/
`NewProgram()`:

```csharp
[ObservableProperty] private ProgramCompletionMode _completionMode = ProgramCompletionMode.Stop;
[ObservableProperty] private bool _returnToStartOnFinish;
[ObservableProperty] private int? _repeatCount;
```

`EditCompletionSettingsCommand` создаёt `CompletionSettingsViewModel` из
текущих полей `ProgramViewModel`; Save-колбэк записывает их обратно и
закрывает оверлей (как `ApplyKeyPointEdit`).

## Исполнение (`ProgramViewModel.PlayAsync`)

### Один проход

Текущий инлайновый блок диспатча и ожидания ack по шагам (строки ~858–920 в
исходном файле — компиляция, установка `TotalSegments`/анимации, цикл
`foreach (var (step, completion) in dispatched)`) выносится в приватный метод:

```csharp
private async Task<bool> RunPassAsync(IReadOnlyList<CompiledStep> steps, bool backward)
```

Обязанности `RunPassAsync`:
- Сбрасывает состояние анимации/прогресса на начало этого прохода
  (`_animActive`, `_visualStepIndex`, `CurrentSegmentIndex = null`,
  `SegmentProgress = 0`) — так прогресс-бар идёт 0→100% на **каждый** проход,
  а не через весь многопроходный запуск.
- Запоминает направление (`_currentPassBackward = backward`) — используется
  `CurrentlyExecutingKeyPointId` для правильной подсветки точки при обратном
  движении (индекс исполняемой точки в обратном проходе считается от конца
  списка, а не от начала).
- Диспатчит все шаги прохода, ждёт acks по очереди — логика 1:1 как сейчас
  (проверка `PlaybackState == Stopped`, установка `Faulted` +
  `FaultedAtSegmentIndex` при неудачном ack).
- Возвращает `false`, если проход не доведён до конца (Stop/Fault) — вызывающий
  код в этом случае просто возвращается, состояние уже выставлено внутри.

### Обратный проход (PingPong)

Компилируется один раз перед стартом наравне с прямым набором:

```csharp
var reversedProgram = new JibProgram { Id = program.Id, Name = program.Name };
reversedProgram.KeyPoints.AddRange(program.KeyPoints.AsEnumerable().Reverse());
var backwardSteps = _compiler.Compile(reversedProgram);
```

`TrajectoryCompiler` не меняется: каждая точка при развороте по-прежнему incoming
использует свои собственные `FeedRateUnitsPerMin`/`Ease`/`DwellSeconds` — это
и есть «шаги выполняются в обратном порядке».

### Внешний цикл повторов

```csharp
var cycle = 0;
while (true)
{
    if (!await RunPassAsync(forwardSteps, backward: false)) return;

    if (CompletionMode == ProgramCompletionMode.PingPong)
    {
        if (!await RunPassAsync(backwardSteps!, backward: true)) return;
    }

    cycle++;

    var isLastCycle = CompletionMode == ProgramCompletionMode.Stop
        || (RepeatCount is int n && cycle >= n);
    if (isLastCycle) break;

    if (CompletionMode == ProgramCompletionMode.Loop)
    {
        if (!await RunReturnToStartMoveAsync()) return; // неявный переезд к первой точке между проходами Loop
    }
}
```

`RunReturnToStartMoveAsync` — маленький хелпер, переиспользующий формат G-code
из существующего `MoveMachineToKeyPointAsync` (G1 к `KeyPoints[0].Pose` на
`KeyPoints[0].FeedRateUnitsPerMin`), ждёт ack, проверяет `Stopped`/ack-failure
так же, как основной цикл.

### Финал

После выхода из цикла (`PlaybackState == Running`):

1. `await WaitForMotionToFinishAsync()` — как сейчас, дожидается реальной
   физической остановки после последнего продиктованного движения.
2. Если `PlaybackState != Running` — возврат (Stop/Fault случились во время
   ожидания).
3. Если `ReturnToStartOnFinish` — диспатч ещё одного G1 к первой точке, ждём
   ack, при неудаче/Stop — возврат; затем ещё раз
   `await WaitForMotionToFinishAsync()`.
4. `PlaybackState = PlaybackState.Completed`.

Галочка «Встать в начальную позицию» **не** срабатывает при ручном «Стоп» —
только на естественном завершении цикла(ов), что соответствует уже
существующему различию между `Completed` и `Stopped` в кодовой базе.

### Что не меняется

- `TrajectoryCompiler`, `CompiledStep`, `ProgramSegment`, `KeyPoint` — без
  изменений.
- `PauseAsync`/резюм с паузы — не затрагивается: пауза удерживает станок через
  `FeedHold` независимо от того, в каком проходе/повторе идёт выполнение;
  `PlayAsync`'s early-return для `Paused → Running` остаётся перед компиляцией
  шагов, как сейчас.
- `StopAsync` — без изменений; уже прерывает `RunPassAsync` через проверку
  `PlaybackState == Stopped` после каждого ack.

## Не входит в объём (сознательно)

- Индикатор «повтор N из M» в UI прогресса — не запрашивался, не добавляется
  (YAGNI); при необходимости — отдельная небольшая доработка поверх этого
  дизайна.
- Миграция/версионирование JSON-файлов программ — не нужна благодаря дефолтам
  в инициализаторах свойств.
