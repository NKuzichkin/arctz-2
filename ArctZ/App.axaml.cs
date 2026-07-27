using ArctZ.ViewModels;
using ArctZ.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using System;
using System.Linq;

namespace ArctZ
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private static ViewModels.ProgramViewModel CreateProgramViewModel()
        {
            // Temporary hand-wired construction — Task 24 replaces this whole
            // method with proper DI via App.Services.
            var limits = Services.Device.MachineLimits.Default;
            var sessionFactory = new Services.Device.DeviceSessionFactory(limits);
            var realTransport = new Services.Device.Simulation.MockDeviceTransport(limits, new Services.Device.SystemPeriodicTimer(), TimeSpan.FromMilliseconds(100));
            var connection = new ConnectionViewModel(
                realTransport,
                () => new Services.Device.Simulation.MockDeviceTransport(limits, new Services.Device.SystemPeriodicTimer(), TimeSpan.FromMilliseconds(100)),
                sessionFactory);
            var storage = new Services.Program.JsonFileProgramStorage(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ArctZPrograms"));
            return new ViewModels.ProgramViewModel(connection, storage, new Services.Program.TrajectoryCompiler());
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = CreateProgramViewModel()
                };
            }
            else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
            {
                singleViewFactoryApplicationLifetime.MainViewFactory = () => new MainView { DataContext = CreateProgramViewModel() };
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                singleViewPlatform.MainView = new MainView
                {
                    DataContext = CreateProgramViewModel()
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}