using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ArctZ.Services;
using ArctZ.Services.Device;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArctZ.ViewModels;

public partial class ConnectionViewModel : ViewModelBase
{
    private readonly IDeviceTransport _realTransport;
    private readonly Func<IDeviceTransport> _createDemoTransport;
    private readonly IDeviceSessionFactory _sessionFactory;
    private readonly IUiDispatcher _uiDispatcher;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnectionModalVisible))]
    private IDeviceSession? _session;

    // Mirrors Session.ConnectionState. IDeviceSession does not implement
    // INotifyPropertyChanged, so a direct "Session.ConnectionState" binding
    // only ever reads the value once (when Session itself changes) and never
    // updates when the same session's state transitions later. This property
    // is kept current via ConnectionStateChanged (see OnSessionChanged below)
    // so bindings on THIS view model update live.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnectionModalVisible))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private ConnectionEndpoint? _selectedEndpoint;

    public bool IsConnectionModalVisible => Session is null || ConnectionState != ConnectionState.Connected;

    public ObservableCollection<ConnectionEndpoint> AvailableEndpoints { get; } = new()
    {
        new ConnectionEndpoint("real", "Устройство", ConnectionEndpointKind.RealDevice),
        new ConnectionEndpoint("demo", "Демо", ConnectionEndpointKind.Demo),
    };

    public ConnectionViewModel(
        IDeviceTransport realTransport,
        Func<IDeviceTransport> createDemoTransport,
        IDeviceSessionFactory sessionFactory,
        IUiDispatcher uiDispatcher)
    {
        _realTransport = realTransport;
        _createDemoTransport = createDemoTransport;
        _sessionFactory = sessionFactory;
        _uiDispatcher = uiDispatcher;
        SelectedEndpoint = AvailableEndpoints[0];
    }

    partial void OnSessionChanged(IDeviceSession? oldValue, IDeviceSession? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.ConnectionStateChanged -= OnSessionConnectionStateChanged;
        }

        if (newValue is not null)
        {
            newValue.ConnectionStateChanged += OnSessionConnectionStateChanged;
        }

        ConnectionState = newValue?.ConnectionState ?? ConnectionState.Disconnected;
    }

    private void OnSessionConnectionStateChanged()
    {
        if (!_uiDispatcher.CheckAccess())
        {
            _uiDispatcher.Post(OnSessionConnectionStateChanged);
            return;
        }

        ConnectionState = Session?.ConnectionState ?? ConnectionState.Disconnected;
    }

    private bool CanConnect() =>
        SelectedEndpoint is not null &&
        ConnectionState is not (ConnectionState.Connecting or ConnectionState.Reconnecting);

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (SelectedEndpoint is null)
        {
            return;
        }

        // All platform heads register IDeviceTransport as a singleton, so a second
        // session would wrap the same transport as the first: two LineReceived
        // subscribers, two status pollers, two racing reconnect loops. Tear the
        // previous session down first — this covers both reconnecting and
        // switching endpoints while connected.
        if (Session is not null)
        {
            await Session.DisconnectAsync();
            Session = null;
        }

        var transport = SelectedEndpoint.Kind == ConnectionEndpointKind.Demo
            ? _createDemoTransport()
            : _realTransport;

        var session = _sessionFactory.Create(transport);
        Session = session;

        try
        {
            await session.ConnectAsync(SelectedEndpoint.Id);
        }
        catch
        {
            // A failed connect leaves the transport's LineReceived/Disconnected handlers
            // subscribed (DeviceSession.ConnectAsync wires them before attempting the
            // transport-level connect). session.DisconnectAsync() unwinds that — critical
            // for the real-device transport, which is a singleton reused by the next
            // attempt; leaked handlers there would double-fire on every subsequent connect.
            await session.DisconnectAsync();
            Session = null;
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (Session is not null)
        {
            await Session.DisconnectAsync();
            Session = null;
        }
    }

    [RelayCommand]
    private Task HomeAsync() => Session?.HomeAsync() ?? Task.CompletedTask;

    [RelayCommand]
    private Task ResetAlarmAsync() => Session?.ResetAlarmAsync() ?? Task.CompletedTask;
}
