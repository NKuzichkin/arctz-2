# Подписи джойстиков + чистка подсказок из меню программы Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Заменить всплывающие `ToolTip.Tip` у джойстиков постоянно видимыми подписями снизу и убрать дублирующие/бесполезные подсказки из раздела «ПРОГРАММА».

**Architecture:** Чисто UI-изменение в `ArctZ/Views/MainView.axaml` (+ code-behind `MainView.axaml.cs` для формулы радиуса). Каждый `VirtualJoystick` оборачивается в `StackPanel` с подписью снизу; `MaxWidth` подписи привязан к диаметру джойстика через существующий `RadiusToSizeConverter`, чтобы не раздувать колонку `Auto`. Формула `ComputeJoystickRadius` резервирует место под подпись.

**Tech Stack:** Avalonia UI (XAML + code-behind), xUnit (`ArctZ.Tests`).

## Global Constraints

- Подпись левого джойстика: «Подъём / поворот стрелы».
- Подпись правого джойстика: «Пан / наклон камеры».
- Подписи — снизу от джойстика, `Opacity="0.6"`, `FontSize="12"`, `TextWrapping="Wrap"`, по центру.
- `ToolTip.Tip` полностью убирается с обоих `VirtualJoystick`.
- Новая константа в `MainView.axaml.cs`: `JoystickLabelReservedHeight = 36`.
- Убрать `TextBlock` (описание джойстиков в разделе «ПРОГРАММА») и `ToolTip.Tip` у пунктов меню «Захватить» и «На точку» — сами команды не менять.
- Финальная проверка — только через build → run → `AskUserQuestion` по каждому изменённому поведению (правило проекта, `CLAUDE.md`).

---

### Task 1: Формула радиуса джойстика резервирует место под подпись

**Files:**
- Modify: `ArctZ/Views/MainView.axaml.cs:20-24` (константы), `ArctZ/Views/MainView.axaml.cs:74-80` (формула)
- Test: `ArctZ.Tests/Views/MainViewJoystickRadiusTests.cs:12-15`

**Interfaces:**
- Consumes: ничего нового (работает с уже существующими `Radius`, `MinRadius`, `MaxRadius` и т.д.).
- Produces: `MainView.ComputeJoystickRadius(double, double, double)` — сигнатура не меняется, меняется только возвращаемое значение (уменьшается на счёт нового `JoystickLabelReservedHeight`). Task 2 не зависит от этого изменения напрямую (использует `Radius` реактивно через биндинг), но полагается на то, что радиус по-прежнему корректно вписывается в `JoystickBar` вместе с подписью.

- [ ] **Step 1: Обновить ожидаемые значения в тесте под новую формулу**

В `ArctZ.Tests/Views/MainViewJoystickRadiusTests.cs` замените 4 строки `InlineData` (текущие строки 12-15):

Было:
```csharp
    [InlineData(1000, 500, 60, 101)]     // высота ограничивает, но не floor/ceiling
    [InlineData(500, 400, 0, 59)]        // headerHeight=0 → включается HeaderFallbackHeight
    [InlineData(500, 400, -10, 59)]      // отрицательный headerHeight тоже триггерит фолбэк
    [InlineData(500, 400, 1, 80.5)]      // headerHeight=1 (>0) — фолбэк НЕ включается, используется как есть
```

Стало:
```csharp
    [InlineData(1000, 500, 60, 83)]      // высота ограничивает, но не floor/ceiling
    [InlineData(500, 400, 0, 50)]        // headerHeight=0 → включается HeaderFallbackHeight
    [InlineData(500, 400, -10, 50)]      // отрицательный headerHeight тоже триггерит фолбэк
    [InlineData(500, 400, 1, 62.5)]      // headerHeight=1 (>0) — фолбэк НЕ включается, используется как есть
```

Остальные 6 `InlineData` (широкий десктоп/узкий телефон/floor-clamp по ширине/floor-clamp по высоте/отрицательный бюджет/вырожденный 0×0 случаи) не меняются — резерв под подпись в них не влияет на итоговый результат (либо уже упираются в `MinRadius`/`MaxRadius`, либо лимитирует ширина).

- [ ] **Step 2: Запустить тест и убедиться, что 4 кейса падают**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~MainViewJoystickRadiusTests"`
Expected: FAIL — 4 из 10 кейсов (1000×500×60, 500×400×0, 500×400×-10, 500×400×1) не совпадают с текущей реализацией.

- [ ] **Step 3: Добавить константу и поправить формулу**

В `ArctZ/Views/MainView.axaml.cs`, после существующей `ProgramPanelMinHeight` (текущие строки 20-24):

Было:
```csharp
        // Border(reveal-3).Margin(12→12+12=24 верт.) + BorderThickness(1+1=2)
        private const double ContentBorderVerticalChrome = 26;
        // ContentGrid.Margin(20+20=40 верт.)
        private const double ContentGridVerticalMargin = 40;
        private const double JoystickBarTopMargin = 12;
        private const double ProgramPanelMinHeight = 160;
```

Стало:
```csharp
        // Border(reveal-3).Margin(12→12+12=24 верт.) + BorderThickness(1+1=2)
        private const double ContentBorderVerticalChrome = 26;
        // ContentGrid.Margin(20+20=40 верт.)
        private const double ContentGridVerticalMargin = 40;
        private const double JoystickBarTopMargin = 12;
        private const double ProgramPanelMinHeight = 160;
        // Подпись под джойстиком: StackPanel Spacing=4 + до 2 строк текста FontSize=12
        private const double JoystickLabelReservedHeight = 36;
```

В том же файле, в `ComputeJoystickRadius` (текущие строки 74-80):

Было:
```csharp
            var contentGridHeight = mainViewHeight - effectiveHeaderHeight - ContentBorderVerticalChrome
                - ContentGridVerticalMargin - JoystickBarTopMargin;
            var joystickRowBudget = contentGridHeight - ProgramPanelMinHeight;
            var heightRadius = joystickRowBudget / 2;
```

Стало:
```csharp
            var contentGridHeight = mainViewHeight - effectiveHeaderHeight - ContentBorderVerticalChrome
                - ContentGridVerticalMargin - JoystickBarTopMargin;
            var joystickRowBudget = contentGridHeight - ProgramPanelMinHeight;
            var heightRadius = (joystickRowBudget - JoystickLabelReservedHeight) / 2;
```

- [ ] **Step 4: Запустить тест и убедиться, что все 10 кейсов проходят**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter "FullyQualifiedName~MainViewJoystickRadiusTests"`
Expected: PASS — все 10 кейсов.

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Views/MainView.axaml.cs ArctZ.Tests/Views/MainViewJoystickRadiusTests.cs
git commit -m "$(cat <<'EOF'
feat: reserve joystick radius budget for caption below joystick

EOF
)"
```

---

### Task 2: Постоянные подписи под джойстиками вместо ToolTip

**Files:**
- Modify: `ArctZ/Views/MainView.axaml:15-18` (ресурсы), `ArctZ/Views/MainView.axaml:211-220` (JoystickBar)

**Interfaces:**
- Consumes: существующий `ArctZ.Components.VirtualJoystick.RadiusToSizeConverter` (публичный класс, уже используется контролом джойстика внутренне; здесь регистрируется как ресурс `MainView` и используется в биндинге `MaxWidth`). Существующее свойство `VirtualJoystick.Radius` (`StyledProperty<double>`).
- Produces: визуальные подписи под `LeftJoystick`/`RightJoystick`; далее ни от чего не зависит.

- [ ] **Step 1: Зарегистрировать конвертер как ресурс**

В `ArctZ/Views/MainView.axaml`, в `<UserControl.Resources>` (текущие строки 15-18):

Было:
```xml
    <UserControl.Resources>
        <conv:ConnectionStateToBrushConverter x:Key="StateToBrush" />
        <conv:LabelLengthToFontSizeConverter x:Key="LabelLengthToFontSize" />
    </UserControl.Resources>
```

Стало:
```xml
    <UserControl.Resources>
        <conv:ConnectionStateToBrushConverter x:Key="StateToBrush" />
        <conv:LabelLengthToFontSizeConverter x:Key="LabelLengthToFontSize" />
        <js:RadiusToSizeConverter x:Key="RadiusToSize" />
    </UserControl.Resources>
```

(`xmlns:js="using:ArctZ.Components.VirtualJoystick"` уже объявлен на корневом `UserControl`, дополнительный `xmlns` не нужен.)

- [ ] **Step 2: Обернуть джойстики в StackPanel с подписью, убрать ToolTip**

В `ArctZ/Views/MainView.axaml`, блок `JoystickBar` (текущие строки 211-220):

Было:
```xml
                        <Grid x:Name="JoystickBar" Grid.Row="1" ColumnDefinitions="Auto,*,Auto" Margin="0,12,0,0">
                            <js:VirtualJoystick x:Name="LeftJoystick" Grid.Column="0" Mode="Fixed" Shape="Circle"
                                                 VerticalAlignment="Center" IsEnabled="{Binding !IsProgramLocked}"
                                                 ToolTip.Tip="Левый джойстик: подъём и поворот стрелы (X · Y)"
                                                 JoystickDown="OnLeftJoystickDown" JoystickMove="OnLeftJoystickMove" JoystickUp="OnLeftJoystickUp" />
                            <js:VirtualJoystick x:Name="RightJoystick" Grid.Column="2" Mode="Fixed" Shape="Circle"
                                                 VerticalAlignment="Center" IsEnabled="{Binding !IsProgramLocked}"
                                                 ToolTip.Tip="Правый джойстик: пан и наклон камеры (Z · A)"
                                                 JoystickDown="OnRightJoystickDown" JoystickMove="OnRightJoystickMove" JoystickUp="OnRightJoystickUp" />
                        </Grid>
```

Стало:
```xml
                        <Grid x:Name="JoystickBar" Grid.Row="1" ColumnDefinitions="Auto,*,Auto" Margin="0,12,0,0">
                            <StackPanel Grid.Column="0" Spacing="4" HorizontalAlignment="Center" VerticalAlignment="Center">
                                <js:VirtualJoystick x:Name="LeftJoystick" Mode="Fixed" Shape="Circle"
                                                     IsEnabled="{Binding !IsProgramLocked}"
                                                     JoystickDown="OnLeftJoystickDown" JoystickMove="OnLeftJoystickMove" JoystickUp="OnLeftJoystickUp" />
                                <TextBlock Text="Подъём / поворот стрелы" Opacity="0.6" FontSize="12"
                                           TextWrapping="Wrap" TextAlignment="Center" HorizontalAlignment="Center"
                                           MaxWidth="{Binding Radius, ElementName=LeftJoystick, Converter={StaticResource RadiusToSize}}" />
                            </StackPanel>
                            <StackPanel Grid.Column="2" Spacing="4" HorizontalAlignment="Center" VerticalAlignment="Center">
                                <js:VirtualJoystick x:Name="RightJoystick" Mode="Fixed" Shape="Circle"
                                                     IsEnabled="{Binding !IsProgramLocked}"
                                                     JoystickDown="OnRightJoystickDown" JoystickMove="OnRightJoystickMove" JoystickUp="OnRightJoystickUp" />
                                <TextBlock Text="Пан / наклон камеры" Opacity="0.6" FontSize="12"
                                           TextWrapping="Wrap" TextAlignment="Center" HorizontalAlignment="Center"
                                           MaxWidth="{Binding Radius, ElementName=RightJoystick, Converter={StaticResource RadiusToSize}}" />
                            </StackPanel>
                        </Grid>
```

(`VerticalAlignment="Center"` переехал с самого `VirtualJoystick` на оборачивающий `StackPanel` — центрирование по-прежнему держит группу [джойстик+подпись] по центру строки, а не только джойстик.)

- [ ] **Step 3: Собрать проект и убедиться, что XAML компилируется**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: Build succeeded, без ошибок компиляции биндингов (compiled bindings строгие — `ElementName`+`Converter` биндинг должен резолвиться без `x:DataType` ошибок, т.к. `Radius` берётся с самого `ElementName`-элемента, а не из `DataContext`).

- [ ] **Step 4: Commit**

```bash
git add ArctZ/Views/MainView.axaml
git commit -m "$(cat <<'EOF'
feat: show permanent captions under joysticks instead of tooltips

EOF
)"
```

---

### Task 3: Убрать подсказки из раздела «ПРОГРАММА»

**Files:**
- Modify: `ArctZ/Views/MainView.axaml:135-136` (текстовый блок), `ArctZ/Views/MainView.axaml:148-149` (ToolTip у «Захватить»), `ArctZ/Views/MainView.axaml:170-172` (ToolTip у «На точку»)

**Interfaces:**
- Consumes: ничего (не зависит от Task 1/2).
- Produces: ничего, потребляемого другими задачами.

- [ ] **Step 1: Удалить дублирующий текстовый блок с описанием джойстиков**

В `ArctZ/Views/MainView.axaml`, внутри `StackPanel Spacing="10"` раздела «ПРОГРАММА» (текущие строки 134-136):

Было:
```xml
                                <TextBlock Classes="section-heading" Text="ПРОГРАММА" />
                                <TextBlock Opacity="0.6" FontSize="12" TextWrapping="Wrap"
                                           Text="Левый джойстик — подъём/поворот стрелы (X·Y). Правый — пан/наклон камеры (Z·A)." />
                                <Grid ColumnDefinitions="*,Auto">
```

Стало:
```xml
                                <TextBlock Classes="section-heading" Text="ПРОГРАММА" />
                                <Grid ColumnDefinitions="*,Auto">
```

- [ ] **Step 2: Убрать ToolTip.Tip у пункта меню «Захватить»**

Текущие строки 148-149:

Было:
```xml
                                                <MenuItem Header="Захватить" Command="{Binding CaptureKeyPointCommand}"
                                                          ToolTip.Tip="Нужно подключение к станку и известная текущая позиция" />
```

Стало:
```xml
                                                <MenuItem Header="Захватить" Command="{Binding CaptureKeyPointCommand}" />
```

- [ ] **Step 3: Убрать ToolTip.Tip у пункта меню «На точку»**

Текущие строки 170-172:

Было:
```xml
                                                        <MenuItem Header="На точку" CommandParameter="{Binding}"
                                                                  ToolTip.Tip="Отправить станок к этой точке"
                                                                  Command="{Binding ((vm:ProgramViewModel)DataContext).MoveMachineToKeyPointCommand, ElementName=KeyPointsList}" />
```

Стало:
```xml
                                                        <MenuItem Header="На точку" CommandParameter="{Binding}"
                                                                  Command="{Binding ((vm:ProgramViewModel)DataContext).MoveMachineToKeyPointCommand, ElementName=KeyPointsList}" />
```

- [ ] **Step 4: Собрать проект**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Views/MainView.axaml
git commit -m "$(cat <<'EOF'
chore: remove redundant joystick/menu hint tooltips from program panel

EOF
)"
```

---

### Task 4: Ручная UI-проверка

**Files:** нет изменений кода — только запуск приложения и подтверждение у пользователя (обязательный шаг по `CLAUDE.md`: build → run → user checks → `AskUserQuestion` по каждому пункту).

**Interfaces:**
- Consumes: результат Task 1-3 (полностью собранное приложение).
- Produces: подтверждение, что фича готова к завершению ветки.

- [ ] **Step 1: Собрать и запустить Desktop head**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Run: `dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: приложение запускается.

- [ ] **Step 2: Прогнать полный набор тестов**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS, без регрессий в других тестах.

- [ ] **Step 3: Попросить пользователя проверить и задать вопросы через AskUserQuestion**

Проверить по одному вопросу на каждый пункт:
- Подписи «Подъём / поворот стрелы» и «Пан / наклон камеры» видны под соответствующими джойстиками и не обрезаются/не наезжают друг на друга.
- Подписи не перекрываются пальцем/курсором при активном перетаскивании джойстика.
- При изменении размера окна джойстики по-прежнему масштабируются (не обрезаются, не наезжают на текст программы) — подпись масштабируется вместе с ними.
- В разделе «ПРОГРАММА» больше нет текста про управление джойстиками.
- В меню «⋮» (пункт «Захватить») и в меню точки (пункт «На точку») больше нет всплывающих подсказок при наведении.

## Self-Review Notes

- **Spec coverage:** требование 1 (постоянные подписи, не перекрывающиеся пальцем) → Task 2 + Global Constraints (текст снизу). Требование 2 (убрать подсказки из раздела «ПРОГРАММА») → Task 3. Требование 3 (не ломать раскладку/расчёт радиуса) → Task 1 (формула) + Task 2 (`MaxWidth` привязка). Все три пункта спеки покрыты.
- **Placeholder scan:** нет TBD/TODO, все шаги содержат конкретный код и команды.
- **Type consistency:** `RadiusToSize` — единое имя ресурса в Task 2 (регистрация и оба использования). `JoystickLabelReservedHeight` — единое имя константы в Task 1 (объявление и использование в формуле). `LeftJoystick`/`RightJoystick` `x:Name` не менялись, продолжают использоваться в code-behind без изменений.
