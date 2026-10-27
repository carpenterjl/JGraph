using System.Globalization;
using System.Numerics;
using System.Text;
using JGraph.Numerics.Sparse;
using static JGraph.Scripting.Jgs.JgsProtobufWire;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// MATLAB Production Server's protobuf wire format (compiler and SDK plan, stage K16; k16_server):
/// the messages <c>MATLAB_Params</c>, <c>MATLAB_Result</c>, <c>MATLAB_Error</c> and <c>MATLAB_Array</c> of
/// MathWorks' client libraries' schema (<c>MATLABArray.proto</c> and its siblings, recovered from the
/// Python client's generated code), read as R2025b's muserve reads them and written byte for byte
/// as it writes them. A request is read for its arguments and output count; a reply carries the
/// outputs or the error. Where muserve's worker reads past what a message holds, ends, or hangs
/// (edge.*, in.count.short, in.payload.absent), JGraph refuses the request instead (ADR 0272).
/// </summary>
internal static class JgsMpsProtobuf
{
    /// <summary><c>MATLAB_Array.MWType</c>.</summary>
    private enum MwType
    {
        Uint8 = 0,
        Int8 = 1,
        Uint16 = 2,
        Int16 = 3,
        Uint32 = 4,
        Int32 = 5,
        Uint64 = 6,
        Int64 = 7,
        Single = 8,
        Double = 9,
        Logical = 10,
        Char = 11,
        Struct = 12,
        Cell = 13,
        Sparse = 14,
        Object = 15,
        HandleObjectRef = 16,
        ArrayRef = 17,
        Enumeration = 18,
        String = 19,
    }

    // MATLAB_Array's fields.
    private const int FType = 1;
    private const int FDimension = 2;
    private const int FArrayId = 3;
    private const int FDataFrame = 4;
    private const int FSharedData = 5;
    private const int FMemoryLayout = 6;
    private const int FIsComplex = 7;
    private const int FUint8 = 16;
    private const int FInt8 = 17;
    private const int FUint16 = 18;
    private const int FInt16 = 19;
    private const int FUint32 = 20;
    private const int FInt32 = 21;
    private const int FUint64 = 22;
    private const int FInt64 = 23;
    private const int FSingle = 24;
    private const int FDouble = 25;
    private const int FLogical = 26;
    private const int FChar = 27;
    private const int FCell = 28;
    private const int FStruct = 29;
    private const int FSparse = 30;
    private const int FClassNameIndex = 31;
    private const int FObjects = 32;
    private const int FObjectRefs = 33;
    private const int FArrayRef = 34;
    private const int FTargetType = 35;
    private const int FEnumerationClassName = 36;
    private const int FEnumerations = 37;
    private const int FStringElements = 38;
    private const int FPackageInfoIndex = 39;
    private const int FSparseWithDataFrame = 40;

    /// <summary>The one code unit a missing string is written as (out.stringmissing).</summary>
    private const int MissingUnit = 0x1FFFF;

    /// <summary>The milliseconds between JGraph's datetime epoch (1899-12-30) and the <c>TimeStamp</c> epoch (1970-01-01).</summary>
    private static readonly double UnixEpochMs = (new DateTime(1970, 1, 1) - JgsTime.Epoch).TotalMilliseconds;

    /// <summary>A value the writer cannot put on the wire, with the identifier and message the reply's error carries.</summary>
    internal sealed class Refused(string identifier, string message) : Exception(message)
    {
        public string Identifier { get; } = identifier;
    }

    /// <summary>What a request asked for: the arguments and the output count, or a fault muserve answers as a MATLAB error of its own.</summary>
    internal sealed record ServerRequest(JgsValue[] Arguments, int Nargout, string? Fault = null);

    private static JgsMpsJson.ServerRefusal InvalidRhs() => new(400, "InvalidInputArgs", "Invalid rhs.");

    // --- reading a request ----------------------------------------------------------------------------

    /// <summary>
    /// <c>MATLAB_Params</c>: <c>nargout</c> (0 when absent; a negative one is muserve's "vector too long"
    /// error) and <c>rhs</c>; a body that is not a message, or an array the reader cannot make, is
    /// <c>400 InvalidInputArgs Invalid rhs.</c> as muserve answers garbage (body.garbage, body.truncated).
    /// </summary>
    public static ServerRequest DecodeRequest(ReadOnlySpan<byte> body, Func<string, JgsClass?> findClass)
    {
        try
        {
            long nargout = 0;
            var arguments = new List<JgsValue>();
            string? fault = null;
            var reader = new Reader(body);
            while (reader.Next(out int field, out int wireType))
            {
                switch (field)
                {
                    case 1 when wireType == Varint:
                        nargout = unchecked((int)reader.ReadVarint());
                        break;
                    case 2 when wireType == Delimited:
                        try
                        {
                            arguments.Add(new ArrayReader(findClass).Read(reader.ReadBytes()));
                        }
                        catch (Fault f) when (fault is null)
                        {
                            fault = f.Message;
                        }

                        break;
                    default:
                        reader.Skip(wireType);
                        break;
                }
            }

            if (nargout < 0)
            {
                return new ServerRequest([], 0, "vector too long");
            }

            return new ServerRequest([.. arguments], (int)Math.Min(nargout, int.MaxValue), fault);
        }
        catch (Exception ex) when (ex is Malformed or Unreadable)
        {
            throw InvalidRhs();
        }
    }

    /// <summary>An array the reader refuses: the request is a 400.</summary>
    private sealed class Unreadable(string what) : Exception(what);

    /// <summary>An array muserve's worker reads but answers with a MATLAB error of its own (enum.nosuch, enum.noclass).</summary>
    private sealed class Fault(string message) : Exception(message);

    private sealed class ArrayReader(Func<string, JgsClass?> findClass)
    {
        private bool _rowMajor;

        private static readonly JgsNumericClass[] NumericOfType =
        [
            JgsNumericClass.UInt8, JgsNumericClass.Int8, JgsNumericClass.UInt16, JgsNumericClass.Int16, JgsNumericClass.UInt32, JgsNumericClass.Int32,
            JgsNumericClass.UInt64, JgsNumericClass.Int64, JgsNumericClass.Single, JgsNumericClass.Double,
        ];

        /// <summary>One <c>MATLAB_Array</c> as a value.</summary>
        public JgsValue Read(ReadOnlySpan<byte> bytes)
        {
            MwType? type = null;
            var dims = new List<ulong>();
            bool complex = false;
            bool complexSet = false;
            ulong layout = 0;
            List<string>? sharedStrings = null;
            int payloadField = 0;
            ReadOnlySpan<byte> payload = default;
            ReadOnlySpan<byte> enumerations = default;
            bool hasEnumerations = false;
            ulong? enumerationClassName = null;
            var reader = new Reader(bytes);
            while (reader.Next(out int field, out int wireType))
            {
                switch (field)
                {
                    case FType when wireType == Varint:
                        type = (MwType)reader.ReadVarint();
                        break;
                    case FDimension:
                        reader.Varints(wireType, dims);
                        break;
                    case FArrayId or FDataFrame or FArrayRef or FClassNameIndex or FObjects or FObjectRefs or FPackageInfoIndex or FSparseWithDataFrame:
                        // Shared arrays, object references and data frames: not a value a request carries (in.ids.set, in.type.arrayref).
                        throw new Unreadable("a shared or referenced array");
                    case FSharedData when wireType == Delimited:
                        sharedStrings = SharedStrings(reader.ReadBytes());
                        break;
                    case FMemoryLayout when wireType == Varint:
                        layout = reader.ReadVarint();
                        break;
                    case FIsComplex when wireType == Varint:
                        complex = reader.ReadBool();
                        complexSet = true;
                        break;
                    case >= FUint8 and <= FSparse when wireType == Delimited:
                        if (payloadField != 0)
                        {
                            throw new Unreadable("two payloads");
                        }

                        payloadField = field;
                        payload = reader.ReadBytes();
                        break;
                    case FEnumerationClassName when wireType == Varint:
                        enumerationClassName = reader.ReadVarint();
                        break;
                    case FEnumerations when wireType == Delimited:
                        enumerations = reader.ReadBytes();
                        hasEnumerations = true;
                        break;
                    case FStringElements when wireType == Delimited:
                        if (payloadField != 0)
                        {
                            throw new Unreadable("two payloads");
                        }

                        payloadField = field;
                        payload = reader.ReadBytes();
                        break;
                    case FTargetType:
                        reader.Skip(wireType);
                        break;
                    default:
                        reader.Skip(wireType);
                        break;
                }
            }

            if (layout == 2)
            {
                throw new Unreadable("an unknown memory layout"); // edge.layout.unknown ends muserve's worker
            }

            _rowMajor = layout == 1; // ROW_MAJOR: the elements arrive with the last dimension varying fastest (in.layout.row.major)

            // Without a type the payload names it (in.type.absent); without either the array is uint8, the schema's default.
            type ??= payloadField switch
            {
                FUint8 => MwType.Uint8,
                FInt8 => MwType.Int8,
                FUint16 => MwType.Uint16,
                FInt16 => MwType.Int16,
                FUint32 => MwType.Uint32,
                FInt32 => MwType.Int32,
                FUint64 => MwType.Uint64,
                FInt64 => MwType.Int64,
                FSingle => MwType.Single,
                FDouble => MwType.Double,
                FLogical => MwType.Logical,
                FChar => MwType.Char,
                FCell => MwType.Cell,
                FStruct => MwType.Struct,
                FSparse => MwType.Sparse,
                FStringElements => MwType.String,
                _ => MwType.Uint8,
            };

            int[] shape = Shape(dims);
            long count = Count(shape);
            switch (type)
            {
                case >= MwType.Uint8 and <= MwType.Double:
                    return Numbers(type.Value, shape, count, payloadField, payload, complex, complexSet);
                case MwType.Logical:
                    return Logicals(shape, count, payloadField, payload);
                case MwType.Char:
                    return Chars(shape, count, payloadField, payload);
                case MwType.Cell:
                    return Cells(shape, count, payloadField, payload);
                case MwType.Struct:
                    return Structs(shape, count, payloadField, payload);
                case MwType.Sparse:
                    return Sparse(shape, payloadField, payload);
                case MwType.String:
                    return Strings(shape, count, payloadField, payload);
                case MwType.Enumeration:
                    return Enumeration(shape, count, sharedStrings, enumerationClassName, hasEnumerations, enumerations);
                default:
                    // OBJECT and HANDLE_OBJECT_REF end muserve's worker, ARRAYREF is its 400 (in.type.*).
                    throw new Unreadable("type " + type);
            }
        }

        /// <summary>
        /// The dimensions as muserve takes them (in.dims.*): none is 0-by-0, one is a column, a trailing
        /// 1 past the second is no dimension; each must fit an array.
        /// </summary>
        private static int[] Shape(List<ulong> dims)
        {
            if (dims.Count == 0)
            {
                return [0, 0];
            }

            if (dims.Count == 1)
            {
                dims.Add(1);
            }

            while (dims.Count > 2 && dims[^1] == 1)
            {
                dims.RemoveAt(dims.Count - 1);
            }

            var shape = new int[dims.Count];
            for (int i = 0; i < shape.Length; i++)
            {
                if (dims[i] > int.MaxValue)
                {
                    throw new Unreadable("a dimension past what an array holds");
                }

                shape[i] = (int)dims[i];
            }

            return shape;
        }

        private static long Count(int[] shape)
        {
            long count = 1;
            foreach (int d in shape)
            {
                count *= d;
                if (count > int.MaxValue)
                {
                    throw new Unreadable("more elements than an array holds");
                }
            }

            return count;
        }

        /// <summary>The payload message of a numeric type: <c>elements</c> (1) and <c>imag_elements</c> (2), as that type's scalars.</summary>
        private static (double[] Real, double[]? Imag) NumericPayload(MwType type, ReadOnlySpan<byte> payload)
        {
            var real = new List<double>();
            List<double>? imag = null;
            var reader = new Reader(payload);
            while (reader.Next(out int field, out int wireType))
            {
                if (field is not (1 or 2))
                {
                    reader.Skip(wireType);
                    continue;
                }

                List<double> into = field == 1 ? real : imag ??= [];
                switch (type)
                {
                    case MwType.Uint8 or MwType.Int8:
                        if (wireType != Delimited)
                        {
                            throw new Malformed("bytes expected");
                        }

                        foreach (byte b in reader.ReadBytes())
                        {
                            into.Add(type == MwType.Int8 ? (sbyte)b : b);
                        }

                        break;
                    case MwType.Uint16:
                    {
                        var values = new List<ulong>();
                        reader.Varints(wireType, values);
                        into.AddRange(values.Select(static v => (double)(ushort)v));
                        break;
                    }

                    case MwType.Int16:
                    {
                        var values = new List<ulong>();
                        reader.Varints(wireType, values);
                        into.AddRange(values.Select(static v => (double)(short)unchecked((int)v)));
                        break;
                    }

                    case MwType.Uint32:
                    {
                        var values = new List<uint>();
                        reader.Fixed32s(wireType, values);
                        into.AddRange(values.Select(static v => (double)v));
                        break;
                    }

                    case MwType.Int32:
                    {
                        var values = new List<uint>();
                        reader.Fixed32s(wireType, values);
                        into.AddRange(values.Select(static v => (double)unchecked((int)v)));
                        break;
                    }

                    case MwType.Uint64:
                    {
                        var values = new List<ulong>();
                        reader.Varints(wireType, values);
                        into.AddRange(values.Select(static v => (double)v));
                        break;
                    }

                    case MwType.Int64:
                    {
                        var values = new List<ulong>();
                        reader.Varints(wireType, values);
                        into.AddRange(values.Select(static v => (double)unchecked((long)v)));
                        break;
                    }

                    case MwType.Single:
                    {
                        var values = new List<uint>();
                        reader.Fixed32s(wireType, values);
                        into.AddRange(values.Select(static v => (double)BitConverter.UInt32BitsToSingle(v)));
                        break;
                    }

                    default:
                    {
                        var values = new List<ulong>();
                        reader.Fixed64s(wireType, values);
                        into.AddRange(values.Select(static v => BitConverter.UInt64BitsToDouble(v)));
                        break;
                    }
                }
            }

            return ([.. real], imag is null ? null : [.. imag]);
        }

        private static int PayloadFieldOf(MwType type) => type switch
        {
            MwType.Uint8 => FUint8,
            MwType.Int8 => FInt8,
            MwType.Uint16 => FUint16,
            MwType.Int16 => FInt16,
            MwType.Uint32 => FUint32,
            MwType.Int32 => FInt32,
            MwType.Uint64 => FUint64,
            MwType.Int64 => FInt64,
            MwType.Single => FSingle,
            MwType.Double => FDouble,
            MwType.Logical => FLogical,
            MwType.Char => FChar,
            MwType.Cell => FCell,
            MwType.Struct => FStruct,
            MwType.Sparse => FSparse,
            _ => FStringElements,
        };

        /// <summary>The payload the type wants, or none for an empty array (the Python client sends an empty char with no payload).</summary>
        private static void CheckPayload(MwType type, int payloadField, long count)
        {
            if (payloadField == 0)
            {
                if (count > 0)
                {
                    throw new Unreadable("no payload"); // muserve reads what is not there (in.payload.absent)
                }
            }
            else if (payloadField != PayloadFieldOf(type))
            {
                throw new Unreadable("a payload of another type"); // muserve reads the bytes as the type's (in.type.mismatch)
            }
        }

        /// <summary>Row-major elements (the last dimension fastest) put into column-major order, as muserve reads them (in.layout.row.major).</summary>
        private static double[] ToColumnMajor(double[] values, int[] shape, long count)
        {
            var result = new double[count];
            int n = shape.Length;
            var index = new int[n];
            for (long k = 0; k < count; k++)
            {
                // k counts row-major: the last dimension varies fastest.
                long rest = k;
                for (int d = n - 1; d >= 0; d--)
                {
                    index[d] = (int)(rest % shape[d]);
                    rest /= shape[d];
                }

                long at = 0;
                long stride = 1;
                for (int d = 0; d < n; d++)
                {
                    at += index[d] * stride;
                    stride *= shape[d];
                }

                result[at] = values[k];
            }

            return result;
        }

        private JgsValue Numbers(MwType type, int[] shape, long count, int payloadField, ReadOnlySpan<byte> payload, bool complexFlag, bool complexSet)
        {
            CheckPayload(type, payloadField, count);
            (double[] real, double[]? imag) = payloadField == 0 ? ([], null) : NumericPayload(type, payload);
            if (_rowMajor && real.Length >= count && count > 1)
            {
                real = ToColumnMajor(real, shape, count);
                if (imag is not null && imag.Length >= count)
                {
                    imag = ToColumnMajor(imag, shape, count);
                }
            }
            JgsNumericClass numericClass = NumericOfType[(int)type];
            if (real.Length < count)
            {
                throw new Unreadable("fewer elements than the dimensions"); // in.count.short: muserve reads past the end
            }

            // Imaginary parts make the array complex, the flag alone makes nothing (in.complex.*; the flag
            // without parts ends muserve's worker, parts fewer than the real ones are read past the end).
            if (imag is null || imag.Length == 0)
            {
                if (complexFlag && count > 0)
                {
                    throw new Unreadable("complex without imaginary parts");
                }

                JgsValue result = count == 1 && shape.All(static d => d == 1) ? JgsValue.Number(real[0]) : JgsMatrix.FromColumnMajorDims(real[..(int)count], shape);
                if (numericClass != JgsNumericClass.Double)
                {
                    result.SetNumericClass(numericClass);
                }

                return result;
            }

            _ = complexSet;
            if (imag.Length < count)
            {
                throw new Unreadable("fewer imaginary parts than elements");
            }

            if (count == 1 && shape.All(static d => d == 1))
            {
                JgsValue scalar = JgsValue.ComplexNum(new Complex(real[0], imag[0]));
                scalar.SetNumericClass(numericClass);
                return scalar;
            }

            var pairs = new JgsValue[count];
            for (int i = 0; i < count; i++)
            {
                pairs[i] = JgsValue.ComplexNum(new Complex(real[i], imag[i]));
            }

            JgsValue array = count == 0 ? JgsMatrix.FromColumnMajorDims([], shape) : JgsMatrix.FromElementsDims(pairs, shape);
            array.SetNumericClass(numericClass);
            return array;
        }

        private static JgsValue Logicals(int[] shape, long count, int payloadField, ReadOnlySpan<byte> payload)
        {
            CheckPayload(MwType.Logical, payloadField, count);
            var values = new List<ulong>();
            if (payloadField != 0)
            {
                var reader = new Reader(payload);
                while (reader.Next(out int field, out int wireType))
                {
                    if (field == 1)
                    {
                        reader.Varints(wireType, values);
                    }
                    else
                    {
                        reader.Skip(wireType);
                    }
                }
            }

            if (values.Count < count)
            {
                throw new Unreadable("fewer elements than the dimensions");
            }

            if (count == 1 && shape.All(static d => d == 1))
            {
                return JgsValue.Bool(values[0] != 0);
            }

            if (count == 0)
            {
                JgsValue empty = JgsBuiltins.EmptyLogical(shape[0], shape.Length == 2 ? shape[1] : 0);
                if (shape.Length > 2)
                {
                    empty.ReshapeDims(shape);
                }

                return empty;
            }

            return JgsMatrix.FromElementsDims([.. values.Take((int)count).Select(static v => JgsValue.Bool(v != 0))], shape);
        }

        /// <summary>The code units of <c>mwchar.elements</c> (each kept to 16 bits as muserve keeps it: in.char.supplementary), column-major.</summary>
        private static JgsValue Chars(int[] shape, long count, int payloadField, ReadOnlySpan<byte> payload)
        {
            CheckPayload(MwType.Char, payloadField, count);
            var units = new List<ulong>();
            if (payloadField != 0)
            {
                var reader = new Reader(payload);
                while (reader.Next(out int field, out int wireType))
                {
                    if (field == 1)
                    {
                        reader.Varints(wireType, units);
                    }
                    else
                    {
                        reader.Skip(wireType);
                    }
                }
            }

            if (units.Count < count)
            {
                throw new Unreadable("fewer characters than the dimensions");
            }

            if (shape.Length > 2)
            {
                throw new Unreadable("a char array of more than two dimensions"); // JGraph holds two (ADR 0270)
            }

            int rows = shape[0];
            int cols = shape[1];
            if (count == 0)
            {
                JgsValue made = JgsValue.CharMatrix(rows == 0 ? [] : [.. Enumerable.Repeat(string.Empty, rows)]);
                made.Reshape(rows, cols);
                return made;
            }

            var lines = new StringBuilder[rows];
            for (int r = 0; r < rows; r++)
            {
                lines[r] = new StringBuilder(cols);
            }

            for (int i = 0; i < count; i++)
            {
                lines[i % rows].Append((char)(ushort)units[i]);
            }

            return rows == 1 ? JgsValue.Str(lines[0].ToString()) : JgsValue.CharMatrix([.. lines.Select(static l => l.ToString())]);
        }

        private List<JgsValue> Elements(ReadOnlySpan<byte> payload, int field)
        {
            var items = new List<JgsValue>();
            var reader = new Reader(payload);
            while (reader.Next(out int f, out int wireType))
            {
                if (f == field && wireType == Delimited)
                {
                    items.Add(Read(reader.ReadBytes()));
                }
                else
                {
                    reader.Skip(wireType);
                }
            }

            return items;
        }

        private JgsValue Cells(int[] shape, long count, int payloadField, ReadOnlySpan<byte> payload)
        {
            CheckPayload(MwType.Cell, payloadField, count);
            List<JgsValue> items = payloadField == 0 ? [] : Elements(payload, 1);
            if (items.Count < count)
            {
                throw new Unreadable("fewer cells than the dimensions");
            }

            JgsValue cell = JgsValue.Cell([.. items.Take((int)count)]);
            cell.Reshape(shape[0], shape.Length == 2 ? shape[1] : (int)count / Math.Max(shape[0], 1));
            if (shape.Length > 2)
            {
                cell.ReshapeDims(shape);
            }

            return cell;
        }

        /// <summary><c>field_names</c> (1) then <c>elements</c> (2), element by element, each element's fields in order (in.struct.array.twofields).</summary>
        private JgsValue Structs(int[] shape, long count, int payloadField, ReadOnlySpan<byte> payload)
        {
            var names = new List<string>();
            var values = new List<JgsValue>();
            if (payloadField != 0)
            {
                if (payloadField != FStruct)
                {
                    throw new Unreadable("a payload of another type");
                }

                var reader = new Reader(payload);
                while (reader.Next(out int field, out int wireType))
                {
                    if (field == 1 && wireType == Delimited)
                    {
                        names.Add(reader.ReadString());
                    }
                    else if (field == 2 && wireType == Delimited)
                    {
                        values.Add(Read(reader.ReadBytes()));
                    }
                    else
                    {
                        reader.Skip(wireType);
                    }
                }
            }

            foreach (string name in names)
            {
                // A name MATLAB cannot hold (in.struct.badname: muserve's worker makes the struct and fails on its use) or one twice (in.struct.dupname: 400).
                if (name.Length == 0 || name.Length > 63 || !char.IsAsciiLetter(name[0]) || name.Any(static c => !(char.IsAsciiLetterOrDigit(c) || c == '_'))
                    || names.Count(n => n == name) > 1)
                {
                    throw new Unreadable("a field name");
                }
            }

            if (values.Count < names.Count * count)
            {
                throw new Unreadable("fewer field values than the elements need"); // edge.struct.short ends muserve's worker
            }

            if (count == 1 && shape.All(static d => d == 1))
            {
                var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
                for (int f = 0; f < names.Count; f++)
                {
                    fields[names[f]] = values[f];
                }

                return JgsValue.Struct(fields);
            }

            var elements = new Dictionary<string, JgsValue>[count];
            for (int i = 0; i < count; i++)
            {
                var element = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
                for (int f = 0; f < names.Count; f++)
                {
                    element[names[f]] = values[(i * names.Count) + f];
                }

                elements[i] = element;
            }

            JgsValue array = JgsValue.StructArray(new JgsStructArray(elements, [.. names]), shape[0], shape.Length == 2 ? shape[1] : (int)count / Math.Max(shape[0], 1));
            if (shape.Length > 2)
            {
                array.ReshapeDims(shape);
            }

            return array;
        }

        /// <summary><c>strings</c> (1), every string's code units in a row, cut by <c>stringSizes</c> (2); a missing string is TBD.</summary>
        private static JgsValue Strings(int[] shape, long count, int payloadField, ReadOnlySpan<byte> payload)
        {
            CheckPayload(MwType.String, payloadField, count);
            var units = new List<ulong>();
            var sizes = new List<ulong>();
            if (payloadField != 0)
            {
                var reader = new Reader(payload);
                while (reader.Next(out int field, out int wireType))
                {
                    if (field == 1)
                    {
                        reader.Varints(wireType, units);
                    }
                    else if (field == 2)
                    {
                        reader.Varints(wireType, sizes);
                    }
                    else
                    {
                        reader.Skip(wireType);
                    }
                }
            }

            if (sizes.Count < count)
            {
                throw new Unreadable("fewer string sizes than the dimensions"); // edge.string.sizes.short: muserve's "an unknown error occurred"
            }

            var values = new JgsValue[count];
            long at = 0;
            for (int i = 0; i < count; i++)
            {
                if (sizes[i] > (ulong)(units.Count - at))
                {
                    throw new Unreadable("fewer characters than the string sizes");
                }

                // The one unit 0x1FFFF is a missing string (out.stringmissing); every other unit is kept to 16 bits.
                if (sizes[i] == 1 && units[(int)at] == MissingUnit)
                {
                    values[i] = JgsValue.Str(JgsBuiltins.MissingSentinel);
                    at++;
                    continue;
                }

                var text = new StringBuilder((int)sizes[i]);
                for (ulong k = 0; k < sizes[i]; k++)
                {
                    text.Append((char)(ushort)units[(int)at++]);
                }

                values[i] = JgsValue.Str(text.ToString());
            }

            JgsValue array = JgsValue.StringArray(values, shape[0], shape.Length == 2 ? shape[1] : (int)count / Math.Max(shape[0], 1));
            if (shape.Length > 2)
            {
                array.ReshapeDims(shape);
            }

            return array;
        }

        /// <summary><c>type</c> (1), <c>ir</c> (2), <c>jc</c> (3) and the values (16, 17 or 18): a compressed column form checked before it is read (edge.sparse.badjc, in.sparse.badir).</summary>
        private static JgsValue Sparse(int[] shape, int payloadField, ReadOnlySpan<byte> payload)
        {
            if (payloadField != FSparse)
            {
                throw new Unreadable(payloadField == 0 ? "no payload" : "a payload of another type");
            }

            if (shape.Length != 2)
            {
                throw new Unreadable("a sparse array of more than two dimensions");
            }

            ulong kind = 0;
            var ir = new List<ulong>();
            var jc = new List<ulong>();
            var values = new List<double>();
            var reader = new Reader(payload);
            while (reader.Next(out int field, out int wireType))
            {
                switch (field)
                {
                    case 1 when wireType == Varint:
                        kind = reader.ReadVarint();
                        break;
                    case 2:
                        reader.Varints(wireType, ir);
                        break;
                    case 3:
                        reader.Varints(wireType, jc);
                        break;
                    case 16 when wireType == Delimited:
                        values.AddRange(NumericPayload(MwType.Double, reader.ReadBytes()).Real);
                        break;
                    case 17 when wireType == Delimited:
                    {
                        var flags = new List<ulong>();
                        var inner = new Reader(reader.ReadBytes());
                        while (inner.Next(out int f, out int w))
                        {
                            if (f == 1)
                            {
                                inner.Varints(w, flags);
                            }
                            else
                            {
                                inner.Skip(w);
                            }
                        }

                        values.AddRange(flags.Select(static v => v != 0 ? 1.0 : 0.0));
                        break;
                    }

                    case 18 when wireType == Delimited:
                        values.AddRange(NumericPayload(MwType.Single, reader.ReadBytes()).Real);
                        break;
                    default:
                        reader.Skip(wireType);
                        break;
                }
            }

            _ = kind; // a logical or single sparse array is double in JGraph (ADR 0266)
            int rows = shape[0];
            int cols = shape[1];
            if (jc.Count != cols + 1 || jc[0] != 0)
            {
                throw new Unreadable("jc");
            }

            var triplets = new List<(int Row, int Col, double Value)>();
            for (int c = 0; c < cols; c++)
            {
                if (jc[c + 1] < jc[c] || jc[c + 1] > (ulong)ir.Count || jc[c + 1] > (ulong)values.Count)
                {
                    throw new Unreadable("jc");
                }

                for (ulong k = jc[c]; k < jc[c + 1]; k++)
                {
                    if (ir[(int)k] >= (ulong)rows)
                    {
                        throw new Unreadable("ir");
                    }

                    triplets.Add(((int)ir[(int)k], c, values[(int)k]));
                }
            }

            return JgsValue.Sparse(CscMatrix.FromTriplets(rows, cols, triplets));
        }

        /// <summary>The <c>stringTable.stringEntry</c> list of <c>sharedData</c> (in.enum.*).</summary>
        private static List<string> SharedStrings(ReadOnlySpan<byte> sharedData)
        {
            var strings = new List<string>();
            var reader = new Reader(sharedData);
            while (reader.Next(out int field, out int wireType))
            {
                if (field == 1 && wireType == Delimited)
                {
                    var table = new Reader(reader.ReadBytes());
                    while (table.Next(out int f, out int w))
                    {
                        if (f == 1 && w == Delimited)
                        {
                            strings.Add(table.ReadString());
                        }
                        else
                        {
                            table.Skip(w);
                        }
                    }
                }
                else
                {
                    reader.Skip(wireType);
                }
            }

            return strings;
        }

        /// <summary>An enumeration array: the class by <c>enumerationClassName</c> into the string table, each element an <c>Object</c> whose <c>enumerationNameIndex</c> (5) names its member.</summary>
        private JgsValue Enumeration(int[] shape, long count, List<string>? strings, ulong? classIndex, bool hasEnumerations, ReadOnlySpan<byte> enumerations)
        {
            if (strings is null || classIndex is null || classIndex >= (ulong)strings.Count)
            {
                throw new Unreadable("an enumeration without its class");
            }

            string className = strings[(int)classIndex.Value];
            var names = new List<string>();
            if (hasEnumerations)
            {
                var reader = new Reader(enumerations);
                while (reader.Next(out int field, out int wireType))
                {
                    if (field == 1 && wireType == Delimited)
                    {
                        ulong? nameIndex = null;
                        var element = new Reader(reader.ReadBytes());
                        while (element.Next(out int f, out int w))
                        {
                            if (f == 5 && w == Varint)
                            {
                                nameIndex = element.ReadVarint();
                            }
                            else
                            {
                                element.Skip(w);
                            }
                        }

                        if (nameIndex is null || nameIndex >= (ulong)strings.Count)
                        {
                            throw new Unreadable("an enumeration member without its name");
                        }

                        names.Add(strings[(int)nameIndex.Value]);
                    }
                    else
                    {
                        reader.Skip(wireType);
                    }
                }
            }

            if (names.Count < count)
            {
                throw new Unreadable("fewer members than the dimensions");
            }

            names = names.Take((int)count).ToList();
            int cols = shape.Length == 2 ? shape[1] : (int)count / Math.Max(shape[0], 1);
            if (className == OnOffSwitchStateValue.Name)
            {
                var states = new bool[names.Count];
                for (int i = 0; i < names.Count; i++)
                {
                    states[i] = OnOffSwitchStateValue.Named(names[i]) ?? throw new Fault("Invalid enumeration");
                }

                return names.Count == 1 ? OnOffSwitchStateValue.Of(states[0]) : OnOffSwitchStateValue.Of(states, shape[0], cols);
            }

            if (findClass(className) is not { IsEnumeration: true } definition)
            {
                throw new Fault($"Class {className} not found."); // in.enum.noclass, muserve's own words
            }

            var members = new JgsObject[names.Count];
            for (int i = 0; i < names.Count; i++)
            {
                members[i] = definition.TryMember(names[i], out JgsValue member) ? member.AsObject : throw new Fault("Invalid enumeration");
            }

            return members.Length == 1 ? JgsValue.Object(members[0]) : JgsValue.ObjectArray(new JgsObjectArray(definition, members), shape[0], cols);
        }
    }

    // --- writing a reply -------------------------------------------------------------------------------

    /// <summary><c>MATLAB_Result</c> with <c>lhs</c>, one <c>MATLAB_Array</c> per output; no outputs is no bytes at all (out.nargout0).</summary>
    public static byte[] EncodeResult(IReadOnlyList<JgsValue> outputs)
    {
        var result = new Writer();
        foreach (JgsValue output in outputs)
        {
            result.MessageField(1, new ArrayWriter().WriteTop(output));
        }

        return result.ToArray();
    }

    /// <summary><c>MATLAB_Result</c> with <c>error</c>: <c>message</c> (1), <c>identifier</c> (2), each frame of <c>stack</c> (3) as <c>file</c> (1), <c>name</c> (2), <c>line</c> (3).</summary>
    public static byte[] EncodeError(string identifier, string message, IReadOnlyList<(string Name, string File, int Line)> frames, bool withStack = true)
    {
        var error = new Writer();
        error.StringField(1, message);
        error.StringField(2, identifier);
        if (withStack)
        {
            foreach ((string name, string file, int line) in frames)
            {
                var frame = new Writer();
                frame.StringField(1, file);
                frame.StringField(2, name);
                frame.VarintField(3, (long)line);
                error.MessageField(3, frame);
            }
        }

        var result = new Writer();
        result.MessageField(2, error);
        return result.ToArray();
    }

    /// <summary>The sixteen bytes muserve names its session by in a handle reference's string table; JGraph's are this process's.</summary>
    private static readonly byte[] SessionId = Guid.NewGuid().ToByteArray();
    private static long _nextObjectId;

    /// <summary>
    /// The pieces of one array before it is assembled: the fields after <c>isComplex</c> are written
    /// first, since the shared string table they intern names into is written before them.
    /// </summary>
    private sealed record Parts(MwType Type, int[] Dims, bool Complex, Writer Tail);

    /// <summary>
    /// The string table of one output (<c>sharedData.stringTable</c>): an empty first entry, then
    /// each name as it is first met while the output is written, nested arrays included, and
    /// <c>startIndex</c> one past the count (out.enum, out.datetime).
    /// </summary>
    private sealed class SharedTable
    {
        private readonly List<byte[]> _entries = [[]];
        private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal) { [string.Empty] = 0 };

        public bool Used => _entries.Count > 1;

        public int Intern(string name)
        {
            if (!_index.TryGetValue(name, out int at))
            {
                at = _entries.Count;
                _entries.Add(Encoding.UTF8.GetBytes(name));
                _index[name] = at;
            }

            return at;
        }

        public int Add(byte[] bytes)
        {
            _entries.Add(bytes);
            return _entries.Count - 1;
        }

        public Writer ToWriter()
        {
            var table = new Writer();
            foreach (byte[] entry in _entries)
            {
                table.BytesField(1, entry);
            }

            table.VarintField(2, (ulong)(_entries.Count + 1));
            var shared = new Writer();
            shared.MessageField(1, table);
            return shared;
        }
    }

    private sealed class ArrayWriter
    {
        private readonly SharedTable _table = new();

        /// <summary>An output's array: its string table, when any name was interned, sits between the dimensions and the layout.</summary>
        public Writer WriteTop(JgsValue value)
        {
            Parts parts = Build(value);
            return Assemble(parts, _table.Used ? _table.ToWriter() : null);
        }

        /// <summary>A nested array: no table of its own, the output's is shared.</summary>
        private Writer Write(JgsValue value) => Assemble(Build(value), null);

        /// <summary><c>type</c>, <c>dimension</c> packed, <c>sharedData</c>, <c>memoryLayout</c> COLUMN_MAJOR, <c>isComplex</c>, then the rest.</summary>
        private static Writer Assemble(Parts parts, Writer? shared)
        {
            var array = new Writer();
            array.VarintField(FType, (ulong)parts.Type);
            var packed = new Writer();
            foreach (int d in parts.Dims)
            {
                packed.WriteVarint((ulong)d);
            }

            array.PackedField(FDimension, packed, always: true);
            if (shared is not null)
            {
                array.MessageField(FSharedData, shared);
            }

            array.VarintField(FMemoryLayout, 0UL);
            array.BoolField(FIsComplex, parts.Complex);
            array.Append(parts.Tail.Span);
            return array;
        }

        private Parts Build(JgsValue value)
        {
            if (value.Type == JgsType.Null)
            {
                return Numbers(JgsMatrix.FromColumnMajor([], 0, 0));
            }

            if (value.IsDatetime)
            {
                return Datetime(value);
            }

            if (value.IsDuration)
            {
                return Duration(value);
            }

            if (value.IsCategorical)
            {
                return Categorical(value);
            }

            switch (value.Type)
            {
                case JgsType.Function or JgsType.Image:
                    return HandleRef();
                case JgsType.Table:
                    return Object(value.AsTable.RowTimes is null ? "table" : "timetable", [1, 1], [[]]);
                case JgsType.Sparse:
                    return Sparse(value.AsSparse);
                case JgsType.External:
                    if (value.AsExternalOrNull() is OnOffSwitchStateValue onOff)
                    {
                        return Enumeration(OnOffSwitchStateValue.Name, onOff.Names, [onOff.Rows, onOff.Columns]);
                    }

                    return value.AsExternal.IsHandle ? HandleRef() : Object(value.AsExternal.ClassName, [1, 1], [[]]);
                case JgsType.Object:
                    if (JgsBuiltins.IsEnumerationValue(value))
                    {
                        return Enumeration(value.ObjectClass.Name, JgsBuiltins.EnumerationNamesOf(value), value.IsObjectArray ? value.Dims : [1, 1]);
                    }

                    if (value.ObjectClass.IsHandle)
                    {
                        return HandleRef();
                    }

                    return ValueObjects(value);
                case JgsType.Cell:
                    return Cells(value);
                case JgsType.Struct:
                    if (JgsBuiltins.IsKeyedCollection(value) || value.ClassName is not null)
                    {
                        // containers.Map is a handle (out.map); a dictionary is a value object (out.dict), written bare.
                        string className = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
                        return className == "containers.Map" ? HandleRef() : Object(className, [1, 1], [[]]);
                    }

                    return Structs(value);
            }

            if (value.IsStringArray)
            {
                return Strings(value);
            }

            if (value.IsCharMatrix)
            {
                return Chars(value.CharMatrixRows(), value.Dims);
            }

            if (value.Type == JgsType.String)
            {
                string text = value.AsString;
                return Chars([text], text.Length == 0 ? [0, 0] : [1, text.Length]);
            }

            return Numbers(value);
        }

        private static MwType TypeOf(JgsNumericClass numericClass) => numericClass switch
        {
            JgsNumericClass.UInt8 => MwType.Uint8,
            JgsNumericClass.Int8 => MwType.Int8,
            JgsNumericClass.UInt16 => MwType.Uint16,
            JgsNumericClass.Int16 => MwType.Int16,
            JgsNumericClass.UInt32 => MwType.Uint32,
            JgsNumericClass.Int32 => MwType.Int32,
            JgsNumericClass.UInt64 => MwType.Uint64,
            JgsNumericClass.Int64 => MwType.Int64,
            JgsNumericClass.Single => MwType.Single,
            _ => MwType.Double,
        };

        private static int PayloadField(MwType type) => type switch
        {
            MwType.Uint8 => FUint8,
            MwType.Int8 => FInt8,
            MwType.Uint16 => FUint16,
            MwType.Int16 => FInt16,
            MwType.Uint32 => FUint32,
            MwType.Int32 => FInt32,
            MwType.Uint64 => FUint64,
            MwType.Int64 => FInt64,
            MwType.Single => FSingle,
            _ => FDouble,
        };

        /// <summary>A numeric type's payload: the elements as that type's scalars, packed (int8 and uint8 as bytes), and the imaginary parts beside them.</summary>
        private static Writer NumericPayload(MwType type, double[] real, double[]? imag)
        {
            var payload = new Writer();
            if (type is MwType.Uint8 or MwType.Int8)
            {
                payload.BytesField(1, real.Select(static v => (byte)(long)v).ToArray());
                if (imag is not null)
                {
                    payload.BytesField(2, imag.Select(static v => (byte)(long)v).ToArray());
                }

                return payload;
            }

            void Pack(int field, double[] values)
            {
                var packed = new Writer();
                foreach (double v in values)
                {
                    switch (type)
                    {
                        case MwType.Uint16 or MwType.Uint64:
                            // A 64-bit value is a double in JGraph (ADR 0174): one at or past 2^64 is the class's largest.
                            packed.WriteVarint(v >= 18446744073709551615.0 ? ulong.MaxValue : v <= 0 ? 0UL : (ulong)v);
                            break;
                        case MwType.Int16 or MwType.Int64:
                            packed.WriteVarint(v >= 9223372036854775807.0 ? long.MaxValue : v <= -9223372036854775808.0 ? long.MinValue : (long)v);
                            break;
                        case MwType.Uint32:
                            packed.WriteFixed32((uint)v);
                            break;
                        case MwType.Int32:
                            packed.WriteFixed32(unchecked((uint)(int)v));
                            break;
                        case MwType.Single:
                            packed.WriteFloat((float)v);
                            break;
                        default:
                            packed.WriteDouble(v);
                            break;
                    }
                }

                payload.PackedField(field, packed);
            }

            Pack(1, real);
            if (imag is not null)
            {
                Pack(2, imag);
            }

            return payload;
        }

        private static Parts Tail(MwType type, int[] dims, bool complex, int field, Writer payload)
        {
            var tail = new Writer();
            tail.MessageField(field, payload);
            return new Parts(type, dims, complex, tail);
        }

        private static Parts Numbers(JgsValue value)
        {
            bool logical = JgsBuiltins.IsLogicalValue(value);
            JgsNumericClass numericClass = value.NumericClass;
            bool complex = value.IsPackedComplex || value.Type == JgsType.Complex || JgsBuiltins.HasComplexElements(value);
            int[] dims = value.Type is JgsType.Number or JgsType.Bool or JgsType.Complex ? [1, 1] : value.Dims;
            int count = value.Type is JgsType.Number or JgsType.Bool or JgsType.Complex ? 1 : value.ArrayLength;
            if (logical)
            {
                var flags = new double[count];
                for (int i = 0; i < count; i++)
                {
                    JgsValue e = count == 1 && value.Type == JgsType.Bool ? value : value.ElementAt(i);
                    flags[i] = e.AsNumber != 0 ? 1 : 0;
                }

                return Tail(MwType.Logical, dims, complex: false, FLogical, LogicalPayload(flags));
            }

            MwType type = TypeOf(numericClass);
            var real = new double[count];
            double[]? imag = complex ? new double[count] : null;
            for (int i = 0; i < count; i++)
            {
                JgsValue e = count == 1 && value.Type is JgsType.Number or JgsType.Complex ? value : value.ElementAt(i);
                if (complex)
                {
                    Complex z = e.AsComplex;
                    real[i] = z.Real;
                    imag![i] = z.Imaginary;
                }
                else
                {
                    real[i] = e.AsNumber;
                }
            }

            return Tail(type, dims, complex, PayloadField(type), NumericPayload(type, real, imag));
        }

        private static Writer LogicalPayload(double[] flags)
        {
            var packed = new Writer();
            foreach (double flag in flags)
            {
                packed.WriteBool(flag != 0);
            }

            var payload = new Writer();
            payload.PackedField(1, packed);
            return payload;
        }

        /// <summary>A char array: its code units column-major in <c>mwchar.elements</c> (out.charmatrix).</summary>
        private static Parts Chars(string[] rows, int[] dims)
        {
            var packed = new Writer();
            int height = rows.Length;
            int width = height == 0 ? 0 : rows[0].Length;
            for (int c = 0; c < width; c++)
            {
                for (int r = 0; r < height; r++)
                {
                    packed.WriteVarint((ulong)rows[r][c]);
                }
            }

            var payload = new Writer();
            payload.PackedField(1, packed);
            return Tail(MwType.Char, dims, complex: false, FChar, payload);
        }

        private static Parts Text(string text) => Chars([text], text.Length == 0 ? [0, 0] : [1, text.Length]);

        /// <summary>A string array: every string's code units in a row and their lengths; a missing string is the one unit 0x1FFFF (out.stringmissing).</summary>
        private static Parts Strings(JgsValue value)
        {
            var units = new Writer();
            var sizes = new Writer();
            foreach (JgsValue item in value.BoxedElements())
            {
                string text = item.AsString;
                if (JgsBuiltins.IsMissingText(text))
                {
                    units.WriteVarint((ulong)MissingUnit);
                    sizes.WriteVarint(1UL);
                    continue;
                }

                foreach (char c in text)
                {
                    units.WriteVarint((ulong)c);
                }

                sizes.WriteVarint((ulong)text.Length);
            }

            var payload = new Writer();
            payload.PackedField(1, units);
            payload.PackedField(2, sizes);
            return Tail(MwType.String, value.Dims, complex: false, FStringElements, payload);
        }

        private Parts Cells(JgsValue value)
        {
            var payload = new Writer();
            foreach (JgsValue item in value.AsCell)
            {
                payload.MessageField(1, Write(item));
            }

            return Tail(MwType.Cell, value.Dims, complex: false, FCell, payload);
        }

        /// <summary>A struct array: <c>field_names</c> then every element's fields in turn (out.structarray).</summary>
        private Parts Structs(JgsValue value)
        {
            Dictionary<string, JgsValue>[] elements;
            string[] names;
            int[] dims;
            if (value.IsStructArray)
            {
                JgsStructArray structs = value.AsStructArray;
                elements = structs.Elements;
                names = [.. structs.FieldNames];
                dims = value.Dims;
            }
            else
            {
                elements = [value.AsStruct];
                names = [.. value.AsStruct.Keys];
                dims = [1, 1];
            }

            return StructOf(dims, names, elements);
        }

        private Parts StructOf(int[] dims, string[] names, Dictionary<string, JgsValue>[] elements)
        {
            var payload = new Writer();
            foreach (string name in names)
            {
                payload.StringField(1, name);
            }

            foreach (Dictionary<string, JgsValue> element in elements)
            {
                foreach (string name in names)
                {
                    payload.MessageField(2, Write(element[name]));
                }
            }

            return Tail(MwType.Struct, dims, complex: false, FStruct, payload);
        }

        /// <summary>A sparse array: the compressed column form, <c>type</c> DOUBLE, <c>ir</c>, <c>jc</c> and the values in <c>mwsparsedouble</c> (out.sparse).</summary>
        private static Parts Sparse(CscMatrix matrix)
        {
            var payload = new Writer();
            payload.VarintField(1, 0UL);
            var ir = new Writer();
            var jc = new Writer();
            var values = new double[matrix.NonZeroCount];
            for (int c = 0; c < matrix.Cols; c++)
            {
                jc.WriteVarint((ulong)matrix.ColumnStarts[c]);
                for (int k = matrix.ColumnStarts[c]; k < matrix.ColumnStarts[c + 1]; k++)
                {
                    ir.WriteVarint((ulong)matrix.RowIndices[k]);
                    values[k] = matrix.Values[k];
                }
            }

            jc.WriteVarint((ulong)matrix.ColumnStarts[matrix.Cols]);
            payload.PackedField(2, ir);
            payload.PackedField(3, jc, always: true);
            payload.MessageField(16, NumericPayload(MwType.Double, values, null));
            return Tail(MwType.Sparse, [matrix.Rows, matrix.Cols], complex: false, FSparse, payload);
        }

        /// <summary>An enumeration array: the class and the members' names in the table, each element an <c>Object</c> naming its member (out.enum).</summary>
        private Parts Enumeration(string className, string[] names, int[] dims)
        {
            var tail = new Writer();
            tail.VarintField(FEnumerationClassName, (ulong)_table.Intern(className));
            var elements = new Writer();
            foreach (string name in names)
            {
                var element = new Writer();
                element.VarintField(5, (ulong)_table.Intern(name));
                elements.MessageField(1, element);
            }

            tail.MessageField(FEnumerations, elements);
            return new Parts(MwType.Enumeration, dims, false, tail);
        }

        /// <summary>
        /// A handle: muserve writes a reference into its worker's own object table, an id of its session
        /// in the string table and the object's number (out.handle, out.map); JGraph's ids are its own,
        /// and no client reads a reference back (ADR 0272).
        /// </summary>
        private Parts HandleRef()
        {
            _table.Intern(string.Empty);
            _table.Add(SessionId);
            var refs = new Writer();
            var mvm = new Writer();
            mvm.WriteVarint(1UL);
            refs.PackedField(1, mvm);
            var ids = new Writer();
            ids.WriteVarint((ulong)Interlocked.Increment(ref _nextObjectId));
            refs.PackedField(2, ids);
            var tail = new Writer();
            tail.MessageField(FObjectRefs, refs);
            return new Parts(MwType.HandleObjectRef, [1, 1], false, tail);
        }

        /// <summary>
        /// A value object array: <c>classNameIndex</c> and, per element, <c>propertyName</c>, <c>propertyClassName</c>,
        /// each <c>propertyValue</c> and <c>propertyPackageInfoIndex</c>, the names interned as each
        /// property is written, its value's names after it (out.datetime, out.duration, out.categorical).
        /// </summary>
        private Parts Object(string className, int[] dims, IReadOnlyList<IReadOnlyList<(string Name, Func<Parts> Value, int ClassIndex)>> elements)
        {
            int classIndex = _table.Intern(className);
            var tail = new Writer();
            tail.VarintField(FClassNameIndex, (ulong)classIndex);
            var objects = new Writer();
            foreach (IReadOnlyList<(string Name, Func<Parts> Value, int ClassIndex)> element in elements)
            {
                var names = new Writer();
                var classes = new Writer();
                var values = new List<Writer>();
                var packages = new Writer();
                foreach ((string name, Func<Parts> value, int propertyClass) in element)
                {
                    names.WriteVarint((ulong)_table.Intern(name));
                    classes.WriteVarint((ulong)propertyClass);
                    values.Add(Assemble(value(), null));
                    packages.WriteVarint(0UL);
                }

                var message = new Writer();
                message.PackedField(2, names);
                message.PackedField(3, classes);
                foreach (Writer value in values)
                {
                    message.MessageField(4, value);
                }

                message.PackedField(8, packages);
                objects.MessageField(1, message);
            }

            tail.MessageField(FObjects, objects);
            return new Parts(MwType.Object, dims, false, tail);
        }

        private static Parts Doubles(double[] values, int[] dims) =>
            Tail(MwType.Double, dims, complex: false, FDouble, NumericPayload(MwType.Double, values, null));

        /// <summary>
        /// A datetime as R2025b's worker serializes its object: <c>TimeStamp</c> (milliseconds since 1970 of
        /// the clock shown), the wall-clock parts, <c>__metaData</c> (a datetime-classed object of
        /// <c>TimeZone</c> and <c>Format</c>) and <c>__externalDimensions</c> (out.datetime, out.datetimeutc).
        /// </summary>
        private Parts Datetime(JgsValue value)
        {
            JgsTimeTag tag = value.TimeTag!;
            double[] ms = JgsBuiltins.TimeMs(value);
            int[] dims = value.Type == JgsType.Number ? [1, 1] : value.Dims;
            var parts = new Dictionary<string, double[]>();
            foreach (string part in new[] { "TimeStamp", "Year", "Month", "Day", "Hour", "Minute", "Second" })
            {
                parts[part] = new double[ms.Length];
            }

            for (int i = 0; i < ms.Length; i++)
            {
                parts["TimeStamp"][i] = ms[i] - UnixEpochMs;
                if (double.IsNaN(ms[i]))
                {
                    foreach (string part in new[] { "Year", "Month", "Day", "Hour", "Minute", "Second" })
                    {
                        parts[part][i] = double.NaN;
                    }

                    continue;
                }

                DateTime wall = JgsTime.WallClock(ms[i], tag, out int cycles);
                parts["Year"][i] = JgsTime.YearOf(wall, cycles);
                parts["Month"][i] = wall.Month;
                parts["Day"][i] = wall.Day;
                parts["Hour"][i] = wall.Hour;
                parts["Minute"][i] = wall.Minute;
                parts["Second"][i] = wall.Second + (wall.Millisecond / 1000.0);
            }

            int classIndex = _table.Intern("datetime");
            var properties = new List<(string, Func<Parts>, int)>();
            foreach (string part in new[] { "TimeStamp", "Year", "Month", "Day", "Hour", "Minute", "Second" })
            {
                double[] numbers = parts[part];
                properties.Add((part, () => Doubles(numbers, dims), 0));
            }

            properties.Add(("__metaData", () => Object("datetime", [1, 1],
                [[("TimeZone", () => Text(tag.TimeZone ?? string.Empty), 0), ("Format", () => Text(tag.Format), 0)]]), classIndex));
            properties.Add(("__externalDimensions", () => Tail(MwType.Uint64, [1, dims.Length], complex: false, FUint64,
                NumericPayload(MwType.Uint64, [.. dims.Select(static d => (double)d)], null)), classIndex));
            // The object is one; the array's shape is in its properties (out.categorical: a 1-by-2 is an OBJECT 1-by-1).
            return Object("datetime", [1, 1], [properties]);
        }

        /// <summary>A duration: <c>millis</c> and <c>fmt</c> (out.duration).</summary>
        private Parts Duration(JgsValue value)
        {
            JgsTimeTag tag = value.TimeTag!;
            double[] ms = JgsBuiltins.TimeMs(value);
            int[] dims = value.Type == JgsType.Number ? [1, 1] : value.Dims;
            return Object("duration", [1, 1], [[("millis", () => Doubles(ms, dims), 0), ("fmt", () => Text(tag.Format), 0)]]);
        }

        /// <summary>A categorical: <c>categoryNames</c> as a column cell, <c>codes</c> as the narrowest unsigned integers, <c>isProtected</c>, <c>isOrdinal</c> (out.categorical).</summary>
        private Parts Categorical(JgsValue value)
        {
            JgsCategoricalTag tag = value.CategoricalTag!;
            double[] codes = JgsCategorical.CodesOf(value);
            int[] dims = value.Type == JgsType.Number ? [1, 1] : value.Dims;
            MwType codeType = tag.Categories.Length <= byte.MaxValue ? MwType.Uint8 : tag.Categories.Length <= ushort.MaxValue ? MwType.Uint16 : MwType.Uint32;
            return Object("categorical", [1, 1],
            [
                [
                    ("categoryNames", () => CellOf([.. tag.Categories.Select(static c => (Func<Parts>)(() => Text(c)))], [tag.Categories.Length, 1]), 0),
                    ("codes", () => Tail(codeType, dims, complex: false, PayloadField(codeType), NumericPayload(codeType, codes, null)), 0),
                    ("isProtected", () => Tail(MwType.Logical, [1, 1], complex: false, FLogical, LogicalPayload([tag.Protected ? 1 : 0])), 0),
                    ("isOrdinal", () => Tail(MwType.Logical, [1, 1], complex: false, FLogical, LogicalPayload([tag.Ordinal ? 1 : 0])), 0),
                ],
            ]);
        }

        private static Parts CellOf(Func<Parts>[] items, int[] dims)
        {
            var payload = new Writer();
            foreach (Func<Parts> item in items)
            {
                payload.MessageField(1, Assemble(item(), null));
            }

            return Tail(MwType.Cell, dims, complex: false, FCell, payload);
        }

        /// <summary>A user value object or array of them: each element's properties by name, in the order the object holds them.</summary>
        private Parts ValueObjects(JgsValue value)
        {
            string className = value.ObjectClass.Name;
            JgsObject[] objects = value.IsObjectArray ? [.. value.AsObjectArray.Elements] : [value.AsObject];
            int[] dims = value.IsObjectArray ? value.Dims : [1, 1];
            var elements = new List<IReadOnlyList<(string, Func<Parts>, int)>>();
            foreach (JgsObject instance in objects)
            {
                var properties = new List<(string, Func<Parts>, int)>();
                foreach ((string name, JgsValue property) in instance.Fields)
                {
                    properties.Add((name, () => Build(property), 0));
                }

                elements.Add(properties);
            }

            return Object(className, dims, elements);
        }
    }
}
