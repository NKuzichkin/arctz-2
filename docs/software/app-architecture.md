# Архитектура приложения

Проект — Avalonia UI (.NET 10, MVVM через `CommunityToolkit.Mvvm`), общий код
в `ArctZ/`, тонкие платформенные точки входа в `ArctZ.Desktop/`,
`ArctZ.Android/`, `ArctZ.iOS/`, `ArctZ.Browser/`. Подробности — в
`AI_AGENT_README.md` и корневом `CLAUDE.md`.

## Текущее состояние

Управление устройством реализовано целиком в `ArctZ/Services/Device/`, не в
платформенных головах:

- `IDeviceTransport` — абстракция транспорта (serial/Bluetooth-COM);
  `Simulation/MockDeviceTransport` — рабочая симуляция контроллера (буферы,
  real-time-байты, таймер движения) для демо-режима без реального устройства.
  Платформенные точки входа регистрируют свою реализацию `IDeviceTransport`
  для реального устройства.
- `IDeviceSession`/`DeviceSession` — жизненный цикл соединения: подключение/
  отключение, реакция на разрыв связи (`FixedDelayReconnectPolicy`),
  трансляция входящих строк через `IStatusParser`, генерация состояния
  (`ConnectionState`, `DeviceStatus`).
- `FluidNcCommandSerializer`/`FluidNcStatusParser` — диалект G-code/GRBL и
  разбор статус-ответов FluidNC (`<Idle|WPos:...|Bf:...>`).
- `JogCommandFactory`/`JogScheduler` — перевод состояния двух джойстиков
  (`DualJoystickState`) в команды `$J=` с троттлингом по таймеру и отменой
  джога (`0x85`) при отпускании.
- `BufferAwareCommandQueue` — очередь исходящих G-code команд с учётом
  доступного буфера контроллера (`Bf:` из статус-отчёта).
- `Services/Program/` — программы-траектории: `JibProgram`/`KeyPoint` (ключевые
  точки), `TrajectoryCompiler` (компиляция в сегменты движения с ease-режимами),
  `JsonFileProgramStorage` (сохранение/загрузка на диск).

Слой ViewModels/Views:

- `ViewModels/ConnectionViewModel.cs` — подключение к реальному устройству или
  демо-транспорту, зеркалит `IDeviceSession.ConnectionState`, отвечает за
  видимость модального окна подключения (`IsConnectionModalVisible`).
- `ViewModels/ProgramViewModel.cs` — главная вью-модель: режимы
  «Программирование»/«Выполнение», библиотека сохранённых программ, авторинг
  ключевых точек (через `KeyPointEditorViewModel`), воспроизведение
  скомпилированной траектории.
- `Components/VirtualJoystick/` — кастомный control для тач/мышь-ввода
  (режимы `Fixed`/`Semi`/`Dynamic`, форма `Circle`/`Box`, блокировка оси,
  события `JoystickDown`/`Move`/`Up` с `Force`/`AngleDeg`/`Direction`).
  Design-спека контрола — `Components/VirtualJoystick/virtual-joystick.md`.
  `ViewModels/JoystickInputMapper.cs` переводит эти события в нормализованные
  оси `X`/`Y` для `Services/Device`.
- `Views/MainView.axaml` — единственный экран: библиотека программ, панели
  авторинга/воспроизведения, оверлеи редактора точки/подтверждения/подключения.
  DataContext — `ProgramViewModel`.

Регистрация DI для платформо-независимой части — `Services/Device/ServiceCollectionExtensions.cs`
(`AddArctZCore`); каждая платформенная голова дополнительно регистрирует свой
`IDeviceTransport` (реальный транспорт) и `IProgramStorage` (расположение
хранилища).

Тесты — `ArctZ.Tests/`, детально покрывают `Services/Device` и
`Services/Program` (буферы, реконнект, планирование джога, сериализация/парсинг,
компиляция траектории), часть `ViewModels` (`ConnectionViewModel`,
`ProgramViewModel`, `JoystickInputMapper`), DI-регистрацию
(`ServiceCollectionExtensionsTests`) и `Components/VirtualJoystick` — через
`Avalonia.Headless` с ручной инициализацией платформы (`ArctZ.Tests/TestApp.cs`),
без пакета `Avalonia.Headless.XUnit` (несовместим по версии xunit с остальными
тестами проекта).

## Известные открытые вопросы

- [ ] Библиотека для serial/Bluetooth-COM на стороне .NET кроссплатформенно —
      единого API может не быть, особенно в Browser/WASM (см.
      [`../protocol/bluetooth-gcode-control.md`](../protocol/bluetooth-gcode-control.md)).
- [ ] Визуальная проверка знака оси Y в `JoystickInputMapper` против реального
      контрола (см. комментарий в файле, помечено как непроверенное).
