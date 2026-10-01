using System.Text;
using JGraph.Devices;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// What one interface (<c>serialport</c>, <c>tcpclient</c>, …) sets on the shared client: its name in
/// R2025b's sentences, the object name its syntax lines use, whether <c>read</c> and <c>write</c> must
/// be given a precision, the identifiers its <c>ErrorRegistry</c> renames, and the ways its own client
/// class departs from GenericClient (tcpclient's <c>TCPCustomClient</c> reads in the precision's own
/// class, with no partial reads, and wraps the transport's refusals in its own identifiers).
/// </summary>
internal sealed class TransportInterface
{
    public required string Name { get; init; }

    public required string ObjectName { get; init; }

    /// <summary>The name in <c>MATLAB:&lt;Name&gt;:x</c> identifiers GenericClient's are renamed to.</summary>
    public string CapitalName { get; init; } = "";

    public bool PrecisionRequired { get; init; }

    /// <summary>The <c>transportlib:client:X</c> identifiers renamed to <see cref="ErrorPrefix"/> + X.</summary>
    public IReadOnlySet<string> TransportlibIds { get; init; } = new HashSet<string>();

    /// <summary>What a renamed identifier starts with: <c>serialport:serialport:</c>.</summary>
    public string? ErrorPrefix { get; init; }

    public IReadOnlySet<string> GenericClientIds { get; init; } = new HashSet<string>();

    public IReadOnlySet<string> WarningIds { get; init; } = new HashSet<string>();

    public string ReadFailedId { get; init; } = "transportlib:transport:readFailed";

    public string ReadFailedLead { get; init; } = "Error reading data from the transport.";

    public IReadOnlyDictionary<string, string> ExtraSyntax { get; init; } = new Dictionary<string, string>();

    /// <summary>tcpclient's read: the precision's own class, <c>read(t)</c> reads what is waiting, and a short read is an error.</summary>
    public bool NativeReads { get; init; }

    /// <summary>The identifier a read's transport refusal is wrapped in, with its message kept (tcpclient's readFailed).</summary>
    public string? ReadErrorWrap { get; init; }

    /// <summary>The same for a write (tcpclient's writeFailed).</summary>
    public string? WriteErrorWrap { get; init; }

    /// <summary>Whether <c>write(obj, data)</c> with no precision writes the data's own class (tcpclient) rather than uint8.</summary>
    public bool WriteInDataClass { get; init; }

    /// <summary>The syntax lines of <c>read</c> when the interface words them itself.</summary>
    public string? ReadSyntax { get; init; }

    /// <summary>The event class a "byte" callback receives: serialport's DataAvailableInfo, or the shared ByteAvailableInfo.</summary>
    public bool SharedEventInfo { get; init; }

    /// <summary>What flush's refusal calls its second input: "buffer", or udpport's "BUFFER".</summary>
    public string FlushBufferWord { get; init; } = "buffer";

    /// <summary>The words of a lost connection (the interface's ConnectionLost message).</summary>
    public string ConnectionLostText { get; init; } = "Unable to detect connection to the device. Ensure that the device is plugged in and create a new object.";

    /// <summary>The identifier a <c>transportlib:client:X</c> or <c>MATLAB:GenericClient:x</c> error surfaces as.</summary>
    public string Translate(string id)
    {
        const string Client = "transportlib:client:";
        const string Generic = "MATLAB:GenericClient:";
        if (id.StartsWith(Client, StringComparison.Ordinal) && TransportlibIds.Contains(id[Client.Length..]))
        {
            return (ErrorPrefix ?? $"{Name}:{Name}:") + id[Client.Length..];
        }

        if (id.StartsWith(Generic, StringComparison.Ordinal) && GenericClientIds.Contains(id[Generic.Length..]))
        {
            return $"MATLAB:{CapitalName}:{id[Generic.Length..]}";
        }

        return id;
    }

    /// <summary>The identifier a <c>transportlib:client:XWarning</c> warning surfaces as.</summary>
    public string TranslateWarning(string key) => WarningIds.Contains(key) ? (ErrorPrefix ?? $"{Name}:{Name}:") + key : $"transportlib:client:{key}";

    /// <summary>A method's valid syntax lines (UserNotificationHandler.ValidInputArgsSyntax).</summary>
    public string Syntax(string method)
    {
        string o = ObjectName;
        return method switch
        {
            "read" => ReadSyntax is { } own ? own.Replace("{0}", o, StringComparison.Ordinal)
                : PrecisionRequired ? $"DATA = read({o},COUNT,PRECISION)" : $"DATA = read({o},COUNT)\nDATA = read({o},COUNT,PRECISION)",
            "readline" => $"DATA = readline({o})",
            "readbinblock" => $"DATA = readbinblock({o})\nDATA = readbinblock({o},PRECISION)",
            "write" => PrecisionRequired ? $"write({o},DATA,PRECISION)" : $"write({o},DATA)\nwrite({o},DATA,PRECISION)",
            "writeline" => $"writeline({o},DATA)",
            "writebinblock" => $"writebinblock({o},DATA,PRECISION)\nwritebinblock({o},DATA,PRECISION,HEADER)",
            "configureCallback" => $"configureCallback({o},\"off\")\nconfigureCallback({o},\"terminator\",CALLBACKFCN)\nconfigureCallback({o},\"byte\",COUNT,CALLBACKFCN)",
            "configureTerminator" => $"configureTerminator({o},TERMINATOR)\nconfigureTerminator({o},READTERMINATOR,WRITETERMINATOR)",
            "flush" => $"flush({o})\nflush({o},BUFFER)",
            "writeread" => $"RESPONSE = writeread({o},COMMAND)",
            _ => ExtraSyntax.TryGetValue(method, out string? extra) ? string.Format(System.Globalization.CultureInfo.InvariantCulture, extra, o) : method,
        };
    }

    public JgsRuntimeException NarginSingular(string method, int line, int col) =>
        new(line, col, Translate("transportlib:client:IncorrectInputArgumentsSingular"),
            $"Invalid number of input arguments for '{method}'. Valid syntax is {Syntax(method)}.");

    public JgsRuntimeException NarginPlural(string method, int line, int col) =>
        new(line, col, Translate("transportlib:client:IncorrectInputArgumentsPlural"),
            $"Invalid number of input arguments for '{method}'. Valid syntaxes are\n{Syntax(method)}");
}

/// <summary>A terminator: what the property shows and the bytes it stands for.</summary>
internal sealed record TerminatorSpec(JgsValue Shown, byte[] Bytes)
{
    public static readonly TerminatorSpec LF = new(JgsValue.StringScalar("LF"), [10]);
    public static readonly TerminatorSpec CR = new(JgsValue.StringScalar("CR"), [13]);
    public static readonly TerminatorSpec CRLF = new(JgsValue.StringScalar("CR/LF"), [13, 10]);

    /// <summary>The name for a text terminator; the character for a numeric one (the legacy mixins' switch).</summary>
    public string Word => Shown.IsStringArray ? Shown.ElementAt(0).AsString : ((char)Bytes[0]).ToString();
}

/// <summary>
/// The shared transport client (device classes plan, architecture C), transcribed from R2025b's
/// <c>GenericClient</c>, <c>ClientImpl</c> and <c>UserNotificationHandler</c>: <c>read</c>,
/// <c>write</c>, <c>readline</c>, <c>writeline</c>, <c>writeread</c>, binblock, <c>flush</c>,
/// <c>configureTerminator</c>, <c>configureCallback</c> and the callbacks, with every refusal in R2025b's
/// identifiers and words. One client serves every interface; <see cref="TransportInterface"/> carries
/// what differs.
/// </summary>
internal sealed class TransportClient
{
    private readonly object _gate = new();
    private readonly DeviceEventQueue _queue;
    private long _byteCounter;
    private bool _pendingCr;

    public TransportClient(IDeviceTransport transport, TransportInterface spec, DeviceObject owner)
    {
        Transport = transport;
        Spec = spec;
        Owner = owner;
        _queue = DeviceEventQueue.ForCurrentThread();
        transport.Input.Appended += OnAppended;
        transport.ConnectionLost += OnConnectionLost;
    }

    public IDeviceTransport Transport { get; }

    public TransportInterface Spec { get; }

    /// <summary>The object callbacks receive as their source.</summary>
    public DeviceObject Owner { get; }

    public TerminatorSpec ReadTerminator { get; private set; } = TerminatorSpec.LF;

    public TerminatorSpec WriteTerminator { get; private set; } = TerminatorSpec.LF;

    /// <summary>Whether the terminator was configured as a read/write pair (the property is then a cell).</summary>
    public bool TerminatorPair { get; private set; }

    /// <summary>The <c>Timeout</c> property, as it was given.</summary>
    public JgsValue Timeout { get; set; } = JgsValue.Number(10);

    public double TimeoutSeconds => Timeout.AsNumber;

    public string BytesAvailableFcnMode { get; private set; } = "off";

    public double BytesAvailableFcnCount { get; private set; } = 64;

    /// <summary>The callback, or null for none (R2025b's empty function_handle).</summary>
    public JgsValue? BytesAvailableFcn { get; private set; }

    public JgsValue? ErrorOccurredFcn { get; set; }

    public JgsValue UserData { get; set; } = JgsEmpty.Zero();

    public bool BigEndian { get; set; }

    /// <summary>Whether the connection was lost (a USB adapter unplugged).</summary>
    public bool Lost { get; private set; }

    public int NumBytesAvailable => Transport.Input.Count;

    public double NumBytesWritten => Transport.BytesWritten;

    /// <summary>The <c>Terminator</c> property: a name, a number, or a cell of the read and write ones.</summary>
    public JgsValue TerminatorValue => TerminatorPair ? JgsValue.Cell([ReadTerminator.Shown, WriteTerminator.Shown]) : ReadTerminator.Shown;

    /// <summary>What a blocking call is cancelled by: the run's Stop.</summary>
    private CancellationToken Cancel => Owner.Interpreter.Cancellation;

    // --- read ------------------------------------------------------------------------------------------

    /// <summary><c>data = read(obj, count, precision)</c>.</summary>
    public JgsValue Read(DeviceCall call)
    {
        if (Spec.NativeReads)
        {
            return ReadNative(call);
        }

        IReadOnlyList<JgsValue> args = call.Args;
        JgsValue precisionArg;
        if (Spec.PrecisionRequired)
        {
            if (args.Count != 2)
            {
                throw Spec.NarginSingular("read", call.Line, call.Column);
            }

            precisionArg = args[1];
        }
        else
        {
            if (args.Count is < 1 or > 2)
            {
                throw Spec.NarginPlural("read", call.Line, call.Column);
            }

            precisionArg = args.Count == 2 ? args[1] : JgsValue.Str("uint8");
        }

        JgsValue countArg = args[0];
        var client = new DeviceChecks.Subject("GenericClient", "count", 2);
        try
        {
            DeviceChecks.Classes(countArg, ["numeric"], client, call.Line, call.Column);
            DeviceChecks.Attributes(countArg, ["integer", "nonzero"], client, call.Line, call.Column);
        }
        catch (JgsRuntimeException e)
        {
            throw Renamed(e, call);
        }

        var channel = new DeviceChecks.Subject("AsyncIOTransportChannel", "count", 2);
        DeviceChecks.Attributes(countArg, ["scalar", "nonnegative"], channel, call.Line, call.Column);
        Precision precision = ParsePrecision(precisionArg, call);
        long count = (long)DeviceChecks.Numbers(countArg).First();
        int size = PrecisionCodec.Size(precision);
        long wantedBytes = count * size;
        LiveTransport(call);
        WaitWithTimers(() => Transport.Input.Count >= wantedBytes, call);
        byte[] bytes = Transport.Input.Take((int)Math.Min(wantedBytes, int.MaxValue));
        ThrowIfLost(call, bytes.Length);
        if (bytes.Length % size != 0)
        {
            throw call.Error(Spec.ReadFailedId,
                $"{Spec.ReadFailedLead}\nThe first input must contain a multiple of {size} elements to convert from real uint8 (8 bits) to real {PrecisionCodec.Name(precision)} ({size * 8} bits).");
        }

        long got = bytes.Length / size;
        if (got < count)
        {
            ReadWarning(call, "Read", got > 0);
        }

        if (bytes.Length == 0)
        {
            return JgsEmpty.Zero();
        }

        return precision switch
        {
            Precision.Char => JgsValue.Str(PrecisionCodec.Latin1(bytes)),
            Precision.String => JgsValue.StringScalar(PrecisionCodec.Latin1(bytes)),
            _ => Row(PrecisionCodec.Decode(bytes, precision, BigEndian)),
        };
    }

    /// <summary>
    /// tcpclient's read (TCPCustomClient): <c>read(t)</c> takes what is waiting, <c>read(t, n)</c> and
    /// <c>read(t, n, precision)</c> wait for all of it or fail — no partial reads — and the answer is in
    /// the precision's own class. The transport's refusals are wrapped in the interface's readFailed.
    /// </summary>
    private JgsValue ReadNative(DeviceCall call)
    {
        IReadOnlyList<JgsValue> args = call.Args.Select(Str2Char).ToArray();
        if (args.Count > 2)
        {
            throw Spec.NarginPlural("read", call.Line, call.Column);
        }

        Precision precision;
        long count;
        try
        {
            precision = args.Count == 2 ? ParsePrecision(args[1], call) : Precision.UInt8;
            if (args.Count >= 1)
            {
                var channel = new DeviceChecks.Subject("AsyncIOTransportChannel", "count", 2);
                DeviceChecks.Classes(args[0], ["numeric"], channel, call.Line, call.Column);
                DeviceChecks.Attributes(args[0], ["scalar", "nonnegative", "integer"], channel, call.Line, call.Column);
                count = (long)DeviceChecks.Numbers(args[0]).First();
            }
            else
            {
                count = Transport.Input.Count / PrecisionCodec.Size(precision);
            }
        }
        catch (JgsRuntimeException e) when (Spec.ReadErrorWrap is { } wrap)
        {
            throw call.Error(wrap, e.Message);
        }

        LiveTransport(call);
        if (count == 0)
        {
            return JgsEmpty.Zero();
        }

        long wanted = count * PrecisionCodec.Size(precision);
        if (!WaitWithTimers(() => Transport.Input.Count >= wanted, call))
        {
            ThrowIfLost(call, 0);
            throw call.Error(Spec.ReadErrorWrap ?? Spec.ReadFailedId,
                "Error receiving data from the remote server.\nAdditional Information: Operation timed out before requested data was received.");
        }

        byte[] bytes = Transport.Input.Take((int)wanted);
        return precision switch
        {
            Precision.Char => JgsValue.Str(PrecisionCodec.Latin1(bytes)),
            Precision.String => JgsValue.StringScalar(PrecisionCodec.Latin1(bytes)),
            _ => TypedRow(PrecisionCodec.Decode(bytes, precision, BigEndian), precision),
        };
    }

    /// <summary>A row in the class a precision names.</summary>
    public static JgsValue TypedRow(double[] values, Precision precision)
    {
        JgsValue row = Row(values);
        row.SetNumericClass(precision switch
        {
            Precision.UInt8 => JgsNumericClass.UInt8,
            Precision.Int8 => JgsNumericClass.Int8,
            Precision.UInt16 => JgsNumericClass.UInt16,
            Precision.Int16 => JgsNumericClass.Int16,
            Precision.UInt32 => JgsNumericClass.UInt32,
            Precision.Int32 => JgsNumericClass.Int32,
            Precision.UInt64 => JgsNumericClass.UInt64,
            Precision.Int64 => JgsNumericClass.Int64,
            Precision.Single => JgsNumericClass.Single,
            _ => JgsNumericClass.Double,
        });
        return row;
    }

    /// <summary>The precision a value's own class writes in, for a write given none (tcpclient).</summary>
    private static Precision PrecisionOfData(JgsValue data) => DeviceChecks.ClassOf(data) switch
    {
        "uint8" => Precision.UInt8,
        "int8" => Precision.Int8,
        "uint16" => Precision.UInt16,
        "int16" => Precision.Int16,
        "uint32" => Precision.UInt32,
        "int32" => Precision.Int32,
        "uint64" => Precision.UInt64,
        "int64" => Precision.Int64,
        "single" => Precision.Single,
        "double" => Precision.Double,
        _ => Precision.Char,
    };

    /// <summary><c>write(obj, data, precision)</c>.</summary>
    public void Write(DeviceCall call)
    {
        IReadOnlyList<JgsValue> args = call.Args;
        JgsValue precisionArg;
        if (Spec.PrecisionRequired)
        {
            if (args.Count != 2)
            {
                throw Spec.NarginSingular("write", call.Line, call.Column);
            }

            precisionArg = args[1];
        }
        else
        {
            if (args.Count is < 1 or > 2)
            {
                throw Spec.NarginPlural("write", call.Line, call.Column);
            }

            precisionArg = args.Count == 2 ? args[1] : JgsValue.Str("uint8");
        }

        double[] values;
        Precision precision;
        try
        {
            values = WriteData(args[0], call);
            precision = args.Count == 1 && Spec.WriteInDataClass ? PrecisionOfData(Str2Char(args[0])) : ParsePrecision(Str2Char(precisionArg), call);
        }
        catch (JgsRuntimeException e) when (Spec.WriteErrorWrap is { } wrap)
        {
            throw call.Error(wrap, e.Message);
        }

        LiveTransport(call);
        Send(PrecisionCodec.Encode(values, precision, BigEndian), call);
    }

    /// <summary>The transport's check of <c>write</c>'s data, then its elements as doubles.</summary>
    private static double[] WriteData(JgsValue data, DeviceCall call, string checker = "AsyncIOTransportChannel")
    {
        var who = new DeviceChecks.Subject(checker, "data", 2);
        string[] allowed = [.. DeviceChecks.NumericClasses, "string", "char"];
        string actual = DeviceChecks.ClassOf(data);
        if (Array.IndexOf(allowed, actual) < 0 || data.Type is JgsType.Cell or JgsType.Struct)
        {
            throw DeviceChecks.TypeRefusal(who, string.Join(", ", allowed), actual, call.Line, call.Column, namesCell: true);
        }

        if (data.IsStringArray && data.ArrayLength != 1)
        {
            throw DeviceChecks.TypeRefusal(who, string.Join(", ", allowed), null, call.Line, call.Column);
        }

        if (DeviceChecks.Count(data) == 0)
        {
            throw DeviceChecks.Expected(who, "expectedNonempty", "nonempty", call.Line, call.Column);
        }

        if (data.Type == JgsType.Array && data.Rows != 1 && data.Cols != 1 && !data.IsStringArray)
        {
            throw call.Error("transportlib:transport:invalidDataDim", "Input number 2, data, must be either a 1-by-n or n-by-1 array.");
        }

        if (data.IsStringArray)
        {
            data = data.ElementAt(0);
        }

        return DeviceChecks.NumberArray(data);
    }

    private static Precision ParsePrecision(JgsValue value, DeviceCall call, string checker = "AsyncIOTransportChannel")
    {
        var who = new DeviceChecks.Subject(checker, "precision", 3);
        if (!DeviceChecks.IsText(value))
        {
            throw DeviceChecks.TypeRefusal(who, "string, char", DeviceChecks.ClassOf(value), call.Line, call.Column);
        }

        return PrecisionCodec.Parse(DeviceChecks.ValidateString(value, PrecisionCodec.Names, who, call.Line, call.Column));
    }

    // --- lines -----------------------------------------------------------------------------------------

    /// <summary><c>data = readline(obj)</c>: up to the read terminator, which is dropped; [] and a warning on a timeout.</summary>
    public JgsValue ReadLine(DeviceCall call)
    {
        if (call.Args.Count != 0)
        {
            throw Spec.NarginSingular("readline", call.Line, call.Column);
        }

        LiveTransport(call);
        string? line = TakeLine(call);
        if (line is null)
        {
            ReadWarning(call, "Readline", someData: false);
            return JgsEmpty.Zero();
        }

        return JgsValue.StringScalar(line);
    }

    /// <summary>
    /// <c>readline</c> with the client's error on read (visadev's writeread): the line, or null when the
    /// terminator did not come in the timeout, and no warning.
    /// </summary>
    public JgsValue? TryReadLine(DeviceCall call)
    {
        LiveTransport(call);
        return TakeLine(call) is { } line ? JgsValue.StringScalar(line) : null;
    }

    /// <summary>Waits up to the timeout for a whole line and takes it; null when none came.</summary>
    private string? TakeLine(DeviceCall call)
    {
        byte[] terminator = ReadTerminator.Bytes;
        int end = -1;
        WaitWithTimers(() => (end = Transport.Input.IndexAfter(terminator)) >= 0, call);
        ThrowIfLost(call, end < 0 ? 0 : 1);
        if (end < 0)
        {
            return null;
        }

        byte[] taken = Transport.Input.Take(end);
        return PrecisionCodec.Latin1(taken.AsSpan(0, taken.Length - terminator.Length));
    }

    /// <summary><c>writeline(obj, data)</c>.</summary>
    public void WriteLine(DeviceCall call)
    {
        if (call.Args.Count != 1)
        {
            throw Spec.NarginSingular("writeline", call.Line, call.Column);
        }

        string text = LineText(call.Args[0], call);
        LiveTransport(call);
        Send([.. PrecisionCodec.TextBytes(text), .. WriteTerminator.Bytes], call);
    }

    /// <summary>The StringClient's check: a scalar string or a nonempty char row.</summary>
    private static string LineText(JgsValue data, DeviceCall call)
    {
        if (data.IsCharMatrix && data.Rows > 1)
        {
            throw call.Error("transportlib:transport:invalidDataDim", "Input number 2, data, must be either a 1-by-n or n-by-1 array.");
        }

        if (!DeviceChecks.IsText(data) || DeviceChecks.Text(data).Length == 0)
        {
            throw call.Error("transportlib:transport:invalidDataType", "Expected input number 2, data, to be a scalar string or a 1-by-n char array.");
        }

        return DeviceChecks.Text(data);
    }

    /// <summary><c>response = writeread(obj, command)</c>: the answer in the command's class; a timeout is an error.</summary>
    public JgsValue WriteRead(DeviceCall call)
    {
        if (call.Args.Count != 1)
        {
            throw Spec.NarginSingular("writeread", call.Line, call.Column);
        }

        string text = LineText(call.Args[0], call);
        LiveTransport(call);
        Send([.. PrecisionCodec.TextBytes(text), .. WriteTerminator.Bytes], call);
        string? line = TakeLine(call)
            ?? throw call.Error("transportclients:string:timeoutToken", "Error reading String.\nTimeout occurred waiting for the String Terminator.");
        return call.Args[0].Type == JgsType.String ? JgsValue.Str(line) : JgsValue.StringScalar(line);
    }

    /// <summary><c>configureTerminator(obj, t)</c> and <c>(obj, readT, writeT)</c>.</summary>
    public void ConfigureTerminator(DeviceCall call)
    {
        if (call.Args.Count is < 1 or > 2)
        {
            throw Spec.NarginPlural("configureTerminator", call.Line, call.Column);
        }

        TerminatorSpec read = ParseTerminator(call.Args[0], call);
        TerminatorSpec write = call.Args.Count == 2 ? ParseTerminator(call.Args[1], call) : read;
        lock (_gate)
        {
            ReadTerminator = read;
            WriteTerminator = write;
            TerminatorPair = call.Args.Count == 2;
            _pendingCr = false;
        }
    }

    /// <summary>Sets both terminators from a saved value (the preferences and <c>loadobj</c>).</summary>
    public void RestoreTerminator(TerminatorSpec read, TerminatorSpec write, bool pair)
    {
        ReadTerminator = read;
        WriteTerminator = write;
        TerminatorPair = pair;
    }

    public TerminatorSpec ParseTerminator(JgsValue value, DeviceCall call)
    {
        JgsRuntimeException Invalid() => call.Error(Spec.Translate("transportlib:client:InvalidTerminator"),
            "Terminator value must be \"LF\", \"CR\", \"CR/LF\", or an integer from 0 to 255, inclusive.");
        string cls = DeviceChecks.ClassOf(value);
        if (Array.IndexOf(DeviceChecks.NumericClasses, cls) >= 0)
        {
            double[] numbers = DeviceChecks.Numbers(value).ToArray();
            if (numbers.Length != 1 || !double.IsFinite(numbers[0]) || numbers[0] < 0 || numbers[0] != Math.Floor(numbers[0]) || numbers[0] > 255)
            {
                throw Invalid();
            }

            return new TerminatorSpec(value, [(byte)numbers[0]]);
        }

        if (!DeviceChecks.IsText(value) || DeviceChecks.Text(value).Length == 0
            || !DeviceChecks.Match(DeviceChecks.Text(value), ["LF", "CR", "CR/LF"], out string? word, out _))
        {
            throw Invalid();
        }

        return word switch
        {
            "LF" => TerminatorSpec.LF,
            "CR" => TerminatorSpec.CR,
            _ => TerminatorSpec.CRLF,
        };
    }

    // --- binblock --------------------------------------------------------------------------------------

    /// <summary><c>data = readbinblock(obj)</c>, <c>(obj, precision)</c>: IEEE 488.2 definite-length block.</summary>
    public JgsValue ReadBinblock(DeviceCall call)
    {
        if (call.Args.Count > 1)
        {
            throw Spec.NarginPlural("readbinblock", call.Line, call.Column);
        }

        Precision precision = call.Args.Count == 1 ? ParsePrecision(call.Args[0], call, "BinBlockClient") : Precision.UInt8;
        LiveTransport(call);
        byte[]? block = TakeBinblock(call);
        if (block is null)
        {
            ReadWarning(call, "Readbinblock", someData: false);
            return JgsEmpty.Zero();
        }

        int size = PrecisionCodec.Size(precision);
        int whole = block.Length / size * size;
        return precision switch
        {
            Precision.Char => JgsValue.Str(PrecisionCodec.Latin1(block)),
            Precision.String => JgsValue.StringScalar(PrecisionCodec.Latin1(block)),
            _ => block.Length == 0 ? JgsEmpty.Zero() : Row(PrecisionCodec.Decode(block.AsSpan(0, whole), precision, BigEndian)),
        };
    }

    /// <summary>
    /// Reads one block: skips to the '#', reads the digit count and the length, then the data. Answers
    /// null when the time runs out before the block is whole; throws the format error for a header that
    /// is not one.
    /// </summary>
    private byte[]? TakeBinblock(DeviceCall call)
    {
        InputBuffer input = Transport.Input;
        int hash = -1;
        WaitWithTimers(() => (hash = input.IndexAfter("#"u8)) >= 0, call);
        if (hash < 0)
        {
            return null;
        }

        input.Take(hash);
        if (!WaitWithTimers(() => input.Count >= 1, call))
        {
            return null;
        }

        byte digitsChar = input.Take(1)[0];
        if (digitsChar is < (byte)'0' or > (byte)'9')
        {
            throw BinblockFormat(call);
        }

        int digits = digitsChar - '0';
        if (digits == 0)
        {
            throw BinblockFormat(call);
        }

        if (!WaitWithTimers(() => input.Count >= digits, call))
        {
            return null;
        }

        string lengthText = Encoding.ASCII.GetString(input.Take(digits));
        if (!long.TryParse(lengthText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long length))
        {
            throw BinblockFormat(call);
        }

        // A block that stops short answers what came, with no warning (probe_sp_lines, rbb_short).
        WaitWithTimers(() => input.Count >= length, call);
        return input.Take((int)Math.Min(length, int.MaxValue));
    }

    private static JgsRuntimeException BinblockFormat(DeviceCall call) =>
        call.Error("transportclients:binblock:binBlockFormatError",
            "Format of received data is not compliant with IEEE 488.2 definite length arbitrary block response data format.\n<a href=\"matlab:helpview('instrument','instrument_binblock_format')\">Related documentation</a>");

    /// <summary><c>writebinblock(obj, data, precision)</c> and <c>(…, header)</c>.</summary>
    public void WriteBinblock(DeviceCall call)
    {
        if (call.Args.Count is < 2 or > 3)
        {
            throw Spec.NarginPlural("writebinblock", call.Line, call.Column);
        }

        double[] values = WriteData(call.Args[0], call, "BinBlockClient");
        Precision precision = ParsePrecision(call.Args[1], call, "BinBlockClient");
        string header = "";
        if (call.Args.Count == 3)
        {
            if (!DeviceChecks.IsText(call.Args[2]))
            {
                throw DeviceChecks.TypeRefusal(new DeviceChecks.Subject("BinBlockClient", "header", 4), "string, char",
                    DeviceChecks.ClassOf(call.Args[2]), call.Line, call.Column);
            }

            header = DeviceChecks.Text(call.Args[2]);
        }

        byte[] data = PrecisionCodec.Encode(values, precision, BigEndian);
        if (data.Length > 999_999_999)
        {
            throw call.Error("transportclients:binblock:maxWriteSizeExceeded", "Cannot write binblock with size greater than 999,999,999 bytes.");
        }

        string length = data.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        LiveTransport(call);
        Send([.. PrecisionCodec.TextBytes(header + "#" + length.Length + length), .. data], call);
    }

    // --- buffers ---------------------------------------------------------------------------------------

    /// <summary><c>flush(obj)</c>, <c>flush(obj, "input"|"output")</c>.</summary>
    public void Flush(DeviceCall call)
    {
        if (call.Args.Count > 1)
        {
            throw Spec.NarginPlural("flush", call.Line, call.Column);
        }

        bool input = true;
        bool output = true;
        if (call.Args.Count == 1)
        {
            var who = new DeviceChecks.Subject("flush", Spec.FlushBufferWord, 2);
            DeviceChecks.Classes(call.Args[0], ["char", "string"], who, call.Line, call.Column);
            DeviceChecks.Attributes(call.Args[0], ["nonempty"], who, call.Line, call.Column);
            string buffer = DeviceChecks.ValidateString(call.Args[0], ["input", "output"], who, call.Line, call.Column);
            input = buffer == "input";
            output = !input;
        }

        LiveTransport(call);
        if (input)
        {
            Transport.FlushInput();
            lock (_gate)
            {
                _byteCounter = 0;
                _pendingCr = false;
            }
        }

        if (output)
        {
            Transport.FlushOutput();
        }
    }

    // --- callbacks -------------------------------------------------------------------------------------

    /// <summary><c>configureCallback</c>'s three forms, told apart by how many arguments came.</summary>
    public void ConfigureCallback(DeviceCall call)
    {
        IReadOnlyList<JgsValue> args = call.Args;
        if (args.Count is < 1 or > 3)
        {
            throw Spec.NarginPlural("configureCallback", call.Line, call.Column);
        }

        JgsValue modeArg = Str2Char(args[0]);
        var who = new DeviceChecks.Subject("configureCallback", "mode", 2);
        DeviceChecks.Classes(modeArg, ["char"], who, call.Line, call.Column);
        DeviceChecks.Attributes(modeArg, ["nonempty"], who, call.Line, call.Column);
        string mode = DeviceChecks.ValidateString(modeArg, ["terminator", "byte", "off"], who, call.Line, call.Column);
        int wanted = mode switch { "off" => 1, "terminator" => 2, _ => 3 };
        if (args.Count != wanted)
        {
            string o = Spec.ObjectName;
            string syntax = mode switch
            {
                "off" => $"configureCallback({o},\"off\")",
                "terminator" => $"configureCallback({o},\"terminator\",@callbackFcn)",
                _ => $"configureCallback({o},\"byte\",count,@callbackFcn)",
            };
            throw call.Error(Spec.Translate("transportlib:client:IncorrectBytesAvailableModeSyntax"), $"Invalid input arguments. Valid syntax is {syntax}.");
        }

        lock (_gate)
        {
            BytesAvailableFcnMode = mode;
            _byteCounter = 0;
        }

        switch (mode)
        {
            case "off":
                BytesAvailableFcn = null;
                break;
            case "terminator":
                BytesAvailableFcn = CallbackValue(args[1], "InvalidBytesAvailableFcn", "BytesAvailableFcn must be a function handle.", call);
                break;
            default:
            {
                JgsValue count = Str2Char(args[1]);
                var countWho = new DeviceChecks.Subject(Spec.Name, "BytesAvailableFcnCount");
                DeviceChecks.Classes(count, ["numeric"], countWho, call.Line, call.Column);
                DeviceChecks.Attributes(count, ["scalar", "nonzero", "positive", "integer", "finite"], countWho, call.Line, call.Column);
                BytesAvailableFcnCount = DeviceChecks.Numbers(count).First();
                BytesAvailableFcn = CallbackValue(args[2], "InvalidBytesAvailableFcn", "BytesAvailableFcn must be a function handle.", call);
                break;
            }
        }
    }

    /// <summary>A callback property's value: a function handle, or <c>[]</c> for none.</summary>
    public JgsValue? CallbackValue(JgsValue value, string key, string sentence, DeviceCall call)
    {
        if (value.Type == JgsType.Function)
        {
            return value;
        }

        if (DeviceChecks.Count(value) == 0 && value.Type is JgsType.Array or JgsType.Null && !value.IsStringArray)
        {
            return null;
        }

        throw call.Error(Spec.Translate("transportlib:client:" + key), sentence);
    }

    private void OnAppended(ReadOnlySpan<byte> bytes, int waiting)
    {
        int events = 0;
        string mode;
        double count;
        lock (_gate)
        {
            mode = BytesAvailableFcnMode;
            count = BytesAvailableFcnCount;
            if (mode == "byte")
            {
                _byteCounter += bytes.Length;
                long step = (long)Math.Max(1, count);
                while (_byteCounter >= step)
                {
                    _byteCounter -= step;
                    events++;
                }
            }
            else if (mode == "terminator")
            {
                byte[] terminator = ReadTerminator.Bytes;
                foreach (byte b in bytes)
                {
                    if (terminator.Length == 1)
                    {
                        if (b == terminator[0])
                        {
                            events++;
                        }
                    }
                    else
                    {
                        if (_pendingCr && b == terminator[1])
                        {
                            events++;
                        }

                        _pendingCr = b == terminator[0];
                    }
                }
            }
        }

        DateTime at = DateTime.Now;
        double reported = mode == "byte" ? count : 1;
        for (int i = 0; i < events; i++)
        {
            _queue.Post(() => FireBytesAvailable(reported, at));
        }
    }

    /// <summary>
    /// Runs the <c>BytesAvailableFcn</c> for one event on the script thread, unless the mode was turned
    /// off after the event queued (GenericClient.callbackFunction). A failure inside it becomes R2025b's
    /// warning, and the script goes on.
    /// </summary>
    private void FireBytesAvailable(double count, DateTime at)
    {
        if (Owner.Deleted || BytesAvailableFcnMode == "off" || BytesAvailableFcn is not { } callback)
        {
            return;
        }

        DeviceObject info = !Spec.SharedEventInfo
            ? new DataAvailableInfo(Owner.Session, Owner.Interpreter, count, at)
            : BytesAvailableFcnMode == "byte"
                ? new SharedEventInfo(Owner.Session, Owner.Interpreter, "matlabshared.transportlib.internal.ByteAvailableInfo", "ByteAvailableInfo",
                    [("BytesAvailableFcnCount", JgsValue.Number(count)), ("AbsoluteTime", JgsBuiltins.DatetimeValue(at))])
                : new SharedEventInfo(Owner.Session, Owner.Interpreter, "matlabshared.transportlib.internal.TerminatorAvailableInfo", "TerminatorAvailableInfo",
                    [("AbsoluteTime", JgsBuiltins.DatetimeValue(at))]);
        try
        {
            JgsCallbacks.Invoke(callback.AsCallable, [JgsValue.External(Owner), JgsValue.External(info)], 0, 0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JgsException failure)
        {
            JgsBuiltins.Warn(Owner.Session.Host, "MATLAB:callback:DynamicPropertyEventError",
                "Error executing the BytesAvailableFcn callback:\n" + failure.Message);
        }
    }

    private void OnConnectionLost(DeviceConnectionLostException lost)
    {
        Lost = true;
        _queue.Post(() => FireErrorOccurred(lost));
    }

    /// <summary>
    /// The error event: the <c>ErrorOccurredFcn</c> with the error's information, or, with none set,
    /// its sentence on the error stream (GenericClient.errorCallbackFunction).
    /// </summary>
    private void FireErrorOccurred(DeviceConnectionLostException lost)
    {
        if (Owner.Deleted)
        {
            return;
        }

        string id = Spec.Name + ":" + Spec.Name + ":ConnectionLost";
        string message = ConnectionLostMessage;
        if (ErrorOccurredFcn is { } callback)
        {
            var info = new ErrorInfo(Owner.Session, Owner.Interpreter, id, message);
            try
            {
                JgsCallbacks.Invoke(callback.AsCallable, [JgsValue.External(info)], 0, 0);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (JgsException failure)
            {
                JgsBuiltins.Warn(Owner.Session.Host, "MATLAB:callback:error", "Error executing the ErrorOccurredFcn callback:\n" + failure.Message);
            }

            return;
        }

        Owner.Session.Host.WriteErr(message + "\n");
    }

    /// <summary>The sentence a lost connection prints (the interface's <c>ConnectionLost</c> message).</summary>
    public string ConnectionLostMessage { get; init; } =
        "Unable to detect connection to the device. Ensure that the device is plugged in and create a new object.";

    /// <summary>The event object of a callback: its class, short name and properties (fixed at the event).</summary>
    public sealed class SharedEventInfo : DeviceObject
    {
        private readonly DeviceClass _class;

        public SharedEventInfo(DeviceSession session, Interpreter interpreter, string className, string shortName, (string Name, JgsValue Value)[] values)
            : base(session, interpreter)
        {
            _class = new DeviceClass(className, shortName, ["handle"],
                values.Select(static v => new DeviceProperty(v.Name, (_, _) => v.Value)).ToList(),
                new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal), [], values.Select(static v => v.Name).ToList());
        }

        public override DeviceClass Class => _class;

        protected override void OnDelete()
        {
        }
    }

    // --- shared pieces -----------------------------------------------------------------------------------

    /// <summary>Writes bytes with the object's timeout; a write the port holds back past it is an error.</summary>
    private void Send(byte[] bytes, DeviceCall call)
    {
        try
        {
            Transport.Write(bytes, TimeSpan.FromSeconds(Math.Max(TimeoutSeconds, 0.001)), Cancel);
        }
        catch (TimeoutException timeout)
        {
            throw call.Error("transportlib:transport:writeFailed", $"Error writing data to the transport.\n{timeout.Message}");
        }
        catch (DeviceConnectionLostException)
        {
            throw LostError(call);
        }
        catch (DeviceIOException failure)
        {
            throw call.Error("transportlib:transport:writeFailed", $"Error writing data to the transport.\n{failure.Message}");
        }
    }

    /// <summary>
    /// Waits for <paramref name="condition"/> up to the object's <c>Timeout</c>, cancelled by Stop.
    /// Timers fire while it waits, as they do in R2025b's blocking reads (probe_sp_callbacks: eight
    /// ticks of a 0.1 s timer during a 0.7 s read); device callbacks do not.
    /// </summary>
    private bool WaitWithTimers(Func<bool> condition, DeviceCall call)
    {
        InputBuffer input = Transport.Input;
        long deadline = Environment.TickCount64 + (long)Math.Ceiling(TimeoutSeconds * 1000);
        JgsTimerScheduler? timers = call.Host.Timers;
        while (true)
        {
            long left = deadline - Environment.TickCount64;
            TimeSpan slice = TimeSpan.FromMilliseconds(Math.Max(0, Math.Min(left, timers is { Armed: true } ? 20 : 250)));
            if (input.WaitUntil(condition, slice, Cancel))
            {
                return true;
            }

            if (input.Fault is not null || left <= 0)
            {
                return false;
            }

            timers?.Drain();
        }
    }

    private void ReadWarning(DeviceCall call, string kind, bool someData)
    {
        string doc = someData ? $"{Spec.Name}_somedata" : $"{Spec.Name}_nodata";
        string what = someData ? "all requested" : "any";
        string method = kind.ToLowerInvariant();
        JgsBuiltins.Warn(call.Host, Spec.TranslateWarning(kind + "Warning"),
            $"The specified amount of data was not returned within the Timeout period for '{method}'.\n"
            + $"'{Spec.Name}' unable to read {what} data. For more information on possible reasons, see "
            + $"<a href=\"matlab: helpview('instrument', '{doc}')\"'>{Spec.Name} Read Warnings</a>.");
    }

    private void LiveTransport(DeviceCall call)
    {
        if (Lost)
        {
            throw LostError(call);
        }
    }

    private void ThrowIfLost(DeviceCall call, int got)
    {
        if (got == 0 && Transport.Input.Fault is not null)
        {
            Lost = true;
            throw LostError(call);
        }
    }

    private JgsRuntimeException LostError(DeviceCall call) =>
        call.Error($"{Spec.Name}:{Spec.Name}:ConnectionLost", ConnectionLostMessage);

    private JgsRuntimeException Renamed(JgsRuntimeException e, DeviceCall call) =>
        new(call.Line, call.Column, Spec.Translate(e.Identifier), e.Message);

    /// <summary>instrument.internal.stringConversionHelpers.str2char: a scalar string becomes its char row.</summary>
    public static JgsValue Str2Char(JgsValue value) =>
        value.IsStringArray && value.ArrayLength == 1 && value.ElementAt(0).Type == JgsType.String ? value.ElementAt(0) : value;

    /// <summary>A double row.</summary>
    public static JgsValue Row(double[] values) => JgsMatrix.FromColumnMajor(values, 1, values.Length);
}
