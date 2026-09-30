using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using JGraph.Devices.Visa;

namespace JGraph.Devices.Simulation;

/// <summary>
/// The VISA the visadev fixtures replay against (device classes plan, stage D4): the simulated serial
/// lines as ASRL resources, and the two instruments <c>tools/devices/peer-sim</c> serves to R2025b —
/// the peer engine on a raw socket, <see cref="SocketName"/>, and behind a HiSLIP server,
/// <see cref="HislipName"/> — each answering <c>*IDN?</c> with <see cref="IdnReply"/> through a reset.
/// Each kind of session keeps the rules NI-VISA was measured to keep (probe_visa_timing): a serial
/// read ends at its count, at the termination character when <c>ASRL_END_IN</c> says so, or at the
/// timeout with what came; a socket read ends when nothing more is waiting; a message-based read
/// ends at the message's END, and a write drops what earlier messages left unread, as a HiSLIP client
/// in synchronized mode does.
/// </summary>
public sealed class SimulatedVisa : IVisaBackend
{
    public const string SocketName = "TCPIP0::127.0.0.1::5025::SOCKET";
    public const string HislipName = "TCPIP0::127.0.0.1::hislip0::INSTR";

    /// <summary>The query the instruments answer from the start, and their answer (visa_peer.m's <c>--on</c>).</summary>
    public static readonly byte[] IdnQuery = "*IDN?\n"u8.ToArray();

    public static readonly byte[] IdnReply = "JGraph,PeerSim,SN0001,1.0\n"u8.ToArray();

    private readonly Func<string, SimulatedLine?> _lineFor;
    private readonly Func<IEnumerable<string>> _portNames;
    private readonly Func<string, bool> _isPeerPort;

    /// <param name="lineFor">The simulated line a port name opens (COM20), or null.</param>
    /// <param name="portNames">Every simulated port name, the peer's included (COM20, COM21).</param>
    /// <param name="isPeerPort">Whether a name is the port the peer holds (COM21).</param>
    public SimulatedVisa(Func<string, SimulatedLine?> lineFor, Func<IEnumerable<string>> portNames, Func<string, bool> isPeerPort)
    {
        _lineFor = lineFor;
        _portNames = portNames;
        _isPeerPort = isPeerPort;
        Socket = new SimulatedInstrument(messages: false);
        Hislip = new SimulatedInstrument(messages: true);
    }

    /// <summary>The instrument behind <see cref="SocketName"/>.</summary>
    public SimulatedInstrument Socket { get; }

    /// <summary>The instrument behind <see cref="HislipName"/>.</summary>
    public SimulatedInstrument Hislip { get; }

    public string PreferredVisa => "National Instruments VISA";

    public IReadOnlyList<string> Find(string expression)
    {
        // The only expression visadevlist asks is "?*::INSTR"; the simulated VISA lists its serial
        // ports, as NI-VISA lists nothing it would have to be told about.
        return _portNames()
            .Select(static name => PortNumber(name))
            .Where(static n => n >= 0)
            .Distinct()
            .Order()
            .Select(static n => $"ASRL{n.ToString(CultureInfo.InvariantCulture)}::INSTR")
            .ToList();
    }

    private static int PortNumber(string port) =>
        port.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(port.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : -1;

    public VisaParsedName Parse(string name)
    {
        string[] parts = name.Split("::");
        string head = parts[0];
        static bool Board(string head, string word, out ushort board)
        {
            board = 0;
            if (!head.StartsWith(word, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string rest = head[word.Length..];
            return rest.Length == 0 || ushort.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out board);
        }

        bool IsClass(string text, string cls) => text.Equals(cls, StringComparison.OrdinalIgnoreCase);

        // COM20: the alias NI-VISA keeps for ASRL20.
        if (parts.Length == 1 && PortNumber(head) is var com and >= 0)
        {
            if (!_portNames().Any(p => PortNumber(p) == com))
            {
                throw new VisaException(VisaStatus.ErrorResourceNotFound, "Insufficient location information or the requested device or resource is not present in the system.");
            }

            return Serial((ushort)com);
        }

        if (Board(head, "ASRL", out ushort port) && head.Length > 4 && (parts.Length == 1 || (parts.Length == 2 && IsClass(parts[1], "INSTR"))))
        {
            return Serial(port);
        }

        if (Board(head, "TCPIP", out ushort tcp) && parts.Length >= 2 && parts[1].Length > 0)
        {
            string host = parts[1];
            if (parts.Length == 4 && IsClass(parts[3], "SOCKET") && ushort.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out ushort socketPort))
            {
                return new VisaParsedName(6, tcp, "SOCKET", $"TCPIP{tcp}::{host}::{socketPort}::SOCKET", "");
            }

            string lan = "inst0";
            bool ok = parts.Length switch
            {
                2 => true,
                3 when IsClass(parts[2], "INSTR") => true,
                3 => (lan = parts[2]).Length > 0,
                4 when IsClass(parts[3], "INSTR") => (lan = parts[2]).Length > 0,
                _ => false,
            };
            if (ok)
            {
                return new VisaParsedName(6, tcp, "INSTR", $"TCPIP{tcp}::{host}::{lan}::INSTR", "");
            }
        }

        if (Board(head, "USB", out ushort usb) && parts.Length >= 4)
        {
            string cls = IsClass(parts[^1], "INSTR") || IsClass(parts[^1], "RAW") ? parts[^1].ToUpperInvariant() : "INSTR";
            string[] body = parts.Skip(1).Take(IsClass(parts[^1], "INSTR") || IsClass(parts[^1], "RAW") ? parts.Length - 2 : parts.Length - 1).ToArray();
            if (body.Length is 3 or 4)
            {
                return new VisaParsedName(7, usb, cls, $"USB{usb}::{string.Join("::", body)}::{cls}", "");
            }
        }

        if (Board(head, "GPIB", out ushort gpib) && parts.Length >= 2 && ushort.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out ushort primary))
        {
            return new VisaParsedName(1, gpib, "INSTR", $"GPIB{gpib}::{primary}::INSTR", "");
        }

        throw new VisaException(VisaStatus.ErrorInvalidResourceName, "Invalid resource reference specified. Parsing error.");
    }

    private VisaParsedName Serial(ushort port)
    {
        string com = "COM" + port.ToString(CultureInfo.InvariantCulture);
        string alias = _portNames().Any(p => p.Equals(com, StringComparison.OrdinalIgnoreCase)) ? com : "";
        return new VisaParsedName(4, port, "INSTR", $"ASRL{port}::INSTR", alias);
    }

    public IVisaSession Open(string name, int openTimeoutMilliseconds)
    {
        VisaParsedName parsed = Parse(name);
        var missing = new VisaException(VisaStatus.ErrorResourceNotFound,
            "Insufficient location information or the requested device or resource is not present in the system.");
        switch (parsed.InterfaceType)
        {
            case 4:
            {
                string com = "COM" + parsed.InterfaceNumber.ToString(CultureInfo.InvariantCulture);
                if (_isPeerPort(com))
                {
                    throw new VisaException(VisaStatus.ErrorResourceBusy, "The resource is valid, but VISA cannot currently access it.");
                }

                SimulatedLine line = _lineFor(com) ?? throw missing;
                SimulatedSerialPort near;
                try
                {
                    near = line.Open(new SerialSettings(), dtr: true, rts: true);
                }
                catch (DeviceOpenException)
                {
                    throw new VisaException(VisaStatus.ErrorResourceBusy, "The resource is valid, but VISA cannot currently access it.");
                }

                return new SerialSession(near, parsed.InterfaceNumber);
            }

            case 6 when parsed.ExpandedName.Equals(Local(SocketName, parsed), StringComparison.OrdinalIgnoreCase):
                return Socket.Connect(parsed, port: 5025);
            case 6 when parsed.ExpandedName.Equals(Local(HislipName, parsed), StringComparison.OrdinalIgnoreCase):
                return Hislip.Connect(parsed, port: 4880);
            default:
                throw missing;
        }
    }

    /// <summary>The instrument's name with the host the script gave, when that host is this machine.</summary>
    private static string Local(string name, VisaParsedName parsed)
    {
        string host = parsed.ExpandedName.Split("::")[1];
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ? name.Replace("127.0.0.1", host, StringComparison.Ordinal) : name;
    }

    public void Dispose()
    {
        Socket.Dispose();
        Hislip.Dispose();
    }

    // --- sessions ------------------------------------------------------------------------------------

    /// <summary>What every simulated session keeps: its attributes, with NI-VISA's defaults.</summary>
    private abstract class SessionBase : IVisaSession
    {
        protected readonly Dictionary<uint, ulong> Attributes = new()
        {
            [VisaAttribute.TimeoutValue] = 2000,
            [VisaAttribute.TermChar] = 10,
            [VisaAttribute.TermCharEnabled] = 0,
            [VisaAttribute.SendEndEnabled] = 1,
            [VisaAttribute.SuppressEndEnabled] = 0,
        };

        protected readonly Dictionary<uint, string> Strings = new();

        protected bool Closed { get; private set; }

        protected int TimeoutMilliseconds => (int)Math.Min(int.MaxValue, Attributes[VisaAttribute.TimeoutValue]);

        protected bool TermCharOn => Attributes[VisaAttribute.TermCharEnabled] != 0;

        protected byte TermChar => (byte)Attributes[VisaAttribute.TermChar];

        public abstract VisaReadResult Read(int count, CancellationToken cancel);

        public abstract void Write(ReadOnlySpan<byte> data, CancellationToken cancel);

        public virtual ulong GetAttribute(uint attribute) =>
            Attributes.TryGetValue(attribute, out ulong value) ? value : throw NotSupported();

        public string GetStringAttribute(uint attribute) =>
            Strings.TryGetValue(attribute, out string? value) ? value : throw NotSupported();

        public virtual void SetAttribute(uint attribute, ulong value)
        {
            if (!Attributes.ContainsKey(attribute))
            {
                throw NotSupported();
            }

            Attributes[attribute] = attribute switch
            {
                VisaAttribute.TermChar => value & 0xFF,
                VisaAttribute.TimeoutValue => Math.Min(value, uint.MaxValue),
                _ => value,
            };
        }

        public abstract void Clear();

        public virtual ushort ReadStatusByte() =>
            throw new VisaException(VisaStatus.ErrorInvalidSetup, "Unable to start operation because setup is invalid (due to attributes being set to an inconsistent state).");

        public virtual void AssertTrigger() =>
            throw new VisaException(VisaStatus.ErrorOperationNotSupported, "Operation is not supported on this session.");

        protected static VisaException NotSupported() =>
            new(VisaStatus.ErrorAttributeNotSupported, "The specified attribute is not defined or supported by the referenced session, event, or find list.");

        public virtual void Dispose() => Closed = true;

        /// <summary>
        /// Waits for <paramref name="done"/> up to the session's timeout; VI_TMO_IMMEDIATE (0) checks once.
        /// Answers whether it came.
        /// </summary>
        protected bool Wait(InputBuffer input, Func<bool> done, CancellationToken cancel)
        {
            int timeout = TimeoutMilliseconds;
            return timeout == 0 ? done() : input.WaitUntil(done, TimeSpan.FromMilliseconds(timeout), cancel);
        }

        /// <summary>
        /// Takes at most <paramref name="count"/> bytes, through the termination character when it is on;
        /// answers them and whether the character ended them.
        /// </summary>
        protected (byte[] Bytes, bool Terminated) TakeUpTo(InputBuffer input, int count, bool termCharEnds)
        {
            int take = Math.Min(count, input.Count);
            bool terminated = false;
            if (termCharEnds)
            {
                int at = input.IndexAfter([TermChar]);
                if (at >= 0 && at <= take)
                {
                    take = at;
                    terminated = true;
                }
            }

            return (input.Take(take), terminated);
        }
    }

    /// <summary>An ASRL INSTR session on a simulated line's near end.</summary>
    private sealed class SerialSession : SessionBase
    {
        private const ulong EndTermChar = 2;
        private readonly SimulatedSerialPort _port;

        public SerialSession(SimulatedSerialPort port, ushort number)
        {
            _port = port;
            Attributes[VisaAttribute.InterfaceType] = 4;
            Attributes[VisaAttribute.InterfaceNumber] = number;
            Attributes[VisaAttribute.AsrlBaud] = 9600;
            Attributes[VisaAttribute.AsrlDataBits] = 8;
            Attributes[VisaAttribute.AsrlStopBits] = 10;
            Attributes[VisaAttribute.AsrlParity] = 0;
            Attributes[VisaAttribute.AsrlFlowControl] = 0;
            Attributes[VisaAttribute.AsrlEndIn] = EndTermChar;
            Attributes[VisaAttribute.AsrlEndOut] = 0;
            Attributes[VisaAttribute.AsrlDtrState] = 1;
            Attributes[VisaAttribute.AsrlRtsState] = 1;
        }

        private bool TermCharEnds => TermCharOn && Attributes[VisaAttribute.AsrlEndIn] == EndTermChar;

        public override ulong GetAttribute(uint attribute)
        {
            SerialPins pins = _port.GetPins();
            return attribute switch
            {
                VisaAttribute.AsrlCtsState => pins.ClearToSend ? 1UL : 0UL,
                VisaAttribute.AsrlDsrState => pins.DataSetReady ? 1UL : 0UL,
                VisaAttribute.AsrlDcdState => pins.CarrierDetect ? 1UL : 0UL,
                VisaAttribute.AsrlRiState => pins.RingIndicator ? 1UL : 0UL,
                VisaAttribute.AsrlDtrState => _port.Dtr ? 1UL : 0UL,
                VisaAttribute.AsrlRtsState => _port.Rts ? 1UL : 0UL,
                _ => base.GetAttribute(attribute),
            };
        }

        public override void SetAttribute(uint attribute, ulong value)
        {
            switch (attribute)
            {
                case VisaAttribute.AsrlDtrState:
                    _port.SetDtr(value != 0);
                    return;
                case VisaAttribute.AsrlRtsState:
                    _port.SetRts(value != 0);
                    return;
                case VisaAttribute.AsrlBaud or VisaAttribute.AsrlDataBits or VisaAttribute.AsrlStopBits or VisaAttribute.AsrlParity or VisaAttribute.AsrlFlowControl:
                {
                    ulong dataBits = attribute == VisaAttribute.AsrlDataBits ? value : Attributes[VisaAttribute.AsrlDataBits];
                    ulong stopBits = attribute == VisaAttribute.AsrlStopBits ? value : Attributes[VisaAttribute.AsrlStopBits];

                    // NI-VISA takes one and a half stop bits only with five data bits, and two stop bits
                    // only with more than five (probe_visa_misc, visa_serial), as the DCB does.
                    if ((stopBits == 15 && dataBits != 5) || (stopBits == 20 && dataBits == 5))
                    {
                        throw new VisaException(VisaStatus.ErrorInvalidSetup,
                            "Unable to start operation because setup is invalid (due to attributes being set to an inconsistent state).");
                    }

                    base.SetAttribute(attribute, value);
                    try
                    {
                        _port.Apply(new SerialSettings
                        {
                            BaudRate = (int)Math.Min(int.MaxValue, Attributes[VisaAttribute.AsrlBaud]),
                            DataBits = (int)Attributes[VisaAttribute.AsrlDataBits],
                            StopBits = Attributes[VisaAttribute.AsrlStopBits] switch { 15 => SerialStopBits.OnePointFive, 20 => SerialStopBits.Two, _ => SerialStopBits.One },
                            Parity = Attributes[VisaAttribute.AsrlParity] switch { 1 => SerialParity.Odd, 2 => SerialParity.Even, 3 => SerialParity.Mark, 4 => SerialParity.Space, _ => SerialParity.None },
                            FlowControl = Attributes[VisaAttribute.AsrlFlowControl] switch { 1 => SerialFlowControl.Software, 2 => SerialFlowControl.Hardware, _ => SerialFlowControl.None },
                        });
                    }
                    catch (DeviceSettingsException)
                    {
                        throw new VisaException(VisaStatus.ErrorAttributeStateNotSupported, "The specified state of the attribute is not valid, or is not supported as defined by the resource.");
                    }

                    return;
                }

                default:
                    base.SetAttribute(attribute, value);
                    return;
            }
        }

        public override VisaReadResult Read(int count, CancellationToken cancel)
        {
            InputBuffer input = _port.Input;
            bool ends = TermCharEnds;
            bool done = Wait(input, () => input.Count >= count || (ends && input.IndexAfter([TermChar]) is > 0 and var at && at <= count), cancel);
            (byte[] bytes, bool terminated) = TakeUpTo(input, count, ends);
            int status = terminated ? VisaStatus.SuccessTermChar : done ? VisaStatus.SuccessMaxCount : VisaStatus.ErrorTimeout;
            return new VisaReadResult(bytes, status);
        }

        public override void Write(ReadOnlySpan<byte> data, CancellationToken cancel) =>
            _port.Write(data, TimeSpan.FromMilliseconds(Math.Max(1, TimeoutMilliseconds)), cancel);

        /// <summary>viClear on a serial INSTR: flush, a break the length of VI_ATTR_ASRL_BREAK_LEN (250 ms), flush.</summary>
        public override void Clear()
        {
            _port.FlushOutput();
            _port.SendBreak(250, CancellationToken.None);
            _port.FlushInput();
        }

        public override void Dispose()
        {
            base.Dispose();
            _port.Dispose();
        }
    }
}

/// <summary>
/// One of the simulated VISA's instruments: the peer engine, and the session (if any) on its near
/// side. The engine outlives sessions, as <c>peer-sim.exe</c> outlives the objects a fixture clears.
/// A socket instrument's bytes are a stream; a message instrument's are messages, each ending in END.
/// </summary>
public sealed class SimulatedInstrument : IPeerLink, IInstrumentLink, IDisposable
{
    private readonly object _gate = new();
    private readonly bool _messages;
    private readonly List<byte> _batch = new();
    private Session? _session;
    private bool _batching;

    internal SimulatedInstrument(bool messages)
    {
        _messages = messages;
        Engine = new PeerEngine(this);
        Engine.AddStandingRule(SimulatedVisa.IdnQuery, SimulatedVisa.IdnReply);
    }

    public PeerEngine Engine { get; }

    public byte StatusByte { get; set; }

    /// <summary>Whether a service request was signalled and not yet seen.</summary>
    public bool ServiceRequested { get; private set; }

    public void RequestService() => ServiceRequested = true;

    internal IVisaSession Connect(VisaParsedName parsed, ushort port)
    {
        var session = new Session(this, _messages);
        session.Describe(parsed, port);
        lock (_gate)
        {
            _session = session;
        }

        return session;
    }

    void IPeerLink.Send(ReadOnlySpan<byte> bytes)
    {
        lock (_gate)
        {
            if (_batching)
            {
                _batch.AddRange(bytes.ToArray());
                return;
            }

            _session?.Deliver(bytes.ToArray());
        }
    }

    /// <summary>Hands written bytes to the engine; what it answers while it reads them is one message.</summary>
    private void FromSession(ReadOnlySpan<byte> bytes)
    {
        lock (_gate)
        {
            _batching = true;
        }

        byte[]? answer = null;
        try
        {
            Engine.Receive(bytes);
        }
        finally
        {
            lock (_gate)
            {
                _batching = false;
                if (_batch.Count > 0)
                {
                    answer = _batch.ToArray();
                    _batch.Clear();
                }
            }
        }

        if (answer is not null)
        {
            lock (_gate)
            {
                _session?.Deliver(answer);
            }
        }
    }

    void IPeerLink.SetRts(bool on)
    {
    }

    void IPeerLink.SetDtr(bool on)
    {
    }

    SerialPins IPeerLink.Pins => default;

    void IPeerLink.SendBreak(int milliseconds)
    {
    }

    public void Dispose() => Engine.Dispose();

    private sealed class Session : IVisaSession
    {
        private readonly SimulatedInstrument _instrument;
        private readonly bool _messages;
        private readonly InputBuffer _input = new();
        private readonly Queue<int> _ends = new();
        private readonly Dictionary<uint, ulong> _attributes = new()
        {
            [VisaAttribute.TimeoutValue] = 2000,
            [VisaAttribute.TermChar] = 10,
            [VisaAttribute.TermCharEnabled] = 0,
            [VisaAttribute.SendEndEnabled] = 1,
            [VisaAttribute.SuppressEndEnabled] = 0,
            [VisaAttribute.InterfaceType] = 6,
            [VisaAttribute.TcpipNoDelay] = 1,
            [VisaAttribute.TcpipKeepAlive] = 0,
        };

        private readonly Dictionary<uint, string> _strings = new();
        private bool _closed;

        public Session(SimulatedInstrument instrument, bool messages)
        {
            _instrument = instrument;
            _messages = messages;
        }

        public void Describe(VisaParsedName parsed, ushort port)
        {
            string[] parts = parsed.ExpandedName.Split("::");
            _attributes[VisaAttribute.InterfaceNumber] = parsed.InterfaceNumber;
            _attributes[VisaAttribute.TcpipPort] = port;
            _attributes[VisaAttribute.TcpipIsHislip] = _messages ? 1UL : 0UL;
            _strings[VisaAttribute.TcpipAddress] = "127.0.0.1";
            if (_messages)
            {
                _strings[VisaAttribute.TcpipDeviceName] = parts[2];
            }
        }

        /// <summary>Bytes the instrument sent: one more message, or more of the stream.</summary>
        public void Deliver(byte[] bytes)
        {
            lock (_ends)
            {
                if (_closed)
                {
                    return;
                }

                _input.Append(bytes);
                if (_messages)
                {
                    _ends.Enqueue((int)Math.Min(int.MaxValue, _input.TotalReceived));
                }
            }
        }

        private int TimeoutMilliseconds => (int)Math.Min(int.MaxValue, _attributes[VisaAttribute.TimeoutValue]);

        public VisaReadResult Read(int count, CancellationToken cancel)
        {
            int timeout = TimeoutMilliseconds;
            bool any = timeout == 0 ? _input.Count > 0 : _input.WaitUntil(() => _input.Count > 0, TimeSpan.FromMilliseconds(timeout), cancel);
            if (!any)
            {
                return new VisaReadResult([], VisaStatus.ErrorTimeout);
            }

            lock (_ends)
            {
                // A message ends the read at its END; a socket ends it when nothing more is waiting
                // (VI_ATTR_SUPPRESS_END_EN off, as visadev sets it).
                long consumed = _input.TotalReceived - _input.Count;
                int available = _input.Count;
                bool end = true;
                if (_messages && _ends.Count > 0)
                {
                    available = (int)Math.Min(available, _ends.Peek() - consumed);
                }

                int take = Math.Min(count, available);
                bool terminated = false;
                if (_attributes[VisaAttribute.TermCharEnabled] != 0)
                {
                    int at = _input.IndexAfter([(byte)_attributes[VisaAttribute.TermChar]]);
                    if (at >= 0 && at <= take)
                    {
                        take = at;
                        terminated = true;
                    }
                }

                byte[] bytes = _input.Take(take);
                if (_messages && _ends.Count > 0 && consumed + take >= _ends.Peek())
                {
                    _ends.Dequeue();
                }
                else if (_messages || take < available)
                {
                    end = false;
                }

                int status = terminated ? VisaStatus.SuccessTermChar : end ? VisaStatus.Success : VisaStatus.SuccessMaxCount;
                return new VisaReadResult(bytes, status);
            }
        }

        public void Write(ReadOnlySpan<byte> data, CancellationToken cancel)
        {
            if (_messages)
            {
                // A new message: a HiSLIP client in synchronized mode drops what older ones left unread.
                lock (_ends)
                {
                    _input.Clear();
                    _ends.Clear();
                }
            }

            _instrument.FromSession(data);
        }

        public ulong GetAttribute(uint attribute) =>
            _attributes.TryGetValue(attribute, out ulong value) ? value : throw NotSupported();

        public string GetStringAttribute(uint attribute) =>
            _strings.TryGetValue(attribute, out string? value) ? value : throw NotSupported();

        public void SetAttribute(uint attribute, ulong value)
        {
            if (!_attributes.ContainsKey(attribute) || attribute is VisaAttribute.InterfaceType or VisaAttribute.InterfaceNumber or VisaAttribute.TcpipPort or VisaAttribute.TcpipIsHislip)
            {
                throw NotSupported();
            }

            _attributes[attribute] = attribute switch
            {
                VisaAttribute.TermChar => value & 0xFF,
                VisaAttribute.TimeoutValue => Math.Min(value, uint.MaxValue),
                _ => value,
            };
        }

        public void Clear()
        {
            if (_messages)
            {
                _instrument.Engine.ClearReceived();
            }

            lock (_ends)
            {
                _input.Clear();
                _ends.Clear();
            }
        }

        public ushort ReadStatusByte() => _messages
            ? _instrument.StatusByte
            : throw new VisaException(VisaStatus.ErrorInvalidSetup,
                "Unable to start operation because setup is invalid (due to attributes being set to an inconsistent state).");

        public void AssertTrigger()
        {
            if (!_messages)
            {
                throw new VisaException(VisaStatus.ErrorOperationNotSupported, "Operation is not supported on this session.");
            }

            _instrument.Engine.TriggerReceived();
        }

        private static VisaException NotSupported() =>
            new(VisaStatus.ErrorAttributeNotSupported, "The specified attribute is not defined or supported by the referenced session, event, or find list.");

        public void Dispose()
        {
            lock (_ends)
            {
                _closed = true;
            }

            lock (_instrument._gate)
            {
                if (ReferenceEquals(_instrument._session, this))
                {
                    _instrument._session = null;
                }
            }
        }
    }
}
