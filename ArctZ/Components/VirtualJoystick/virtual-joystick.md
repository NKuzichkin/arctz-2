# 📜 Техническое задание для AI-агента: Реализация `VirtualJoystick` на C# / Avalonia UI (XAML)

## 🎯 Цель
Создать нативный кастомный контрол `VirtualJoystick` для Avalonia UI, полностью воспроизводящий логику, конфигурацию и поведение предоставленного JS-референса (`virtual-joystick.js`), но с использованием идиоматичных паттернов Avalonia (привязки данных, `Pointer` события, `VisualStateManager`, `RenderTransform`).

---

## 🔄 Карта соответствия: JavaScript → Avalonia
| JS Feature | Avalonia Equivalent | Примечание |
|------------|---------------------|------------|
| `HTMLElement` + Shadow DOM | `TemplatedControl` + `ControlTemplate` | Использовать `PART_Base`, `PART_Knob` |
| CSS Variables `--x`, `--y` | `TranslateTransform` на `PART_Knob` | Высокая производительность, без маршалинга |
| `dataset.mode`, `lock`, `shape` | `AvaloniaProperty` (enum/string) | `JoystickMode`, `JoystickLock`, `JoystickShape` |
| `pointerdown/move/up` | `OnPointerPressed/Moved/Released` | Отслеживать `Pointer.Id` |
| CSS `&[part*="active"]` | `VisualStateManager` + `Active` state | Анимация прозрачности/фона |
| `dataset.direction`, `force` | `DirectProperty` (readonly) | Для привязки в VM/View |
| `joystickdown/move/up` | `.NET Events` или `RoutedEvent` | `EventHandler<JoystickEventArgs>` |

---

## 🛠 Пошаговая реализация

### Шаг 1. Определение типов и аргументов событий
Создайте перечисления и класс аргументов для типизации состояния джойстика:
```csharp
public enum JoystickMode { Fixed, Semi, Dynamic }
public enum JoystickLock { None, X, Y }
public enum JoystickShape { Circle, Box }
public enum JoystickDirection { None, N, NE, E, SE, S, SW, W }

public class JoystickEventArgs : EventArgs
{
    public Point Position { get; set; }
    public double Force { get; set; }      // 0.0 .. 1.0+
    public double AngleDeg { get; set; }   // 0..360
    public double AngleRad { get; set; }
    public JoystickDirection Direction { get; set; }
    public JoystickDirection? Captured { get; set; }
    public JoystickDirection? Released { get; set; }
}
```

### Шаг 2. Создание кастомного контрола (C#)
Наследуйте `TemplatedControl`. Зарегистрируйте свойства:
- **Конфигурационные:** `Radius` (65), `Mode`, `Lock`, `Shape`, `Threshold`
- **Выходные (readonly):** `X`, `Y`, `Force`, `Angle`, `Direction`, `Distance`
- **События:** `JoystickDown`, `JoystickMove`, `JoystickUp`

Используйте `StyledProperty` для конфигурации и `DirectProperty` для выходных данных, чтобы избежать лишних перерисовок.

### Шаг 3. XAML-шаблон (`Themes/VirtualJoystick.axaml`)
```xml
<Style Selector="local|VirtualJoystick">
  <Setter Property="Template">
    <ControlTemplate>
      <Grid Width="{Binding Radius, RelativeSource={RelativeSource TemplatedParent}, Converter={StaticResource RadiusToSizeConverter}}"
            Height="{Binding Radius, RelativeSource={RelativeSource TemplatedParent}, Converter={StaticResource RadiusToSizeConverter}}">
        <!-- Базовая область -->
        <Ellipse Name="PART_Base" Fill="White" Opacity="0.3" Stroke="Gray" StrokeThickness="1"/>
        
        <!-- Маркер центра -->
        <Ellipse Width="50" Height="50" Fill="Transparent" Stroke="DarkGray" StrokeThickness="1"
                 HorizontalAlignment="Center" VerticalAlignment="Center" IsHitTestVisible="False"/>
                 
        <!-- Стик (кусок) -->
        <Ellipse Name="PART_Knob" Width="50" Height="50" Fill="White" Opacity="0.5" Stroke="Gray" StrokeThickness="1">
          <Ellipse.RenderTransform>
            <TranslateTransform />
          </Ellipse.RenderTransform>
        </Ellipse>

        <!-- Визуальные состояния -->
        <VisualStateManager.VisualStateGroups>
          <VisualStateGroup Name="CommonStates">
            <VisualState Name="Inactive">
              <!-- Дефолт -->
            </VisualState>
            <VisualState Name="Active">
              <Storyboard>
                <DoubleAnimation Storyboard.TargetName="PART_Base" Storyboard.TargetProperty="Opacity" To="0.6" Duration="0:0:0.2"/>
                <DoubleAnimation Storyboard.TargetName="PART_Knob" Storyboard.TargetProperty="Opacity" To="0.8" Duration="0:0:0.2"/>
                <DoubleAnimation Storyboard.TargetName="PART_Knob" Storyboard.TargetProperty="(UIElement.RenderTransform).ScaleX" To="1.1" Duration="0:0:0.1"/>
                <DoubleAnimation Storyboard.TargetName="PART_Knob" Storyboard.TargetProperty="(UIElement.RenderTransform).ScaleY" To="1.1" Duration="0:0:0.1"/>
              </Storyboard>
            </VisualState>
          </VisualStateGroup>
        </VisualStateManager.VisualStateGroups>
      </Grid>
    </ControlTemplate>
  </Setter>
  
  <Setter Property="Opacity" Value="1">
    <Style.Triggers>
      <DataTrigger Binding="{Binding Mode}" Value="Dynamic">
        <Setter Property="Opacity" Value="0"/>
      </DataTrigger>
    </Style.Triggers>
  </Setter>
</Style>
```
> 💡 **Примечание:** `RadiusToSizeConverter` возвращает `radius * 2`. В `PART_Knob` используйте `ScaleTransform` вместе с `TranslateTransform` или объедините в `TransformGroup`.

### Шаг 4. Логика ввода и математика (C#)
Переопределите методы работы с указателем. Храните активный `PointerId` (аналог `#pointers`).
```csharp
protected override void OnPointerPressed(PointerPressedEventArgs e)
{
    if (Mode != JoystickMode.Fixed && _activePointers.Count > 0) return;
    
    var pos = e.GetPosition(this);
    if (!Bounds.Contains(pos)) return;

    _activePointers.Add(e.Pointer.Id);
    VisualStateManager.GoToState(this, "Active", true);
    CalculateAndBind(e.GetCurrentPoint(this).Position);
    JoystickDown?.Invoke(this, new JoystickEventArgs());
}

protected override void OnPointerMoved(PointerEventArgs e)
{
    if (!_activePointers.Contains(e.Pointer.Id)) return;
    CalculateAndBind(e.GetCurrentPoint(this).Position);
    JoystickMove?.Invoke(this, new JoystickEventArgs());
}

protected override void OnPointerReleased(PointerReleasedEventArgs e)
{
    if (!_activePointers.Contains(e.Pointer.Id)) return;
    _activePointers.Remove(e.Pointer.Id);
    
    ResetKnob();
    VisualStateManager.GoToState(this, "Inactive", true);
    JoystickUp?.Invoke(this, new JoystickEventArgs());
}
```

**Ядро расчета (`CalculateAndBind`):**
1. Получить дельту от центра: `dx = pos.X - Radius`, `dy = pos.Y - Radius`
2. Применить `Lock`: если `Lock == X` → `dy = 0`; если `Lock == Y` → `dx = 0`
3. `hypot = Math.Hypot(dx, dy)`, `angle = Math.Atan2(dy, dx)`
4. Нормализовать угол в `0..360` (по часовой стрелке от оси X)
5. **Clamping:** 
   - `Circle`: если `hypot > Radius` → нормализовать вектор к длине `Radius`
   - `Box`: `dx = Math.Clamp(dx, -Radius, Radius)`, `dy = Math.Clamp(dy, -Radius, Radius)`
6. Обновить `TranslateTransform` у `PART_Knob`
7. Вычислить `Force = hypot / Radius`, `Distance = Math.Min(hypot, Radius)`
8. Если `Force < Threshold` → `Direction = None`, иначе маппинг угла на 8 секторов.

### Шаг 5. Маппинг направлений и `Capture/Release`
Реализуйте логику, аналогичную `#getDir` и `#getUniqueDir`:
```csharp
private JoystickDirection GetDirection(double degree)
{
    // degree 0 = East, 90 = South, 180 = West, 270 = North (по часовой)
    var normalized = degree % 360;
    if (normalized < 0) normalized += 360;
    
    if (normalized < 22.5) return JoystickDirection.E;
    if (normalized < 67.5) return JoystickDirection.SE;
    if (normalized < 112.5) return JoystickDirection.S;
    if (normalized < 157.5) return JoystickDirection.SW;
    if (normalized < 202.5) return JoystickDirection.W;
    if (normalized < 247.5) return JoystickDirection.NW;
    if (normalized < 292.5) return JoystickDirection.N;
    if (normalized < 337.5) return JoystickDirection.NE;
    return JoystickDirection.E;
}

// Capture: направление появилось впервые
// Release: направление исчезло
private void UpdateDirectionState(JoystickDirection current)
{
    var prev = Direction;
    Captured = (prev == JoystickDirection.None && current != JoystickDirection.None) ? current : null;
    Released = (prev != JoystickDirection.None && current == JoystickDirection.None) ? prev : null;
    Direction = current;
}
```

### Шаг 6. Режимы `Semi` и `Dynamic`
- **`Fixed`**: стик всегда виден, привязан к центру.
- **`Semi`**: при инициализации `Opacity = 0`. При первом `pointerdown` внутри области → появляется, перемещается в точку касания, фиксируется там до `pointerup`.
- **`Dynamic`**: аналогично `Semi`, но после `pointerup` плавно исчезает и сбрасывает позицию в центр.
> Реализуйте через `VisualStateManager` и анимацию `Opacity`/`TranslateTransform` с `Easing`.

---

## 📦 Пример использования (XAML + C#)
```xml
<local:VirtualJoystick 
    Radius="65" 
    Mode="Dynamic" 
    Shape="Circle" 
    Lock="None" 
    Threshold="0.15"
    JoystickMove="OnJoystickMove" />
```
```csharp
private void OnJoystickMove(object sender, JoystickEventArgs e)
{
    // e.Direction, e.Force, e.AngleDeg, e.Position
    // Используйте для управления персонажем/камерой
}
```

---

## ✅ Чек-лист приемки
- [ ] Контрол наследует `TemplatedControl`, использует `PART_Base` и `PART_Knob`
- [ ] Поддержка `Pointer` событий с отслеживанием `PointerId`
- [ ] Корректный `Clamp` для `Circle` и `Box`
- [ ] `Lock` по осям X/Y работает изолированно
- [ ] `Threshold` фильтрует слабые нажатия
- [ ] `VisualStateManager` переключает `Active/Inactive` с плавными переходами
- [ ] Свойства `Direction`, `Force`, `Angle` обновляются в реальном времени и доступны для привязки
- [ ] События `Down/Move/Up` генерируются синхронно с вводом
- [ ] Нет утечек памяти (отписка от событий не требуется для `Pointer`, но следите за `DispatcherTimer` если добавите авто-сброс)

---

## ⚠️ Важные замечания для агента
1. **Не используйте `Canvas.Left/Top`** для перемещения стика. В Avalonia это вызывает лишние layout-проходы. Всегда используйте `RenderTransform.TranslateTransform`.
2. **Поток ввода:** `PointerMoved` может срабатывать вне контрола. Всегда используйте `e.GetCurrentPoint(this).Position` и проверяйте `_activePointers.Contains(e.Pointer.Id)`.
3. **Производительность:** Обновляйте `DirectProperty` только если значение изменилось (`SetAndRaise` или ручная проверка).
4. **Анимации:** В `Dynamic`/`Semi` режимах используйте `Storyboard` с `CubicEaseOut` для плавного появления/возврата.
5. **Тестирование:** Проверьте поведение на тачскринах (мультитач игнорируется, как в JS), на мыши и при изменении DPI/масштаба окна.

Придерживайтесь этой спецификации. Если потребуются уточнения по математике углов или привязке к MVVM, запросите дополнительный контекст. 🚀