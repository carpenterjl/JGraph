using System.Net.Sockets;
using JGraph.Devices;
using JGraph.Devices.Network;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// <c>tcpclient</c> (device classes plan, stage D2), transcribed from R2025b's <c>tcpclient.m</c> and
/// <c>TCPCustomClient.m</c>: every refusal in construction is <c>cannotCreateObject</c> with the cause's
/// message; <c>read</c> answers in the precision's own class, with no partial reads; <c>write</c> without
/// a precision writes the data's own class; the rest is the shared client with no identifier renaming.
/// </summary>
internal sealed class TcpclientObject : DeviceObject
{
    internal static readonly TransportInterface Interface = new()
    {
        Name = "tcpclient",
        ObjectName = "t",
        NativeReads = true,
        ReadErrorWrap = "MATLAB:networklib:tcpclient:readFailed",
        WriteErrorWrap = "MATLAB:networklib:tcpclient:writeFailed",
        WriteInDataClass = true,
        ReadSyntax = "DATA = read({0})\nDATA = read({0},COUNT)\nDATA = read({0},COUNT,PRECISION)",
        SharedEventInfo = true,
        ConnectionLostText = "The connection to the remote server was lost.",
    };

    private static readonly DeviceClass Declaration = Declare();

    private readonly string _address;
    private readonly double _port;
    private readonly JgsValue _connectTimeout;
    private readonly bool _transferDelay;
    private TransportClient? _client;
    private string _byteOrder = "little-endian";
    private string _tag = "";

    private TcpclientObject(DeviceSession session, Interpreter interpreter, string address, double port, JgsValue connectTimeout, bool transferDelay)
        : base(session, interpreter)
    {
        _address = address;
        _port = port;
        _connectTimeout = connectTimeout;
        _transferDelay = transferDelay;
    }

    public override DeviceClass Class => Declaration;

    private TransportClient Client => _client!;

    public override string? Summary() => Deleted ? "deleted" : $"{_address}:{_port}";

    private static JgsRuntimeException Cannot(int line, int col, string message) =>
        new(line, col, "MATLAB:networklib:tcpclient:cannotCreateObject", message);

    /// <summary><c>t = tcpclient(address, port)</c>, <c>tcpclient(address, port, Name=Value…)</c>.</summary>
    public static JgsValue Create(DeviceSession session, Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        if (args.Count == 1)
        {
            throw Cannot(line, col, "Not enough input arguments.");
        }

        try
        {
            JgsValue address = TransportClient.Str2Char(args[0]);
            var addressWho = new DeviceChecks.Subject("tcpclient", "ADDRESS", 1);
            DeviceChecks.Classes(address, ["char"], addressWho, line, col);
            DeviceChecks.Attributes(address, ["nonempty"], addressWho, line, col);
            var portWho = new DeviceChecks.Subject("tcpclient", "PORT", 2);
            DeviceChecks.Classes(args[1], ["numeric"], portWho, line, col);
            DeviceChecks.Attributes(args[1], ["ge:1", "le:65535", "scalar"], portWho, line, col);
            List<JgsValue> nv = args.Skip(2).Select(TransportClient.Str2Char).ToList();
            if (nv.Count % 2 != 0)
            {
                throw Cannot(line, col, "The input arguments contain unmatched set of parameter name-value pairs.");
            }

            var given = ParseOptions(nv, line, col);
            JgsValue timeout = given.TryGetValue("Timeout", out JgsValue? t) ? t : JgsValue.Number(20);
            JgsValue connectTimeout = given.TryGetValue("ConnectTimeout", out JgsValue? ct) ? ct : JgsValue.Number(double.PositiveInfinity);
            bool delay = !given.TryGetValue("EnableTransferDelay", out JgsValue? d) || d.AsNumber != 0;
            var client = new TcpclientObject(session, interpreter, DeviceChecks.Text(address), DeviceChecks.Numbers(args[1]).First(), connectTimeout, delay);
            var call = new DeviceCall { Target = client, Args = [], Line = line, Column = col };
            JgsValue checkedTimeout = CheckTimeout(timeout, call);
            if (given.TryGetValue("ByteOrder", out JgsValue? order))
            {
                client._byteOrder = ByteOrderOf(order, call);
            }

            if (given.TryGetValue("Tag", out JgsValue? tag))
            {
                client._tag = DeviceChecks.Text(tag);
            }

            client.Connect(checkedTimeout, call);
            session.Remember(client);
            JgsValue made = JgsValue.External(client);
            JgsLifetime.Minted(made);
            return made;
        }
        catch (JgsRuntimeException e) when (e.Identifier != "MATLAB:networklib:tcpclient:cannotCreateObject")
        {
            throw Cannot(line, col, e.Message);
        }
    }

    private static readonly string[] Options = ["Timeout", "ConnectTimeout", "EnableTransferDelay", "ByteOrder", "Tag"];

    /// <summary>tcpclient's inputParser: partial and case-blind, with its validators' refusals.</summary>
    private static Dictionary<string, JgsValue> ParseOptions(List<JgsValue> nv, int line, int col)
    {
        var given = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        for (int i = 0; i < nv.Count; i += 2)
        {
            string written = DeviceChecks.IsText(nv[i]) ? DeviceChecks.Text(nv[i]) : "";
            if (!DeviceChecks.Match(written, Options, out string? name, out _))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:UnmatchedParameter",
                    $"'{written}' is not a recognized parameter. For a list of valid name-value pair arguments, see the documentation for this function.");
            }

            JgsValue value = nv[i + 1];
            string? failure = name switch
            {
                "Timeout" or "ConnectTimeout" => Array.IndexOf(DeviceChecks.NumericClasses, DeviceChecks.ClassOf(value)) >= 0 ? null : "isnumeric",
                "EnableTransferDelay" => DeviceChecks.ClassOf(value) == "logical" && DeviceChecks.Count(value) == 1 ? null : "@(x)islogical(x)&&isscalar(x)",
                "Tag" => DeviceChecks.ClassOf(value) is "char" or "string" ? null : "@(x)isstring(x)||ischar(x)",
                _ => null,
            };
            if (failure is not null)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ArgumentFailedValidation",
                    $"The value of '{name}' is invalid. It must satisfy the function: {failure}.");
            }

            given[name!] = value;
        }

        return given;
    }

    private void Connect(JgsValue timeout, DeviceCall call)
    {
        double seconds = DeviceChecks.Numbers(_connectTimeout).First();
        TimeSpan connectTimeout = double.IsFinite(seconds) ? TimeSpan.FromSeconds(Math.Max(0.001, seconds)) : TimeSpan.FromSeconds(60);
        TcpTransport transport;
        try
        {
            transport = TcpTransport.Connect(_address, (int)_port, connectTimeout, _transferDelay, Interpreter.Cancellation);
        }
        catch (DeviceOpenException e)
        {
            // A connect a finite ConnectTimeout cuts short reports the socket option R2025b reads from
            // the closed socket afterwards (Windows retries a refused loopback connect for about two
            // seconds, so ConnectTimeout 1 on a closed port lands here in both engines).
            string reason = e.Win32Error switch
            {
                10060 when double.IsFinite(seconds) => "get_option: The file handle supplied is not valid",
                (int)SocketError.ConnectionRefused => "No connection could be made because the target machine actively refused it",
                (int)SocketError.HostNotFound or (int)SocketError.NoData or (int)SocketError.TryAgain => "resolve: No such host is known",
                10060 => "Operation timed out",
                _ => e.Message,
            };
            throw call.Error("MATLAB:networklib:tcpclient:cannotCreateObject",
                "Cannot create a communication link with the remote server. Please check the input arguments(ADDRESS and PORT) and make sure the server is running.\n"
                + $"Additional Information: {reason}\nSee <a href=\"matlab: helpview('matlab', 'tcpclient_connectError')\">related documentation</a> for troubleshooting steps.");
        }

        _client = new TransportClient(transport, Interface, this)
        {
            ConnectionLostMessage = Interface.ConnectionLostText,
            Timeout = timeout,
            BigEndian = _byteOrder == "big-endian",
        };
    }

    /// <summary>The transport's Timeout check under tcpclient's names: MATLAB:TCPClient:*, then a double or a single.</summary>
    internal static JgsValue CheckTimeout(JgsValue value, DeviceCall call)
    {
        var who = new DeviceChecks.Subject("TCPClient", "TIMEOUT");
        DeviceChecks.Classes(value, ["numeric"], who, call.Line, call.Column);
        DeviceChecks.Attributes(value, ["scalar", "nonnegative", "nonnan"], who, call.Line, call.Column);
        if (DeviceChecks.ClassOf(value) is not ("double" or "single"))
        {
            throw call.Error("Stream:timeout:invalidTime", "Timeout must be a non-negative scalar double.");
        }

        return value;
    }

    /// <summary>ByteOrder through the client's setProperty: validatestring's words under transportlib:client:InvalidType.</summary>
    internal static string ByteOrderOf(JgsValue value, DeviceCall call)
    {
        try
        {
            return DeviceChecks.ValidateString(TransportClient.Str2Char(value), ["little-endian", "big-endian"], new DeviceChecks.Subject(null, null), call.Line, call.Column);
        }
        catch (JgsRuntimeException e)
        {
            throw call.Error("transportlib:client:InvalidType", e.Message);
        }
    }

    private static TcpclientObject Me(DeviceObject o) => (TcpclientObject)o;

    private static DeviceClass Declare()
    {
        static JgsRuntimeException ReadOnly(DeviceCall call, string name, string function) =>
            call.Error("transportlib:client:ReadOnlyProperty", $"To set \"{name}\", use the \"{function}\" function.");

        var properties = new List<DeviceProperty>
        {
            new("Address", static (o, _) => JgsValue.Str(Me(o)._address)),
            new("Port", static (o, _) => JgsValue.Number(Me(o)._port)),
            new("NumBytesAvailable", static (o, _) => JgsValue.Number(Me(o).Client.NumBytesAvailable)),
            new("NumBytesWritten", static (o, _) => JgsValue.Number(Me(o).Client.NumBytesWritten)),
            new("ConnectTimeout", static (o, _) => Me(o)._connectTimeout),
            new("EnableTransferDelay", static (o, _) => JgsValue.Bool(Me(o)._transferDelay)),
            new("Timeout", static (o, _) => Me(o).Client.Timeout, static (o, v, c) => Me(o).Client.Timeout = CheckTimeout(v, c)),
            new("UserData", static (o, _) => Me(o).Client.UserData, static (o, v, _) => Me(o).Client.UserData = v),
            new("ByteOrder", static (o, _) => JgsValue.StringScalar(Me(o)._byteOrder), static (o, v, c) =>
            {
                Me(o)._byteOrder = ByteOrderOf(v, c);
                Me(o).Client.BigEndian = Me(o)._byteOrder == "big-endian";
            }),
            new("Terminator", static (o, _) => Me(o).Client.TerminatorValue, static (_, _, c) => throw ReadOnly(c, "Terminator", "configureTerminator")),
            new("BytesAvailableFcn", static (o, _) => Me(o).Client.BytesAvailableFcn ?? JgsEmpty.Zero(),
                static (_, _, c) => throw ReadOnly(c, "BytesAvailableFcn", "configureCallback")),
            new("BytesAvailableFcnCount", static (o, _) => JgsValue.Number(Me(o).Client.BytesAvailableFcnCount),
                static (_, _, c) => throw ReadOnly(c, "BytesAvailableFcnCount", "configureCallback")),
            new("BytesAvailableFcnMode", static (o, _) => JgsValue.StringScalar(Me(o).Client.BytesAvailableFcnMode),
                static (_, _, c) => throw ReadOnly(c, "BytesAvailableFcnMode", "configureCallback")),
            new("ErrorOccurredFcn", static (o, _) => Me(o).Client.ErrorOccurredFcn ?? JgsEmpty.Zero(),
                static (o, v, c) => Me(o).Client.ErrorOccurredFcn = Me(o).Client.CallbackValue(v, "InvalidErrorOccurredFcn", "ErrorOccurredFcn must be a function handle.", c)),
            new("Tag", static (o, _) => JgsValue.StringScalar(Me(o)._tag), static (o, v, c) => Me(o)._tag = NetworkShared.TagOf(v, c)),
        };

        return new DeviceClass("tcpclient", "tcpclient",
            ["matlabshared.testmeas.internal.SetGet", "matlab.mixin.SetGet", "handle", "matlabshared.testmeas.CustomDisplay",
             "matlab.mixin.CustomDisplay", "matlabshared.testmeas.internal.mixins.CacheEnabler", "matlabshared.transportlib.internal.TagAccessor"],
            properties,
            NetworkShared.ClientMethods(static o => Me(o).Client, withBinblock: true),
            ["addlistener", "configureCallback", "configureTerminator", "delete", "eq", "findobj", "findprop", "flush", "ge", "get", "gt",
             "isvalid", "le", "listener", "lt", "matlabCodegenRedirect", "ne", "notify", "read", "readbinblock", "readline", "set",
             "tcpclient", "write", "writebinblock", "writeline", "writeread"],
            ["Address", "Port", "Tag", "NumBytesAvailable"]);
    }

    protected override void OnDelete() => _client?.Transport.Dispose();
}

/// <summary>What the network objects share: the shared client's methods, Tag's check, the *find functions.</summary>
internal static class NetworkShared
{
    /// <summary>The shared client's methods for an object whose client <paramref name="client"/> reads.</summary>
    public static Dictionary<string, DeviceMethodBody> ClientMethods(Func<DeviceObject, TransportClient> client, bool withBinblock,
        bool withLines = true, Func<DeviceCall, bool>? mayWrite = null)
    {
        TransportClient Live(DeviceCall call)
        {
            call.Target.LiveOrThrow(call.Line, call.Column);
            return client(call.Target);
        }

        void WriteGuard(DeviceCall call)
        {
            if (mayWrite is not null && !mayWrite(call))
            {
                throw call.Error("instrument:interface:tcpserver:NotConnectedWrite", "Failed to write from the server. A TCP/IP client must be connected to the server.");
            }
        }

        var methods = new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["read"] = c => [Live(c).Read(c)],
            ["write"] = c => Void(c, c =>
            {
                WriteGuard(c);
                Live(c).Write(c);
            }),
            ["configureCallback"] = c => Void(c, c => Live(c).ConfigureCallback(c)),
            ["flush"] = c => Void(c, c => Live(c).Flush(c)),
        };
        if (withLines)
        {
            methods["readline"] = c => [Live(c).ReadLine(c)];
            methods["writeline"] = c => Void(c, c =>
            {
                WriteGuard(c);
                Live(c).WriteLine(c);
            });
            methods["configureTerminator"] = c => Void(c, c => Live(c).ConfigureTerminator(c));
        }

        if (withBinblock)
        {
            methods["readbinblock"] = c => [Live(c).ReadBinblock(c)];
            methods["writebinblock"] = c => Void(c, c =>
            {
                WriteGuard(c);
                Live(c).WriteBinblock(c);
            });
            methods["writeread"] = c => [Live(c).WriteRead(c)];
        }

        return methods;
    }

    public static JgsValue[] Void(DeviceCall call, Action<DeviceCall> body)
    {
        if (call.Wanted > 0)
        {
            throw call.Error("MATLAB:TooManyOutputs", "Too many output arguments.");
        }

        body(call);
        return [];
    }

    /// <summary>TagAccessor's check of Tag.</summary>
    public static string TagOf(JgsValue value, DeviceCall call)
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

        return DeviceChecks.Text(value);
    }

    /// <summary><c>tcpclientfind</c>, <c>tcpserverfind</c>, <c>udpportfind</c>: the live objects of a class whose properties match.</summary>
    public static JgsValue Find(DeviceSession session, Func<DeviceObject, bool> ofKind, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "testmeaslib:ObjectCacher:NVPairsAsPairs",
                "Incorrect number of input arguments. Each parameter name must be followed by a corresponding value.");
        }

        var found = new List<DeviceObject>();
        foreach (DeviceObject device in session.Live)
        {
            if (!ofKind(device) || device.Deleted)
            {
                continue;
            }

            bool matches = true;
            for (int i = 0; i < args.Count && matches; i += 2)
            {
                string name = DeviceChecks.IsText(args[i]) ? DeviceChecks.Text(args[i]) : "";
                if (device.Class.Find(name, ignoreCase: true) is not { Hidden: false } property)
                {
                    matches = false;
                    break;
                }

                matches = SerialportObject.SameValue(property.Get(device, new DeviceCall { Target = device, Args = [], Line = line, Column = col }), args[i + 1]);
            }

            if (matches)
            {
                found.Add(device);
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
