using ArctZ.Components.VirtualJoystick;
using ArctZ.Services.Program;
using ArctZ.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ArctZ.Views
{
    public partial class MainView : UserControl
    {
        public MainView()
        {
            InitializeComponent();
        }

        private ProgramViewModel? ViewModel => DataContext as ProgramViewModel;

        private void OnLeftJoystickDown(object? sender, JoystickEventArgs e) => ViewModel?.OnLeftJoystickDown(e);

        private void OnLeftJoystickMove(object? sender, JoystickEventArgs e) => ViewModel?.OnLeftJoystickMove(e);

        private void OnLeftJoystickUp(object? sender, JoystickEventArgs e) => ViewModel?.OnLeftJoystickUp(e);

        private void OnRightJoystickDown(object? sender, JoystickEventArgs e) => ViewModel?.OnRightJoystickDown(e);

        private void OnRightJoystickMove(object? sender, JoystickEventArgs e) => ViewModel?.OnRightJoystickMove(e);

        private void OnRightJoystickUp(object? sender, JoystickEventArgs e) => ViewModel?.OnRightJoystickUp(e);

        private void OnAuthoringModeClicked(object? sender, RoutedEventArgs e)
        {
            if (ViewModel is { } vm)
            {
                vm.Mode = ProgramMode.Authoring;
            }
        }

        private void OnPlaybackModeClicked(object? sender, RoutedEventArgs e)
        {
            if (ViewModel is { } vm)
            {
                vm.Mode = ProgramMode.Playback;
            }
        }

        private async void OnLibrarySelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ViewModel is { } vm && sender is ListBox { SelectedItem: ProgramLibraryItem summary })
            {
                await vm.LoadProgramCommand.ExecuteAsync(summary);
            }
        }
    }
}
