using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArctZ.ViewModels;

public partial class ConnectionViewModel : ViewModelBase
{
    private readonly IDeviceTransport _realTransport;
    private readonly Func<IDeviceTransport> _createDemoTransport;
    private readonly IDeviceSessionFactory _sessionFactory;

    [ObservableProperty]
    private IDeviceSession? _session;

    [ObservableProperty]
    private ConnectionEndpoint? _selectedEndpoint;

    public ObservableCollection<ConnectionEndpoint> AvailableEndpoints { get; } = new()
    {
        new ConnectionEndpoint("real", "Устройство", ConnectionEndpointKind.RealDevice),
        new ConnectionEndpoint("demo", "Демо", ConnectionEndpointKind.Demo),
    };

    public ConnectionViewModel(
        IDeviceTransport realTransport,
        Func<IDeviceTransport> createDemoTransport,
        IDeviceSessionFactory sessionFactory)
    {
        _realTransport = realTransport;
        _createDemoTransport = createDemoTransport;
        _sessionFactory = sessionFactory;
        SelectedEndpoint = AvailableEndpoints[0];
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (SelectedEndpoint is null)
        {
            return;
        }

        var transport = SelectedEndpoint.Kind == ConnectionEndpointKind.Demo
            ? _createDemoTransport()
            : _realTransport;

        Session = _sessionFactory.Create(transport);
        await Session.ConnectAsync(SelectedEndpoint.Id);
    }

    [RelayCommand]
    private Task DisconnectAsync() => Session?.DisconnectAsync() ?? Task.CompletedTask;

    [RelayCommand]
    private Task HomeAsync() => Session?.HomeAsync() ?? Task.CompletedTask;

    [RelayCommand]
    private Task ResetAlarmAsync() => Session?.ResetAlarmAsync() ?? Task.CompletedTask;
}
