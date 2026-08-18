using System;
using System.Linq;
using System.Threading.Tasks;
using ArctZ.Services.Device;
using ArctZ.Services.Diagnostics;
using ArctZ.Tests.Services.Device;
using ArctZ.ViewModels;
using static ArctZ.Tests.TestSupport.AsyncAssert;

namespace ArctZ.Tests.ViewModels;

public class ConnectionViewModelDiagnosticsTests
{
    private static FakeDeviceEndpointProvider DefaultEndpointProvider() => new()
    {
        KnownEndpoints = { new DeviceEndpointInfo("real", "Устройство", true) },
    };

    private static async Task<ConnectionViewModel> CreateConnectedVmAsync(FakeDeviceTransport transport)
    {
        var vm = new ConnectionViewModel(
            transport,
            () => new FakeDeviceTransport(),
            new DeviceSessionFactory(MachineLimits.Default),
            DefaultEndpointProvider());
        await vm.RefreshEndpointsCommand.Execute();
        vm.SelectedEndpoint = vm.AvailableEndpoints.Single(e => e.Kind == ConnectionEndpointKind.RealDevice);
        await vm.ConnectCommand.Execute();
        return vm;
    }

    [Fact]
    public async Task ReceivedLine_IsRecordedInTheExchangeLog()
    {
        var transport = new FakeDeviceTransport();
        var vm = await CreateConnectedVmAsync(transport);

        transport.SimulateReceivedLine("error:9");

        var entry = Assert.Single(vm.ExchangeLog.Snapshot());
        Assert.Equal(DeviceExchangeDirection.Received, entry.Direction);
        Assert.Equal("error:9", entry.Line);
    }

    [Fact]
    public async Task SentLine_IsRecordedInTheExchangeLog()
    {
        var transport = new FakeDeviceTransport();
        var vm = await CreateConnectedVmAsync(transport);

        // Deliberately not awaited: FakeDeviceTransport never answers "ok", so the command
        // stays pending forever. The line still reaches the transport, which is all this asserts.
        _ = vm.Session!.SendGCodeAsync("G0 X10");

        await WaitUntilAsync(
            () => vm.ExchangeLog.Snapshot().Any(e => e.Direction == DeviceExchangeDirection.Sent && e.Line == "G0 X10"),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StatusReports_AreKeptOutOfTheExchangeLog()
    {
        var transport = new FakeDeviceTransport();
        var vm = await CreateConnectedVmAsync(transport);

        transport.SimulateReceivedLine("<Idle|MPos:0.000,0.000,0.000,0.000|FS:0,0>");

        Assert.Empty(vm.ExchangeLog.Snapshot());
    }

    [Fact]
    public async Task ExchangeLog_KeepsOnlyTheMostRecentEntries()
    {
        var transport = new FakeDeviceTransport();
        var vm = await CreateConnectedVmAsync(transport);

        for (var i = 0; i < ConnectionViewModel.MaxExchangeLogEntries + 5; i++)
        {
            transport.SimulateReceivedLine($"[MSG:line {i}]");
        }

        var snapshot = vm.ExchangeLog.Snapshot();
        Assert.Equal(ConnectionViewModel.MaxExchangeLogEntries, snapshot.Count);
        Assert.Equal("[MSG:line 5]", snapshot[0].Line);
    }

    [Fact]
    public async Task FirmwareBanner_IsCapturedFromTheGrblGreeting()
    {
        var transport = new FakeDeviceTransport();
        var vm = await CreateConnectedVmAsync(transport);

        transport.SimulateReceivedLine("Grbl 3.7 [FluidNC v3.7.0 (wifi) '$' for help]");

        Assert.Equal("Grbl 3.7 [FluidNC v3.7.0 (wifi) '$' for help]", vm.FirmwareBanner);
    }

    [Fact]
    public async Task FirmwareBanner_IsCapturedFromAnInfoMessage()
    {
        var transport = new FakeDeviceTransport();
        var vm = await CreateConnectedVmAsync(transport);

        transport.SimulateReceivedLine("[MSG:INFO: FluidNC v3.7.0 https://github.com/bdring/FluidNC]");

        Assert.Equal("[MSG:INFO: FluidNC v3.7.0 https://github.com/bdring/FluidNC]", vm.FirmwareBanner);
    }

    [Fact]
    public async Task FirmwareBanner_IgnoresOrdinaryTraffic()
    {
        var transport = new FakeDeviceTransport();
        var vm = await CreateConnectedVmAsync(transport);

        transport.SimulateReceivedLine("error:9");

        Assert.Null(vm.FirmwareBanner);
    }

    [Fact]
    public async Task FirmwareBanner_KeepsTheFirstGreetingOfTheSession()
    {
        var transport = new FakeDeviceTransport();
        var vm = await CreateConnectedVmAsync(transport);

        transport.SimulateReceivedLine("Grbl 3.7 [FluidNC v3.7.0 (wifi) '$' for help]");
        transport.SimulateReceivedLine("[MSG:INFO: connected]");

        Assert.Equal("Grbl 3.7 [FluidNC v3.7.0 (wifi) '$' for help]", vm.FirmwareBanner);
    }

    [Fact]
    public async Task AlarmCode_IsRecordedInTheErrorLog()
    {
        var vm = await CreateConnectedVmAsync(new FakeDeviceTransport());

        vm.LastAlarmCode = 1;

        var entry = Assert.Single(vm.ErrorLog.Snapshot());
        Assert.Equal(DiagnosticErrorKind.Alarm, entry.Kind);
        Assert.Contains("1", entry.Message);
    }

    [Fact]
    public async Task ClearingAnAlarm_DoesNotAddAnErrorLogEntry()
    {
        var vm = await CreateConnectedVmAsync(new FakeDeviceTransport());
        vm.LastAlarmCode = 1;

        vm.LastAlarmCode = null;

        Assert.Single(vm.ErrorLog.Snapshot());
    }

    [Fact]
    public async Task ConnectionError_IsRecordedInTheErrorLog()
    {
        var vm = await CreateConnectedVmAsync(new FakeDeviceTransport());

        vm.LastError = "порт закрыт";

        var entry = Assert.Single(vm.ErrorLog.Snapshot());
        Assert.Equal(DiagnosticErrorKind.Connection, entry.Kind);
        Assert.Equal("порт закрыт", entry.Message);
    }

    [Fact]
    public async Task EndpointError_IsRecordedInTheErrorLog()
    {
        var vm = await CreateConnectedVmAsync(new FakeDeviceTransport());

        vm.EndpointError = "устройство не найдено";

        var entry = Assert.Single(vm.ErrorLog.Snapshot());
        Assert.Equal(DiagnosticErrorKind.Endpoint, entry.Kind);
        Assert.Equal("устройство не найдено", entry.Message);
    }

    [Fact]
    public async Task ErrorLog_KeepsOnlyTheMostRecentEntries()
    {
        var vm = await CreateConnectedVmAsync(new FakeDeviceTransport());

        for (var i = 0; i < ConnectionViewModel.MaxErrorLogEntries + 3; i++)
        {
            vm.LastError = $"сбой {i}";
        }

        var snapshot = vm.ErrorLog.Snapshot();
        Assert.Equal(ConnectionViewModel.MaxErrorLogEntries, snapshot.Count);
        Assert.Equal("сбой 3", snapshot[0].Message);
    }

    [Fact]
    public async Task Reconnecting_KeepsTheDiagnosticLogs()
    {
        var transport = new FakeDeviceTransport();
        var vm = await CreateConnectedVmAsync(transport);
        transport.SimulateReceivedLine("error:9");
        vm.LastError = "порт закрыт";

        await vm.ConnectCommand.Execute();

        // The whole point of these logs is explaining a drop that already happened;
        // wiping them on the reconnect that follows would destroy the evidence.
        Assert.Contains(vm.ExchangeLog.Snapshot(), e => e.Line == "error:9");
        Assert.Contains(vm.ErrorLog.Snapshot(), e => e.Message == "порт закрыт");
    }

    [Fact]
    public async Task Reconnecting_DoesNotDoubleRecordReceivedLines()
    {
        var transport = new FakeDeviceTransport();
        var vm = await CreateConnectedVmAsync(transport);

        await vm.ConnectCommand.Execute();
        transport.SimulateReceivedLine("error:9");

        // The real-device transport is a singleton: a decorator left attached by the
        // previous session would log every line a second time.
        Assert.Single(vm.ExchangeLog.Snapshot(), e => e.Line == "error:9");
    }
}
