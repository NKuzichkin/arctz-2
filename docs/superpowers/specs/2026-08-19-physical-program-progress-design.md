# Прогресс выполнения программы: круг на точке, общий бар, Android-уведомление

## Контекст

Программа уже умеет вычислять ack-подтверждённый прогресс — `ProgramViewModel.OverallProgress`
(`CurrentSegmentIndex`/`SegmentProgress`/`TotalSegments`, обновляется только по приходу `ok` на
отправленную G-code строку) и подсветку текущей точки (`CurrentlyExecutingKeyPointId`, тот же
источник). Видимого прогресс-бара в UI сейчас нет — он был реализован и полностью откачен
(`f3b9d3e "fix: remove the visible program progress bar"`, 2026-08-08) после двух живых
UI-тестов, потому что анимация строилась на **оценке по времени** (расстояние/подача из
`TrajectoryCompiler`), синхронизированной с приходом ack. Оба захода задокументированы в
`docs/superpowers/specs/2026-08-07-program-progress-time-interpolation-design.md` (и его двух
ревизиях) и провалились по одной и той же причине: контроллер (и `MockDeviceTransport`, и
реальный FluidNC) подтверждает G-code строку, как только она принята в приёмный буфер, а не
когда движение физически завершено — буфер может принять несколько строк вперёд, пока станок
ещё едет по предыдущей. Время «от отправки до ack» не коррелирует со временем движения, поэтому
любая анимация, синхронизированная с ack, либо скачет назад, либо преждевременно долетает до
100%.

Этот дизайн намеренно не повторяет time-based подход. Вместо оценки используется **факт** —
реальная позиция станка (`DeviceStatus.WPos`), которая уже парсится корректно
(`project_status_parser_mpos_fix`) и приходит по статус-репортам каждые ~100мс через
`IDeviceSession.DeviceStatusChanged` (тот же источник, на котором уже построен
`WaitForMotionToFinishAsync`, дождавшийся именно этой развязки ack↔движение для корректного
`PlaybackState.Completed`).

## Требования

1. Прогресс подъезда к текущей точке — уменьшающийся круг на тайле точки в `MainView`.
2. Общий прогресс программы — прогресс-бар над списком точек (`ItemsControl x:Name="KeyPointsList"`,
   `MainView.axaml:204`), сбрасывается в 0% на каждом новом проходе Loop/PingPong.
3. Тот же общий прогресс — в Android foreground-уведомлении (`MachineSessionService`), шагом 5%
   (обновление уведомления только при пересечении очередной границы кратной 5).
4. Ни один из трёх слоёв не завязан на ack/`OverallProgress`/`CurrentlyExecutingKeyPointId` —
   у них общий новый источник, построенный на реальной позиции станка.
5. Существующая ack-логика (`OverallProgress`, `CurrentSegmentIndex`, `SegmentProgress`,
   `CurrentlyExecutingKeyPointId` и подсветка точки, `FaultedMessage`) не меняется — ни
   поведение, ни существующие тесты.

## Решение

### 1. `CompiledStep` получает целевую позу

`CompiledStep` (`ArctZ/Services/Program/CompiledStep.cs`) сейчас — `(SegmentIndex, Command,
SegmentProgress, EstimatedDurationSeconds)`, без позы (она "спрятана" внутри текстовой G-code
команды). Добавляется поле `Pose` (`MachinePose`, target-поза этого шага):

- Обычный (не eased) сегмент — `segment.To.Pose` (`TrajectoryCompiler.cs:26-27`).
- Eased-подшаг — уже вычисленный `pose` (`TrajectoryCompiler.cs:62`, `Interpolate(...)`).
- Dwell-шаг (`G4`) — та же поза, что у предшествующего ему шага перемещения (`segment.To.Pose`) —
  на ломаной это вершина нулевой длины, см. §3.

`EstimatedDurationSeconds` не трогается (используется только тестами компилятора, не входит в
новый прогресс-трекер — см. «Вне рамок»).

### 2. Общий helper для маппинга `SegmentIndex → KeyPoint`

Логика `CurrentlyExecutingKeyPointId` (`ProgramViewModel.cs:926-948`) — маппинг индекса сегмента
на `KeyPoint.Id` с учётом направления прохода (forward/backward, backward — через
`KeyPoints.Count - 1 - segmentIndex`) — выносится в статический helper (например,
`JibProgram.TargetKeyPoint(IReadOnlyList<KeyPoint> passKeyPoints, int segmentIndex, bool
backward)`), которым пользуются и существующее свойство (без изменения поведения/тестов), и
новый трекер (§3). Не дублировать эту логику.

### 3. `PhysicalProgressTracker` — новый класс, `ArctZ/Services/Program/`

Чистый класс без зависимостей от UI/Avalonia/потоков (кроме таймера для dwell, см. §4),
юнит-тестируемый напрямую.

**Конструктор/`Reset`**: принимает упорядоченный список `CompiledStep` текущего прохода (тот же,
что уходит в `PlayAsync`), список `KeyPoint` этого прохода (`KeyPoints` или `ReversedProgram`),
флаг `backward`, и стартовую позу `MachinePose startingPose` — реальная `WPos` станка,
захваченная в момент старта прохода (**не** `KeyPoints[0]`: сегмент 0 в `JibProgram.Segments()`
имеет `From == To == KeyPoints[0]`, нулевую дистанцию по построению модели, реальный станок едет
туда с текущего физического места).

**Ломаная (polyline)**: `startingPose` — вершина 0, дальше по порядку — `Pose` каждого
`CompiledStep`. Рёбра нулевой длины (dwell-шаги, см. §1) не участвуют в проекции по расстоянию,
но сохраняют позицию в списке — при проекции на такую вершину трекер знает, какому
`CompiledStep`/`SegmentIndex` она соответствует, это нужно для dwell-фазы (§5).

**На каждое обновление позиции** (`MachinePose current`, вызывается извне — см. §6):

1. Спроецировать `current` на каждое ребро ломаной (ближайшая точка на отрезке), взять кандидат
   с минимальным расстоянием до `current`.
2. Кумулятивная дистанция кандидата = сумма длин всех рёбер до него + расстояние от начала его
   ребра до точки проекции.
3. `_farthestCumulativeDistance = Math.Max(_farthestCumulativeDistance, candidateDistance)` —
   монотонный максимум, реальная позиция никогда не двигает прогресс назад, даже если геометрия
   даёт локально более близкую проекцию на предыдущее ребро (сглаживание углов контроллером,
   шум отчёта).
4. Из `_farthestCumulativeDistance` определить текущий `SegmentIndex` (по накопленным длинам
   рёбер, сгруппированным по `SegmentIndex` — см. §1: несколько eased-подшагов одного сегмента
   складываются в одну группу) и `ApproachFraction` — пройденная дистанция внутри группы
   текущего `SegmentIndex` / суммарная длина группы. Если группа нулевой длины (сегмент 0 —
   `From == To`, либо соседние точки физически совпадают) — `ApproachFraction` сразу `1.0`,
   без деления на ноль: точка считается «достигнутой» в момент, когда позиция впервые попадает
   в эту группу.

**Публичные свойства** (обновляются после каждого пересчёта, `INotifyPropertyChanged` — тип
подключается как обычный `ObservableObject`, чтобы `ProgramViewModel` мог напрямую
прокидывать/биндить):

- `double OverallFraction` — `_farthestCumulativeDistance / totalPathLength`, `0..1`.
- `double ApproachFraction` — см. п.4 выше, `0..1`.
- `Guid? PhysicallyExecutingKeyPointId` — `JibProgram.TargetKeyPoint(...)` (§2) от текущего
  физического `SegmentIndex`, не от ack-индекса.
- `bool IsDwelling` / `double DwellFraction` — см. §5.

Если `totalPathLength == 0` (все точки прохода совпадают) — `OverallFraction`/`ApproachFraction`
мгновенно `1.0` на первом обновлении позиции (защитный случай, аналогично нулевой подаче в
старом дизайне).

### 4. Жизненный цикл — привязка к проходам, не к всей программе

Новый `PhysicalProgressTracker` создаётся заново (`Reset(...)`) в начале каждого прохода —
там же, где уже сбрасываются `CurrentSegmentIndex = null`/`SegmentProgress = 0` при старте
прохода в `PlayAsync` (включая начало каждого повтора Loop/PingPong — общий бар обязан обнуляться
на каждом новом проходе, как согласовано с пользователем). Захват `startingPose` — из
`Connection.Session?.DeviceStatus?.WPos` в этот же момент (если `null` — событие обновления
позиции произойдёт раньше первого содержательного апдейта трекера, это не блокирует старт).

### 5. Dwell-фаза круга

Круг на точке отражает `ApproachFraction` (полный → пустой по мере физического приближения).
Когда `ApproachFraction` достигает `1.0` **и** у целевой точки `DwellSeconds > 0` — трекер
переключается в dwell-фазу: `IsDwelling = true`, `DwellFraction` заново `1.0 → 0.0` за реальное
время `DwellSeconds`.

Позиция во время dwell не меняется (станок стоит), поэтому анимировать `DwellFraction` нечем,
кроме таймера. `PhysicalProgressTracker` получает `IPeriodicTimer` (тот же интерфейс/DI-паттерн,
что `StatusPoller`/`JogScheduler`, `ServiceCollectionExtensions.cs`), с интервалом 100мс.
Таймер стартует в момент входа в dwell-фазу (`ApproachFraction` дошёл до 1.0 у точки с
`DwellSeconds > 0`), считает `elapsed`, `DwellFraction = Clamp(1 - elapsed / DwellSeconds, 0,
1)`.

Границы фазы — оба реальных события, не таймер:
- **Начало** — реальное прибытие позиции (`ApproachFraction == 1.0`), не ack.
- **Конец** — трекер выходит из dwell-фазы, когда обновление позиции показывает переход к
  следующему ребру ломаной (следующий `SegmentIndex` начал накапливать дистанцию) — то есть
  физическое начало следующего перемещения, а не истечение расчётных `DwellSeconds`. Таймер —
  чисто визуальная анимация между этими двумя реальными границами, не участвует в определении
  «выполнилось ли на самом деле» ничего; если реальная dwell заняла больше/меньше, чем
  `DwellSeconds` (маловероятно — `G4 P<seconds>` в контроллере точен), круг просто договаривает
  до 0 либо остаётся на 0 до реального начала следующего движения.

Таймер останавливается вне dwell-фазы и на `Pause`/`Stop`/`Faulted`/`Completed` — централизованно,
там же, где сейчас `OnPlaybackStateChanged` управляет прочими ресурсами прохода.

### 6. Подключение к позиции — переиспользование существующей подписки

`ProgramViewModel` уже подписан на `IDeviceSession.DeviceStatusChanged` напрямую (не через
дедуплицирующий `ConnectionViewModel.DeviceStatus`) в `OnSessionDeviceStatusChanged`
(`ProgramViewModel.cs:991`) — именно из-за той же причины, что описана в его комментарии
(программа нулевой дистанции не меняет `WPos`, дедуплицирующее свойство не увидело бы апдейт).
Новый трекер получает позицию через ту же точку: `OnSessionDeviceStatusChanged` дополнительно
вызывает `_progressTracker?.OnPositionUpdated(status.WPos)` — второй независимой подписки на
`DeviceStatusChanged` не заводится.

`ProgramViewModel` получает три новых `[ObservableProperty]`-обёртки (или прямой проброс
`PropertyChanged` от трекера), которые открывает наружу для биндингов: `OverallFraction`,
`ApproachFraction`+`IsDwelling`+`DwellFraction` (или единое `PointProgress` — решается на этапе
реализации), `PhysicallyExecutingKeyPointId`.

### 7. UI — круг на тайле

Новый радиальный индикатор — маленький бейдж (кастомная `Path`/`ArcSegment`-геометрия, в
Avalonia нет готового radial/pie контрола) в углу тайла 120×60 (`MainView.axaml:212-271`).
Видим только когда `PhysicallyExecutingKeyPointId` этой точки совпадает с её `Id` **и**
`PlaybackState` — `Running`/`Paused` (симметрично существующему `KeyPointIsExecutingConverter`,
новый параллельный `IMultiValueConverter`, возвращающий геометрию дуги по значению
`ApproachFraction`/`DwellFraction`, а не просто `bool`).

Цвет — из палитры HUD (`Themes/Colors.axaml`), не хардкод — как у остальных иконок
(см. правило иконок в `CLAUDE.md`).

### 8. UI — общий прогресс-бар

`ProgressBar` возвращается в `MainView.axaml` над `KeyPointsList` (там же, где был до
`f3b9d3e`), `Value` привязан к `ProgramViewModel.OverallFraction` (0..1), `IsVisible` — как и
раньше, `IsProgramLocked` (`PlaybackState is Running or Paused`). `DoubleTransition`
(`CubicEaseOut`, 0.3с) на `ProgressBar.Value` можно оставить — она сглаживает *реальные* скачки
позиции (кванты в 100мс), а не постулированную оценку, так что не создаёт риска рассинхронизации
как в старом дизайне.

### 9. Android-уведомление

`BackgroundSessionState` (`ArctZ/Services/App/BackgroundSessionState.cs`) — новое поле
`int? ProgressPercent`. `BackgroundSessionProjector.Project(...)` получает дополнительный
параметр `double? overallFraction` и вычисляет `ProgressPercent = overallFraction is { } f ?
(int)(Math.Round(f * 100 / 5.0) * 5) : null` — округление до кратного 5, `null` вне
Running/Paused (там и `overallFraction` не имеет смысла). `BackgroundSessionCoordinator.Refresh()`
передаёт `_program.OverallFraction` в `Project`; отдельной подписки не нужно — `Refresh()` уже
вызывается на каждом статус-репорте во время Running (см. комментарий в
`BackgroundSessionCoordinator.cs:76-78`), а `_lastSent == state` (равенство `record`) уже
дедуплицирует вызов `_host.Update` — обновление уйдёт в Android только когда `ProgressPercent`
(или что-то другое) реально изменился, то есть ровно на пересечении границы 5%.
`MachineSessionService.BuildNotification` (`ArctZ.Android/MachineSessionService.cs:147-178`)
добавляет `if (state.ProgressPercent is { } pct) { builder.SetProgress(100, pct, false); }`.

## Вне рамок

- `EstimatedDurationSeconds`/времянная оценка (`TrajectoryCompiler`) не используется новым
  трекером и не удаляется — остаётся как есть, покрыта существующими тестами компилятора.
- `OverallProgress`, `CurrentSegmentIndex`, `SegmentProgress`, `CurrentlyExecutingKeyPointId`,
  `FaultedMessage` — не меняются.
- iOS/Browser/Desktop — общий бар и круг работают на всех головах одинаково (чистая
  ViewModel-логика); прогресс в системном уведомлении — только Android (единственная голова с
  foreground-уведомлением, `project_android_foreground_session_complete`).
- Калибровка/предсказание — никакой оценки будущего времени, только факт уже пройденной
  дистанции.

## Тестирование

- `TrajectoryCompilerTests` — новые кейсы на `CompiledStep.Pose` (обычный сегмент, eased-подшаги,
  dwell-шаг).
- Новые юнит-тесты `PhysicalProgressTrackerTests` — синтетическая последовательность
  `MachinePose`, проверка: монотонность `OverallFraction`/`ApproachFraction` (не идёт назад при
  геометрически «шумной» позиции), корректный `PhysicallyExecutingKeyPointId` на forward и
  backward проходе, нулевая дистанция (все точки совпадают) не делит на ноль, dwell-фаза входит и
  выходит по реальным границам (не по истечении таймера, если позиция физически ещё не тронулась).
- Dwell-таймер — через `ManualPeriodicTimer` (`ArctZ.Tests/Services/Device/ManualPeriodicTimer.cs`),
  по образцу существующих тестов `StatusPoller`/`JogScheduler`.
- `BackgroundSessionProjectorTests` — округление до кратного 5, `null` вне Running/Paused.
- `ProgramViewModelPlaybackTests` — трекер сбрасывается на каждый новый проход (в т.ч. повтор
  Loop/PingPong), `PhysicallyExecutingKeyPointId` не совпадает с ack-индексом, когда буфер ушёл
  вперёд (синтетический `MockDeviceTransport`-сценарий: несколько ack подряд без промежуточных
  апдейтов позиции).
- Обязательный живой UI-тест по правилам проекта (`CLAUDE.md`, раздел «Тестирование UI»): собрать
  и запустить `ArctZ.Desktop`, прогнать программу с несколькими точками (включая dwell и
  `EaseInOut`, включая Loop/PingPong повтор), подтвердить через `AskUserQuestion` по каждому из
  трёх слоёв отдельно — круг на точке, общий бар, (для Android — отдельный шаг с реальной
  сборкой/установкой пользователем, как описано в `CLAUDE.md`).
