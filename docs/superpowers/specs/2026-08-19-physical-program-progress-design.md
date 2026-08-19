# Прогресс выполнения программы: круг на точке, общий бар, warning-индикатор, Android-уведомление

## Контекст

Программа уже умеет вычислять ack-подтверждённый прогресс — `ProgramViewModel.OverallProgress`
(`CurrentSegmentIndex`/`SegmentProgress`/`TotalSegments`, обновляется только по приходу `ok` на
отправленную G-code строку) и подсветку текущей точки (`CurrentlyExecutingKeyPointId`, тот же
источник). Ack подтверждает, что строка G-code принята в приёмный буфер контроллера, а не что
движение физически завершено — буфер может принять несколько строк вперёд, пока станок ещё едет
по предыдущей. Эта ack-логика не меняется этим дизайном (см. «Вне рамок»).

Отдельно уже реализован (Tasks 1-11, коммиты `db0f04f..684e08f`) слой прогресса на основе реальной
позиции станка (`DeviceStatus.WPos`, парсится корректно — `project_status_parser_mpos_fix`,
приходит по статус-репортам каждые ~100мс через `IDeviceSession.DeviceStatusChanged`):
`PhysicalProgressTracker`, свойства `ProgramViewModel.PhysicalOverallProgress` /
`PhysicalPointRemainingFraction` / `PhysicallyExecutingKeyPointId`, прогресс-бар в `MainView.axaml`
и уменьшающийся круг на тайле точки. Этот слой вычислял прогресс по **пройденной дистанции**
(проекция WPos на ломаную из целевых поз шагов) — после живого тестирования пользователь запросил
пересмотр: дистанционный прогресс визуально не даёт того, что нужно.

**Ревизия 2 этого дизайна** заменяет дистанционную метрику на **временную**, сохраняя главную идею
предыдущей версии — реальная позиция остаётся источником истины для того, *какая точка сейчас
физически активна* (а не ack-индекс), просто единица измерения прогресса внутри точки и по
программе в целом меняется с «пройденных миллиметров» на «прошедшие секунды против расчётных».
Дополнительно вводится **warning-индикатор**: если станок физически не укладывается в расчётное
время точки (перебор более чем на 15%), это сигнал, что `TransitionSeconds` для этой точки
настроен слишком оптимистично, и пользователю стоит увеличить его вручную.

`CompiledStep` уже содержит всё нужное для временной метрики без новых полей:
`EstimatedDurationSeconds` — расчётная длительность именно этого шага (для eased-подшагов —
их доля от `TransitionSeconds` точки, см. `TrajectoryCompiler.CompileEased`; для dwell-шага —
`DwellSeconds`). Сумма `EstimatedDurationSeconds` всех шагов одного `SegmentIndex` — это полное
расчётное время точки (переход + стоянка), ровно то, что просил пользователь.

## Требования

1. **Прогресс на текущей точке** — один непрерывный круг на тайле точки (без отдельной фазы
   «переход» / «стоянка» — это было в Ревизии 1 и убирается): растёт по времени от входа в точку
   (физическое начало движения к ней) до её полного завершения (переход **плюс** стоянка).
   Визуально круг остаётся «уменьшающимся» (полный → пустой), как в Ревизии 1 — см. §5.
2. **Общий прогресс программы** — прогресс-бар над списком точек, растёт по прошедшему
   астрономическому времени с начала прохода, делённому на сумму расчётного времени всех точек
   прохода (переход + стоянка каждой). Сбрасывается в 0% на каждом новом проходе (Loop/PingPong) —
   не меняется относительно Ревизии 1.
3. **Warning-индикатор (!)** на тайле текущей физически активной точки, если время нахождения на
   ней превышает расчётное более чем на **15%**. Живой сигнал: гаснет сразу при физическом переходе
   к следующей точке, не накапливается в список/историю.
4. **Частота обновления UI:** единый таймер **200мс** — раз в 200мс тик пересчитывает временные
   доли (нужно даже когда позиция не меняется — станок стоит на dwell) и проталкивает их в
   `[ObservableProperty]`-обёртки `ProgramViewModel`. Позиционные апдейты (~100мс от статус-
   репортов) дополнительно триггерят немедленный пересчёт «какая точка сейчас активна» — не ждут
   тика таймера для этой части.
5. **Android foreground-уведомление** — тот же общий прогресс, шагом 5% (не меняется относительно
   Ревизии 1).
6. Ни один из слоёв не завязан на ack/`OverallProgress`/`CurrentlyExecutingKeyPointId`.
7. Существующая ack-логика (`OverallProgress`, `CurrentSegmentIndex`, `SegmentProgress`,
   `CurrentlyExecutingKeyPointId` и подсветка точки, `FaultedMessage`) не меняется — ни поведение,
   ни существующие тесты.

## Решение

### 1. `CompiledStep.Pose` — уже реализовано, без изменений

`CompiledStep` (`ArctZ/Services/Program/CompiledStep.cs`) уже содержит `Pose` (`MachinePose`,
целевая поза шага) и `IsDwellStep` (`bool`) — добавлены в Ревизии 1, Task 1, не трогаются.
`EstimatedDurationSeconds` (уже существовавшее поле, было «вне рамок» в Ревизии 1) теперь
используется трекером напрямую — см. §3.

### 2. Общий helper для маппинга `SegmentIndex → KeyPoint` — уже реализовано, без изменений

`JibProgram.TargetKeyPoint(IReadOnlyList<KeyPoint> passKeyPoints, int? segmentIndex, bool
backward)` (Ревизия 1, Task 2) — маппинг индекса сегмента на `KeyPoint.Id` с учётом направления
прохода. Используется без изменений.

### 3. `TimeProgressTracker` — переписывается из `PhysicalProgressTracker`

Тот же файл `ArctZ/Services/Program/PhysicalProgressTracker.cs`, переименовать класс (и файл) в
`TimeProgressTracker` — семантика вывода меняется с «доля пройденной дистанции» на «доля
прошедшего времени», переименование отражает это для будущих читателей. Чистый класс без
зависимостей от UI/Avalonia/потоков; время передаётся параметром (`DateTime`) в каждый вызов, не
берётся из `DateTime.UtcNow` внутри — тестируемость синтетическими метками времени.

**Что остаётся от Ревизии 1 без изменений** (внутренняя, приватная механика):

- Массив `Edge` (`From`, `To`, `SegmentIndex`, `Length`, `CumulativeBefore`) — геометрическая
  проекция позиции на ломаную из `Pose` каждого `CompiledStep`.
- `_farthestCumulativeDistance` — монотонный максимум пройденной дистанции; определяет, какое
  ребро (и значит какой `SegmentIndex`) сейчас «физически активно». Это остаётся единственным
  источником истины для «какая точка сейчас активна» — то, ради чего Ревизия 1 вообще перешла на
  реальную позицию вместо ack, и что решает исходную проблему (ack ≠ физическое движение).
- `int? CurrentSegmentIndex` — вычисляется из `_farthestCumulativeDistance` (`FindEdgeIndexAt`),
  без изменений. Zero-length dwell-рёбра (Ревизия 1, §1) по-прежнему не участвуют в проекции по
  расстоянию, но благодаря `SegmentIndex`-группировке `CurrentSegmentIndex` естественным образом
  остаётся на точке N весь физический dwell (позиция не движется → `_farthestCumulativeDistance`
  не растёт → `FindEdgeIndexAt` продолжает возвращать ребро с `SegmentIndex == N`), пока реальное
  движение к следующей точке не сдвинет `_farthestCumulativeDistance` дальше. Отдельная фаза
  «IsDwelling» (Ревизия 1, §5) для этого больше не нужна — граница «точка N закончилась» и так уже
  строго физическая, что делает Ревизию 2 проще Ревизии 1, а не сложнее.

**Что удаляется** (мёртвый код после перехода на временную метрику — эти члены Ревизии 1
использовались только для дистанционной `ApproachFraction`/`DwellFraction`, которых в Ревизии 2
больше нет):

- `_segmentSpans` (`Dictionary<int, (double Start, double Length)>`) и публичное свойство
  `ApproachFraction` — заменены на `_segmentEstimatedSeconds`/`CurrentStepFraction` ниже (та же
  роль — доля внутри текущей точки — но по времени, не по дистанции).
- Поле `DwellSeconds` на `Edge`, приватные `_isDwelling`/`_dwellElapsedSeconds`/
  `_dwellTotalSeconds`, публичные `IsDwelling`/`DwellFraction`, метод
  `FindDwellEdgeForSegment` — вся отдельная dwell-фаза (см. предыдущий пункт: граница точки теперь
  и так строго физическая через `CurrentSegmentIndex`, отдельно её ловить не нужно).
- `OnTimerElapsed(TimeSpan interval)` — заменяется на `OnClockTick(DateTime now)` (см. §4):
  абсолютное время вместо накопления дельт, нужно единому 200мс таймеру наравне с
  `OnPositionUpdated`.

**Что добавляется** (при конструировании, наряду с рёбрами, в одном проходе по `steps`):

- `Dictionary<int, double> _segmentEstimatedSeconds` — для каждого `SegmentIndex` сумма
  `EstimatedDurationSeconds` всех его шагов (все eased-подшаги перехода + dwell-шаг, если есть) —
  это «расчётное время перехода в точку плюс время стоянки», как просил пользователь.
- `double _totalEstimatedSeconds` — сумма `EstimatedDurationSeconds` вообще всех шагов прохода.
- `DateTime _passStartedAt` — передаётся в конструктор (реальное время старта прохода, засекается
  вызывающей стороной в момент `Reset`).
- `DateTime _currentSegmentEnteredAt` — изначально `_passStartedAt`; переустанавливается на
  переданное `now`, когда пересчёт видит, что `CurrentSegmentIndex` изменился с предыдущего
  пересчёта.

**Пересчёт** — единый приватный `Recompute(DateTime now)`, вызывается из обоих публичных методов
обновления (см. §4):

1. Если `CurrentSegmentIndex` изменился относительно сохранённого с прошлого пересчёта значения —
   `_currentSegmentEnteredAt = now` (точка только что физически стала активной, время внутри неё
   стартует с нуля).
2. `elapsedInSegment = (now - _currentSegmentEnteredAt).TotalSeconds`.
3. `estimatedForSegment = _segmentEstimatedSeconds.GetValueOrDefault(CurrentSegmentIndex ?? -1, 0)`.
4. Обновить кэшированные `CurrentStepFraction`, `CurrentPointHasWarning` (формулы — см. свойства
   ниже), поднять `Changed`.

**Публичные свойства** (кэшированные результаты последнего `Recompute`, `event Action? Changed`
как в Ревизии 1):

- `double OverallFraction` — `_totalEstimatedSeconds <= 0 ? 1.0 : Math.Clamp((now -
  _passStartedAt).TotalSeconds / _totalEstimatedSeconds, 0, 1)`. Чистое астрономическое время с
  начала прохода — специально **не** суммируется из фактических длительностей по сегментам
  (что потребовало бы отдельного учёта для каждого завершённого сегмента): прошедшее время с
  начала прохода уже по определению равно сумме фактического времени всех сегментов, пройденных к
  этому моменту, плюс времени в текущем. Если сумма расчётного времени всех точек превышена
  (несколько точек подряд не уложились в расчёт) — бар доходит до 100% и остаётся там до
  `Completed`, не переполняясь; это допустимый побочный эффект чисто временной метрики, а
  warning-индикатор (см. ниже) — основной сигнал именно о такой рассинхронизации, не сам бар.
- `double CurrentStepFraction` — `estimatedForSegment <= 0 ? 1.0 : elapsedInSegment /
  estimatedForSegment`. **Не клэмпится** к `1.0` внутри трекера (в отличие от `OverallFraction`) —
  значение выше `1.0` — это и есть перебор времени, нужен как есть для warning-проверки и для теста
  на него; клэмп для отображения (не уходить в отрицательные значения на круге) делает вызывающая
  сторона (§6).
- `bool CurrentPointHasWarning` — `estimatedForSegment > 0 && elapsedInSegment > estimatedForSegment
  * 1.15`. Живой, не защёлкивающийся флаг: как только `CurrentSegmentIndex` меняется,
  `_currentSegmentEnteredAt` сбрасывается и `elapsedInSegment` обнуляется, так что warning для
  предыдущей точки не переносится на следующую (см. Требование 3).
- `int? CurrentSegmentIndex` — см. выше, без изменений относительно Ревизии 1.

Если `_totalEstimatedSeconds == 0` (все `EstimatedDurationSeconds` нулевые — вырожденный случай,
не встречается при непустом списке точек с положительным `TransitionSeconds`) — `OverallFraction`
сразу `1.0`, аналогично защитному случаю Ревизии 1.

### 4. Публичные методы обновления и единый таймер

Два публичных входа, оба вызывают внутренний `Recompute(now)`:

- `void OnPositionUpdated(MachinePose position, DateTime now)` — проекция позиции на ломаную (как
  в Ревизии 1, без изменений в этой части), обновление `_farthestCumulativeDistance`, затем
  `Recompute(now)`. Вызывается из `OnSessionDeviceStatusChanged` (см. §6), даёт низкую задержку
  реакции на «точка физически сменилась», не дожидаясь следующего тика таймера.
- `void OnClockTick(DateTime now)` — просто `Recompute(now)` без апдейта позиции. Нужен, чтобы
  `CurrentStepFraction`/`CurrentPointHasWarning`/`OverallFraction` продолжали расти по времени,
  пока станок физически стоит на dwell (позиция не шлёт новых апдейтов, отличных от предыдущих) —
  без этого метода круг застыл бы на dwell до следующего реального шага.

Оба метода заменяют `OnPositionUpdated(MachinePose)` (без времени) и `OnTimerElapsed(TimeSpan
interval)` (дельта, не абсолютное время) из Ревизии 1 — сигнатуры меняются, вызывающая сторона
(`ProgramViewModel`) обновляется соответственно (§6).

**Единый таймер 200мс** в `ProgramViewModel` заменяет специализированный 100мс dwell-таймер
Ревизии 1 (`IPeriodicTimer`, тот же DI-паттерн, что `StatusPoller`/`JogScheduler`,
`ServiceCollectionExtensions.cs`). На каждый тик: `_progressTracker?.OnClockTick(DateTime.UtcNow)`
(поднимет `Changed` → `OnProgressTrackerChanged` → `OnPropertyChanged` для всех проброшенных
свойств, см. §6). Таймер стартует при входе `PlaybackState` в `Running`, останавливается на
`Pause`/`Stop`/`Faulted`/`Completed` — централизованно, там же, где Ревизия 1 уже управляет
прочими ресурсами прохода в `OnPlaybackStateChanged`.

### 5. Жизненный цикл — привязка к проходам, без изменений в точке входа

`TimeProgressTracker` создаётся заново (`Reset`/новый экземпляр) в начале каждого прохода — та же
точка, где Ревизия 1 уже это делает в `PlayAsync` (включая каждый повтор Loop/PingPong).
Дополнительно к `startingPose` (реальная `WPos` в момент старта, как в Ревизии 1) конструктор
получает `passStartedAt: DateTime.UtcNow`, зафиксированный в тот же момент.

### 6. Подключение к позиции и таймеру — переиспользование существующей подписки

`OnSessionDeviceStatusChanged` (`ProgramViewModel.cs:991`, уже существует по причине, описанной в
его комментарии — программа нулевой дистанции не меняет `WPos`, дедуплицирующее
`ConnectionViewModel.DeviceStatus` не увидело бы апдейт) вызывает
`_progressTracker?.OnPositionUpdated(status.WPos, DateTime.UtcNow)` — та же точка входа, что и в
Ревизии 1, сигнатура получает второй параметр.

`ProgramViewModel` пробрасывает свойства трекера наружу под теми же именами, что уже привязаны в
`MainView.axaml` (минимизирует изменения XAML) — семантика имён меняется (дистанция → время), сами
имена и биндинги нет:

- `PhysicalOverallProgress => _progressTracker?.OverallFraction ?? 0`
- `PhysicalPointRemainingFraction => _progressTracker is null ? 1.0 : 1.0 -
  Math.Clamp(_progressTracker.CurrentStepFraction, 0, 1)` — клэмп здесь (не в трекере, см. §3):
  `CurrentStepFraction` выше `1.0` (перебор времени) не должен уводить визуальный остаток в
  отрицательные значения — круг просто держится пустым, а видимость перебора обеспечивает
  warning-индикатор, не геометрия круга.
- `PhysicallyExecutingKeyPointId => _progressTracker is null ? null :
  JibProgram.TargetKeyPoint(KeyPoints, _progressTracker.CurrentSegmentIndex, _currentPassBackward)`
  — без изменений относительно Ревизии 1.
- Новое: `PhysicalPointHasTimeWarning => _progressTracker?.CurrentPointHasWarning ?? false`.

`OnProgressTrackerChanged` (уже существующий обработчик `_progressTracker.Changed`) дополнительно
поднимает `OnPropertyChanged(nameof(PhysicalPointHasTimeWarning))`.

### 7. UI — круг на тайле и warning-индикатор

Круг — без изменений относительно Ревизии 1 (уже реализованная геометрия дуги,
`FractionToPieSliceConverter`, биндинг на `PhysicalPointRemainingFraction` и
`PhysicallyExecutingKeyPointId`, видимость по `PlaybackState is Running or Paused`) — меняется
только то, *откуда* берётся значение `PhysicalPointRemainingFraction` (время вместо дистанции,
§6), сама разметка/конвертер не трогается.

**Новое: warning-индикатор**. Небольшая иконка (`MaterialIconKind`, см. правило иконок в
`CLAUDE.md` — регистрация уже выполнена в `App.axaml`, `Icon rollout` завершён ранее) в углу того
же тайла 120×60, `IsVisible` через `MultiBinding`/конвертер на `PhysicallyExecutingKeyPointId ==
<Id этой точки> && PhysicalPointHasTimeWarning`. Цвет — предупреждающий акцент из палитры HUD
(`Themes/Colors.axaml`), не хардкод.

### 8. UI — общий прогресс-бар

Без изменений относительно Ревизии 1: `ProgressBar` в `MainView.axaml` над `KeyPointsList`,
`Value` привязан к `PhysicalOverallProgress` (0..1), `IsVisible` по `IsProgramLocked`,
`DoubleTransition` (`CubicEaseOut`, 0.3с) остаётся — 200мс шаг тика уже даёт плавные приращения,
transition дополнительно сглаживает без риска рассинхронизации (тот же аргумент, что в Ревизии 1).

### 9. Android-уведомление

Без изменений относительно Ревизии 1: `BackgroundSessionState.ProgressPercent`,
`BackgroundSessionProjector.Project(..., double? overallFraction)`, округление до кратного 5,
`BackgroundSessionCoordinator.Refresh()` передаёт `_program.PhysicalOverallProgress`,
`MachineSessionService.BuildNotification` вызывает `builder.SetProgress(100, pct, false)`. Этот
слой уже целиком реализован (Ревизия 1, Tasks 7-9) и не завязан на то, как именно вычисляется
`PhysicalOverallProgress` внутри — семантика поменялась (время вместо дистанции), контракт
(`double` в диапазоне `0..1`) нет.

## Вне рамок

- `OverallProgress`, `CurrentSegmentIndex` (ack-версия), `SegmentProgress`,
  `CurrentlyExecutingKeyPointId`, `FaultedMessage` — не меняются (существующий ack-based слой
  остаётся как есть).
- iOS/Browser/Desktop — общий бар и круг работают на всех головах одинаково (чистая
  ViewModel-логика); прогресс в системном уведомлении — только Android
  (`project_android_foreground_session_complete`).
- Настраиваемый threshold для warning-индикатора — зафиксирован на ±15%, не выносится в
  пользовательские настройки в этой итерации.
- Персистентный список/история warning-точек — индикатор живой (только для текущей физически
  активной точки), накопительный отчёт по всем точкам прохода не входит в объём.
- Автоматическая корректировка `TransitionSeconds` — индикатор только сигнализирует, изменение
  значения остаётся ручным действием пользователя в редакторе точки.

## Тестирование

- Переименование/адаптация существующих `PhysicalProgressTrackerTests` →
  `TimeProgressTrackerTests` (класс и файл переименовываются вместе с трекером). Новые кейсы
  поверх унаследованных (геометрия/маппинг сегментов — без изменений, тесты на неё переносятся
  как есть):
  - `OverallFraction` растёт по переданному `now`, не по позиции — синтетический сценарий, где
    позиция не меняется между двумя вызовами `OnClockTick` с разными `now`, `OverallFraction`
    всё равно увеличивается.
  - `CurrentStepFraction` сбрасывается к малому значению, когда `CurrentSegmentIndex` меняется
    (новая точка — новый отсчёт elapsed, не наследует время предыдущей).
  - `CurrentPointHasWarning`: сценарий, где `elapsedInSegment` на 20% больше
    `_segmentEstimatedSeconds` → `true`; сценарий с разницей 10% → `false` (граница ±15%).
  - `CurrentPointHasWarning` гаснет сразу при переходе `CurrentSegmentIndex` на следующий, даже
    если предыдущая точка была помечена варнингом непосредственно перед переходом.
  - `estimatedForSegment == 0` (вырожденный случай) → `CurrentStepFraction == 1.0`,
    `CurrentPointHasWarning == false` (защита от деления на ноль/ложного варнинга).
  - `OnClockTick` во время dwell (позиция не меняется) продолжает растить `CurrentStepFraction`
    без вызовов `OnPositionUpdated` — закрывает случай, ради которого убрана отдельная
    dwell-фаза/таймер Ревизии 1.
  - Монотонность `CurrentSegmentIndex`/принадлежности сегменту — без изменений относительно
    Ревизии 1 (не идёт назад при геометрически «шумной» позиции).
- `ManualPeriodicTimer` (`ArctZ.Tests/Services/Device/ManualPeriodicTimer.cs`) — по образцу
  существующих тестов `StatusPoller`/`JogScheduler`, для 200мс таймера в `ProgramViewModel`.
- `BackgroundSessionProjectorTests` — без изменений (округление до кратного 5, `null` вне
  Running/Paused) — не завязаны на природу `overallFraction`.
- `ProgramViewModelPlaybackTests` — трекер сбрасывается на каждый новый проход (в т.ч. повтор
  Loop/PingPong, без изменений относительно Ревизии 1); новый кейс —
  `PhysicalPointHasTimeWarning` пробрасывается в `PropertyChanged` при изменении внутри трекера;
  200мс таймер стартует на `Running`, останавливается на `Pause`/`Stop`/`Faulted`/`Completed`
  (замена dwell-таймера Ревизии 1 на единый таймер — тест на старт/стоп адаптируется).
- Обязательный живой UI-тест по правилам проекта (`CLAUDE.md`, раздел «Тестирование UI»): собрать
  и запустить `ArctZ.Desktop`, прогнать программу с несколькими точками (включая dwell,
  `EaseInOut`, Loop/PingPong повтор, и намеренно заниженное `TransitionSeconds` на одной точке —
  например, физически недостижимое малое время — для проверки warning-индикатора), подтвердить
  через `AskUserQuestion` по каждому слою отдельно: круг на точке (растёт по времени, а не
  скачками по позиции), общий бар, warning-индикатор (появляется и гаснет в нужный момент), (для
  Android — отдельный шаг с реальной сборкой/установкой пользователем, как описано в `CLAUDE.md`).
