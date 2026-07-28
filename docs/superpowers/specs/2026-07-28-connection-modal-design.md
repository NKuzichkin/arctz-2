# Модальное окно подключения — дизайн

Дата: 2026-07-28

## Проблема

Сейчас выбор endpoint'а и кнопка «Подключить» лежат в той же панели верхнего хедера, что и статус, Homing и «Сброс аварии», и видны постоянно — даже пока подключение не установлено. Пользователю нужно, чтобы:

1. Подключение происходило в модальном окне, блокирующем весь экран, пока соединение не будет установлено.
2. На главном экране в постоянно видимой панели оставались только статус подключения и кнопка «Отключить» (Homing и «Сброс аварии» остаются там же, рядом со статусом).
3. Нажатие «Отключить» рвёт связь немедленно и снова показывает модальное окно выбора подключения — то же самое, что появляется при старте приложения.
4. Если связь обрывается неожиданно (устройство уходит в `Reconnecting`/`Disconnected` без явного нажатия «Отключить»), модальное окно должно появиться автоматически, блокируя экран.

## Инвариант

Главный экран разблокирован **только когда `ConnectionState == Connected`**. Модалка подключения видна во всех остальных случаях:
- `Session == null` (старт приложения, либо после явного отключения)
- `ConnectionState == Connecting` (идёт первое подключение)
- `ConnectionState == Reconnecting` (авто-реконнект после обрыва — см. `DeviceSession.OnTransportDisconnected`)
- `ConnectionState == Disconnected` при живой `Session` (реконнект исчерпал попытки)

Это единое условие `IsConnectionModalVisible` покрывает все четыре пункта проблемы без отдельной ветки логики на каждый случай.

## Изменения в `ConnectionViewModel`

- Новое `[ObservableProperty] private ConnectionState _connectionState` — зеркалирует `Session.ConnectionState`. Обновляется через подписку на `Session.ConnectionStateChanged` при смене `Session` (partial-метод `OnSessionChanged(IDeviceSession? oldValue, IDeviceSession? newValue)`, генерируемый `[ObservableProperty]` для `Session`): отписаться от старой сессии, подписаться на новую, синхронизировать `ConnectionState` сразу и на каждое последующее событие.
  - Это же чинит скрытый баг: `IDeviceSession` не реализует `INotifyPropertyChanged`, поэтому прямой биндинг `Session.ConnectionState` в XAML не обновляется при изменении состояния уже созданной сессии — обновлялся только при пересоздании `Session`. Новое свойство на самой `ConnectionViewModel` (которая — `ObservableObject`) даёт живой биндинг.
- Новое computed `bool IsConnectionModalVisible => Session is null || ConnectionState != ConnectionState.Connected;` с `[NotifyPropertyChangedFor(nameof(IsConnectionModalVisible))]` на `Session` и на `ConnectionState`.
- `ConnectAsync` получает `CanExecute`: `SelectedEndpoint is not null && ConnectionState is not (ConnectionState.Connecting or ConnectionState.Reconnecting)` — блокирует повторный запуск подключения поверх уже идущего попытки/реконнекта. Нужно вызывать `ConnectCommand.NotifyCanExecuteChanged()` там же, где меняется `ConnectionState` и `SelectedEndpoint`.
- `DisconnectAsync` не меняется: он уже обнуляет `Session` после `DisconnectAsync()`, и `IsConnectionModalVisible` реагирует на это автоматически. Отдельная команда «открыть модалку» не нужна.

## `ConnectionView.axaml` (верхняя статус-панель)

Остаётся видна на главном экране постоянно (когда `IsConnectionModalVisible == false`, т.е. фактически покрыта модалкой, когда `true`). Содержит:
- индикатор статуса (эллипс + текст), теперь биндится на `ConnectionState` вместо `Session.ConnectionState`
- кнопка **Homing**
- кнопка **Сброс аварии**
- кнопка **Отключить**

Комбобокс выбора endpoint и кнопка «Подключить» убираются отсюда — переезжают в модалку.

## Модальный оверлей в `MainView.axaml`

Корневой `<DockPanel>` оборачивается в `<Grid>`; вторым (верхним по Z) ребёнком — оверлей:

```xml
<Border IsVisible="{Binding Connection.IsConnectionModalVisible}" Background="#CC0A0E12">
    <Border x:DataType="vm:ConnectionViewModel" DataContext="{Binding Connection}"
            Width="360" Background="{StaticResource HudPanelElevatedBrush}"
            BorderBrush="{StaticResource HudBorderStrongBrush}" BorderThickness="1"
            Padding="20" HorizontalAlignment="Center" VerticalAlignment="Center">
        <StackPanel Spacing="14">
            <TextBlock Classes="section-heading" Text="ПОДКЛЮЧЕНИЕ" />
            <StackPanel Orientation="Horizontal" Spacing="8">
                <Ellipse Width="8" Height="8" Fill="{Binding ConnectionState, Converter={StaticResource StateToBrush}}" />
                <TextBlock Text="{Binding ConnectionState, Converter={StaticResource StateToLabel}}" />
            </StackPanel>
            <ComboBox ItemsSource="{Binding AvailableEndpoints}" SelectedItem="{Binding SelectedEndpoint}"
                      DisplayMemberBinding="{Binding DisplayName}" />
            <Button Classes="primary" Content="Подключить" Command="{Binding ConnectCommand}" HorizontalAlignment="Stretch" />
        </StackPanel>
    </Border>
</Border>
```

Оверлей — полноэкранный (покрывает `Grid`, а не только контентную зону, как существующие оверлеи `IsEditingKeyPoint`/`PendingConfirmation` внутри `RootPanel`), поэтому блокирует и хедер, и панель библиотеки, и рабочую область целиком.

## Затронутые файлы

- `ArctZ/ViewModels/ConnectionViewModel.cs` — `ConnectionState`, `IsConnectionModalVisible`, `CanExecute` для `ConnectAsync`
- `ArctZ/Views/ConnectionView.axaml` — убрать комбобокс/«Подключить», перевести биндинг статуса на `ConnectionState`
- `ArctZ/Views/MainView.axaml` — обернуть `DockPanel` в `Grid`, добавить оверлей модалки

## Не в скоупе

- Таймаут/отмена зависшего `Connecting` — не запрашивалось, существующая логика транспорта не трогается.
- Изменение поведения авто-реконнекта в `DeviceSession` — модалка только отражает существующие состояния, не меняет логику `IReconnectPolicy`.
- Тесты — в решении нет тестовых проектов (см. CLAUDE.md); проверка через `dotnet build` и ручной прогон в Desktop-хосте.
