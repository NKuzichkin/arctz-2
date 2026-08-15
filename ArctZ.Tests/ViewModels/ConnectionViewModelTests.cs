using System;
using System.Linq;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Device.Simulation;
using ArctZ.Tests.Services.Device;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class ConnectionViewModelTests
{
    private static FakeDeviceEndpointProvider DefaultEndpointProvider() => new()
    {
        KnownEndpoints = { new DeviceEndpointInfo("real", "Устройство", true) },
    };

    private static async Task<ConnectionViewModel> CreateVmAsync(
        IDeviceTransport realTransport,
        IDeviceTransport? demoTransport = null,
        IDeviceEndpointProvider? endpointProvider = null,
        IReconnectPolicy? autoConnectRetryPolicy = null)
    {
        var vm = new ConnectionViewModel(
            realTransport,
            () => demoTransport ?? new FakeDeviceTransport(),
            new DeviceSessionFactory(MachineLimits.Default),
            endpointProvider ?? DefaultEndpointProvider(),
            autoConnectRetryPolicy);
        await vm.RefreshEndpointsCommand.Execute();
        return vm;
    }

    [Fact]
    public async Task Constructor_DefaultAutoConnectRetryPolicy_HasFiveMaxAttempts()
    {
        var vm = await CreateVmAsync(new FakeDeviceTransport());

        Assert.Equal(5, vm.AutoConnectMaxAttempts);
        Assert.Equal(AutoConnectPhase.Idle, vm.AutoConnectPhase);
        Assert.Equal(0, vm.AutoConnectAttempt);
    }

    [Fact]
    public async Task Constructor_CustomAutoConnectRetryPolicy_IsUsedInsteadOfDefault()
    {
        var customPolicy = new FixedDelayReconnectPolicy(maxAttempts: 2, delay: TimeSpan.FromMilliseconds(1));
        var vm = await CreateVmAsync(new FakeDeviceTransport(), autoConnectRetryPolicy: customPolicy);

        Assert.Equal(2, vm.AutoConnectMaxAttempts);
    }

    [Fact]
    public async Task Constructor_RealTransportSupported_ListsRealAndDemoAndDoesNotFlagUnsupported()
    {
        var vm = await CreateVmAsync(new FakeDeviceTransport());

        Assert.Equal(2, vm.AvailableEndpoints.Count);
        Assert.Contains(vm.AvailableEndpoints, e => e.Kind == ConnectionEndpointKind.RealDevice);
        Assert.Contains(vm.AvailableEndpoints, e => e.Kind == ConnectionEndpointKind.Demo);
        Assert.Equal(ConnectionEndpointKind.RealDevice, vm.SelectedEndpoint!.Kind);
        Assert.False(vm.IsRealDeviceUnsupported);
    }

    [Fact]
    public async Task Constructor_RealTransportUnsupported_OnlyListsDemoAndFlagsUnsupported()
    {
        var realTransport = new FakeDeviceTransport { IsSupported = false };
        var vm = await CreateVmAsync(realTransport);

        Assert.Single(vm.AvailableEndpoints);
        Assert.Equal(ConnectionEndpointKind.Demo, vm.AvailableEndpoints[0].Kind);
        Assert.Equal(ConnectionEndpointKind.Demo, vm.SelectedEndpoint!.Kind);
        Assert.True(vm.IsRealDeviceUnsupported);
    }

    [Fact]
    public async Task ConnectCommand_DemoSelected_ConnectsUsingDemoTransportNotRealTransport()
    {
        var realTransport = new FakeDeviceTransport();
        var demoTransport = new FakeDeviceTransport();
        var vm = await CreateVmAsync(realTransport, demoTransport);
        vm.SelectedEndpoint = vm.AvailableEndpoints.Single(e => e.Kind == ConnectionEndpointKind.Demo);

        await vm.ConnectCommand.Execute();

        Assert.True(demoTransport.IsConnected);
        Assert.False(realTransport.IsConnected);
        Assert.Equal(ConnectionState.Connected, vm.Session!.ConnectionState);
    }

    [Fact]
    public async Task ConnectCommand_RealDeviceSelected_ConnectsUsingRealTransport()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = await CreateVmAsync(realTransport);

        await vm.ConnectCommand.Execute();

        Assert.True(realTransport.IsConnected);
    }

    [Fact]
    public async Task DisconnectCommand_DisconnectsActiveSessionAndClearsIt()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = await CreateVmAsync(realTransport);
        await vm.ConnectCommand.Execute();

        await vm.DisconnectCommand.Execute();

        Assert.False(realTransport.IsConnected);
        Assert.Null(vm.Session);
    }

    [Fact]
    public async Task ManualDisconnect_SuppressesAutoConnectUntilNextSuccessfulConnect()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = await CreateVmAsync(realTransport);
        await vm.ConnectCommand.Execute();

        await vm.DisconnectCommand.Execute();

        // A fire-and-forget AutoConnectAsync() call would flip AutoConnectPhase away from Idle
        // (at minimum to Searching) before its first await — asserting it stays Idle proves no
        // auto-connect loop was started by the explicit disconnect.
        Assert.Equal(AutoConnectPhase.Idle, vm.AutoConnectPhase);
        Assert.Null(vm.Session);

        // Manual reconnect clears the suppression: a subsequent involuntary loss should be free to
        // auto-restart again. This is exercised end-to-end in Task 6; here we only prove the
        // manual connect path still works after a manual disconnect.
        await vm.ConnectCommand.Execute();
        Assert.NotNull(vm.Session);
    }

    [Fact]
    public async Task ConnectCommand_WhileAlreadyConnected_DisconnectsPreviousSessionBeforeCreatingNewOne()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = await CreateVmAsync(realTransport);
        await vm.ConnectCommand.Execute();
        var firstSession = vm.Session;

        await vm.ConnectCommand.Execute();

        Assert.NotNull(firstSession);
        Assert.NotSame(firstSession, vm.Session);
        Assert.Equal(ConnectionState.Disconnected, firstSession!.ConnectionState);
        Assert.Equal(ConnectionState.Connected, vm.Session!.ConnectionState);
        Assert.True(realTransport.IsConnected);
    }

    [Fact]
    public async Task ConnectCommand_SwitchingEndpointWhileConnected_TearsDownTheRealTransportSession()
    {
        var realTransport = new FakeDeviceTransport();
        var demoTransport = new FakeDeviceTransport();
        var vm = await CreateVmAsync(realTransport, demoTransport);
        await vm.ConnectCommand.Execute();

        vm.SelectedEndpoint = vm.AvailableEndpoints.Single(e => e.Kind == ConnectionEndpointKind.Demo);
        await vm.ConnectCommand.Execute();

        Assert.False(realTransport.IsConnected);
        Assert.True(demoTransport.IsConnected);
        Assert.Equal(ConnectionState.Connected, vm.Session!.ConnectionState);
    }

    [Fact]
    public async Task IsConnectionModalVisible_TracksSessionLifecycle()
    {
        var vm = await CreateVmAsync(new FakeDeviceTransport());

        Assert.True(vm.IsConnectionModalVisible);

        await vm.ConnectCommand.Execute();
        Assert.False(vm.IsConnectionModalVisible);

        await vm.DisconnectCommand.Execute();
        Assert.True(vm.IsConnectionModalVisible);
    }

    [Fact]
    public async Task ConnectCommand_TransportThrows_ResetsSessionAndReenablesRetry()
    {
        var realTransport = new FakeDeviceTransport { ConnectFailuresRemaining = 1 };
        var vm = await CreateVmAsync(realTransport);

        await vm.ConnectCommand.Execute();

        Assert.Null(vm.Session);
        Assert.True(vm.IsConnectionModalVisible);
        Assert.True(vm.ConnectCommand.CanExecute(null));

        // Retry succeeds now that ConnectFailuresRemaining is exhausted.
        await vm.ConnectCommand.Execute();
        Assert.NotNull(vm.Session);
        Assert.False(vm.IsConnectionModalVisible);
        Assert.Equal(ConnectionState.Connected, vm.Session!.ConnectionState);
    }

    // The two tests below exercise the .Switch()-based rewrite in ConnectionViewModel's
    // constructor (mirroring Session.ConnectionStateChanged) driven by the SESSION raising a
    // state change on its own, rather than by a command — the scenario that whole rewrite exists
    // for. Every other test in this file drives the VM exclusively through
    // ConnectCommand/DisconnectCommand and would stay green even if .Switch() were deleted
    // outright.

    [Fact]
    public async Task UnsolicitedDisconnect_TransitionsToReconnectingAndShowsModal()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = await CreateVmAsync(realTransport);
        await vm.ConnectCommand.Execute();

        // ConnectFailuresRemaining is set only after the initial connect succeeds, so the
        // upcoming reconnect attempts (not this connect) are the ones that fail — same idiom as
        // ProgramViewModelPlaybackTests.LinkLoss_DuringPlayback_....
        realTransport.ConnectFailuresRemaining = 10;
        realTransport.SimulateDisconnect();

        // DeviceSession.OnTransportDisconnected sets Reconnecting synchronously (via the
        // lock-guarded SerialEventQueue, which drains inline) before it ever awaits a retry, and
        // ConnectionViewModel's ObserveOn uses RxSchedulers.MainThreadScheduler, which
        // ReactiveUIBootstrap pins to ImmediateScheduler for tests — so no wait is needed here.
        Assert.Equal(ConnectionState.Reconnecting, vm.ConnectionState);
        Assert.True(vm.IsConnectionModalVisible);
    }

    [Fact]
    public async Task ConnectionStateChanged_OnAReplacedSession_DoesNotAffectTheViewModel()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = await CreateVmAsync(realTransport);
        await vm.ConnectCommand.Execute();
        var session1 = vm.Session;

        vm.SelectedEndpoint = vm.AvailableEndpoints.Single(e => e.Kind == ConnectionEndpointKind.Demo);
        await vm.ConnectCommand.Execute();
        var session2 = vm.Session;

        Assert.NotNull(session1);
        Assert.NotSame(session1, session2);
        Assert.Equal(ConnectionState.Connected, vm.ConnectionState);

        // session1 is no longer reachable through the VM, but it is still a live DeviceSession
        // object that can raise ConnectionStateChanged independently of anything the VM does
        // (e.g. a reconnect loop or a stray disconnect racing the switch to session2). Calling
        // DisconnectAsync directly on the captured reference reproduces exactly that: it fires
        // ConnectionStateChanged on session1 without going through the VM at all. Without
        // .Switch() unsubscribing from session1 when Session moved to session2 (the one thing the
        // old OnSessionChanged did explicitly), this would stomp vm.ConnectionState back to
        // Disconnected even though session2 is the one actually connected.
        await session1!.DisconnectAsync();

        Assert.Equal(ConnectionState.Connected, vm.ConnectionState);
    }

    [Fact]
    public async Task SendGCode_AfterConnect_AppendsLineToSentGCodeLines()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = await CreateVmAsync(realTransport);
        await vm.ConnectCommand.Execute();

        _ = vm.Session!.SendGCodeAsync("G1 X10 Y20 F500");

        Assert.Equal(new[] { "G1 X10 Y20 F500" }, vm.SentGCodeLines);
    }

    [Fact]
    public async Task ConnectCommand_Reconnecting_ClearsPreviousSentGCodeLines()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = await CreateVmAsync(realTransport);
        await vm.ConnectCommand.Execute();
        _ = vm.Session!.SendGCodeAsync("G1 X10");
        Assert.Single(vm.SentGCodeLines);

        await vm.ConnectCommand.Execute();

        Assert.Empty(vm.SentGCodeLines);
    }

    [Fact]
    public async Task SendGCode_Over200Lines_DropsOldestNotNewest()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = await CreateVmAsync(realTransport);
        await vm.ConnectCommand.Execute();

        for (var i = 0; i < 205; i++)
        {
            _ = vm.Session!.SendGCodeAsync($"G1 X{i}");
            realTransport.SimulateReceivedLine("ok");
        }

        Assert.Equal(200, vm.SentGCodeLines.Count);
        Assert.Equal("G1 X5", vm.SentGCodeLines[0]);
        Assert.Equal("G1 X204", vm.SentGCodeLines[^1]);
    }

    [Fact]
    public async Task DisconnectCommand_StopsAppendingToSentGCodeLines()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = await CreateVmAsync(realTransport);
        await vm.ConnectCommand.Execute();
        var session = vm.Session!;

        await vm.DisconnectCommand.Execute();
        _ = session.SendGCodeAsync("G1 X1");

        Assert.Empty(vm.SentGCodeLines);
    }

    [Fact]
    public async Task ToggleGCodeLogCommand_TogglesIsGCodeLogOpen()
    {
        var vm = await CreateVmAsync(new FakeDeviceTransport());
        Assert.False(vm.IsGCodeLogOpen);

        vm.ToggleGCodeLogCommand.Execute(null);
        Assert.True(vm.IsGCodeLogOpen);

        vm.ToggleGCodeLogCommand.Execute(null);
        Assert.False(vm.IsGCodeLogOpen);
    }

    [Fact]
    public async Task IsAlarmModalVisible_TracksAlarmTriggerAndReset()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = await CreateVmAsync(realTransport);
        await vm.ConnectCommand.Execute();
        Assert.False(vm.IsAlarmModalVisible);
        Assert.False(vm.IsAnyModalVisible);

        realTransport.SimulateReceivedLine("ALARM:1");
        Assert.True(vm.IsAlarmModalVisible);
        Assert.True(vm.IsAnyModalVisible);

        // ResetAlarmCommand is IEnhancedCommand<Unit> (ReactiveUI), which has no ExecuteAsync —
        // Execute() returns a cold IObservable<Unit> that only starts running once subscribed.
        // .GetAwaiter() (System.Reactive.Linq.Observable, already global-used via GlobalUsings.cs)
        // subscribes immediately (starting ResetAlarmAsync's execution up to its "$X" send, which
        // suspends until the queue's TaskCompletionSource resolves) and returns an AsyncSubject<Unit>
        // that is itself awaitable, so it can be captured now and awaited after unblocking the
        // in-flight "$X" with a simulated "ok" — same fire-now/unblock/await-later idiom as
        // ProgramViewModelPlaybackTests' `var playTask = vm.PlayCommand.ExecuteAsync(null); ...
        // transport.SimulateReceivedLine("ok"); await playTask;`, adapted to this VM's ReactiveUI
        // commands instead of ProgramViewModel's CommunityToolkit IAsyncRelayCommand ones.
        var resetAwaiter = vm.ResetAlarmCommand.Execute().GetAwaiter();
        realTransport.SimulateReceivedLine("ok");
        await resetAwaiter;

        Assert.False(vm.IsAlarmModalVisible);
        Assert.False(vm.IsAnyModalVisible);
    }

    [Fact]
    public async Task UnsolicitedDisconnect_DuringAlarm_ConnectionModalWinsOverAlarmModal()
    {
        // Regression test: an alarm firing followed by a routine transport-level link drop
        // (same Session instance, just ConnectionState moving to Reconnecting/Disconnected —
        // see the comment above the Session subscription in ConnectionViewModel's constructor,
        // LastAlarmCode is only cleared when Session itself changes, NOT on a state transition
        // of the same session) used to leave BOTH IsConnectionModalVisible and
        // IsAlarmModalVisible true. Because the alarm modal is the last child of MainView's root
        // Grid, it painted over the connection modal, hiding the only working recovery control
        // ("Подключить") behind a hit-testable scrim — "Сброс аварии" alone can't recover
        // because it depends on a live link to get an "ok" back. ConnectionViewModel now favors
        // the connection modal whenever both conditions are true.
        var realTransport = new FakeDeviceTransport();
        var vm = await CreateVmAsync(realTransport);
        await vm.ConnectCommand.Execute();

        realTransport.SimulateReceivedLine("ALARM:1");
        Assert.True(vm.IsAlarmModalVisible);
        Assert.False(vm.IsConnectionModalVisible);
        Assert.True(vm.IsAnyModalVisible);

        // Same idiom as UnsolicitedDisconnect_TransitionsToReconnectingAndShowsModal: the
        // upcoming reconnect attempts must fail so the VM observes Reconnecting rather than
        // racing straight back to Connected.
        realTransport.ConnectFailuresRemaining = 10;
        realTransport.SimulateDisconnect();

        Assert.Equal(ConnectionState.Reconnecting, vm.ConnectionState);
        Assert.True(vm.IsConnectionModalVisible);
        Assert.False(vm.IsAlarmModalVisible);
        Assert.True(vm.IsAnyModalVisible);
    }

    [Fact]
    public async Task ToggleMockSettingsCommand_TogglesIsMockSettingsOpen()
    {
        var vm = await CreateVmAsync(new FakeDeviceTransport());
        Assert.False(vm.IsMockSettingsOpen);

        vm.ToggleMockSettingsCommand.Execute(null);
        Assert.True(vm.IsMockSettingsOpen);

        vm.ToggleMockSettingsCommand.Execute(null);
        Assert.False(vm.IsMockSettingsOpen);
    }

    [Fact]
    public async Task TriggerMockErrorAndAlarmCommands_ConnectedToNonMockDemoTransport_DoNotThrow()
    {
        // The default demo transport in these tests is FakeDeviceTransport, which does not
        // implement IMockDeviceControl — this exercises the cast-miss no-op path, not just
        // "never connected".
        var vm = await CreateVmAsync(new FakeDeviceTransport());
        vm.SelectedEndpoint = vm.AvailableEndpoints.Single(e => e.Kind == ConnectionEndpointKind.Demo);
        await vm.ConnectCommand.Execute();

        var errorException = Record.Exception(() => vm.TriggerMockErrorCommand.Execute(null));
        var alarmException = Record.Exception(() => vm.TriggerMockAlarmCommand.Execute(null));

        Assert.Null(errorException);
        Assert.Null(alarmException);
    }

    [Fact]
    public async Task TriggerMockAlarmCommand_WhileConnectedToRealMockTransport_SetsLastAlarmCodeToOne()
    {
        var realTransport = new FakeDeviceTransport();
        var mockTransport = new MockDeviceTransport(MachineLimits.Default, new ManualPeriodicTimer(), TimeSpan.FromMilliseconds(100));
        var vm = await CreateVmAsync(realTransport, mockTransport);
        vm.SelectedEndpoint = vm.AvailableEndpoints.Single(e => e.Kind == ConnectionEndpointKind.Demo);
        await vm.ConnectCommand.Execute();
        Assert.False(vm.IsAlarmModalVisible);

        vm.TriggerMockAlarmCommand.Execute(null);

        Assert.Equal(1, vm.LastAlarmCode);
        Assert.True(vm.IsAlarmModalVisible);
    }

    [Fact]
    public async Task MockResponseDelayMs_ChangedWhileConnected_DelaysNextCommandAck()
    {
        var realTransport = new FakeDeviceTransport();
        var ticker = new ManualPeriodicTimer();
        var mockTransport = new MockDeviceTransport(MachineLimits.Default, ticker, TimeSpan.FromMilliseconds(100));
        var vm = await CreateVmAsync(realTransport, mockTransport);
        vm.SelectedEndpoint = vm.AvailableEndpoints.Single(e => e.Kind == ConnectionEndpointKind.Demo);
        await vm.ConnectCommand.Execute();

        vm.MockResponseDelayMs = 200; // 2 ticks at the 100ms tick interval

        var sendTask = vm.Session!.SendGCodeAsync("G4 P0");
        ticker.RaiseElapsed();
        Assert.False(sendTask.IsCompleted);
        ticker.RaiseElapsed();
        Assert.False(sendTask.IsCompleted);
        ticker.RaiseElapsed();

        Assert.True(sendTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task AvailableEndpoints_MultipleKnownDevices_RealDevicesListedBeforeDemo()
    {
        var provider = new FakeDeviceEndpointProvider
        {
            KnownEndpoints =
            {
                new DeviceEndpointInfo("aa:bb", "FluidNC-1", true),
                new DeviceEndpointInfo("cc:dd", "FluidNC-2", true),
            },
        };
        var vm = await CreateVmAsync(new FakeDeviceTransport(), endpointProvider: provider);

        Assert.Equal(3, vm.AvailableEndpoints.Count);
        Assert.Equal("aa:bb", vm.AvailableEndpoints[0].Id);
        Assert.Equal("cc:dd", vm.AvailableEndpoints[1].Id);
        Assert.Equal(ConnectionEndpointKind.Demo, vm.AvailableEndpoints[2].Kind);
    }

    [Fact]
    public async Task RefreshEndpointsCommand_PreservesManuallySelectedEndpointById()
    {
        var provider = new FakeDeviceEndpointProvider
        {
            KnownEndpoints = { new DeviceEndpointInfo("aa:bb", "FluidNC-1", true) },
        };
        var vm = await CreateVmAsync(new FakeDeviceTransport(), endpointProvider: provider);
        vm.SelectedEndpoint = vm.AvailableEndpoints.Single(e => e.Kind == ConnectionEndpointKind.Demo);

        await vm.RefreshEndpointsCommand.Execute();

        Assert.Equal(ConnectionEndpointKind.Demo, vm.SelectedEndpoint!.Kind);
    }

    [Fact]
    public async Task RefreshEndpointsCommand_ProviderThrows_SetsEndpointErrorAndKeepsExistingList()
    {
        var provider = new FakeDeviceEndpointProvider
        {
            KnownEndpoints = { new DeviceEndpointInfo("aa:bb", "FluidNC-1", true) },
        };
        var vm = await CreateVmAsync(new FakeDeviceTransport(), endpointProvider: provider);
        Assert.Equal(2, vm.AvailableEndpoints.Count);

        provider.GetKnownEndpointsException = new InvalidOperationException("Нет разрешения на Bluetooth");
        await vm.RefreshEndpointsCommand.Execute();

        Assert.Equal("Нет разрешения на Bluetooth", vm.EndpointError);
        Assert.True(vm.HasEndpointError);
        Assert.Equal(2, vm.AvailableEndpoints.Count);
    }

    [Fact]
    public async Task ScanCommand_DeviceFound_AddsItBeforeDemoWithoutDuplicates()
    {
        var provider = new FakeDeviceEndpointProvider();
        var vm = await CreateVmAsync(new FakeDeviceTransport(), endpointProvider: provider);
        Assert.Single(vm.AvailableEndpoints);

        vm.ScanCommand.Execute(null);
        Assert.True(vm.IsScanning);
        provider.DiscoverySubject.OnNext(new DeviceEndpointInfo("aa:bb", "FluidNC-1", false));
        provider.DiscoverySubject.OnNext(new DeviceEndpointInfo("aa:bb", "FluidNC-1", false));
        provider.DiscoverySubject.OnCompleted();

        Assert.False(vm.IsScanning);
        Assert.Equal(2, vm.AvailableEndpoints.Count);
        Assert.Equal("aa:bb", vm.AvailableEndpoints[0].Id);
        Assert.Equal(ConnectionEndpointKind.Demo, vm.AvailableEndpoints[1].Kind);
    }

    [Fact]
    public async Task ScanCommand_InvokedWhileScanning_StopsTheScan()
    {
        var provider = new FakeDeviceEndpointProvider();
        var vm = await CreateVmAsync(new FakeDeviceTransport(), endpointProvider: provider);

        vm.ScanCommand.Execute(null);
        Assert.True(vm.IsScanning);

        vm.ScanCommand.Execute(null);

        Assert.False(vm.IsScanning);
        Assert.False(provider.DiscoverySubject.HasObservers);
    }

    [Fact]
    public async Task ScanCommand_ProviderCompletesSynchronously_CanBeInvokedAgainAfterward()
    {
        // Regression test: Discover() completing synchronously on Subscribe() (as
        // Observable.Empty does, matching SingleRealDeviceEndpointProvider and
        // AndroidBluetoothEndpointProvider with no adapter) used to race the
        // "_scanSubscription = ..." assignment in ToggleScan — the OnCompleted callback ran
        // before that assignment and nulled the field, which the outer assignment then
        // overwrote back to a non-null (but already-terminated) disposable. IsScanning ended up
        // false while _scanSubscription stayed non-null, so the *next* ScanCommand.Execute()
        // took the "stop scanning" branch (disposed the dead subscription, no-op'd
        // IsScanning = false) instead of starting a new scan — the toggle was permanently
        // poisoned. This is fixed via a SingleAssignmentDisposable placeholder assigned before
        // Subscribe() runs.
        var provider = new FakeDeviceEndpointProvider { DiscoverOverride = () => Observable.Empty<DeviceEndpointInfo>() };
        var vm = await CreateVmAsync(new FakeDeviceTransport(), endpointProvider: provider);

        vm.ScanCommand.Execute(null);
        Assert.False(vm.IsScanning);
        Assert.Equal(1, provider.DiscoverCallCount);

        // Before the fix, this took the "stop scanning" branch (dead _scanSubscription left
        // over from the synchronous completion above) and never called Discover() again.
        vm.ScanCommand.Execute(null);
        Assert.False(vm.IsScanning);
        Assert.Equal(2, provider.DiscoverCallCount);
    }

    [Fact]
    public async Task ConnectCommand_UnpairedRealDeviceSelected_PairsBeforeConnecting()
    {
        var provider = new FakeDeviceEndpointProvider
        {
            KnownEndpoints = { new DeviceEndpointInfo("aa:bb", "FluidNC-1", false) },
        };
        var realTransport = new FakeDeviceTransport();
        var vm = await CreateVmAsync(realTransport, endpointProvider: provider);

        await vm.ConnectCommand.Execute();

        Assert.Equal(new[] { "aa:bb" }, provider.PairedIds);
        Assert.True(realTransport.IsConnected);
        Assert.True(vm.SelectedEndpoint!.IsPaired);
    }

    [Fact]
    public async Task ConnectCommand_PairingFails_DoesNotCreateSessionAndSetsEndpointError()
    {
        var provider = new FakeDeviceEndpointProvider
        {
            KnownEndpoints = { new DeviceEndpointInfo("aa:bb", "FluidNC-1", false) },
            PairResult = false,
        };
        var realTransport = new FakeDeviceTransport();
        var vm = await CreateVmAsync(realTransport, endpointProvider: provider);

        await vm.ConnectCommand.Execute();

        Assert.Null(vm.Session);
        Assert.False(realTransport.IsConnected);
        Assert.False(string.IsNullOrEmpty(vm.EndpointError));
    }

    [Fact]
    public async Task ConnectCommand_PairingThrows_DoesNotCreateSessionAndSurfacesTheError()
    {
        var provider = new FakeDeviceEndpointProvider
        {
            KnownEndpoints = { new DeviceEndpointInfo("aa:bb", "FluidNC-1", false) },
            PairException = new InvalidOperationException("Нет разрешения на Bluetooth"),
        };
        var vm = await CreateVmAsync(new FakeDeviceTransport(), endpointProvider: provider);

        await vm.ConnectCommand.Execute();

        Assert.Null(vm.Session);
        Assert.Equal("Нет разрешения на Bluetooth", vm.EndpointError);
    }

    [Fact]
    public async Task ConnectCommand_AlreadyPairedRealDevice_DoesNotCallPairAsync()
    {
        var provider = new FakeDeviceEndpointProvider
        {
            KnownEndpoints = { new DeviceEndpointInfo("aa:bb", "FluidNC-1", true) },
        };
        var vm = await CreateVmAsync(new FakeDeviceTransport(), endpointProvider: provider);

        await vm.ConnectCommand.Execute();

        Assert.Empty(provider.PairedIds);
    }
}
