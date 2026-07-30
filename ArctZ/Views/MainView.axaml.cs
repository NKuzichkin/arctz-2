using ArctZ.Components.VirtualJoystick;
using ArctZ.Services.Program;
using ArctZ.ViewModels;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ArctZ.Views
{
    public partial class MainView : UserControl
    {
        private const double NarrowLayoutBreakpoint = 700;

        public MainView()
        {
            InitializeComponent();
            SizeChanged += OnSizeChanged;
        }

        private ProgramViewModel? ViewModel => DataContext as ProgramViewModel;

        private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            var isNarrow = e.NewSize.Width < NarrowLayoutBreakpoint;
            HeaderGrid.Classes.Set("narrow", isNarrow);
            ContentGrid.Classes.Set("narrow", isNarrow);

            if (isNarrow)
            {
                HeaderGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
            }
            else
            {
                HeaderGrid.RowDefinitions = new RowDefinitions("");
            }
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
