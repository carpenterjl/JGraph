using JGraph.Data;
using JGraph.Devices;
using JGraph.Devices.Bluetooth;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// <c>bluetooth</c> and <c>bluetoothlist</c> (device classes plan, stage D3), transcribed from
/// R2025b's <c>bluetooth.m</c>, <c>bluetoothlist.m</c> and <c>matlab.bluetooth.ChannelClient</c>: a
/// classic device's serial port (RFCOMM) channel on the shared client. The channel client registers
/// no error map, so the client's refusals keep their transportlib and GenericClient identifiers; the
/// constructor's are <c>MATLAB:bluetooth:bluetooth:*</c>, the radio's <c>MATLAB:bluetooth:bluetoothlist:*</c>.
/// </summary>
internal sealed class BluetoothObject : DeviceObject
{
    internal static readonly TransportInterface Interface = new()
    {
        Name = "bluetooth",
        ObjectName = "b",
        SharedEventInfo = true,
        ConnectionLostText = "Device connection is lost.",
    };

    private const string ConnectDoc = "\nSee <a href=\"matlab: helpview('matlab', 'bluetooth_connectError')\">related documentation</a> for troubleshooting steps.";

    private static readonly DeviceClass Declaration = Declare();

    private readonly TransportClient _client;
    private readonly string _name;
    private readonly ulong _address;
    private readonly int _channel;
    private string _byteOrder = "little-endian";

    private BluetoothObject(DeviceSession session, Interpreter interpreter, IDeviceTransport transport, string name, ulong address, int channel)
        : base(session, interpreter)
    {
        _name = name;
        _address = address;
        _channel = channel;
        _client = new TransportClient(transport, Interface, this)
        {
            ConnectionLostMessage = Interface.ConnectionLostText,
            Timeout = JgsValue.Number(10),
        };
    }

    public override DeviceClass Class => Declaration;

    public override string? Summary() => Deleted ? "deleted" : $"{_name} ({BluetoothAddress.Format(_address)}:{_channel})";

    // --- the radio ----------------------------------------------------------------------------------

    /// <summary>The backend with the radio on, or the refusal R2025b's list transport gives.</summary>
    internal static IBluetoothBackend Radio(int line, int col, bool forBle)
    {
        IBluetoothBackend backend = BluetoothBackends.Current(out string? failure)
            ?? throw new JgsRuntimeException(line, col, forBle ? "MATLAB:ble:ble:failToScan" : "MATLAB:bluetooth:bluetoothlist:failedScan",
                failure ?? "The Bluetooth backend could not be loaded.");
        BluetoothRadioState state;
        try
        {
            state = backend.Radio;
        }
        catch (Exception e) when (e is not JgsException and not OperationCanceledException)
        {
            state = BluetoothRadioState.Missing;
        }

        return state switch
        {
            BluetoothRadioState.On => backend,
            _ when forBle => throw new JgsRuntimeException(line, col, "MATLAB:ble:ble:bluetoothOperationRadioNotAvailable", "Bluetooth radio is not available or turned off."),
            BluetoothRadioState.Missing => throw new JgsRuntimeException(line, col, "MATLAB:bluetooth:bluetoothlist:noBluetoothAdapter",
                "No Bluetooth adapter detected. Make sure system has a built-in or external Bluetooth adapter."),
            _ => throw new JgsRuntimeException(line, col, "MATLAB:bluetooth:bluetoothlist:winBluetoothNotPoweredOn", "System Bluetooth is not turned on."),
        };
    }

    // --- bluetoothlist -------------------------------------------------------------------------------

    /// <summary><c>bluetoothlist</c>, <c>bluetoothlist("Timeout", seconds)</c>: a table of Name, Address, Channel and Status.</summary>
    public static JgsValue List(JGraphScriptGlobals host, Interpreter interpreter, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        TimeSpan? timeout = null;
        if (args.Count > 0)
        {
            JgsValue name = TransportClient.Str2Char(args[0]);
            if (!DeviceChecks.IsText(name))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMustBeChar", "Expected a string scalar or character vector for the parameter name.");
            }

            string written = DeviceChecks.Text(name);
            if (!written.Equals("Timeout", StringComparison.OrdinalIgnoreCase) && !"Timeout".StartsWith(written, StringComparison.OrdinalIgnoreCase) || written.Length == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:UnmatchedParameter",
                    $"'{written}' is not a recognized parameter. For a list of valid name-value pair arguments, see the documentation for this function.");
            }

            if (args.Count == 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMissingValue",
                    "No value was given for 'Timeout'. Name-value pair arguments require a name followed by a value.");
            }

            JgsValue value = args[1];
            bool numericScalar = DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(value)) && DeviceChecks.Count(value) == 1;
            double seconds = numericScalar ? DeviceChecks.Numbers(value).First() : double.NaN;
            if (!(seconds >= 5))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:bluetooth:bluetoothlist:invalidTimeout",
                    "The value of 'Timeout' is invalid. Timeout must be greater than or equal to 5.");
            }

            timeout = TimeSpan.FromSeconds(Math.Min(seconds, 3600));
        }

        IBluetoothBackend backend = Radio(line, col, forBle: false);
        IReadOnlyList<ClassicDeviceInfo> found;
        try
        {
            found = backend.DiscoverClassic(timeout, interpreter.Cancellation);
        }
        catch (Exception e) when (e is not JgsException and not OperationCanceledException)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:bluetooth:bluetoothlist:failedScan",
                "Failed to scan nearby Bluetooth Classic devices. Contact Technical Support if issue persists.");
        }

        if (wanted == 0)
        {
            host.print("Run <a href=\"matlab:blelist\">blelist</a> to search for nearby Bluetooth Low Energy peripheral devices.");
        }

        if (found.Count == 0)
        {
            JgsBuiltins.Warn(host, "MATLAB:bluetooth:bluetoothlist:noDeviceFound", "Bluetooth Classic devices not detected. Ensure devices are turned on and within range.");
            return JgsEmpty.Zero();
        }

        static string StatusText(ClassicDeviceStatus status) => status switch
        {
            ClassicDeviceStatus.Ready => "Ready to connect",
            ClassicDeviceStatus.Connected => "Connected",
            ClassicDeviceStatus.Unpaired => "Requires pairing",
            ClassicDeviceStatus.Unsupported => "<a href=\"matlab:helpview(matlab.bluetooth.internal.getDocMap, 'status_unsupported')\">Unsupported</a>",
            _ => "<a href=\"matlab:helpview(matlab.bluetooth.internal.getDocMap, 'status_unknown')\">Unknown</a>",
        };

        int n = found.Count;
        JgsValue Strings(Func<ClassicDeviceInfo, string> pick) =>
            JgsValue.StringArray(found.Select(d => JgsValue.Str(pick(d))).ToArray(), n, 1);
        var table = new Table(
        [
            JgsBuiltins.TableColumnFrom("bluetoothlist", "Name", Strings(static d => d.Name), line, col),
            JgsBuiltins.TableColumnFrom("bluetoothlist", "Address", Strings(static d => BluetoothAddress.Format(d.Address)), line, col),
            // R2025b's Channel is a categorical, which JGraph holds as its cell of names (div=ADR0186).
            JgsBuiltins.TableColumnFrom("bluetoothlist", "Channel", CellColumn(found.Select(static d =>
                JgsValue.Str(d.Channel?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown")).ToArray()), line, col),
            JgsBuiltins.TableColumnFrom("bluetoothlist", "Status", Strings(static d => StatusText(d.Status)), line, col),
        ]);
        return JgsValue.Table(table);
    }

    /// <summary>A cell column of <paramref name="elements"/>.</summary>
    internal static JgsValue CellColumn(JgsValue[] elements)
    {
        JgsValue cell = JgsValue.Cell(elements);
        cell.Reshape(elements.Length, 1);
        return cell;
    }

    // --- the constructor ------------------------------------------------------------------------------

    /// <summary><c>b = bluetooth</c>, <c>bluetooth(name|address)</c>, <c>bluetooth(name|address, channel, Name=Value…)</c>.</summary>
    public static JgsValue Create(DeviceSession session, Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 6)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        string name;
        ulong address;
        int channel;
        if (args.Count == 0)
        {
            if (session.LastBluetooth is not { } last)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:bluetooth:bluetooth:noLastConnection", "Bluetooth Classic device connection has not been made before.");
            }

            (name, address, channel) = last;
            _ = Radio(line, col, forBle: false);
        }
        else
        {
            JgsValue identifier = TransportClient.Str2Char(args[0]);
            if (!DeviceChecks.IsText(identifier) || identifier.IsCharMatrix)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:bluetooth:bluetooth:invalidIdentifierType",
                    "Invalid device identifier. Device name or address must be specified as a string or character vector.");
            }

            string written = DeviceChecks.Text(identifier);
            if (System.Text.RegularExpressions.Regex.Match(written, "([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}") is { Success: true } m && m.Value == written)
            {
                written = written.Replace("-", "", StringComparison.Ordinal).Replace(":", "", StringComparison.Ordinal).ToUpperInvariant();
            }

            IBluetoothBackend backend = Radio(line, col, forBle: false);
            IReadOnlyList<ClassicDeviceInfo> paired;
            try
            {
                paired = backend.PairedClassic();
            }
            catch (Exception e) when (e is not JgsException and not OperationCanceledException)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:bluetooth:bluetooth:failedGetPairedDevices",
                    "Unexpected error - Failed to get paired devices. Contact Technical Support if issue persists." + ConnectDoc);
            }

            ClassicDeviceInfo? device = paired.FirstOrDefault(d =>
                d.Name.Equals(written, StringComparison.OrdinalIgnoreCase) || BluetoothAddress.Format(d.Address).Equals(written, StringComparison.OrdinalIgnoreCase))
                ?? throw new JgsRuntimeException(line, col, "MATLAB:bluetooth:bluetooth:invalidIdentifierValue",
                    $"Unable to find paired device with specified name or address \"{written}\". Run bluetoothlist to check device status and channel number." + ConnectDoc);
            if (session.Live.OfType<BluetoothObject>().Any(b => !b.Deleted && b._address == device.Address))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:bluetooth:bluetooth:connectionExists", "Connection to this device already exists in the workspace." + ConnectDoc);
            }

            name = device.Name;
            address = device.Address;
            channel = 1;
            if (args.Count >= 2)
            {
                JgsValue c = args[1];
                bool ok = DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(c)) && DeviceChecks.Count(c) == 1
                    && DeviceChecks.Numbers(c).First() is var v && v == Math.Floor(v) && v >= 0 && v <= 255;
                if (!ok)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:bluetooth:bluetooth:invalidChannel",
                        "Invalid device channel. Device channel must be specified as an integer between 0 and 255.");
                }

                channel = (int)DeviceChecks.Numbers(c).First();
            }
        }

        // Name-value pairs: ByteOrder and Timeout, partial and case-blind (inputParser).
        var options = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        List<JgsValue> nv = args.Skip(2).Select(TransportClient.Str2Char).ToList();
        if (nv.Count % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMissingValue",
                $"No value was given for '{(DeviceChecks.IsText(nv[^1]) ? DeviceChecks.Text(nv[^1]) : "")}'. Name-value pair arguments require a name followed by a value.");
        }

        for (int i = 0; i < nv.Count; i += 2)
        {
            string written = DeviceChecks.IsText(nv[i]) ? DeviceChecks.Text(nv[i]) : "";
            if (!DeviceChecks.IsText(nv[i]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMustBeChar", "Expected a string scalar or character vector for the parameter name.");
            }

            if (!DeviceChecks.Match(written, ["ByteOrder", "Timeout"], out string? option, out _))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:UnmatchedParameter",
                    $"'{written}' is not a recognized parameter. For a list of valid name-value pair arguments, see the documentation for this function.");
            }

            options[option!] = nv[i + 1];
        }

        IBluetoothBackend channelBackend = Radio(line, col, forBle: false);
        IDeviceTransport transport;
        try
        {
            transport = channelBackend.ConnectRfcomm(address, channel, interpreter.Cancellation);
        }
        catch (DeviceOpenException e)
        {
            (string key, string text) = e.Win32Error switch
            {
                10048 => ("failedConnectAddressInUse", "Unable to connect to device because device is already in use."),
                10061 => ("failedConnectConnectionRefused", "Unable to connect to device because device refused the connection."),
                10060 => ("failedConnectConnectionTimedOut", "Unable to connect to device because connection has timed out."),
                _ => ("failedConnectWithArgs", $"Unable to connect to device due to the following error: \"{e.Message.TrimEnd('.')}\". Run bluetoothlist to check device status and channel number."),
            };
            throw new JgsRuntimeException(line, col, "MATLAB:bluetooth:bluetooth:" + key, text + ConnectDoc);
        }

        var made = new BluetoothObject(session, interpreter, transport, name, address, channel);
        var call = new DeviceCall { Target = made, Args = [], Line = line, Column = col };
        try
        {
            if (options.TryGetValue("ByteOrder", out JgsValue? order))
            {
                made._byteOrder = TcpclientObject.ByteOrderOf(order, call);
                made._client.BigEndian = made._byteOrder == "big-endian";
            }

            made.SetTimeout(options.TryGetValue("Timeout", out JgsValue? timeout) ? timeout : JgsValue.Number(10), call);
        }
        catch
        {
            transport.Dispose();
            throw;
        }

        session.Remember(made);
        JgsValue value = JgsValue.External(made);
        JgsLifetime.Minted(value);
        return value;
    }

    /// <summary>ChannelClient.setTimeout: the client's own check, then at least one second.</summary>
    private void SetTimeout(JgsValue value, DeviceCall call)
    {
        JgsValue checkedValue = TcpclientObject.CheckTimeout(value, call);
        if (DeviceChecks.Numbers(checkedValue).First() < 1)
        {
            throw call.Error("MATLAB:bluetooth:bluetooth:invalidTimeout", "Invalid timeout value. Timeout must be specified as an integer greater than or equal to 1.");
        }

        _client.Timeout = checkedValue;
    }

    private static BluetoothObject Me(DeviceObject o) => (BluetoothObject)o;

    private static DeviceClass Declare()
    {
        static JgsRuntimeException ReadOnly(DeviceCall call, string name, string function) =>
            call.Error("transportlib:client:ReadOnlyProperty", $"To set \"{name}\", use the \"{function}\" function.");

        var properties = new List<DeviceProperty>
        {
            new("Name", static (o, _) => JgsValue.StringScalar(Me(o)._name)),
            new("Address", static (o, _) => JgsValue.StringScalar(BluetoothAddress.Format(Me(o)._address))),
            new("Channel", static (o, _) => JgsValue.Number(Me(o)._channel)),
            new("NumBytesAvailable", static (o, _) => JgsValue.Number(Me(o)._client.NumBytesAvailable)),
            new("NumBytesWritten", static (o, _) => JgsValue.Number(Me(o)._client.NumBytesWritten)),
            new("Terminator", static (o, _) => Me(o)._client.TerminatorValue, static (_, _, c) => throw ReadOnly(c, "Terminator", "configureTerminator")),
            new("BytesAvailableFcn", static (o, _) => Me(o)._client.BytesAvailableFcn ?? JgsEmpty.Zero(),
                static (_, _, c) => throw ReadOnly(c, "BytesAvailableFcn", "configureCallback")),
            new("BytesAvailableFcnCount", static (o, _) => JgsValue.Number(Me(o)._client.BytesAvailableFcnCount),
                static (_, _, c) => throw ReadOnly(c, "BytesAvailableFcnCount", "configureCallback")),
            new("BytesAvailableFcnMode", static (o, _) => JgsValue.StringScalar(Me(o)._client.BytesAvailableFcnMode),
                static (_, _, c) => throw ReadOnly(c, "BytesAvailableFcnMode", "configureCallback")),
            new("ByteOrder", static (o, _) => JgsValue.StringScalar(Me(o)._byteOrder), static (o, v, c) =>
            {
                Me(o)._byteOrder = TcpclientObject.ByteOrderOf(v, c);
                Me(o)._client.BigEndian = Me(o)._byteOrder == "big-endian";
            }),
            new("Timeout", static (o, _) => Me(o)._client.Timeout, static (o, v, c) => Me(o).SetTimeout(v, c)),
            new("ErrorOccurredFcn", static (o, _) => Me(o)._client.ErrorOccurredFcn ?? JgsEmpty.Zero(),
                static (o, v, c) => Me(o)._client.ErrorOccurredFcn = Me(o)._client.CallbackValue(v, "InvalidErrorOccurredFcn", "ErrorOccurredFcn must be a function handle.", c)),
            new("UserData", static (o, _) => Me(o)._client.UserData, static (o, v, _) => Me(o)._client.UserData = v),
            // LegacyBluetooth's hidden names.
            new("RemoteID", static (o, _) => JgsValue.StringScalar(BluetoothAddress.Format(Me(o)._address)), Hidden: true),
            new("RemoteName", static (o, _) => JgsValue.StringScalar(Me(o)._name), Hidden: true),
        };

        return new DeviceClass("bluetooth", "bluetooth",
            ["handle", "matlabshared.testmeas.CustomDisplay", "matlab.mixin.CustomDisplay",
             "matlabshared.transportlib.internal.compatibility.LegacyBluetooth"],
            properties,
            NetworkShared.ClientMethods(static o => Me(o)._client, withBinblock: false),
            ["addlistener", "bluetooth", "configureCallback", "configureTerminator", "delete", "eq", "findobj", "findprop", "flush", "ge",
             "gt", "isvalid", "le", "listener", "lt", "ne", "notify", "read", "readline", "write", "writeline"],
            ["Name", "Address", "Channel", "NumBytesAvailable", "NumBytesWritten"]);
    }

    protected override void OnDelete()
    {
        _client.Transport.Dispose();
        Session.LastBluetooth = (_name, _address, _channel);
    }
}
