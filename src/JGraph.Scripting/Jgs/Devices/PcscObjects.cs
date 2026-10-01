using System.Globalization;
using System.Text;
using JGraph.Devices.SmartCard;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>What the <c>jgraph.pcsc</c> functions share (device classes plan, stage D12, ADR 0196).</summary>
internal static class PcscShared
{
    /// <summary>The session's readers: jgraph.internal.pcscsim's, or the machine's through PC/SC.</summary>
    public static ISmartCardBackend Backend(DeviceSession session) =>
        session.SmartCardSimulation is { } simulated ? simulated
        : OperatingSystem.IsWindows() ? WinSmartCards.Instance
        : NoReaders.Instance;

    /// <summary>A platform with no PC/SC backend has no readers.</summary>
    private sealed class NoReaders : ISmartCardBackend
    {
        public static readonly NoReaders Instance = new();

        public IReadOnlyList<SmartCardReader> Readers() => [];

        public ISmartCard Connect(string reader, string share, string protocol) =>
            throw new SmartCardException("Smart cards are supported on Windows only.", 0);

        public IDisposable Watch(Action<SmartCardEvent> changed) => new Nothing();

        private sealed class Nothing : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    /// <summary>Bytes as a script holds them: a uint8 row.</summary>
    public static JgsValue Bytes(byte[] bytes) => TransportClient.TypedRow(Array.ConvertAll(bytes, static b => (double)b), Precision.UInt8);

    /// <summary>Bytes as hex text, the way an ATR or an APDU is written: "3B 88 80 01".</summary>
    public static string HexText(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length * 3);
        foreach (byte b in bytes)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    /// <summary>Hex text as bytes: pairs of digits, parted or not by spaces, colons, commas or dashes, each with or without "0x"; null when it is not that.</summary>
    public static byte[]? ParseHex(string text)
    {
        var digits = new StringBuilder(text.Length);
        foreach (string token in text.Split([' ', '\t', ':', ',', '-'], StringSplitOptions.RemoveEmptyEntries))
        {
            string part = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? token[2..] : token;
            if (part.Length == 0 || part.Length % 2 != 0)
            {
                return null;
            }

            digits.Append(part);
        }

        var bytes = new byte[digits.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(digits.ToString(i * 2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out bytes[i]))
            {
                return null;
            }
        }

        return bytes;
    }

    /// <summary>Bytes from a numeric array of 0 to 255, or from hex text.</summary>
    public static byte[] ByteArgument(JgsValue value, string what, int line, int col)
    {
        if (DeviceChecks.IsText(value))
        {
            return ParseHex(DeviceChecks.Text(value))
                ?? throw new JgsRuntimeException(line, col, "JGraph:pcsc:InvalidBytes", $"{what} is bytes (integers from 0 to 255) or hex text (\"00 A4 04 00\").");
        }

        if (!DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(value)))
        {
            throw new JgsRuntimeException(line, col, "JGraph:pcsc:InvalidBytes", $"{what} is bytes (integers from 0 to 255) or hex text (\"00 A4 04 00\").");
        }

        double[] numbers = DeviceChecks.Numbers(value).ToArray();
        if (Array.Exists(numbers, static x => x < 0 || x > 255 || x != Math.Floor(x)))
        {
            throw new JgsRuntimeException(line, col, "JGraph:pcsc:InvalidBytes", $"{what} is bytes (integers from 0 to 255) or hex text (\"00 A4 04 00\").");
        }

        return Array.ConvertAll(numbers, static x => (byte)x);
    }

    /// <summary>Name-value pairs from <paramref name="first"/> on, each name matched in any case by a prefix that names one.</summary>
    public static Dictionary<string, string> Options(IReadOnlyList<JgsValue> args, int first, string function, (string Name, string[] Choices)[] options, int line, int col)
    {
        string[] names = Array.ConvertAll(options, static o => o.Name);
        if ((args.Count - first) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "JGraph:pcsc:NameValue", $"{function} takes name-value pairs; the names are {string.Join(", ", names)}.");
        }

        var chosen = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = first; i < args.Count; i += 2)
        {
            string written = DeviceChecks.IsText(args[i]) ? DeviceChecks.Text(args[i]) : "";
            if (!DeviceChecks.Match(written, names, out string? name, out bool ambiguous) || ambiguous)
            {
                throw new JgsRuntimeException(line, col, "JGraph:pcsc:NameValue", $"'{written}' is not a name {function} takes; the names are {string.Join(", ", names)}.");
            }

            string[] choices = Array.Find(options, o => o.Name == name).Choices;
            string value = DeviceChecks.IsText(args[i + 1]) ? DeviceChecks.Text(args[i + 1]) : "";
            string? choice = Array.Find(choices, c => c.Equals(value, StringComparison.OrdinalIgnoreCase));
            chosen[name!] = choice
                ?? throw new JgsRuntimeException(line, col, "JGraph:pcsc:NameValue", $"{name} is one of {string.Join(", ", choices.Select(static c => $"\"{c}\""))}.");
        }

        return chosen;
    }

    /// <summary>What an ISO 7816-4 status word says, for the ones a script meets; empty for the rest.</summary>
    public static string StatusMeaning(int sw) => (sw >> 8, sw & 0xFF) switch
    {
        (0x90, 0x00) => "success",
        (0x61, var n) => $"{n} more bytes are waiting for GET RESPONSE",
        (0x62, 0x83) => "the selected file is invalidated",
        (0x63, var n) when (n & 0xF0) == 0xC0 => $"verification failed, {n & 0x0F} tries left",
        (0x67, 0x00) => "wrong length",
        (0x69, 0x82) => "security status not satisfied",
        (0x69, 0x83) => "authentication method blocked",
        (0x69, 0x85) => "conditions of use not satisfied",
        (0x6A, 0x80) => "incorrect data",
        (0x6A, 0x82) => "file or application not found",
        (0x6A, 0x86) => "incorrect P1 P2",
        (0x6A, 0x88) => "referenced data not found",
        (0x6C, var n) => $"wrong Le; the right one is {n}",
        (0x6D, 0x00) => "instruction not supported",
        (0x6E, 0x00) => "class not supported",
        (0x6F, 0x00) => "no precise diagnosis",
        _ => "",
    };

    /// <summary><c>T = jgraph.pcsc.readers</c>: each reader, its state, whether a card is in it, and the card's ATR.</summary>
    public static JgsValue ReadersTable(DeviceSession session, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        IReadOnlyList<SmartCardReader> readers = List(session, line, col);
        int n = readers.Count;
        JgsValue Text(Func<SmartCardReader, string> pick) => JgsValue.StringArray(readers.Select(r => JgsValue.Str(pick(r))).ToArray(), n, 1);
        JgsValue present = n == 0 ? JgsBuiltins.EmptyLogical(0, 1) : JgsValue.Array(readers.Select(static r => JgsValue.Bool(r.Present)).ToArray());
        present.Reshape(n, 1);
        (string Name, JgsValue Value)[] columns =
        [
            ("Reader", Text(static r => r.Reader)),
            ("State", Text(static r => r.State)),
            ("CardPresent", present),
            ("ATR", Text(static r => HexText(r.Atr))),
        ];
        return JgsValue.Table(new JGraph.Data.Table(columns.Select(c => JgsBuiltins.TableColumnFrom("jgraph.pcsc.readers", c.Name, c.Value, line, col)).ToList()));
    }

    public static IReadOnlyList<SmartCardReader> List(DeviceSession session, int line, int col)
    {
        try
        {
            return Backend(session).Readers();
        }
        catch (SmartCardException e)
        {
            throw new JgsRuntimeException(line, col, "JGraph:pcsc:Failed", e.Message);
        }
    }

    /// <summary><c>code = jgraph.pcsc.ctlcode(n)</c>: SCARD_CTL_CODE(n), the control code of a reader function number.</summary>
    public static JgsValue ControlCode(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count != 1 || !DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(args[0])) || DeviceChecks.Count(args[0]) != 1
            || DeviceChecks.Numbers(args[0]).First() is var n && (n != Math.Floor(n) || n < 0 || n > 4095))
        {
            throw new JgsRuntimeException(line, col, "JGraph:pcsc:ControlCode", "Valid syntax is jgraph.pcsc.ctlcode(N), with N a function number from 0 to 4095.");
        }

        return JgsValue.Number(SmartCardWords.ControlCode((int)DeviceChecks.Numbers(args[0]).First()));
    }
}

/// <summary>
/// <c>c = jgraph.pcsc.connect(reader, Share=, Protocol=)</c> (device classes plan, stage D12, ADR
/// 0196): a connection to the card in a reader, through PC/SC. <c>[resp, sw] = transmit(c, apdu)</c>
/// sends a command and answers the response data and the status word as hex text;
/// <c>control(c, code, data)</c> reaches the reader itself; <c>status</c>, <c>beginTransaction</c>,
/// <c>endTransaction</c>, <c>reconnect</c> and <c>disconnect(c, Disposition=)</c> are PC/SC's. A
/// status the caller does not take is not dropped: <c>resp = transmit(c, apdu)</c> throws unless the
/// card answered 9000.
/// </summary>
internal sealed class PcscCardObject : DeviceObject
{
    private static readonly (string, string[])[] ConnectOptions =
        [("Share", ["shared", "exclusive", "direct"]), ("Protocol", ["any", "T0", "T1", "raw"])];

    private static readonly string[] Dispositions = ["leave", "reset", "unpower", "eject"];

    private static readonly DeviceClass Declaration = Declare();

    private readonly ISmartCard _card;
    private readonly ISmartCardBackend _backend;
    private string _share;
    private string _atr;
    private bool _inTransaction;
    private JgsValue _userData = JgsEmpty.Zero();

    private PcscCardObject(DeviceSession session, Interpreter interpreter, ISmartCardBackend backend, ISmartCard card, string share)
        : base(session, interpreter)
    {
        _backend = backend;
        _card = card;
        _share = share;
        _atr = ReadAtr();
    }

    public override DeviceClass Class => Declaration;

    public override string? Summary() => Deleted ? "disconnected" : $"{_card.Reader} ({_card.Protocol}, {_share})";

    private string ReadAtr()
    {
        try
        {
            return PcscShared.HexText(_card.Atr());
        }
        catch (SmartCardException)
        {
            return "";
        }
    }

    // --- connecting ------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>jgraph.pcsc.connect()</c> for the one reader that holds a card, <c>connect(reader)</c> by name,
    /// index or row of jgraph.pcsc.readers, each with <c>Share=</c> ("shared", "exclusive", "direct") and
    /// <c>Protocol=</c> ("any", "T0", "T1", "raw").
    /// </summary>
    public static JgsValue Connect(DeviceSession session, Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        int first = args.Count % 2;
        Dictionary<string, string> options = PcscShared.Options(args, first, "jgraph.pcsc.connect", ConnectOptions, line, col);
        string share = options.GetValueOrDefault("Share", "shared");
        string protocol = options.GetValueOrDefault("Protocol", "any");
        IReadOnlyList<SmartCardReader> readers = PcscShared.List(session, line, col);
        string reader = first == 1 ? Named(readers, args[0], line, col) : Only(readers, share, line, col);
        ISmartCardBackend backend = PcscShared.Backend(session);
        ISmartCard card;
        try
        {
            card = backend.Connect(reader, share, protocol);
        }
        catch (SmartCardException e)
        {
            throw new JgsRuntimeException(line, col, "JGraph:pcsc:ConnectFailed", e.Message);
        }

        var made = new PcscCardObject(session, interpreter, backend, card, share);
        session.Remember(made);
        JgsValue value = JgsValue.External(made);
        JgsLifetime.Minted(value);
        return value;
    }

    private static string Quoted(IEnumerable<SmartCardReader> readers) => string.Join(", ", readers.Select(static r => $"'{r.Reader}'"));

    /// <summary>The reader a call with none means: the one that holds a card, or the one reader when the connection is direct.</summary>
    private static string Only(IReadOnlyList<SmartCardReader> readers, string share, int line, int col)
    {
        if (readers.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "JGraph:pcsc:NoReaders", "No smart-card reader is attached.");
        }

        List<SmartCardReader> candidates = share == "direct" ? readers.ToList() : readers.Where(static r => r.Present).ToList();
        return candidates.Count switch
        {
            1 => candidates[0].Reader,
            0 => throw new JgsRuntimeException(line, col, "JGraph:pcsc:NoCard", $"No reader holds a card. The readers are: {Quoted(readers)}."),
            _ => throw new JgsRuntimeException(line, col, "JGraph:pcsc:SeveralReaders",
                $"{candidates.Count} readers {(share == "direct" ? "are attached" : "hold a card")}; name one of {Quoted(candidates)}."),
        };
    }

    /// <summary>A reader by its name (whole, in any case, else the one reader the text is part of), its index, or a row of jgraph.pcsc.readers.</summary>
    private static string Named(IReadOnlyList<SmartCardReader> readers, JgsValue asked, int line, int col)
    {
        if (readers.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "JGraph:pcsc:NoReaders", "No smart-card reader is attached.");
        }

        string name;
        if (asked.Type == JgsType.Table)
        {
            JgsValue names = JgsBuiltins.TableColumnValue(asked.AsTable, "Reader", line, col);
            if (!names.IsStringArray || names.ArrayLength != 1)
            {
                throw new JgsRuntimeException(line, col, "JGraph:pcsc:OneRow", $"jgraph.pcsc.connect takes one row of jgraph.pcsc.readers; this table has {names.ArrayLength}.");
            }

            name = names.ElementAt(0).AsString;
        }
        else if (DeviceChecks.IsText(asked))
        {
            name = DeviceChecks.Text(asked);
        }
        else if (DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(asked)) && DeviceChecks.Count(asked) == 1
            && DeviceChecks.Numbers(asked).First() is var k && k == Math.Floor(k) && k >= 1 && k <= readers.Count)
        {
            return readers[(int)k - 1].Reader;
        }
        else
        {
            throw new JgsRuntimeException(line, col, "JGraph:pcsc:NoReader",
                $"The reader must be a name, a row of jgraph.pcsc.readers, or an index from 1 to {readers.Count}.");
        }

        if (readers.FirstOrDefault(r => r.Reader.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } whole)
        {
            return whole.Reader;
        }

        List<SmartCardReader> partial = name.Length == 0 ? [] : readers.Where(r => r.Reader.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
        return partial.Count switch
        {
            1 => partial[0].Reader,
            0 => throw new JgsRuntimeException(line, col, "JGraph:pcsc:NoReader", $"No reader is named '{name}'. The readers are: {Quoted(readers)}."),
            _ => throw new JgsRuntimeException(line, col, "JGraph:pcsc:AmbiguousReader", $"'{name}' matches more than one reader: {Quoted(partial)}. Give more of the name."),
        };
    }

    // --- the object ------------------------------------------------------------------------------------------

    private static PcscCardObject Me(DeviceObject o) => (PcscCardObject)o;

    private static DeviceClass Declare()
    {
        var properties = new List<DeviceProperty>
        {
            new("Reader", static (o, _) => JgsValue.StringScalar(Me(o)._card.Reader)),
            new("ATR", static (o, _) => JgsValue.StringScalar(Me(o)._atr)),
            new("Protocol", static (o, _) => JgsValue.StringScalar(Me(o)._card.Protocol)),
            new("Share", static (o, _) => JgsValue.StringScalar(Me(o)._share)),
            new("InTransaction", static (o, _) => JgsValue.Bool(Me(o)._inTransaction)),
            new("UserData", static (o, _) => Me(o)._userData, static (o, v, _) => Me(o)._userData = v),
        };

        var methods = new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["transmit"] = static c => Me(c.Target).Transmit(c),
            ["control"] = static c => [Me(c.Target).Control(c)],
            ["status"] = static c => [Me(c.Target).Status(c)],
            ["beginTransaction"] = static c => Void(c, static c => Me(c.Target).BeginTransaction(c)),
            ["endTransaction"] = static c => Void(c, static c => Me(c.Target).EndTransaction(c)),
            ["reconnect"] = static c => Void(c, static c => Me(c.Target).Reconnect(c)),
            ["disconnect"] = static c => Void(c, static c => Me(c.Target).Disconnect(c)),
        };

        return new DeviceClass("jgraph.pcsc.Card", "Card", ["handle"], properties, methods,
            ["Card", "beginTransaction", "control", "delete", "disconnect", "endTransaction", "get", "isvalid", "reconnect", "set", "status", "transmit"],
            ["Reader", "ATR", "Protocol", "Share"]);
    }

    private static JgsValue[] Void(DeviceCall call, Action<DeviceCall> body)
    {
        if (call.Wanted > 0)
        {
            throw call.Error("MATLAB:TooManyOutputs", "Too many output arguments.");
        }

        body(call);
        return [];
    }

    /// <summary>Runs a card operation, turning PC/SC's failures into JGraph:pcsc:Failed.</summary>
    private static T Guard<T>(DeviceCall call, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (SmartCardException e)
        {
            throw call.Error("JGraph:pcsc:Failed", e.Message);
        }
    }

    /// <summary>
    /// A transaction another Card object of this session holds on the reader would make PC/SC wait for
    /// it to end, and a script has one thread: the call is refused instead.
    /// </summary>
    private void NotHeldElsewhere(DeviceCall call)
    {
        foreach (DeviceObject other in Session.Live)
        {
            if (other is PcscCardObject card && card != this && card._inTransaction && ReferenceEquals(card._backend, _backend) && card._card.Reader == _card.Reader)
            {
                throw call.Error("JGraph:pcsc:Busy",
                    $"Another Card object of this session holds a transaction on '{_card.Reader}', and this call would wait for it forever. End that transaction first.");
            }
        }
    }

    /// <summary><c>[resp, sw] = transmit(c, apdu)</c>.</summary>
    private JgsValue[] Transmit(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        if (call.Wanted > 2)
        {
            throw call.Error("MATLAB:TooManyOutputs", "Too many output arguments.");
        }

        if (call.Args.Count != 1)
        {
            throw call.Error("JGraph:pcsc:Nargin", "Valid syntax is [RESP, SW] = transmit(c, APDU), with APDU as bytes or hex text.");
        }

        byte[] apdu = PcscShared.ByteArgument(call.Args[0], "An APDU", call.Line, call.Column);
        if (apdu.Length is < 4 or > 65544)
        {
            throw call.Error("JGraph:pcsc:InvalidApdu", $"An APDU has from 4 to 65544 bytes: CLA INS P1 P2, then the length and data. This one has {apdu.Length}.");
        }

        NotHeldElsewhere(call);
        byte[] response = Guard(call, () => _card.Transmit(apdu));
        if (response.Length < 2)
        {
            throw call.Error("JGraph:pcsc:ShortResponse", $"The card answered {response.Length} bytes, too few to hold a status word.");
        }

        int sw = (response[^2] << 8) | response[^1];
        JgsValue data = PcscShared.Bytes(response[..^2]);
        if (call.Wanted >= 2)
        {
            return [data, JgsValue.StringScalar(sw.ToString("X4", CultureInfo.InvariantCulture))];
        }

        if (sw != 0x9000)
        {
            string meaning = PcscShared.StatusMeaning(sw);
            throw call.Error("JGraph:pcsc:Status",
                $"The card answered {sw:X4}{(meaning.Length > 0 ? $" ({meaning})" : "")}. To handle a status yourself, take it as a second output: [resp, sw] = transmit(c, apdu).");
        }

        return [data];
    }

    /// <summary><c>out = control(c, code, data)</c>: a reader control code; the code is a number or hex text.</summary>
    private JgsValue Control(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count is < 1 or > 2)
        {
            throw call.Error("JGraph:pcsc:Nargin", "Valid syntax is OUT = control(c, CODE) or control(c, CODE, DATA); jgraph.pcsc.ctlcode(N) makes a code from a function number.");
        }

        uint code;
        JgsValue asked = call.Args[0];
        if (DeviceChecks.IsText(asked) && DeviceChecks.Text(asked).Trim() is var text
            && uint.TryParse(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint parsed))
        {
            code = parsed;
        }
        else if (!DeviceChecks.IsText(asked) && DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(asked)) && DeviceChecks.Count(asked) == 1
            && DeviceChecks.Numbers(asked).First() is var n && n == Math.Floor(n) && n is >= 0 and <= uint.MaxValue)
        {
            code = (uint)n;
        }
        else
        {
            throw call.Error("JGraph:pcsc:ControlCode", "A control code is a number from 0 to 4294967295, or hex text (\"0x00313520\"); jgraph.pcsc.ctlcode(N) makes one from a function number.");
        }

        byte[] input = call.Args.Count == 2 ? PcscShared.ByteArgument(call.Args[1], "The data", call.Line, call.Column) : [];
        return PcscShared.Bytes(Guard(call, () => _card.Control(code, input)));
    }

    private JgsValue Status(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count != 0)
        {
            throw call.Error("JGraph:pcsc:Nargin", "Valid syntax is S = status(c).");
        }

        (string state, byte[] atr) = Guard(call, () => (_card.State(), _card.Atr()));
        return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Reader"] = JgsValue.StringScalar(_card.Reader),
            ["State"] = JgsValue.StringScalar(state),
            ["Protocol"] = JgsValue.StringScalar(_card.Protocol),
            ["ATR"] = JgsValue.StringScalar(PcscShared.HexText(atr)),
        });
    }

    private void BeginTransaction(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count != 0)
        {
            throw call.Error("JGraph:pcsc:Nargin", "Valid syntax is beginTransaction(c).");
        }

        NotHeldElsewhere(call);
        Guard(call, () =>
        {
            _card.BeginTransaction();
            return 0;
        });
        _inTransaction = true;
    }

    private void EndTransaction(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        string disposition = PcscShared.Options(call.Args, 0, "endTransaction", [("Disposition", Dispositions)], call.Line, call.Column).GetValueOrDefault("Disposition", "leave");
        Guard(call, () =>
        {
            _card.EndTransaction(disposition);
            return 0;
        });
        _inTransaction = false;
    }

    private void Reconnect(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        Dictionary<string, string> options = PcscShared.Options(call.Args, 0, "reconnect",
            [.. ConnectOptions, ("Initialization", ["leave", "reset", "unpower"])], call.Line, call.Column);
        string share = options.GetValueOrDefault("Share", _share);
        Guard(call, () =>
        {
            _card.Reconnect(share, options.GetValueOrDefault("Protocol", "any"), options.GetValueOrDefault("Initialization", "leave"));
            return 0;
        });
        _share = share;
        _atr = ReadAtr();
    }

    /// <summary><c>disconnect(c, Disposition=)</c>: ends the connection, doing to the card what the disposition says; the object is deleted.</summary>
    private void Disconnect(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        string disposition = PcscShared.Options(call.Args, 0, "disconnect", [("Disposition", Dispositions)], call.Line, call.Column).GetValueOrDefault("Disposition", "leave");
        _card.Disconnect(disposition);
        Delete();
    }

    protected override void OnDelete()
    {
        _inTransaction = false;
        _card.Dispose();
    }
}

/// <summary>
/// <c>w = jgraph.pcsc.watch(@fcn)</c> (device classes plan, stage D12, ADR 0196): runs
/// <c>fcn(w, evt)</c> when a card is inserted or removed, or a reader arrives or leaves, at the device
/// queue's drain points (a <c>pause</c>, <c>drawnow</c>, the idle prompt). <c>evt.Type</c> is
/// <c>"CardInserted"</c>, <c>"CardRemoved"</c>, <c>"ReaderAdded"</c> or <c>"ReaderRemoved"</c>,
/// <c>evt.Reader</c> the reader, <c>evt.ATR</c> an inserted card's ATR, <c>evt.AbsTime</c> when.
/// <c>delete(w)</c>, or its last holder going, stops it.
/// </summary>
internal sealed class PcscWatchObject : DeviceObject
{
    private static readonly DeviceClass Declaration = new(
        "jgraph.pcsc.Watcher",
        "Watcher",
        ["handle"],
        [
            new DeviceProperty("Callback", static (o, _) => ((PcscWatchObject)o)._callback),
            new DeviceProperty("Events", static (o, _) => JgsValue.Number(((PcscWatchObject)o)._events)),
        ],
        new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal),
        ["Watcher", "delete", "get", "isvalid"],
        ["Callback", "Events"]);

    private readonly JgsValue _callback;
    private readonly DeviceEventQueue _queue;
    private readonly IDisposable _watch;
    private int _events;

    private PcscWatchObject(DeviceSession session, Interpreter interpreter, JgsValue callback)
        : base(session, interpreter)
    {
        _callback = callback;
        _queue = DeviceEventQueue.ForCurrentThread();
        _watch = PcscShared.Backend(session).Watch(OnChanged);
    }

    public override DeviceClass Class => Declaration;

    public override string? Summary() => Deleted ? "stopped" : $"{_events} events";

    public static JgsValue Start(DeviceSession session, Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count != 1 || args[0].Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col, "JGraph:pcsc:Nargin", "Valid syntax is w = jgraph.pcsc.watch(@FCN); FCN is called as FCN(w, evt).");
        }

        var watch = new PcscWatchObject(session, interpreter, args[0]);
        session.Remember(watch);
        JgsValue made = JgsValue.External(watch);
        JgsLifetime.Minted(made);
        return made;
    }

    /// <summary>On the backend's thread: the event is stamped and queued for the script's.</summary>
    private void OnChanged(SmartCardEvent e)
    {
        DateTime at = DateTime.Now;
        _queue.Post(() => Fire(e, at));
    }

    private void Fire(SmartCardEvent e, DateTime at)
    {
        if (Deleted)
        {
            return;
        }

        _events++;
        JgsValue evt = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Type"] = JgsValue.StringScalar(e.Type),
            ["Reader"] = JgsValue.StringScalar(e.Reader),
            ["ATR"] = JgsValue.StringScalar(PcscShared.HexText(e.Atr)),
            ["AbsTime"] = JgsBuiltins.DatetimeValue(at),
        });
        try
        {
            JgsCallbacks.Invoke(_callback.AsCallable, [JgsValue.External(this), evt], 0, 0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JgsException failure)
        {
            JgsBuiltins.Warn(Session.Host, "JGraph:pcsc:CallbackError", "Error executing the jgraph.pcsc.watch callback:\n" + failure.Message);
        }
    }

    protected override void OnDelete() => _watch.Dispose();
}
