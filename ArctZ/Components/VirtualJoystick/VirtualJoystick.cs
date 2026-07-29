using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace ArctZ.Components.VirtualJoystick;

public enum JoystickMode { Fixed, Semi, Dynamic }
public enum JoystickLock { None, X, Y }
public enum JoystickShape { Circle, Box }
public enum JoystickDirection { None, N, NE, E, SE, S, SW, W, NW }

public class JoystickEventArgs : EventArgs
{
    public Point Position { get; set; }
    public double Force { get; set; }
    public double AngleDeg { get; set; }
    public double AngleRad { get; set; }
    public JoystickDirection Direction { get; set; }
    public JoystickDirection? Captured { get; set; }
    public JoystickDirection? Released { get; set; }
}

public class VirtualJoystick : TemplatedControl
{
    public static readonly StyledProperty<double> RadiusProperty =
        AvaloniaProperty.Register<VirtualJoystick, double>(nameof(Radius), 65.0);

    public static readonly StyledProperty<JoystickMode> ModeProperty =
        AvaloniaProperty.Register<VirtualJoystick, JoystickMode>(nameof(Mode), JoystickMode.Fixed);

    public static readonly StyledProperty<JoystickLock> LockProperty =
        AvaloniaProperty.Register<VirtualJoystick, JoystickLock>(nameof(Lock), JoystickLock.None);

    public static readonly StyledProperty<JoystickShape> ShapeProperty =
        AvaloniaProperty.Register<VirtualJoystick, JoystickShape>(nameof(Shape), JoystickShape.Circle);

    public static readonly StyledProperty<double> ThresholdProperty =
        AvaloniaProperty.Register<VirtualJoystick, double>(nameof(Threshold), 0.15);

    public static readonly DirectProperty<VirtualJoystick, double> XProperty =
        AvaloniaProperty.RegisterDirect<VirtualJoystick, double>(nameof(X), o => o._x);

    public static readonly DirectProperty<VirtualJoystick, double> YProperty =
        AvaloniaProperty.RegisterDirect<VirtualJoystick, double>(nameof(Y), o => o._y);

    public static readonly DirectProperty<VirtualJoystick, double> ForceProperty =
        AvaloniaProperty.RegisterDirect<VirtualJoystick, double>(nameof(Force), o => o._force);

    public static readonly DirectProperty<VirtualJoystick, double> AngleProperty =
        AvaloniaProperty.RegisterDirect<VirtualJoystick, double>(nameof(Angle), o => o._angle);

    public static readonly DirectProperty<VirtualJoystick, JoystickDirection> DirectionProperty =
        AvaloniaProperty.RegisterDirect<VirtualJoystick, JoystickDirection>(nameof(Direction), o => o._direction);

    public event EventHandler<JoystickEventArgs>? JoystickDown;
    public event EventHandler<JoystickEventArgs>? JoystickMove;
    public event EventHandler<JoystickEventArgs>? JoystickUp;

    private readonly HashSet<long> _activePointers = new();
    private Point _touchOrigin;
    private double _x, _y, _force, _angle;
    private JoystickDirection _direction;
    private JoystickDirection? _lastCaptured;
    private JoystickDirection? _lastReleased;

    private Grid? _rootGrid;
    private Grid? _visualsGrid;
    private TranslateTransform? _rootTranslate;
    private TranslateTransform? _knobTranslate;
    private ScaleTransform? _knobScale;

    public double Radius
    {
        get => GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    public JoystickMode Mode
    {
        get => GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public JoystickLock Lock
    {
        get => GetValue(LockProperty);
        set => SetValue(LockProperty, value);
    }

    public JoystickShape Shape
    {
        get => GetValue(ShapeProperty);
        set => SetValue(ShapeProperty, value);
    }

    public double Threshold
    {
        get => GetValue(ThresholdProperty);
        set => SetValue(ThresholdProperty, value);
    }

    public double X => _x;
    public double Y => _y;
    public double Force => _force;
    public double Angle => _angle;
    public JoystickDirection Direction => _direction;

    public VirtualJoystick()
    {
        Focusable = true;
        ClipToBounds = false;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _rootGrid = e.NameScope.Find<Grid>("PART_Root");
        _visualsGrid = e.NameScope.Find<Grid>("PART_Visuals");
        _rootTranslate = new TranslateTransform();
        if (_rootGrid != null)
        {
            _rootGrid.RenderTransform = _rootTranslate;
        }

        var knob = e.NameScope.Find<Ellipse>("PART_Knob");
        if (knob != null)
        {
            _knobScale = new ScaleTransform();
            _knobTranslate = new TranslateTransform();
            var group = new TransformGroup();
            group.Children.Add(_knobScale);
            group.Children.Add(_knobTranslate);
            knob.RenderTransform = group;
        }

        UpdateSize();
        UpdateInitialVisibility();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ModeProperty)
        {
            UpdateInitialVisibility();
        }
        else if (change.Property == RadiusProperty)
        {
            UpdateSize();
        }
    }

    private void UpdateSize()
    {
        var diameter = Radius * 2.0;
        if (_rootGrid != null)
        {
            _rootGrid.Width = diameter;
            _rootGrid.Height = diameter;
        }
    }

    private void UpdateInitialVisibility()
    {
        if (_visualsGrid != null)
        {
            _visualsGrid.Opacity = Mode == JoystickMode.Fixed ? 1.0 : 0.0;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var pos = e.GetPosition(this);

        if (_activePointers.Count == 0)
        {
            if (Mode != JoystickMode.Fixed)
            {
                if (!new Rect(0, 0, Bounds.Width, Bounds.Height).Contains(pos))
                    return;

                if (_rootTranslate != null)
                {
                    _rootTranslate.X = pos.X - Radius;
                    _rootTranslate.Y = pos.Y - Radius;
                }

                if (_visualsGrid != null)
                {
                    _visualsGrid.Opacity = 1.0;
                }

                _touchOrigin = pos;
            }
            else
            {
                _touchOrigin = new Point(Radius, Radius);
            }
        }
        else if (Mode == JoystickMode.Fixed)
        {
            return;
        }

        _activePointers.Add(e.Pointer.Id);
        PseudoClasses.Set(":active", true);

        if (_knobScale != null)
        {
            _knobScale.ScaleX = 1.1;
            _knobScale.ScaleY = 1.1;
        }

        CalculateAndBind(pos);
        FireJoystickDown();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_activePointers.Contains(e.Pointer.Id))
            return;

        var pos = e.GetPosition(this);
        CalculateAndBind(pos);
        FireJoystickMove();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_activePointers.Contains(e.Pointer.Id))
            return;

        _activePointers.Remove(e.Pointer.Id);

        ResetKnob();
        PseudoClasses.Set(":active", false);

        if (_knobScale != null)
        {
            _knobScale.ScaleX = 1.0;
            _knobScale.ScaleY = 1.0;
        }

        if (Mode == JoystickMode.Dynamic)
        {
            if (_visualsGrid != null)
            {
                _visualsGrid.Opacity = 0.0;
            }

            if (_rootTranslate != null)
            {
                _rootTranslate.X = 0;
                _rootTranslate.Y = 0;
            }
        }

        FireJoystickUp();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        if (_activePointers.Count == 0)
        {
            // Releasing pointer capture after a normal OnPointerReleased (the
            // common case) raises this too. _activePointers is already empty
            // by then, so there is nothing new to release — without this
            // guard, ResetKnob()/FireJoystickUp() below would run a second
            // time and report a bogus JoystickUp with Direction/Released
            // already zeroed out by the first release.
            return;
        }

        _activePointers.Clear();
        ResetKnob();
        PseudoClasses.Set(":active", false);
        if (_knobScale != null)
        {
            _knobScale.ScaleX = 1.0;
            _knobScale.ScaleY = 1.0;
        }

        if (Mode != JoystickMode.Fixed)
        {
            if (_visualsGrid != null)
            {
                _visualsGrid.Opacity = 0.0;
            }

            if (_rootTranslate != null)
            {
                _rootTranslate.X = 0;
                _rootTranslate.Y = 0;
            }
        }

        FireJoystickUp();
    }

    private void CalculateAndBind(Point pos)
    {
        double dx = pos.X - _touchOrigin.X;
        double dy = pos.Y - _touchOrigin.Y;

        if (Lock == JoystickLock.X)
            dy = 0;
        else if (Lock == JoystickLock.Y)
            dx = 0;

        double hypot = Math.Sqrt(dx * dx + dy * dy);

        if (Shape == JoystickShape.Circle && hypot > Radius && hypot > 0.001)
        {
            double scale = Radius / hypot;
            dx *= scale;
            dy *= scale;
            hypot = Radius;
        }
        else if (Shape == JoystickShape.Box)
        {
            dx = Math.Clamp(dx, -Radius, Radius);
            dy = Math.Clamp(dy, -Radius, Radius);
        }

        if (_knobTranslate != null)
        {
            _knobTranslate.X = dx;
            _knobTranslate.Y = dy;
        }

        double force = hypot / Radius;
        double angleRad = Math.Atan2(dy, dx);
        double angleDeg = angleRad * 180.0 / Math.PI;
        if (angleDeg < 0)
            angleDeg += 360.0;

        var newDirection = force < Threshold ? JoystickDirection.None : GetDirection(angleDeg);
        var prev = _direction;
        _lastCaptured = prev == JoystickDirection.None && newDirection != JoystickDirection.None ? newDirection : (JoystickDirection?)null;
        _lastReleased = prev != JoystickDirection.None && newDirection == JoystickDirection.None ? prev : (JoystickDirection?)null;

        SetAndRaise(XProperty, ref _x, dx);
        SetAndRaise(YProperty, ref _y, dy);
        SetAndRaise(ForceProperty, ref _force, force);
        SetAndRaise(AngleProperty, ref _angle, angleDeg);
        SetAndRaise(DirectionProperty, ref _direction, newDirection);
    }

    private void ResetKnob()
    {
        if (_knobTranslate != null)
        {
            _knobTranslate.X = 0;
            _knobTranslate.Y = 0;
        }

        _lastCaptured = null;
        _lastReleased = _direction != JoystickDirection.None ? _direction : (JoystickDirection?)null;

        SetAndRaise(XProperty, ref _x, 0);
        SetAndRaise(YProperty, ref _y, 0);
        SetAndRaise(ForceProperty, ref _force, 0);
        SetAndRaise(AngleProperty, ref _angle, 0);
        SetAndRaise(DirectionProperty, ref _direction, JoystickDirection.None);
    }

    private static JoystickDirection GetDirection(double degree)
    {
        var normalized = degree % 360.0;
        if (normalized < 0)
            normalized += 360.0;

        if (normalized < 22.5)
            return JoystickDirection.E;
        if (normalized < 67.5)
            return JoystickDirection.SE;
        if (normalized < 112.5)
            return JoystickDirection.S;
        if (normalized < 157.5)
            return JoystickDirection.SW;
        if (normalized < 202.5)
            return JoystickDirection.W;
        if (normalized < 247.5)
            return JoystickDirection.NW;
        if (normalized < 292.5)
            return JoystickDirection.N;
        if (normalized < 337.5)
            return JoystickDirection.NE;
        return JoystickDirection.E;
    }

    private JoystickEventArgs BuildEventArgs()
    {
        return new JoystickEventArgs
        {
            Position = new Point(_x, _y),
            Force = _force,
            AngleDeg = _angle,
            AngleRad = _angle * Math.PI / 180.0,
            Direction = _direction,
            Captured = _lastCaptured,
            Released = _lastReleased,
        };
    }

    private void FireJoystickDown()
    {
        JoystickDown?.Invoke(this, BuildEventArgs());
    }

    private void FireJoystickMove()
    {
        JoystickMove?.Invoke(this, BuildEventArgs());
    }

    private void FireJoystickUp()
    {
        JoystickUp?.Invoke(this, BuildEventArgs());
    }
}
