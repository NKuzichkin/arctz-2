using CommunityToolkit.Mvvm.ComponentModel;

namespace ArctZ.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _greeting = "Welcome to Avalonia!";

        [ObservableProperty]
        private double _joystickX;

        [ObservableProperty]
        private double _joystickY;

        [ObservableProperty]
        private double _joystickForce;

        [ObservableProperty]
        private double _joystickAngle;

        [ObservableProperty]
        private string _joystickDirection = "None";
    }
}
