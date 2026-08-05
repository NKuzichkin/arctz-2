using System.Linq;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Tests.Services.Device;
using ArctZ.ViewModels;

namespace ArctZ.Tests.ViewModels;

public class ConnectionViewModelTests
{
    private static ConnectionViewModel CreateVm(IDeviceTransport realTransport, IDeviceTransport? demoTransport = null) =>
        new(realTransport, () => demoTransport ?? new FakeDeviceTransport(), new DeviceSessionFactory(MachineLimits.Default));

    [Fact]
    public void Constructor_DefaultsToFirstEndpointAndListsRealAndDemo()
    {
        var vm = CreateVm(new FakeDeviceTransport());

        Assert.Equal(2, vm.AvailableEndpoints.Count);
        Assert.Contains(vm.AvailableEndpoints, e => e.Kind == ConnectionEndpointKind.RealDevice);
        Assert.Contains(vm.AvailableEndpoints, e => e.Kind == ConnectionEndpointKind.Demo);
        Assert.Equal(ConnectionEndpointKind.RealDevice, vm.SelectedEndpoint!.Kind);
    }

    [Fact]
    public async Task ConnectCommand_DemoSelected_ConnectsUsingDemoTransportNotRealTransport()
    {
        var realTransport = new FakeDeviceTransport();
        var demoTransport = new FakeDeviceTransport();
        var vm = CreateVm(realTransport, demoTransport);
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
        var vm = CreateVm(realTransport);

        await vm.ConnectCommand.Execute();

        Assert.True(realTransport.IsConnected);
    }

    [Fact]
    public async Task DisconnectCommand_DisconnectsActiveSessionAndClearsIt()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = CreateVm(realTransport);
        await vm.ConnectCommand.Execute();

        await vm.DisconnectCommand.Execute();

        Assert.False(realTransport.IsConnected);
        Assert.Null(vm.Session);
    }

    [Fact]
    public async Task ConnectCommand_WhileAlreadyConnected_DisconnectsPreviousSessionBeforeCreatingNewOne()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = CreateVm(realTransport);
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
        var vm = CreateVm(realTransport, demoTransport);
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
        var vm = CreateVm(new FakeDeviceTransport());

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
        var vm = CreateVm(realTransport);

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
        var vm = CreateVm(realTransport);
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
        var vm = CreateVm(realTransport);
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
        var vm = CreateVm(realTransport);
        await vm.ConnectCommand.Execute();

        _ = vm.Session!.SendGCodeAsync("G1 X10 Y20 F500");

        Assert.Equal(new[] { "G1 X10 Y20 F500" }, vm.SentGCodeLines);
    }

    [Fact]
    public async Task ConnectCommand_Reconnecting_ClearsPreviousSentGCodeLines()
    {
        var realTransport = new FakeDeviceTransport();
        var vm = CreateVm(realTransport);
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
        var vm = CreateVm(realTransport);
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
        var vm = CreateVm(realTransport);
        await vm.ConnectCommand.Execute();
        var session = vm.Session!;

        await vm.DisconnectCommand.Execute();
        _ = session.SendGCodeAsync("G1 X1");

        Assert.Empty(vm.SentGCodeLines);
    }

    [Fact]
    public void ToggleGCodeLogCommand_TogglesIsGCodeLogOpen()
    {
        var vm = CreateVm(new FakeDeviceTransport());
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
        var vm = CreateVm(realTransport);
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
        var vm = CreateVm(realTransport);
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
}
