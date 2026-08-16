using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
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
    private readonly IDeviceEndpointProvider _endpointProvider;
    private static readonly ConnectionEndpoint DemoEndpoint = new("demo", "Демо", ConnectionEndpointKind.Demo);
    private IDisposable? _sentGCodeSubscription;
    private IDisposable? _scanSubscription;
    private IMockDeviceControl? _currentMockControl;
    private const int MaxSentGCodeLines = 200;
    private const int MockErrorCode = 9;
    private const int MockAlarmCode = 1;
    private static readonly TimeSpan AutoConnectScanWindow = TimeSpan.FromSeconds(10);

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

    [Reactive] private bool isMockSettingsOpen;
    [Reactive] private int mockResponseDelayMs;

    // Set by ProgramViewModel to mirror its IsProgramLocked. Disconnect tears
    // down the link out from under an in-flight program dispatch loop
    // (ProgramViewModel.PlayAsync captures Connection.Session per step), so it
    // must be unavailable while a program is Running/Paused.
    [Reactive] private bool isPlaybackLocked;

    [Reactive] private string? endpointError;

    [Reactive] private bool isScanning;

    [Reactive] private AutoConnectPhase autoConnectPhase = AutoConnectPhase.Idle;
    [Reactive] private int autoConnectAttempt;

    private readonly IReconnectPolicy _autoConnectRetryPolicy;
    private CancellationTokenSource? _autoConnectCts;
    private bool _autoConnectSuppressed;

    public int AutoConnectMaxAttempts => _autoConnectRetryPolicy.MaxAttempts;

    public bool HasEndpointError => !string.IsNullOrEmpty(EndpointError);

    public bool IsDiscoverySupported => _realTransport.IsSupported && _endpointProvider.SupportsDiscovery;

    // Mirrors Session.DeviceStatus the same way ConnectionState mirrors
    // Session.ConnectionState — see the comment above for why a direct
    // "Session.DeviceStatus" binding wouldn't update live.
    [Reactive] private DeviceStatus? deviceStatus;

    // LastError mirrors Session.LastError (set right before ConnectionStateChanged
    // fires — see DeviceSession.OnTransportDisconnected — so it rides the same
    // subscription). LastAlarmCode has no session-side property to mirror; it's
    // set purely from the AlarmTriggered event and cleared on reset/reconnect.
    [Reactive] private string? lastError;
    [Reactive] private int? lastAlarmCode;

    public bool HasError => !string.IsNullOrEmpty(LastError) || LastAlarmCode is not null;

    public string? ErrorMessage => LastAlarmCode is { } code
        ? $"Авария FluidNC: код {code}"
        : LastError;

    public bool IsAutoConnectSplashVisible =>
        AutoConnectPhase is AutoConnectPhase.Searching or AutoConnectPhase.Connecting or AutoConnectPhase.WaitingRetry
        || ConnectionState == ConnectionState.Reconnecting;

    public string AutoConnectStatusText => ConnectionState == ConnectionState.Reconnecting
        ? "Переподключение…" // DeviceSession's fast internal reconnect (Part 1) — no attempt count exposed
        : AutoConnectPhase switch
        {
            AutoConnectPhase.Searching => "Поиск FluidNC…",
            AutoConnectPhase.Connecting => "Подключение…",
            AutoConnectPhase.WaitingRetry => $"Попытка {AutoConnectAttempt} из {AutoConnectMaxAttempts} не удалась, повтор…",
            _ => "",
        };

    public bool IsConnectionModalVisible =>
        !IsAutoConnectSplashVisible && (Session is null || ConnectionState != ConnectionState.Connected);

    // Авария (LastAlarmCode) блокирует основной экран отдельной модалкой; обычная ошибка
    // соединения (LastError) остаётся баннером внутри ConnectionView — см. HasError/ErrorMessage.
    // Приоритет: заставка автоподключения > ручная модалка соединения > модалка аварии — авария
    // не должна перекрывать единственный работающий путь восстановления связи, будь то заставка
    // (идёт автоматика) или ручной список (автоматика сдалась).
    public bool IsAlarmModalVisible =>
        LastAlarmCode is not null && !IsConnectionModalVisible && !IsAutoConnectSplashVisible;

    public bool IsAnyModalVisible => IsAutoConnectSplashVisible || IsConnectionModalVisible || IsAlarmModalVisible;

    public string ConnectionStateLabel => ConnectionState switch
    {
        ConnectionState.Disconnected => "Не подключено",
        ConnectionState.Connecting => "Подключение…",
        ConnectionState.Connected => "Подключено",
        ConnectionState.Reconnecting => "Переподключение…",
        _ => "—",
    };

    public string PositionLabel => DeviceStatus is { } status
        ? $"X {status.WPos.X:0.00}  Y {status.WPos.Y:0.00}  Z {status.WPos.Z:0.00}  A {status.WPos.A:0.00}"
        : "—";

    public ObservableCollection<ConnectionEndpoint> AvailableEndpoints { get; } = new();

    public bool IsRealDeviceUnsupported => !_realTransport.IsSupported;

    public ObservableCollection<string> SentGCodeLines { get; } = new();

    public IEnhancedCommand<Unit> ConnectCommand { get; }
    public IEnhancedCommand<Unit> DisconnectCommand { get; }
    public IEnhancedCommand<Unit> ResetAlarmCommand { get; }
    public IEnhancedCommand<Unit> RefreshEndpointsCommand { get; }
    public IEnhancedCommand<Unit> ScanCommand { get; }
    public IEnhancedCommand<Unit> ToggleGCodeLogCommand { get; }
    public IEnhancedCommand<Unit> ToggleMockSettingsCommand { get; }
    public IEnhancedCommand<Unit> TriggerMockErrorCommand { get; }
    public IEnhancedCommand<Unit> TriggerMockAlarmCommand { get; }

    public ConnectionViewModel(
        IDeviceTransport realTransport,
        Func<IDeviceTransport> createDemoTransport,
        IDeviceSessionFactory sessionFactory,
        IDeviceEndpointProvider endpointProvider,
        IReconnectPolicy? autoConnectRetryPolicy = null)
    {
        _realTransport = realTransport;
        _createDemoTransport = createDemoTransport;
        _sessionFactory = sessionFactory;
        _endpointProvider = endpointProvider;
        _autoConnectRetryPolicy = autoConnectRetryPolicy ?? new ExponentialBackoffReconnectPolicy(ExponentialBackoffReconnectPolicy.DefaultDelays);

        AvailableEndpoints.Add(DemoEndpoint);
        SelectedEndpoint = _realTransport.IsSupported ? null : DemoEndpoint;

        var canConnect = this.WhenAnyValue(
            x => x.SelectedEndpoint,
            x => x.ConnectionState,
            (endpoint, state) => endpoint is not null &&
                state is not (ConnectionState.Connecting or ConnectionState.Reconnecting));

        var notPlaybackLocked = this.WhenAnyValue(x => x.IsPlaybackLocked, locked => !locked);

        // Track() subscribes ThrownExceptions (an unobserved command fault would otherwise crash
        // the process — see ReactiveViewModelBase.Track) and registers the command for disposal.
        ConnectCommand = Track(ReactiveCommand.CreateFromTask(ManualConnectAsync, canConnect)
            .Enhance(text: "Подключить", name: "ConnectCommand"));
        DisconnectCommand = Track(ReactiveCommand.CreateFromTask(ManualDisconnectAsync, notPlaybackLocked)
            .Enhance(text: "Отключить", name: "DisconnectCommand"));
        ResetAlarmCommand = Track(ReactiveCommand.CreateFromTask(ResetAlarmAsync)
            .Enhance(text: "Сброс аварии", name: "ResetAlarmCommand"));
        RefreshEndpointsCommand = Track(ReactiveCommand.CreateFromTask(RefreshEndpointsAsync)
            .Enhance(text: "Обновить список", name: "RefreshEndpointsCommand"));
        ScanCommand = Track(ReactiveCommand.Create(ToggleScan)
            .Enhance(text: "Поиск", name: "ScanCommand"));
        ToggleGCodeLogCommand = Track(ReactiveCommand.Create(() => { IsGCodeLogOpen = !IsGCodeLogOpen; })
            .Enhance(text: "Лог G-code", name: "ToggleGCodeLogCommand"));
        ToggleMockSettingsCommand = Track(ReactiveCommand.Create(() => { IsMockSettingsOpen = !IsMockSettingsOpen; })
            .Enhance(text: "Настройки мока", name: "ToggleMockSettingsCommand"));
        TriggerMockErrorCommand = Track(ReactiveCommand.Create(() => { _currentMockControl?.ForceNextCommandError(MockErrorCode); })
            .Enhance(text: "Смоделировать ошибку", name: "TriggerMockErrorCommand"));
        TriggerMockAlarmCommand = Track(ReactiveCommand.Create(() => { _currentMockControl?.TriggerAlarm(MockAlarmCode); })
            .Enhance(text: "Смоделировать аварию", name: "TriggerMockAlarmCommand"));

        // Immediately mirror a newly-assigned session's state, then keep mirroring it
        // as ConnectionStateChanged fires later (on a background thread for the
        // real-device path — ObserveOn marshals back before the property is set).
        // .Switch() drops the previous session's event subscription the moment
        // Session changes to a new value or null, replacing the old
        // OnSessionChanged-based subscribe/unsubscribe dance.
        this.WhenAnyValue(x => x.Session)
            .Do(s =>
            {
                ConnectionState = s?.ConnectionState ?? ConnectionState.Disconnected;
                LastError = s?.LastError;
                LastAlarmCode = null;
            })
            .Select(s => s is null
                ? Observable.Empty<Unit>()
                : Observable.FromEvent(h => s.ConnectionStateChanged += h, h => s.ConnectionStateChanged -= h)
                    .ObserveOn(RxSchedulers.MainThreadScheduler))
            .Switch()
            .Subscribe(_stateChangedEvent =>
            {
                ConnectionState = Session?.ConnectionState ?? ConnectionState.Disconnected;
                LastError = Session?.LastError;

                // DeviceSession's own fast reconnect-to-known-id loop (3x200ms, unchanged)
                // exhausted on its own — Session is still assigned (only ManualDisconnectAsync
                // nulls it), its ConnectionState just flipped to Disconnected. Hand off to the
                // name-search auto-connect orchestrator unless the user explicitly disconnected.
                if (ConnectionState == ConnectionState.Disconnected && !_autoConnectSuppressed)
                {
                    _ = AutoConnectAsync();
                }
            })
            .DisposeWith(Disposables);

        this.WhenAnyValue(x => x.Session)
            .Select(s => s is null
                ? Observable.Empty<int>()
                : Observable.FromEvent<Action<int>, int>(
                        onNext => code => onNext(code),
                        h => s.AlarmTriggered += h,
                        h => s.AlarmTriggered -= h)
                    .ObserveOn(RxSchedulers.MainThreadScheduler))
            .Switch()
            .Subscribe(code => LastAlarmCode = code)
            .DisposeWith(Disposables);

        // Same mirroring for DeviceStatus (position/machine state), driven by
        // DeviceStatusChanged instead — fires on every status report, so this
        // is what keeps the coordinate/state readout in the header live.
        this.WhenAnyValue(x => x.Session)
            .Do(s => DeviceStatus = s?.DeviceStatus)
            .Select(s => s is null
                ? Observable.Empty<Unit>()
                : Observable.FromEvent(h => s.DeviceStatusChanged += h, h => s.DeviceStatusChanged -= h)
                    .ObserveOn(RxSchedulers.MainThreadScheduler))
            .Switch()
            .Subscribe(_ => DeviceStatus = Session?.DeviceStatus)
            .DisposeWith(Disposables);

        // IsConnectionModalVisible/ConnectionStateLabel are plain computed
        // properties (no ObservableAsPropertyHelper) — re-raise their
        // INotifyPropertyChanged notifications whenever a dependency changes,
        // same intent as CommunityToolkit's [NotifyPropertyChangedFor] before.
        this.WhenAnyValue(x => x.Session, x => x.ConnectionState, x => x.DeviceStatus, x => x.LastError, x => x.LastAlarmCode, x => x.EndpointError,
                (s, cs, ds, le, ac, ee) => (s, cs, ds, le, ac, ee))
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(IsConnectionModalVisible));
                this.RaisePropertyChanged(nameof(IsAlarmModalVisible));
                this.RaisePropertyChanged(nameof(IsAnyModalVisible));
                this.RaisePropertyChanged(nameof(ConnectionStateLabel));
                this.RaisePropertyChanged(nameof(PositionLabel));
                this.RaisePropertyChanged(nameof(HasError));
                this.RaisePropertyChanged(nameof(ErrorMessage));
                this.RaisePropertyChanged(nameof(HasEndpointError));
            })
            .DisposeWith(Disposables);

        this.WhenAnyValue(x => x.AutoConnectPhase, x => x.AutoConnectAttempt,
                (_, _) => Unit.Default)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(IsAutoConnectSplashVisible));
                this.RaisePropertyChanged(nameof(AutoConnectStatusText));
                this.RaisePropertyChanged(nameof(IsConnectionModalVisible));
                this.RaisePropertyChanged(nameof(IsAlarmModalVisible));
                this.RaisePropertyChanged(nameof(IsAnyModalVisible));
            })
            .DisposeWith(Disposables);

        this.WhenAnyValue(x => x.MockResponseDelayMs)
            .Subscribe(ms => _currentMockControl?.SetResponseDelay(TimeSpan.FromMilliseconds(ms)))
            .DisposeWith(Disposables);

        RefreshEndpointsCommand.Execute().Subscribe().DisposeWith(Disposables);
    }

    /// <summary>Entry point for the "Подключить" button. Cancels any in-flight AutoConnectAsync
    /// loop first — a manual choice must never race a background auto-connect attempt — and
    /// clears the suppression flag so a later involuntary disconnect is free to auto-restart.</summary>
    private Task ManualConnectAsync()
    {
        _autoConnectCts?.Cancel();
        _autoConnectSuppressed = false;
        return ConnectAsync();
    }

    /// <summary>Entry point for the "Отключить" button. Cancels any in-flight AutoConnectAsync
    /// loop and suppresses auto-restart (Task 6's subscription) until the next successful
    /// connect — the user turned it off on purpose, auto-connect must not turn it back on.</summary>
    private async Task ManualDisconnectAsync()
    {
        _autoConnectCts?.Cancel();
        _autoConnectSuppressed = true;
        await DisconnectAsync();
    }

    private async Task ConnectAsync()
    {
        if (SelectedEndpoint is null)
        {
            return;
        }

        if (SelectedEndpoint.Kind == ConnectionEndpointKind.RealDevice && !SelectedEndpoint.IsPaired)
        {
            EndpointError = null;
            bool paired;
            try
            {
                paired = await _endpointProvider.PairAsync(SelectedEndpoint.Id);
            }
            catch (Exception ex)
            {
                EndpointError = ex.Message;
                return;
            }

            if (!paired)
            {
                EndpointError = "Не удалось спарить устройство.";
                return;
            }

            var pairedIndex = AvailableEndpoints.IndexOf(SelectedEndpoint);
            var pairedEndpoint = SelectedEndpoint with { IsPaired = true };
            if (pairedIndex >= 0)
            {
                AvailableEndpoints[pairedIndex] = pairedEndpoint;
            }

            SelectedEndpoint = pairedEndpoint;
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

        _currentMockControl = innerTransport as IMockDeviceControl;
        _currentMockControl?.SetResponseDelay(TimeSpan.FromMilliseconds(MockResponseDelayMs));

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
        catch (Exception ex)
        {
            // Ошибку выставляем до DisconnectAsync(): у реального Bluetooth-транспорта
            // закрытие сокета с зависшим нативным Connect() само блокируется, и после
            // await эта строка уже не выполнится — пользователь видел бы вечное
            // "подключение" без единого сообщения.
            EndpointError = ex.Message;
            Session = null;

            // A failed connect leaves the transport's LineReceived/Disconnected handlers
            // subscribed (DeviceSession.ConnectAsync wires them before attempting the
            // transport-level connect). session.DisconnectAsync() unwinds that — critical
            // for the real-device transport, which is a singleton reused by the next
            // attempt; leaked handlers there would double-fire on every subsequent connect.
            await session.DisconnectAsync();
        }
    }

    private async Task DisconnectAsync()
    {
        if (Session is not null)
        {
            await Session.DisconnectAsync();
            Session = null;
            _currentMockControl = null;
        }

        if (_sentGCodeSubscription is not null)
        {
            Disposables.Remove(_sentGCodeSubscription);
            _sentGCodeSubscription = null;
        }
    }

    /// <summary>Finds a FluidNC-named device and connects to it, retrying with the configured
    /// backoff schedule up to _autoConnectRetryPolicy.MaxAttempts times before giving up and
    /// leaving the manual connection modal (IsConnectionModalVisible) as the fallback.
    /// <para>Re-entrancy is bounded, not mutually exclusive. A new call cancels the previous
    /// call's own loop — its pending retry wait and its progression to the next iteration — and
    /// the ReferenceEquals guard in the finally block below ensures a superseded call's
    /// AutoConnectPhase/_autoConnectCts bookkeeping can never clobber a newer call's state. But
    /// the cancel is not awaited, and the core ConnectAsync() this loop calls accepts no
    /// CancellationToken: a superseded call already mid-flight inside ConnectAsync() runs that
    /// attempt to completion in the background and can still mutate Session/SelectedEndpoint/
    /// AvailableEndpoints on the shared real transport afterwards. That is an accepted narrow
    /// race — the loop spends nearly all of its time in discovery or backoff rather than inside
    /// ConnectAsync(), so two calls overlapping there is rare — not a guarantee of mutual
    /// exclusion. Closing it properly would mean threading cancellation through the pre-existing
    /// ConnectAsync().</para>
    /// Never called from the constructor (see Global Constraints) — App.axaml.cs calls it once at
    /// startup, and the restart subscription in the constructor (Task 6) calls it again after
    /// DeviceSession's own fast reconnect-to-known-id loop gives up.</summary>
    public async Task AutoConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsRealDeviceUnsupported)
        {
            return;
        }

        _autoConnectCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _autoConnectCts = cts;
        var token = cts.Token;

        try
        {
            for (var attempt = 1; attempt <= _autoConnectRetryPolicy.MaxAttempts; attempt++)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                AutoConnectAttempt = attempt;
                AutoConnectPhase = AutoConnectPhase.Searching;

                var endpoint = await FindFluidNcEndpointAsync(token);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (endpoint is not null)
                {
                    SelectedEndpoint = endpoint;
                    AutoConnectPhase = AutoConnectPhase.Connecting;
                    await ConnectAsync();

                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    if (Session?.ConnectionState == ConnectionState.Connected)
                    {
                        AutoConnectPhase = AutoConnectPhase.Idle;
                        _autoConnectSuppressed = false;
                        return;
                    }
                }

                if (attempt < _autoConnectRetryPolicy.MaxAttempts)
                {
                    AutoConnectPhase = AutoConnectPhase.WaitingRetry;
                    await _autoConnectRetryPolicy.WaitBeforeRetryAsync(attempt, token);
                }
            }

            AutoConnectPhase = AutoConnectPhase.GivenUp;
            EndpointError ??= "Устройство FluidNC не найдено.";
        }
        // Guarded by our OWN token: an OperationCanceledException raised by anything else (e.g.
        // the transport aborting inside ConnectAsync's DisconnectAsync cleanup, which sits
        // outside any try/catch there) is NOT our cancellation and must propagate rather than be
        // absorbed as "superseded" — swallowing it would exit the loop with AutoConnectPhase
        // frozen mid-flight, no GivenUp, no EndpointError and no retry: a silent hang.
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Superseded by ManualConnectAsync/ManualDisconnectAsync (Task 4) or app shutdown —
            // leave whatever state that action already set; nothing to clean up here.
        }
        finally
        {
            // Only the loop that is still the current one may write back shared state; a
            // superseded loop unwinding later must not stomp on its successor's phase.
            if (ReferenceEquals(_autoConnectCts, cts))
            {
                _autoConnectCts = null;

                // Every cancellation exit — the three early returns above and the catch — leaves
                // the phase at Searching/Connecting/WaitingRetry. finally runs on all of them, so
                // this is the single place that returns it to Idle. Natural-completion paths
                // (success/GivenUp) set their own phase and are untouched: the token is not
                // cancelled there.
                if (token.IsCancellationRequested)
                {
                    AutoConnectPhase = AutoConnectPhase.Idle;
                }
            }
        }
    }

    /// <summary>Looks for a FluidNC-named endpoint: first among already-known endpoints
    /// (RefreshEndpointsAsync), then — if the platform supports it — via a bounded discovery
    /// scan. Every discovered endpoint (matching or not) is still merged into AvailableEndpoints
    /// via OnDeviceDiscovered, exactly like the manual ScanCommand does, so a user who takes over
    /// manually after a give-up sees the same list a manual scan would have produced.</summary>
    private async Task<ConnectionEndpoint?> FindFluidNcEndpointAsync(CancellationToken cancellationToken)
    {
        await RefreshEndpointsAsync();

        var known = AvailableEndpoints.FirstOrDefault(e =>
            e.Kind == ConnectionEndpointKind.RealDevice && FluidNcDeviceName.Matches(e.DisplayName));
        if (known is not null || !IsDiscoverySupported)
        {
            return known;
        }

        var match = await _endpointProvider.Discover()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Do(OnDeviceDiscovered)
            .Where(info => FluidNcDeviceName.Matches(info.Name))
            .Take(1)
            .Select(info => (DeviceEndpointInfo?)info)
            .Timeout(AutoConnectScanWindow, Observable.Return((DeviceEndpointInfo?)null))
            .Catch(Observable.Return((DeviceEndpointInfo?)null))
            .FirstOrDefaultAsync()
            .ToTask(cancellationToken);

        return match is null ? null : AvailableEndpoints.FirstOrDefault(e => e.Id == match.Id);
    }

    private void AppendSentGCodeLine(string line)
    {
        if (SentGCodeLines.Count >= MaxSentGCodeLines)
        {
            SentGCodeLines.RemoveAt(0);
        }

        SentGCodeLines.Add(line);
    }

    private async Task ResetAlarmAsync()
    {
        if (Session is null)
        {
            return;
        }

        await Session.ResetAlarmAsync();
        LastAlarmCode = null;
        LastError = null;
    }

    private async Task RefreshEndpointsAsync()
    {
        if (!_realTransport.IsSupported)
        {
            return;
        }

        var previousSelectedId = SelectedEndpoint?.Id;

        try
        {
            var known = await _endpointProvider.GetKnownEndpointsAsync();
            EndpointError = null;

            var realEndpoints = known
                .Select(info => new ConnectionEndpoint(info.Id, info.Name, ConnectionEndpointKind.RealDevice, info.IsPaired))
                .ToList();

            AvailableEndpoints.Clear();
            foreach (var endpoint in realEndpoints)
            {
                AvailableEndpoints.Add(endpoint);
            }

            AvailableEndpoints.Add(DemoEndpoint);

            SelectedEndpoint =
                AvailableEndpoints.FirstOrDefault(e => e.Id == previousSelectedId) ??
                realEndpoints.FirstOrDefault() ??
                DemoEndpoint;
        }
        catch (Exception ex)
        {
            EndpointError = ex.Message;
        }
    }

    private void ToggleScan()
    {
        if (_scanSubscription is not null)
        {
            _scanSubscription.Dispose();
            _scanSubscription = null;
            IsScanning = false;
            return;
        }

        IsScanning = true;
        EndpointError = null;

        // Assign a placeholder before Subscribe() runs so a synchronous OnCompleted/OnError
        // (e.g. Observable.Empty, or a real provider with no adapter available) can't race the
        // outer "_scanSubscription = ..." assignment below and get overwritten with an
        // already-terminated-but-non-null disposable — that would leave IsScanning == false but
        // _scanSubscription != null, permanently poisoning the next ScanCommand toggle into
        // taking the "stop scanning" branch instead of starting a new scan.
        var subscription = new SingleAssignmentDisposable();
        _scanSubscription = subscription;

        subscription.Disposable = _endpointProvider.Discover()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(
                OnDeviceDiscovered,
                ex =>
                {
                    EndpointError = ex.Message;
                    IsScanning = false;
                    if (ReferenceEquals(_scanSubscription, subscription))
                    {
                        _scanSubscription = null;
                    }
                },
                () =>
                {
                    IsScanning = false;
                    if (ReferenceEquals(_scanSubscription, subscription))
                    {
                        _scanSubscription = null;
                    }
                });
    }

    private void OnDeviceDiscovered(DeviceEndpointInfo info)
    {
        if (AvailableEndpoints.Any(e => e.Id == info.Id))
        {
            return;
        }

        var demoIndex = AvailableEndpoints.IndexOf(DemoEndpoint);
        var insertAt = demoIndex >= 0 ? demoIndex : AvailableEndpoints.Count;
        AvailableEndpoints.Insert(insertAt, new ConnectionEndpoint(info.Id, info.Name, ConnectionEndpointKind.RealDevice, info.IsPaired));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scanSubscription?.Dispose();
        }

        base.Dispose(disposing);
    }
}
