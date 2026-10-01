using JGraph.Devices.SmartCard;

namespace JGraph.Devices.Simulation;

/// <summary>
/// Smart-card readers for the tests and <c>jgraph.internal.pcscsim</c> (device classes plan, stage
/// D12): two readers and one card, which starts in the first. The card speaks T=1 only and holds one
/// application:
/// <list type="bullet">
/// <item>SELECT by name (<c>00 A4 04 00</c>) of <see cref="Aid"/> answers its FCI and 9000; another name, 6A82;</item>
/// <item>GET DATA (<c>00 CA 00 00</c>) answers "JGraph" once selected, and 6985 before;</item>
/// <item>the echo (<c>80 10 00 00 Lc data</c>) answers its data once selected;</item>
/// <item>VERIFY (<c>00 20 00 00 04</c> and four digits) answers 9000 for 1234, else 63C2, 63C1, 63C0 and then 6983, the count kept across resets as a card's is;</item>
/// <item>an unknown instruction is 6D00, an unknown class 6E00, a wrong length 6700.</item>
/// </list>
/// A reset forgets the selection. A reader answers two control codes: SCARD_CTL_CODE(3400) with a
/// feature list, and SCARD_CTL_CODE(2048) with its input reversed. Connections share, exclude, lose
/// the card and see it reset as PC/SC's do, with the same SCARD_ codes.
/// </summary>
public sealed class SimulatedSmartCards : ISmartCardBackend
{
    public const string FirstReader = "JGraph Test Reader 0";
    public const string SecondReader = "JGraph Test Reader 1";

    /// <summary>The application's name on the card.</summary>
    public static readonly byte[] Aid = [0xA0, 0x00, 0x00, 0x0F, 0x4A, 0x47, 0x01];

    private const int SharingViolation = unchecked((int)0x8010000B);
    private const int NoSmartCard = unchecked((int)0x8010000C);
    private const int UnknownReader = unchecked((int)0x80100009);
    private const int ProtoMismatch = unchecked((int)0x8010000F);
    private const int NotTransacted = unchecked((int)0x80100016);
    private const int UnsupportedFeature = unchecked((int)0x80100022);
    private const int ResetCard = unchecked((int)0x80100068);
    private const int RemovedCard = unchecked((int)0x80100069);
    private const int InvalidHandle = unchecked((int)0x80100003);
    private const int NotReady = unchecked((int)0x80100010);

    private readonly object _gate = new();
    private readonly List<string> _readers = [FirstReader, SecondReader];
    private readonly List<Connection> _connections = [];
    private readonly List<Action<SmartCardEvent>> _watchers = [];
    private string? _cardIn = FirstReader;
    private int _insertions = 1;
    private int _resets;
    private bool _selected;
    private int _pinTries = 3;
    private Connection? _transaction;

    /// <summary>The card's answer to reset: T=0 and T=1 offered, eight historical bytes "JGRAPH\0\0", and its check byte.</summary>
    public static byte[] CardAtr { get; } = MakeAtr();

    private static byte[] MakeAtr()
    {
        byte[] atr = [0x3B, 0x88, 0x80, 0x01, 0x4A, 0x47, 0x52, 0x41, 0x50, 0x48, 0x00, 0x00, 0x00];
        byte check = 0;
        for (int i = 1; i < atr.Length - 1; i++)
        {
            check ^= atr[i];
        }

        atr[^1] = check;
        return atr;
    }

    /// <summary>How many connections are open.</summary>
    public int OpenCount
    {
        get
        {
            lock (_gate)
            {
                return _connections.Count;
            }
        }
    }

    public IReadOnlyList<SmartCardReader> Readers()
    {
        lock (_gate)
        {
            return _readers.Select(r =>
            {
                bool present = _cardIn == r;
                List<Connection> held = _connections.Where(c => c.Reader == r && c.Share != "direct").ToList();
                string state = !present ? "empty" : held.Exists(static c => c.Share == "exclusive") ? "exclusive" : held.Count > 0 ? "inuse" : "present";
                return new SmartCardReader(r, state, present, present ? CardAtr : []);
            }).ToList();
        }
    }

    private static SmartCardException Failure(string what, int code) => new($"{what}: {SmartCardWords.Describe(code)}.", code);

    public ISmartCard Connect(string reader, string share, string protocol)
    {
        lock (_gate)
        {
            string what = share == "direct" ? $"The reader '{reader}' could not be reached" : $"The card in '{reader}' could not be reached";
            if (!_readers.Contains(reader))
            {
                throw Failure(what, UnknownReader);
            }

            var connection = new Connection(this, reader);
            Negotiate(connection, share, protocol, what);
            _connections.Add(connection);
            return connection;
        }
    }

    /// <summary>What SCardConnect and SCardReconnect check, in their order: sharing, the card, the protocol.</summary>
    private void Negotiate(Connection connection, string share, string protocol, string what)
    {
        List<Connection> others = _connections.Where(c => c != connection && c.Reader == connection.Reader).ToList();
        if (others.Exists(static c => c.Share == "exclusive") || (share == "exclusive" && others.Count > 0))
        {
            throw Failure(what, SharingViolation);
        }

        bool present = _cardIn == connection.Reader;
        if (share != "direct" && !present)
        {
            throw Failure(what, NoSmartCard);
        }

        if (protocol is "T0" or "raw" && present)
        {
            throw Failure(what, ProtoMismatch);
        }

        connection.Share = share;
        connection.Protocol = present && (share != "direct" || protocol == "T1") ? "T1" : "none";
        connection.Insertion = _insertions;
        connection.Resets = _resets;
    }

    /// <summary>Takes the card out of its reader: its connections answer "removed" from now on.</summary>
    public void Remove()
    {
        string? reader;
        lock (_gate)
        {
            reader = _cardIn;
            if (reader is null)
            {
                return;
            }

            _cardIn = null;
            _selected = false;
            _transaction = null;
        }

        Raise(new SmartCardEvent("CardRemoved", reader, []));
    }

    /// <summary>Puts the card into <paramref name="reader"/>, out of wherever it was.</summary>
    public void Insert(string reader)
    {
        Remove();
        lock (_gate)
        {
            if (!_readers.Contains(reader))
            {
                throw new ArgumentException($"No simulated reader is named '{reader}'.", nameof(reader));
            }

            _cardIn = reader;
            _insertions++;
        }

        Raise(new SmartCardEvent("CardInserted", reader, CardAtr));
    }

    /// <summary>Unplugs a reader, its card going with it.</summary>
    public void Unplug(string reader)
    {
        bool had;
        lock (_gate)
        {
            if (!_readers.Remove(reader))
            {
                return;
            }

            had = _cardIn == reader;
        }

        if (had)
        {
            Remove();
        }

        Raise(new SmartCardEvent("ReaderRemoved", reader, []));
    }

    /// <summary>Plugs a reader back in, empty.</summary>
    public void Plug(string reader)
    {
        lock (_gate)
        {
            if (reader != FirstReader && reader != SecondReader)
            {
                throw new ArgumentException($"No simulated reader is named '{reader}'.", nameof(reader));
            }

            if (_readers.Contains(reader))
            {
                return;
            }

            _readers.Add(reader);
            _readers.Sort(StringComparer.Ordinal);
        }

        Raise(new SmartCardEvent("ReaderAdded", reader, []));
    }

    private void Raise(SmartCardEvent e)
    {
        Action<SmartCardEvent>[] watchers;
        lock (_gate)
        {
            watchers = _watchers.ToArray();
        }

        foreach (Action<SmartCardEvent> watcher in watchers)
        {
            watcher(e);
        }
    }

    public IDisposable Watch(Action<SmartCardEvent> changed)
    {
        lock (_gate)
        {
            _watchers.Add(changed);
        }

        return new Unwatch(this, changed);
    }

    private sealed class Unwatch(SimulatedSmartCards owner, Action<SmartCardEvent> changed) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._gate)
            {
                owner._watchers.Remove(changed);
            }
        }
    }

    private void ResetCardLocked()
    {
        _resets++;
        _selected = false;
    }

    /// <summary>The card's answer to one command APDU.</summary>
    private byte[] Answer(ReadOnlySpan<byte> apdu)
    {
        static byte[] Status(int sw, ReadOnlySpan<byte> data = default) => [.. data, (byte)(sw >> 8), (byte)sw];

        byte cla = apdu[0], ins = apdu[1], p1 = apdu[2], p2 = apdu[3];
        ReadOnlySpan<byte> data = apdu.Length > 5 ? apdu.Slice(5, Math.Min(apdu[4], apdu.Length - 5)) : default;
        bool lengthOk = apdu.Length <= 5 || apdu.Length == 5 + apdu[4] || apdu.Length == 6 + apdu[4];
        if (cla is not (0x00 or 0x80))
        {
            return Status(0x6E00);
        }

        switch (cla, ins)
        {
            case (0x00, 0xA4):
                if (p1 != 0x04 || p2 != 0x00)
                {
                    return Status(0x6A86);
                }

                if (!lengthOk || data.Length == 0)
                {
                    return Status(0x6700);
                }

                _selected = data.SequenceEqual(Aid);
                return _selected ? Status(0x9000, [0x6F, 0x09, 0x84, 0x07, .. Aid]) : Status(0x6A82);
            case (0x00, 0xCA):
                return !_selected ? Status(0x6985) : p1 != 0 || p2 != 0 ? Status(0x6A88) : Status(0x9000, "JGraph"u8);
            case (0x80, 0x10):
                return !_selected ? Status(0x6985) : !lengthOk ? Status(0x6700) : Status(0x9000, data);
            case (0x00, 0x20):
                if (!lengthOk || data.Length != 4)
                {
                    return Status(0x6700);
                }

                if (_pinTries == 0)
                {
                    return Status(0x6983);
                }

                if (data.SequenceEqual("1234"u8))
                {
                    _pinTries = 3;
                    return Status(0x9000);
                }

                _pinTries--;
                return Status(0x63C0 | _pinTries);
            default:
                return Status(0x6D00);
        }
    }

    private sealed class Connection(SimulatedSmartCards owner, string reader) : ISmartCard
    {
        private bool _closed;

        public string Reader { get; } = reader;

        public string Share { get; set; } = "shared";

        public string Protocol { get; set; } = "none";

        public int Insertion { get; set; }

        public int Resets { get; set; }

        /// <summary>Throws what PC/SC answers a handle whose card has gone, changed or been reset.</summary>
        private void Usable(string what, bool needsCard)
        {
            if (_closed)
            {
                throw Failure(what, InvalidHandle);
            }

            if (Share == "direct" && Protocol == "none")
            {
                if (needsCard)
                {
                    throw Failure(what, NotReady);
                }

                return;
            }

            if (owner._cardIn != Reader || owner._insertions != Insertion)
            {
                throw Failure(what, RemovedCard);
            }

            if (owner._resets != Resets)
            {
                throw Failure(what, ResetCard);
            }
        }

        public byte[] Atr()
        {
            lock (owner._gate)
            {
                Usable("The card's status could not be read", needsCard: false);
                return owner._cardIn == Reader ? CardAtr : [];
            }
        }

        public string State()
        {
            lock (owner._gate)
            {
                Usable("The card's status could not be read", needsCard: false);
                return owner._cardIn != Reader ? "absent" : Protocol == "none" ? "negotiable" : "specific";
            }
        }

        public byte[] Transmit(ReadOnlySpan<byte> apdu)
        {
            lock (owner._gate)
            {
                Usable("The APDU could not be sent", needsCard: true);
                if (owner._transaction is { } holder && holder != this)
                {
                    throw Failure("The APDU could not be sent", SharingViolation);
                }

                return owner.Answer(apdu);
            }
        }

        public byte[] Control(uint code, ReadOnlySpan<byte> input)
        {
            lock (owner._gate)
            {
                Usable("The control code failed", needsCard: false);
                if (code == SmartCardWords.ControlCode(3400))
                {
                    uint verify = SmartCardWords.ControlCode(3500);
                    return [0x06, 0x04, (byte)(verify >> 24), (byte)(verify >> 16), (byte)(verify >> 8), (byte)verify];
                }

                if (code == SmartCardWords.ControlCode(2048))
                {
                    byte[] reversed = input.ToArray();
                    Array.Reverse(reversed);
                    return reversed;
                }

                throw Failure("The control code failed", UnsupportedFeature);
            }
        }

        public void BeginTransaction()
        {
            lock (owner._gate)
            {
                Usable("The transaction could not begin", needsCard: true);
                if (owner._transaction is { } holder && holder != this)
                {
                    throw Failure("The transaction could not begin", SharingViolation);
                }

                owner._transaction = this;
            }
        }

        public void EndTransaction(string disposition)
        {
            lock (owner._gate)
            {
                Usable("The transaction could not end", needsCard: true);
                if (owner._transaction != this)
                {
                    throw Failure("The transaction could not end", NotTransacted);
                }

                owner._transaction = null;
                Apply(disposition);
            }
        }

        /// <summary>What a disposition does to the card; the connection that asked stays in step with it.</summary>
        private void Apply(string disposition)
        {
            if (disposition is "reset" or "unpower" && owner._cardIn == Reader && owner._insertions == Insertion)
            {
                owner.ResetCardLocked();
                Resets = owner._resets;
            }
        }

        public void Reconnect(string share, string protocol, string initialization)
        {
            lock (owner._gate)
            {
                if (_closed)
                {
                    throw Failure($"The card in '{Reader}' could not be reached again", InvalidHandle);
                }

                owner.Negotiate(this, share, protocol, $"The card in '{Reader}' could not be reached again");
                if (initialization is "reset" or "unpower" && owner._cardIn == Reader)
                {
                    owner.ResetCardLocked();
                    Resets = owner._resets;
                }
            }
        }

        public void Disconnect(string disposition)
        {
            lock (owner._gate)
            {
                if (_closed)
                {
                    return;
                }

                if (owner._transaction == this)
                {
                    owner._transaction = null;
                }

                if (Share != "direct" || Protocol != "none")
                {
                    Apply(disposition);
                }

                _closed = true;
                owner._connections.Remove(this);
            }
        }

        public void Dispose() => Disconnect("leave");
    }
}
