# Панель лога отправленных G-code команд — дизайн

Дата: 2026-07-31

## Проблема

Для демонстраций (в первую очередь в режиме «Демо») полезно видеть вживую, какие G-code/`$`-команды приложение реально отправляет на устройство — и джог, и точки программы, и Play/Home/Reset. Сейчас такой видимости нет: `IDeviceTransport.SendLineAsync` вызывается из нескольких мест (`BufferAwareCommandQueue.Pump`, `JogScheduler.OnElapsedCore`), но ни одна отправленная строка нигде не сохраняется и не показывается в UI.

## Что попадает в лог

Каждая строка, переданная в `IDeviceTransport.SendLineAsync` — это включает:
- явные команды из `BufferAwareCommandQueue` (точки программы, Play, `$H`, `$X`)
- непрерывный поток `$J=...` от `JogScheduler` во время джоггинга

Realtime-байты (`SendRawByteAsync`: `?`, `!`, `~`, jog-cancel `0x85`) в лог **не** попадают — это не текстовые G-code строки, а протокольные однобайтовые сигналы.

Лог работает одинаково для обоих типов подключения (реальное устройство и Демо) — отдельного скрытия/показа по типу endpoint'а не требуется, кнопка открытия видна всегда.

## `LoggingDeviceTransport` — новый декоратор

Новый файл `ArctZ/Services/Device/LoggingDeviceTransport.cs`:

```csharp
public sealed class LoggingDeviceTransport : IDeviceTransport
{
    private readonly IDeviceTransport _inner;

    public LoggingDeviceTransport(IDeviceTransport inner) => _inner = inner;

    public event Action<string>? LineSent;

    public bool IsConnected => _inner.IsConnected;
    public event Action<string>? LineReceived { add => _inner.LineReceived += value; remove => _inner.LineReceived -= value; }
    public event Action? Disconnected { add => _inner.Disconnected += value; remove => _inner.Disconnected -= value; }

    public Task ConnectAsync(string deviceId, CancellationToken cancellationToken = default) =>
        _inner.ConnectAsync(deviceId, cancellationToken);

    public Task DisconnectAsync() => _inner.DisconnectAsync();

    public Task SendLineAsync(string line, CancellationToken cancellationToken = default)
    {
        LineSent?.Invoke(line);
        return _inner.SendLineAsync(line, cancellationToken);
    }

    public Task SendRawByteAsync(byte value, CancellationToken cancellationToken = default) =>
        _inner.SendRawByteAsync(value, cancellationToken);
}
```

`LineSent` стреляет синхронно на том же потоке, что вызвал `SendLineAsync` — это может быть UI-поток (явная отправка из `ProgramViewModel`) или фоновый (таймер `JogScheduler`, поток чтения serial-порта при обработке status-report в `BufferAwareCommandQueue.UpdateBufferCapacity`). Маршалинг на UI-поток — забота подписчика (см. ниже), не декоратора.

## Изменения в `ConnectionViewModel`

- `public ObservableCollection<string> SentGCodeLines { get; } = new();` — хронологический порядок (старые сверху, новые снизу), лимит `MaxSentGCodeLines = 200`: при добавлении, если `Count > 200`, удаляется элемент с индексом 0.
- `[Reactive] private bool isGCodeLogOpen;` → `IsGCodeLogOpen`.
- `ToggleGCodeLogCommand` (`ReactiveCommand.Create(() => IsGCodeLogOpen = !IsGCodeLogOpen)`, через `Track(...).Enhance(...)` как остальные команды).
- Новое поле `private IDisposable? _sentGCodeSubscription;`.
- В `ConnectAsync`, там же, где сейчас выбирается `transport` (строка `var transport = SelectedEndpoint.Kind == ConnectionEndpointKind.Demo ? _createDemoTransport() : _realTransport;`):
  ```csharp
  if (_sentGCodeSubscription is not null)
  {
      Disposables.Remove(_sentGCodeSubscription);
  }

  var loggingTransport = new LoggingDeviceTransport(transport);
  SentGCodeLines.Clear();
  _sentGCodeSubscription = Observable.FromEvent<string>(
          h => loggingTransport.LineSent += h,
          h => loggingTransport.LineSent -= h)
      .ObserveOn(RxSchedulers.MainThreadScheduler)
      .Subscribe(AppendSentGCodeLine)
      .DisposeWith(Disposables);

  var session = _sessionFactory.Create(loggingTransport);
  ```
  `Disposables.Remove(...)` both removes the previous subscription from the composite and disposes it, so a stale subscription is never left registered on `Disposables`. The new subscription is chained with `.DisposeWith(Disposables)` so it is torn down automatically if the view model itself is disposed. `DisconnectAsync` performs the same `Disposables.Remove(_sentGCodeSubscription)` (then sets the field to `null`) so a manual disconnect stops appending too.
  (`loggingTransport`, не `transport`, передаётся дальше в `_sessionFactory.Create(...)`.)
- `AppendSentGCodeLine(string line)`:
  ```csharp
  private void AppendSentGCodeLine(string line)
  {
      SentGCodeLines.Add(line);
      if (SentGCodeLines.Count > MaxSentGCodeLines)
      {
          SentGCodeLines.RemoveAt(0);
      }
  }
  ```

Сброс лога происходит в начале каждого `ConnectAsync` (при первом подключении, при реконнекте на тот же endpoint, при переключении endpoint'а) — до создания новой сессии, независимо от того, успеет ли сам коннект завершиться успешно.

Подписка на `LineSent` использует тот же `Observable.FromEvent(...).ObserveOn(RxSchedulers.MainThreadScheduler)`, что уже применяется для `Session.ConnectionStateChanged` — то же самое место мутирует bound-состояние из потенциально фонового события, тот же механизм маршалинга, та же тестовая инфраструктура (`ReactiveUIBootstrap` подменяет `MainThreadScheduler` на `ImmediateScheduler` для тестов, синхронно).

## UI: `MainView.axaml`

**Кнопка в шапке** — в `WrapPanel#PlaybackButtons`, рядом с Play/Пауза/Стоп, всегда видима:
```xml
<Button Content="Лог G-code" Command="{Binding Connection.ToggleGCodeLogCommand}" />
```

**Оверлей** — новый `Border`, сосед существующих оверлеев (`IsEditingKeyPoint`, `PendingRename`, `PendingConfirmation`, `IsLibraryOpen`) внутри `Grid#RootPanel`:
```xml
<Border IsVisible="{Binding Connection.IsGCodeLogOpen}" Background="#CC0A0E12">
    <Border Width="420" MaxHeight="480" Background="{StaticResource HudPanelElevatedBrush}"
            BorderBrush="{StaticResource HudBorderStrongBrush}" BorderThickness="1"
            Padding="20" HorizontalAlignment="Center" VerticalAlignment="Center">
        <DockPanel>
            <Grid DockPanel.Dock="Top" ColumnDefinitions="*,Auto" Margin="0,0,0,10">
                <TextBlock Grid.Column="0" Classes="section-heading" Text="ЛОГ G-CODE" VerticalAlignment="Center" />
                <Button Grid.Column="1" Content="✕" Padding="8,2" Command="{Binding Connection.ToggleGCodeLogCommand}" />
            </Grid>
            <ListBox x:Name="GCodeLogList" ItemsSource="{Binding Connection.SentGCodeLines}">
                <ListBox.Styles>
                    <Style Selector="ListBoxItem">
                        <Setter Property="FontFamily" Value="{StaticResource HudFontMono}" />
                        <Setter Property="FontSize" Value="13" />
                        <Setter Property="Padding" Value="0,2" />
                    </Style>
                </ListBox.Styles>
            </ListBox>
        </DockPanel>
    </Border>
</Border>
```
Моноширинный шрифт (`HudFontMono` → JetBrains Mono) — тот же ресурс, что используется для остальных телеметрических значений (`PlaybackState`, координаты точек) через класс `telemetry`; здесь применяется напрямую к `ListBoxItem`, так как элементы списка — обычные `string`, а не объекты с DataTemplate.

**Автопрокрутка к последней строке** — код-behind в `MainView.axaml.cs`, тот же стиль, что `OnLibrarySelectionChanged`:
```csharp
public MainView()
{
    InitializeComponent();
    SizeChanged += OnSizeChanged;
    DataContextChanged += OnDataContextChanged;
}

private void OnDataContextChanged(object? sender, EventArgs e)
{
    if (DataContext is ProgramViewModel vm)
    {
        vm.Connection.SentGCodeLines.CollectionChanged -= OnSentGCodeLinesChanged;
        vm.Connection.SentGCodeLines.CollectionChanged += OnSentGCodeLinesChanged;
    }
}

private void OnSentGCodeLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
{
    if (e.Action == NotifyCollectionChangedAction.Add &&
        sender is ObservableCollection<string> { Count: > 0 } lines)
    {
        GCodeLogList.ScrollIntoView(lines[^1]);
    }
}
```
Подписка стоит постоянно (DataContext у `MainView` не меняется за время жизни приложения — один `ProgramViewModel` на всё приложение), поэтому обрезка (`RemoveAt(0)`, событие `Remove`) не запускает лишнюю прокрутку — реагируем только на `Add`.

## Обработка ошибок / граничные случаи

- Провал `ConnectAsync` (см. `catch` блок, который делает `session.DisconnectAsync()` и обнуляет `Session`) не требует отдельной очистки лога/подписки: `SentGCodeLines` уже очищен в начале попытки, `_sentGCodeSubscription` будет корректно заменена (со снятием старой через `Disposables.Remove(...)`) на следующей попытке подключения.
- Оверлей лога и модалка подключения (`IsConnectionModalVisible`) не конфликтуют: модалка рисуется поверх всего `Grid` (включая `RootPanel`), поэтому пока модалка видна, оверлей лога всё равно доступен только после подключения — кнопка в шапке физически недоступна, пока модалка блокирует экран (`DockPanel IsEnabled="{Binding !Connection.IsConnectionModalVisible}"`).

## Тесты

`ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs` (новые кейсы):
- отправка строки через `Session.SendGCodeAsync` после подключения добавляет её в `SentGCodeLines`;
- повторный `ConnectCommand.Execute()` (реконнект/смена endpoint'а) очищает `SentGCodeLines` от предыдущей сессии;
- превышение лимита в 200 записей обрезает самые старые (индекс 0), а не новые;
- `ToggleGCodeLogCommand` переключает `IsGCodeLogOpen`.

Новый `ArctZ.Tests/Services/Device/LoggingDeviceTransportTests.cs`:
- `SendLineAsync` поднимает `LineSent` с той же строкой и форвардит вызов во внутренний транспорт;
- `SendRawByteAsync`, `ConnectAsync`, `DisconnectAsync`, `LineReceived`, `Disconnected` прозрачно проксируются во внутренний транспорт (без побочных эффектов на `LineSent`).

## Затронутые файлы

- `ArctZ/Services/Device/LoggingDeviceTransport.cs` — новый декоратор
- `ArctZ/ViewModels/ConnectionViewModel.cs` — `SentGCodeLines`, `IsGCodeLogOpen`, `ToggleGCodeLogCommand`, оборачивание транспорта в `ConnectAsync`
- `ArctZ/Views/MainView.axaml` — кнопка в шапке, оверлей лога
- `ArctZ/Views/MainView.axaml.cs` — автопрокрутка `GCodeLogList`
- `ArctZ.Tests/ViewModels/ConnectionViewModelTests.cs` — новые тесты
- `ArctZ.Tests/Services/Device/LoggingDeviceTransportTests.cs` — новый файл тестов

## Не в скоупе

- Персистентность лога между запусками приложения / экспорт в файл — не запрашивалось.
- Фильтрация/поиск по логу, разделение джог- и явных команд визуально — не запрашивалось, лог показывает всё подряд как единый поток.
- Отображение входящих строк от устройства (`ok`/`error`/status-report) — задача только про **отправленные** команды.
