using System;
using ArctZ.Components.VirtualJoystick;
using ArctZ.Services.Program;
using ArctZ.ViewModels;
using Avalonia.Controls;

namespace ArctZ.Views
{
    public partial class MainView : UserControl
    {
        private const double NarrowLayoutBreakpoint = 700;

        // Border(reveal-3).Margin(0,12,12,12→12) + BorderThickness(1+1=2) + ContentGrid.Margin(20+20=40)
        private const double ContentGridChromeWidth = 54;
        private const double NarrowJoystickMinRadius = 50;
        private const double NarrowJoystickEdgeMargin = 12;

        // Вертикальный «хром» вокруг ContentGrid без учёта шапки:
        // Border(reveal-3).Margin(0,12,12,12→12+12=24 верт.) + BorderThickness(1+1=2) + ContentGrid.Margin(20+20=40 верт.) = 66.
        // Плюс консервативная оценка высоты двухрядной узкой шапки (Padding 12,10 + строка статуса + строка кнопок + отступ 8px) ≈ 100.
        private const double MainViewChromeHeight = 166;
        private const double NarrowProgramPanelMinHeight = 160;

        public MainView()
        {
            InitializeComponent();
            SizeChanged += OnSizeChanged;
        }

        private ProgramViewModel? ViewModel => DataContext as ProgramViewModel;

        private bool? _isNarrow;

        private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            var isNarrow = e.NewSize.Width < NarrowLayoutBreakpoint;
            if (_isNarrow != isNarrow)
            {
                _isNarrow = isNarrow;

                HeaderGrid.Classes.Set("narrow", isNarrow);
                ContentGrid.Classes.Set("narrow", isNarrow);

                HeaderGrid.RowDefinitions = new RowDefinitions(isNarrow ? "Auto,Auto" : "");
                ContentGrid.ColumnDefinitions = new ColumnDefinitions(isNarrow ? "*,*" : "Auto,*,Auto");
                ContentGrid.RowDefinitions = new RowDefinitions(isNarrow ? "*,Auto" : "");

                if (!isNarrow)
                {
                    LeftJoystick.ClearValue(VirtualJoystick.RadiusProperty);
                    RightJoystick.ClearValue(VirtualJoystick.RadiusProperty);
                }
            }

            if (isNarrow)
            {
                var radius = ComputeNarrowJoystickRadius(e.NewSize.Width, e.NewSize.Height);
                LeftJoystick.Radius = radius;
                RightJoystick.Radius = radius;
            }
        }

        internal static double ComputeNarrowJoystickRadius(double mainViewWidth, double mainViewHeight)
        {
            var contentGridWidth = mainViewWidth - ContentGridChromeWidth;
            var columnWidth = contentGridWidth / 2;
            var widthRadius = Math.Max(NarrowJoystickMinRadius, columnWidth / 2 - NarrowJoystickEdgeMargin);

            var contentGridHeight = mainViewHeight - MainViewChromeHeight;
            var joystickRowBudget = contentGridHeight - NarrowProgramPanelMinHeight;
            var heightRadius = Math.Max(NarrowJoystickMinRadius, joystickRowBudget / 2);

            return Math.Min(widthRadius, heightRadius);
        }

        private void OnLeftJoystickDown(object? sender, JoystickEventArgs e) => ViewModel?.OnLeftJoystickDown(e);

        private void OnLeftJoystickMove(object? sender, JoystickEventArgs e) => ViewModel?.OnLeftJoystickMove(e);

        private void OnLeftJoystickUp(object? sender, JoystickEventArgs e) => ViewModel?.OnLeftJoystickUp(e);

        private void OnRightJoystickDown(object? sender, JoystickEventArgs e) => ViewModel?.OnRightJoystickDown(e);

        private void OnRightJoystickMove(object? sender, JoystickEventArgs e) => ViewModel?.OnRightJoystickMove(e);

        private void OnRightJoystickUp(object? sender, JoystickEventArgs e) => ViewModel?.OnRightJoystickUp(e);

        private async void OnLibrarySelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ViewModel is { } vm && sender is ListBox { SelectedItem: ProgramLibraryItem summary })
            {
                await vm.LoadProgramCommand.ExecuteAsync(summary);
            }
        }
    }
}
