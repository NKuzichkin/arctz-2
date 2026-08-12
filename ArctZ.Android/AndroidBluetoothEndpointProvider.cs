using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.Bluetooth;
using Android.Content;
using ArctZ.Services.Device;

namespace ArctZ.Android;

/// <summary>
/// IDeviceEndpointProvider поверх BluetoothAdapter: спаренные устройства через
/// BondedDevices, поиск через ACTION_FOUND/ACTION_DISCOVERY_FINISHED, спаривание
/// через CreateBond + ACTION_BOND_STATE_CHANGED.
/// </summary>
public sealed class AndroidBluetoothEndpointProvider : IDeviceEndpointProvider
{
    private static readonly TimeSpan PairTimeout = TimeSpan.FromSeconds(60);

    private readonly AndroidPermissions _permissions;

    public AndroidBluetoothEndpointProvider(AndroidPermissions permissions)
    {
        _permissions = permissions;
    }

    public bool SupportsDiscovery => true;

    public async Task<IReadOnlyList<DeviceEndpointInfo>> GetKnownEndpointsAsync(CancellationToken cancellationToken = default)
    {
        var granted = await _permissions.RequestAsync(ConnectPermissions()).ConfigureAwait(false);
        if (!granted)
        {
            throw new InvalidOperationException("Нет разрешения на использование Bluetooth.");
        }

        var adapter = BluetoothAdapter.DefaultAdapter
            ?? throw new InvalidOperationException("Bluetooth недоступен на этом устройстве.");

        return adapter.BondedDevices?
            .Select(d => new DeviceEndpointInfo(d.Address!, d.Name ?? d.Address!, true))
            .ToList<DeviceEndpointInfo>()
            ?? new List<DeviceEndpointInfo>();
    }

    public IObservable<DeviceEndpointInfo> Discover() => Observable.Create<DeviceEndpointInfo>(observer =>
    {
        var adapter = BluetoothAdapter.DefaultAdapter;
        if (adapter is null)
        {
            observer.OnCompleted();
            return () => { };
        }

        var context = global::Android.App.Application.Context;
        var receiver = new DiscoveryReceiver(observer);
        var filter = new IntentFilter();
        filter.AddAction(BluetoothDevice.ActionFound!);
        filter.AddAction(BluetoothAdapter.ActionDiscoveryFinished!);

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            context.RegisterReceiver(receiver, filter, ReceiverFlags.NotExported);
        }
        else
        {
            context.RegisterReceiver(receiver, filter);
        }

        adapter.StartDiscovery();

        return () =>
        {
            adapter.CancelDiscovery();
            context.UnregisterReceiver(receiver);
        };
    });

    public async Task<bool> PairAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var granted = await _permissions.RequestAsync(ConnectPermissions()).ConfigureAwait(false);
        if (!granted)
        {
            throw new InvalidOperationException("Нет разрешения на использование Bluetooth.");
        }

        var adapter = BluetoothAdapter.DefaultAdapter
            ?? throw new InvalidOperationException("Bluetooth недоступен на этом устройстве.");

        var device = adapter.GetRemoteDevice(deviceId)
            ?? throw new InvalidOperationException("Устройство не найдено.");

        if (device.BondState == Bond.Bonded)
        {
            return true;
        }

        var tcs = new TaskCompletionSource<bool>();
        var context = global::Android.App.Application.Context;
        var receiver = new BondStateReceiver(deviceId, tcs);
        context.RegisterReceiver(receiver, new IntentFilter(BluetoothDevice.ActionBondStateChanged));

        try
        {
            device.CreateBond();
            using var timeoutCts = new CancellationTokenSource(PairTimeout);
            using var registration = timeoutCts.Token.Register(() => tcs.TrySetResult(false));
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            context.UnregisterReceiver(receiver);
        }
    }

    private static string[] ConnectPermissions() =>
        OperatingSystem.IsAndroidVersionAtLeast(31)
            ? new[] { "android.permission.BLUETOOTH_CONNECT" }
            : new[] { "android.permission.BLUETOOTH" };

    private sealed class DiscoveryReceiver : BroadcastReceiver
    {
        private readonly IObserver<DeviceEndpointInfo> _observer;

        public DiscoveryReceiver(IObserver<DeviceEndpointInfo> observer)
        {
            _observer = observer;
        }

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent is null)
            {
                return;
            }

            if (intent.Action == BluetoothAdapter.ActionDiscoveryFinished)
            {
                _observer.OnCompleted();
                return;
            }

            if (intent.Action != BluetoothDevice.ActionFound)
            {
                return;
            }

#pragma warning disable CA1422, CS0618
            var device = intent.GetParcelableExtra(BluetoothDevice.ExtraDevice) as BluetoothDevice;
#pragma warning restore CA1422, CS0618

            if (device?.Address is null)
            {
                return;
            }

            _observer.OnNext(new DeviceEndpointInfo(device.Address, device.Name ?? device.Address, device.BondState == Bond.Bonded));
        }
    }

    private sealed class BondStateReceiver : BroadcastReceiver
    {
        private readonly string _deviceId;
        private readonly TaskCompletionSource<bool> _completion;

        public BondStateReceiver(string deviceId, TaskCompletionSource<bool> completion)
        {
            _deviceId = deviceId;
            _completion = completion;
        }

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action != BluetoothDevice.ActionBondStateChanged)
            {
                return;
            }

#pragma warning disable CA1422, CS0618
            var device = intent.GetParcelableExtra(BluetoothDevice.ExtraDevice) as BluetoothDevice;
#pragma warning restore CA1422, CS0618

            if (device?.Address != _deviceId)
            {
                return;
            }

            var bondState = (Bond)intent.GetIntExtra(BluetoothDevice.ExtraBondState, (int)Bond.None);
            if (bondState == Bond.Bonded)
            {
                _completion.TrySetResult(true);
            }
            else if (bondState == Bond.None)
            {
                _completion.TrySetResult(false);
            }
        }
    }
}
