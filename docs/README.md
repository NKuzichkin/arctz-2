# Документация проекта ArctZ

Этот каталог содержит проектную документацию по разработке функциональной копии
камерного джиба Edelkrone JibONE — механики устройства и программного обеспечения
для управления им.

> `docs/superpowers/` — служебный каталог, создаваемый навыками (skills) Claude Code
> (планы/спецификации отдельных задач, например CI). Он не относится к документации
> проекта как таковой — не путать с разделами ниже.

## Содержание

- **Для пользователей** (не черновик — см. «Статус» ниже)
  - [`user-guide.md`](user-guide.md) — руководство пользователя приложения ArctZ: быстрый старт, безопасность, подключение, главный экран, программы и ключевые точки, диагностика, глоссарий. Иллюстрировано скриншотами из [`screenshots/`](../screenshots/).
    Скриншоты перегенерируются командой `dotnet test ArctZ.Tests.Screenshots/ArctZ.Tests.Screenshots.csproj`; при изменении интерфейса обновляйте и их, и текст руководства (включая строку «Документ соответствует состоянию приложения на …» в его начале).
  - Экспорт в PDF (страницы A5): `docs/export-user-guide-pdf.ps1` → `docs/user-guide.pdf`. Рендерит markdown через системный Chrome в headless-режиме; при первом запуске ставит зависимости в `docs/tools/pdf-export/` (нужен интернет один раз). См. подробности в самом скрипте (`Get-Help ./docs/export-user-guide-pdf.ps1 -Full`).
    `docs/user-guide.pdf` — генерируемый артефакт: он в `.gitignore`, руками его не правят, при изменении руководства перегенерируют скриптом.
- **Исследование**
  - [`research/edelkrone-jibone-reference.md`](research/edelkrone-jibone-reference.md) — характеристики оригинального Edelkrone JibONE (версии v1/v3, Pan Module, JibPLUS) и открытые вопросы по их повторению.
- **Механика (`hardware/`)**
  - [`hardware/mechanics.md`](hardware/mechanics.md) — оси движения, приводы, балансировка, габариты и материалы копии.
- **Прошивка (`firmware/`)**
  - [`firmware/fluidnc-setup.md`](firmware/fluidnc-setup.md) — настройка платы на FluidNC: формат `config.yaml`, секции `axes`/`stepping`/`homing`, типы моторов/драйверов, требования к Bluetooth-сборке.
  - [`firmware/fluidnc-slow-motion-limits.md`](firmware/fluidnc-slow-motion-limits.md) — нижняя граница скорости FluidNC: порог `MINIMUM_FEED_RATE`, минимальная частота шага (`max(1.0, 2290 / steps_per_mm)`), мгновенное исполнение движений короче шага, ненадёжность `G93`. Плюс обратная сторона порогов — сколько максимум может длиться одна команда (семантика `ok`, предел `G4`, ограничение времени `G93` длиной перемещения). Разбор issue [#1715](https://github.com/bdring/FluidNC/issues/1715), [#1372](https://github.com/bdring/FluidNC/issues/1372), [#772](https://github.com/bdring/FluidNC/issues/772) и исходников прошивки + следствия для `TrajectoryCompiler`/`JogCommandFactory` и выбора редукции.
- **Протокол (`protocol/`)**
  - [`protocol/bluetooth-gcode-control.md`](protocol/bluetooth-gcode-control.md) — связь приложение ↔ плата: Bluetooth SPP (виртуальный COM-порт), диалект G-code, jog-режим (`$J=`), realtime-команды, статус-ответы, маппинг джойстика на команды.
  - [`protocol/gcode_sender_architecture.md`](protocol/gcode_sender_architecture.md) — обзор архитектурных паттернов существующих G-code сендеров (UGS, cncjs, bCNC, ioSender и др.) для GRBL/FluidNC: потоковые протоколы, буферы, джоггинг, конечный автомат состояний.
  - [`research/gcode-sender-architecture-fluidnc-grbl/report.md`](research/gcode-sender-architecture-fluidnc-grbl/report.md) — детальный исследовательский отчёт, на основе которого написан `gcode_sender_architecture.md` (плюс сырые данные по каждому сендеру в `results/`).
- **Программное обеспечение (`software/`)**
  - [`software/app-architecture.md`](software/app-architecture.md) — архитектура приложения (`ArctZ/`, Avalonia/MVVM), текущий код (`VirtualJoystick`, `MainViewModel`) и планируемые дополнения.

## Статус

Проектные разделы (исследование, механика, прошивка, протокол, ПО) на данный момент —
**черновики** (`DRAFT`). Они фиксируют текущее понимание задачи и открытые вопросы,
а не финальные решения. Помечайте закрытые вопросы и удаляйте статус `DRAFT`,
когда раздел стабилизируется.

Руководство пользователя (`user-guide.md`) под это правило **не** подпадает: оно описывает
не замысел, а фактическое поведение текущей сборки, поэтому его правят вместе с интерфейсом.
