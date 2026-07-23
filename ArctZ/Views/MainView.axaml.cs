using ArctZ.Components.VirtualJoystick;
using ArctZ.ViewModels;
using Avalonia.Controls;

namespace ArctZ.Views
{
    public partial class MainView : UserControl
    {
        public MainView()
        {
            InitializeComponent();
        }

        private void OnJoystickMove(object? sender, JoystickEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.JoystickX = e.Position.X;
                vm.JoystickY = e.Position.Y;
                vm.JoystickForce = e.Force;
                vm.JoystickAngle = e.AngleDeg;
                vm.JoystickDirection = e.Direction.ToString();
            }
        }
    }
}
