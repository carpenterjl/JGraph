using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using JGraph.Devices;
using JGraph.Devices.Serial;
using JGraph.Devices.Simulation;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// <c>serialport</c> (device classes plan, stage 2): R2025b's <c>internal.Serialport</c> transcribed —
/// its constructor's three forms and their validation order, every property with the refusal its
/// setter raises, the methods of the shared client, pins and break, the preferences the no-argument
/// constructor reads, and the hidden legacy methods and properties of its <c>Legacy*</c> mixins.
/// </summary>
internal sealed class SerialportObject : DeviceObject, ILegacyTransport
{
    /// <summary>The serialport interface's settings on the shared client (internal.Serialport's registries).</summary>
    internal static readonly TransportInterface Interface = new()
    {
        Name = "serialport",
        ObjectName = "s",
        CapitalName = "Serialport",
        PrecisionRequired = true,
        TransportlibIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "IncorrectInputArgumentsSingular", "IncorrectInputArgumentsPlural", "IncorrectBytesAvailableModeSyntax",
            "InvalidBytesAvailableFcn", "InvalidTerminator", "NoICTLicense", "InvalidErrorOccurredFcn", "ReadOnlyProperty",
        },
        GenericClientIds = new HashSet<string>(StringComparer.Ordinal) { "expectedInteger", "expectedNonZero", "invalidType" },
        WarningIds = new HashSet<string>(StringComparer.Ordinal) { "ReadWarning", "ReadlineWarning", "ReadbinblockWarning" },
        ReadFailedId = "seriallib:serial:readFailed",
        ReadFailedLead = "Error reading data from the serial port.",
        ExtraSyntax = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["getpinstatus"] = "getpinstatus({0})",
            ["setRTS"] = "setRTS({0},FLAG)",
            ["setDTR"] = "setDTR({0},FLAG)",
            ["serialbreak"] = "serialbreak({0},TIME)",
        },
    };

    private const string ConnectionLostText =
        "Unable to detect connection to the serialport device. Ensure that the device is plugged in and create a new serialport object.";

    private const string DocLink = "See <a href=\"matlab: helpview('matlab', 'serialport_connectError')\">related documentation</a> for troubleshooting steps.";

    /// <summary>The legacy properties R2025b answers with [] and a warning (LegacyBase).</summary>
    internal static readonly string[] UnsupportedProperties =
    [
        "BreakInterruptFcn", "BusManagementStatus", "BytesToOutput", "CompareBits", "ConfirmationFcn", "DatagramAddress",
        "DatagramPort", "DataTerminalReady", "DriverName", "DriverSessions", "DriverType", "HandshakeStatus",
        "InputDatagramPacketSize", "InterruptFcn", "LocalPortMode", "MappedMemoryBase", "MappedMemorySize", "MemoryBase",
        "MemoryIncrement", "MemorySize", "MemorySpace", "NetworkRole", "ObjectVisibility", "OutputDatagramPacketSize",
        "OutputEmptyFcn", "PinStatusFcn", "Profile", "ReadAsyncMode", "RecordDetail", "RecordMode", "RecordName",
        "RecordStatus", "RequestToSend", "Sessions", "TimerFcn", "TimerPeriod", "TransferStatus", "TriggerFcn",
        "TriggerLine", "TriggerType", "ValuesReceived", "ValuesSent",
    ];

    internal static readonly string[] UnsupportedMethods =
        ["instrhelp", "readasync", "stopasync", "propinfo", "record", "instrid", "instrsupport", "instrcallback", "instrnotify", "instrfind", "instrfindall"];

    private static readonly DeviceClass Declaration = Declare();

    private readonly string _port;
    private TransportClient? _client;
    private JgsValue _baud = JgsValue.Number(9600);
    private JgsValue _dataBits = JgsValue.Number(8);
    private JgsValue _stopBits = JgsValue.Number(1);
    private string _parity = "none";
    private string _flow = "none";
    private string _byteOrder = "little-endian";
    private string _tag = "";
    private JgsValue _inputBufferSize = JgsValue.Number(512);
    private JgsValue _outputBufferSize = JgsValue.Number(1024);

    private SerialportObject(DeviceSession session, Interpreter interpreter, string port)
        : base(session, interpreter)
    {
        _port = port;
    }

    public override DeviceClass Class => Declaration;

    /// <summary>The client, once connected.</summary>
    internal TransportClient Client => _client ?? throw new InvalidOperationException("not connected");

    private ISerialTransport Serial => (ISerialTransport)Client.Transport;

    public string Port => _port;

    public override string? Summary() =>
        Deleted ? "deleted" : $"{_port}, {DeviceChecks.Numbers(_baud).First().ToString(CultureInfo.InvariantCulture)} baud";

    /// <summary>isequal as serialportfind compares: text by its characters (a char row equals a string), the rest by value.</summary>
    internal static bool SameValue(JgsValue a, JgsValue b) =>
        a.AsExternalOrNull() is DeviceEnumValue member ? member.Matches(b)
        : DeviceChecks.IsText(a) && DeviceChecks.IsText(b)
            ? DeviceChecks.Text(a) == DeviceChecks.Text(b)
            : JgsStdlib.DeepEquals(a, b);

    // --- construction ----------------------------------------------------------------------------------

    /// <summary><c>s = serialport()</c>, <c>serialport(port, baud)</c>, <c>serialport(port, baud, Name=Value…)</c>.</summary>
    public static JgsValue Create(DeviceSession session, Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 1)
        {
            throw new JgsRuntimeException(line, col, "serialport:serialport:IncorrectInputArgumentsPlural",
                "Invalid number of input arguments for 'serialport'. Valid syntaxes are\ns = serialport()\ns = serialport(PORT,BAUDRATE)\ns = serialport(PORT,BAUDRATE,NAME,VALUE)");
        }

        JgsValue portArg;
        JgsValue baudArg;
        List<JgsValue> nv;
        (TerminatorSpec Read, TerminatorSpec Write, bool Pair)? savedTerminator = null;
        if (args.Count == 0)
        {
            JsonObject? saved = DevicePreferences.Read("serialport");
            if (saved is null || !SavedFields.All(saved.ContainsKey))
            {
                throw new JgsRuntimeException(line, col, "serialport:serialport:NoSavedPreferences",
                    "Unable to create the default serialport object. Specify the 'Port' and 'BaudRate' input arguments.");
            }

            portArg = JgsValue.Str(saved["Port"]!.GetValue<string>());
            baudArg = JgsValue.Number(saved["BaudRate"]!.GetValue<double>());
            savedTerminator = TerminatorFromJson(saved["Terminator"]);
            nv = [];
            foreach (string name in new[] { "ByteOrder", "FlowControl", "StopBits", "DataBits", "Parity", "Timeout", "Tag" })
            {
                nv.Add(JgsValue.Str(name));
                nv.Add(saved[name] is JsonValue v && v.TryGetValue(out double number) ? JgsValue.Number(number) : JgsValue.Str(saved[name]?.GetValue<string>() ?? ""));
            }
        }
        else
        {
            portArg = args[0];
            baudArg = args[1];
            nv = args.Skip(2).Select(TransportClient.Str2Char).ToList();
        }

        portArg = TransportClient.Str2Char(portArg);
        var portWho = new DeviceChecks.Subject("Serialport", "PORT", 1);
        DeviceChecks.Classes(portArg, ["char"], portWho, line, col);
        DeviceChecks.Attributes(portArg, ["nonempty"], portWho, line, col);
        var baudWho = new DeviceChecks.Subject("Serialport", "BAUDRATE", 2);
        DeviceChecks.Classes(baudArg, ["double"], baudWho, line, col);
        DeviceChecks.Attributes(baudArg, ["nonempty", "positive", "scalar"], baudWho, line, col);

        string port = DeviceChecks.Text(portArg);
        var serial = new SerialportObject(session, interpreter, port);
        var call = new DeviceCall { Target = serial, Args = [], Line = line, Column = col };
        serial.SetBaudRate(baudArg, call);
        if (nv.Count % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "serialport:serialport:UnmatchedPVPairs",
                "Incorrect number of input arguments. Each parameter name must be followed by a corresponding value.");
        }

        serial.InitProperties(nv, call);
        serial.Connect(call);
        if (savedTerminator is { } terminator)
        {
            serial.Client.RestoreTerminator(terminator.Read, terminator.Write, terminator.Pair);
        }

        session.Remember(serial);
        JgsValue made = JgsValue.External(serial);
        JgsLifetime.Minted(made);
        return made;
    }

    private static readonly string[] SavedFields = ["Port", "BaudRate", "ByteOrder", "FlowControl", "StopBits", "DataBits", "Parity", "Timeout", "Terminator", "Tag"];

    private static readonly string[] ParameterNames = ["DataBits", "Parity", "StopBits", "FlowControl", "ByteOrder", "Timeout", "Tag"];

    /// <summary>initProperties: an inputParser with partial matching, then each setter in turn.</summary>
    private void InitProperties(List<JgsValue> nv, DeviceCall call)
    {
        var given = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        for (int i = 0; i < nv.Count; i += 2)
        {
            string written = DeviceChecks.IsText(nv[i]) ? DeviceChecks.Text(nv[i]) : "";
            var hits = ParameterNames.Where(p => written.Length > 0 && p.StartsWith(written, StringComparison.OrdinalIgnoreCase)).ToList();
            string? exact = hits.Find(h => h.Equals(written, StringComparison.OrdinalIgnoreCase));
            if (hits.Count == 0)
            {
                throw call.Error("MATLAB:InputParser:UnmatchedParameter",
                    $"'{written}' is not a recognized parameter. For a list of valid name-value pair arguments, see the documentation for this function.");
            }

            if (hits.Count > 1 && exact is null)
            {
                throw call.Error("MATLAB:InputParser:AmbiguousParameter",
                    $"Expected a parameter name, but '{written}' matches multiple parameter names:  {string.Join(", ", hits)}.");
            }

            string name = exact ?? hits[0];
            JgsValue value = nv[i + 1];
            string? failure = name switch
            {
                "DataBits" or "StopBits" or "Timeout" => DeviceChecks.Count(value) == 1 ? null : "isscalar",
                "Tag" => DeviceChecks.ClassOf(value) is "char" or "string" ? null : "@(x)isstring(x)||ischar(x)",
                _ => null,
            };
            if (failure is not null)
            {
                throw call.Error("MATLAB:InputParser:ArgumentFailedValidation", $"The value of '{name}' is invalid. It must satisfy the function: {failure}.");
            }

            if (name is "Parity" or "FlowControl" or "ByteOrder")
            {
                try
                {
                    var who = new DeviceChecks.Subject(null, null);
                    DeviceChecks.Classes(value, ["char", "string"], who, call.Line, call.Column);
                    DeviceChecks.Attributes(value, ["nonempty"], who, call.Line, call.Column);
                }
                catch (JgsRuntimeException refused)
                {
                    throw call.Error(refused.Identifier, $"The value of '{name}' is invalid. {refused.Message}");
                }
            }

            given[name] = value;
        }

        JgsValue Given(string name, JgsValue fallback) => given.TryGetValue(name, out JgsValue? v) ? v : fallback;
        SetDataBits(Given("DataBits", JgsValue.Number(8)), call);
        SetChoice("Parity", Given("Parity", JgsValue.Str("none")), ["none", "even", "odd"], call);
        SetStopBits(Given("StopBits", JgsValue.Number(1)), call);
        SetChoice("FlowControl", Given("FlowControl", JgsValue.Str("none")), ["none", "hardware", "software"], call);
        SetChoice("ByteOrder", Given("ByteOrder", JgsValue.Str("little-endian")), ["little-endian", "big-endian"], call);
        _pendingTimeout = CheckTimeout(Given("Timeout", JgsValue.Number(10)), call);
        SetTag(Given("Tag", JgsValue.StringScalar("")), call);
    }

    private JgsValue _pendingTimeout = JgsValue.Number(10);

    /// <summary>Opens the port with the settings made so far; any failure is R2025b's ConnectionFailed.</summary>
    private void Connect(DeviceCall call)
    {
        IDeviceTransport transport;
        try
        {
            SerialSettings settings = Settings();
            if (Session.SimulatedFor(_port) is { } line)
            {
                transport = line.Open(settings);
            }
            else if (Session.IsSimulatedPeer(_port) || !OperatingSystem.IsWindows())
            {
                throw new DeviceOpenException($"The port {_port} is in use.");
            }
            else
            {
                transport = OpenWin32(settings);
            }
        }
        catch (Exception e) when (e is DeviceOpenException or DeviceSettingsException or ArgumentException)
        {
            throw call.Error("serialport:serialport:ConnectionFailed",
                $"Unable to connect to the serialport device at port '{_port}'. Verify that a device is connected to the port, the port is not in use, and all serialport input arguments and parameter values are supported by the device.\n{DocLink}");
        }

        _client = new TransportClient(transport, Interface, this)
        {
            ConnectionLostMessage = ConnectionLostText,
            Timeout = _pendingTimeout,
            BigEndian = _byteOrder == "big-endian",
        };
    }

    [SupportedOSPlatform("windows")]
    private Win32SerialPort OpenWin32(SerialSettings settings) => Win32SerialPort.Open(_port, settings);

    private SerialSettings Settings()
    {
        double data = DeviceChecks.Numbers(_dataBits).First();
        if (data is < 5 or > 8)
        {
            throw new DeviceSettingsException("invalid character_size value");
        }

        double baud = DeviceChecks.Numbers(_baud).First();
        return new SerialSettings
        {
            BaudRate = (int)Math.Min(baud, int.MaxValue),
            DataBits = (int)data,
            Parity = _parity switch { "even" => SerialParity.Even, "odd" => SerialParity.Odd, _ => SerialParity.None },
            StopBits = DeviceChecks.Numbers(_stopBits).First() switch { 1.5 => SerialStopBits.OnePointFive, 2 => SerialStopBits.Two, _ => SerialStopBits.One },
            FlowControl = _flow switch { "hardware" => SerialFlowControl.Hardware, "software" => SerialFlowControl.Software, _ => SerialFlowControl.None },
        };
    }

    /// <summary>Applies the settings to an open port; the driver's refusal is <paramref name="refusal"/>.</summary>
    private void ApplyNow(DeviceCall call, string refusalId, string refusal)
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            Serial.Apply(Settings());
        }
        catch (DeviceSettingsException e)
        {
            throw call.Error(refusalId, $"{refusal}\n{e.Message}");
        }
    }

    // --- setters ------------------------------------------------------------------------------------------

    private void SetBaudRate(JgsValue value, DeviceCall call)
    {
        bool ok = Array.IndexOf(DeviceChecks.NumericClasses, DeviceChecks.ClassOf(value)) >= 0 && DeviceChecks.Count(value) == 1
            && DeviceChecks.Numbers(value).First() is var x && double.IsFinite(x) && x > 0 && x == Math.Floor(x);
        if (!ok)
        {
            throw call.Error("serialport:serialport:InvalidBaudRate", "BaudRate must be a scalar positive integer.");
        }

        JgsValue old = _baud;
        _baud = value;
        try
        {
            ApplyNow(call, "seriallib:serial:setBaudRateFailed", "Error setting BaudRate.");
        }
        catch
        {
            _baud = old;
            throw;
        }
    }

    private void SetDataBits(JgsValue value, DeviceCall call)
    {
        var who = new DeviceChecks.Subject("Serial", "DataBits");
        DeviceChecks.Classes(value, ["numeric"], who, call.Line, call.Column);
        DeviceChecks.Attributes(value, ["scalar", "integer"], who, call.Line, call.Column);
        double bits = DeviceChecks.Numbers(value).First();
        if (_client is not null && bits is < 5 or > 8)
        {
            throw call.Error("seriallib:serial:setDataBitsFailed", "Error setting number of Data Bits.\ninvalid character_size value");
        }

        JgsValue old = _dataBits;
        _dataBits = value;
        try
        {
            ApplyNow(call, "seriallib:serial:setDataBitsFailed", "Error setting number of Data Bits.");
        }
        catch
        {
            _dataBits = old;
            throw;
        }
    }

    private void SetStopBits(JgsValue value, DeviceCall call)
    {
        var who = new DeviceChecks.Subject("Serial", "StopBits");
        DeviceChecks.Classes(value, ["numeric"], who, call.Line, call.Column);
        double[] numbers = DeviceChecks.Numbers(value).ToArray();
        if (numbers.Length != 1 || numbers[0] is not (1 or 1.5 or 2))
        {
            throw call.Error("seriallib:serial:invalidStopBits", "StopBits must be one of 1, 1.5, or 2.");
        }

        JgsValue old = _stopBits;
        _stopBits = value;
        try
        {
            ApplyNow(call, "seriallib:serial:setStopBitsFailed", "Error setting number of Stop Bits.");
        }
        catch
        {
            _stopBits = old;
            throw;
        }
    }

    /// <summary>Parity, FlowControl and ByteOrder: text, nonempty, then one of the choices (partial, case-blind).</summary>
    private void SetChoice(string name, JgsValue value, string[] options, DeviceCall call)
    {
        value = TransportClient.Str2Char(value);
        var who = new DeviceChecks.Subject("Serial", name);
        DeviceChecks.Classes(value, ["char", "string"], who, call.Line, call.Column);
        DeviceChecks.Attributes(value, ["nonempty"], who, call.Line, call.Column);
        string chosen = DeviceChecks.ValidateString(value, options, new DeviceChecks.Subject(null, null), call.Line, call.Column);
        switch (name)
        {
            case "Parity":
            {
                string old = _parity;
                _parity = chosen;
                try
                {
                    ApplyNow(call, "seriallib:serial:setParityFailed", "Error setting Parity.");
                }
                catch
                {
                    _parity = old;
                    throw;
                }

                break;
            }

            case "FlowControl":
            {
                string old = _flow;
                _flow = chosen;
                try
                {
                    ApplyNow(call, "seriallib:serial:setFlowControlFailed", "Error setting FlowControl.");
                }
                catch
                {
                    _flow = old;
                    throw;
                }

                break;
            }

            default:
                _byteOrder = chosen;
                if (_client is not null)
                {
                    _client.BigEndian = chosen == "big-endian";
                }

                break;
        }
    }

    /// <summary>The transport's Timeout check (MATLAB:Serial:*), then its demand for a double or single.</summary>
    private static JgsValue CheckTimeout(JgsValue value, DeviceCall call)
    {
        var who = new DeviceChecks.Subject("Serial", "Timeout");
        DeviceChecks.Classes(value, ["numeric"], who, call.Line, call.Column);
        DeviceChecks.Attributes(value, ["scalar", "finite", "nonnegative", "nonzero"], who, call.Line, call.Column);
        if (DeviceChecks.ClassOf(value) is not ("double" or "single"))
        {
            throw call.Error("Stream:timeout:invalidTime", "Timeout must be a non-negative scalar double.");
        }

        return value;
    }

    private void SetTag(JgsValue value, DeviceCall call)
    {
        string cls = DeviceChecks.ClassOf(value);
        if (cls is not ("char" or "string" or "cell"))
        {
            throw DeviceChecks.TypeRefusal(new DeviceChecks.Subject("TagAccessor", "TAG"), "char, string", cls, call.Line, call.Column);
        }

        if (!DeviceChecks.IsText(value) || value.IsCharMatrix)
        {
            throw call.Error("MATLAB:TagAccessor:expectedScalartext", "Expected TAG to be a non-missing string scalar or character vector.");
        }

        _tag = DeviceChecks.Text(value);
    }

    // --- the declaration -----------------------------------------------------------------------------------

    private static SerialportObject Me(DeviceObject o) => (SerialportObject)o;

    private static DeviceClass Declare()
    {
        static JgsRuntimeException ReadOnly(DeviceCall call, string name, string function) =>
            call.Error("serialport:serialport:ReadOnlyProperty", $"To set \"{name}\", use the \"{function}\" function.");

        var properties = new List<DeviceProperty>
        {
            new("Port", static (o, _) => JgsValue.StringScalar(Me(o)._port)),
            new("BaudRate", static (o, _) => Me(o)._baud, static (o, v, c) => Me(o).SetBaudRate(v, c)),
            new("Timeout", static (o, _) => Me(o).Client.Timeout, static (o, v, c) => Me(o).Client.Timeout = CheckTimeout(v, c)),
            new("FlowControl", static (o, _) => JgsValue.StringScalar(Me(o)._flow), static (o, v, c) => Me(o).SetChoice("FlowControl", v, ["none", "hardware", "software"], c)),
            new("Parity", static (o, _) => JgsValue.StringScalar(Me(o)._parity), static (o, v, c) => Me(o).SetChoice("Parity", v, ["none", "even", "odd"], c)),
            new("StopBits", static (o, _) => Me(o)._stopBits, static (o, v, c) => Me(o).SetStopBits(v, c)),
            new("DataBits", static (o, _) => Me(o)._dataBits, static (o, v, c) => Me(o).SetDataBits(v, c)),
            new("ByteOrder", static (o, _) => JgsValue.StringScalar(Me(o)._byteOrder), static (o, v, c) => Me(o).SetChoice("ByteOrder", v, ["little-endian", "big-endian"], c)),
            new("UserData", static (o, _) => Me(o).Client.UserData, static (o, v, _) => Me(o).Client.UserData = v),
            new("Terminator", static (o, _) => Me(o).Client.TerminatorValue, static (_, _, c) => throw ReadOnly(c, "Terminator", "configureTerminator")),
            new("BytesAvailableFcnCount", static (o, _) => JgsValue.Number(Me(o).Client.BytesAvailableFcnCount),
                static (_, _, c) => throw ReadOnly(c, "BytesAvailableFcnCount", "configureCallback")),
            new("BytesAvailableFcnMode", static (o, _) => JgsValue.StringScalar(Me(o).Client.BytesAvailableFcnMode),
                static (_, _, c) => throw ReadOnly(c, "BytesAvailableFcnMode", "configureCallback")),
            new("BytesAvailableFcn", static (o, _) => Me(o).Client.BytesAvailableFcn ?? JgsEmpty.Zero(),
                static (_, _, c) => throw ReadOnly(c, "BytesAvailableFcn", "configureCallback")),
            new("ErrorOccurredFcn", static (o, _) => Me(o).Client.ErrorOccurredFcn ?? JgsEmpty.Zero(),
                static (o, v, c) => Me(o).Client.ErrorOccurredFcn = Me(o).Client.CallbackValue(v, "InvalidErrorOccurredFcn", "ErrorOccurredFcn must be a function handle.", c)),
            new("NumBytesAvailable", static (o, _) => JgsValue.Number(Me(o).Client.NumBytesAvailable)),
            new("NumBytesWritten", static (o, _) => JgsValue.Number(Me(o).Client.NumBytesWritten)),
            new("Tag", static (o, _) => JgsValue.StringScalar(Me(o)._tag), static (o, v, c) => Me(o).SetTag(v, c)),

            // The hidden legacy properties (LegacyBase, LegacySerial).
            new("BytesAvailable", static (o, _) => JgsValue.Number(Me(o).Client.NumBytesAvailable), Hidden: true),
            new("ErrorFcn", static (o, _) => Me(o).Client.ErrorOccurredFcn ?? JgsEmpty.Zero(),
                static (o, v, c) => Me(o).Client.ErrorOccurredFcn = Me(o).Client.CallbackValue(v, "InvalidErrorOccurredFcn", "ErrorOccurredFcn must be a function handle.", c), Hidden: true),
            new("PinStatus", static (o, c) => Me(o).LegacyPinStatus(c), Hidden: true),
            new("Status", static (_, _) => JgsValue.Str("open"), Hidden: true),
            new("InputBufferSize", static (o, _) => Me(o)._inputBufferSize, static (o, v, _) => Me(o)._inputBufferSize = v, Hidden: true),
            new("OutputBufferSize", static (o, _) => Me(o)._outputBufferSize, static (o, v, _) => Me(o)._outputBufferSize = v, Hidden: true),
        };

        foreach (string name in UnsupportedProperties)
        {
            properties.Add(new DeviceProperty(name,
                (o, c) =>
                {
                    Unsupported(c, "PropertyNotSupported", name == "LocalPortMode" ? "LocalPortMod" : name);
                    return JgsEmpty.Zero();
                },
                (_, _, c) => Unsupported(c, "PropertyNotSupported", name == "LocalPortMode" ? "LocalPortMod" : name),
                Hidden: true));
        }

        var methods = new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["read"] = static c => [Me(c.Target).Live(c).Read(c)],
            ["readline"] = static c => [Me(c.Target).Live(c).ReadLine(c)],
            ["readbinblock"] = static c => [Me(c.Target).Live(c).ReadBinblock(c)],
            ["write"] = static c => Void(c, static c => Me(c.Target).Live(c).Write(c)),
            ["writeline"] = static c => Void(c, static c => Me(c.Target).Live(c).WriteLine(c)),
            ["writebinblock"] = static c => Void(c, static c => Me(c.Target).Live(c).WriteBinblock(c)),
            ["writeread"] = static c => [Me(c.Target).Live(c).WriteRead(c)],
            ["configureCallback"] = static c => Void(c, static c => Me(c.Target).Live(c).ConfigureCallback(c)),
            ["configureTerminator"] = static c => Void(c, static c => Me(c.Target).Live(c).ConfigureTerminator(c)),
            ["flush"] = static c => Void(c, static c => Me(c.Target).Live(c).Flush(c)),
            ["getpinstatus"] = static c => [Me(c.Target).GetPinStatus(c)],
            ["setRTS"] = static c => Void(c, static c => Me(c.Target).SetPin(c, "setRTS", rts: true)),
            ["setDTR"] = static c => Void(c, static c => Me(c.Target).SetPin(c, "setDTR", rts: false)),
            ["serialbreak"] = static c => Void(c, static c => Me(c.Target).Break(c)),
            ["instrhwinfo"] = static c => [Me(c.Target).InstrHwInfo(c)],

            // LegacyBase, LegacyASCIIMixin, LegacyBinaryMixin, LegacyBinblockMixin, LegacyQueryMixin.
            ["fopen"] = static c => Void(c, static c => c.Target.LiveOrThrow(c.Line, c.Column)),
            ["fclose"] = static c => Void(c, static c => Legacy(c, "DoesNotCloseConnection",
                "The fclose method does not close the connection. Instead, clear the interface object to close the connection.")),
            ["flushinput"] = static c => Void(c, static c => Me(c.Target).Live(c).Flush(Retarget(c, [JgsValue.Str("input")]))),
            ["flushoutput"] = static c => Void(c, static c => Me(c.Target).Live(c).Flush(Retarget(c, [JgsValue.Str("output")]))),
            ["fprintf"] = static c => Void(c, static c => SerialportLegacy.Fprintf(Me(c.Target), c)),
            ["fwrite"] = static c => Void(c, static c => SerialportLegacy.Fwrite(Me(c.Target), c)),
            ["fread"] = static c => SerialportLegacy.Fread(Me(c.Target), c),
            ["fgetl"] = static c => SerialportLegacy.Fgetl(Me(c.Target), c, keepTerminator: false),
            ["fgets"] = static c => SerialportLegacy.Fgetl(Me(c.Target), c, keepTerminator: true),
            ["fscanf"] = static c => SerialportLegacy.Fscanf(Me(c.Target), c),
            ["scanstr"] = static c => SerialportLegacy.Scanstr(Me(c.Target), c),
            ["query"] = static c => SerialportLegacy.Query(Me(c.Target), c),
            ["binblockread"] = static c => SerialportLegacy.BinblockRead(Me(c.Target), c),
            ["binblockwrite"] = static c => Void(c, static c => SerialportLegacy.BinblockWrite(Me(c.Target), c)),
        };

        foreach (string name in UnsupportedMethods)
        {
            methods[name] = c =>
            {
                Unsupported(c, "MethodNotSupported", name);
                return [];
            };
        }

        return new DeviceClass(
            "internal.Serialport",
            "Serialport",
            [
                "matlabshared.testmeas.internal.SetGet", "matlab.mixin.SetGet", "handle", "matlabshared.testmeas.CustomDisplay",
                "matlab.mixin.CustomDisplay", "matlabshared.transportlib.internal.compatibility.LegacyBase",
                "matlabshared.transportlib.internal.compatibility.LegacyBinaryMixin",
                "matlabshared.transportlib.internal.compatibility.LegacyNullMixin",
                "matlabshared.transportlib.internal.compatibility.LegacyASCIIMixin",
                "matlabshared.transportlib.internal.compatibility.LegacyBinblockMixin",
                "matlabshared.transportlib.internal.compatibility.LegacyQueryMixin",
                "matlabshared.testmeas.internal.mixins.CacheEnabler", "matlabshared.transportlib.internal.TagAccessor",
            ],
            properties,
            methods,
            [
                "Serialport", "addlistener", "configureCallback", "configureTerminator", "delete", "eq", "findobj", "findprop", "flush",
                "ge", "get", "getpinstatus", "gt", "isvalid", "le", "listener", "lt", "ne", "notify", "read", "readbinblock", "readline",
                "serialbreak", "set", "setDTR", "setRTS", "write", "writebinblock", "writeline", "writeread",
            ],
            ["Port", "BaudRate", "Tag", "NumBytesAvailable"]);
    }

    /// <summary>A method with no output: asked for one, it is MATLAB's TooManyOutputs.</summary>
    private static JgsValue[] Void(DeviceCall call, Action<DeviceCall> body)
    {
        if (call.Wanted > 0)
        {
            throw call.Error("MATLAB:TooManyOutputs", "Too many output arguments.");
        }

        body(call);
        return [];
    }

    internal static DeviceCall Retarget(DeviceCall call, IReadOnlyList<JgsValue> args) =>
        new() { Target = call.Target, Args = args, Wanted = call.Wanted, Line = call.Line, Column = call.Column };

    internal TransportClient Live(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        return Client;
    }

    // What the legacy mixins reach: the client's own methods.
    TransportClient ILegacyTransport.Live(DeviceCall call) => Live(call);

    JgsValue ILegacyTransport.LegacyRead(DeviceCall call) => Live(call).Read(call);

    JgsValue ILegacyTransport.LegacyReadLine(DeviceCall call) => Live(call).ReadLine(call);

    JgsValue ILegacyTransport.LegacyWriteRead(DeviceCall call) => Live(call).WriteRead(call);

    JgsValue ILegacyTransport.LegacyReadBinblock(DeviceCall call) => Live(call).ReadBinblock(call);

    void ILegacyTransport.LegacyWrite(DeviceCall call) => Live(call).Write(call);

    void ILegacyTransport.LegacyWriteLine(DeviceCall call) => Live(call).WriteLine(call);

    void ILegacyTransport.LegacyWriteBinblock(DeviceCall call) => Live(call).WriteBinblock(call);

    private static void Unsupported(DeviceCall call, string key, string name) =>
        JgsBuiltins.Warn(call.Host, "transportlib:legacy:" + key,
            key == "MethodNotSupported" ? $"{name} is not a valid method for this interface." : $"{name} is not a valid property for this interface.");

    private static void Legacy(DeviceCall call, string key, string text)
    {
        call.Target.LiveOrThrow(call.Line, call.Column);
        JgsBuiltins.Warn(call.Host, "transportlib:legacy:" + key, text);
    }

    // --- pins and break -------------------------------------------------------------------------------------

    private JgsValue GetPinStatus(DeviceCall call)
    {
        TransportClient client = Live(call);
        if (call.Args.Count != 0)
        {
            throw Interface.NarginSingular("getpinstatus", call.Line, call.Column);
        }

        SerialPins pins = Pins(call, client);
        return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["ClearToSend"] = JgsValue.Bool(pins.ClearToSend),
            ["DataSetReady"] = JgsValue.Bool(pins.DataSetReady),
            ["CarrierDetect"] = JgsValue.Bool(pins.CarrierDetect),
            ["RingIndicator"] = JgsValue.Bool(pins.RingIndicator),
        });
    }

    private SerialPins Pins(DeviceCall call, TransportClient client)
    {
        try
        {
            return Serial.GetPins();
        }
        catch (Exception e) when (e is DeviceConnectionLostException or DeviceIOException or ObjectDisposedException)
        {
            throw call.Error("serialport:serialport:ConnectionLost", client.ConnectionLostMessage);
        }
    }

    /// <summary>The legacy PinStatus: the four pins as 'on'/'off' (matlab.lang.OnOffSwitchState).</summary>
    private JgsValue LegacyPinStatus(DeviceCall call)
    {
        SerialPins pins = Pins(call, Client);
        static JgsValue OnOff(bool on) => JgsValue.Str(on ? "on" : "off");
        return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["ClearToSend"] = OnOff(pins.ClearToSend),
            ["DataSetReady"] = OnOff(pins.DataSetReady),
            ["CarrierDetect"] = OnOff(pins.CarrierDetect),
            ["RingIndicator"] = OnOff(pins.RingIndicator),
        });
    }

    private void SetPin(DeviceCall call, string method, bool rts)
    {
        TransportClient client = Live(call);
        if (call.Args.Count != 1)
        {
            throw Interface.NarginSingular(method, call.Line, call.Column);
        }

        var who = new DeviceChecks.Subject("Serial", method);
        DeviceChecks.Classes(call.Args[0], ["logical"], who, call.Line, call.Column);
        DeviceChecks.Attributes(call.Args[0], ["scalar"], who, call.Line, call.Column);
        bool on = DeviceChecks.Numbers(call.Args[0]).First() != 0;
        try
        {
            if (rts)
            {
                Serial.SetRts(on);
            }
            else
            {
                Serial.SetDtr(on);
            }
        }
        catch (Exception e) when (e is DeviceConnectionLostException or DeviceIOException or ObjectDisposedException)
        {
            throw call.Error("serialport:serialport:ConnectionLost", client.ConnectionLostMessage);
        }
    }

    private void Break(DeviceCall call)
    {
        TransportClient client = Live(call);
        if (call.Args.Count != 1)
        {
            throw Interface.NarginSingular("serialbreak", call.Line, call.Column);
        }

        var who = new DeviceChecks.Subject("Serial", "serialBreak");
        DeviceChecks.Classes(call.Args[0], ["numeric"], who, call.Line, call.Column);
        DeviceChecks.Attributes(call.Args[0], ["scalar", "nonnegative", "integer", "nonzero"], who, call.Line, call.Column);
        int milliseconds = (int)Math.Min(DeviceChecks.Numbers(call.Args[0]).First(), int.MaxValue);
        try
        {
            Serial.SendBreak(milliseconds, Interpreter.Cancellation);
        }
        catch (Exception e) when (e is DeviceConnectionLostException or DeviceIOException or ObjectDisposedException)
        {
            throw call.Error("serialport:serialport:ConnectionLost", client.ConnectionLostMessage);
        }
    }

    /// <summary><c>instrhwinfo(s, name)</c>: the property's value.</summary>
    private JgsValue InstrHwInfo(DeviceCall call)
    {
        if (call.Args.Count < 1)
        {
            throw call.Error("MATLAB:minrhs", "Not enough input arguments.");
        }

        return GetProperty(DeviceChecks.IsText(call.Args[0]) ? DeviceChecks.Text(call.Args[0]) : "", call);
    }

    // --- lifetime ------------------------------------------------------------------------------------------

    /// <summary>delete: saves the settings for the next <c>serialport()</c>, then closes the port.</summary>
    protected override void OnDelete()
    {
        if (_client is not { } client)
        {
            return;
        }

        if (client.Transport.Connected)
        {
            DevicePreferences.Write("serialport", SavedRecord(client));
        }

        client.Transport.Dispose();
    }

    private JsonObject SavedRecord(TransportClient client) => new()
    {
        ["Port"] = _port,
        ["BaudRate"] = DeviceChecks.Numbers(_baud).First(),
        ["ByteOrder"] = _byteOrder,
        ["FlowControl"] = _flow,
        ["StopBits"] = DeviceChecks.Numbers(_stopBits).First(),
        ["DataBits"] = DeviceChecks.Numbers(_dataBits).First(),
        ["Parity"] = _parity,
        ["Timeout"] = client.TimeoutSeconds,
        ["Terminator"] = client.TerminatorPair
            ? new JsonArray(TerminatorJson(client.ReadTerminator), TerminatorJson(client.WriteTerminator))
            : TerminatorJson(client.ReadTerminator),
        ["Tag"] = _tag,
    };

    private static JsonNode TerminatorJson(TerminatorSpec terminator) =>
        terminator.Shown.IsStringArray ? JsonValue.Create(terminator.Word)! : JsonValue.Create((double)terminator.Bytes[0])!;

    private static (TerminatorSpec, TerminatorSpec, bool)? TerminatorFromJson(JsonNode? node)
    {
        static TerminatorSpec One(JsonNode? item) => item is JsonValue value && value.TryGetValue(out double number)
            ? new TerminatorSpec(JgsValue.Number(number), [(byte)number])
            : (item?.GetValue<string>()) switch
            {
                "CR" => TerminatorSpec.CR,
                "CR/LF" => TerminatorSpec.CRLF,
                _ => TerminatorSpec.LF,
            };

        return node switch
        {
            null => null,
            JsonArray pair when pair.Count == 2 => (One(pair[0]), One(pair[1]), true),
            _ => (One(node), One(node), false),
        };
    }

    /// <summary><c>internal.Serialport.clearPreferences()</c>: answers whether there was anything to remove.</summary>
    public static JgsValue ClearPreferences()
    {
        DevicePreferences.Write("serialport", null);
        return JgsValue.Bool(true); // R2025b answers true whether or not there was anything to remove
    }

    // --- the lists -------------------------------------------------------------------------------------------

    /// <summary><c>serialportlist</c>, <c>serialportlist("all"|"available")</c>.</summary>
    public static JgsValue List(DeviceSession session, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        bool available = false;
        if (args.Count == 1)
        {
            string choice = DeviceChecks.ValidateString(args[0], ["all", "available"], new DeviceChecks.Subject("seriallist", "ports", 1), line, col);
            available = choice == "available";
        }

        var names = new List<string>();
        var held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DeviceObject device in session.Live)
        {
            if (device is SerialportObject serial)
            {
                held.Add(serial._port);
            }
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (string port in SerialPortList.All())
            {
                bool simulated = session.SimulatedFor(port) is not null || session.IsSimulatedPeer(port);
                if (simulated)
                {
                    continue;
                }

                if (!available || (!held.Contains(port) && SerialPortList.CanOpen(port)))
                {
                    names.Add(port);
                }
            }
        }

        foreach ((string name, bool free) in session.SimulatedPorts())
        {
            if (!available || free)
            {
                names.Add(name);
            }
        }

        names.Sort(NaturalOrder.Instance);
        return JgsValue.StringArray(names.Select(JgsValue.Str).ToArray());
    }

    /// <summary><c>serialportfind</c>, <c>serialportfind(Name, Value, …)</c>: [] when nothing matches.</summary>
    public static JgsValue Find(DeviceSession session, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "testmeaslib:ObjectCacher:NVPairsAsPairs",
                "Incorrect number of input arguments. Each parameter name must be followed by a corresponding value.");
        }

        var found = new List<DeviceObject>();
        foreach (DeviceObject device in session.Live)
        {
            if (device is not SerialportObject serial || serial.Deleted)
            {
                continue;
            }

            bool matches = true;
            for (int i = 0; i < args.Count && matches; i += 2)
            {
                string name = DeviceChecks.IsText(args[i]) ? DeviceChecks.Text(args[i]) : "";
                if (serial.Class.Find(name, ignoreCase: true) is not { Hidden: false } property)
                {
                    matches = false;
                    break;
                }

                JgsValue have = property.Get(serial, new DeviceCall { Target = serial, Args = [], Line = line, Column = col });
                matches = SameValue(have, args[i + 1]);
            }

            if (matches)
            {
                found.Add(serial);
            }
        }

        return found.Count switch
        {
            0 => JgsEmpty.Zero(),
            1 => JgsValue.External(found[0]),
            _ => JgsValue.External(new DeviceArray(found)),
        };
    }
}
