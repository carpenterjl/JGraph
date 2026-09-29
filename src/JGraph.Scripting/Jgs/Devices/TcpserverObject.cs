using JGraph.Devices;
using JGraph.Devices.Network;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// <c>tcpserver</c> (device classes plan, stage D2), transcribed from R2025b's <c>TCPServer.m</c> and its
/// <c>InputParser.m</c>: one client at a time, its bytes read through the shared client (a double row,
/// as GenericClient answers), a write refused while no client is connected, and
/// <c>ConnectionChangedFcn(src, evt)</c> with <c>tcpserver.internal.ConnectionInfo</c> when a client comes
/// or goes.
/// </summary>
internal sealed class TcpserverObject : DeviceObject
{
    internal static readonly TransportInterface Interface = new()
    {
        Name = "tcpserver",
        ObjectName = "t",
        SharedEventInfo = true,
        ConnectionLostText = "The connection to the client was lost.",
    };

    private static readonly DeviceClass Declaration = Declare();

    private readonly TcpServerTransport _server;
    private readonly TransportClient _client;
    private readonly DeviceEventQueue _queue;
    private JgsValue? _connectionChanged;
    private string _byteOrder = "little-endian";
    private string _tag = "";

    private TcpserverObject(DeviceSession session, Interpreter interpreter, TcpServerTransport server)
        : base(session, interpreter)
    {
        _server = server;
        _client = new TransportClient(server, Interface, this) { ConnectionLostMessage = Interface.ConnectionLostText };
        _queue = DeviceEventQueue.ForCurrentThread();
        server.ConnectionChanged += (connected, address, port) =>
        {
            DateTime at = DateTime.Now;
            _queue.Post(() => FireConnectionChanged(connected, address, port, at));
        };
    }

    public override DeviceClass Class => Declaration;

    public override string? Summary() => Deleted ? "deleted" : $"{_server.ServerAddress}:{_server.ServerPort}";

    private static JgsRuntimeException Cannot(int line, int col, string message) =>
        new(line, col, "instrument:interface:tcpserver:cannotCreateObject", message);

    /// <summary><c>t = tcpserver(port)</c>, <c>tcpserver(address, port)</c>, <c>tcpserver(address, port, Name=Value…)</c>.</summary>
    public static JgsValue Create(DeviceSession session, Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        IReadOnlyList<JgsValue> a = args.Select(TransportClient.Str2Char).ToArray();
        string? address;
        JgsValue port;
        List<JgsValue> nv;
        try
        {
            if (a.Count == 0)
            {
                throw Cannot(line, col, "Invalid number of input arguments for 'tcpserver'. Valid syntaxes are\nt = tcpserver(SERVERPORT)\nt = tcpserver(SERVERADDRESS,SERVERPORT)\nt = tcpserver(SERVERADDRESS,SERVERPORT,NAME,VALUE)");
            }

            if (a.Count == 1)
            {
                (address, port, nv) = (null, a[0], []);
            }
            else if (a.Count == 2)
            {
                if (IsNumeric(a[0]) && a[1].Type == JgsType.String)
                {
                    throw Cannot(line, col, "Incorrect number of input arguments. Each parameter name must be followed by a corresponding value.");
                }

                (address, port, nv) = (Address(a[0], line, col), a[1], []);
            }
            else if (IsNumeric(a[0]))
            {
                (address, port, nv) = (null, a[0], a.Skip(1).ToList());
            }
            else
            {
                (address, port, nv) = (Address(a[0], line, col), a[1], a.Skip(2).ToList());
            }

            var portWho = new DeviceChecks.Subject("tcpserver", "SERVERPORT");
            DeviceChecks.Classes(port, ["numeric"], portWho, line, col);
            DeviceChecks.Attributes(port, ["ge:1", "le:65535", "nonempty", "scalar", "integer"], portWho, line, col);
            if (nv.Count % 2 != 0)
            {
                throw Cannot(line, col, "Incorrect number of input arguments. Each parameter name must be followed by a corresponding value.");
            }
        }
        catch (JgsRuntimeException e) when (e.Identifier != "instrument:interface:tcpserver:cannotCreateObject")
        {
            throw Cannot(line, col, e.Message);
        }

        var options = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        try
        {
            for (int i = 0; i < nv.Count; i += 2)
            {
                string written = DeviceChecks.IsText(nv[i]) ? DeviceChecks.Text(nv[i]) : "";
                if (!DeviceChecks.Match(written, ["Timeout", "ByteOrder", "ConnectionChangedFcn", "Tag"], out string? name, out _))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:InputParser:UnmatchedParameter",
                        $"'{written}' is not a recognized parameter. For a list of valid name-value pair arguments, see the documentation for this function.");
                }

                options[name!] = nv[i + 1];
            }
        }
        catch (JgsRuntimeException e)
        {
            throw Cannot(line, col, e.Message);
        }

        TcpServerTransport server;
        try
        {
            server = TcpServerTransport.Listen(address, (int)DeviceChecks.Numbers(port).First());
        }
        catch (DeviceOpenException e)
        {
            throw new JgsRuntimeException(line, col, "instrument:interface:tcpserver:cannotConnect",
                e.Message.TrimEnd('.') + "\nSee <a href=\"matlab: helpview('instrument', 'tcpserver_connectError')\">related documentation</a> for troubleshooting steps.");
        }

        var made = new TcpserverObject(session, interpreter, server);
        var call = new DeviceCall { Target = made, Args = [], Line = line, Column = col };
        try
        {
            if (options.TryGetValue("Timeout", out JgsValue? timeout))
            {
                made._client.Timeout = TcpclientObject.CheckTimeout(timeout, call);
            }

            if (options.TryGetValue("ByteOrder", out JgsValue? order))
            {
                made._byteOrder = TcpclientObject.ByteOrderOf(order, call);
                made._client.BigEndian = made._byteOrder == "big-endian";
            }

            if (options.TryGetValue("ConnectionChangedFcn", out JgsValue? fcn))
            {
                made.SetConnectionChanged(fcn, call);
            }

            if (options.TryGetValue("Tag", out JgsValue? tag))
            {
                made._tag = NetworkShared.TagOf(tag, call);
            }
        }
        catch (JgsRuntimeException e)
        {
            server.Dispose();
            throw Cannot(line, col, e.Message);
        }

        session.Remember(made);
        JgsValue value = JgsValue.External(made);
        JgsLifetime.Minted(value);
        return value;
    }

    private static bool IsNumeric(JgsValue value) => Array.IndexOf(DeviceChecks.NumericClasses, DeviceChecks.ClassOf(value)) >= 0;

    private static string Address(JgsValue value, int line, int col)
    {
        var who = new DeviceChecks.Subject("tcpserver", "SERVERADDRESS", 1);
        DeviceChecks.Classes(value, ["char", "string"], who, line, col);
        DeviceChecks.Attributes(value, ["nonempty"], who, line, col);
        return DeviceChecks.Text(value);
    }

    private void SetConnectionChanged(JgsValue value, DeviceCall call)
    {
        if (value.Type == JgsType.Function)
        {
            _connectionChanged = value;
        }
        else if (DeviceChecks.Count(value) == 0 && value.Type is JgsType.Array or JgsType.Null)
        {
            _connectionChanged = null;
        }
        else
        {
            throw call.Error("instrument:interface:tcpserver:InvalidConnectionChangedFcn", "ConnectionChangedFcn must be a function handle.");
        }
    }

    private void FireConnectionChanged(bool connected, string address, int port, DateTime at)
    {
        if (Deleted || _connectionChanged is not { } callback)
        {
            return;
        }

        var info = new TransportClient.SharedEventInfo(Session, Interpreter, "tcpserver.internal.ConnectionInfo", "ConnectionInfo",
        [
            ("Connected", JgsValue.Bool(connected)),
            ("ClientAddress", connected ? JgsValue.StringScalar(address) : JgsValue.Str("")),
            ("ClientPort", connected ? JgsValue.Number(port) : JgsEmpty.Zero()),
            ("AbsoluteTime", JgsBuiltins.DatetimeValue(at)),
            // R2025b's Source is the server's internal event handler, not the server (probe_net_tcp).
            ("Source", JgsValue.External(new TransportClient.SharedEventInfo(Session, Interpreter, "tcpserver.internal.EventHandler", "EventHandler", []))),
            ("EventName", JgsValue.Str("ConnectionInfo")),
        ]);
        try
        {
            JgsCallbacks.Invoke(callback.AsCallable, [JgsValue.External(this), JgsValue.External(info)], 0, 0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JgsException failure)
        {
            JgsBuiltins.Warn(Session.Host, "MATLAB:callback:error", "Error executing the ConnectionChangedFcn callback:\n" + failure.Message);
        }
    }

    private static TcpserverObject Me(DeviceObject o) => (TcpserverObject)o;

    private static DeviceClass Declare()
    {
        static JgsRuntimeException ReadOnly(DeviceCall call, string name, string function) =>
            call.Error("transportlib:client:ReadOnlyProperty", $"To set \"{name}\", use the \"{function}\" function.");

        var properties = new List<DeviceProperty>
        {
            new("ServerAddress", static (o, _) => JgsValue.StringScalar(Me(o)._server.ServerAddress)),
            new("ServerPort", static (o, _) => JgsValue.Number(Me(o)._server.ServerPort)),
            new("Connected", static (o, _) => JgsValue.Bool(Me(o)._server.Connected)),
            new("ClientAddress", static (o, _) => JgsValue.StringScalar(Me(o)._server.Client.Address)),
            new("ClientPort", static (o, _) => Me(o)._server.Connected ? JgsValue.Number(Me(o)._server.Client.Port) : JgsEmpty.Zero()),
            new("NumBytesAvailable", static (o, _) => JgsValue.Number(Me(o)._client.NumBytesAvailable)),
            new("NumBytesWritten", static (o, _) => JgsValue.Number(Me(o)._client.NumBytesWritten)),
            new("Timeout", static (o, _) => Me(o)._client.Timeout, static (o, v, c) => Me(o)._client.Timeout = TcpclientObject.CheckTimeout(v, c)),
            new("ByteOrder", static (o, _) => JgsValue.StringScalar(Me(o)._byteOrder), static (o, v, c) =>
            {
                Me(o)._byteOrder = TcpclientObject.ByteOrderOf(v, c);
                Me(o)._client.BigEndian = Me(o)._byteOrder == "big-endian";
            }),
            new("Terminator", static (o, _) => Me(o)._client.TerminatorValue, static (_, _, c) => throw ReadOnly(c, "Terminator", "configureTerminator")),
            new("BytesAvailableFcnMode", static (o, _) => JgsValue.StringScalar(Me(o)._client.BytesAvailableFcnMode),
                static (_, _, c) => throw ReadOnly(c, "BytesAvailableFcnMode", "configureCallback")),
            new("BytesAvailableFcnCount", static (o, _) => JgsValue.Number(Me(o)._client.BytesAvailableFcnCount),
                static (_, _, c) => throw ReadOnly(c, "BytesAvailableFcnCount", "configureCallback")),
            new("BytesAvailableFcn", static (o, _) => Me(o)._client.BytesAvailableFcn ?? JgsEmpty.Zero(),
                static (_, _, c) => throw ReadOnly(c, "BytesAvailableFcn", "configureCallback")),
            new("ErrorOccurredFcn", static (o, _) => Me(o)._client.ErrorOccurredFcn ?? JgsEmpty.Zero(),
                static (o, v, c) => Me(o)._client.ErrorOccurredFcn = Me(o)._client.CallbackValue(v, "InvalidErrorOccurredFcn", "ErrorOccurredFcn must be a function handle.", c)),
            new("UserData", static (o, _) => Me(o)._client.UserData, static (o, v, _) => Me(o)._client.UserData = v),
            new("ConnectionChangedFcn", static (o, _) => Me(o)._connectionChanged ?? JgsEmpty.Zero(), static (o, v, c) => Me(o).SetConnectionChanged(v, c)),
            new("Tag", static (o, _) => JgsValue.StringScalar(Me(o)._tag), static (o, v, c) => Me(o)._tag = NetworkShared.TagOf(v, c)),
        };

        Dictionary<string, DeviceMethodBody> methods = NetworkShared.ClientMethods(static o => Me(o)._client, withBinblock: true,
            mayWrite: static c => Me(c.Target)._server.Connected);
        methods.Remove("writeread");
        return new DeviceClass("tcpserver.internal.TCPServer", "TCPServer",
            ["matlabshared.testmeas.internal.SetGet", "matlab.mixin.SetGet", "handle", "matlabshared.testmeas.CustomDisplay",
             "matlab.mixin.CustomDisplay", "matlabshared.testmeas.internal.mixins.CacheEnabler", "matlabshared.transportlib.internal.TagAccessor"],
            properties,
            methods,
            ["TCPServer", "addlistener", "configureCallback", "configureTerminator", "delete", "eq", "findobj", "findprop", "flush", "ge",
             "get", "gt", "isvalid", "le", "listener", "lt", "ne", "notify", "read", "readbinblock", "readline", "set", "write",
             "writebinblock", "writeline"],
            ["ServerAddress", "ServerPort", "Connected", "ClientAddress", "ClientPort", "Tag", "NumBytesAvailable"]);
    }

    protected override void OnDelete() => _server.Dispose();
}
