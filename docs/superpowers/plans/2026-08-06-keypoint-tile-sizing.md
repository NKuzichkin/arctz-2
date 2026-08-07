# Плашки точек программы: лимит названия, фиксированный размер, сжимающийся шрифт — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** В блоке «ТОЧКИ» (`MainView.axaml`) ограничить название точки 30 символами, зафиксировать размер плашки точки независимо от длины названия и умеренно увеличить сами плашки, вместо роста плашки — ступенчато уменьшать шрифт названия.

**Architecture:** Один новый чистый конвертер `LabelLengthToFontSizeConverter` (со статическим методом `ComputeFontSize`, тестируемым напрямую — по образцу `MainView.ComputeJoystickRadius`) плюс точечные правки XAML в `MainView.axaml`: `MaxLength` на `TextBox` редактора точки, фиксированные `Width`/`Height` на `Button`-плашке, `FontSize` через новый конвертер и `TextWrapping`/`MaxLines`/`TextTrimming` на `TextBlock` названия как подстраховка.

**Tech Stack:** Avalonia 12 (XAML + `IValueConverter`), xUnit (`ArctZ.Tests`).

## Global Constraints

- Лимит названия точки: 30 символов (`docs/superpowers/specs/2026-08-06-keypoint-tile-sizing-design.md`).
- Размер плашки точки: фиксированный `Width="120" Height="60"`, не зависит от длины `Label`.
- Шаги `FontSize` по длине `Label`: ≤10 → 16, ≤18 → 14, ≤26 → 12, ≤30 → 10.
- Подстраховка от переполнения: `TextWrapping="Wrap" MaxLines="2" TextTrimming="CharacterEllipsis"`.
- UI-изменения проверяются только по стандартному воркфлоу проекта (build → run → пользователь тестирует → `AskUserQuestion` по каждому пункту) — см. `CLAUDE.md`, раздел «Тестирование UI».

---

### Task 1: `LabelLengthToFontSizeConverter`

**Files:**
- Create: `ArctZ/Converters/LabelLengthToFontSizeConverter.cs`
- Test: `ArctZ.Tests/Converters/LabelLengthToFontSizeConverterTests.cs`

**Interfaces:**
- Produces: `ArctZ.Converters.LabelLengthToFontSizeConverter` — публичный класс, реализует `Avalonia.Data.Converters.IValueConverter`. Публичный статический метод `public static double ComputeFontSize(string? label)`, используемый как самим конвертером, так и юнит-тестами напрямую (без хождения через `Convert`/`Avalonia.Data`).

- [ ] **Step 1: Write the failing test**

```csharp
using ArctZ.Converters;

namespace ArctZ.Tests.Converters;

public class LabelLengthToFontSizeConverterTests
{
    [Theory]
    [InlineData(null, 16)]
    [InlineData("", 16)]
    [InlineData("Точка 1", 16)]           // 7 симв. -> <=10
    [InlineData("1234567890", 16)]        // ровно 10 -> <=10
    [InlineData("12345678901", 14)]       // 11 симв. -> <=18
    [InlineData("123456789012345678", 14)] // ровно 18 -> <=18
    [InlineData("1234567890123456789", 12)] // 19 симв. -> <=26
    [InlineData("12345678901234567890123456", 12)] // ровно 26 -> <=26
    [InlineData("123456789012345678901234567", 10)] // 27 симв. -> <=30
    [InlineData("123456789012345678901234567890", 10)] // ровно 30 -> <=30
    public void ComputeFontSize_ReturnsExpectedStep(string? label, double expected)
    {
        var fontSize = LabelLengthToFontSizeConverter.ComputeFontSize(label);

        Assert.Equal(expected, fontSize);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter LabelLengthToFontSizeConverterTests`
Expected: FAIL (build error — `LabelLengthToFontSizeConverter` не существует).

- [ ] **Step 3: Write minimal implementation**

```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ArctZ.Converters;

public class LabelLengthToFontSizeConverter : IValueConverter
{
    public static double ComputeFontSize(string? label)
    {
        var length = label?.Length ?? 0;
        return length switch
        {
            <= 10 => 16,
            <= 18 => 14,
            <= 26 => 12,
            _ => 10,
        };
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ComputeFontSize(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj --filter LabelLengthToFontSizeConverterTests`
Expected: PASS (10 тестов).

- [ ] **Step 5: Commit**

```bash
git add ArctZ/Converters/LabelLengthToFontSizeConverter.cs ArctZ.Tests/Converters/LabelLengthToFontSizeConverterTests.cs
git commit -m "feat: add LabelLengthToFontSizeConverter for keypoint tile labels"
```

---

### Task 2: Разметка плашки точки в `MainView.axaml`

**Files:**
- Modify: `ArctZ/Views/MainView.axaml:1-16` (регистрация конвертера в `UserControl.Resources`)
- Modify: `ArctZ/Views/MainView.axaml:162-185` (`DataTemplate` плашки точки)
- Modify: `ArctZ/Views/MainView.axaml:227` (`TextBox` названия в редакторе точки)

**Interfaces:**
- Consumes: `ArctZ.Converters.LabelLengthToFontSizeConverter` из Task 1.

- [ ] **Step 1: Зарегистрировать конвертер в ресурсах `MainView.axaml`**

Файл уже импортирует `xmlns:conv="using:ArctZ.Converters"` (строка 8) и содержит блок
`UserControl.Resources` с `<conv:ConnectionStateToBrushConverter x:Key="StateToBrush" />`
(строка 16). Добавить рядом:

```xml
<conv:LabelLengthToFontSizeConverter x:Key="LabelLengthToFontSize" />
```

- [ ] **Step 2: Зафиксировать размер плашки и подключить конвертер шрифта**

Заменить текущий блок (строки 162–185):

```xml
<DataTemplate x:DataType="program:KeyPoint">
    <Button Background="{StaticResource HudPanelElevatedBrush}"
            BorderBrush="{StaticResource HudBorderBrush}" BorderThickness="1"
            Padding="14,12" HorizontalContentAlignment="Left">
```
...
```xml
        <TextBlock Classes="telemetry" FontSize="16" Text="{Binding Label}" />
    </Button>
</DataTemplate>
```

на:

```xml
<DataTemplate x:DataType="program:KeyPoint">
    <Button Width="120" Height="60"
            Background="{StaticResource HudPanelElevatedBrush}"
            BorderBrush="{StaticResource HudBorderBrush}" BorderThickness="1"
            Padding="16,14" HorizontalContentAlignment="Left">
```
...
```xml
        <TextBlock Classes="telemetry" TextWrapping="Wrap" MaxLines="2" TextTrimming="CharacterEllipsis"
                   FontSize="{Binding Label, Converter={StaticResource LabelLengthToFontSize}}"
                   Text="{Binding Label}" />
    </Button>
</DataTemplate>
```

(Содержимое `Button.Flyout` между открывающим и закрывающим тегом `Button` не трогается —
меняются только атрибуты `Button` и сам `TextBlock` в конце шаблона.)

- [ ] **Step 3: Ограничить ввод названия 30 символами**

Заменить строку 227:

```xml
<TextBox Text="{Binding Label}" PlaceholderText="Название" />
```

на:

```xml
<TextBox Text="{Binding Label}" PlaceholderText="Название" MaxLength="30" />
```

- [ ] **Step 4: Собрать десктоп-голову, чтобы проверить, что XAML валиден**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Expected: Build succeeded, без ошибок компиляции XAML (компилируемые биндинги упали бы
на этапе сборки, если `x:DataType`/имена свойств не совпадают).

- [ ] **Step 5: Прогнать существующий набор тестов, чтобы убедиться, что ничего не сломано**

Run: `dotnet test ArctZ.Tests/ArctZ.Tests.csproj`
Expected: PASS, включая новые тесты из Task 1 и уже существующие `MainViewJoystickRadiusTests`,
`ProgramViewModel*Tests`.

- [ ] **Step 6: Commit**

```bash
git add ArctZ/Views/MainView.axaml
git commit -m "feat: fix keypoint tile size, cap label length, shrink font by label length"
```

---

### Task 3: Ручная UI-проверка

**Files:** нет изменений кода — только проверка поведения приложения.

**Interfaces:** нет (использует то, что произвели Task 1–2).

- [ ] **Step 1: Собрать и запустить Desktop-голову**

Run: `dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj`
Затем: `dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj`

Expected: приложение реально запущено (не просто собрано).

- [ ] **Step 2: Попросить пользователя проверить три сценария**

Попросить пользователя в блоке «ТОЧКИ»:
1. Создать/переименовать точку с коротким названием (например, «А») и с названием
   ровно 30 символов — убедиться, что ввести больше 30 символов невозможно.
2. Сравнить размер плашки короткой и длинной (30 символов) точки — плашки должны быть
   одинакового размера.
3. Убедиться, что у длинной точки текст читаемый (мельче шрифтом, а не обрезанной
   плашкой), и что новые плашки визуально крупнее прежних.

- [ ] **Step 3: Задать вопросы через `AskUserQuestion`**

Один вопрос на каждый из трёх пунктов выше (не один общий «выглядит нормально?»),
как того требует `CLAUDE.md` («Тестирование UI»).

- [ ] **Step 4: Зафиксировать результат**

Если пользователь подтвердил все три пункта — задача завершена, дополнительных
коммитов не требуется (код уже закоммичен в Task 1–2). Если пользователь запросил
правки — внести их точечно и повторить Step 1–3.
