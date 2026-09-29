using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace JGraph.Devices.Bluetooth;

/// <summary>
/// The Windows backend for <c>bluetooth</c>, <c>bluetoothlist</c>, <c>ble</c> and <c>blelist</c>
/// (device classes plan, stage D3), over Windows.Devices.Bluetooth: the radio's state, classic
/// discovery with the serial port channel read from each device's SDP record, RFCOMM through Winsock
/// (<see cref="RfcommTransport"/>), advertisement scanning, and GATT. Every call blocks the script
/// thread until the WinRT operation ends; none needs a synchronization context.
/// </summary>
public sealed class WinRtBluetoothBackend : IBluetoothBackend
{
    /// <summary>How long the classic inquiry runs when <c>bluetoothlist</c> names no Timeout.</summary>
    private static readonly TimeSpan DefaultInquiry = TimeSpan.FromSeconds(5);

    public BluetoothRadioState Radio
    {
        get
        {
            IReadOnlyList<Radio> radios = Windows.Devices.Radios.Radio.GetRadiosAsync().AsTask().GetAwaiter().GetResult();
            Radio? bluetooth = radios.FirstOrDefault(static r => r.Kind == RadioKind.Bluetooth);
            return bluetooth is null ? BluetoothRadioState.Missing
                : bluetooth.State == RadioState.On ? BluetoothRadioState.On
                : BluetoothRadioState.Off;
        }
    }

    // --- classic -----------------------------------------------------------------------------------

    public IReadOnlyList<ClassicDeviceInfo> PairedClassic() =>
        Describe(DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true)).AsTask().GetAwaiter().GetResult(), paired: true);

    public IReadOnlyList<ClassicDeviceInfo> DiscoverClassic(TimeSpan? timeout, CancellationToken cancel)
    {
        var found = new List<ClassicDeviceInfo>(PairedClassic());
        var unpaired = new Dictionary<string, DeviceInformation>(StringComparer.Ordinal);
        DeviceWatcher watcher = DeviceInformation.CreateWatcher(BluetoothDevice.GetDeviceSelectorFromPairingState(false));
        watcher.Added += (_, info) =>
        {
            lock (unpaired)
            {
                unpaired[info.Id] = info;
            }
        };
        watcher.Updated += static (_, _) => { };
        watcher.Start();
        try
        {
            cancel.WaitHandle.WaitOne(timeout ?? DefaultInquiry);
        }
        finally
        {
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            {
                watcher.Stop();
            }
        }

        cancel.ThrowIfCancellationRequested();
        DeviceInformation[] seen;
        lock (unpaired)
        {
            seen = unpaired.Values.ToArray();
        }

        found.AddRange(Describe(seen, paired: false));
        return found;
    }

    private static List<ClassicDeviceInfo> Describe(IEnumerable<DeviceInformation> devices, bool paired)
    {
        var answers = new List<ClassicDeviceInfo>();
        foreach (DeviceInformation info in devices)
        {
            BluetoothDevice? device;
            try
            {
                device = BluetoothDevice.FromIdAsync(info.Id).AsTask().GetAwaiter().GetResult();
            }
            catch (Exception e) when (e is COMException or UnauthorizedAccessException)
            {
                continue;
            }

            if (device is null)
            {
                continue;
            }

            using (device)
            {
                answers.Add(DescribeOne(device, paired));
            }
        }

        return answers;
    }

    private static ClassicDeviceInfo DescribeOne(BluetoothDevice device, bool paired)
    {
        string name = device.Name ?? "";
        int? channel = null;
        ClassicDeviceStatus status;
        try
        {
            RfcommDeviceServicesResult services = device.GetRfcommServicesForIdAsync(RfcommServiceId.SerialPort, BluetoothCacheMode.Uncached)
                .AsTask().GetAwaiter().GetResult();
            if (services.Error != BluetoothError.Success)
            {
                status = ClassicDeviceStatus.Unknown;
            }
            else if (services.Services.Count == 0)
            {
                status = ClassicDeviceStatus.Unsupported;
            }
            else
            {
                channel = ChannelOf(services.Services[0]);
                status = !paired ? ClassicDeviceStatus.Unpaired
                    : device.ConnectionStatus == BluetoothConnectionStatus.Connected ? ClassicDeviceStatus.Connected
                    : ClassicDeviceStatus.Ready;
            }
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException)
        {
            status = ClassicDeviceStatus.Unknown;
        }

        return new ClassicDeviceInfo(name, device.BluetoothAddress, channel, status);
    }

    /// <summary>The RFCOMM channel in a service's SDP protocol descriptor list (attribute 0x0004).</summary>
    private static int? ChannelOf(RfcommDeviceService service)
    {
        try
        {
            IReadOnlyDictionary<uint, Windows.Storage.Streams.IBuffer> attributes =
                service.GetSdpRawAttributesAsync(BluetoothCacheMode.Uncached).AsTask().GetAwaiter().GetResult();
            return attributes.TryGetValue(4, out Windows.Storage.Streams.IBuffer? list) ? SdpRecord.RfcommChannel(list.ToArray()) : null;
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public IDeviceTransport ConnectRfcomm(ulong address, int channel, CancellationToken cancel) =>
        RfcommTransport.Connect(address, channel, cancel);

    // --- Low Energy scanning ------------------------------------------------------------------------

    public IReadOnlyList<BleAdvertisementInfo> Scan(TimeSpan timeout, IReadOnlyList<string> services, CancellationToken cancel)
    {
        var heard = new Dictionary<ulong, AdvertisementSoFar>();
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        watcher.Received += (_, args) =>
        {
            try
            {
                lock (heard)
                {
                    if (!heard.TryGetValue(args.BluetoothAddress, out AdvertisementSoFar? soFar))
                    {
                        soFar = new AdvertisementSoFar(args.BluetoothAddress);
                        heard[args.BluetoothAddress] = soFar;
                    }

                    soFar.Take(args);
                }
            }
            catch (Exception)
            {
                // An advertisement that cannot be read is skipped; the scan goes on.
            }
        };
        watcher.Start();
        try
        {
            cancel.WaitHandle.WaitOne(timeout);
        }
        finally
        {
            watcher.Stop();
        }

        cancel.ThrowIfCancellationRequested();
        List<BleAdvertisementInfo> answers;
        lock (heard)
        {
            answers = heard.Values.Select(static s => s.Info()).ToList();
        }

        if (services.Count > 0)
        {
            answers = answers.Where(a => a.CompleteServiceUuids.Concat(a.IncompleteServiceUuids)
                .Any(u => services.Contains(GattNames.Canonical(u), StringComparer.OrdinalIgnoreCase))).ToList();
        }

        return answers.OrderByDescending(static a => a.Rssi).ToList();
    }

    /// <summary>What a peripheral's advertisements and scan responses have said so far.</summary>
    private sealed class AdvertisementSoFar
    {
        private readonly ulong _address;
        private string _type = "";
        private int _rssi;
        private string? _localName;
        private int? _appearance;
        private string? _shortName;
        private string? _completeName;
        private int? _txPower;
        private (double, double)? _interval;
        private byte[]? _manufacturer;
        private byte[]? _serviceData;
        private readonly List<string> _complete = new();
        private readonly List<string> _incomplete = new();
        private readonly List<string> _solicited = new();

        public AdvertisementSoFar(ulong address) => _address = address;

        public void Take(BluetoothLEAdvertisementReceivedEventArgs args)
        {
            _rssi = args.RawSignalStrengthInDBm;
            if (args.AdvertisementType != BluetoothLEAdvertisementType.ScanResponse)
            {
                _type = args.AdvertisementType switch
                {
                    BluetoothLEAdvertisementType.ConnectableUndirected => "Connectable Undirected",
                    BluetoothLEAdvertisementType.ConnectableDirected => "Connectable Directed",
                    BluetoothLEAdvertisementType.ScannableUndirected => "Scannable Undirected",
                    BluetoothLEAdvertisementType.NonConnectableUndirected => "Non-connectable Undirected",
                    _ => args.AdvertisementType.ToString(),
                };
            }

            if (!string.IsNullOrEmpty(args.Advertisement.LocalName))
            {
                _localName = args.Advertisement.LocalName;
            }

            foreach (BluetoothLEAdvertisementDataSection section in args.Advertisement.DataSections)
            {
                byte[] data = section.Data.ToArray();
                switch (section.DataType)
                {
                    case 0x02:
                        AddUuids(_incomplete, data, 2);
                        break;
                    case 0x03:
                        AddUuids(_complete, data, 2);
                        break;
                    case 0x04:
                        AddUuids(_incomplete, data, 4);
                        break;
                    case 0x05:
                        AddUuids(_complete, data, 4);
                        break;
                    case 0x06:
                        AddUuids(_incomplete, data, 16);
                        break;
                    case 0x07:
                        AddUuids(_complete, data, 16);
                        break;
                    case 0x08:
                        _shortName = System.Text.Encoding.UTF8.GetString(data);
                        break;
                    case 0x09:
                        _completeName = System.Text.Encoding.UTF8.GetString(data);
                        break;
                    case 0x0A when data.Length >= 1:
                        _txPower = unchecked((sbyte)data[0]);
                        break;
                    case 0x12 when data.Length >= 4:
                        _interval = (BitConverter.ToUInt16(data, 0) * 1.25, BitConverter.ToUInt16(data, 2) * 1.25);
                        break;
                    case 0x14:
                        AddUuids(_solicited, data, 2);
                        break;
                    case 0x15:
                        AddUuids(_solicited, data, 16);
                        break;
                    case 0x16 or 0x20 or 0x21:
                        _serviceData = data;
                        break;
                    case 0x19 when data.Length >= 2:
                        _appearance = BitConverter.ToUInt16(data, 0);
                        break;
                    case 0xFF:
                        _manufacturer = data;
                        break;
                }
            }
        }

        private static void AddUuids(List<string> into, byte[] data, int size)
        {
            for (int i = 0; i + size <= data.Length; i += size)
            {
                string uuid = size switch
                {
                    2 => BitConverter.ToUInt16(data, i).ToString("X4", System.Globalization.CultureInfo.InvariantCulture),
                    4 => BitConverter.ToUInt32(data, i).ToString("X8", System.Globalization.CultureInfo.InvariantCulture),
                    _ => new Guid(data.AsSpan(i, 16)).ToString("D").ToUpperInvariant(),
                };
                string shortest = GattNames.Shortest(GattNames.Canonical(uuid));
                if (!into.Contains(shortest))
                {
                    into.Add(shortest);
                }
            }
        }

        public BleAdvertisementInfo Info() => new(
            _localName ?? _completeName ?? _shortName ?? "", _address, _rssi, _type, _appearance, _shortName, _completeName, _txPower,
            _interval, _manufacturer, _serviceData, _complete.ToArray(), _incomplete.ToArray(), _solicited.ToArray());
    }

    // --- Low Energy connections -----------------------------------------------------------------------

    public IBlePeripheral ConnectBle(ulong address, CancellationToken cancel)
    {
        BluetoothLEDevice? device = BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(cancel).GetAwaiter().GetResult();
        if (device is null)
        {
            throw new GattException("Unreachable", "The peripheral could not be reached.");
        }

        try
        {
            return new WinRtPeripheral(device);
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }

    /// <summary>A connected peripheral: its GATT tree read once, uncached, at connection.</summary>
    private sealed class WinRtPeripheral : IBlePeripheral
    {
        private readonly BluetoothLEDevice _device;
        private readonly List<GattDeviceService> _services = new();
        private readonly List<List<GattCharacteristic>> _characteristics = new();
        private readonly Dictionary<(int, int), List<GattDescriptor>> _descriptors = new();
        private readonly Dictionary<(int, int), TypedEventHandlerHolder> _subscriptions = new();

        public WinRtPeripheral(BluetoothLEDevice device)
        {
            _device = device;
            GattDeviceServicesResult services = device.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask().GetAwaiter().GetResult();
            Check(services.Status);
            foreach (GattDeviceService service in services.Services)
            {
                _services.Add(service);
                GattCharacteristicsResult characteristics = service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask().GetAwaiter().GetResult();
                _characteristics.Add(characteristics.Status == GattCommunicationStatus.Success
                    ? characteristics.Characteristics.ToList()
                    : throw Failure(characteristics.Status));
            }
        }

        public string Name => _device.Name ?? "";

        public ulong Address => _device.BluetoothAddress;

        public bool Connected => _device.ConnectionStatus == BluetoothConnectionStatus.Connected;

        public IReadOnlyList<GattAttributeInfo> Services() =>
            _services.Select(static s => new GattAttributeInfo(s.Uuid.ToString("D").ToUpperInvariant(), [])).ToList();

        public IReadOnlyList<GattAttributeInfo> Characteristics(int service) =>
            _characteristics[service].Select(static c => new GattAttributeInfo(c.Uuid.ToString("D").ToUpperInvariant(), AttributesOf(c))).ToList();

        public IReadOnlyList<GattAttributeInfo> Descriptors(int service, int characteristic) =>
            DescriptorsOf(service, characteristic).Select(static d => new GattAttributeInfo(d.Uuid.ToString("D").ToUpperInvariant(), ["Read", "Write"])).ToList();

        private List<GattDescriptor> DescriptorsOf(int service, int characteristic)
        {
            if (!_descriptors.TryGetValue((service, characteristic), out List<GattDescriptor>? found))
            {
                GattDescriptorsResult result = _characteristics[service][characteristic].GetDescriptorsAsync(BluetoothCacheMode.Uncached).AsTask().GetAwaiter().GetResult();
                Check(result.Status);
                found = result.Descriptors.ToList();
                _descriptors[(service, characteristic)] = found;
            }

            return found;
        }

        /// <summary>R2025b's attribute words, in the order its tables list them.</summary>
        private static string[] AttributesOf(GattCharacteristic characteristic)
        {
            GattCharacteristicProperties p = characteristic.CharacteristicProperties;
            bool encrypted = characteristic.ProtectionLevel is GattProtectionLevel.EncryptionRequired or GattProtectionLevel.EncryptionAndAuthenticationRequired;
            var words = new List<string>();
            if (p.HasFlag(GattCharacteristicProperties.Broadcast))
            {
                words.Add("Broadcast");
            }

            if (p.HasFlag(GattCharacteristicProperties.Read))
            {
                words.Add(encrypted ? "ReadEncryptionRequired" : "Read");
            }

            if (p.HasFlag(GattCharacteristicProperties.WriteWithoutResponse))
            {
                words.Add("WriteWithoutResponse");
            }

            if (p.HasFlag(GattCharacteristicProperties.Write))
            {
                words.Add(encrypted ? "WriteEncryptionRequired" : "Write");
            }

            if (p.HasFlag(GattCharacteristicProperties.Notify))
            {
                words.Add(encrypted ? "NotifyEncryptionRequired" : "Notify");
            }

            if (p.HasFlag(GattCharacteristicProperties.Indicate))
            {
                words.Add(encrypted ? "IndicateEncryptionRequired" : "Indicate");
            }

            if (p.HasFlag(GattCharacteristicProperties.AuthenticatedSignedWrites))
            {
                words.Add("AuthenticatedSignedWrites");
            }

            if (p.HasFlag(GattCharacteristicProperties.ExtendedProperties))
            {
                words.Add("ExtendedProperties");
            }

            if (p.HasFlag(GattCharacteristicProperties.ReliableWrites))
            {
                words.Add("ReliableWrites");
            }

            if (p.HasFlag(GattCharacteristicProperties.WritableAuxiliaries))
            {
                words.Add("WritableAuxiliaries");
            }

            return words.ToArray();
        }

        public byte[] Read(int service, int characteristic)
        {
            GattReadResult result = _characteristics[service][characteristic].ReadValueAsync(BluetoothCacheMode.Uncached).AsTask().GetAwaiter().GetResult();
            Check(result.Status, result.ProtocolError);
            return result.Value?.ToArray() ?? [];
        }

        public void Write(int service, int characteristic, byte[] data, bool withResponse)
        {
            GattWriteResult result = _characteristics[service][characteristic]
                .WriteValueWithResultAsync(data.AsBuffer(), withResponse ? GattWriteOption.WriteWithResponse : GattWriteOption.WriteWithoutResponse)
                .AsTask().GetAwaiter().GetResult();
            Check(result.Status, result.ProtocolError);
        }

        public byte[] ReadDescriptor(int service, int characteristic, int descriptor)
        {
            GattReadResult result = DescriptorsOf(service, characteristic)[descriptor].ReadValueAsync(BluetoothCacheMode.Uncached).AsTask().GetAwaiter().GetResult();
            Check(result.Status, result.ProtocolError);
            return result.Value?.ToArray() ?? [];
        }

        public void WriteDescriptor(int service, int characteristic, int descriptor, byte[] data)
        {
            GattWriteResult result = DescriptorsOf(service, characteristic)[descriptor].WriteValueWithResultAsync(data.AsBuffer()).AsTask().GetAwaiter().GetResult();
            Check(result.Status, result.ProtocolError);
        }

        public GattSubscription SubscriptionOf(int service, int characteristic)
        {
            GattReadClientCharacteristicConfigurationDescriptorResult result = _characteristics[service][characteristic]
                .ReadClientCharacteristicConfigurationDescriptorAsync().AsTask().GetAwaiter().GetResult();
            if (result.Status != GattCommunicationStatus.Success)
            {
                return GattSubscription.Unknown;
            }

            return result.ClientCharacteristicConfigurationDescriptor switch
            {
                GattClientCharacteristicConfigurationDescriptorValue.Notify => GattSubscription.Notify,
                GattClientCharacteristicConfigurationDescriptorValue.Indicate => GattSubscription.Indicate,
                _ => GattSubscription.None,
            };
        }

        public void Subscribe(int service, int characteristic, GattSubscription kind, Action<byte[], DateTime>? onValue)
        {
            GattCharacteristic target = _characteristics[service][characteristic];
            lock (_subscriptions)
            {
                if (_subscriptions.Remove((service, characteristic), out TypedEventHandlerHolder? old))
                {
                    target.ValueChanged -= old.Handler;
                }

                if (onValue is not null)
                {
                    var holder = new TypedEventHandlerHolder((_, args) =>
                    {
                        try
                        {
                            onValue(args.CharacteristicValue.ToArray(), args.Timestamp.LocalDateTime);
                        }
                        catch (Exception)
                        {
                            // A notification handler runs on a WinRT thread and must not end the process.
                        }
                    });
                    target.ValueChanged += holder.Handler;
                    _subscriptions[(service, characteristic)] = holder;
                }
            }

            GattClientCharacteristicConfigurationDescriptorValue value = kind switch
            {
                GattSubscription.Notify => GattClientCharacteristicConfigurationDescriptorValue.Notify,
                GattSubscription.Indicate => GattClientCharacteristicConfigurationDescriptorValue.Indicate,
                _ => GattClientCharacteristicConfigurationDescriptorValue.None,
            };
            GattWriteResult result = target.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(value).AsTask().GetAwaiter().GetResult();
            Check(result.Status, result.ProtocolError);
        }

        private static void Check(GattCommunicationStatus status, byte? protocolError = null)
        {
            if (status != GattCommunicationStatus.Success)
            {
                throw Failure(status, protocolError);
            }
        }

        /// <summary>The failure, named by the words of R2025b's gattCommunication* identifiers.</summary>
        private static GattException Failure(GattCommunicationStatus status, byte? protocolError = null) => status switch
        {
            GattCommunicationStatus.Unreachable => new GattException("Unreachable", "The peripheral could not be reached."),
            GattCommunicationStatus.AccessDenied => new GattException("AccessDenied", "Access to the attribute was denied."),
            GattCommunicationStatus.ProtocolError => new GattException(protocolError switch
            {
                0x02 => "ProtocolErrorReadNotPermitted",
                0x03 => "ProtocolErrorWriteNotPermitted",
                0x05 => "ProtocolErrorInsufficientAuthentication",
                0x08 => "ProtocolErrorInsufficientAuthorization",
                0x0F => "ProtocolErrorInsufficientEncryption",
                _ => "ProtocolErrorUnknown",
            }, $"The peripheral answered with GATT protocol error {protocolError}."),
            _ => new GattException("Unknown", "The GATT operation failed."),
        };

        public void Dispose()
        {
            lock (_subscriptions)
            {
                _subscriptions.Clear();
            }

            foreach (GattDeviceService service in _services)
            {
                service.Dispose();
            }

            _device.Dispose();
        }

        private sealed class TypedEventHandlerHolder
        {
            public TypedEventHandlerHolder(Windows.Foundation.TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> handler) => Handler = handler;

            public Windows.Foundation.TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> Handler { get; }
        }
    }
}
