using ArctZ.ViewModels;
using ArctZ.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using Zafiro.Avalonia.ViewLocators;

namespace ArctZ
{
    public partial class App : Application
    {
        public static IServiceProvider? Services { get; set; }

        public static bool PrintMode { get; set; }

        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void RegisterViews()
        {
            DataTypeViewLocator.RegisterGlobal<ConnectionViewModel, ConnectionView>();
        }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);

            if (PrintMode)
            {
                RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
                PrintTheme.Apply(Resources);
            }
        }

        public override void OnFrameworkInitializationCompleted()
        {
            var viewModel = Services!.GetRequiredService<ProgramViewModel>();
            _ = viewModel.RefreshLibraryCommand.ExecuteAsync(null);
            _ = viewModel.Connection.AutoConnectAsync();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = viewModel
                };

                if (PrintMode)
                {
                    desktop.MainWindow.Classes.Add("print");
                }
            }
            else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
            {
                singleViewFactoryApplicationLifetime.MainViewFactory = () => new MainView { DataContext = viewModel };
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                singleViewPlatform.MainView = new MainView
                {
                    DataContext = viewModel
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
