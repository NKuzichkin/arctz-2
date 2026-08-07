# Прогресс-бар выполнения программы: плавное движение с интерполяцией по времени

## Контекст

`ProgressBar` в `MainView.axaml` (строки ~208-215) уже привязан к `OverallProgress` с
0.3с `DoubleTransition` (`CubicEaseOut`), но сама величина `OverallProgress`
(`ProgramViewModel.cs:596`) обновляется скачками — только когда контроллер
подтверждает (`ok`) очередную G-code-строку. Для линейного сегмента без
плавного въезда/выезда (`EaseMode != EaseInOut`) `TrajectoryCompiler` компилирует
весь сегмент в одну G1-строку (`TrajectoryCompiler.cs:24-27`) — значит прогресс
скачет 0%→100% за один ack, без промежуточного движения. Для `EaseInOut`-сегментов
уже есть 6 подшагов (`EaseSubdivisions`), но и это дискретные скачки, синхронизированные
не с реальным временем движения, а со скоростью прихода ack (которая зависит от
буфера контроллера, а не от физической скорости перемещения).

Причина скачков — известное и осознанное ограничение (см. `AI_AGENT_README`/память
проекта: FluidNC подтверждает G-code по приёму в буфер, а не по завершению
физического движения), и точная синхронизация с реальным движением средствами
самого протокола недоступна. Задача — визуально сгладить это через оценку по
времени, а не изменить протокол.

## Требования

1. Прогресс-бар должен непрерывно двигаться в реальном времени в течение всего
   времени выполнения сегмента, а не стоять на месте между ack'ами и скакать при
   их получении.
2. Оценка скорости движения — по расстоянию и текущей подаче (feed rate), без
   привязки к частоте ack.
3. Расхождение оценки с реальностью корректируется в момент прихода настоящего
   ack — значение "довoдится" (snap) до подтверждённого, отображаемый прогресс
   никогда не идёт назад.
4. Существующее поведение, наблюдаемое тестами (`OverallProgress`,
   `SegmentProgress`, `CurrentSegmentIndex` меняются только по ack) не меняется —
   добавляется отдельное анимированное представление для UI.

## Решение

### 1. Оценка длительности шага — `TrajectoryCompiler`/`CompiledStep`

`CompiledStep` (`CompiledStep.cs`) получает новое поле `EstimatedDurationSeconds`:

- **Обычный (не eased) сегмент** — один G1-шаг на весь сегмент:
  `EstimatedDurationSeconds = Distance(From.Pose, To.Pose) / To.FeedRateUnitsPerMin * 60`,
  где `Distance` — евклидова норма разницы по всем 4 осям (та же геометрия, что уже
  использует `Interpolate`).
- **Eased-подшаг** (`CompileEased`) — расстояние между позой на предыдущем `t` и
  текущем `t` (`Interpolate(From, To, t_prev)` → `Interpolate(From, To, t)`), делённое
  на `FeedMultiplier(t) * To.FeedRateUnitsPerMin` (тот же feed, что уже идёт в
  G-code этого подшага).
- **Dwell-шаг** (`G4`) — длительность точная, без оценки: `EstimatedDurationSeconds =
  To.DwellSeconds`.
- Если подача ≤ 0 (защитный случай) — `EstimatedDurationSeconds = 0` (см. ниже —
  нулевая длительность просто мгновенно "доводит" прогресс до цели на первом тике).

### 2. Анимированное отображение — `ProgramViewModel`

Новое `[ObservableProperty] double DisplayProgress` (0..1) — на него переключается
`Binding` в `MainView.axaml` (`ProgressBar.Value`) вместо `OverallProgress`.
`OverallProgress`/`SegmentProgress`/`CurrentSegmentIndex` не меняются — это
по-прежнему дискретная, подтверждённая ack'ами истина, на которой держатся
существующие тесты и `FaultedMessage`/`CurrentlyExecutingKeyPointId`.

В цикле `PlayAsync` (`ProgramViewModel.cs:731-749`), перед `await completion` для
каждого шага:

```
var targetOverall = Math.Clamp((step.SegmentIndex + step.SegmentProgress) / TotalSegments, 0, 1);
BeginStepAnimation(previousOverall, targetOverall, step.EstimatedDurationSeconds);
var result = await completion;
...
DisplayProgress = targetOverall; // snap на истину при ack
previousOverall = targetOverall;
```

`BeginStepAnimation` запоминает `(_animStart, _animTarget, _animDurationSeconds)` и
обнуляет `_animElapsedSeconds`. Работает общий `IPeriodicTimer` (интервал 100 мс —
как у `StatusPoller`/`JogScheduler`, `ServiceCollectionExtensions.cs:22-23`): на
каждый `Elapsed` — `_animElapsedSeconds += interval`; `frac = duration <= 0 ? 1.0 :
Clamp(elapsed / duration, 0, 1)`; `DisplayProgress = start + (target - start) * frac`.
Тик — фиксированный инкремент времени (не `DateTime.UtcNow`/`Stopwatch`), что делает
поведение детерминированным и тестируемым через уже существующий
`ManualPeriodicTimer` (`ArctZ.Tests/Services/Device/ManualPeriodicTimer.cs`) —
`RaiseElapsed()` в тесте эквивалентен одному интервалу реального времени.

`ProgramViewModel` получает конструкторные параметры `IPeriodicTimer timer,
TimeSpan interval`. Регистрация в DI (`ServiceCollectionExtensions.cs`) — явной
лямбдой (`sp => new ProgramViewModel(..., new SystemPeriodicTimer(),
TimeSpan.FromMilliseconds(100))`), как уже сделано для `MockDeviceTransport`,
поскольку `TimeSpan`/`IPeriodicTimer` нельзя резолвить из контейнера напрямую
(не единственные потребители с разными интервалами).

### 3. Жизненный цикл таймера — централизованно в `OnPlaybackStateChanged`

`PlaybackState == Running` → `timer.Start(interval)`. Любое другое значение
(`Paused/Stopped/Faulted/Completed/Idle`) → `timer.Stop()`. Пауза замораживает
анимацию на месте (feed hold реально останавливает станок) — `_animElapsedSeconds`
не сбрасывается, при возобновлении (`Play` из `Paused`) досчитывает с того же
места, т.к. таймер просто продолжает тикать по тому же шагу.

`DisplayProgress = 0` сбрасывается в тех же местах, где уже сбрасывается
`SegmentProgress = 0`: начало свежего `PlayAsync` (`ProgramViewModel.cs:720`) и
`StopAsync` (`ProgramViewModel.cs:779`).

### 4. Крайние случаи

- Нулевая/отрицательная подача → `EstimatedDurationSeconds = 0` → шаг доводится
  до цели на первом же тике, без деления на ноль.
- Оценка не претендует на физическую точность (реальный контроллер применяет
  ускорение и lookahead) — это чисто визуальное сглаживание прогресс-бара, не
  телеметрия положения.
- Между `Stopped`/`Faulted` и следующим `Play` таймер уже остановлен
  централизованно — отдельной очистки не требуется.

## Вне рамок

- Изменение реального формата G-code/протокола не рассматривается.
- `CurrentlyExecutingKeyPointId` и подсветка текущей точки остаются завязаны на
  дискретный `CurrentSegmentIndex`, не на `DisplayProgress`.
- Транзишн `DoubleTransition` на `ProgressBar.Value` в XAML не трогается — его
  0.3с `CubicEaseOut` продолжает сглаживать каждое обновление `DisplayProgress`
  поверх тиков таймера.

## Тестирование

- `TrajectoryCompilerTests` — новые кейсы на `EstimatedDurationSeconds`: обычный
  сегмент (расстояние/подача), eased-подшаги (сумма длительностей подшагов),
  dwell (точное значение `DwellSeconds`).
- `ProgramViewModelPlaybackTests` — конструктор `CreateViewModel` получает
  `ManualPeriodicTimer`; новый тест проверяет, что после отправки шага и
  нескольких `RaiseElapsed()` (но до реального `ok`) `DisplayProgress` растёт
  монотонно, не достигая/не превышая целевое значение шага, а по приходу `ok`
  доводится ровно до него.
- Ручная UI-проверка по стандартному воркфлоу проекта: собрать и запустить
  `ArctZ.Desktop`, прогнать программу с несколькими точками (в т.ч. с
  `EaseInOut` и без) и подтвердить через `AskUserQuestion`, что бар движется
  плавно, а не скачками, и не идёт назад.
