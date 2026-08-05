# Единый статус станка и программы — дизайн

Дата: 2026-08-05

**Опирается на:** `docs/superpowers/specs/2026-08-05-header-status-alarm-redesign-design.md` (уже реализован), который свёл шапку к единой панели статуса `Border(HeaderStatusRow)` с двумя текстовыми полями рядом — `ConnectionView.MachineStateLabel`/`PositionLabel` (машинное состояние станка) внутри `ContentControl(ConnectionStatus)` и отдельно `MainView.PlaybackStateLabel` (состояние воспроизведения программы) в соседней колонке того же `Grid`. Этот документ убирает дублирование между ними.

## Проблема

`MachineStateLabel` (`ConnectionViewModel.cs`) и `PlaybackStateLabel` (`ProgramViewModel.cs`) — два независимых свойства, отражающих две разные вещи (состояние станка по данным FluidNC vs состояние проигрывания программы внутри приложения), но в состоянии покоя оба говорят «ничего не происходит» разными словами: «Простой» и «Ожидание» одновременно. Пользователю неочевидно, зачем два слова, и в проигрывании программы машинное состояние (`Run`/`Idle` между шагами G-code) визуально дёргается независимо от факта самого прогона, что дополнительно путает.

На деле состояния не независимы — большая часть пересечений уже определена кодом:
- `PauseAsync`/`StopAsync` (`ProgramViewModel.cs:643-668`) отправляют `FeedHoldAsync()` — станок физически уходит в `MachineState.Hold`. Обратно в `Idle` без явной `ResumeAsync()`/нового `PlayAsync` не возвращается.
- Джойстики блокируются на время `IsProgramLocked` (`Running`/`Paused`) — `MachineState.Jog` физически недостижим, пока играет программа.
- `PlaybackState.Completed`/`Stopped`/`Faulted` не сбрасываются обратно в `Idle` нигде в коде — это отдельная, самостоятельная особенность (см. «Автосброс терминальных состояний» ниже), обнаруженная в ходе этой работы.

## Решение: одно вычисляемое свойство `ProgramViewModel.StatusLabel`

`ProgramViewModel` — корневой `x:DataType` шапки (`MainView.axaml`), уже читает `Connection.*` напрямую в нескольких местах (`Connection.Session`, `Connection.DeviceStatus`, `Connection.IsPlaybackLocked`) — тот же паттерн, никакой новой зависимости между вью-моделями. Обратное (свойство на `ConnectionViewModel`, читающее `PlaybackState` от `ProgramViewModel`) невозможно — `ConnectionViewModel` не знает о родителе.

### Приоритет вычисления (сверху вниз, первое совпадение побеждает)

```csharp
public string StatusLabel
{
    get
    {
        if (PlaybackState == PlaybackState.Faulted) return "Ошибка";
        if (PlaybackState == PlaybackState.Running) return "Выполнение";
        if (PlaybackState == PlaybackState.Paused) return "Пауза";
        if (Connection.DeviceStatus?.State == MachineState.Jog) return "Джог";
        if (Connection.DeviceStatus?.State == MachineState.Home) return "Homing";
        if (PlaybackState == PlaybackState.Completed) return "Завершено";
        if (PlaybackState == PlaybackState.Stopped) return "Остановлено";
        return "Ожидание";
    }
}
```

`MachineState.Alarm` **не входит** в этот приоритет — авария уже перекрывает весь экран блокирующей модалкой (`ConnectionViewModel.IsAlarmModalVisible`, см. `2026-08-05-header-status-alarm-redesign-design.md`), `StatusLabel` под ней всё равно не виден. Пока авария активна, `StatusLabel` продолжает показывать то значение, которое было актуально до неё (обычно «Ожидание» или «Выполнение») — специальной обработки не требуется, вычисление просто не проверяет `Alarm`.

`Джог`/`Homing` проверяются только после `Faulted`/`Running`/`Paused` — они физически недостижимы, пока программа выполняется или на паузе (джойстики заблокированы `IsProgramLocked`), так что реальных конфликтов приоритета между ними и статусами воспроизведения не бывает; порядок проверки здесь — подстраховка, а не рабочая ветка.

`MachineState.Hold` намеренно не проверяется отдельной веткой: единственный способ в него попасть — `FeedHoldAsync()` из `PauseAsync`/`StopAsync` (`ProgramViewModel.cs:643-668`), а обе эти команды уже выставляют `PlaybackState.Paused`/`Stopped` раньше, чем `Hold` мог бы быть проверен — эти ветки в `switch` выше и перехватывают случай первыми.

### Уведомление об изменениях

`StatusLabel` пересчитывается при изменении `PlaybackState` (уже есть `[NotifyPropertyChangedFor]` на `_playbackState` — добавляется `nameof(StatusLabel)` туда) и при изменении `Connection.DeviceStatus` (уже есть подписка `OnConnectionPropertyChanged` на `nameof(ConnectionViewModel.DeviceStatus)`, `ProgramViewModel.cs:505-512` — добавляется `OnPropertyChanged(nameof(StatusLabel))` в её тело).

### Автосброс терминальных состояний

`PlaybackState.Completed`/`Stopped`/`Faulted` сегодня не возвращаются в `Idle` нигде в коде — висят до следующего `PlayAsync`. Это отдельная, самостоятельная особенность (не связанная с объединением статусов как таковым), которую эта задача заодно чинит, раз уже трогает `StatusLabel`/`PlaybackState`.

Добавляется таймер на 4 секунды: как только `PlaybackState` становится `Completed`, `Stopped` или `Faulted`, запускается отложенный сброс обратно в `Idle`. Реализация — `await Task.Delay(...)` с последующей мутацией `PlaybackState`, тот же идиом «`await` на асинхронной операции → мутация свойства сразу после», что уже используется в этом файле в `PlayAsync`/`PauseAsync` (`ProgramViewModel.cs:579-596`) — Avalonia восстанавливает `SynchronizationContext` UI-потока после `await` так же, как после `await Session.ResumeAsync()`; никакого диспетчер-абстрагирования (`IUiDispatcher` и т.п.) не требуется, в файле такого механизма и так нет.

Требования к корректности:
- Отложенный сброс должен **отменяться**, если пользователь успел нажать «Пуск» до истечения 4 секунд и `PlaybackState` реально ушёл в `Running` — иначе таймер зайдёт и затрёт `Running` обратно на `Idle` посреди прогона.
- Каждый новый вход в терминальное состояние отменяет предыдущий отложенный сброс и запускает новый (на случай `Faulted` → быстрый повторный `Stopped` и т.п.).
- Реализация — `CancellationTokenSource`, пересоздаваемый при каждом входе в терминальное состояние, отменяемый при выходе из него.

Задержка (4 секунды) выносится в `internal` поле/константу с возможностью переопределения в тестах (`InternalsVisibleTo("ArctZ.Tests")` уже настроен в `ArctZ.csproj:34`) — юнит-тест ставит значение в единицы миллисекунд и реально дожидается срабатывания, вместо ожидания 4 настоящих секунд.

## Изменения в XAML

`ArctZ/Views/ConnectionView.axaml`: убирается `TextBlock Text="{Binding MachineStateLabel}"` (строка 18). Остаётся только `TextBlock Text="{Binding PositionLabel}"` внутри того же `StackPanel` (координаты — самостоятельная телеметрия, не часть объединяемого статуса, показывать её отдельно не мешает).

`ArctZ/Views/MainView.axaml:96`: `Text="{Binding PlaybackStateLabel}"` → `Text="{Binding StatusLabel}"`.

## Удаляемый код

- `ConnectionViewModel.MachineStateLabel` (свойство + `switch`) и соответствующий `this.RaisePropertyChanged(nameof(MachineStateLabel))` в уведомляющем блоке конструктора — после правки XAML на это свойство никто не биндится.
- `ProgramViewModel.PlaybackStateLabel` (свойство + `switch`) и атрибут `[NotifyPropertyChangedFor(nameof(PlaybackStateLabel))]` на `_playbackState` — заменяется на `nameof(StatusLabel)`.

## Затронутые файлы

- `ArctZ/ViewModels/ProgramViewModel.cs` — новое свойство `StatusLabel`, уведомления, таймер автосброса с `CancellationTokenSource`, `internal`-задержка для тестов.
- `ArctZ/ViewModels/ConnectionViewModel.cs` — удаление `MachineStateLabel`.
- `ArctZ/Views/ConnectionView.axaml`, `ArctZ/Views/MainView.axaml` — точечные правки биндингов.

## Тестирование

- Юнит-тесты на `StatusLabel` в `ArctZ.Tests/ViewModels/ProgramViewModelPlaybackTests.cs` (или соседнем файле, по месту существующих тестов `PlaybackState`) — по одному кейсу на каждую ветку приоритета, плюс кейс на автосброс (с переопределённой короткой задержкой) и кейс на отмену автосброса при повторном `Play` до истечения таймера.
- `dotnet build` для `ArctZ.Desktop`/`ArctZ.Browser` — компиляция XAML после смены биндингов.
- Полный прогон `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`.

## Не в скоупе

- Показ «Авария» где-либо в объединённом статусе — уже решено в `2026-08-05-header-status-alarm-redesign-design.md`, модалка остаётся единственным источником этой информации.
- Любые изменения в `MachineState`/`PlaybackState` как перечислениях, в `DeviceStatus`, в `IDeviceSession` — только чтение существующих значений.
- Раздельные подписи "Простой"/"Ожидание" где-либо ещё в приложении, если таковые появятся в будущем — эта правка касается только шапки `MainView`.
