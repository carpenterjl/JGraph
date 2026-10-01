using System.Globalization;
using JGraph.Data;
using JGraph.Devices;
using JGraph.Devices.Bluetooth;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// <c>blelist</c> and <c>ble</c> (device classes plan, stage D3), transcribed from R2025b's
/// <c>blelist.m</c> and <c>ble.m</c> with blelib's p-coded parts as probe_bt_* and probe_ble_internals*
/// measured them: the scan's table (Index, Name, Address, RSSI, Advertisement, strongest first), the
/// devices a scan found kept for <c>ble</c> to match a name or an address against, a peripheral's
/// Services and Characteristics tables, and <c>characteristic</c>.
/// </summary>
internal sealed class BleObject : DeviceObject
{
    /// <summary>R2025b's blelib constants (probe_ble_internals).</summary>
    internal const double DefaultScanTimeout = 2;

    private static readonly DeviceClass Declaration = Declare();

    private readonly IBlePeripheral _peripheral;
    private readonly string _name;
    private readonly string _address;
    private readonly List<(int Service, string ServiceUuid, string ServiceName)> _services = new();
    private readonly List<(int Service, int Index, string Uuid, string Name, string[] Attributes)> _characteristics = new();
    private readonly List<BleCharacteristicObject> _children = new();

    private BleObject(DeviceSession session, Interpreter interpreter, IBlePeripheral peripheral, string name, string address)
        : base(session, interpreter)
    {
        _peripheral = peripheral;
        _name = name;
        _address = address;
    }

    public override DeviceClass Class => Declaration;

    public override string? Summary() => Deleted ? "deleted" : $"{_name} ({_address})";

    internal IBlePeripheral Peripheral => _peripheral;

    // --- blelist --------------------------------------------------------------------------------------

    private static readonly string[] ListParameters = ["name", "timeout", "services"];

    /// <summary><c>blelist</c>, <c>blelist(Name=, Timeout=, Services=)</c>.</summary>
    public static JgsValue List(DeviceSession session, Interpreter interpreter, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        JgsValue? nameArg = null;
        double timeout = DefaultScanTimeout;
        var services = new List<string>();
        for (int i = 0; i < args.Count; i += 2)
        {
            JgsValue key = TransportClient.Str2Char(args[i]);
            if (!DeviceChecks.IsText(key))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:ble:ble:invalidNameValue", "Invalid 'name' value specified. Value must be a string or character vector.");
            }

            string written = DeviceChecks.Text(key);
            if (!DeviceChecks.Match(written, ListParameters, out string? parameter, out _))
            {
                if (i + 1 >= args.Count)
                {
                    throw InvalidPair(line, col);
                }

                throw InvalidPair(line, col);
            }

            if (i + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMissingValue",
                    $"No value was given for '{parameter}'. Name-value pair arguments require a name followed by a value.");
            }

            JgsValue value = args[i + 1];
            switch (parameter)
            {
                case "name":
                    nameArg = value;
                    JgsValue text = TransportClient.Str2Char(value);
                    if (DeviceChecks.Count(value) == 0 || !DeviceChecks.IsText(text) || text.IsCharMatrix)
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:ble:ble:invalidNameValue", "Invalid 'name' value specified. Value must be a string or character vector.");
                    }

                    nameArg = text;
                    break;
                case "timeout":
                    string cls = DeviceChecks.ClassOf(value);
                    bool numeric = DeviceChecks.NumericClasses.Contains(cls) && DeviceChecks.Count(value) == 1;
                    double seconds = numeric ? DeviceChecks.Numbers(value).First() : double.NaN;
                    if (!(seconds > 0 && seconds < 10485))
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:ble:ble:invalidTimeoutValue", "Invalid 'timeout' value specified. Value must be a numeric between 0 and 10485.");
                    }

                    timeout = seconds;
                    break;
                default:
                    services.Clear();
                    services.AddRange(GattUuids.ServiceList(value, line, col));
                    break;
            }
        }

        IBluetoothBackend backend = BluetoothObject.Radio(line, col, forBle: true);
        IReadOnlyList<BleAdvertisementInfo> heard;
        try
        {
            heard = backend.Scan(TimeSpan.FromSeconds(timeout), services, interpreter.Cancellation);
        }
        catch (BluetoothRadioException)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:ble:ble:bluetoothOperationRadioNotAvailable", "Bluetooth radio is not available or turned off.");
        }
        catch (Exception e) when (e is not JgsException and not OperationCanceledException)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:ble:ble:failToScan", "Failed to detect nearby Bluetooth Low Energy peripheral devices. Contact Technical Support if issue persists.");
        }

        foreach (BleAdvertisementInfo advertisement in heard)
        {
            session.BleSeen[BluetoothAddress.Format(advertisement.Address)] = (advertisement.Name, !advertisement.Type.StartsWith("Non", StringComparison.Ordinal));
        }

        if (wanted == 0)
        {
            session.Host.print("Run <a href=\"matlab:bluetoothlist\">bluetoothlist</a> to search for nearby Bluetooth classic devices.");
        }

        List<BleAdvertisementInfo> rows = heard.ToList();
        if (rows.Count > 0 && nameArg is not null)
        {
            string prefix = DeviceChecks.Text(nameArg);
            rows = rows.Where(r => r.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (rows.Count == 0)
        {
            if (nameArg is null)
            {
                JgsBuiltins.Warn(session.Host, "MATLAB:ble:ble:noDeviceFound", "No nearby Bluetooth Low Energy peripheral devices detected. Try a larger 'timeout' value.");
            }
            else
            {
                JgsBuiltins.Warn(session.Host, "MATLAB:ble:ble:noDeviceWithNameFound",
                    "No nearby Bluetooth Low Energy peripheral devices with a name starting with this string detected. Try calling blelist with a larger 'timeout' value.");
            }

            return JgsEmpty.Zero();
        }

        int n = rows.Count;
        JgsValue Doubles(Func<int, double> pick) => JgsMatrix.FromColumnMajor(Enumerable.Range(0, n).Select(pick).ToArray(), n, 1);
        JgsValue Strings(Func<BleAdvertisementInfo, string> pick) => JgsValue.StringArray(rows.Select(r => JgsValue.Str(pick(r))).ToArray(), n, 1);
        JgsValue advertisements = JgsValue.StructArray(rows.Select(AdvertisementStruct).ToArray());
        advertisements.Reshape(n, 1);
        var table = new Table(
        [
            JgsBuiltins.TableColumnFrom("blelist", "Index", Doubles(static i => i + 1), line, col),
            JgsBuiltins.TableColumnFrom("blelist", "Name", Strings(static r => r.Name), line, col),
            JgsBuiltins.TableColumnFrom("blelist", "Address", Strings(static r => BluetoothAddress.Format(r.Address)), line, col),
            JgsBuiltins.TableColumnFrom("blelist", "RSSI", Doubles(i => rows[i].Rssi), line, col),
            JgsBuiltins.TableColumnFrom("blelist", "Advertisement", advertisements, line, col),
        ]);
        return JgsValue.Table(table);
    }

    private static JgsRuntimeException InvalidPair(int line, int col) =>
        new(line, col, "MATLAB:ble:ble:invalidNVPair", "Unrecognized parameter. Parameter name must be one of the following: name, services, timeout.");

    /// <summary>One Advertisement: R2025b's eleven fields, [] for what the peripheral did not say.</summary>
    private static Dictionary<string, JgsValue> AdvertisementStruct(BleAdvertisementInfo a)
    {
        static JgsValue Bytes(byte[]? data) => data is null ? JgsEmpty.Zero() : TransportClient.Row(data.Select(static b => (double)b).ToArray());
        static JgsValue Uuids(IReadOnlyList<string> list) => list.Count == 0 ? JgsEmpty.Zero() : JgsValue.StringArray(list.Select(JgsValue.Str).ToArray());
        return new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Type"] = JgsValue.StringScalar(a.Type),
            ["Appearance"] = a.Appearance is int appearance ? JgsValue.Number(appearance) : JgsEmpty.Zero(),
            ["ShortenedLocalName"] = a.ShortenedLocalName is { } shortName ? JgsValue.StringScalar(shortName) : JgsEmpty.Zero(),
            ["CompleteLocalName"] = a.CompleteLocalName is { } fullName ? JgsValue.StringScalar(fullName) : JgsEmpty.Zero(),
            ["TxPowerLevel"] = a.TxPowerLevel is int power ? JgsValue.Number(power) : JgsEmpty.Zero(),
            ["SlaveConnectionIntervalRange"] = a.SlaveConnectionIntervalRange is (double lo, double hi) ? TransportClient.Row([lo, hi]) : JgsEmpty.Zero(),
            ["ManufacturerSpecificData"] = Bytes(a.ManufacturerSpecificData),
            ["ServiceData"] = Bytes(a.ServiceData),
            ["CompleteServiceUUIDs"] = Uuids(a.CompleteServiceUuids),
            ["IncompleteServiceUUIDs"] = Uuids(a.IncompleteServiceUuids),
            ["ServiceSolicitationUUIDs"] = Uuids(a.ServiceSolicitationUuids),
        };
    }

    // --- ble --------------------------------------------------------------------------------------------

    private const string ConnectFailure = "Failed to connect to device. Make sure device is within proper range and not connected elsewhere. For more information, see Troubleshooting section in Documentation.";

    /// <summary><c>b = ble(address)</c>, <c>ble(name)</c>.</summary>
    public static JgsValue Create(DeviceSession session, Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:ble:ble:bleCalledWithoutInput",
                "Not enough input arguments. Specify device address or device name as a string or character vector.");
        }

        if (args.Count > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        JgsValue input = TransportClient.Str2Char(args[0]);
        if (DeviceChecks.Count(args[0]) == 0 || !DeviceChecks.IsText(input) || input.IsCharMatrix || DeviceChecks.Text(input).Length == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:ble:ble:invalidIdentifier",
                "Invalid device identifier. Device name or address must be specified as a string or character vector.");
        }

        string written = DeviceChecks.Text(input);
        if (System.Text.RegularExpressions.Regex.Match(written, "([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}") is { Success: true } m && m.Value == written)
        {
            written = written.Replace(":", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        }

        IBluetoothBackend backend = BluetoothObject.Radio(line, col, forBle: true);
        (bool found, string address, string name) = Resolve(session, written, line, col);
        if (!found)
        {
            try
            {
                foreach (BleAdvertisementInfo advertisement in backend.Scan(TimeSpan.FromSeconds(DefaultScanTimeout), [], interpreter.Cancellation))
                {
                    session.BleSeen[BluetoothAddress.Format(advertisement.Address)] = (advertisement.Name, !advertisement.Type.StartsWith("Non", StringComparison.Ordinal));
                }
            }
            catch (Exception e) when (e is not JgsException and not OperationCanceledException)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:ble:ble:failToScan", "Failed to detect nearby Bluetooth Low Energy peripheral devices. Contact Technical Support if issue persists.");
            }

            (found, address, name) = Resolve(session, written, line, col);
            if (!found)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:ble:ble:undiscoveredDevice",
                    "Unable to find device with specified name or address. Check device status and run blelist to find device.");
            }
        }

        if (session.Live.OfType<BleObject>().Any(b => !b.Deleted && b._address == address))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:ble:ble:connectionExists", "Connection to this device already exists in the workspace.");
        }

        IBlePeripheral peripheral;
        try
        {
            BluetoothAddress.TryParse(address, out ulong numeric);
            peripheral = backend.ConnectBle(numeric, interpreter.Cancellation);
        }
        catch (Exception e) when (e is not JgsException and not OperationCanceledException)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:ble:ble:failToConnect", ConnectFailure);
        }

        var made = new BleObject(session, interpreter, peripheral, name, address);
        try
        {
            made.Discover();
        }
        catch (Exception e) when (e is not JgsException and not OperationCanceledException)
        {
            peripheral.Dispose();
            throw new JgsRuntimeException(line, col, "MATLAB:ble:ble:failToConnect", ConnectFailure);
        }

        session.Remember(made);
        JgsValue value = JgsValue.External(made);
        JgsLifetime.Minted(value);
        return value;
    }

    /// <summary>ble.validateAddress over what scans have found: an address by validatestring, then a unique name.</summary>
    private static (bool Found, string Address, string Name) Resolve(DeviceSession session, string input, int line, int col)
    {
        if (session.BleSeen.Count == 0)
        {
            return (false, "", "");
        }

        string[] addresses = session.BleSeen.Keys.ToArray();
        bool ambiguous;
        if (DeviceChecks.Match(input, addresses, out string? address, out ambiguous))
        {
            return Connectable(session, address!, line, col);
        }

        if (ambiguous)
        {
            DeviceChecks.ValidateString(JgsValue.Str(input), addresses, new DeviceChecks.Subject(null, null), line, col);
        }

        string[] names = session.BleSeen.Values.Select(static v => v.Name).Where(static n => n.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        if (!DeviceChecks.Match(input, names, out string? name, out ambiguous))
        {
            if (ambiguous)
            {
                DeviceChecks.ValidateString(JgsValue.Str(input), names, new DeviceChecks.Subject(null, null), line, col);
            }

            return (false, "", "");
        }

        string[] matches = session.BleSeen.Where(kv => kv.Value.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(static kv => kv.Key).ToArray();
        if (matches.Length > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:ble:ble:ambiguousDeviceName",
                $"Multiple devices found with device name {name}. Specify one of the following addresses to connect: {string.Join(',', matches)}.");
        }

        return Connectable(session, matches[0], line, col);
    }

    private static (bool, string, string) Connectable(DeviceSession session, string address, int line, int col)
    {
        (string name, bool connectable) = session.BleSeen[address];
        if (!connectable)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:ble:ble:unconnectableDevice",
                "This device does not support connections or it is currently not connectable. If device is connected elsewhere, disconnect from it and run blelist first.");
        }

        return (true, address, name);
    }

    /// <summary>The Services and Characteristics tables, as ble's constructor builds them.</summary>
    private void Discover()
    {
        IReadOnlyList<GattAttributeInfo> services = _peripheral.Services();
        for (int s = 0; s < services.Count; s++)
        {
            string serviceUuid = GattNames.Shortest(services[s].Uuid);
            string serviceName = GattNames.ServiceName(serviceUuid);
            _services.Add((s, serviceUuid, serviceName));
            IReadOnlyList<GattAttributeInfo> characteristics;
            try
            {
                characteristics = _peripheral.Characteristics(s);
            }
            catch (GattException e) when (e.Kind == "AccessDenied")
            {
                JgsBuiltins.Warn(Session.Host, "MATLAB:ble:ble:serviceAccessDenied", $"Access to {serviceName} service with UUID {serviceUuid} was denied.");
                continue;
            }

            for (int c = 0; c < characteristics.Count; c++)
            {
                string uuid = GattNames.Shortest(characteristics[c].Uuid);
                _characteristics.Add((s, c, uuid, GattNames.CharacteristicName(serviceUuid, uuid), characteristics[c].Attributes.ToArray()));
            }
        }
    }

    /// <summary>The Services table, or [] for a peripheral that has none.</summary>
    private JgsValue ServicesTable()
    {
        if (_services.Count == 0)
        {
            return JgsEmpty.Zero();
        }

        int n = _services.Count;
        return JgsValue.Table(new Table(
        [
            JgsBuiltins.TableColumnFrom("ble", "ServiceName", JgsValue.StringArray(_services.Select(static s => JgsValue.Str(s.ServiceName)).ToArray(), n, 1), 0, 0),
            JgsBuiltins.TableColumnFrom("ble", "ServiceUUID", JgsValue.StringArray(_services.Select(static s => JgsValue.Str(s.ServiceUuid)).ToArray(), n, 1), 0, 0),
        ]));
    }

    /// <summary>The Characteristics table, or [] for a peripheral that has no services.</summary>
    private JgsValue CharacteristicsTable()
    {
        if (_services.Count == 0)
        {
            return JgsEmpty.Zero();
        }

        int n = _characteristics.Count;
        JgsValue Strings(Func<(int Service, int Index, string Uuid, string Name, string[] Attributes), string> pick) =>
            JgsValue.StringArray(_characteristics.Select(c => JgsValue.Str(pick(c))).ToArray(), n, 1);
        return JgsValue.Table(new Table(
        [
            JgsBuiltins.TableColumnFrom("ble", "ServiceName", Strings(c => _services[c.Service].ServiceName), 0, 0),
            JgsBuiltins.TableColumnFrom("ble", "ServiceUUID", Strings(c => _services[c.Service].ServiceUuid), 0, 0),
            JgsBuiltins.TableColumnFrom("ble", "CharacteristicName", Strings(static c => c.Name), 0, 0),
            JgsBuiltins.TableColumnFrom("ble", "CharacteristicUUID", Strings(static c => c.Uuid), 0, 0),
            JgsBuiltins.TableColumnFrom("ble", "Attributes",
                BluetoothObject.CellColumn(_characteristics.Select(static c => JgsValue.StringArray(c.Attributes.Select(JgsValue.Str).ToArray())).ToArray()), 0, 0),
        ]));
    }

    /// <summary>The Connected property: the link's state, with R2025b's warning when it is down.</summary>
    internal bool IsConnected(bool warn)
    {
        bool connected = !Deleted && _peripheral.Connected;
        if (!connected && warn)
        {
            JgsBuiltins.Warn(Session.Host, "MATLAB:ble:ble:deviceDisconnected", "Device is disconnected.");
        }

        return connected;
    }

    /// <summary><c>c = characteristic(b, service, characteristic)</c>: the one object per characteristic.</summary>
    private JgsValue Characteristic(DeviceCall call)
    {
        if (call.Args.Count != 2)
        {
            throw call.Error(call.Args.Count < 2 ? "MATLAB:narginchk:notEnoughInputs" : "MATLAB:narginchk:tooManyInputs",
                call.Args.Count < 2 ? "Not enough input arguments." : "Too many input arguments.");
        }

        LiveOrThrow(call.Line, call.Column);
        string serviceUuid = GattUuids.Shortest(GattUuids.Service(call.Args[0], call.Line, call.Column));
        string[] serviceUuids = _services.Select(static s => s.ServiceUuid).ToArray();
        string chosenService = DeviceChecks.Match(serviceUuid, serviceUuids, out string? matchedService, out bool ambiguous)
            ? matchedService!
            : ambiguous
                ? DeviceChecks.ValidateString(JgsValue.Str(serviceUuid), serviceUuids, new DeviceChecks.Subject(null, null), call.Line, call.Column)
                : throw call.Error("MATLAB:ble:ble:unsupportedService", "Unsupported service. See ble object properties for a list of supported service UUIDs and names.");
        int service = Array.IndexOf(serviceUuids, chosenService);

        string uuid = GattUuids.Shortest(GattUuids.Characteristic(chosenService, call.Args[1], call.Line, call.Column));
        var under = _characteristics.Where(c => c.Service == service).ToList();
        string[] uuids = under.Select(static c => c.Uuid).ToArray();
        string chosen = DeviceChecks.Match(uuid, uuids, out string? matched, out ambiguous)
            ? matched!
            : ambiguous
                ? DeviceChecks.ValidateString(JgsValue.Str(uuid), uuids, new DeviceChecks.Subject(null, null), call.Line, call.Column)
                : throw call.Error("MATLAB:ble:ble:unsupportedCharacteristic", "Unsupported characteristic. See ble object properties for a list of supported characteristic UUIDs and names.");
        var entry = under[Array.IndexOf(uuids, chosen)];

        BleCharacteristicObject? existing = _children.FirstOrDefault(c => !c.Deleted && c.ServiceIndex == service && c.CharacteristicIndex == entry.Index);
        if (existing is not null)
        {
            return JgsValue.External(existing);
        }

        var made = new BleCharacteristicObject(this, service, entry.Index, entry.Name, entry.Uuid, entry.Attributes);
        _children.Add(made);
        JgsValue value = JgsValue.External(made);
        JgsLifetime.Minted(value);
        return value;
    }

    internal void ChildGone(BleCharacteristicObject child)
    {
        _children.Remove(child);
        if (Exact > 0 && --Exact == 0)
        {
            Delete();
        }
    }

    private static BleObject Me(DeviceObject o) => (BleObject)o;

    private static DeviceClass Declare()
    {
        var properties = new List<DeviceProperty>
        {
            new("Name", static (o, _) => JgsValue.StringScalar(Me(o)._name)),
            new("Address", static (o, _) => JgsValue.StringScalar(Me(o)._address)),
            new("Connected", static (o, _) => JgsValue.Bool(Me(o).IsConnected(warn: true))),
            new("Services", static (o, _) => Me(o).ServicesTable()),
            new("Characteristics", static (o, _) => Me(o).CharacteristicsTable()),
        };
        var methods = new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["characteristic"] = static c => [Me(c.Target).Characteristic(c)],
        };
        return new DeviceClass("ble", "ble", ["matlabshared.blelib.internal.Node", "handle", "matlab.mixin.CustomDisplay"],
            properties, methods,
            ["addlistener", "ble", "characteristic", "delete", "eq", "findobj", "findprop", "ge", "gt", "isvalid", "le", "listener", "lt", "ne", "notify"],
            ["Name", "Address", "Connected", "Services", "Characteristics"]);
    }

    protected override void OnDelete()
    {
        foreach (BleCharacteristicObject child in _children.ToArray())
        {
            child.ParentGone();
        }

        _peripheral.Dispose();
    }
}

/// <summary>
/// <c>matlabshared.blelib.Characteristic</c>: read, write, subscribe, unsubscribe, descriptor and
/// DataAvailableFcn, with R2025b's read and write interfaces chosen by the Attributes (ReadOnly,
/// NotifyOnly, ReadNotify, WriteCommon, or the Default that refuses), transcribed from their sources.
/// </summary>
internal sealed class BleCharacteristicObject : DeviceObject
{
    private static readonly DeviceClass Declaration = Declare();

    private readonly BleObject _parent;
    private readonly string _name;
    private readonly string _uuid;
    private readonly string[] _attributes;
    private readonly List<(byte[] Data, DateTime At)> _buffer = new();
    private readonly List<BleDescriptorObject> _children = new();
    private readonly DeviceEventQueue _queue;
    private List<(string Name, string Uuid, string[] Attributes)>? _descriptors;
    private JgsValue? _dataAvailableFcn;
    private GattSubscription? _defaultSubscription;
    private bool _subscriptionOn;
    private long _totalWritten;
    private long _providedInCallbacks;
    private bool _parentGone;

    public BleCharacteristicObject(BleObject parent, int service, int characteristic, string name, string uuid, string[] attributes)
        : base(parent.Session, parent.Interpreter)
    {
        _parent = parent;
        ServiceIndex = service;
        CharacteristicIndex = characteristic;
        _name = name;
        _uuid = uuid;
        _attributes = attributes;
        _queue = DeviceEventQueue.ForCurrentThread();
        parent.Exact++; // a characteristic keeps its peripheral, as its Node parent does in R2025b
        try
        {
            DiscoverDescriptors();
        }
        catch (Exception e) when (e is GattException or DeviceIOException)
        {
            throw new JgsRuntimeException(0, 0, e is GattException { Kind: "Disconnected" } ? "MATLAB:ble:ble:failToExecuteDeviceDisconnected" : "MATLAB:ble:ble:failToDiscoverDescriptors",
                e is GattException { Kind: "Disconnected" }
                    ? "Device is disconnected to the computer. Clear and recreate ble object."
                    : "Failed to discover descriptors. If device is disconnected, then clear existing ble object and recreate it.");
        }
    }

    public int ServiceIndex { get; }

    public int CharacteristicIndex { get; }

    public override DeviceClass Class => Declaration;

    public override string? Summary() => Deleted ? "deleted" : $"{_name} ({_uuid})";

    private bool CanRead => _attributes.Any(static a => a is "Read" or "ReadEncryptionRequired");

    private bool CanNotify => _attributes.Any(static a => a is "Notify" or "NotifyEncryptionRequired" or "Indicate" or "IndicateEncryptionRequired");

    private bool CanWrite => _attributes.Any(static a => a is "Write" or "WriteEncryptionRequired" or "WriteWithoutResponse" or "ReliableWrites" or "AuthenticatedSignedWrites");

    private void DiscoverDescriptors()
    {
        _descriptors = _parent.Peripheral.Descriptors(ServiceIndex, CharacteristicIndex)
            .Select(static d => (GattNames.DescriptorName(d.Uuid), GattNames.Shortest(d.Uuid), d.Attributes.ToArray()))
            .ToList();
    }

    private JgsValue DescriptorsTable()
    {
        if (_descriptors is not { Count: > 0 } list)
        {
            return JgsEmpty.Zero();
        }

        int n = list.Count;
        return JgsValue.Table(new Table(
        [
            JgsBuiltins.TableColumnFrom("ble", "DescriptorName", JgsValue.StringArray(list.Select(static d => JgsValue.Str(d.Name)).ToArray(), n, 1), 0, 0),
            JgsBuiltins.TableColumnFrom("ble", "DescriptorUUID", JgsValue.StringArray(list.Select(static d => JgsValue.Str(d.Uuid)).ToArray(), n, 1), 0, 0),
            JgsBuiltins.TableColumnFrom("ble", "Attributes",
                BluetoothObject.CellColumn(list.Select(static d => JgsValue.StringArray(d.Attributes.Select(JgsValue.Str).ToArray())).ToArray()), 0, 0),
        ]));
    }

    // --- executing on the peripheral ------------------------------------------------------------------

    /// <summary>Characteristic.execute: refuses while the link is down, and names a GATT failure as R2025b does.</summary>
    internal T Execute<T>(DeviceCall call, Func<IBlePeripheral, T> body, string otherwiseKey, string otherwiseText)
    {
        LiveOrThrow(call.Line, call.Column);
        if (_parentGone || !_parent.IsConnected(warn: false))
        {
            throw call.Error("MATLAB:ble:ble:failToExecuteDeviceDisconnected", "Device is disconnected to the computer. Clear and recreate ble object.");
        }

        try
        {
            return body(_parent.Peripheral);
        }
        catch (GattException e)
        {
            throw GattRefusal(call, e, otherwiseKey, otherwiseText);
        }
    }

    internal static JgsRuntimeException GattRefusal(DeviceCall call, GattException e, string otherwiseKey, string otherwiseText) => e.Kind switch
    {
        "Disconnected" => call.Error("MATLAB:ble:ble:failToExecuteDeviceDisconnected", "Device is disconnected to the computer. Clear and recreate ble object."),
        "Unreachable" => call.Error("MATLAB:ble:ble:gattCommunicationUnreachable", "Operation failed because device is unreachable. Check if device is within proper range."),
        "AccessDenied" => call.Error("MATLAB:ble:ble:gattCommunicationAccessDenied", "Operation failed because access was denied."),
        "ProtocolErrorReadNotPermitted" => call.Error("MATLAB:ble:ble:gattCommunicationProtocolErrorReadNotPermitted", "Operation failed because read is not permitted."),
        "ProtocolErrorWriteNotPermitted" => call.Error("MATLAB:ble:ble:gattCommunicationProtocolErrorWriteNotPermitted", "Operation failed because write is not permitted."),
        "ProtocolErrorInsufficientAuthentication" => call.Error("MATLAB:ble:ble:gattCommunicationProtocolErrorInsufficientAuthentication",
            "Operation failed because authentication was insufficient. Try pairing device and recreate ble object."),
        "ProtocolErrorInsufficientAuthorization" => call.Error("MATLAB:ble:ble:gattCommunicationProtocolErrorInsufficientAuthorization", "Operation failed because authorization was insufficient."),
        "ProtocolErrorInsufficientEncryption" => call.Error("MATLAB:ble:ble:gattCommunicationProtocolErrorInsufficientEncryption", "Operation failed because encryption was insufficient."),
        "ProtocolErrorUnknown" => call.Error("MATLAB:ble:ble:gattCommunicationProtocolErrorUnknown", "Operation failed because there was an unknown error."),
        _ => call.Error("MATLAB:ble:ble:" + otherwiseKey, otherwiseText),
    };

    // --- read -------------------------------------------------------------------------------------------

    private const string ReadFailure = "Failed to read characteristic. If device is disconnected, then clear existing ble object and recreate it.";

    /// <summary><c>[value, timestamp] = read(c)</c>, <c>read(c, "latest"|"oldest")</c>.</summary>
    private JgsValue[] Read(DeviceCall call)
    {
        if (call.Args.Count > 1)
        {
            throw call.Error("MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        if (!CanRead && !CanNotify)
        {
            throw Unsupported(call);
        }

        string mode = "latest";
        if (call.Args.Count == 1)
        {
            string[] modes = CanNotify ? ["oldest", "latest"] : ["latest"];
            JgsValue written = TransportClient.Str2Char(call.Args[0]);
            if (!DeviceChecks.IsText(written) || !DeviceChecks.Match(DeviceChecks.Text(written), modes, out string? chosen, out _))
            {
                throw CanNotify
                    ? call.Error("MATLAB:ble:ble:invalidModeNotifyOnly", "Invalid mode value. Value must be one of the following: oldest, latest.")
                    : call.Error("MATLAB:ble:ble:invalidModeReadOnly", "Invalid mode value. Value must be latest.");
            }

            mode = chosen!;
            if (CanRead && mode == "oldest" && !_subscriptionOn)
            {
                throw call.Error("MATLAB:ble:ble:invalidModeReadNotify", "Subscribe to notification or indication before reading oldest data.");
            }
        }

        if (CanRead && (!CanNotify || !_subscriptionOn))
        {
            byte[] data = Execute(call, p => p.Read(ServiceIndex, CharacteristicIndex), "failToReadCharacteristic", ReadFailure);
            return [Doubles(data), JgsBuiltins.DatetimeValue(DateTime.Now)];
        }

        if (!_subscriptionOn)
        {
            Subscribe(call, userCalled: false, null);
        }

        // NotifyOnly.read: waits up to ten seconds, pausing, so callbacks run meanwhile.
        long deadline = Environment.TickCount64 + 10_000;
        while (true)
        {
            lock (_buffer)
            {
                if (_buffer.Count > 0)
                {
                    break;
                }
            }

            if (Environment.TickCount64 >= deadline)
            {
                throw call.Error("MATLAB:ble:ble:noDataAvailable", "Device has not sent new data. If device is disconnected, then clear existing ble object and recreate it.");
            }

            call.Interpreter.Cancellation.ThrowIfCancellationRequested();
            DeviceEventQueue.DrainCurrent();
            WaitHandle.WaitAny([call.Interpreter.Cancellation.WaitHandle, DeviceEventQueue.Posted], 1);
        }

        (byte[] Data, DateTime At) taken;
        lock (_buffer)
        {
            if (mode == "latest")
            {
                taken = _buffer[^1];
                _buffer.Clear();
            }
            else
            {
                taken = _buffer[0];
                _buffer.RemoveAt(0);
            }
        }

        return [Doubles(taken.Data), JgsBuiltins.DatetimeValue(taken.At)];
    }

    private static JgsValue Doubles(byte[] data) =>
        data.Length == 0 ? JgsEmpty.Zero() : TransportClient.Row(data.Select(static b => (double)b).ToArray());

    private static JgsRuntimeException Unsupported(DeviceCall call) =>
        call.Error("MATLAB:ble:ble:unsupportedOperation", "Operation is not supported on this object.");

    // --- write ------------------------------------------------------------------------------------------

    private static readonly string[] WriteTypes = ["WithResponse", "WithoutResponse"];

    internal static readonly string[] WritePrecisions = ["uint8", "uint16", "uint32", "uint64"];

    /// <summary><c>write(c, data)</c>, <c>write(c, data, type)</c>, <c>write(c, data, precision)</c>, <c>write(c, data, type, precision)</c>.</summary>
    private void Write(DeviceCall call)
    {
        if (!CanWrite)
        {
            throw Unsupported(call);
        }

        if (call.Args.Count is < 1 or > 3)
        {
            throw call.Error(call.Args.Count < 1 ? "MATLAB:narginchk:notEnoughInputs" : "MATLAB:narginchk:tooManyInputs",
                call.Args.Count < 1 ? "Not enough input arguments." : "Too many input arguments.");
        }

        string? type = null;
        string precision = "uint8";
        bool typeGiven = false, precisionGiven = false;
        string[] words = [.. WriteTypes, .. WritePrecisions];
        foreach (JgsValue option in call.Args.Skip(1))
        {
            string chosen = DeviceChecks.ValidateString(TransportClient.Str2Char(option), words, new DeviceChecks.Subject(null, null), call.Line, call.Column);
            if (WriteTypes.Contains(chosen))
            {
                if (typeGiven)
                {
                    throw call.Error("MATLAB:ble:ble:duplicateWriteOptions", "Invalid input combination. Specify type, precision or both.");
                }

                type = chosen;
                typeGiven = true;
            }
            else
            {
                if (precisionGiven)
                {
                    throw call.Error("MATLAB:ble:ble:duplicateWriteOptions", "Invalid input combination. Specify type, precision or both.");
                }

                precision = chosen;
                precisionGiven = true;
            }
        }

        bool withResponse;
        if (type is null)
        {
            withResponse = _attributes.Any(static a => a is "Write" or "WriteEncryptionRequired" or "ReliableWrites");
        }
        else
        {
            bool supported = type == "WithResponse"
                ? _attributes.Any(static a => a is "Write" or "WriteEncryptionRequired" or "ReliableWrites")
                : _attributes.Any(static a => a is "WriteWithoutResponse" or "AuthenticatedSignedWrites");
            if (!supported)
            {
                throw call.Error("MATLAB:ble:ble:unsupportedWriteType", "This characteristic does not support this type of write. See characteristic object Attributes for supported types.");
            }

            withResponse = type == "WithResponse";
        }

        byte[] data = BleData.Bytes(call.Args[0], precision, call);
        Execute(call, p =>
        {
            p.Write(ServiceIndex, CharacteristicIndex, data, withResponse);
            return 0;
        }, "failToWriteCharacteristic", "Failed to write characteristic. If device is disconnected, then clear existing ble object and recreate it.");
    }

    // --- subscriptions ------------------------------------------------------------------------------------

    /// <summary><c>subscribe(c)</c>, <c>subscribe(c, "notification"|"indication")</c>.</summary>
    private void SubscribeMethod(DeviceCall call)
    {
        if (!CanNotify)
        {
            throw Unsupported(call);
        }

        if (call.Args.Count > 1)
        {
            throw call.Error("MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        Subscribe(call, userCalled: true, call.Args.Count == 1 ? call.Args[0] : null);
    }

    private void Subscribe(DeviceCall call, bool userCalled, JgsValue? typeArg)
    {
        bool notifies = _attributes.Any(static a => a is "Notify" or "NotifyEncryptionRequired");
        bool indicates = _attributes.Any(static a => a is "Indicate" or "IndicateEncryptionRequired");
        GattSubscription kind = notifies ? GattSubscription.Notify : GattSubscription.Indicate;
        if (userCalled && typeArg is not null)
        {
            var supported = new List<string>();
            if (notifies)
            {
                supported.Add("notification");
            }

            if (indicates)
            {
                supported.Add("indication");
            }

            JgsValue written = TransportClient.Str2Char(typeArg);
            if (!DeviceChecks.IsText(written) || !DeviceChecks.Match(DeviceChecks.Text(written), supported.ToArray(), out string? chosen, out _))
            {
                throw call.Error("MATLAB:ble:ble:invalidSubscriptionType", $"Invalid subscription type value. Value must be one of the following: {string.Join(", ", supported)}.");
            }

            kind = chosen == "notification" ? GattSubscription.Notify : GattSubscription.Indicate;
        }

        GattSubscription before = Execute(call, p =>
        {
            GattSubscription status = p.SubscriptionOf(ServiceIndex, CharacteristicIndex);
            p.Subscribe(ServiceIndex, CharacteristicIndex, kind, OnValue);
            return status;
        }, "failToSubscribeCharacteristic", "Failed to subscribe to characteristic. If device is disconnected, then clear existing ble object and recreate it.");
        _defaultSubscription ??= before;
        _subscriptionOn = true;
    }

    /// <summary><c>unsubscribe(c)</c>.</summary>
    private void Unsubscribe(DeviceCall call)
    {
        if (!CanNotify)
        {
            throw Unsupported(call);
        }

        if (call.Args.Count > 0)
        {
            throw call.Error("MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        if (_defaultSubscription is null)
        {
            return;
        }

        Execute(call, p =>
        {
            p.Subscribe(ServiceIndex, CharacteristicIndex, GattSubscription.None, null);
            return 0;
        }, "failToUnsubscribeCharacteristic", "Failed to unsubscribe to characteristic. If device is disconnected, then clear existing ble object and recreate it.");
        _subscriptionOn = false;
        lock (_buffer)
        {
            _buffer.Clear();
        }
    }

    /// <summary>A value the peripheral sent: buffered, and the callback scheduled where R2025b's would run.</summary>
    private void OnValue(byte[] data, DateTime at)
    {
        lock (_buffer)
        {
            _buffer.Add((data, at));
            _totalWritten++;
        }

        _queue.Post(HandleData);
    }

    /// <summary>Characteristic.handleData: one call per value not yet handed to a callback.</summary>
    private void HandleData()
    {
        if (Deleted || _dataAvailableFcn is not { } callback || !_subscriptionOn)
        {
            return;
        }

        long pending;
        lock (_buffer)
        {
            pending = Math.Min(_totalWritten - _providedInCallbacks, _buffer.Count);
            if (pending <= 0)
            {
                _providedInCallbacks = _totalWritten - _buffer.Count;
                pending = 0;
            }
        }

        for (long i = 0; i < pending; i++)
        {
            if (_dataAvailableFcn is null || !_subscriptionOn || Deleted)
            {
                break;
            }

            var eventData = new TransportClient.SharedEventInfo(Session, Interpreter, "matlabshared.blelib.internal.DataAvailableEventData", "DataAvailableEventData",
                [("Source", JgsEmpty.Zero()), ("EventName", JgsValue.Str(""))]);
            try
            {
                JgsCallbacks.Invoke(callback.AsCallable, [JgsValue.External(this), JgsValue.External(eventData)], 0, 0);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (JgsException failure)
            {
                string where = failure is JgsRuntimeException { Line: > 0 } runtime ? runtime.Line.ToString(CultureInfo.InvariantCulture) : "1";
                Session.Host.WriteErr($"Error in {callback.AsCallable.Name}(line {where})\n{failure.Message}\n\n");
            }

            lock (_buffer)
            {
                _providedInCallbacks++;
            }
        }
    }

    /// <summary>DataAvailableFcn's setter: [] clears it; otherwise a two-input handle, and a subscription if none.</summary>
    private void SetDataAvailableFcn(JgsValue value, DeviceCall call)
    {
        if (!CanNotify)
        {
            throw call.Error("MATLAB:ble:ble:noDataAvailableFcnAccess", "DataAvailableFcn is not supported for this characteristic.");
        }

        if (value.Type != JgsType.Function && DeviceChecks.Count(value) == 0 && DeviceChecks.ClassOf(value) is not ("char" or "cell"))
        {
            _dataAvailableFcn = null;
            return;
        }

        if (value.Type != JgsType.Function || value.AsCallable is AnonymousFunction { Declaration.Parameters.Count: not 2 })
        {
            throw call.Error("MATLAB:ble:ble:invalidDataAvailableFcn", "Invalid value for DataAvailableFcn. Specify [] or a function handle that accepts two inputs.");
        }

        JgsValue? original = _dataAvailableFcn;
        try
        {
            _dataAvailableFcn = value;
            if (!_subscriptionOn)
            {
                Subscribe(call, userCalled: false, null);
            }
        }
        catch
        {
            _dataAvailableFcn = original;
            throw;
        }
    }

    // --- descriptors --------------------------------------------------------------------------------------

    /// <summary><c>d = descriptor(c, uuid|name)</c>: the one object per descriptor.</summary>
    private JgsValue Descriptor(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        if (_descriptors is not { Count: > 0 } list)
        {
            throw call.Error("MATLAB:ble:ble:noDescriptors", "No descriptors found for this characteristic.");
        }

        if (call.Args.Count != 1)
        {
            throw call.Error(call.Args.Count < 1 ? "MATLAB:narginchk:notEnoughInputs" : "MATLAB:narginchk:tooManyInputs",
                call.Args.Count < 1 ? "Not enough input arguments." : "Too many input arguments.");
        }

        string uuid = GattUuids.Shortest(GattUuids.Descriptor(call.Args[0], call.Line, call.Column));
        string[] uuids = list.Select(static d => d.Uuid).ToArray();
        string chosen = DeviceChecks.Match(uuid, uuids, out string? matched, out bool ambiguous)
            ? matched!
            : ambiguous
                ? DeviceChecks.ValidateString(JgsValue.Str(uuid), uuids, new DeviceChecks.Subject(null, null), call.Line, call.Column)
                : throw call.Error("MATLAB:ble:ble:unsupportedDescriptor", "Unsupported descriptor. See characteristic object properties for a list of supported descriptor UUIDs and names.");
        int index = Array.IndexOf(uuids, chosen);
        BleDescriptorObject? existing = _children.FirstOrDefault(d => !d.Deleted && d.Index == index);
        if (existing is not null)
        {
            return JgsValue.External(existing);
        }

        var made = new BleDescriptorObject(this, index, list[index].Name, list[index].Uuid, list[index].Attributes);
        _children.Add(made);
        JgsValue value = JgsValue.External(made);
        JgsLifetime.Minted(value);
        return value;
    }

    /// <summary>Descriptor/write's updateSubscription: a written Client Characteristic Configuration moves SubscriptionOn.</summary>
    internal void SubscriptionWritten(GattSubscription status) =>
        _subscriptionOn = status is GattSubscription.Notify or GattSubscription.Indicate;

    internal void ChildGone(BleDescriptorObject child)
    {
        _children.Remove(child);
        if (Exact > 0 && --Exact == 0)
        {
            Delete();
        }
    }

    internal void ParentGone() => _parentGone = true;

    private static BleCharacteristicObject Me(DeviceObject o) => (BleCharacteristicObject)o;

    private static DeviceClass Declare()
    {
        var properties = new List<DeviceProperty>
        {
            new("Name", static (o, _) => JgsValue.StringScalar(Me(o)._name)),
            new("UUID", static (o, _) => JgsValue.StringScalar(Me(o)._uuid)),
            new("Attributes", static (o, _) => JgsValue.StringArray(Me(o)._attributes.Select(JgsValue.Str).ToArray())),
            new("Descriptors", static (o, _) => Me(o).DescriptorsTable()),
            new("DataAvailableFcn", static (o, c) => Me(o).CanNotify
                    ? Me(o)._dataAvailableFcn ?? JgsEmpty.Zero()
                    : throw c.Error("MATLAB:ble:ble:noDataAvailableFcnAccess", "DataAvailableFcn is not supported for this characteristic."),
                static (o, v, c) => Me(o).SetDataAvailableFcn(v, c)),
        };
        var methods = new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["read"] = static c => Me(c.Target).Read(c),
            ["write"] = static c => NetworkShared.Void(c, static c => Me(c.Target).Write(c)),
            ["subscribe"] = static c => NetworkShared.Void(c, static c => Me(c.Target).SubscribeMethod(c)),
            ["unsubscribe"] = static c => NetworkShared.Void(c, static c => Me(c.Target).Unsubscribe(c)),
            ["descriptor"] = static c => [Me(c.Target).Descriptor(c)],
        };
        return new DeviceClass("matlabshared.blelib.Characteristic", "Characteristic",
            ["matlabshared.blelib.internal.Node", "handle", "matlab.mixin.CustomDisplay"], properties, methods,
            ["Characteristic", "addlistener", "delete", "descriptor", "eq", "findobj", "findprop", "ge", "gt", "isvalid", "le", "listener", "lt",
             "ne", "notify", "read", "subscribe", "unsubscribe", "write"],
            ["Name", "UUID", "Attributes", "Descriptors", "DataAvailableFcn"]);
    }

    protected override void OnDelete()
    {
        // Characteristic.delete: resetSubscription puts back the subscription the peripheral had.
        if (_defaultSubscription is { } original && !_parentGone && _parent.IsConnected(warn: false))
        {
            try
            {
                _parent.Peripheral.Subscribe(ServiceIndex, CharacteristicIndex,
                    original is GattSubscription.Notify or GattSubscription.Indicate ? original : GattSubscription.None, null);
            }
            catch (Exception e) when (e is GattException or DeviceIOException)
            {
            }
        }

        foreach (BleDescriptorObject child in _children.ToArray())
        {
            child.ParentGone();
        }

        _parent.ChildGone(this);
    }
}

/// <summary><c>matlabshared.blelib.Descriptor</c>: read and write, and a written CCCD's effect on its characteristic.</summary>
internal sealed class BleDescriptorObject : DeviceObject
{
    private static readonly DeviceClass Declaration = Declare();

    private readonly BleCharacteristicObject _parent;
    private readonly string _name;
    private readonly string _uuid;
    private readonly string[] _attributes;
    private bool _parentGone;

    public BleDescriptorObject(BleCharacteristicObject parent, int index, string name, string uuid, string[] attributes)
        : base(parent.Session, parent.Interpreter)
    {
        _parent = parent;
        Index = index;
        _name = name;
        _uuid = uuid;
        _attributes = attributes;
        parent.Exact++;
    }

    public int Index { get; }

    public override string? Summary() => Deleted ? "deleted" : $"{_name} ({_uuid})";

    public override DeviceClass Class => Declaration;

    private JgsValue Read(DeviceCall call)
    {
        if (!_attributes.Contains("Read"))
        {
            throw call.Error("MATLAB:ble:ble:unsupportedOperation", "Operation is not supported on this object.");
        }

        if (call.Args.Count > 0)
        {
            throw call.Error("MATLAB:TooManyInputs", "Too many input arguments.");
        }

        byte[] data = _parent.Execute(call, p => p.ReadDescriptor(_parent.ServiceIndex, _parent.CharacteristicIndex, Index),
            "failToReadDescriptor", "Failed to read descriptor. If device is disconnected, then clear existing ble object and recreate it.");
        return data.Length == 0 ? JgsEmpty.Zero() : TransportClient.Row(data.Select(static b => (double)b).ToArray());
    }

    private void Write(DeviceCall call)
    {
        if (!_attributes.Contains("Write"))
        {
            throw call.Error("MATLAB:ble:ble:unsupportedOperation", "Operation is not supported on this object.");
        }

        if (call.Args.Count is < 1 or > 2)
        {
            throw call.Error(call.Args.Count < 1 ? "MATLAB:narginchk:notEnoughInputs" : "MATLAB:narginchk:tooManyInputs",
                call.Args.Count < 1 ? "Not enough input arguments." : "Too many input arguments.");
        }

        string precision = call.Args.Count == 2
            ? DeviceChecks.ValidateString(TransportClient.Str2Char(call.Args[1]), BleCharacteristicObject.WritePrecisions, new DeviceChecks.Subject(null, null), call.Line, call.Column)
            : "uint8";
        byte[] data = BleData.Bytes(call.Args[0], precision, call);
        GattSubscription status = _parent.Execute(call, p =>
        {
            p.WriteDescriptor(_parent.ServiceIndex, _parent.CharacteristicIndex, Index, data);
            return _uuid == "2902" ? p.SubscriptionOf(_parent.ServiceIndex, _parent.CharacteristicIndex) : GattSubscription.Unknown;
        }, "failToWriteDescriptor", "Failed to write descriptor. If device is disconnected, then clear existing ble object and recreate it.");
        if (_uuid == "2902")
        {
            _parent.SubscriptionWritten(status);
        }
    }

    internal void ParentGone() => _parentGone = true;

    private static BleDescriptorObject Me(DeviceObject o) => (BleDescriptorObject)o;

    private static DeviceClass Declare()
    {
        var properties = new List<DeviceProperty>
        {
            new("Name", static (o, _) => JgsValue.StringScalar(Me(o)._name)),
            new("UUID", static (o, _) => JgsValue.StringScalar(Me(o)._uuid)),
            new("Attributes", static (o, _) => JgsValue.StringArray(Me(o)._attributes.Select(JgsValue.Str).ToArray())),
        };
        var methods = new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["read"] = static c => [Me(c.Target).Read(c)],
            ["write"] = static c => NetworkShared.Void(c, static c => Me(c.Target).Write(c)),
        };
        return new DeviceClass("matlabshared.blelib.Descriptor", "Descriptor",
            ["matlabshared.blelib.internal.Node", "handle", "matlab.mixin.CustomDisplay"], properties, methods,
            ["Descriptor", "addlistener", "delete", "eq", "findobj", "findprop", "ge", "gt", "isvalid", "le", "listener", "lt", "ne", "notify", "read", "write"],
            ["Name", "UUID", "Attributes"]);
    }

    protected override void OnDelete()
    {
        if (!_parentGone)
        {
            _parent.ChildGone(this);
        }
    }
}

/// <summary>blelib's validateDataRange (probe_ble_internals, probe_ble_internals2): what a write may send.</summary>
internal static class BleData
{
    public static byte[] Bytes(JgsValue data, string precision, DeviceCall call)
    {
        int size = precision switch { "uint16" => 2, "uint32" => 4, "uint64" => 8, _ => 1 };
        ulong max = precision switch { "uint16" => ushort.MaxValue, "uint32" => uint.MaxValue, "uint64" => ulong.MaxValue, _ => byte.MaxValue };
        string cls = DeviceChecks.ClassOf(data);
        JgsRuntimeException InvalidType() => call.Error("MATLAB:ble:ble:invalidDataType", "Invalid data type. Data must be a scalar or an array of integers.");

        if (DeviceChecks.Count(data) == 0 && cls is not "cell")
        {
            return [];
        }

        // R2025b's validateDataRange fails on a matrix inside an && (MATLAB:nonLogicalConditional);
        // JGraph refuses it as the data it is not (div=ADR0186).
        if (data.Rows > 1 && data.Cols > 1)
        {
            throw InvalidType();
        }

        var values = new List<ulong>();
        if (cls == "char" || (cls == "string" && DeviceChecks.Count(data) == 1))
        {
            foreach (char ch in DeviceChecks.Text(data))
            {
                values.Add(size == 1 ? Math.Min(ch, (ulong)255) : ch);
            }
        }
        else if (cls == precision)
        {
            values.AddRange(DeviceChecks.Numbers(data).Select(static v => (ulong)v));
        }
        else if (cls is "double" or "single")
        {
            if (data.Type == JgsType.Complex || data.IsPackedComplex)
            {
                throw InvalidType();
            }

            foreach (double v in DeviceChecks.Numbers(data))
            {
                if (!double.IsFinite(v) || v != Math.Floor(v))
                {
                    throw InvalidType();
                }

                if (v < 0 || v > max)
                {
                    throw call.Error("MATLAB:ble:ble:invalidDataRanged", $"Data must be between 0 and {max.ToString(CultureInfo.InvariantCulture)}.");
                }

                values.Add((ulong)v);
            }
        }
        else
        {
            throw InvalidType();
        }

        var bytes = new byte[values.Count * size];
        for (int i = 0; i < values.Count; i++)
        {
            for (int b = 0; b < size; b++)
            {
                bytes[i * size + b] = (byte)(values[i] >> (8 * b));
            }
        }

        return bytes;
    }
}

/// <summary>blelib's UUID resolver (probe_ble_internals*): a UUID in any of its forms, or a name the tables know.</summary>
internal static class GattUuids
{
    private const string BadType = "Invalid data type for UUID. Service value must be a string or hexadecimal.";

    public static string Shortest(string canonical) => GattNames.Shortest(canonical);

    /// <summary>getServiceUUID: the canonical UUID of a service written as a UUID, a 16-bit number or a name.</summary>
    public static string Service(JgsValue value, int line, int col) =>
        Resolve(value, GattNames.Services.Select(static s => (s.Name, s.Uuid)).ToArray(), "MATLAB:ble:ble:invalidServiceUUIDValue",
            "Invalid value for service. See <a href=\"https://www.bluetooth.com/specifications/gatt/services\">Bluetooth SIG webpage</a> for a list of valid services.", line, col);

    /// <summary>getCharacteristicUUID: names are those the tables put under <paramref name="serviceUuid"/>.</summary>
    public static string Characteristic(string serviceUuid, JgsValue value, int line, int col)
    {
        string service = GattNames.Shortest(GattNames.Canonical(serviceUuid));
        (string, string)[] names = GattNames.Services.FirstOrDefault(s => s.Uuid.Equals(service, StringComparison.OrdinalIgnoreCase))?
            .Characteristics.Select(static c => (c.Name, c.Uuid)).ToArray() ?? [];
        return Resolve(value, names, "MATLAB:ble:ble:invalidCharacteristicUUIDValue",
            "Invalid value for characteristic. See ble object for list of valid characteristic values.", line, col);
    }

    /// <summary>getDescriptorUUID.</summary>
    public static string Descriptor(JgsValue value, int line, int col) =>
        Resolve(value, GattNames.Descriptors.Select(static d => (d.Name, d.Uuid)).ToArray(), "MATLAB:ble:ble:invalidDescriptorUUIDValue",
            "Invalid value for descriptor. See characteristic object for list of valid descriptor values.", line, col);

    /// <summary>blelist's Services: one value, a numeric vector of 16-bit UUIDs, or a text array; unique canonical UUIDs.</summary>
    public static IReadOnlyList<string> ServiceList(JgsValue value, int line, int col)
    {
        if (DeviceChecks.Count(value) == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:ble:ble:invalidServiceUUIDValue",
                "Invalid value for service. See <a href=\"https://www.bluetooth.com/specifications/gatt/services\">Bluetooth SIG webpage</a> for a list of valid services.");
        }

        IEnumerable<JgsValue> items = value.Type == JgsType.Cell ? value.AsCell
            : DeviceChecks.ClassOf(value) == "char" ? [value]
            : DeviceChecks.Count(value) > 1 ? value.BoxedElements()
            : [value];
        return items.Select(item => Service(item, line, col)).Distinct(StringComparer.Ordinal).OrderBy(static u => u, StringComparer.Ordinal).ToList();
    }

    private static string Resolve(JgsValue value, (string Name, string Uuid)[] named, string badId, string badText, int line, int col)
    {
        string cls = DeviceChecks.ClassOf(value);
        if (DeviceChecks.NumericClasses.Contains(cls))
        {
            double v = DeviceChecks.Count(value) == 1 ? DeviceChecks.Numbers(value).First() : double.NaN;
            if (DeviceChecks.Count(value) != 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:nonLogicalConditional",
                    "Operands to the logical AND (&&) and OR (||) operators must be convertible to logical scalar values. Use the ANY or ALL functions to reduce operands to logical scalar values.");
            }

            if (v >= 0 && v <= 0xFFFF && v == Math.Floor(v))
            {
                return GattNames.Canonical((int)v);
            }

            throw new JgsRuntimeException(line, col, badId, badText);
        }

        JgsValue text = TransportClient.Str2Char(value);
        if (!DeviceChecks.IsText(text))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:ble:ble:invalidUUIDType", BadType);
        }

        string written = DeviceChecks.Text(text);
        if (written.Trim().Equals(GattNames.Custom, StringComparison.OrdinalIgnoreCase))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:ble:ble:invalidUUIDNameCustom", "Specify UUID for custom service or characteristic.");
        }

        if (GattNames.TryCanonical(written) is { } canonical)
        {
            return canonical;
        }

        string[] names = named.Select(static n => n.Name).ToArray();
        bool ambiguous = false;
        if (written.Length > 0 && DeviceChecks.Match(written, names, out string? match, out ambiguous))
        {
            return GattNames.Canonical(named[Array.IndexOf(names, match)].Uuid);
        }

        if (ambiguous)
        {
            DeviceChecks.ValidateString(JgsValue.Str(written), names, new DeviceChecks.Subject(null, null), line, col);
        }

        throw new JgsRuntimeException(line, col, badId, badText);
    }
}
