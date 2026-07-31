using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Zafiro.UI.Commands;

namespace ArctZ.ViewModels;

public partial class ConnectionViewModel : ReactiveViewModelBase
{
    private readonly IDeviceTransport _realTransport;
    private readonly Func<IDeviceTransport> _createDemoTransport;
    private readonly IDeviceSessionFactory _sessionFactory;
    private IDisposable? _sentGCodeSubscription;
    private const int MaxSentGCodeLines = 200;

    [Reactive] private IDeviceSession? session;

    // Mirrors Session.ConnectionState. IDeviceSession does not implement
    // INotifyPropertyChanged, so a direct "Session.ConnectionState" binding
    // only ever reads the value once (when Session itself changes) and never
    // updates when the same session's state transitions later. This property
    // is kept current via the ConnectionStateChanged event subscription set up
    // in the constructor below, so bindings on THIS view model update live.
    [Reactive] private ConnectionState connectionState = ConnectionState.Disconnected;

    [Reactive] private ConnectionEndpoint? selectedEndpoint;

    [Reactive] private bool isGCodeLogOpen;

    public bool IsConnectionModalVisible => Session is null || ConnectionState != ConnectionState.Connected;

    public string ConnectionStateLabel => ConnectionState switch
    {
        ConnectionState.Disconnected => "Не подключено",
        ConnectionState.Connecting => "Подключение…",
        ConnectionState.Connected => "Подключено",
        ConnectionState.Reconnecting => "Переподключение…",
        _ => "—",
    };

    public ObservableCollection<ConnectionEndpoint> AvailableEndpoints { get; } = new()
    {
        new ConnectionEndpoint("real", "Устройство", ConnectionEndpointKind.RealDevice),
        new ConnectionEndpoint("demo", "Демо", ConnectionEndpointKind.Demo),
    };

    public ObservableCollection<string> SentGCodeLines { get; } = new();

    public IEnhancedCommand<Unit> ConnectCommand { get; }
    public IEnhancedCommand<Unit> DisconnectCommand { get; }
    public IEnhancedCommand<Unit> HomeCommand { get; }
    public IEnhancedCommand<Unit> ResetAlarmCommand { get; }
    public IEnhancedCommand<Unit> ToggleGCodeLogCommand { get; }

    public ConnectionViewModel(
        IDeviceTransport realTransport,
        Func<IDeviceTransport> createDemoTransport,
        IDeviceSessionFactory sessionFactory)
    {
        _realTransport = realTransport;
        _createDemoTransport = createDemoTransport;
        _sessionFactory = sessionFactory;
        SelectedEndpoint = AvailableEndpoints[0];

        var canConnect = this.WhenAnyValue(
            x => x.SelectedEndpoint,
            x => x.ConnectionState,
            (endpoint, state) => endpoint is not null &&
                state is not (ConnectionState.Connecting or ConnectionState.Reconnecting));

        // Track() subscribes ThrownExceptions (an unobserved command fault would otherwise crash
        // the process — see ReactiveViewModelBase.Track) and registers the command for disposal.
        ConnectCommand = Track(ReactiveCommand.CreateFromTask(ConnectAsync, canConnect)
            .Enhance(text: "Подключить", name: "ConnectCommand"));
        DisconnectCommand = Track(ReactiveCommand.CreateFromTask(DisconnectAsync)
            .Enhance(text: "Отключить", name: "DisconnectCommand"));
        HomeCommand = Track(ReactiveCommand.CreateFromTask(HomeAsync)
            .Enhance(text: "Homing", name: "HomeCommand"));
        ResetAlarmCommand = Track(ReactiveCommand.CreateFromTask(ResetAlarmAsync)
            .Enhance(text: "Сброс аварии", name: "ResetAlarmCommand"));
        ToggleGCodeLogCommand = Track(ReactiveCommand.Create(() => { IsGCodeLogOpen = !IsGCodeLogOpen; })
            .Enhance(text: "Лог G-code", name: "ToggleGCodeLogCommand"));

        // Immediately mirror a newly-assigned session's state, then keep mirroring it
        // as ConnectionStateChanged fires later (on a background thread for the
        // real-device path — ObserveOn marshals back before the property is set).
        // .Switch() drops the previous session's event subscription the moment
        // Session changes to a new value or null, replacing the old
        // OnSessionChanged-based subscribe/unsubscribe dance.
        this.WhenAnyValue(x => x.Session)
            .Do(s => ConnectionState = s?.ConnectionState ?? ConnectionState.Disconnected)
            .Select(s => s is null
                ? Observable.Empty<Unit>()
                : Observable.FromEvent(h => s.ConnectionStateChanged += h, h => s.ConnectionStateChanged -= h)
                    .ObserveOn(RxSchedulers.MainThreadScheduler))
            .Switch()
            .Subscribe(_ => ConnectionState = Session?.ConnectionState ?? ConnectionState.Disconnected)
            .DisposeWith(Disposables);

        // IsConnectionModalVisible/ConnectionStateLabel are plain computed
        // properties (no ObservableAsPropertyHelper) — re-raise their
        // INotifyPropertyChanged notifications whenever a dependency changes,
        // same intent as CommunityToolkit's [NotifyPropertyChangedFor] before.
        this.WhenAnyValue(x => x.Session, x => x.ConnectionState, (s, cs) => (s, cs))
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(IsConnectionModalVisible));
                this.RaisePropertyChanged(nameof(ConnectionStateLabel));
            })
            .DisposeWith(Disposables);
    }

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

        var innerTransport = SelectedEndpoint.Kind == ConnectionEndpointKind.Demo
            ? _createDemoTransport()
            : _realTransport;

        if (_sentGCodeSubscription is not null)
        {
            Disposables.Remove(_sentGCodeSubscription);
        }

        var loggingTransport = new LoggingDeviceTransport(innerTransport);
        SentGCodeLines.Clear();
        _sentGCodeSubscription = Observable.FromEvent<string>(
                h => loggingTransport.LineSent += h,
                h => loggingTransport.LineSent -= h)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(AppendSentGCodeLine)
            .DisposeWith(Disposables);

        var session = _sessionFactory.Create(loggingTransport);
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

    private async Task DisconnectAsync()
    {
        if (Session is not null)
        {
            await Session.DisconnectAsync();
            Session = null;
        }

        if (_sentGCodeSubscription is not null)
        {
            Disposables.Remove(_sentGCodeSubscription);
            _sentGCodeSubscription = null;
        }
    }

    private void AppendSentGCodeLine(string line)
    {
        if (SentGCodeLines.Count >= MaxSentGCodeLines)
        {
            SentGCodeLines.RemoveAt(0);
        }

        SentGCodeLines.Add(line);
    }

    private Task HomeAsync() => Session?.HomeAsync() ?? Task.CompletedTask;

    private Task ResetAlarmAsync() => Session?.ResetAlarmAsync() ?? Task.CompletedTask;
}
