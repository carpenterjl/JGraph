using System.Net;
using System.Net.Sockets;
using JGraph.Devices;
using JGraph.Devices.Network;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// <c>udpport</c> (device classes plan, stage D2), after R2025b's <c>udpport.m</c>, its
/// <c>InputParser.m</c> and the byte and datagram <c>UDPPort</c> classes: byte mode reads the datagrams'
/// bytes through the shared client; datagram mode reads whole datagrams as
/// <c>udpport.datagram.Datagram</c> values (Data, SenderAddress, SenderPort). A write names a destination
/// or goes to the last one named; <c>configureMulticast</c> joins and leaves a group.
/// </summary>
internal sealed class UdpportObject : DeviceObject
{
    internal static readonly TransportInterface Interface = new()
    {
        Name = "udpport",
        ObjectName = "u",
        ErrorPrefix = "instrument:interface:udpport:",
        TransportlibIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "IncorrectInputArgumentsSingular", "IncorrectInputArgumentsPlural",
        },
        SharedEventInfo = true,
    };

    /// <summary>Datagram mode's: the same, but its flush calls the buffer BUFFER (probe_net_echo).</summary>
    internal static readonly TransportInterface DatagramInterface = new()
    {
        Name = "udpport",
        ObjectName = "u",
        ErrorPrefix = "instrument:interface:udpport:",
        TransportlibIds = Interface.TransportlibIds,
        SharedEventInfo = true,
        FlushBufferWord = "BUFFER",
    };

    internal static readonly string[] NameValueNames = ["LocalHost", "LocalPort", "Timeout", "ByteOrder", "OutputDatagramSize", "EnablePortSharing", "Tag"];

    private static readonly DeviceClass ByteDeclaration = Declare(datagram: false);
    private static readonly DeviceClass DatagramDeclaration = Declare(datagram: true);

    private readonly UdpTransport _udp;
    private readonly TransportClient _client;
    private readonly bool _datagram;
    private readonly bool _ipv6;
    private readonly bool _portSharing;
    private readonly DeviceEventQueue _queue;
    private string _byteOrder = "little-endian";
    private string _tag = "";
    private string _group = "";
    private string _datagramMode = "off";
    private double _datagramCount = 1;
    private JgsValue? _datagramFcn;
    private long _datagramCounter;
    private long _datagramsWritten;

    private UdpportObject(DeviceSession session, Interpreter interpreter, UdpTransport udp, bool datagram, bool ipv6, bool portSharing)
        : base(session, interpreter)
    {
        _udp = udp;
        _datagram = datagram;
        _ipv6 = ipv6;
        _portSharing = portSharing;
        _client = new TransportClient(udp, datagram ? DatagramInterface : Interface, this);
        _queue = DeviceEventQueue.ForCurrentThread();
        udp.DatagramReceived += _ =>
        {
            int events = 0;
            lock (_udp)
            {
                if (_datagramMode == "datagram")
                {
                    _datagramCounter++;
                    long step = (long)Math.Max(1, _datagramCount);
                    while (_datagramCounter >= step)
                    {
                        _datagramCounter -= step;
                        events++;
                    }
                }
            }

            DateTime at = DateTime.Now;
            for (int i = 0; i < events; i++)
            {
                _queue.Post(() => FireDatagrams(at));
            }
        };
    }

    public override DeviceClass Class => _datagram ? DatagramDeclaration : ByteDeclaration;

    public override string? Summary() => Deleted ? "deleted" : $"{_udp.LocalHost}:{_udp.LocalPort}";

    /// <summary><c>u = udpport</c>, <c>udpport("byte"|"datagram")</c>, <c>udpport(mode, "IPV4"|"IPV6", Name=Value…)</c>.</summary>
    public static JgsValue Create(DeviceSession session, Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        List<JgsValue> a = args.Select(TransportClient.Str2Char).ToList();
        string mode = "byte";
        string address = "IPV4";
        int first = a.Count;
        if (a.Count >= 1)
        {
            string[] choices = ["byte", "datagram", "IPV4", "IPV6", .. NameValueNames];
            string head = DeviceChecks.ValidateString(a[0], choices, new DeviceChecks.Subject(null, null, 1), line, col);
            bool firstIsName = Array.IndexOf(NameValueNames, head) >= 0;
            mode = head == "datagram" ? "datagram" : "byte";
            if (firstIsName)
            {
                first = 0;
                a[0] = JgsValue.Str(head);
            }
            else if (head is "IPV4" or "IPV6")
            {
                address = head;
                first = 1;
            }
            else
            {
                first = 1;
            }

            if (a.Count == 1 && firstIsName)
            {
                throw new JgsRuntimeException(line, col, "instrument:interface:udpport:UnmatchedPVPairs",
                    "Incorrect number of input arguments. Each parameter name must be followed by a corresponding value.");
            }

            if (a.Count >= 2 && !firstIsName)
            {
                if (a[1].Type != JgsType.String)
                {
                    throw new JgsRuntimeException(line, col, "instrument:interface:udpport:IncorrectSecondArgument", "Invalid second argument.");
                }

                if (DeviceChecks.Match(a[1].AsString, ["IPV4", "IPV6"], out string? version, out _) && head is "byte" or "datagram")
                {
                    address = version!;
                    first = 2;
                }
                else
                {
                    string name = DeviceChecks.ValidateString(a[1], NameValueNames, new DeviceChecks.Subject(null, null, 2), line, col);
                    a[1] = JgsValue.Str(name);
                    first = 1;
                }
            }
        }

        List<JgsValue> nv = a.Skip(first).ToList();
        if (nv.Count % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "instrument:interface:udpport:UnmatchedPVPairs",
                "Incorrect number of input arguments. Each parameter name must be followed by a corresponding value.");
        }

        var options = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        for (int i = 0; i < nv.Count; i += 2)
        {
            string written = DeviceChecks.IsText(nv[i]) ? DeviceChecks.Text(nv[i]) : "";
            if (!DeviceChecks.Match(written, NameValueNames, out string? name, out _))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:UnmatchedParameter",
                    $"'{written}' is not a recognized parameter. For a list of valid name-value pair arguments, see the documentation for this function.");
            }

            options[name!] = nv[i + 1];
        }

        string localHost = options.TryGetValue("LocalHost", out JgsValue? host) && DeviceChecks.IsText(host) ? DeviceChecks.Text(host) : "";
        double localPort = options.TryGetValue("LocalPort", out JgsValue? port) ? DeviceChecks.Numbers(port).FirstOrDefault() : 0;
        bool sharing = options.TryGetValue("EnablePortSharing", out JgsValue? share) && share.AsNumber != 0;
        UdpTransport udp;
        try
        {
            if (localPort is < 0 or > 65535 || localPort != Math.Floor(localPort))
            {
                throw new DeviceOpenException("bad port");
            }

            udp = UdpTransport.Open(address == "IPV6", localHost, (int)localPort, sharing, mode == "datagram");
        }
        catch (DeviceOpenException)
        {
            throw new JgsRuntimeException(line, col, "instrument:interface:udpport:ConnectFailed",
                $"Unable to bind to LOCALHOST and LOCALPORT. Verify that the LOCALPORT is not in use, and that LOCALHOST and LOCALPORT are valid for IPADDRESSVERSION \"{address}\".\n"
                + "See <a href=\"matlab: helpview('instrument', 'udpport_connectError')\">related documentation</a> for troubleshooting steps.");
        }

        var made = new UdpportObject(session, interpreter, udp, mode == "datagram", address == "IPV6", sharing);
        var call = new DeviceCall { Target = made, Args = [], Line = line, Column = col };
        try
        {
            if (options.TryGetValue("Timeout", out JgsValue? timeout))
            {
                made.SetTimeout(timeout, call);
            }

            if (options.TryGetValue("ByteOrder", out JgsValue? order))
            {
                made._byteOrder = TcpclientObject.ByteOrderOf(order, call);
                made._client.BigEndian = made._byteOrder == "big-endian";
            }

            if (options.TryGetValue("OutputDatagramSize", out JgsValue? size))
            {
                made.SetOutputSize(size, call);
            }

            if (options.TryGetValue("Tag", out JgsValue? tag))
            {
                made._tag = NetworkShared.TagOf(tag, call);
            }
        }
        catch
        {
            udp.Dispose();
            throw;
        }

        session.Remember(made);
        JgsValue value = JgsValue.External(made);
        JgsLifetime.Minted(value);
        return value;
    }

    private void SetTimeout(JgsValue value, DeviceCall call)
    {
        try
        {
            var who = new DeviceChecks.Subject(null, "TIMEOUT");
            DeviceChecks.Classes(value, ["double"], who, call.Line, call.Column);
            DeviceChecks.Attributes(value, ["positive"], who, call.Line, call.Column);
        }
        catch (JgsRuntimeException e)
        {
            throw call.Error("instrument:interface:udpport:InvalidEntry", e.Message);
        }

        _client.Timeout = value;
    }

    private void SetOutputSize(JgsValue value, DeviceCall call)
    {
        try
        {
            var who = new DeviceChecks.Subject(null, "OUTPUTDATAGRAMSIZE");
            DeviceChecks.Classes(value, ["numeric"], who, call.Line, call.Column);
            DeviceChecks.Attributes(value, ["scalar", "integer", "nonnan", "finite", "positive", "le:65507"], who, call.Line, call.Column);
        }
        catch (JgsRuntimeException e)
        {
            throw call.Error("instrument:interface:udpport:InvalidEntry", e.Message);
        }

        _udp.OutputDatagramSize = (int)DeviceChecks.Numbers(value).First();
    }

    private void SetFlag(JgsValue value, DeviceCall call, Action<bool> apply)
    {
        try
        {
            DeviceChecks.Classes(value, ["logical"], new DeviceChecks.Subject(null, "FLAG"), call.Line, call.Column);
        }
        catch (JgsRuntimeException e)
        {
            throw call.Error("instrument:interface:udpport:InvalidEntry", e.Message);
        }

        apply(value.AsNumber != 0);
    }

    private static JgsRuntimeException ReadOnlyEntry(DeviceCall call, string name) =>
        call.Error("instrument:interface:udpport:ReadOnly", $"To set \"{name}\", use name-value pair arguments in the udpport constructor.");

    // --- writes -----------------------------------------------------------------------------------------

    /// <summary>
    /// <c>write(u, data)</c>, <c>(u, data, precision)</c>, <c>(u, data, address, port)</c> and
    /// <c>(u, data, precision, address, port)</c>; the destination is kept for writes that name none.
    /// </summary>
    private void Write(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        IReadOnlyList<JgsValue> a = call.Args.Select(TransportClient.Str2Char).ToArray();
        if (a.Count is < 1 or > 4)
        {
            throw Syntax(call, "write", "write(u,DATA)\nwrite(u,DATA,PRECISION)\nwrite(u,DATA,DESTINATIONADDRESS,DESTINATIONPORT)\nwrite(u,DATA,PRECISION,DESTINATIONADDRESS,DESTINATIONPORT)");
        }

        JgsValue precision = a.Count is 2 or 4 ? a[1] : JgsValue.Str("uint8");
        if (a.Count >= 3)
        {
            Destination(a[^2], a[^1], call);
        }
        else if (_udp.RemoteEndpoint is null)
        {
            throw call.Error("instrument:interface:udpport:EmptyRemoteHostAndPort",
                "Specify destination address and destination port using the write function. Valid syntaxes are\nwrite(u,DATA,DESTINATIONADDRESS,DESTINATIONPORT)\nwrite(u,DATA,PRECISION,DESTINATIONADDRESS,DESTINATIONPORT)");
        }

        _client.Write(SerialportObject.Retarget(call, [a[0], precision]));
        if (_datagram)
        {
            Interlocked.Increment(ref _datagramsWritten);
        }
    }

    private void WriteLine(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        IReadOnlyList<JgsValue> a = call.Args.Select(TransportClient.Str2Char).ToArray();
        if (a.Count is not (1 or 3))
        {
            throw Syntax(call, "writeline", "writeline(u,DATA)\nwriteline(u,DATA,DESTINATIONADDRESS,DESTINATIONPORT)");
        }

        if (a.Count == 3)
        {
            Destination(a[1], a[2], call);
        }
        else if (_udp.RemoteEndpoint is null)
        {
            throw call.Error("instrument:interface:udpport:EmptyRemoteHostAndPort",
                "Specify destination address and destination port using the writeline function. Valid syntax is writeline(u,DATA,DESTINATIONADDRESS,DESTINATIONPORT).");
        }

        _client.WriteLine(SerialportObject.Retarget(call, [a[0]]));
    }

    /// <summary>udpport's own methods' syntax refusal: <c>Incorrect number of input arguments for "name"</c>.</summary>
    private static JgsRuntimeException Syntax(DeviceCall call, string method, string forms) =>
        call.Error("instrument:interface:udpport:IncorrectInputArgumentsPlural",
            $"Incorrect number of input arguments for \"{method}\". Valid syntaxes are\n{forms}");

    /// <summary>
    /// Resolves a destination and keeps it, or refuses in R2025b's words: the address is resolved
    /// first (so <c>write(u, data, "uint8", host)</c> fails on resolving "uint8"), then the port.
    /// </summary>
    private void Destination(JgsValue address, JgsValue port, DeviceCall call)
    {
        const string Lead = "Unable to write to the specified DESTINATIONADDRESS and DESTINATIONPORT. Additional information:\n";
        const string NoLink = "Cannot create a communication link with the remote server. Please check the input arguments(ADDRESS and PORT) and make sure the server is running.\n";
        string host = DeviceChecks.IsText(address) ? DeviceChecks.Text(address) : "";
        if (!IPAddress.TryParse(host, out IPAddress? ip))
        {
            try
            {
                ip = host.Length == 0 ? null
                    : Dns.GetHostAddresses(host).FirstOrDefault(x => x.AddressFamily == (_ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork));
            }
            catch (Exception e) when (e is SocketException or ArgumentException)
            {
                ip = null;
            }
        }

        if (ip is null)
        {
            throw call.Error("instrument:interface:udpport:InvalidRemoteEndpoint",
                Lead + NoLink + "Additional Information: resolve: No such host is known\nUnable to create remote endpoint.");
        }

        if (DeviceChecks.ClassOf(port) is "char" or "string")
        {
            throw call.Error("instrument:interface:udpport:InvalidRemoteEndpoint", Lead + "Comparison between string and double is not supported.");
        }

        double portNumber = DeviceChecks.Numbers(port).FirstOrDefault(-1);
        if (!(portNumber >= 1 && portNumber < 65536))
        {
            throw call.Error("instrument:interface:udpport:InvalidRemoteEndpoint",
                Lead + NoLink + "Additional Information: Remote Port number must be positive and less than 2^16.");
        }

        _udp.RemoteEndpoint = new IPEndPoint(ip, (int)portNumber);
    }

    // --- datagram mode ------------------------------------------------------------------------------------

    /// <summary><c>data = read(d, count)</c>, <c>read(d, count, precision)</c>: a row of Datagram values.</summary>
    private JgsValue ReadDatagrams(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count is < 1 or > 2)
        {
            throw Syntax(call, "read", "DATA = read(u,COUNT)\nDATA = read(u,COUNT,PRECISION)");
        }

        // The channel checks the count's class; a count below one reads nothing and answers [] at
        // once. The precision is only looked at once datagrams have come (R2025b waits out its
        // Timeout on read(d, 1, "int3") and warns).
        var who = new DeviceChecks.Subject("AsyncIOTransportChannel", "count", 2);
        DeviceChecks.Classes(call.Args[0], ["numeric"], who, call.Line, call.Column);
        DeviceChecks.Attributes(call.Args[0], ["scalar"], who, call.Line, call.Column);
        double wanted = DeviceChecks.Numbers(call.Args[0]).First();
        if (!(wanted >= 1))
        {
            return JgsEmpty.Zero();
        }

        int count = (int)Math.Min(Math.Floor(wanted), int.MaxValue);
        _udp.WaitForDatagrams(count, TimeSpan.FromSeconds(_client.TimeoutSeconds), Interpreter.Cancellation);
        IReadOnlyList<ReceivedDatagram> taken = _udp.TakeDatagrams(count);
        Precision precision = Precision.UInt8;
        if (call.Args.Count == 2 && taken.Count > 0)
        {
            JgsValue written = TransportClient.Str2Char(call.Args[1]);
            precision = PrecisionCodec.Parse(DeviceChecks.ValidateString(written, PrecisionCodec.Names,
                new DeviceChecks.Subject("AsyncIOTransportChannel", "precision", 3), call.Line, call.Column));
        }

        int size = PrecisionCodec.Size(precision);
        if (precision is not (Precision.Char or Precision.String) && taken.FirstOrDefault(d => d.Data.Length % size != 0) is { } odd)
        {
            string name = PrecisionCodec.Name(precision);
            throw call.Error("network:udp:receiveFailed",
                "Error receiving data from the remote server.\nAdditional Information: The first input must contain a multiple of "
                + $"{size} elements to convert from real uint8 (8 bits) to real {name} ({size * 8} bits).");
        }
        if (taken.Count < count)
        {
            JgsBuiltins.Warn(call.Host, "instrument:interface:udpport:ReadWarning",
                "Specified amount of data was not returned within the Timeout period for \"read\".\n"
                + $"'udpport' unable to read {(taken.Count > 0 ? "all requested" : "any")} data. For more information on possible reasons, see "
                + $"<a href=\"matlab: helpview('instrument', 'udpport_{(taken.Count > 0 ? "somedata" : "nodata")}')\"'>udpport Read Warnings</a>.");
        }

        if (taken.Count == 0)
        {
            return JgsEmpty.Zero();
        }

        Dictionary<string, JgsValue>[] elements = taken.Select(d => new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Data"] = precision switch
            {
                Precision.Char => JgsValue.Str(PrecisionCodec.Latin1(d.Data)),
                Precision.String => JgsValue.StringScalar(PrecisionCodec.Latin1(d.Data)),
                _ => TransportClient.Row(PrecisionCodec.Decode(d.Data, precision, _byteOrder == "big-endian")),
            },
            ["SenderAddress"] = JgsValue.StringScalar(d.SenderAddress),
            ["SenderPort"] = JgsValue.Number(d.SenderPort),
        }).ToArray();
        JgsValue result = elements.Length == 1 ? JgsValue.Struct(elements[0]) : JgsValue.StructArray(elements);
        result.SetClassName("udpport.datagram.Datagram");
        return result;
    }

    /// <summary><c>configureCallback(d, "off")</c> and <c>(d, "datagram", count, fcn)</c>.</summary>
    private void ConfigureDatagramCallback(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        IReadOnlyList<JgsValue> a = call.Args.Select(TransportClient.Str2Char).ToArray();
        const string Forms = "configureCallback(u,\"off\")\nconfigureCallback(u,\"datagram\",COUNT,CALLBACKFCN)";
        if (a.Count == 0)
        {
            throw Syntax(call, "configureCallback", Forms);
        }

        string mode = DeviceChecks.ValidateString(a[0], ["datagram", "off"], new DeviceChecks.Subject("configureCallback", "MODE"), call.Line, call.Column);
        if (mode == "off" && a.Count == 1)
        {
            lock (_udp)
            {
                _datagramMode = "off";
                _datagramFcn = null;
            }

            return;
        }

        if (mode != "datagram" || a.Count != 3)
        {
            throw Syntax(call, "configureCallback", Forms);
        }

        try
        {
            var who = new DeviceChecks.Subject(null, "DatagramsAvailableFcnCount");
            DeviceChecks.Classes(a[1], ["numeric"], who, call.Line, call.Column);
            DeviceChecks.Attributes(a[1], ["scalar", "gt:0", "integer", "finite"], who, call.Line, call.Column);
            DeviceChecks.Classes(a[2], ["function_handle"], new DeviceChecks.Subject(null, null), call.Line, call.Column);
        }
        catch (JgsRuntimeException e)
        {
            throw call.Error("instrument:interface:udpport:InvalidEntry", e.Message);
        }

        // The count starts from the datagrams already waiting, and a read does not lower it: R2025b
        // runs the callback once per Count datagrams counted so, and only as one arrives
        // (probe_net_dgcb: three waiting and one more with Count 2 is two calls).
        lock (_udp)
        {
            _datagramMode = "datagram";
            _datagramCount = DeviceChecks.Numbers(a[1]).First();
            _datagramFcn = a[2];
            _datagramCounter = _udp.DatagramCount;
        }
    }

    private void FireDatagrams(DateTime at)
    {
        if (Deleted || _datagramMode == "off" || _datagramFcn is not { } callback)
        {
            return;
        }

        var info = new TransportClient.SharedEventInfo(Session, Interpreter, "udpport.datagram.DatagramAvailableInfo", "DatagramAvailableInfo",
            [("DatagramsAvailableFcnCount", JgsValue.Number(_datagramCount)), ("AbsoluteTime", JgsBuiltins.DatetimeValue(at))]);
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
            JgsBuiltins.Warn(Session.Host, "MATLAB:callback:DynamicPropertyEventError", "Error executing the DatagramsAvailableFcn callback:\n" + failure.Message);
        }
    }

    // --- multicast -----------------------------------------------------------------------------------------

    /// <summary><c>configureMulticast(u, group)</c>, <c>(u, group, loopback)</c>, <c>(u, "off")</c>.</summary>
    private void ConfigureMulticast(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        IReadOnlyList<JgsValue> a = call.Args.Select(TransportClient.Str2Char).ToArray();
        if (a.Count is < 1 or > 2)
        {
            throw Syntax(call, "configureMulticast", "configureMulticast(u,ADDRESS)\nconfigureMulticast(u,ADDRESS,LOOPBACK)\nconfigureMulticast(u,\"off\")");
        }

        try
        {
            DeviceChecks.Classes(a[0], ["char", "string"], new DeviceChecks.Subject(null, "MULTICASTGROUP", 2), call.Line, call.Column);
            if (a.Count == 2)
            {
                DeviceChecks.Classes(a[1], ["logical"], new DeviceChecks.Subject(null, "ENABLEMULTICASTLOOPBACK"), call.Line, call.Column);
            }
        }
        catch (JgsRuntimeException e)
        {
            throw call.Error("instrument:interface:udpport:InvalidEntry", e.Message);
        }

        string text = DeviceChecks.Text(a[0]);
        if (text.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            if (_group.Length > 0 && IPAddress.TryParse(_group, out IPAddress? current))
            {
                try
                {
                    _udp.LeaveGroup(current);
                }
                catch (SocketException)
                {
                }
            }

            _group = "";
            return;
        }

        if (!IPAddress.TryParse(text, out IPAddress? group) || !IsMulticast(group))
        {
            throw call.Error("instrument:interface:udpport:InvalidMulticastAddressGroup",
                $"Unable to set the MulticastGroup to {text}. set_option: The requested address is not valid in its context");
        }

        // The group joined already is kept (Windows refuses a second membership as an invalid
        // argument); another group replaces it.
        if (!IPAddress.TryParse(_group, out IPAddress? joined) || !joined.Equals(group))
        {
            try
            {
                _udp.JoinGroup(group);
            }
            catch (SocketException e)
            {
                throw call.Error("instrument:interface:udpport:InvalidMulticastAddressGroup", $"Unable to set the MulticastGroup to {text}. set_option: {e.Message}");
            }

            if (joined is not null)
            {
                try
                {
                    _udp.LeaveGroup(joined);
                }
                catch (SocketException)
                {
                }
            }
        }

        _group = text;
        if (a.Count == 2)
        {
            SetFlag(a[1], call, on => _udp.MulticastLoopback = on);
        }
    }

    private static bool IsMulticast(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 ? address.IsIPv6Multicast : (address.GetAddressBytes()[0] & 0xF0) == 0xE0;

    // --- the declarations -------------------------------------------------------------------------------------

    private static UdpportObject Me(DeviceObject o) => (UdpportObject)o;

    private static DeviceClass Declare(bool datagram)
    {
        static JgsRuntimeException ReadOnly(DeviceCall call, string name, string function) =>
            call.Error("instrument:interface:udpport:ReadOnly", $"To set \"{name}\", use the \"{function}\" function.");

        var common = new List<DeviceProperty>
        {
            new("Timeout", static (o, _) => Me(o)._client.Timeout, static (o, v, c) => Me(o).SetTimeout(v, c)),
            new("ByteOrder", static (o, _) => JgsValue.StringScalar(Me(o)._byteOrder), static (o, v, c) =>
            {
                Me(o)._byteOrder = TcpclientObject.ByteOrderOf(v, c);
                Me(o)._client.BigEndian = Me(o)._byteOrder == "big-endian";
            }),
            new("OutputDatagramSize", static (o, _) => JgsValue.Number(Me(o)._udp.OutputDatagramSize), static (o, v, c) => Me(o).SetOutputSize(v, c)),
        };
        var network = new List<DeviceProperty>
        {
            new("EnableBroadcast", static (o, _) => JgsValue.Bool(Me(o)._udp.EnableBroadcast), static (o, v, c) => Me(o).SetFlag(v, c, on => Me(o)._udp.EnableBroadcast = on)),
            new("EnableMulticast", static (o, _) => JgsValue.Bool(Me(o)._group.Length > 0),
                static (_, _, c) => throw c.Error("instrument:interface:udpport:ReadOnly", "To set \"EnableMulticast\", use the \"configureMulticast\" function.")),
            new("EnableMulticastLoopback", static (o, _) => JgsValue.Bool(Me(o)._udp.MulticastLoopback),
                static (o, v, c) => Me(o).SetFlag(v, c, on => Me(o)._udp.MulticastLoopback = on)),
            new("MulticastGroup", static (o, _) => JgsValue.StringScalar(Me(o)._group),
                static (_, _, c) => throw c.Error("instrument:interface:udpport:ReadOnly", "To set \"MulticastGroup\", use the \"configureMulticast\" function.")),
            new("EnablePortSharing", static (o, _) => JgsValue.Bool(Me(o)._portSharing), static (_, _, c) => throw ReadOnlyEntry(c, "EnablePortSharing")),
            new("IPAddressVersion", static (o, _) => JgsValue.StringScalar(Me(o)._ipv6 ? "IPV6" : "IPV4"),
                static (_, _, c) => throw c.Error("instrument:interface:udpport:ReadOnly", "To set \"IPAddressVersion\", use the udpport constructor.")),
            new("LocalHost", static (o, _) => JgsValue.StringScalar(Me(o)._udp.LocalHost), static (_, _, c) => throw ReadOnlyEntry(c, "LocalHost")),
            new("LocalPort", static (o, _) => JgsValue.Number(Me(o)._udp.LocalPort), static (_, _, c) => throw ReadOnlyEntry(c, "LocalPort")),
        };
        var errorAndUser = new List<DeviceProperty>
        {
            new("ErrorOccurredFcn", static (o, _) => Me(o)._client.ErrorOccurredFcn ?? JgsEmpty.Zero(),
                static (o, v, c) => Me(o)._client.ErrorOccurredFcn = Me(o)._client.CallbackValue(v, "InvalidErrorOccurredFcn", "ErrorOccurredFcn must be a function handle.", c)),
            new("UserData", static (o, _) => Me(o)._client.UserData, static (o, v, _) => Me(o)._client.UserData = v),
        };
        var tag = new DeviceProperty("Tag", static (o, _) => JgsValue.StringScalar(Me(o)._tag), static (o, v, c) => Me(o)._tag = NetworkShared.TagOf(v, c));
        string[] superclasses =
        [
            "matlabshared.testmeas.internal.SetGet", "matlab.mixin.SetGet", "handle", "matlabshared.testmeas.CustomDisplay",
            "matlab.mixin.CustomDisplay", "udpport.UDPPortBase", "matlabshared.testmeas.internal.mixins.CacheEnabler",
            "matlabshared.transportlib.internal.TagAccessor", "matlab.mixin.Heterogeneous",
        ];

        if (!datagram)
        {
            var properties = new List<DeviceProperty>();
            properties.AddRange(common);
            properties.AddRange(network);
            properties.AddRange(errorAndUser);
            properties.AddRange(
            [
                new("Terminator", static (o, _) => Me(o)._client.TerminatorValue, static (_, _, c) => throw ReadOnly(c, "Terminator", "configureTerminator")),
                new("BytesAvailableFcnCount", static (o, _) => JgsValue.Number(Me(o)._client.BytesAvailableFcnCount),
                    static (_, _, c) => throw ReadOnly(c, "BytesAvailableFcnCount", "configureCallback")),
                new("BytesAvailableFcnMode", static (o, _) => JgsValue.StringScalar(Me(o)._client.BytesAvailableFcnMode),
                    static (_, _, c) => throw ReadOnly(c, "BytesAvailableFcnMode", "configureCallback")),
                new("BytesAvailableFcn", static (o, _) => Me(o)._client.BytesAvailableFcn ?? JgsEmpty.Zero(),
                    static (_, _, c) => throw ReadOnly(c, "BytesAvailableFcn", "configureCallback")),
                new("NumBytesAvailable", static (o, _) => JgsValue.Number(Me(o)._client.NumBytesAvailable)),
                new("NumBytesWritten", static (o, _) => JgsValue.Number(Me(o)._client.NumBytesWritten)),
                tag,
            ]);
            Dictionary<string, DeviceMethodBody> methods = NetworkShared.ClientMethods(static o => Me(o)._client, withBinblock: false);
            methods["write"] = static c => NetworkShared.Void(c, static c => Me(c.Target).Write(c));
            methods["writeline"] = static c => NetworkShared.Void(c, static c => Me(c.Target).WriteLine(c));
            methods["configureMulticast"] = static c => NetworkShared.Void(c, static c => Me(c.Target).ConfigureMulticast(c));
            return new DeviceClass("udpport.byte.UDPPort", "UDPPort", superclasses, properties, methods,
                ["UDPPort", "addlistener", "configureCallback", "configureMulticast", "configureTerminator", "delete", "eq", "findobj",
                 "findprop", "flush", "ge", "get", "gt", "isvalid", "le", "listener", "lt", "ne", "notify", "read", "readline", "set",
                 "write", "writeline"],
                ["IPAddressVersion", "LocalHost", "LocalPort", "Tag", "NumBytesAvailable"]);
        }

        var dgProperties = new List<DeviceProperty>();
        dgProperties.AddRange(common);
        dgProperties.Add(errorAndUser[1]);
        dgProperties.AddRange(network);
        dgProperties.AddRange(
        [
            new("DatagramsAvailableFcnCount", static (o, _) => JgsValue.Number(Me(o)._datagramCount),
                static (_, _, c) => throw ReadOnly(c, "DatagramsAvailableFcnCount", "configureCallback")),
            new("NumDatagramsAvailable", static (o, _) => JgsValue.Number(Me(o)._udp.DatagramCount)),
            new("NumDatagramsWritten", static (o, _) => JgsValue.Number(Interlocked.Read(ref Me(o)._datagramsWritten))),
            new("DatagramsAvailableFcnMode", static (o, _) => JgsValue.StringScalar(Me(o)._datagramMode),
                static (_, _, c) => throw ReadOnly(c, "DatagramsAvailableFcnMode", "configureCallback")),
            new("DatagramsAvailableFcn", static (o, _) => Me(o)._datagramFcn ?? JgsEmpty.Zero(),
                static (_, _, c) => throw ReadOnly(c, "DatagramsAvailableFcn", "configureCallback")),
            errorAndUser[0],
            tag,
        ]);
        var dgMethods = new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["read"] = static c => [Me(c.Target).ReadDatagrams(c)],
            ["write"] = static c => NetworkShared.Void(c, static c => Me(c.Target).Write(c)),
            ["configureCallback"] = static c => NetworkShared.Void(c, static c => Me(c.Target).ConfigureDatagramCallback(c)),
            ["configureMulticast"] = static c => NetworkShared.Void(c, static c => Me(c.Target).ConfigureMulticast(c)),
            ["flush"] = static c => NetworkShared.Void(c, static c => Me(c.Target)._client.Flush(c)),
        };
        return new DeviceClass("udpport.datagram.UDPPort", "UDPPort", superclasses, dgProperties, dgMethods,
            ["UDPPort", "addlistener", "configureCallback", "configureMulticast", "delete", "eq", "findobj", "findprop", "flush", "ge", "get",
             "gt", "isvalid", "le", "listener", "lt", "ne", "notify", "read", "set", "write"],
            ["IPAddressVersion", "LocalHost", "LocalPort", "Tag", "NumDatagramsAvailable"]);
    }

    protected override void OnDelete() => _udp.Dispose();
}
