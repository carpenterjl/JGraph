using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using JGraph.Scripting.Jgs;

namespace JGraph.Runtime.Native;

/// <summary>
/// Arrays in the binary form the C++ shared library API writes and reads (compiler and SDK plan,
/// stage K12; native\jgrt\include\jgraph\execution.hpp's Writer and Reader): each array its
/// <c>ArrayType</c> as a byte, its dimensions, then its elements in column-major order - numbers,
/// characters and logicals as they lie in memory (complex parts interleaved), strings each a
/// present flag and its UTF-16, a cell's or struct's elements as arrays in turn, an enumeration's
/// class and member names, an object array's class and each element's engine handle and place, a
/// sparse matrix's rows, column starts and values. Read into the engine's values and made from them
/// on the engine thread.
/// </summary>
/// <remarks>
/// <para>
/// What R2025b passes and JGraph cannot hold is refused with a message rather than changed, as
/// stage K10's conversions refuse it (ADR 0266): a complex integer array, a complex or single
/// sparse array, an int64 or uint64 past 2^53. A sparse logical array comes in as JGraph's sparse
/// double. An object comes back only to the component that made it.
/// </para>
/// <para>
/// A Python package's calls (stage K13, <c>jgrtPyOpen</c>) carry three more forms, for what R2025b's
/// Python runtime converts by value (record-k13.ps1, k13_client: out.datetime, out.duration, out.dict,
/// dt.*, dict.*): a datetime as its wall-clock milliseconds since 1899-12-30 and its offset from UTC
/// when it has a zone, a duration as its milliseconds, a dictionary as its keys and values. The
/// matlab_pysdk runtime in the JGraph Runtime's python folder reads and writes them.
/// </para>
/// </remarks>
internal static class NativeWire
{
    // jgraph::data::ArrayType, by value.
    private const byte Logical = 0;
    private const byte Char = 1;
    private const byte StringType = 2;
    private const byte DoubleType = 3;
    private const byte ComplexDouble = 13;
    private const byte ComplexSingle = 14;
    private const byte CellType = 23;
    private const byte StructType = 24;
    private const byte ObjectType = 25;
    private const byte ValueObject = 26;
    private const byte HandleObjectRef = 27;
    private const byte EnumType = 28;
    private const byte SparseLogical = 29;
    private const byte SparseDouble = 30;
    private const byte SparseComplexDouble = 31;
    private const byte SparseSingle = 33;
    private const byte SparseComplexSingle = 34;

    // A Python package's own forms (stage K13), outside the Data API's ArrayType values.
    private const byte PythonDatetime = 100;
    private const byte PythonDuration = 101;
    private const byte PythonDictionary = 102;

    /// <summary>The format R2025b gives a datetime that comes from Python (k13_client: in.datetime).</summary>
    private const string PythonDatetimeFormat = "uuuu-MM-dd'T'HH:mm:ss.SSS'Z'";

    private const double ExactLimit = 9007199254740992.0; // 2^53

    /// <summary>The numeric classes by their ArrayType (DOUBLE to UINT64), and each element's size.</summary>
    private static readonly (string Class, int Size)[] Numbers =
    [
        ("double", 8), ("single", 4), ("int8", 1), ("uint8", 1), ("int16", 2), ("uint16", 2), ("int32", 4), ("uint32", 4), ("int64", 8), ("uint64", 8),
    ];

    /// <summary>Whether a call's first input is an object or enumeration, so the name may be one of its class's methods.</summary>
    internal static bool StartsWithObject(ReadOnlySpan<byte> inputs) =>
        inputs.Length > 0 && inputs[0] is ObjectType or ValueObject or HandleObjectRef or EnumType;

    /// <summary>A call's inputs as the engine's values for <paramref name="session"/>; a Python package's forms too with <paramref name="python"/>. Engine thread.</summary>
    /// <exception cref="ArgumentException">An input JGraph cannot hold, or an object another component made.</exception>
    internal static JgsValue[] ReadAll(byte[] bytes, ComponentSession session, bool python = false)
    {
        var reader = new Reader(bytes) { Python = python };
        var values = new List<JgsValue>();
        while (!reader.AtEnd)
        {
            values.Add(Read(reader, session));
        }

        return [.. values];
    }

    /// <summary>A call's outputs in the binary form; a Python package's forms too with <paramref name="python"/>. Engine thread.</summary>
    internal static byte[] WriteAll(IEnumerable<JgsValue> values, ComponentSession session, bool python = false)
    {
        var writer = new Writer { Python = python };
        foreach (JgsValue value in values)
        {
            Write(writer, value, session);
        }

        return writer.ToArray();
    }

    /// <summary>Text for a status other than a MATLAB error: its UTF-8, its length first.</summary>
    internal static byte[] Text(string text)
    {
        var writer = new Writer();
        writer.Text8(text);
        return writer.ToArray();
    }

    /// <summary>A MATLAB error's record: its identifier, message, stack (innermost first) and causes.</summary>
    internal static byte[] Error(string identifier, string message, IReadOnlyList<(string Name, string File, int Line)> frames, JgsValue? carried)
    {
        var writer = new Writer();
        WriteError(writer, identifier, message, frames, carried);
        return writer.ToArray();
    }

    private static void WriteError(Writer w, string identifier, string message, IReadOnlyList<(string Name, string File, int Line)> frames, JgsValue? carried)
    {
        w.Text8(identifier);
        w.Text16(message);
        w.U32((uint)frames.Count);
        foreach ((string name, string file, int line) in frames)
        {
            w.Text16(file);
            w.Text16(name);
            w.U64((ulong)Math.Max(line, 0));
        }

        JgsValue[] causes = carried is { Type: JgsType.Struct } thrown && thrown.AsStruct.TryGetValue("cause", out JgsValue? held) && held.Type == JgsType.Cell
            ? [.. held.AsCell.Where(static c => c.Type == JgsType.Struct)]
            : [];
        w.U32((uint)causes.Length);
        foreach (JgsValue cause in causes)
        {
            IReadOnlyDictionary<string, JgsValue> fields = cause.AsStruct;
            string id = fields.TryGetValue("identifier", out JgsValue? i) && i.Type == JgsType.String ? i.AsString : string.Empty;
            string text = fields.TryGetValue("message", out JgsValue? m) ? (m.Type == JgsType.String ? m.AsString : m.Display()) : string.Empty;
            WriteError(w, id, text, [], cause);
        }
    }

    // --- Reading ---------------------------------------------------------------------------------

    private static JgsValue Read(Reader r, ComponentSession session)
    {
        byte type = r.Byte();
        uint nd = r.U32();
        var dims = new int[Math.Max(nd, 2)];
        Array.Fill(dims, 1);
        for (int k = 0; k < nd; k++)
        {
            dims[k] = checked((int)r.U64());
        }

        int count = Count(dims);
        switch (type)
        {
            case Logical:
            {
                var bits = new bool[count];
                for (int i = 0; i < count; i++)
                {
                    bits[i] = r.Byte() != 0;
                }

                return RuntimeValueConversion.ToEngine(new RuntimeLogical(bits, dims), session);
            }

            case Char:
            {
                var chars = new char[count];
                for (int i = 0; i < count; i++)
                {
                    chars[i] = (char)r.U16();
                }

                return RuntimeValueConversion.ToEngine(new RuntimeChar(new string(chars), dims), session);
            }

            case StringType:
            {
                var strings = new string?[count];
                for (int i = 0; i < count; i++)
                {
                    strings[i] = r.Byte() != 0 ? r.Text16() : null;
                }

                return RuntimeValueConversion.ToEngine(new RuntimeString(strings, dims), session);
            }

            case CellType:
            {
                var elements = new JgsValue[count];
                for (int i = 0; i < count; i++)
                {
                    elements[i] = Read(r, session);
                }

                JgsValue made = JgsValue.Cell(elements);
                made.ReshapeDims(dims);
                return made;
            }

            case StructType:
                return ReadStruct(r, dims, count, session);

            case EnumType:
                return ReadEnumeration(r, dims, count, session);

            case ObjectType or ValueObject or HandleObjectRef:
                return ReadObjects(r, dims, count, session);

            case SparseDouble or SparseLogical:
                return ReadSparse(r, dims, type == SparseLogical, session);

            case SparseComplexDouble or SparseSingle or SparseComplexSingle:
                throw new ArgumentException(type == SparseComplexDouble
                    ? "JGraph has no complex sparse arrays: pass the real and imaginary parts apart."
                    : "JGraph has no single sparse arrays: pass the matrix as sparse double.");

            case PythonDatetime when r.Python:
                return ReadDatetime(r, session);

            case PythonDuration when r.Python:
                // duration(h, m, s, ms): R2025b's from a timedelta keeps the default format (k13_client: dt.delta.in).
                return Feval(session, "duration", JgsValue.Number(0), JgsValue.Number(0), JgsValue.Number(0), JgsValue.Number(r.Number(0, 8)));

            case PythonDictionary when r.Python:
                return r.Byte() == 0 ? Feval(session, "dictionary") : Feval(session, "dictionary", Read(r, session), Read(r, session));

            case >= DoubleType and < DoubleType + 10:
                return ReadNumbers(r, type - DoubleType, complex: false, dims, count, session);

            case >= ComplexDouble and < ComplexDouble + 10:
                return ReadNumbers(r, type - ComplexDouble, complex: true, dims, count, session);

            default:
                throw new ArgumentException($"An array of type {type} cannot be passed to a component.");
        }
    }

    private static JgsValue ReadNumbers(Reader r, int kind, bool complex, int[] dims, int count, ComponentSession session)
    {
        (string className, int size) = Numbers[kind];
        if (complex && className is not ("double" or "single"))
        {
            throw new ArgumentException($"JGraph keeps no complex {className} array: pass it as a complex double or single one.");
        }

        var real = new double[count];
        double[]? imaginary = complex ? new double[count] : null;
        for (int i = 0; i < count; i++)
        {
            real[i] = r.Number(kind, size);
            if (imaginary is not null)
            {
                imaginary[i] = r.Number(kind, size);
            }
        }

        return RuntimeValueConversion.ToEngine(new RuntimeNumeric(className, real, imaginary, dims), session);
    }

    private static JgsValue ReadStruct(Reader r, int[] dims, int count, ComponentSession session)
    {
        uint nf = r.U32();
        var names = new string[nf];
        for (int f = 0; f < nf; f++)
        {
            names[f] = r.Text8();
        }

        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
        {
            throw new ArgumentException("A struct passed in has one field name twice; JGraph's structs, like MATLAB's, hold each name once.");
        }

        var elements = new Dictionary<string, JgsValue>[count];
        for (int i = 0; i < count; i++)
        {
            elements[i] = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            foreach (string name in names)
            {
                elements[i][name] = Read(r, session);
            }
        }

        if (count == 1 && dims.Length == 2 && dims[0] == 1 && dims[1] == 1)
        {
            return JgsValue.Struct(elements[0]);
        }

        JgsValue array = JgsValue.StructArray(new JgsStructArray(elements, names), dims[0], dims.Length == 2 ? dims[1] : count / Math.Max(dims[0], 1));
        if (dims.Length > 2)
        {
            array.ReshapeDims(dims);
        }

        return array;
    }

    /// <summary>
    /// An enumeration array of the members named, as R2025b's runtime refuses one it cannot make
    /// (calls: in.enumnoclass, in.enumbad): no such class, or no such member.
    /// </summary>
    private static JgsValue ReadEnumeration(Reader r, int[] dims, int count, ComponentSession session)
    {
        string className = r.Text8();
        var members = new string[count];
        for (int i = 0; i < count; i++)
        {
            members[i] = r.Text8();
        }

        if (session.Interpreter.ClassForLoad(className) is not { IsEnumeration: true } definition)
        {
            throw new ArgumentException($"Class {className} not found.");
        }

        var values = new JgsValue[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = definition.TryMember(members[i], out JgsValue found) ? found : throw new ArgumentException("Invalid enumeration");
        }

        return Joined(values, dims, session);
    }

    /// <summary>
    /// An object array of elements the engine holds: the value a result gave whole, or one made of
    /// elements of several, joined as horzcat joins them and shaped.
    /// </summary>
    private static JgsValue ReadObjects(Reader r, int[] dims, int count, ComponentSession session)
    {
        r.Text8();
        var held = new (JgsValue Value, int Index)[count];
        RuntimeOpaque? whole = null;
        bool same = true;
        for (int i = 0; i < count; i++)
        {
            ulong handle = r.U64();
            ulong index = r.U64();
            if (handle == 0 || GCHandle.FromIntPtr((nint)handle).Target is not RuntimeOpaque { Held: JgsValue value } opaque)
            {
                throw new ArgumentException("An object array's element holds no object.");
            }

            if (!ReferenceEquals(opaque.Owner, session))
            {
                throw new ArgumentException($"A {opaque.ClassName} value can only be passed back to the component that returned it.");
            }

            whole ??= opaque;
            same &= ReferenceEquals(whole, opaque) && index == (ulong)i;
            held[i] = (value, checked((int)index));
        }

        if (whole is not null && same && whole.Count == count)
        {
            return (JgsValue)whole.Held;
        }

        return Joined([.. held.Select(static h => Element(h.Value, h.Index))], dims, session);
    }

    private static JgsValue Element(JgsValue value, int index)
    {
        if (value.Type is JgsType.Array && index < value.ArrayLength)
        {
            return value.ElementAt(index);
        }

        return index == 0 ? value : throw new ArgumentException($"The object has no element {index + 1}.");
    }

    private static JgsValue Joined(JgsValue[] values, int[] dims, ComponentSession session)
    {
        if (values.Length == 1)
        {
            return values[0];
        }

        if (values.Length == 0)
        {
            throw new ArgumentException("An empty enumeration or object array cannot be passed to a component.");
        }

        JgsValue joined = JgsBuiltins.Feval(session.Interpreter, [JgsValue.Str("horzcat"), .. values], 1, 0, 0)[0];
        return dims.Length == 2 && dims[0] == 1
            ? joined
            : JgsBuiltins.Feval(session.Interpreter, [JgsValue.Str("reshape"), joined, .. dims.Select(static d => JgsValue.Number(d))], 1, 0, 0)[0];
    }

    /// <summary>
    /// A Python datetime: its wall clock and, for an aware one, its zone as R2025b names a fixed offset
    /// (<c>+02:00:00</c>), with the format R2025b gives it (k13_client: in.datetime, dt.tz.in, dt.offset.in).
    /// </summary>
    private static JgsValue ReadDatetime(Reader r, ComponentSession session)
    {
        double wall = r.Number(0, 8);
        bool zoned = r.Byte() != 0;
        double offset = r.Number(0, 8);
        DateTime at = JgsTime.ToDateTime(wall);
        var args = new List<JgsValue>
        {
            JgsValue.Str("datetime"), JgsValue.Number(at.Year), JgsValue.Number(at.Month), JgsValue.Number(at.Day),
            JgsValue.Number(at.Hour), JgsValue.Number(at.Minute), JgsValue.Number(JgsTime.SecondsOf(at)),
        };
        if (zoned)
        {
            var span = TimeSpan.FromSeconds(Math.Abs(offset));
            args.AddRange([JgsValue.Str("TimeZone"), JgsValue.Str($"{(offset < 0 ? '-' : '+')}{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}")]);
        }

        args.AddRange([JgsValue.Str("Format"), JgsValue.Str(PythonDatetimeFormat)]);
        return JgsBuiltins.Feval(session.Interpreter, [.. args], 1, 0, 0)[0];
    }

    private static JgsValue Feval(ComponentSession session, string name, params JgsValue[] args) =>
        JgsBuiltins.Feval(session.Interpreter, [JgsValue.Str(name), .. args], 1, 0, 0)[0];

    private static JgsValue ReadSparse(Reader r, int[] dims, bool logical, ComponentSession session)
    {
        int nnz = checked((int)r.U64());
        var rows = new int[nnz];
        for (int k = 0; k < nnz; k++)
        {
            rows[k] = checked((int)r.U64());
        }

        var starts = new int[dims[1] + 1];
        for (int c = 0; c < starts.Length; c++)
        {
            starts[c] = checked((int)r.U64());
        }

        var values = new double[nnz];
        for (int k = 0; k < nnz; k++)
        {
            values[k] = logical ? (r.Byte() != 0 ? 1 : 0) : r.Number(0, 8);
        }

        return RuntimeValueConversion.ToEngine(new RuntimeSparse(dims[0], dims[1], rows, starts, values), session);
    }

    // --- Writing ---------------------------------------------------------------------------------

    private static void Write(Writer w, JgsValue value, ComponentSession session)
    {
        if (value.Type == JgsType.Null)
        {
            w.Header(DoubleType, [0, 0]);
            return;
        }

        if (w.Python && WritePython(w, value, session))
        {
            return;
        }

        string cls = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        int[] dims = JgsBuiltins.SizeDims(value);
        if (value.Type == JgsType.Cell && cls == "cell")
        {
            w.Header(CellType, dims);
            foreach (JgsValue element in value.AsCell)
            {
                Write(w, element, session);
            }

            return;
        }

        if (value.Type == JgsType.Struct && cls == "struct")
        {
            JgsStructArray payload = value.AsStructArray;
            w.Header(StructType, dims);
            w.U32((uint)payload.FieldNames.Length);
            foreach (string name in payload.FieldNames)
            {
                w.Text8(name);
            }

            foreach (IReadOnlyDictionary<string, JgsValue> element in payload.Elements)
            {
                foreach (string name in payload.FieldNames)
                {
                    Write(w, element[name], session);
                }
            }

            return;
        }

        switch (RuntimeValueConversion.FromEngine(value, session))
        {
            case RuntimeNumeric numeric:
                WriteNumbers(w, numeric);
                return;

            case RuntimeLogical logical:
                w.Header(Logical, [.. logical.Dimensions]);
                foreach (bool bit in logical.Elements)
                {
                    w.Byte(bit ? (byte)1 : (byte)0);
                }

                return;

            case RuntimeChar text:
                w.Header(Char, [.. text.Dimensions]);
                foreach (char c in text.Characters)
                {
                    w.U16(c);
                }

                return;

            case RuntimeString strings:
                w.Header(StringType, [.. strings.Dimensions]);
                foreach (string? s in strings.Elements)
                {
                    w.Byte(s is null ? (byte)0 : (byte)1);
                    if (s is not null)
                    {
                        w.Text16(s);
                    }
                }

                return;

            case RuntimeSparse sparse:
                w.Header(SparseDouble, [.. sparse.Dimensions]);
                w.U64((ulong)sparse.Values.Count);
                foreach (int row in sparse.RowIndices)
                {
                    w.U64((ulong)row);
                }

                foreach (int start in sparse.ColumnStarts)
                {
                    w.U64((ulong)start);
                }

                foreach (double x in sparse.Values)
                {
                    w.Number(0, x);
                }

                return;

            case RuntimeOpaque opaque:
                WriteOpaque(w, value, opaque, session);
                return;

            default:
                w.Header(DoubleType, [0, 0]);
                return;
        }
    }

    /// <summary>
    /// A Python package's own forms: a datetime or duration 1-by-1 as its milliseconds (a datetime's
    /// wall clock in its zone, and the zone's offset there), a dictionary as its keys and values each a
    /// cell of elements, an unconfigured one as nothing. Anything else is written as for C++.
    /// </summary>
    private static bool WritePython(Writer w, JgsValue value, ComponentSession session)
    {
        if (value.TimeTag is { Kind: JgsTimeKind.Datetime or JgsTimeKind.Duration } tag && value.ArrayLength == 1)
        {
            double ms = JgsBuiltins.TimeMs(value)[0];
            if (tag.Kind == JgsTimeKind.Duration)
            {
                w.Header(PythonDuration, [1, 1]);
                w.Number(0, ms);
                return true;
            }

            TimeZoneInfo? zone = JgsTime.ZoneOf(tag);
            w.Header(PythonDatetime, [1, 1]);
            w.Number(0, zone is null || double.IsNaN(ms) ? ms : JgsTime.FromDateTime(JgsTime.WallClock(ms, tag)));
            w.Byte(zone is null ? (byte)0 : (byte)1);
            w.Number(0, zone is null || double.IsNaN(ms) ? 0 : JgsTime.OffsetAt(ms, zone).TotalSeconds);
            return true;
        }

        if (JgsBuiltins.ClassOf(value, JgsDialect.Matlab) != "dictionary")
        {
            return false;
        }

        bool configured = Feval(session, "isConfigured", value).AsBool;
        w.Header(PythonDictionary, [1, 1]);
        w.Byte(configured ? (byte)1 : (byte)0);
        if (configured)
        {
            foreach (string part in new[] { "keys", "values" })
            {
                JgsValue elements = Feval(session, part, value);
                Write(w, elements.Type == JgsType.Cell ? elements : Feval(session, "num2cell", elements), session);
            }
        }

        return true;
    }

    private static void WriteNumbers(Writer w, RuntimeNumeric numeric)
    {
        int kind = Array.IndexOf(RuntimeNumeric.Classes.ToArray(), numeric.ClassName);
        w.Header((byte)((numeric.IsComplex ? ComplexDouble : DoubleType) + kind), [.. numeric.Dimensions]);
        IReadOnlyList<double> real = numeric.Real;
        IReadOnlyList<double>? imaginary = numeric.Imaginary;
        for (int i = 0; i < numeric.Count; i++)
        {
            w.Number(kind, real[i]);
            if (imaginary is not null)
            {
                w.Number(kind, imaginary[i]);
            }
        }
    }

    /// <summary>
    /// A value C++ cannot hold as data: an enumeration as its class and members' names; any other
    /// object as a handle on the engine's value. A handle class's object, a function handle and a
    /// containers.Map are HANDLE_OBJECT_REF, every other VALUE_OBJECT (calls: make.*); a class
    /// JGraph keeps as a value of its own (a table, a datetime, a categorical) is one object
    /// whatever its size, as stage K9's .NET API gives it.
    /// </summary>
    private static void WriteOpaque(Writer w, JgsValue value, RuntimeOpaque opaque, ComponentSession session)
    {
        if (JgsBuiltins.IsEnumerationValue(value))
        {
            string[] names = JgsBuiltins.EnumerationNamesOf(value);
            w.Header(EnumType, [.. opaque.Dimensions]);
            w.Text8(opaque.ClassName);
            foreach (string name in names)
            {
                w.Text8(name);
            }

            return;
        }

        bool handle = value.Type == JgsType.Function || (value.Type == JgsType.Object && value.ObjectClass.IsHandle) || opaque.ClassName == "containers.Map";
        int[] dims = value.Type == JgsType.Object ? [.. opaque.Dimensions] : [1, 1];
        var held = new RuntimeOpaque(opaque.ClassName, dims, session, value);
        ulong reference = (ulong)GCHandle.ToIntPtr(GCHandle.Alloc(held));
        w.Header(handle ? HandleObjectRef : ValueObject, dims);
        w.Text8(opaque.ClassName);
        int count = Count(dims);
        for (int i = 0; i < count; i++)
        {
            w.U64(reference);
            w.U64((ulong)i);
        }
    }

    private static int Count(IReadOnlyList<int> dims)
    {
        int count = 1;
        foreach (int d in dims)
        {
            count = checked(count * d);
        }

        return count;
    }

    // --- The bytes -------------------------------------------------------------------------------

    private sealed class Reader(byte[] bytes)
    {
        private int _at;

        /// <summary>Whether a Python package's forms may come (stage K13).</summary>
        public bool Python { get; init; }

        public bool AtEnd => _at >= bytes.Length;

        public byte Byte() => Take(1)[0];

        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

        public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));

        public string Text8()
        {
            int n = checked((int)U32());
            return Encoding.UTF8.GetString(Take(n));
        }

        public string Text16()
        {
            int n = checked((int)U32());
            return new string(MemoryMarshal.Cast<byte, char>(Take(n * 2)));
        }

        /// <summary>One element of a numeric class, read as JGraph keeps it: a double, a 64-bit integer only up to 2^53.</summary>
        public double Number(int kind, int size)
        {
            ReadOnlySpan<byte> b = Take(size);
            return kind switch
            {
                0 => BinaryPrimitives.ReadDoubleLittleEndian(b),
                1 => BinaryPrimitives.ReadSingleLittleEndian(b),
                2 => (sbyte)b[0],
                3 => b[0],
                4 => BinaryPrimitives.ReadInt16LittleEndian(b),
                5 => BinaryPrimitives.ReadUInt16LittleEndian(b),
                6 => BinaryPrimitives.ReadInt32LittleEndian(b),
                7 => BinaryPrimitives.ReadUInt32LittleEndian(b),
                8 => Exact(BinaryPrimitives.ReadInt64LittleEndian(b)),
                _ => Exact(BinaryPrimitives.ReadUInt64LittleEndian(b)),
            };
        }

        // JGraph keeps a 64-bit integer as a double, exact to 2^53 (the interop plan's
        // JGraph:interop:int64Precision): one past that is refused, not rounded.
        private static double Exact(long x) => x is >= -(1L << 53) and <= 1L << 53
            ? x
            : throw new ArgumentException($"{x} cannot be held exactly: JGraph keeps an int64 as a double, exact to 2^53.");

        private static double Exact(ulong x) => x <= 1UL << 53
            ? x
            : throw new ArgumentException($"{x} cannot be held exactly: JGraph keeps a uint64 as a double, exact to 2^53.");

        private ReadOnlySpan<byte> Take(int n)
        {
            if (n < 0 || _at + n > bytes.Length)
            {
                throw new ArgumentException("The inputs of a call are damaged.");
            }

            ReadOnlySpan<byte> span = bytes.AsSpan(_at, n);
            _at += n;
            return span;
        }
    }

    private sealed class Writer
    {
        private readonly MemoryStream _bytes = new();
        private readonly byte[] _scratch = new byte[8];

        /// <summary>Whether a Python package's forms are written (stage K13).</summary>
        public bool Python { get; init; }

        public byte[] ToArray() => _bytes.ToArray();

        public void Header(byte type, int[] dims)
        {
            Byte(type);
            U32((uint)dims.Length);
            foreach (int d in dims)
            {
                U64((ulong)d);
            }
        }

        public void Byte(byte b) => _bytes.WriteByte(b);

        public void U16(ushort v)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(_scratch, v);
            _bytes.Write(_scratch, 0, 2);
        }

        public void U32(uint v)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_scratch, v);
            _bytes.Write(_scratch, 0, 4);
        }

        public void U64(ulong v)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(_scratch, v);
            _bytes.Write(_scratch, 0, 8);
        }

        public void Text8(string s)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(s);
            U32((uint)utf8.Length);
            _bytes.Write(utf8);
        }

        public void Text16(string s)
        {
            U32((uint)s.Length);
            _bytes.Write(MemoryMarshal.AsBytes(s.AsSpan()));
        }

        /// <summary>One element of a numeric class from the double JGraph keeps it as.</summary>
        public void Number(int kind, double x)
        {
            switch (kind)
            {
                case 0: BinaryPrimitives.WriteDoubleLittleEndian(_scratch, x); _bytes.Write(_scratch, 0, 8); break;
                case 1: BinaryPrimitives.WriteSingleLittleEndian(_scratch, (float)x); _bytes.Write(_scratch, 0, 4); break;
                case 2: _bytes.WriteByte((byte)(sbyte)x); break;
                case 3: _bytes.WriteByte((byte)x); break;
                case 4: BinaryPrimitives.WriteInt16LittleEndian(_scratch, (short)x); _bytes.Write(_scratch, 0, 2); break;
                case 5: BinaryPrimitives.WriteUInt16LittleEndian(_scratch, (ushort)x); _bytes.Write(_scratch, 0, 2); break;
                case 6: BinaryPrimitives.WriteInt32LittleEndian(_scratch, (int)x); _bytes.Write(_scratch, 0, 4); break;
                case 7: BinaryPrimitives.WriteUInt32LittleEndian(_scratch, (uint)x); _bytes.Write(_scratch, 0, 4); break;
                case 8: BinaryPrimitives.WriteInt64LittleEndian(_scratch, (long)x); _bytes.Write(_scratch, 0, 8); break;
                default: BinaryPrimitives.WriteUInt64LittleEndian(_scratch, (ulong)x); _bytes.Write(_scratch, 0, 8); break;
            }
        }
    }
}
