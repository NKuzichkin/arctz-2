# Документация проекта ArctZ

Этот каталог содержит проектную документацию по разработке функциональной копии
камерного джиба Edelkrone JibONE — механики устройства и программного обеспечения
для управления им.

> `docs/superpowers/` — служебный каталог, создаваемый навыками (skills) Claude Code
> (планы/спецификации отдельных задач, например CI). Он не относится к документации
> проекта как таковой — не путать с разделами ниже.

## Содержание

- **Исследование**
  - [`research/edelkrone-jibone-reference.md`](research/edelkrone-jibone-reference.md) — характеристики оригинального Edelkrone JibONE (версии v1/v3, Pan Module, JibPLUS) и открытые вопросы по их повторению.
- **Механика (`hardware/`)**
  - [`hardware/mechanics.md`](hardware/mechanics.md) — оси движения, приводы, балансировка, габариты и материалы копии.
- **Прошивка (`firmware/`)**
  - [`firmware/fluidnc-setup.md`](firmware/fluidnc-setup.md) — настройка платы на FluidNC: формат `config.yaml`, секции `axes`/`stepping`/`homing`, типы моторов/драйверов, требования к Bluetooth-сборке.
- **Протокол (`protocol/`)**
  - [`protocol/bluetooth-gcode-control.md`](protocol/bluetooth-gcode-control.md) — связь приложение ↔ плата: Bluetooth SPP (виртуальный COM-порт), диалект G-code, jog-режим (`$J=`), realtime-команды, статус-ответы, маппинг джойстика на команды.
- **Программное обеспечение (`software/`)**
  - [`software/app-architecture.md`](software/app-architecture.md) — архитектура приложения (`ArctZ/`, Avalonia/MVVM), текущий код (`VirtualJoystick`, `MainViewModel`) и планируемые дополнения.

## Статус

Все файлы в перечисленных разделах на данный момент — **черновики** (`DRAFT`).
Они фиксируют текущее понимание задачи и открытые вопросы, а не финальные решения.
Помечайте закрытые вопросы и удаляйте статус `DRAFT`, когда раздел стабилизируется.
