# Подсветка текущей исполняемой ключевой точки — дизайн

Дата: 2026-08-06

## Цель

Во время выполнения программы (`PlaybackState.Running`/`Paused`) визуально выделять
плитку той ключевой точки, к которой станок сейчас движется, в списке точек на
`MainView`. Подсветка сдвигается по мере выполнения программы и исчезает, когда
воспроизведение не активно (`Idle`/`Completed`/`Faulted`/`Stopped`).

## Не входит в задачу

- Отдельная маркировка точки, на которой произошла ошибка (`FaultedAtSegmentIndex`).
- Автоскролл к подсвеченной плитке, если список не помещается на экране.
- Изменение гранулярности отслеживания прогресса — используется существующий
  ack-based сигнал по сегментам (переход на более точное отслеживание по буферу
  контроллера остаётся отложенным пунктом, как и раньше).

## Логика: какая точка считается "текущей"

Прогресс сейчас отслеживается по **сегментам** (переход между двумя соседними
точками), не по точкам: `ProgramViewModel.CurrentSegmentIndex` (`int?`) обновляется
в `PlayAsync()` при получении ack от контроллера на команду перемещения. Сегмент `i`
— это движение от `KeyPoints[i]` к `KeyPoints[i + 1]` (см. `JibProgram.Segments()`).

Новое вычисляемое свойство на `ProgramViewModel`:

```csharp
public Guid? CurrentlyExecutingKeyPointId
{
    get
    {
        if (PlaybackState is not (PlaybackState.Running or PlaybackState.Paused))
        {
            return null;
        }

        var targetIndex = (CurrentSegmentIndex ?? -1) + 1;
        return targetIndex >= 0 && targetIndex < KeyPoints.Count
            ? KeyPoints[targetIndex].Id
            : null;
    }
}
```

- `[NotifyPropertyChangedFor(nameof(CurrentlyExecutingKeyPointId))]` добавляется на
  `_playbackState` и `_currentSegmentIndex`, чтобы свойство обновлялось реактивно.
- Как только нажат "Пуск" (`PlaybackState` становится `Running`, `CurrentSegmentIndex`
  ещё `null`), подсвечивается точка назначения первого сегмента (`KeyPoints[1]`) — то
  есть подсветка включается по факту отправки команды, не дожидаясь ack.
- При получении ack на каждый сегмент подсветка сдвигается на следующую точку.
- В `Paused` подсветка остаётся на той же точке, к которой шло движение перед паузой.
- Вне `Running`/`Paused` — `null`, подсветки нет.

## Реализация в XAML

Плитка точки — `Button Width="120" Height="60"` в инлайновом
`DataTemplate x:DataType="program:KeyPoint"` (`ArctZ/Views/MainView.axaml`, внутри
`ItemsControl x:Name="KeyPointsList"`). `KeyPoint` — record, а не ViewModel, поэтому
у него нет собственного bindable-свойства `IsExecuting`.

По аналогии с уже используемым в этом же шаблоне паттерном
`Command="{Binding ((vm:ProgramViewModel)DataContext).XxxCommand, ElementName=KeyPointsList}"`
(флайаут-команды точки), добавляется:

1. `IMultiValueConverter` (`KeyPointIsExecutingConverter`), сравнивающий `Guid`
   собственной точки плитки с `CurrentlyExecutingKeyPointId` родительской VM и
   возвращающий `bool`.
2. `MultiBinding` на `Classes.executing` кнопки-плитки:
   - первый Binding — `{Binding Id}` (Id самой точки, локальный DataContext);
   - второй Binding —
     `{Binding ((vm:ProgramViewModel)DataContext).CurrentlyExecutingKeyPointId, ElementName=KeyPointsList}`.
3. Новый стиль `Style Selector="Button.executing"`, по аналогии с уже существующим
   `Border.loaded-entry` (`ArctZ/Views/MainView.axaml`), с акцентными цветами
   `HudAccentBrush`/`HudAccentDimBrush`:
   - `Background="{DynamicResource HudAccentDimBrush}"`
   - `BorderBrush="{DynamicResource HudAccentBrush}"`
   - `BorderThickness="2"` — рамка по всему периметру (не только слева, как у
     `loaded-entry`): плитка — самостоятельный квадрат в WrapPanel, а не строка
     списка, поэтому левая полоска-акцент здесь не читалась бы как выделение.

### Найденная проблема: список точек гасится во время воспроизведения

Весь блок с плитками обёрнут в `StackPanel IsEnabled="{Binding !IsProgramLocked}"`
(`MainView.axaml:134`), а `IsProgramLocked` истинно как раз в `Running`/`Paused` — то
есть именно тогда, когда должна быть видна подсветка, весь список визуально гасится
дефолтным `:disabled`-стилем темы Fluent (в проекте нет собственного override для
`:disabled`). Чтобы подсветка не терялась под затемнением, добавляется явный override
для `Button.executing:disabled` (полная непрозрачность и те же акцентные цвета).
Взаимодействие с плиткой всё равно остаётся заблокированным — меняется только
визуальное затемнение.

## Тестирование

- Unit-тест на `ProgramViewModel`: проверить переходы `CurrentlyExecutingKeyPointId`
  по мере изменения `PlaybackState`/`CurrentSegmentIndex` — подсветка на `KeyPoints[1]`
  сразу при переходе в `Running` без ack, сдвиг при каждом обновлении
  `CurrentSegmentIndex`, `null` вне `Running`/`Paused`.
- Обязательная UI-проверка проекта: собрать и запустить `ArctZ.Desktop`, выполнить
  программу (или её часть), визуально подтвердить, что подсветка появляется на первой
  точке назначения сразу при "Пуск", сдвигается по мере выполнения и не гаснет под
  disabled-затемнением списка, исчезает по завершении/остановке/ошибке.
