using JGraph.Devices.Bluetooth;

namespace JGraph.Devices.Simulation;

/// <summary>
/// The Bluetooth the tests talk to (device classes plan, stage D3): a radio whose state a test sets,
/// the classic device <c>JGraphPeer</c> whose serial port channel 1 is a <see cref="SimulatedLine"/>
/// driven by the <see cref="PeerEngine"/> (the same in-band protocol as the serial fixtures), and the
/// Low Energy peripheral <c>JGraphPeer</c> with the services <c>tools/devices/esp32-peer</c> gives
/// the real board, so a fixture recorded in R2025b against the board replays here.
/// </summary>
/// <remarks>
/// The peripheral's GATT tree, in order:
/// <list type="bullet">
/// <item>180F Battery Service: 2A19 Battery Level (Read, Notify), value 100.</item>
/// <item>180D Heart Rate: 2A37 Heart Rate Measurement (Notify), 2A38 Body Sensor Location (Read), value 1.</item>
/// <item>FFE0 (custom): FFE1 (Read, WriteWithoutResponse, Write, Notify) with descriptors 2901 and
/// 2902. A write becomes the value and is notified back when notifications are on: an echo.</item>
/// </list>
/// Heart Rate Measurement notifies <c>[0 60+k]</c> every 100 ms while it is subscribed, k counting from
/// 0 and wrapping at 40.
/// </remarks>
public sealed class SimulatedBluetooth : IBluetoothBackend, IDisposable
{
    /// <summary>The peer's classic address; the peripheral's is one more.</summary>
    public const ulong PeerAddress = 0x7C9EBD4A0B10;

    /// <summary>The name both the classic device and the peripheral give.</summary>
    public const string PeerName = "JGraphPeer";

    private readonly SimulatedLine _line = new("JGraphPeer:1");
    private readonly List<SimulatedPeripheral> _connected = new();

    public BluetoothRadioState Radio { get; set; } = BluetoothRadioState.On;

    /// <summary>The serial port device on channel 1.</summary>
    public PeerEngine Engine => _line.Engine;

    public IReadOnlyList<ClassicDeviceInfo> PairedClassic()
    {
        RequireRadio();
        return
        [
            new(PeerName, PeerAddress, 1, _line.InUse ? ClassicDeviceStatus.Connected : ClassicDeviceStatus.Ready),
            new("JGraphHeadset", 0x7C9EBD4A0B20, null, ClassicDeviceStatus.Unsupported),
        ];
    }

    public IReadOnlyList<ClassicDeviceInfo> DiscoverClassic(TimeSpan? timeout, CancellationToken cancel)
    {
        RequireRadio();
        return [.. PairedClassic(), new("JGraphStranger", 0x7C9EBD4A0B30, 1, ClassicDeviceStatus.Unpaired)];
    }

    public IDeviceTransport ConnectRfcomm(ulong address, int channel, CancellationToken cancel)
    {
        RequireRadio();
        if (address != PeerAddress || channel is not (0 or 1))
        {
            throw new DeviceOpenException("No connection could be made because the target machine actively refused it.", 10061);
        }

        return _line.Open(new SerialSettings());
    }

    public IReadOnlyList<BleAdvertisementInfo> Scan(TimeSpan timeout, IReadOnlyList<string> services, CancellationToken cancel)
    {
        RequireRadio();
        var peer = new BleAdvertisementInfo(PeerName, PeerAddress + 1, -48, "Connectable Undirected", null, null, PeerName, 3, null,
            [0xE5, 0x02, 0x4A, 0x47], null, ["180F", "180D", "FFE0"], [], []);
        var beacon = new BleAdvertisementInfo("", 0x7C9EBD4A0B50, -80, "Non-connectable Undirected", null, null, null, null, null,
            [0x4C, 0x00, 0x02, 0x15], null, [], [], []);
        IEnumerable<BleAdvertisementInfo> heard = [peer, beacon];
        if (services.Count > 0)
        {
            heard = heard.Where(a => a.CompleteServiceUuids.Any(u => services.Contains(GattNames.Canonical(u), StringComparer.OrdinalIgnoreCase)));
        }

        return heard.ToList();
    }

    public IBlePeripheral ConnectBle(ulong address, CancellationToken cancel)
    {
        RequireRadio();
        if (address != PeerAddress + 1)
        {
            throw new GattException("Unreachable", "The peripheral could not be reached.");
        }

        var peripheral = new SimulatedPeripheral(this);
        lock (_connected)
        {
            _connected.Add(peripheral);
        }

        return peripheral;
    }

    /// <summary>Drops every peripheral's link, as a board that is switched off does.</summary>
    public void DisconnectAll()
    {
        lock (_connected)
        {
            foreach (SimulatedPeripheral peripheral in _connected)
            {
                peripheral.Drop();
            }
        }
    }

    private void RequireRadio()
    {
        if (Radio != BluetoothRadioState.On)
        {
            throw new BluetoothRadioException(Radio);
        }
    }

    public void Dispose()
    {
        lock (_connected)
        {
            foreach (SimulatedPeripheral peripheral in _connected.ToArray())
            {
                peripheral.Dispose();
            }
        }

        _line.Dispose();
    }

    internal void Forget(SimulatedPeripheral peripheral)
    {
        lock (_connected)
        {
            _connected.Remove(peripheral);
        }
    }

    /// <summary>The peripheral's link: its GATT tree, values, subscriptions and the heart rate timer.</summary>
    internal sealed class SimulatedPeripheral : IBlePeripheral
    {
        private static readonly GattAttributeInfo[] ServiceList =
        [
            new(GattNames.Canonical("180F"), []),
            new(GattNames.Canonical("180D"), []),
            new(GattNames.Canonical("FFE0"), []),
        ];

        private static readonly GattAttributeInfo[][] CharacteristicList =
        [
            [new(GattNames.Canonical("2A19"), ["Read", "Notify"])],
            [new(GattNames.Canonical("2A37"), ["Notify"]), new(GattNames.Canonical("2A38"), ["Read"])],
            [new(GattNames.Canonical("FFE1"), ["Read", "WriteWithoutResponse", "Write", "Notify"])],
        ];

        private readonly SimulatedBluetooth _owner;
        private readonly object _gate = new();
        private readonly Dictionary<(int, int), byte[]> _values = new()
        {
            [(0, 0)] = [100],
            [(1, 1)] = [1],
            [(2, 0)] = [],
        };

        private readonly Dictionary<(int, int), (GattSubscription Kind, Action<byte[], DateTime>? OnValue)> _subscribed = new();
        private readonly byte[] _userDescription = "JGraph echo"u8.ToArray();
        private Timer? _heartRate;
        private int _beat;
        private volatile bool _connected = true;

        public SimulatedPeripheral(SimulatedBluetooth owner) => _owner = owner;

        public string Name => PeerName;

        public ulong Address => PeerAddress + 1;

        public bool Connected => _connected;

        public IReadOnlyList<GattAttributeInfo> Services() => Live(ServiceList);

        public IReadOnlyList<GattAttributeInfo> Characteristics(int service) => Live(CharacteristicList[service]);

        public IReadOnlyList<GattAttributeInfo> Descriptors(int service, int characteristic)
        {
            if (!_connected)
            {
                throw Disconnected();
            }

            return (service, characteristic) switch
            {
                (2, 0) => [new(GattNames.Canonical("2901"), ["Read"]), new(GattNames.Canonical("2902"), ["Read", "Write"])],
                (0, 0) or (1, 0) => [new(GattNames.Canonical("2902"), ["Read", "Write"])],
                _ => [],
            };
        }

        private T Live<T>(T value) => _connected ? value : throw Disconnected();

        public byte[] Read(int service, int characteristic)
        {
            lock (_gate)
            {
                if (!_connected)
                {
                    throw Disconnected();
                }

                return _values.TryGetValue((service, characteristic), out byte[]? value)
                    ? value.ToArray()
                    : throw new GattException("ProtocolErrorReadNotPermitted", "Read not permitted.");
            }
        }

        public void Write(int service, int characteristic, byte[] data, bool withResponse)
        {
            Action<byte[], DateTime>? notify = null;
            lock (_gate)
            {
                if (!_connected)
                {
                    throw Disconnected();
                }

                if ((service, characteristic) != (2, 0))
                {
                    throw new GattException("ProtocolErrorWriteNotPermitted", "Write not permitted.");
                }

                _values[(2, 0)] = data.ToArray();
                if (_subscribed.TryGetValue((2, 0), out var subscription) && subscription.Kind != GattSubscription.None)
                {
                    notify = subscription.OnValue;
                }
            }

            notify?.Invoke(data.ToArray(), DateTime.Now);
        }

        public byte[] ReadDescriptor(int service, int characteristic, int descriptor)
        {
            lock (_gate)
            {
                if (!_connected)
                {
                    throw Disconnected();
                }

                IReadOnlyList<GattAttributeInfo> descriptors = Descriptors(service, characteristic);
                string uuid = GattNames.Shortest(descriptors[descriptor].Uuid);
                if (uuid == "2901")
                {
                    return _userDescription.ToArray();
                }

                GattSubscription kind = _subscribed.TryGetValue((service, characteristic), out var s) ? s.Kind : GattSubscription.None;
                return kind switch
                {
                    GattSubscription.Notify => [1, 0],
                    GattSubscription.Indicate => [2, 0],
                    _ => [0, 0],
                };
            }
        }

        public void WriteDescriptor(int service, int characteristic, int descriptor, byte[] data)
        {
            IReadOnlyList<GattAttributeInfo> descriptors = Descriptors(service, characteristic);
            if (GattNames.Shortest(descriptors[descriptor].Uuid) != "2902")
            {
                throw new GattException("ProtocolErrorWriteNotPermitted", "Write not permitted.");
            }

            GattSubscription kind = data.Length > 0 && (data[0] & 1) != 0 ? GattSubscription.Notify
                : data.Length > 0 && (data[0] & 2) != 0 ? GattSubscription.Indicate
                : GattSubscription.None;
            Action<byte[], DateTime>? onValue;
            lock (_gate)
            {
                onValue = _subscribed.TryGetValue((service, characteristic), out var s) ? s.OnValue : null;
            }

            Subscribe(service, characteristic, kind, onValue);
        }

        public GattSubscription SubscriptionOf(int service, int characteristic)
        {
            lock (_gate)
            {
                return _subscribed.TryGetValue((service, characteristic), out var s) ? s.Kind : GattSubscription.None;
            }
        }

        public void Subscribe(int service, int characteristic, GattSubscription kind, Action<byte[], DateTime>? onValue)
        {
            lock (_gate)
            {
                if (!_connected)
                {
                    throw Disconnected();
                }

                _subscribed[(service, characteristic)] = (kind, onValue);
                bool beating = _subscribed.TryGetValue((1, 0), out var heart) && heart.Kind != GattSubscription.None && heart.OnValue is not null;
                if (beating && _heartRate is null)
                {
                    _beat = 0;
                    _heartRate = new Timer(_ => Beat(), null, 100, 100);
                }
                else if (!beating && _heartRate is not null)
                {
                    _heartRate.Dispose();
                    _heartRate = null;
                }
            }
        }

        private void Beat()
        {
            Action<byte[], DateTime>? onValue;
            byte[] value;
            lock (_gate)
            {
                if (!_connected || !_subscribed.TryGetValue((1, 0), out var heart) || heart.Kind == GattSubscription.None)
                {
                    return;
                }

                onValue = heart.OnValue;
                value = [0, (byte)(60 + _beat)];
                _beat = (_beat + 1) % 40;
            }

            try
            {
                onValue?.Invoke(value, DateTime.Now);
            }
            catch (Exception)
            {
                // A timer thread must not end the process.
            }
        }

        private static GattException Disconnected() => new("Disconnected", "The peripheral is disconnected.");

        public void Drop()
        {
            lock (_gate)
            {
                _connected = false;
                _heartRate?.Dispose();
                _heartRate = null;
            }
        }

        public void Dispose()
        {
            Drop();
            _owner.Forget(this);
        }
    }
}
