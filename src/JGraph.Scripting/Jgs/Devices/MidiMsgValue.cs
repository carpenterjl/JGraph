using System.Globalization;
using System.Text;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>One MIDI message: its eight raw bytes (the first lowest) and its timestamp.</summary>
internal readonly record struct MidiMsgItem(ulong Raw, double Timestamp)
{
    public byte this[int i] => (byte)(Raw >> (8 * i));

    public MidiMsgItem WithByte(int i, byte value) =>
        this with { Raw = (Raw & ~(0xFFUL << (8 * i))) | ((ulong)value << (8 * i)) };

    public static MidiMsgItem Of(ReadOnlySpan<byte> bytes, double timestamp)
    {
        ulong raw = 0;
        for (int i = 0; i < Math.Min(8, bytes.Length); i++)
        {
            raw |= (ulong)bytes[i] << (8 * i);
        }

        return new MidiMsgItem(raw, timestamp);
    }
}

/// <summary>
/// A <c>midimsg</c> array (device classes plan, stage D10b, ADR 0194): Audio Toolbox's value class,
/// transcribed from R2025b's midimsg.m. Each element keeps eight raw bytes and a timestamp; every
/// other property is read from the bytes as the class's get methods read them, and written back as
/// its set methods write them.
/// </summary>
internal sealed class MidiMsgValue : IJgsExternalArray
{
    public MidiMsgValue(MidiMsgItem[] items, int rows, int columns)
    {
        Items = items;
        Rows = rows;
        Columns = columns;
    }

    public MidiMsgItem[] Items { get; }

    public int Rows { get; }

    public int Columns { get; }

    public string ClassName => "midimsg";

    public bool IsHandle => false;

    public string Kind => "midimsg array";

    public bool IsA(string className) => className == "midimsg";

    public IJgsExternal CopyForBinding() => this; // never changed in place

    public static JgsValue Of(MidiMsgItem[] items, int rows, int columns) => JgsValue.External(new MidiMsgValue(items, rows, columns));

    public static JgsValue Scalar(MidiMsgItem item) => Of([item], 1, 1);

    public IJgsExternalArray Build(IReadOnlyList<(IJgsExternalArray Source, int Index)?> elements, int rows, int columns)
    {
        var items = new MidiMsgItem[rows * columns];
        for (int i = 0; i < items.Length; i++)
        {
            items[i] = elements[i] is ({ } source, int index) ? ((MidiMsgValue)source).Items[index] : default;
        }

        return new MidiMsgValue(items, rows, columns);
    }

    /// <summary>A double becomes midimsg(x), as MATLAB converts it for a join or a store (<c>[a; 1]</c>).</summary>
    public IJgsExternalArray Coerce(JgsValue value, int line, int col)
    {
        JgsValue made = MidiMsgs.Construct([value], line, col);
        return (IJgsExternalArray)made.AsExternal;
    }

    public JgsValue GetMember(int index, string name, int line, int col) => MidiMsgs.Get(Items[index], name, line, col);

    public IJgsExternalArray WithMember(int index, string name, JgsValue value, int line, int col)
    {
        MidiMsgItem[] items = (MidiMsgItem[])Items.Clone();
        items[index] = MidiMsgs.Set(Items[index], name, value, line, col);
        return new MidiMsgValue(items, Rows, Columns);
    }

    public bool SameAs(IJgsExternalArray other) =>
        other is MidiMsgValue that && that.Rows == Rows && that.Columns == Columns && that.Items.AsSpan().SequenceEqual(Items);

    public string Disp() => MidiMsgs.Disp(this);

    public string Display() => Disp().TrimStart();

    public string? Summary() => Items.Length == 1 ? MidiMsgs.TypeName(Items[0]) : null;
}

/// <summary>
/// A <c>midimsgtype</c> array: R2025b's enumeration over int32. JGraph has no enumeration classes, so
/// its members are a small value array that answers what R2025b's do (probe_midi_msg): names as text,
/// numbers, <c>==</c> element by element against members, a name or numbers, and the order and
/// arithmetic of int32. It indexes, joins and grows as every <see cref="IJgsExternalArray"/> does, so
/// <c>[msgs.Type] == midimsgtype.NoteOn</c> picks the note-ons out of a list.
/// </summary>
internal sealed class MidiTypeValue : IJgsExternalArray
{
    private MidiTypeValue(int[] codes, int rows, int columns)
    {
        Codes = codes;
        Rows = rows;
        Columns = columns;
    }

    /// <summary>Each member's number, column-major.</summary>
    public int[] Codes { get; }

    public int Rows { get; }

    public int Columns { get; }

    /// <summary>The first member's name (the name of a scalar).</summary>
    public string Name => NameOf(Codes[0]);

    public int Code => Codes[0];

    public bool IsScalar => Codes.Length == 1;

    public string ClassName => "midimsgtype";

    public bool IsHandle => false;

    public string Kind => "enumeration member";

    public bool IsA(string className) => className is "midimsgtype" or "int32";

    public IJgsExternal CopyForBinding() => this;

    public static string NameOf(int code) => code switch
    {
        0 => "Data",
        -1 => "Undefined",
        _ => MidiMsgs.TypeNames[code - 1],
    };

    public static JgsValue Of(string name) => JgsValue.External(new MidiTypeValue([MidiMsgs.CodeOf(name)], 1, 1));

    public static JgsValue Column(IEnumerable<string> names)
    {
        int[] codes = names.Select(MidiMsgs.CodeOf).ToArray();
        return JgsValue.External(new MidiTypeValue(codes, codes.Length, codes.Length == 0 ? 0 : 1));
    }

    public static bool TryMember(string name, out JgsValue member)
    {
        bool known = Array.IndexOf(MidiMsgs.TypeNames, name) >= 0;
        member = known ? Of(name) : JgsValue.Null;
        return known;
    }

    public IJgsExternalArray Build(IReadOnlyList<(IJgsExternalArray Source, int Index)?> elements, int rows, int columns)
    {
        var codes = new int[rows * columns];
        for (int i = 0; i < codes.Length; i++)
        {
            // A gap growth fills takes the default member, the first the enumeration lists.
            codes[i] = elements[i] is ({ } source, int index) ? ((MidiTypeValue)source).Codes[index] : 1;
        }

        return new MidiTypeValue(codes, rows, columns);
    }

    /// <summary>Text or a number joined to members becomes the member it names, as midimsgtype(x) makes it.</summary>
    public IJgsExternalArray Coerce(JgsValue value, int line, int col) => (IJgsExternalArray)Construct([value], line, col).AsExternal;

    public JgsValue GetMember(int index, string name, int line, int col) =>
        throw new JgsRuntimeException(line, col, "MATLAB:noSuchMethodOrField",
            $"Unrecognized method, property, or field '{name}' for class 'midimsgtype'.");

    public IJgsExternalArray WithMember(int index, string name, JgsValue value, int line, int col) =>
        throw new JgsRuntimeException(line, col, "MATLAB:noPublicFieldForClass", $"Unrecognized property '{name}' for class 'midimsgtype'.");

    public bool SameAs(IJgsExternalArray other) =>
        other is MidiTypeValue that && that.Rows == Rows && that.Columns == Columns && that.Codes.AsSpan().SequenceEqual(Codes);

    /// <summary>disp: each row of names, the columns padded to their longest and four spaces apart.</summary>
    public string Disp()
    {
        if (Codes.Length == 0)
        {
            return $"  {Rows}×{Columns} empty midimsgtype enumeration array";
        }

        int[] widths = new int[Columns];
        for (int c = 0; c < Columns; c++)
        {
            for (int r = 0; r < Rows; r++)
            {
                widths[c] = Math.Max(widths[c], NameOf(Codes[(c * Rows) + r]).Length);
            }
        }

        var lines = new List<string>();
        for (int r = 0; r < Rows; r++)
        {
            var line = new StringBuilder();
            for (int c = 0; c < Columns; c++)
            {
                string name = NameOf(Codes[(c * Rows) + r]);
                line.Append("    ").Append(c == Columns - 1 ? name : name.PadRight(widths[c]));
            }

            lines.Add(line.ToString());
        }

        return string.Join("\n", lines);
    }

    public string Display() => IsScalar
        ? $"midimsgtype enumeration\n\n    {Name}"
        : $"{Rows}×{Columns} midimsgtype enumeration array\n\n{Disp()}";

    public string? Summary() => IsScalar ? Name : null;

    /// <summary>
    /// <c>==</c> element by element against members, one name as text, or numbers: a logical array of
    /// the shape of whichever side is not a scalar. Null when the shapes do not agree.
    /// </summary>
    public JgsValue? Equal(JgsValue other)
    {
        int[] theirs;
        int rows;
        int columns;
        if (other.AsExternalOrNull() is MidiTypeValue members)
        {
            (theirs, rows, columns) = (members.Codes, members.Rows, members.Columns);
        }
        else if (DeviceChecks.IsText(other))
        {
            string text = DeviceChecks.Text(other);
            (theirs, rows, columns) = ([Array.IndexOf(MidiMsgs.TypeNames, text) >= 0 ? MidiMsgs.CodeOf(text) : int.MinValue], 1, 1);
        }
        else if (DeviceChecks.NumericClasses.Append("logical").Contains(DeviceChecks.ClassOf(other)))
        {
            double[] numbers = DeviceChecks.Numbers(other).ToArray();
            theirs = numbers.Select(static n => n == Math.Floor(n) && n >= int.MinValue + 1 && n <= int.MaxValue ? (int)n : int.MinValue).ToArray();
            (rows, columns) = numbers.Length == 1 ? (1, 1) : (JgsMatrix.RowCount(other), JgsMatrix.ColCount(other));
        }
        else
        {
            return JgsValue.Bool(false);
        }

        int count = Math.Max(Codes.Length, theirs.Length);
        if (!(Codes.Length == 1 || theirs.Length == 1 || (Rows == rows && Columns == columns)))
        {
            return null;
        }

        var same = new double[count];
        for (int i = 0; i < count; i++)
        {
            same[i] = Codes[Codes.Length == 1 ? 0 : i] == theirs[theirs.Length == 1 ? 0 : i] ? 1 : 0;
        }

        (int outRows, int outColumns) = Codes.Length == 1 && theirs.Length != 1 ? (rows, columns) : (Rows, Columns);
        if (count == 1)
        {
            return JgsValue.Bool(same[0] != 0);
        }

        JgsValue mask = JgsValue.Array(same.Select(static b => JgsValue.Bool(b != 0)).ToArray());
        mask.Reshape(outRows, outColumns);
        return mask;
    }

    /// <summary>Whether a value names this (scalar) member: a member, its name as text, or its number.</summary>
    public bool Matches(JgsValue value) => Equal(value) is { Type: JgsType.Bool } answer && answer.AsNumber != 0;

    /// <summary>The members as the numbers they stand for, in a numeric class (int32 unless told otherwise).</summary>
    public JgsValue Numbers(JgsNumericClass numericClass = JgsNumericClass.Int32)
    {
        double[] values = Codes.Select(static c => (double)c).ToArray();
        JgsValue plain = values.Length == 1 ? JgsValue.Number(values[0]) : JgsMatrix.FromColumnMajor(values, Rows, Columns);
        return JgsNumericClasses.Stamp(plain, numericClass);
    }

    /// <summary><c>midimsgtype(x)</c>: members by their numbers or a member by its name.</summary>
    public static JgsValue Construct(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count != 1)
        {
            throw new JgsRuntimeException(line, col, args.Count == 0 ? "MATLAB:minrhs" : "MATLAB:TooManyInputs",
                args.Count == 0 ? "Not enough input arguments." : "Too many input arguments.");
        }

        if (args[0].AsExternalOrNull() is MidiTypeValue already)
        {
            return JgsValue.External(already);
        }

        if (DeviceChecks.IsText(args[0]))
        {
            string text = DeviceChecks.Text(args[0]);
            return TryMember(text, out JgsValue named) ? named
                : throw new JgsRuntimeException(line, col, "MATLAB:class:CannotConvert",
                    $"Cannot convert '{text}' to 'midimsgtype'.");
        }

        if (DeviceChecks.NumericClasses.Append("logical").Contains(DeviceChecks.ClassOf(args[0])))
        {
            double[] numbers = DeviceChecks.Numbers(args[0]).ToArray();
            int[] known = MidiMsgs.TypeNames.Select(MidiMsgs.CodeOf).ToArray();
            if (numbers.All(n => known.Contains((int)n) && n == Math.Floor(n)))
            {
                int[] codes = numbers.Select(static n => (int)n).ToArray();
                (int rows, int columns) = codes.Length == 1 ? (1, 1) : (JgsMatrix.RowCount(args[0]), JgsMatrix.ColCount(args[0]));
                return JgsValue.External(new MidiTypeValue(codes, rows, columns));
            }
        }

        throw new JgsRuntimeException(line, col, "MATLAB:class:InvalidEnum",
            "Unable to construct an enumeration member of class 'midimsgtype' from the given input.");
    }
}

/// <summary>midimsg.m's constructor, get and set methods, validProperties, description and disp.</summary>
internal static class MidiMsgs
{
    /// <summary>midimsgtype's members, in the order its enumeration block lists them.</summary>
    public static readonly string[] TypeNames =
    [
        "NoteOn", "NoteOff", "PolyKeyPressure", "AllSoundOff", "ResetAllControllers", "LocalControl",
        "AllNotesOff", "OmniOff", "OmniOn", "MonoOn", "PolyOn", "ControlChange", "ProgramChange",
        "ChannelPressure", "PitchBend", "SystemExclusive", "MIDITimeCodeQuarterFrame", "SongPositionPointer",
        "SongSelect", "TuneRequest", "EOX", "TimingClock", "Start", "Continue", "Stop", "ActiveSensing",
        "SystemReset", "Data", "Undefined",
    ];

    public static int CodeOf(string name) => name switch
    {
        "Data" => 0,
        "Undefined" => -1,
        _ => Array.IndexOf(TypeNames, name) + 1,
    };

    /// <summary>The public properties, in the order <c>properties(msg)</c> lists them.</summary>
    public static readonly string[] PropertyNames =
    [
        "Type", "NumMsgBytes", "MsgBytes", "Timestamp", "Channel", "Note", "Velocity", "KeyPressure",
        "LocalControl", "MonoChannels", "CCNumber", "CCValue", "Program", "ChannelPressure", "PitchChange",
        "TimeCodeSequence", "TimeCodeValue", "SongPosition", "Song",
    ];

    private static readonly string[] ChannelTypes =
        ["NoteOff", "NoteOn", "PolyKeyPressure", "ControlChange", "ProgramChange", "ChannelPressure", "PitchBend"];

    private static readonly string[] SystemTypes =
    [
        "SystemExclusive", "MIDITimeCodeQuarterFrame", "SongPositionPointer", "SongSelect", "Undefined", "Undefined",
        "TuneRequest", "EOX", "TimingClock", "Undefined", "Start", "Continue", "Stop", "Undefined", "ActiveSensing", "SystemReset",
    ];

    private static readonly string[] ChannelModeTypes =
        ["AllSoundOff", "ResetAllControllers", "LocalControl", "AllNotesOff", "OmniOff", "OmniOn", "MonoOn", "PolyOn"];

    /// <summary>get.Type: the status byte's type through DecodeMidiByte, and a control change's mode through DecodeChannelModeByte.</summary>
    public static string TypeName(MidiMsgItem m)
    {
        byte status = m[0];
        if (status < 0x80)
        {
            return "Data";
        }

        if (status >= 0xF0)
        {
            return SystemTypes[status - 0xF0];
        }

        string type = ChannelTypes[(status >> 4) - 8];
        return type == "ControlChange" && m[1] >= 120 && m[1] <= 127 ? ChannelModeTypes[m[1] - 120] : type;
    }

    /// <summary>get.NumMsgBytes: a status byte's message length, or the data bytes before an F4 terminus.</summary>
    public static int NumMsgBytes(MidiMsgItem m)
    {
        if (m[0] > 127)
        {
            return JGraph.Devices.Midi.MidiWire.LengthOf(m[0]);
        }

        for (int i = 0; i < 8; i++)
        {
            if (m[i] == 0xF4)
            {
                return i;
            }
        }

        return 8;
    }

    public static byte[] MsgBytes(MidiMsgItem m)
    {
        byte[] bytes = new byte[NumMsgBytes(m)];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = m[i];
        }

        return bytes;
    }

    /// <summary>validProperties: the type-specific properties a message of this type answers.</summary>
    public static string[] ValidProperties(string type) => type switch
    {
        "NoteOn" or "NoteOff" => ["Channel", "Note", "Velocity"],
        "PolyKeyPressure" => ["Channel", "Note", "KeyPressure"],
        "AllSoundOff" or "ResetAllControllers" or "AllNotesOff" or "OmniOff" or "OmniOn" or "PolyOn" => ["Channel"],
        "LocalControl" => ["Channel", "LocalControl"],
        "MonoOn" => ["Channel", "MonoChannels"],
        "ControlChange" => ["Channel", "CCNumber", "CCValue"],
        "ProgramChange" => ["Channel", "Program"],
        "ChannelPressure" => ["Channel", "ChannelPressure"],
        "PitchBend" => ["Channel", "PitchChange"],
        "MIDITimeCodeQuarterFrame" => ["TimeCodeSequence", "TimeCodeValue"],
        "SongPositionPointer" => ["SongPosition"],
        "SongSelect" => ["Song"],
        _ => [],
    };

    private static void ValidFor(MidiMsgItem m, string property, int line, int col)
    {
        string type = TypeName(m);
        string[] valid = ValidProperties(type);
        if (Array.IndexOf(valid, property) < 0)
        {
            throw new JgsRuntimeException(line, col, "audio:midi:MidiMsgInvalidPropertyAccess",
                $"Accessing property {property} is not valid for {type} MIDI messages. Valid properties are: {string.Join(", ", valid)}.");
        }
    }

    /// <summary>Bytes as a uint8 row; none as a 0-by-0 uint8, which is what <c>uint8([])</c> is.</summary>
    internal static JgsValue Uint8Row(byte[] bytes) => JgsNumericClasses.Stamp(
        bytes.Length == 0 ? JgsMatrix.FromColumnMajor([], 0, 0) : TransportClient.Row(Array.ConvertAll(bytes, static b => (double)b)),
        JgsNumericClass.UInt8);

    /// <summary><c>msg.name</c> on one message.</summary>
    public static JgsValue Get(MidiMsgItem m, string name, int line, int col)
    {
        switch (name)
        {
            case "Type":
                return MidiTypeValue.Of(TypeName(m));
            case "NumMsgBytes":
                return JgsValue.Number(NumMsgBytes(m));
            case "MsgBytes":
                return Uint8Row(MsgBytes(m));
            case "Timestamp":
                return JgsValue.Number(m.Timestamp);
            case "RawBytes":
                return Uint8Row(Enumerable.Range(0, 8).Select(i => m[i]).ToArray());
            case "DecodeMidiByte" or "DecodeChannelModeByte":
                throw new JgsRuntimeException(line, col, "JGraph:midimsg:DecodeTable",
                    $"midimsg's hidden constant {name} is not readable in JGraph.");
        }

        if (Array.IndexOf(PropertyNames, name) < 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:noSuchMethodOrField",
                $"Unrecognized method, property, or field '{name}' for class 'midimsg'.");
        }

        ValidFor(m, name, line, col);
        return name switch
        {
            "Channel" => JgsValue.Number((m[0] % 16) + 1),
            "Note" or "CCNumber" or "ChannelPressure" or "Program" or "Song" => JgsValue.Number(m[1]),
            "CCValue" or "KeyPressure" or "Velocity" or "MonoChannels" => JgsValue.Number(m[2]),
            "LocalControl" => JgsValue.Bool(m[2] != 0),
            "PitchChange" or "SongPosition" => JgsValue.Number((m[2] * 128) + m[1]),
            "TimeCodeSequence" => JgsValue.Number(m[1] >> 4),
            "TimeCodeValue" => JgsValue.Number(m[1] & 15),
            _ => throw new InvalidOperationException(name),
        };
    }

    /// <summary><c>msg.name = value</c> on one message: the set methods, and Timestamp's (1,1) double.</summary>
    public static MidiMsgItem Set(MidiMsgItem m, string name, JgsValue value, int line, int col)
    {
        switch (name)
        {
            case "Type" or "NumMsgBytes" or "MsgBytes" or "RawBytes" or "DecodeMidiByte" or "DecodeChannelModeByte":
                throw new JgsRuntimeException(line, col, "MATLAB:class:SetProhibited",
                    $"Unable to set the '{name}' property of class ''midimsg'' because it is read-only.");
            case "Timestamp":
                return m with { Timestamp = TimestampProperty(value, line, col) };
        }

        if (Array.IndexOf(PropertyNames, name) < 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:noPublicFieldForClass", $"Unrecognized property '{name}' for class 'midimsg'.");
        }

        ValidFor(m, name, line, col);
        switch (name)
        {
            case "Channel":
                return m.WithByte(0, (byte)((m[0] & 240) + Channel(value, line, col)));
            case "Note":
                return m.WithByte(1, Data(value, 0, 127, "Note", line, col));
            case "CCNumber":
                return m.WithByte(1, Data(value, 0, 119, "CCNumber", line, col));
            case "CCValue":
                return m.WithByte(2, Data(value, 0, 127, "ccvalue", line, col));
            case "KeyPressure":
                return m.WithByte(2, Data(value, 0, 127, "pressure", line, col));
            case "ChannelPressure":
                return m.WithByte(1, Data(value, 0, 127, "ChannelPressure", line, col));
            case "Program":
                return m.WithByte(1, Data(value, 0, 127, "program", line, col));
            case "Song":
                return m.WithByte(1, Data(value, 0, 127, "song", line, col));
            case "LocalControl":
                return m.WithByte(2, LocalControl(value, line, col));
            case "Velocity":
                return m.WithByte(2, Data(value, 0, 127, "Velocity", line, col));
            case "MonoChannels":
                return m.WithByte(2, Data(value, 0, 16, "MonoChannels", line, col));
            case "PitchChange":
            case "SongPosition":
            {
                int change = Data14(value, "change", line, col);
                return m.WithByte(1, (byte)(change % 128)).WithByte(2, (byte)(change / 128));
            }

            case "TimeCodeSequence":
                return m.WithByte(1, (byte)((Data(value, 0, 7, "TimeCodeSequence", line, col) * 16) + (m[1] & 15)));
            case "TimeCodeValue":
                return m.WithByte(1, (byte)((m[1] & 240) + Data(value, 0, 15, "TimeCodeValue", line, col)));
            default:
                throw new InvalidOperationException(name);
        }
    }

    /// <summary>Timestamp's declaration, <c>(1,1) double</c>: a value converted to double, then required to be one.</summary>
    private static double TimestampProperty(JgsValue value, int line, int col)
    {
        string cls = DeviceChecks.ClassOf(value);
        if (!(DeviceChecks.NumericClasses.Contains(cls) || cls is "char" or "logical"))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validation:UnableToConvert",
                $"Error setting property 'Timestamp' of class 'midimsg'. Value must be double or be convertible to double.");
        }

        if (DeviceChecks.Count(value) != 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validation:IncompatibleSize",
                "Error setting property 'Timestamp' of class 'midimsg'. Value must be a scalar.");
        }

        return cls == "char" ? DeviceChecks.Text(value)[0] : DeviceChecks.Numbers(value).First();
    }

    // --- the file's local validators ---------------------------------------------------------------------------

    private static DeviceChecks.Subject Named(string name) => new("midimsg", name);

    /// <summary>validateDataValue: a scalar, real, finite, integer, non-NaN number between the bounds.</summary>
    private static byte Data(JgsValue value, int min, int max, string name, int line, int col) =>
        (byte)DataValue(value, min, max, name, line, col);

    private static int Data14(JgsValue value, string name, int line, int col) => (int)DataValue(value, 0, 16383, name, line, col);

    private static double DataValue(JgsValue value, int min, int max, string name, int line, int col)
    {
        DeviceChecks.Classes(value, ["numeric"], Named(name), line, col);
        DeviceChecks.Attributes(value,
            ["scalar", "real", "finite", "integer", "nonnan", $"ge:{min.ToString(CultureInfo.InvariantCulture)}", $"le:{max.ToString(CultureInfo.InvariantCulture)}"],
            Named(name), line, col);
        return DeviceChecks.Numbers(value).First();
    }

    /// <summary>validateChannel: 1 to 16, answered as 0 to 15.</summary>
    private static int Channel(JgsValue value, int line, int col) => (int)DataValue(value, 1, 16, "channel", line, col) - 1;

    /// <summary>validateLocalControl: a logical scalar, 127 for true.</summary>
    private static byte LocalControl(JgsValue value, int line, int col)
    {
        DeviceChecks.Classes(value, ["logical"], Named("localcontrol"), line, col);
        DeviceChecks.Attributes(value, ["scalar"], Named("localcontrol"), line, col);
        return DeviceChecks.Numbers(value).First() != 0 ? (byte)127 : (byte)0;
    }

    /// <summary>validateTimestamp: a real numeric scalar, as a double.</summary>
    private static double Timestamp(JgsValue value, int line, int col, string name = "timestamp")
    {
        DeviceChecks.Classes(value, ["numeric"], Named(name), line, col);
        DeviceChecks.Attributes(value, ["scalar", "real"], Named(name), line, col);
        return DeviceChecks.Numbers(value).First();
    }

    private static void ArgCount(int count, int low, int high, int line, int col)
    {
        if (count < low)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (count > high)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }
    }

    // --- the constructor ------------------------------------------------------------------------------------------

    private static readonly string[] LegalTypes = [.. TypeNames, "Note"];

    /// <summary><c>midimsg(msgType, …)</c>, <c>midimsg(size)</c> and <c>midimsg</c>, in midimsg.m's order of checks.</summary>
    public static JgsValue Construct(IReadOnlyList<JgsValue> args, int line, int col)
    {
        int nargin = args.Count;
        if (nargin == 0)
        {
            return MidiMsgValue.Scalar(default);
        }

        JgsValue msgType = args[0];
        if (msgType.AsExternalOrNull() is MidiTypeValue member)
        {
            msgType = JgsValue.Str(member.Name);
        }
        else if (msgType.IsStringArray && msgType.ArrayLength == 1 && msgType.ElementAt(0).Type == JgsType.String)
        {
            msgType = JgsValue.Str(msgType.ElementAt(0).AsString);
        }

        string msgClass = DeviceChecks.ClassOf(msgType);
        if (nargin == 1 && (DeviceChecks.NumericClasses.Contains(msgClass)))
        {
            var sizeWho = new DeviceChecks.Subject("midimsg", "midimsgArraySize", 2);
            DeviceChecks.Attributes(msgType, ["integer", "nonnegative"], sizeWho, line, col);
            double[] dims = DeviceChecks.Numbers(msgType).ToArray();
            (int rows, int columns) = dims.Length switch
            {
                0 => (0, 0),
                1 => ((int)dims[0], (int)dims[0]),
                _ when dims.Skip(2).All(static d => d == 1) => ((int)dims[0], (int)dims[1]),
                _ when dims.Skip(2).Any(static d => d == 0) => (0, 0),
                _ => throw new JgsRuntimeException(line, col, "JGraph:externalArray:NDims",
                    "An object array of more than two dimensions is not supported in JGraph."),
            };
            return MidiMsgValue.Of(new MidiMsgItem[rows * columns], rows, columns);
        }

        var typeWho = new DeviceChecks.Subject("midimsg", "msgType", 1);
        if (msgClass is not ("char" or "string"))
        {
            throw DeviceChecks.TypeRefusal(typeWho, "char, string, midimsgtype", msgClass, line, col, namesCell: true);
        }

        string type = DeviceChecks.ValidateString(msgType, LegalTypes, typeWho, line, col);
        JgsValue V(int k) => args[k]; // varargin{k}
        double Stamp(int count) => nargin == count ? Timestamp(V(nargin - 1), line, col) : 0;

        MidiMsgItem Status(int status, int low, int high, params (int Index, byte Value)[] bytes)
        {
            ArgCount(nargin, low, high, line, col);
            MidiMsgItem m = default(MidiMsgItem).WithByte(0, (byte)status);
            foreach ((int index, byte value) in bytes)
            {
                m = m.WithByte(index, value);
            }

            return m with { Timestamp = Stamp(high) };
        }

        MidiMsgItem ChannelMessage(int status, int low, int high, Func<(int Index, byte Value)[]> rest)
        {
            ArgCount(nargin, low, high, line, col);
            int channel = Channel(V(1), line, col);
            MidiMsgItem m = default(MidiMsgItem).WithByte(0, (byte)(status + channel));
            foreach ((int index, byte value) in rest())
            {
                m = m.WithByte(index, value);
            }

            return m with { Timestamp = Stamp(high) };
        }

        switch (type)
        {
            case "NoteOn":
                return MidiMsgValue.Scalar(ChannelMessage(0x90, 4, 5, () => [(1, Data(V(2), 0, 127, "note", line, col)), (2, Data(V(3), 0, 127, "velocity", line, col))]));
            case "NoteOff":
                return MidiMsgValue.Scalar(ChannelMessage(0x80, 4, 5, () => [(1, Data(V(2), 0, 127, "note", line, col)), (2, Data(V(3), 0, 127, "velocity", line, col))]));
            case "Note":
            {
                ArgCount(nargin, 5, 6, line, col);
                int channel = Channel(V(1), line, col) + 1;
                byte note = Data(V(2), 0, 127, "note", line, col);
                byte velocity = Data(V(3), 0, 127, "velocity", line, col);
                double duration = Timestamp(V(4), line, col, "duration");
                double on = nargin == 6 ? Timestamp(V(nargin - 1), line, col) : 0;
                MidiMsgItem first = MidiMsgItem.Of([(byte)(0x90 + channel - 1), note, velocity], on);
                MidiMsgItem second = MidiMsgItem.Of([(byte)(0x90 + channel - 1), note, 0], on + duration);
                return MidiMsgValue.Of([first, second], 2, 1);
            }

            case "PolyKeyPressure":
                return MidiMsgValue.Scalar(ChannelMessage(0xA0, 4, 5, () => [(1, Data(V(2), 0, 127, "note", line, col)), (2, Data(V(3), 0, 127, "pressure", line, col))]));
            case "ControlChange":
                return MidiMsgValue.Scalar(ChannelMessage(0xB0, 4, 5, () => [(1, Data(V(2), 0, 119, "ccnumber", line, col)), (2, Data(V(3), 0, 127, "ccvalue", line, col))]));
            case "AllSoundOff":
                return MidiMsgValue.Scalar(ChannelMessage(0xB0, 2, 3, () => [(1, 120), (2, 0)]));
            case "ResetAllControllers":
                return MidiMsgValue.Scalar(ChannelMessage(0xB0, 2, 3, () => [(1, 121), (2, 0)]));
            case "LocalControl":
                return MidiMsgValue.Scalar(ChannelMessage(0xB0, 3, 4, () => [(1, 122), (2, LocalControl(V(2), line, col))]));
            case "AllNotesOff":
                return MidiMsgValue.Scalar(ChannelMessage(0xB0, 2, 3, () => [(1, 123), (2, 0)]));
            case "OmniOff":
                return MidiMsgValue.Scalar(ChannelMessage(0xB0, 2, 3, () => [(1, 124), (2, 0)]));
            case "OmniOn":
                return MidiMsgValue.Scalar(ChannelMessage(0xB0, 2, 3, () => [(1, 125), (2, 0)]));
            case "MonoOn":
                return MidiMsgValue.Scalar(ChannelMessage(0xB0, 3, 4, () => [(1, 126), (2, Data(V(2), 0, 16, "M", line, col))]));
            case "PolyOn":
                return MidiMsgValue.Scalar(ChannelMessage(0xB0, 2, 3, () => [(1, 127), (2, 0)]));
            case "ProgramChange":
                return MidiMsgValue.Scalar(ChannelMessage(0xC0, 3, 4, () => [(1, Data(V(2), 0, 127, "program", line, col))]));
            case "ChannelPressure":
                return MidiMsgValue.Scalar(ChannelMessage(0xD0, 3, 4, () => [(1, Data(V(2), 0, 127, "pressure", line, col))]));
            case "PitchBend":
                return MidiMsgValue.Scalar(ChannelMessage(0xE0, 3, 4, () =>
                {
                    int change = Data14(V(2), "change", line, col);
                    return [(1, (byte)(change % 128)), (2, (byte)(change / 128))];
                }));
            case "SystemExclusive":
                if (nargin < 3)
                {
                    return MidiMsgValue.Scalar(Status(0xF0, 1, 2));
                }
                else
                {
                    ArgCount(nargin, 3, 3, line, col);
                    double stamp = Timestamp(V(nargin - 1), line, col);
                    MidiMsgValue body = (MidiMsgValue)Construct([JgsValue.Str("Data"), V(1), JgsValue.Number(stamp)], line, col).AsExternal;
                    return MidiMsgValue.Of(
                        [new MidiMsgItem(0xF0, stamp), .. body.Items, new MidiMsgItem(0xF7, stamp)], body.Items.Length + 2, 1);
                }

            case "MIDITimeCodeQuarterFrame":
            {
                ArgCount(nargin, 3, 4, line, col);
                byte sequence = Data(V(1), 0, 7, "n", line, col);
                byte value = Data(V(2), 0, 15, "d", line, col);
                return MidiMsgValue.Scalar(MidiMsgItem.Of([0xF1, (byte)((16 * sequence) + value)], Stamp(4)));
            }

            case "SongPositionPointer":
            {
                ArgCount(nargin, 2, 3, line, col);
                int position = Data14(V(1), "position", line, col);
                return MidiMsgValue.Scalar(MidiMsgItem.Of([0xF2, (byte)(position % 128), (byte)(position / 128)], Stamp(3)));
            }

            case "SongSelect":
            {
                ArgCount(nargin, 2, 3, line, col);
                byte song = Data(V(1), 0, 127, "song", line, col);
                return MidiMsgValue.Scalar(MidiMsgItem.Of([0xF3, song], Stamp(3)));
            }

            case "TuneRequest":
                return MidiMsgValue.Scalar(Status(0xF6, 1, 2));
            case "EOX":
                return MidiMsgValue.Scalar(Status(0xF7, 1, 2));
            case "TimingClock":
                return MidiMsgValue.Scalar(Status(0xF8, 1, 2));
            case "Start":
                return MidiMsgValue.Scalar(Status(0xFA, 1, 2));
            case "Continue":
                return MidiMsgValue.Scalar(Status(0xFB, 1, 2));
            case "Stop":
                return MidiMsgValue.Scalar(Status(0xFC, 1, 2));
            case "ActiveSensing":
                return MidiMsgValue.Scalar(Status(0xFE, 1, 2));
            case "SystemReset":
                return MidiMsgValue.Scalar(Status(0xFF, 1, 2));
            case "Undefined":
                return MidiMsgValue.Scalar(Status(0xF4, 1, 2));
            case "Data":
            {
                ArgCount(nargin, 2, 3, line, col);
                var dataWho = new DeviceChecks.Subject(null, null, 2);
                DeviceChecks.Classes(V(1), ["numeric"], dataWho, line, col);
                DeviceChecks.Attributes(V(1), ["nonempty", "ge:0", "le:127", "real", "finite", "integer", "nonnan"], dataWho, line, col);
                byte[] bytes = DeviceChecks.Numbers(V(1)).Select(static d => (byte)d).ToArray();
                double stamp = Stamp(3);
                int count = (bytes.Length + 7) / 8;
                var items = new MidiMsgItem[count];
                for (int k = 0; k < count; k++)
                {
                    int left = bytes.Length - (8 * k);
                    items[k] = left >= 8
                        ? MidiMsgItem.Of(bytes.AsSpan(8 * k, 8), stamp)
                        : MidiMsgItem.Of([.. bytes.AsSpan(8 * k, left), 0xF4], stamp);
                }

                return MidiMsgValue.Of(items, count, 1);
            }

            default:
                throw new InvalidOperationException("unexpected msg type");
        }
    }

    // --- display ----------------------------------------------------------------------------------------------------

    private static string F(string format, params object[] values) =>
        JgsSprintf.FormatMatlab(format, values.Select(static v => v switch
        {
            string s => JgsValue.Str(s),
            int i => JgsValue.Number(i),
            double d => JgsValue.Number(d),
            _ => throw new InvalidOperationException(),
        }).ToList());

    /// <summary>description(msg): the type, the type's own properties and the timestamp, as disp lists them.</summary>
    public static string Description(MidiMsgItem m)
    {
        string type = TypeName(m);
        int channel = (m[0] % 16) + 1;
        string annotation = type switch
        {
            "PolyKeyPressure" => F(" %s: %-2d %s: %-3d %s: %-3d", "Channel", channel, "Note", (int)m[1], "KeyPressure", (int)m[2]),
            "NoteOff" or "NoteOn" => F(" %s: %-2d %s: %-3d %s: %-3d", "Channel", channel, "Note", (int)m[1], "Velocity", (int)m[2]),
            "AllSoundOff" or "ResetAllControllers" or "AllNotesOff" or "OmniOff" or "OmniOn" or "PolyOn" => F(" %s: %-2d", "Channel", channel),

            // foo{1 + msg.LocalControl > 63} is foo{false}, an empty list, so sprintf stops at the %s.
            "LocalControl" => F(" %s: %-2d %s", "Channel", channel),
            "MonoOn" => F(" %s: %-2d %s: %-2d", "Channel", channel, "MonoChannels", (int)m[2]),
            "ControlChange" => F(" %s: %-2d %s: %-3d %s: %-3d", "Channel", channel, "CCNumber", (int)m[1], "CCValue", (int)m[2]),
            "ProgramChange" => F(" %s: %-2d %s: %-3d", "Channel", channel, "Program", (int)m[1]),
            "ChannelPressure" => F(" %s: %-2d %s: %-3d", "Channel", channel, "ChannelPressure", (int)m[1]),
            "PitchBend" => F(" %s: %-2d %s: %-3d", "Channel", channel, "PitchChange", (m[2] * 128) + m[1]),
            "MIDITimeCodeQuarterFrame" => F(" %s: %d %s: %-2d", "TimeCodeSequence", m[1] >> 4, "TimeCodeValue", m[1] & 15),
            "SongPositionPointer" => F(" %s: %-5d", "SongPosition", (m[2] * 128) + m[1]),
            "SongSelect" => F(" %s: %-3d", "Song", (int)m[1]),
            _ => "",
        };
        return F("%-15s%s %s: %g", type, annotation, "Timestamp", m.Timestamp);
    }

    /// <summary>midimsg's disp: a header, then a line an element with its description and bytes in hex.</summary>
    public static string Disp(MidiMsgValue msg)
    {
        string size = $"{msg.Rows}×{msg.Columns}";
        var sb = new StringBuilder();
        if (msg.Items.Length == 0)
        {
            sb.Append("  ").Append(size).Append(" midimsg array.\n");
            return sb.ToString();
        }

        sb.Append("  ").Append(msg.Items.Length == 1 ? "midimsg with properties" : $"{size} midimsg array").Append(":\n");
        foreach (MidiMsgItem m in msg.Items)
        {
            string bytes = string.Join(" ", MsgBytes(m).Select(static b => b.ToString("X2", CultureInfo.InvariantCulture)));
            sb.Append("    ").Append(Description(m)).Append("  [ ").Append(bytes).Append(" ]\n");
        }

        return sb.ToString();
    }
}
