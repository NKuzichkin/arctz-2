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
            if (_isNarrow == isNarrow)
            {
                return;
            }
            _isNarrow = isNarrow;

            HeaderGrid.Classes.Set("narrow", isNarrow);
            ContentGrid.Classes.Set("narrow", isNarrow);

            HeaderGrid.RowDefinitions = new RowDefinitions(isNarrow ? "Auto,Auto" : "");
            ContentGrid.ColumnDefinitions = new ColumnDefinitions(isNarrow ? "*,Auto,Auto,*" : "Auto,*,Auto");
            ContentGrid.RowDefinitions = new RowDefinitions(isNarrow ? "*,Auto" : "");
        }

        internal static double ComputeNarrowJoystickRadius(double mainViewWidth)
        {
            var contentGridWidth = mainViewWidth - ContentGridChromeWidth;
            var columnWidth = contentGridWidth / 2;
            return Math.Max(NarrowJoystickMinRadius, columnWidth / 2 - NarrowJoystickEdgeMargin);
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
