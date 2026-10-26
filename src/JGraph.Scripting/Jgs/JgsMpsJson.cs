using System.Globalization;
using System.Numerics;
using System.Text;
using JGraph.Numerics.Sparse;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The JSON of MATLAB Production Server (compiler and SDK plan, stage K14; fixture
/// <c>k14_mps_json</c>, probes <c>probe_json*</c>, <c>k14_server</c>): what <c>mps.json.encode</c>
/// writes and <c>mps.json.decode</c> reads, in R2025b's two notations.
/// <list type="bullet">
/// <item><b>Small notation</b> covers a real double or logical array (a scalar as the bare value, a
/// column as a flat list, anything else nested by its dimensions, the first outermost), a char row
/// as a JSON string, and a scalar struct as an object of its fields; NaN and the infinities are the
/// objects <c>{"mwdata":"NaN"}</c>. Every other value takes the large form, its children small.</item>
/// <item><b>Large notation</b> wraps everything as <c>{"mwdata":…,"mwsize":[…],"mwtype":"…"}</c> with
/// <c>mwcomplex</c>, <c>mwsparse</c> and <c>mwmetadata</c> when they apply, the keys in alphabetical
/// order and the data column-major; a struct's fields are each a list over the elements.</item>
/// </list>
/// Struct fields are written in ordinal order, as R2025b writes and reads them. Numbers are written
/// as <c>jsonencode</c> writes them (<see cref="JgsJson.FormatDouble"/>). Decoding checks what R2025b
/// checks, in its words; where R2025b's decoder ends the MATLAB process (a field name MATLAB cannot
/// hold, an object <c>mwdata</c> for a class, a malformed sparse or string <c>mwdata</c>) JGraph
/// refuses instead, with R2025b's own text where it printed one (ADR 0270).
/// </summary>
internal static class JgsMpsJson
{
    /// <summary>The two notations, <c>Format</c>'s values.</summary>
    internal enum Notation
    {
        Small,
        Large,
    }

    /// <summary>How NaN and the infinities are written, <c>NanInfType</c>'s values: the text <c>"NaN"</c>, or the object <c>{"mwdata":"NaN"}</c>.</summary>
    internal enum NanInfType
    {
        String,
        Object,
    }

    /// <summary>A value the encoder cannot write or text the decoder cannot read, with R2025b's identifier and message.</summary>
    internal sealed class Refused(string identifier, string message) : Exception(message)
    {
        public string Identifier { get; } = identifier;
    }

    /// <summary>The milliseconds between JGraph's datetime epoch (1899-12-30) and R2025b's <c>TimeStamp</c> epoch (1970-01-01).</summary>
    private static readonly double UnixEpochMs = (new DateTime(1970, 1, 1) - JgsTime.Epoch).TotalMilliseconds;

    private const string Prefix = "MPS:MATLAB:JSON:";

    private static Refused Error(string mnemonic, string message) => new(Prefix + mnemonic, message);

    // --- the tree ---------------------------------------------------------------------------------

    private abstract record Node;

    private sealed record Num(string Text) : Node;

    private sealed record Str(string Text) : Node;

    private sealed record Lit(string Text) : Node; // true, false, null

    private sealed record Arr(List<Node> Items) : Node;

    private sealed record Obj(List<(string Name, Node Value)> Members) : Node
    {
        public Node? this[string name] => Members.FirstOrDefault(m => m.Name == name).Value;

        public bool Has(string name) => Members.Any(m => m.Name == name);
    }

    // --- encoding ---------------------------------------------------------------------------------

    /// <summary><c>mps.json.encode(value, 'Format', notation, 'NanInfType', nanType, 'PrettyPrint', pretty)</c>, or <see cref="Refused"/>.</summary>
    public static string Encode(JgsValue value, Notation notation, NanInfType nanType, bool pretty, bool datetimeParts = false)
    {
        Node node = new Writer(notation, nanType, datetimeParts).Write(value);
        var text = new StringBuilder();
        Emit(text, node, pretty, 0);
        return text.ToString();
    }

    /// <summary>
    /// <c>mps.json.encoderequest(rhs, ...)</c>: the request body a server takes, each argument in
    /// <paramref name="notation"/>; <paramref name="nargoutText"/> is <c>num2str(Nargout)</c> as
    /// R2025b writes it, whatever it was given.
    /// </summary>
    public static string EncodeRequest(
        IReadOnlyList<JgsValue> rhs, string nargoutText, Notation notation, NanInfType nanType, Notation outputNotation,
        NanInfType outputNanType, bool pretty)
    {
        string nl = pretty ? "\n" : string.Empty;
        var text = new StringBuilder("{\"rhs\":[");
        for (int i = 0; i < rhs.Count; i++)
        {
            if (i > 0)
            {
                text.Append(',').Append(nl);
            }

            text.Append(Encode(rhs[i], notation, nanType, pretty));
        }

        text.Append("],").Append(nl)
            .Append("\"nargout\":").Append(nargoutText).Append(',').Append(nl)
            .Append("\"outputFormat\":{\"mode\":\"").Append(outputNotation == Notation.Large ? "large" : "small")
            .Append("\",\"nanType\":\"").Append(outputNanType == NanInfType.Object ? "object" : "string").Append("\"}").Append(nl)
            .Append('}');
        return text.ToString();
    }

    /// <summary>A server's reply with the outputs: <c>{"lhs":[…]}</c>, each in the asked notation.</summary>
    public static string EncodeResponse(IReadOnlyList<JgsValue> outputs, Notation notation, NanInfType nanType, bool datetimeParts = true)
    {
        var text = new StringBuilder("{\"lhs\":[");
        for (int i = 0; i < outputs.Count; i++)
        {
            if (i > 0)
            {
                text.Append(',');
            }

            text.Append(Encode(outputs[i], notation, nanType, pretty: false, datetimeParts));
        }

        return text.Append("]}").ToString();
    }

    /// <summary>
    /// A server's reply for an error the function did not catch, as R2025b's server writes it
    /// (k14_server: error.*): the identifier, the message, the M-code frames innermost first, each
    /// <c>{"file","line","name"}</c>, and <c>"type":"matlaberror"</c>, the keys in order.
    /// </summary>
    public static string EncodeError(string identifier, string message, IReadOnlyList<(string Name, string File, int Line)> frames, bool withStack = true)
    {
        var stack = new List<Node>(frames.Count);
        foreach ((string name, string file, int line) in frames)
        {
            stack.Add(new Obj([("file", new Str(file)), ("line", new Num(line.ToString(CultureInfo.InvariantCulture))), ("name", new Str(name))]));
        }

        // With --hide-matlab-error-stack the server leaves the key out altogether (k15_server: hidden.*).
        var error = new Obj(withStack
            ?
            [
                ("id", new Str(identifier)),
                ("message", new Str(message)),
                ("stack", new Arr(stack)),
                ("type", new Str("matlaberror")),
            ]
            :
            [
                ("id", new Str(identifier)),
                ("message", new Str(message)),
                ("type", new Str("matlaberror")),
            ]);
        var text = new StringBuilder();
        Emit(text, new Obj([("error", error)]), pretty: false, 0);
        return text.ToString();
    }

    private sealed class Writer(Notation notation, NanInfType nanType, bool datetimeParts)
    {
        private bool Small => notation == Notation.Small;

        public Node Write(JgsValue value)
        {
            if (value.Type == JgsType.Null)
            {
                return Small ? new Arr([]) : Wrap("double", [0, 0], new Arr([]));
            }

            if (value.IsTime)
            {
                return value.IsDatetime ? Datetime(value) : throw Unsupported(value.IsDuration ? "duration" : "calendarDuration");
            }

            if (value.IsCategorical)
            {
                throw Unsupported("categorical");
            }

            switch (value.Type)
            {
                case JgsType.Function:
                    throw Unsupported("function_handle");
                case JgsType.Table:
                    throw Unsupported(value.AsTable.RowTimes is null ? "table" : "timetable");
                case JgsType.Image:
                    throw Unsupported("image");
                case JgsType.Sparse:
                    return Sparse(value.AsSparse);
                case JgsType.External:
                    if (value.AsExternalOrNull() is OnOffSwitchStateValue onOff)
                    {
                        return Enumeration(OnOffSwitchStateValue.Name, onOff.Names, [onOff.Rows, onOff.Columns]);
                    }

                    throw Unsupported(value.AsExternal.ClassName);
                case JgsType.Object:
                    if (JgsBuiltins.IsEnumerationValue(value))
                    {
                        return Enumeration(value.ObjectClass.Name, JgsBuiltins.EnumerationNamesOf(value), value.IsObjectArray ? value.Dims : [1, 1]);
                    }

                    throw Unsupported(value.ClassName ?? "object");
                case JgsType.Cell:
                    return Cell(value);
                case JgsType.Struct:
                    if (JgsBuiltins.IsKeyedCollection(value) || value.ClassName is not null)
                    {
                        throw Unsupported(JgsBuiltins.ClassOf(value, JgsDialect.Matlab));
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
                int[] dims = text.Length == 0 ? [0, 0] : [1, text.Length];
                return Small && dims[0] <= 1 ? new Str(text) : Wrap("char", dims, new Arr([new Str(text)]));
            }

            return Numbers(value);
        }

        private static Refused Unsupported(string className) => Error("EncodeUnsupportedType", $"{className} objects not supported");

        /// <summary>The large form: the keys in alphabetical order, <c>mwcomplex</c> and <c>mwsparse</c> only when true.</summary>
        private static Obj Wrap(string type, int[] dims, Node data, bool complex = false, bool sparse = false, Node? metadata = null)
        {
            var members = new List<(string, Node)>(6);
            if (complex)
            {
                members.Add(("mwcomplex", new Lit("true")));
            }

            members.Add(("mwdata", data));
            if (metadata is not null)
            {
                members.Add(("mwmetadata", metadata));
            }

            members.Add(("mwsize", Size(dims)));
            if (sparse)
            {
                members.Add(("mwsparse", new Lit("true")));
            }

            members.Add(("mwtype", new Str(type)));
            return new Obj(members);
        }

        private static Arr Size(int[] dims) => new([.. dims.Select(static d => (Node)new Num(d.ToString(CultureInfo.InvariantCulture)))]);

        private Node NanOrInf(double x, bool inSmall)
        {
            string word = double.IsNaN(x) ? "NaN" : x > 0 ? "Inf" : "-Inf";
            // The small notation has no place for a bare "NaN" (it would be a char), so it is always the object.
            return inSmall || nanType == NanInfType.Object ? new Obj([("mwdata", new Str(word))]) : new Str(word);
        }

        private Node Number(double x, JgsNumericClass numericClass, bool inSmall)
        {
            if (double.IsNaN(x) || double.IsInfinity(x))
            {
                return NanOrInf(x, inSmall);
            }

            return numericClass switch
            {
                JgsNumericClass.Double => new Num(JgsJson.FormatDouble(x)),
                JgsNumericClass.Single => new Num(JgsJson.FormatSingle((float)x)),
                _ => new Num(x.ToString("F0", CultureInfo.InvariantCulture)),
            };
        }

        private Node Numbers(JgsValue value)
        {
            bool logical = JgsBuiltins.IsLogicalValue(value);
            JgsNumericClass numericClass = value.NumericClass;
            bool complex = value.IsPackedComplex || value.Type == JgsType.Complex || JgsBuiltins.HasComplexElements(value);
            int[] dims = value.Type is JgsType.Number or JgsType.Bool or JgsType.Complex ? [1, 1] : value.Dims;
            int count = value.Type is JgsType.Number or JgsType.Bool or JgsType.Complex ? 1 : value.ArrayLength;
            string type = logical ? "logical" : numericClass.MatlabName();

            if (complex)
            {
                // Interleaved real and imaginary parts, the array's class kept (e_c_*).
                var parts = new List<Node>(2 * count);
                for (int i = 0; i < count; i++)
                {
                    Complex z = (count == 1 && value.Type == JgsType.Complex ? value : value.ElementAt(i)).AsComplex;
                    parts.Add(Number(z.Real, numericClass, inSmall: false));
                    parts.Add(Number(z.Imaginary, numericClass, inSmall: false));
                }

                return Wrap(type, dims, new Arr(parts), complex: true);
            }

            Node Element(int i, bool inSmall)
            {
                JgsValue e = count == 1 && value.Type is JgsType.Number or JgsType.Bool ? value : value.ElementAt(i);
                double x = e.AsNumber;
                return logical || e.Type == JgsType.Bool ? new Lit(x != 0 ? "true" : "false") : Number(x, numericClass, inSmall);
            }

            bool smallClass = logical || numericClass == JgsNumericClass.Double;
            // The small notation holds a double or logical array whose every dimension has elements,
            // or an empty 0-by-0 double (e_d_*): a 1-by-0, a 0-by-3 or logical([]) takes the large form (e_l_0x0).
            if (Small && smallClass && (count > 0 || (!logical && dims.Length == 2 && dims[0] == 0 && dims[1] == 0)))
            {
                return Nest(dims, count, i => Element(i, inSmall: true));
            }

            var items = new List<Node>(count);
            for (int i = 0; i < count; i++)
            {
                items.Add(Element(i, inSmall: false));
            }

            return Wrap(type, dims, new Arr(items));
        }

        /// <summary>
        /// A small-notation array: a scalar is the bare value, a column a flat list, and anything else
        /// nests by every dimension, the first outermost (e_d_2x2x2: <c>[[[1,5],[3,7]],[[2,6],[4,8]]]</c>).
        /// </summary>
        private static Node Nest(int[] dims, int count, Func<int, Node> element)
        {
            if (count == 0)
            {
                return new Arr([]);
            }

            if (count == 1)
            {
                return element(0);
            }

            if (dims.Length == 2 && dims[1] == 1)
            {
                return new Arr([.. Enumerable.Range(0, count).Select(element)]);
            }

            int[] strides = new int[dims.Length];
            int stride = 1;
            for (int d = 0; d < dims.Length; d++)
            {
                strides[d] = stride;
                stride *= dims[d];
            }

            Node Level(int d, int offset)
            {
                if (d == dims.Length)
                {
                    return element(offset);
                }

                var items = new List<Node>(dims[d]);
                for (int i = 0; i < dims[d]; i++)
                {
                    items.Add(Level(d + 1, offset + (i * strides[d])));
                }

                return new Arr(items);
            }

            return Level(0, 0);
        }

        private Node Chars(string[] rows, int[] dims)
        {
            // One string of the characters in column-major order (e_ch_2x3: 'adbecf').
            var text = new StringBuilder();
            int height = rows.Length;
            int width = height == 0 ? 0 : rows[0].Length;
            for (int c = 0; c < width; c++)
            {
                for (int r = 0; r < height; r++)
                {
                    text.Append(rows[r][c]);
                }
            }

            return Wrap("char", dims, new Arr([new Str(text.ToString())]));
        }

        private Node Strings(JgsValue value)
        {
            JgsValue[] items = value.BoxedElements();
            var nodes = new List<Node>(items.Length);
            foreach (JgsValue item in items)
            {
                string text = item.AsString;
                nodes.Add(JgsBuiltins.IsMissingText(text) ? new Obj([("mwdata", new Str("missing"))]) : new Str(text));
            }

            return Wrap("string", value.Dims, new Arr(nodes));
        }

        private Node Cell(JgsValue value)
        {
            JgsValue[] items = value.AsCell;
            return Wrap("cell", value.Dims, new Arr([.. items.Select(item => Inner(item, "cell"))]));
        }

        /// <summary>
        /// A container's element. R2025b checks a value for MATLAB's own classes before it serializes it, so a table
        /// in a cell is refused as a table (e_u_cell_table); a function handle, or another object, passes that check
        /// and fails in the serializer, which names the container (e_u_string_in_cell_fh, e_u_struct_fh).
        /// </summary>
        private Node Inner(JgsValue element, string container)
        {
            try
            {
                return Write(element);
            }
            catch (Refused refused) when (refused.Identifier == "MPS:MATLAB:JSON:EncodeUnsupportedType"
                && element.Type is not (JgsType.Cell or JgsType.Struct or JgsType.Table)
                && !element.IsDuration && !element.IsCategorical && !JgsBuiltins.IsKeyedCollection(element))
            {
                throw Unsupported(container);
            }
        }

        private Node Structs(JgsValue value)
        {
            if (!value.IsStructArray)
            {
                Dictionary<string, JgsValue> fields = value.AsStruct;
                string[] names = [.. fields.Keys.Order(StringComparer.Ordinal)];
                if (Small)
                {
                    return new Obj([.. names.Select(n => (n, Inner(fields[n], "struct")))]);
                }

                return Wrap("struct", [1, 1], new Obj([.. names.Select(n => (n, (Node)new Arr([Inner(fields[n], "struct")])))]));
            }

            JgsStructArray array = value.AsStructArray;
            Dictionary<string, JgsValue>[] elements = array.Elements;
            string[] fieldNames = [.. array.FieldNames.Order(StringComparer.Ordinal)];
            int[] dims = value.Dims;
            if (Small && elements.Length == 0 && fieldNames.Length == 0)
            {
                return new Obj([]); // struct([]) in the small notation is {} (e_sa_0x0, a loss R2025b accepts)
            }

            var members = new List<(string, Node)>(fieldNames.Length);
            foreach (string name in fieldNames)
            {
                members.Add((name, new Arr([.. elements.Select(e => Inner(e[name], "struct"))])));
            }

            return Wrap("struct", dims, new Obj(members));
        }

        private Node Sparse(CscMatrix matrix)
        {
            // Zero-based rows, zero-based columns and the values, each stored entry in column-major
            // order (e_sp_*); a logical sparse array has no value list in R2025b, which JGraph does
            // not hold (every sparse array is double here).
            var rows = new List<Node>(matrix.NonZeroCount);
            var cols = new List<Node>(matrix.NonZeroCount);
            var values = new List<Node>(matrix.NonZeroCount);
            for (int c = 0; c < matrix.Cols; c++)
            {
                for (int k = matrix.ColumnStarts[c]; k < matrix.ColumnStarts[c + 1]; k++)
                {
                    rows.Add(new Num(matrix.RowIndices[k].ToString(CultureInfo.InvariantCulture)));
                    cols.Add(new Num(c.ToString(CultureInfo.InvariantCulture)));
                    values.Add(Number(matrix.Values[k], JgsNumericClass.Double, inSmall: false));
                }
            }

            return Wrap("double", [matrix.Rows, matrix.Cols], new Arr([new Arr(rows), new Arr(cols), new Arr(values)]), sparse: true);
        }

        private Node Enumeration(string className, string[] names, int[] dims) =>
            Wrap(className, dims, new Arr([.. names.Select(static n => (Node)new Str(n))]));

        private Node Datetime(JgsValue value)
        {
            // mwdata.TimeStamp is milliseconds since 1970 in the zone's clock (a zoned datetime's UTC
            // instant, an unzoned one's wall clock), written as a double array in the notation asked;
            // mwmetadata carries Format and TimeZone, small as text and large as char arrays (e_dt_*).
            // A server writes the wall-clock parts beside the stamp (k14_server: out.datetime).
            JgsTimeTag tag = value.TimeTag!;
            double[] ms = JgsBuiltins.TimeMs(value);
            int[] dims = value.Type == JgsType.Number ? [1, 1] : value.Dims;
            double[] stamps = [.. ms.Select(m => m - UnixEpochMs)];
            Node DoubleArray(double[] numbers)
            {
                if (Small && (numbers.Length > 0 || (dims.Length == 2 && dims[0] == 0 && dims[1] == 0)))
                {
                    return Nest(dims, numbers.Length, i => Number(numbers[i], JgsNumericClass.Double, inSmall: true));
                }

                return Wrap("double", dims, new Arr([.. numbers.Select(n => Number(n, JgsNumericClass.Double, inSmall: false))]));
            }

            Node Text(string text) => Small ? new Str(text) : Wrap("char", text.Length == 0 ? [0, 0] : [1, text.Length], new Arr([new Str(text)]));

            var data = new List<(string, Node)>();
            if (datetimeParts)
            {
                var parts = new Dictionary<string, double[]>
                {
                    ["Day"] = new double[ms.Length],
                    ["Hour"] = new double[ms.Length],
                    ["Minute"] = new double[ms.Length],
                    ["Month"] = new double[ms.Length],
                    ["Second"] = new double[ms.Length],
                    ["Year"] = new double[ms.Length],
                };
                for (int i = 0; i < ms.Length; i++)
                {
                    if (double.IsNaN(ms[i]))
                    {
                        foreach (double[] part in parts.Values)
                        {
                            part[i] = double.NaN;
                        }

                        continue;
                    }

                    DateTime wall = JgsTime.WallClock(ms[i], tag, out int cycles);
                    parts["Day"][i] = wall.Day;
                    parts["Hour"][i] = wall.Hour;
                    parts["Minute"][i] = wall.Minute;
                    parts["Month"][i] = wall.Month;
                    parts["Second"][i] = wall.Second + (wall.Millisecond / 1000.0);
                    parts["Year"][i] = JgsTime.YearOf(wall, cycles);
                }

                foreach (string part in new[] { "Day", "Hour", "Minute", "Month", "Second" })
                {
                    data.Add((part, DoubleArray(parts[part])));
                }

                data.Add(("TimeStamp", DoubleArray(stamps)));
                data.Add(("Year", DoubleArray(parts["Year"])));
            }
            else
            {
                data.Add(("TimeStamp", DoubleArray(stamps)));
            }

            var metadata = new Obj([("Format", Text(tag.Format)), ("TimeZone", Text(tag.TimeZone ?? string.Empty))]);
            return Wrap("datetime", dims, new Obj(data), metadata: metadata);
        }
    }

    // --- writing text -----------------------------------------------------------------------------

    /// <summary>
    /// The text of a tree: compact, or pretty-printed as R2025b does it (p_*): four spaces a level,
    /// one item or member a line, no space after a colon, and an empty array or object as its
    /// brackets around the current indent.
    /// </summary>
    private static void Emit(StringBuilder text, Node node, bool pretty, int indent)
    {
        switch (node)
        {
            case Num n:
                text.Append(n.Text);
                break;
            case Lit l:
                text.Append(l.Text);
                break;
            case Str s:
                Quote(text, s.Text);
                break;
            case Arr a:
                if (a.Items.Count == 0)
                {
                    text.Append('[').Append(' ', pretty ? indent : 0).Append(']');
                    break;
                }

                text.Append('[');
                for (int i = 0; i < a.Items.Count; i++)
                {
                    if (i > 0)
                    {
                        text.Append(',');
                    }

                    if (pretty)
                    {
                        text.Append('\n').Append(' ', indent + 4);
                    }

                    Emit(text, a.Items[i], pretty, indent + 4);
                }

                if (pretty)
                {
                    text.Append('\n').Append(' ', indent);
                }

                text.Append(']');
                break;
            case Obj o:
                if (o.Members.Count == 0)
                {
                    text.Append('{').Append(' ', pretty ? indent : 0).Append('}');
                    break;
                }

                text.Append('{');
                for (int i = 0; i < o.Members.Count; i++)
                {
                    if (i > 0)
                    {
                        text.Append(',');
                    }

                    if (pretty)
                    {
                        text.Append('\n').Append(' ', indent + 4);
                    }

                    Quote(text, o.Members[i].Name);
                    text.Append(':');
                    Emit(text, o.Members[i].Value, pretty, indent + 4);
                }

                if (pretty)
                {
                    text.Append('\n').Append(' ', indent);
                }

                text.Append('}');
                break;
        }
    }

    /// <summary>A JSON string as R2025b writes it: the slash escaped too, controls as their letters or <c>\u00xx</c>, everything else as it is (e_ch_*).</summary>
    private static void Quote(StringBuilder text, string value)
    {
        text.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': text.Append("\\\""); break;
                case '\\': text.Append("\\\\"); break;
                case '/': text.Append("\\/"); break;
                case '\b': text.Append("\\b"); break;
                case '\f': text.Append("\\f"); break;
                case '\n': text.Append("\\n"); break;
                case '\r': text.Append("\\r"); break;
                case '\t': text.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        text.Append(c);
                    }

                    break;
            }
        }

        text.Append('"');
    }

    // --- decoding ---------------------------------------------------------------------------------

    /// <summary><c>mps.json.decode(text)</c>, or <see cref="Refused"/>. <paramref name="findClass"/> answers an enumeration class by its name.</summary>
    public static JgsValue Decode(string text, Func<string, JgsClass?> findClass) => new Reader(findClass).Top(new Parser(text).ParseDocument());

    /// <summary>
    /// <c>mps.json.decoderesponse(text)</c>: the outputs of <c>{"lhs":[…]}</c> as a column cell, or
    /// the struct <c>jsondecode</c> makes of an <c>{"error":{…}}</c>'s error (r_res_*).
    /// </summary>
    public static JgsValue DecodeResponse(string text, Func<string, JgsClass?> findClass)
    {
        Node root = new Parser(text).ParseDocument();
        if (root is Obj response)
        {
            if (response["lhs"] is Arr outputs)
            {
                var reader = new Reader(findClass);
                JgsValue[] values = [.. outputs.Items.Select(reader.Top)];
                JgsValue cell = JgsValue.Cell(values);
                cell.Reshape(values.Length, 1);
                return cell;
            }

            if (!response.Has("lhs") && response.Has("error"))
            {
                JgsValue whole = JgsJson.Decode(text);
                return whole.AsStruct["error"];
            }
        }

        throw Error("DecodeInvalidFevalResponse", "Text must be in the format {\"lhs\":[...]} or {\"error\":{...}}");
    }

    /// <summary>
    /// A server's request body (k14_server: no.*, nargout.*, rhs.*, outputformat.*): the arguments,
    /// the output count and the output format, or the refusal the server answers with.
    /// </summary>
    public static ServerRequest DecodeRequest(string text, Func<string, JgsClass?> findClass)
    {
        Node root;
        try
        {
            root = new Parser(text, lenientWhitespace: true).ParseDocument();
        }
        catch (Refused)
        {
            throw new ServerRefusal(400, "InvalidJson", "Invalid JSON");
        }

        if (root is not Obj request)
        {
            throw new ServerRefusal(400, "InvalidInput", "Invalid input");
        }

        // The first of a name's values is the one read (body.duplicate).
        Node? nargoutNode = request["nargout"];
        if (nargoutNode is null)
        {
            throw new ServerRefusal(400, "MissingOutputArgs", "nargout missing");
        }

        if (nargoutNode is not Num nargoutNumber || !double.TryParse(nargoutNumber.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double nargoutValue)
            || nargoutValue < 0)
        {
            throw new ServerRefusal(400, "InvalidInput", "Invalid input");
        }

        int nargout = (int)Math.Min(Math.Floor(nargoutValue), int.MaxValue);
        Node? rhsNode = request["rhs"];
        if (rhsNode is null)
        {
            throw new ServerRefusal(400, "MissingInputArgs", "rhs missing.");
        }

        var reader = new Reader(findClass);
        JgsValue[] arguments;
        if (rhsNode is Arr rhs)
        {
            try
            {
                arguments = [.. rhs.Items.Select(reader.Top)];
            }
            catch (Refused)
            {
                throw new ServerRefusal(400, "InvalidInputArgs", "Invalid rhs.");
            }
        }
        else if (rhsNode is Lit { Text: "null" })
        {
            throw new ServerRefusal(400, "InvalidInputArgs", "Invalid rhs.");
        }
        else
        {
            arguments = []; // a number or an object as rhs is taken as no arguments (rhs.number, rhs.object)
        }

        // Without a mode the output is large; the nanType is read only beside a mode (outputformat.*).
        Notation outputNotation = Notation.Large;
        NanInfType nan = NanInfType.String;
        if (request["outputFormat"] is { } formatNode)
        {
            if (formatNode is not Obj format)
            {
                throw new ServerRefusal(400, "InvalidInput", "Invalid input");
            }

            if (format["mode"] is { } modeNode)
            {
                if (modeNode is not Str mode)
                {
                    throw new ServerRefusal(400, "InvalidInput", "Invalid input");
                }

                if (mode.Text == "small")
                {
                    outputNotation = Notation.Small;
                    if (format["nanType"] is Str { Text: "object" })
                    {
                        nan = NanInfType.Object;
                    }
                }
            }
        }

        return new ServerRequest(arguments, nargout, outputNotation, nan);
    }

    /// <summary>What a server's request body asked for.</summary>
    internal sealed record ServerRequest(JgsValue[] Arguments, int Nargout, Notation OutputNotation, NanInfType OutputNanType);

    /// <summary>A request the server refuses before calling anything: the status and the reason phrase R2025b's server writes (k14_server).</summary>
    internal sealed class ServerRefusal(int status, string code, string reason) : Exception(reason)
    {
        public int Status { get; } = status;

        public string Code { get; } = code;

        public string Reason { get; } = reason;
    }

    private sealed class Reader(Func<string, JgsClass?> findClass)
    {
        private static readonly string[] NumericTypes =
            ["double", "single", "int8", "int16", "int32", "int64", "uint8", "uint16", "uint32", "uint64"];

        /// <summary>A value at the top of a document or inside an array or a cell.</summary>
        public JgsValue Top(Node node) => Value(node, inField: false);

        private JgsValue Value(Node node, bool inField)
        {
            switch (node)
            {
                case Num n:
                    return JgsValue.Number(NumberOf(n.Text));
                case Lit { Text: "true" }:
                    return JgsValue.Bool(true);
                case Lit { Text: "false" }:
                    return JgsValue.Bool(false);
                case Lit:
                    throw inField
                        ? NullNotSupported()
                        : Error("DecodeInvalidMWType",
                            "Unsupported \"mwtype\": \"Input value is either foundation::json::Undefined or foundation::json::Null. The value cannot be converted to MATLAB Data Array.\".");
                case Str s:
                    return CharRow(s.Text);
                case Arr a:
                    return SmallArray(a);
                case Obj o:
                    if (o.Has("mwtype") && o.Has("mwsize") && o.Has("mwdata"))
                    {
                        return Large(o);
                    }

                    if (NanObject(o) is { } special)
                    {
                        return JgsValue.Number(special);
                    }

                    return Fields(o);
            }

            throw NullNotSupported();
        }

        private static Refused NullNotSupported() =>
            Error("DecodeNullNotSupported", "JSON null not supported. Value must be a JSON string, JSON number, JSON boolean, JSON array, or JSON object.");

        private static JgsValue CharRow(string text) => text.Length == 0 ? JgsValue.CharMatrix([]) : JgsValue.Str(text);

        /// <summary><c>{"mwdata":"NaN"}</c>, <c>"Inf"</c> or <c>"-Inf"</c>, whatever else the object holds (x_arr_nan_obj_extra); null otherwise.</summary>
        private static double? NanObject(Obj o) => o["mwdata"] is Str { Text: var word } ? SpecialWord(word) : null;

        private static double? SpecialWord(string word) => word switch
        {
            "NaN" => double.NaN,
            "Inf" => double.PositiveInfinity,
            "-Inf" => double.NegativeInfinity,
            _ => null,
        };

        private static double NumberOf(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

        // --- the small notation ---

        private enum Leaf
        {
            Double,
            Logical,
            Char,
            Undefined,
            Object,
        }

        private static Leaf LeafOf(Node node) => node switch
        {
            Num => Leaf.Double,
            Lit { Text: "true" or "false" } => Leaf.Logical,
            Lit => Leaf.Undefined,
            Str => Leaf.Char,
            Obj o when NanObject(o) is not null => Leaf.Double,
            _ => Leaf.Object,
        };

        private static string LeafName(Leaf leaf) => leaf switch
        {
            Leaf.Double => "double",
            Leaf.Logical => "logical",
            Leaf.Char => "char",
            Leaf.Undefined => "undefined",
            _ => "object",
        };

        /// <summary>
        /// A JSON array in the small notation (x_arr_*): numbers (NaN and the infinities as their
        /// objects) or booleans, nested to any depth with every array of one depth the same size;
        /// one level is a column, deeper levels index the dimensions in order, the first outermost.
        /// </summary>
        private static JgsValue SmallArray(Arr array)
        {
            if (array.Items.Count == 0)
            {
                return JgsMatrix.FromColumnMajor([], 0, 0);
            }

            var sizes = new List<int>();
            var leaves = new List<(Node Node, int[] Path)>();
            Leaf? kind = null;
            void Walk(Arr level, int depth, int[] path)
            {
                if (level.Items.Count == 0)
                {
                    throw Error("DecodeMismatchedMWSize", "Number of elements in \"mwdata\" does not match \"mwsize\" specified.");
                }

                if (sizes.Count == depth)
                {
                    sizes.Add(level.Items.Count);
                }
                else if (sizes[depth] != level.Items.Count)
                {
                    throw Error("DecodeArrayInconsistentSizes", "Nested arrays in small notation of the same depth must have consistent sizes.");
                }

                for (int i = 0; i < level.Items.Count; i++)
                {
                    Node item = level.Items[i];
                    int[] here = [.. path, i];
                    if (item is Arr inner)
                    {
                        if (leaves.Count > 0 && leaves[0].Path.Length == depth + 1)
                        {
                            throw Error("DecodeArrayInconsistentDepths", "Nested arrays in small notation must have consistent depths.");
                        }

                        Walk(inner, depth + 1, here);
                        continue;
                    }

                    if (leaves.Count > 0 && leaves[0].Path.Length != depth + 1)
                    {
                        throw Error("DecodeArrayInconsistentDepths", "Nested arrays in small notation must have consistent depths.");
                    }

                    Leaf leaf = LeafOf(item);
                    if (kind is null)
                    {
                        if (leaf is not (Leaf.Double or Leaf.Logical))
                        {
                            throw Error("DecodeInvalidArrayElemType",
                                "Arrays in small notation must contain JSON numbers, JSON booleans, or values {\"mwdata\":\"NaN\"}, {\"mwdata\":\"Inf\"}, or {\"mwdata\": \"-Inf\"}");
                        }

                        kind = leaf;
                    }
                    else if (leaf != kind)
                    {
                        throw Error("DecodeArrayInconsistentTypes",
                            $"Array in small notation cannot have multiple types. Use only {LeafName(kind.Value)} or only {LeafName(leaf)} values.");
                    }

                    leaves.Add((item, here));
                }
            }

            Walk(array, 0, []);
            int depth = sizes.Count;
            int[] dims = depth == 1 ? [sizes[0], 1] : [.. sizes];
            var values = new JgsValue[leaves.Count];
            foreach ((Node node, int[] path) in leaves)
            {
                int index = 0;
                int stride = 1;
                for (int d = 0; d < path.Length; d++)
                {
                    index += path[d] * stride;
                    stride *= sizes[d];
                }

                values[index] = kind == Leaf.Logical
                    ? JgsValue.Bool(((Lit)node).Text == "true")
                    : JgsValue.Number(node is Num n ? NumberOf(n.Text) : NanObject((Obj)node)!.Value);
            }

            if (values.Length == 1 && dims.All(static d => d == 1))
            {
                return values[0];
            }

            return kind == Leaf.Logical
                ? JgsMatrix.FromElementsDims(values, dims)
                : JgsMatrix.FromColumnMajorDims([.. values.Select(static v => v.AsNumber)], dims);
        }

        /// <summary>A JSON object as a scalar struct: its fields in ordinal order, each a value (x_obj_*); a null field refused.</summary>
        private JgsValue Fields(Obj o)
        {
            var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            foreach ((string name, Node value) in o.Members.OrderBy(static m => m.Name, StringComparer.Ordinal))
            {
                CheckFieldName(name);
                fields.TryAdd(name, Value(value, inField: true)); // the first of a name's values is kept (x_obj_dup)
            }

            return JgsValue.Struct(fields);
        }

        /// <summary>
        /// A field name MATLAB can hold. R2025b's decoder ends the process on one it cannot ("Invalid
        /// field name "1a" specified."); JGraph refuses with that text. An empty name is refused in
        /// R2025b's own words.
        /// </summary>
        private static void CheckFieldName(string name)
        {
            if (name.Length == 0)
            {
                throw Error("DecodeInvalidMWType", "Unsupported \"mwtype\": \"invalid string position\".");
            }

            if (name.Length > 63 || !char.IsAsciiLetter(name[0]) || name.Any(static c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))
            {
                throw Error("DecodeInvalidFieldName", $"Invalid field name \"{name}\" specified.");
            }
        }

        // --- the large notation ---

        private static Refused DataMustBe(string type, string what) => Error("DecodeInvalidMWDataType", $"\"mwdata\" for {type} arrays must be {what}.");

        private static Refused Mismatched() => Error("DecodeMismatchedMWSize", "Number of elements in \"mwdata\" does not match \"mwsize\" specified.");

        private static Refused Unsupported(string detail) => Error("DecodeInvalidMWType", $"Unsupported \"mwtype\": \"{detail}\".");

        private JgsValue Large(Obj o)
        {
            if (o["mwtype"] is not Str typeNode)
            {
                throw Error("DecodeInvalidPropertyType", "Property \"mwtype\" must be a JSON string.");
            }

            string type = typeNode.Text;
            int[] dims = SizeOf(o["mwsize"]!);
            bool complex = Flag(o, "mwcomplex");
            bool sparse = Flag(o, "mwsparse");
            Node data = o["mwdata"]!;
            long count = dims.Aggregate(1L, static (a, b) => a * b);

            if (sparse)
            {
                return Sparse(type, dims, data, complex);
            }

            if (Array.IndexOf(NumericTypes, type) >= 0)
            {
                return Numbers(type, dims, data, complex, count);
            }

            switch (type)
            {
                case "logical":
                    return Logicals(dims, data, count);
                case "char":
                    return Chars(dims, data, count);
                case "string":
                    return Strings(dims, data, count);
                case "cell":
                    return Cells(dims, data, count);
                case "struct":
                    return Structs(dims, data, count);
                case "datetime":
                    return Datetime(dims, data, o["mwmetadata"], count);
            }

            return Enumeration(type, dims, data);
        }

        private static int[] SizeOf(Node node)
        {
            if (node is not Arr size)
            {
                throw Error("DecodeInvalidPropertyType", "Property \"mwsize\" must be a JSON array.");
            }

            var dims = new List<int>(size.Items.Count);
            foreach (Node item in size.Items)
            {
                if (item is not Num n || !IntegerToken(n.Text) || !int.TryParse(n.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int d) || d < 0)
                {
                    throw Error("DecodeInvalidMWSize", "\"mwsize\" must be an array of 2 or more positive integers.");
                }

                dims.Add(d);
            }

            if (dims.Count < 2)
            {
                throw Error("DecodeInvalidMWSize", "\"mwsize\" must be an array of 2 or more positive integers.");
            }

            while (dims.Count > 2 && dims[^1] == 1)
            {
                dims.RemoveAt(dims.Count - 1); // a trailing 1 is no dimension (y_size_trailing1)
            }

            return [.. dims];
        }

        private static bool Flag(Obj o, string name)
        {
            Node? node = o[name];
            if (node is null)
            {
                return false;
            }

            if (node is Lit { Text: "true" or "false" } flag)
            {
                return flag.Text == "true";
            }

            throw Error("DecodeInvalidPropertyType", $"Property \"{name}\" must be a JSON boolean.");
        }

        /// <summary>Whether a number token is an integer as written: no fraction and no exponent (y_int8_exp: 1e2 is not).</summary>
        private static bool IntegerToken(string text) => text.IndexOfAny(['.', 'e', 'E']) < 0;

        private static JgsNumericClass ClassOf(string type) => type switch
        {
            "single" => JgsNumericClass.Single,
            "int8" => JgsNumericClass.Int8,
            "int16" => JgsNumericClass.Int16,
            "int32" => JgsNumericClass.Int32,
            "int64" => JgsNumericClass.Int64,
            "uint8" => JgsNumericClass.UInt8,
            "uint16" => JgsNumericClass.UInt16,
            "uint32" => JgsNumericClass.UInt32,
            "uint64" => JgsNumericClass.UInt64,
            _ => JgsNumericClass.Double,
        };

        private static Refused InvalidFloating(string type) =>
            Error("DecodeInvalidFloatingElem",
                $"Invalid value for type {type}. Value must be a JSON number or one of the following values: \"NaN\", \"Inf\", \"-Inf\", {{\"mwdata\":\"NaN\"}}, {{\"mwdata\":\"Inf\"}}, or {{\"mwdata\": \"-Inf\"}}.");

        /// <summary>A double or single element: a number, or NaN and the infinities as text or objects (y_double_*).</summary>
        private static double Floating(Node node, string type)
        {
            double x = node switch
            {
                Num n => NumberOf(n.Text),
                Str s when SpecialWord(s.Text) is { } special => special,
                Obj o when NanObject(o) is { } special => special,
                _ => throw InvalidFloating(type),
            };
            if (type == "single" && !double.IsInfinity(x) && Math.Abs(x) > float.MaxValue)
            {
                throw InvalidFloating(type); // y_single_big: 1e40 has no single
            }

            return x;
        }

        /// <summary>
        /// An integer element (y_int*): an integer token, read saturating for the 64-bit classes
        /// and refused out of range for the others, a negative one for an unsigned class read as 0.
        /// </summary>
        private static double Integer(Node node, string type, JgsNumericClass numericClass)
        {
            if (node is not Num n || !IntegerToken(n.Text))
            {
                throw Error("DecodeInvalidIntegralElem", "Invalid value for type int or uint. Value must be an integral number.");
            }

            var value = BigInteger.Parse(n.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            bool unsigned = numericClass is JgsNumericClass.UInt8 or JgsNumericClass.UInt16 or JgsNumericClass.UInt32 or JgsNumericClass.UInt64;
            if (unsigned && value.Sign < 0)
            {
                return 0;
            }

            (BigInteger min, BigInteger max) = numericClass switch
            {
                JgsNumericClass.Int8 => (new BigInteger(sbyte.MinValue), new BigInteger(sbyte.MaxValue)),
                JgsNumericClass.Int16 => (new BigInteger(short.MinValue), new BigInteger(short.MaxValue)),
                JgsNumericClass.Int32 => (new BigInteger(int.MinValue), new BigInteger(int.MaxValue)),
                JgsNumericClass.Int64 => (new BigInteger(long.MinValue), new BigInteger(long.MaxValue)),
                JgsNumericClass.UInt8 => (BigInteger.Zero, new BigInteger(byte.MaxValue)),
                JgsNumericClass.UInt16 => (BigInteger.Zero, new BigInteger(ushort.MaxValue)),
                JgsNumericClass.UInt32 => (BigInteger.Zero, new BigInteger(uint.MaxValue)),
                _ => (BigInteger.Zero, new BigInteger(ulong.MaxValue)),
            };
            if (value < min || value > max)
            {
                if (numericClass is JgsNumericClass.Int64 or JgsNumericClass.UInt64)
                {
                    value = value < min ? min : max; // y_int64_over, y_uint64_over: the 64-bit classes saturate
                }
                else
                {
                    throw Error("DecodeInvalidIntegralElem", $"Invalid value for type {type}. Value must be an integral number.");
                }
            }

            return (double)value;
        }

        private JgsValue Numbers(string type, int[] dims, Node data, bool complex, long count)
        {
            if (data is not Arr items)
            {
                throw DataMustBe(type, "a JSON array");
            }

            JgsNumericClass numericClass = ClassOf(type);
            bool floating = numericClass is JgsNumericClass.Double or JgsNumericClass.Single;
            double Element(Node node) => floating ? Floating(node, type) : Integer(node, type, numericClass);

            if (complex)
            {
                // Pairs of real and imaginary parts (y_complex_*). R2025b takes no NaN text in them
                // and so cannot read what it writes; JGraph reads it (ADR 0270).
                var parts = new double[items.Items.Count];
                for (int i = 0; i < parts.Length; i++)
                {
                    if (items.Items[i] is Obj inner && NanObject(inner) is null)
                    {
                        throw Error("DecodeIncompleteComplexData", "Unexpected end of complex number data");
                    }

                    // A complex part is a JSON number: R2025b refuses its own "NaN" word here (y_complex_nan, e_c_nan_round).
                    if (items.Items[i] is Str)
                    {
                        throw InvalidFloating(type);
                    }

                    parts[i] = Element(items.Items[i]);
                }

                if (parts.Length % 2 != 0)
                {
                    throw Error("DecodeIncompleteComplexData", "Unexpected end of complex number data");
                }

                if (parts.Length / 2 != count)
                {
                    throw Mismatched();
                }

                int n = parts.Length / 2;
                if (n == 1)
                {
                    JgsValue scalar = JgsValue.ComplexNum(new Complex(parts[0], parts[1]));
                    scalar.SetNumericClass(numericClass);
                    return scalar;
                }

                var pairs = new JgsValue[n];
                for (int i = 0; i < n; i++)
                {
                    pairs[i] = JgsValue.ComplexNum(new Complex(parts[2 * i], parts[(2 * i) + 1]));
                }

                JgsValue array = JgsMatrix.FromElementsDims(pairs, dims);
                array.SetNumericClass(numericClass);
                return array;
            }

            var values = new double[items.Items.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = Element(items.Items[i]);
            }

            if (values.Length != count)
            {
                throw Mismatched();
            }

            JgsValue result = values.Length == 1 && dims.All(static d => d == 1) ? JgsValue.Number(values[0]) : JgsMatrix.FromColumnMajorDims(values, dims);
            if (numericClass != JgsNumericClass.Double)
            {
                result.SetNumericClass(numericClass);
            }

            return result;
        }

        private static JgsValue Logicals(int[] dims, Node data, long count)
        {
            if (data is not Arr items)
            {
                throw DataMustBe("logical", "a JSON array");
            }

            var values = new JgsValue[items.Items.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = items.Items[i] is Lit { Text: "true" or "false" } flag
                    ? JgsValue.Bool(flag.Text == "true")
                    : throw Error("DecodeInvalidLogicalElem", "Invalid value for type logical.  Value must be 'true' or 'false'.");
            }

            if (values.Length != count)
            {
                throw Mismatched();
            }

            if (values.Length == 1 && dims.All(static d => d == 1))
            {
                return values[0];
            }

            if (values.Length == 0)
            {
                JgsValue empty = JgsBuiltins.EmptyLogical(dims[0], dims.Length == 2 ? dims[1] : 0);
                if (dims.Length > 2)
                {
                    empty.ReshapeDims(dims);
                }

                return empty;
            }

            return JgsMatrix.FromElementsDims(values, dims);
        }

        private static JgsValue Chars(int[] dims, Node data, long count)
        {
            if (data is not Arr items)
            {
                throw DataMustBe("char", "a JSON array");
            }

            if (items.Items is not [Str text])
            {
                throw Error("DecodeInvalidCharArrayMWData", "\"mwdata\" for char array must be in the format '[\"...\"]'.");
            }

            if (text.Text.Length != count)
            {
                throw Mismatched();
            }

            if (dims.Length > 2)
            {
                throw Error("DecodeInvalidMWSize", "JGraph holds a char array of two dimensions; R2025b holds more.");
            }

            int rows = dims[0];
            int cols = dims[1];
            if (count == 0)
            {
                // An empty char keeps the shape mwsize gives it: 0-by-1 (e_ch_0x1) as well as 0-by-0 and 2-by-0.
                JgsValue made = JgsValue.CharMatrix(rows == 0 ? [] : [.. Enumerable.Repeat(string.Empty, rows)]);
                made.Reshape(rows, cols);
                return made;
            }

            if (rows == 1)
            {
                return JgsValue.Str(text.Text);
            }

            // The characters arrive column-major (y_char_matrix: 'adbecf' is ['abc';'def']).
            var lines = new StringBuilder[rows];
            for (int r = 0; r < rows; r++)
            {
                lines[r] = new StringBuilder(cols);
            }

            for (int i = 0; i < text.Text.Length; i++)
            {
                lines[i % rows].Append(text.Text[i]);
            }

            return JgsValue.CharMatrix([.. lines.Select(static l => l.ToString())]);
        }

        private static JgsValue Strings(int[] dims, Node data, long count)
        {
            if (data is not Arr items)
            {
                throw DataMustBe("string", "a JSON array");
            }

            var values = new JgsValue[items.Items.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = items.Items[i] switch
                {
                    Str s => JgsValue.Str(s.Text),
                    Obj { Members: [("mwdata", Str { Text: "missing" })] } => JgsValue.Str(JgsBuiltins.MissingSentinel),
                    Num => throw Unsupported("invalid conversion: JSON number to JSON object"),
                    // R2025b's decoder ends the process here; JGraph refuses with its text (ADR 0270).
                    _ => throw Unsupported("String array must contain JSON String, or {\"mwdata\":\"missing\"}"),
                };
            }

            if (values.Length != count)
            {
                throw Mismatched();
            }

            JgsValue array = JgsValue.StringArray(values, dims[0], dims.Length == 2 ? dims[1] : values.Length / Math.Max(dims[0], 1));
            if (dims.Length > 2)
            {
                array.ReshapeDims(dims);
            }

            return array;
        }

        private JgsValue Cells(int[] dims, Node data, long count)
        {
            if (data is not Arr items)
            {
                throw DataMustBe("cell", "a JSON array");
            }

            if (items.Items.Count != count)
            {
                throw Mismatched();
            }

            JgsValue[] values = [.. items.Items.Select(item => Value(item, inField: true))];
            JgsValue cell = JgsValue.Cell(values);
            cell.Reshape(dims[0], dims.Length == 2 ? dims[1] : values.Length / Math.Max(dims[0], 1));
            if (dims.Length > 2)
            {
                cell.ReshapeDims(dims);
            }

            return cell;
        }

        private JgsValue Structs(int[] dims, Node data, long count)
        {
            if (data is not Obj fields)
            {
                throw DataMustBe("struct", "a JSON object");
            }

            var names = new List<string>();
            var columns = new List<JgsValue[]>();
            foreach ((string name, Node list) in fields.Members.OrderBy(static m => m.Name, StringComparer.Ordinal))
            {
                if (list is not Arr perElement)
                {
                    throw Unsupported($"invalid conversion: JSON {KindName(list)} to JSON array");
                }

                if (perElement.Items.Count != count)
                {
                    throw Mismatched();
                }

                CheckFieldName(name);
                if (names.Contains(name))
                {
                    continue;
                }

                names.Add(name);
                columns.Add([.. perElement.Items.Select(item => Value(item, inField: true))]);
            }

            if (count == 1 && dims.All(static d => d == 1))
            {
                var fieldsOfOne = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
                for (int f = 0; f < names.Count; f++)
                {
                    fieldsOfOne[names[f]] = columns[f][0];
                }

                return JgsValue.Struct(fieldsOfOne);
            }

            var elements = new Dictionary<string, JgsValue>[count];
            for (int i = 0; i < count; i++)
            {
                var element = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
                for (int f = 0; f < names.Count; f++)
                {
                    element[names[f]] = columns[f][i];
                }

                elements[i] = element;
            }

            JgsValue array = JgsValue.StructArray(new JgsStructArray(elements, [.. names]), dims[0], dims.Length == 2 ? dims[1] : (int)count / Math.Max(dims[0], 1));
            if (dims.Length > 2)
            {
                array.ReshapeDims(dims);
            }

            return array;
        }

        private static string KindName(Node node) => node switch
        {
            Num => "number",
            Str => "string",
            Lit { Text: "null" } => "null",
            Lit => "boolean",
            Obj => "object",
            _ => "array",
        };

        private JgsValue Sparse(string type, int[] dims, Node data, bool complex)
        {
            if (type != "double" && type != "logical")
            {
                // R2025b reads the type's elements first and refuses them as it would in a full array.
                throw type == "char"
                    ? Error("DecodeInvalidCharArrayMWData", "\"mwdata\" for char array must be in the format '[\"...\"]'.")
                    : Array.IndexOf(NumericTypes, type) >= 0
                        ? Error("DecodeInvalidIntegralElem", "Invalid value for type int or uint. Value must be an integral number.")
                        : DataMustBe(type, "a object");
            }

            if (dims.Length > 2)
            {
                throw Unsupported("Sparse Array can only have two dimensions.");
            }

            if (complex)
            {
                throw new Refused("JGraph:mps:json:complexSparse", "JGraph holds no complex sparse array (compiler and SDK plan, ADR 0266); the value cannot be decoded.");
            }

            // R2025b's decoder ends the process on a malformed sparse mwdata; JGraph refuses with the
            // text it printed as it went (ADR 0270).
            if (data is not Arr lists || lists.Items.Any(static l => l is not Arr) || lists.Items.Count is not (2 or 3) || (lists.Items.Count == 2 && type != "logical"))
            {
                throw Unsupported("Sparse array expects a row array, column array, and an optional data array inside \"mwdata\". Unexpected data found");
            }

            Arr rows = (Arr)lists.Items[0];
            Arr cols = (Arr)lists.Items[1];
            Arr? values = lists.Items.Count == 3 ? (Arr)lists.Items[2] : null;
            if (rows.Items.Count != cols.Items.Count || (values is not null && values.Items.Count != rows.Items.Count))
            {
                throw Unsupported("Expected JSON array of JSON arrays to be of appropriate size in \"mwdata\" for a sparse array");
            }

            int Index(Node node, string check, int limit)
            {
                int index = (int)Math.Truncate(Floating(node, "double")); // a fraction is dropped (risky4: 0.5 reads as 0)
                return index < 0 || index >= limit ? throw Unsupported(check) : index;
            }

            var triplets = new List<(int Row, int Col, double Value)>(rows.Items.Count);
            for (int k = 0; k < rows.Items.Count; k++)
            {
                int r = Index(rows.Items[k], "Row Range Check", dims[0]);
                int c = Index(cols.Items[k], "Column Range Check", dims[1]);
                double v = values is null ? 1 : type == "logical" ? (Floating(values.Items[k], "double") != 0 ? 1 : 0) : Floating(values.Items[k], "double");
                triplets.Add((r, c, v));
            }

            return JgsValue.Sparse(CscMatrix.FromTriplets(dims[0], dims[1], triplets));
        }

        private JgsValue Enumeration(string type, int[] dims, Node data)
        {
            if (data is Obj)
            {
                // R2025b's decoder ends the process ("Object creation not supported."); JGraph refuses (ADR 0270).
                throw Unsupported(type);
            }

            if (data is not Arr items || items.Items.Any(static i => i is not Str))
            {
                throw DataMustBe(type, "a object");
            }

            string[] names = [.. items.Items.Select(static i => ((Str)i).Text)];
            if (names.Length == 0)
            {
                throw Unsupported($"{type} is not supported.");
            }

            long count = dims.Aggregate(1L, static (a, b) => a * b);
            if (names.Length != count)
            {
                throw Mismatched();
            }

            if (type == OnOffSwitchStateValue.Name)
            {
                var states = new bool[names.Length];
                for (int i = 0; i < names.Length; i++)
                {
                    states[i] = OnOffSwitchStateValue.Named(names[i]) ?? throw Unsupported("Invalid enumeration");
                }

                return names.Length == 1 ? OnOffSwitchStateValue.Of(states[0]) : OnOffSwitchStateValue.Of(states, dims[0], dims.Length == 2 ? dims[1] : names.Length / Math.Max(dims[0], 1));
            }

            if (findClass(type) is not { IsEnumeration: true } definition)
            {
                throw Unsupported("Invalid enumeration");
            }

            var members = new JgsObject[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                members[i] = definition.TryMember(names[i], out JgsValue member) ? member.AsObject : throw Unsupported("Invalid enumeration");
            }

            return members.Length == 1
                ? JgsValue.Object(members[0])
                : JgsValue.ObjectArray(new JgsObjectArray(definition, members), dims[0], dims.Length == 2 ? dims[1] : names.Length / Math.Max(dims[0], 1));
        }

        private JgsValue Datetime(int[] dims, Node data, Node? metadataNode, long count)
        {
            if (data is not Obj fields)
            {
                throw DataMustBe("datetime", "a object");
            }

            if (metadataNode is not Obj metadata || metadata["Format"] is null)
            {
                throw Unsupported("Unable to load serialized datetime array with missing Format");
            }

            string Text(Node node, string property)
            {
                JgsValue value = Value(node, inField: true);
                return value.Type == JgsType.String ? value.AsString
                    : value.IsCharMatrix && value.CharMatrixRows().Length <= 1 ? (value.CharMatrixRows() is [var row] ? row : string.Empty)
                    : throw Unsupported($"Unable to load serialized datetime array with invalid value for {property}");
            }

            string format = Text(metadata["Format"]!, "Format");
            string zone = metadata["TimeZone"] is { } zoneNode ? Text(zoneNode, "TimeZone") : string.Empty;
            if (zone.Length > 0 && !JgsTime.TryResolveZone(zone, out _))
            {
                throw Unsupported("Unable to load serialized datetime array with invalid value for TimeZone");
            }

            if (fields["TimeStamp"] is not { } stampNode)
            {
                throw Unsupported("Unable to load serialized datetime array with missing TimeStamp");
            }

            JgsValue stamps = Value(stampNode, inField: true);
            if (stamps.IsStringArray || stamps.IsCharMatrix || stamps.Type is not (JgsType.Number or JgsType.Array) || JgsBuiltins.IsLogicalValue(stamps))
            {
                throw Unsupported("Unable to load serialized datetime array with invalid value for TimeStamp");
            }

            double[] ms = stamps.Type == JgsType.Number ? [stamps.AsNumber] : [.. stamps.BoxedElements().Select(static e => e.AsNumber)];
            if (ms.Length != count)
            {
                throw Mismatched();
            }

            double[] stored = [.. ms.Select(m => m + UnixEpochMs)];
            var tag = new JgsTimeTag(JgsTimeKind.Datetime, format, zone.Length == 0 ? null : zone, FormatSet: true);
            JgsValue result = JgsMatrix.FromColumnMajorDims(stored, dims);
            return result.MarkTime(tag);
        }
    }

    // --- reading text -----------------------------------------------------------------------------

    /// <summary>
    /// R2025b's JSON reader behind <c>mps.json.decode</c> (x_*): no whitespace before or after the
    /// document (a server's reader allows it), the usual whitespace inside it, and syntax errors
    /// reported at a line, a zero-based column and a zero-based character with the line and a
    /// caret under the place.
    /// </summary>
    private sealed class Parser(string text, bool lenientWhitespace = false)
    {
        private int _at;

        public Node ParseDocument()
        {
            if (lenientWhitespace)
            {
                SkipBlanks();
            }

            if (_at >= text.Length)
            {
                throw Incomplete();
            }

            Node node = ParseValue();
            if (lenientWhitespace)
            {
                SkipBlanks();
            }

            if (_at < text.Length)
            {
                throw Unexpected(_at);
            }

            return node;
        }

        private Node ParseValue()
        {
            if (_at >= text.Length)
            {
                throw Incomplete();
            }

            char c = text[_at];
            switch (c)
            {
                case '{':
                    return ParseObject();
                case '[':
                    return ParseArray();
                case '"':
                    return new Str(ParseString());
                case 't':
                    return Literal("true");
                case 'f':
                    return Literal("false");
                case 'n':
                    return Literal("null");
            }

            if (c == '-' || char.IsAsciiDigit(c))
            {
                return ParseNumber();
            }

            throw Unexpected(_at);
        }

        private Node Literal(string word)
        {
            if (string.CompareOrdinal(text, _at, word, 0, word.Length) == 0)
            {
                _at += word.Length;
                return new Lit(word);
            }

            throw Unexpected(_at);
        }

        private Node ParseNumber()
        {
            int start = _at;
            if (text[_at] == '-')
            {
                _at++;
            }

            int digits = _at;
            while (_at < text.Length && char.IsAsciiDigit(text[_at]))
            {
                _at++;
            }

            if (_at == digits)
            {
                throw _at >= text.Length ? Incomplete() : Unexpected(_at);
            }

            if (_at < text.Length && text[_at] == '.')
            {
                _at++;
                if (_at >= text.Length)
                {
                    throw Incomplete();
                }

                if (!char.IsAsciiDigit(text[_at]))
                {
                    throw At("DecodeNumberInvalidFractionalComponent", _at, "Fractional component after decimal is invalid");
                }

                while (_at < text.Length && char.IsAsciiDigit(text[_at]))
                {
                    _at++;
                }
            }

            if (_at < text.Length && text[_at] is 'e' or 'E')
            {
                _at++;
                if (_at < text.Length && text[_at] is '+' or '-')
                {
                    _at++;
                }

                if (_at >= text.Length)
                {
                    throw Incomplete();
                }

                if (!char.IsAsciiDigit(text[_at]))
                {
                    // Not measured in R2025b (the fixture has no such case); the identifier is its catalog's.
                    throw At("DecodeNumberInvalidExponent", _at, "Exponent component is invalid");
                }

                while (_at < text.Length && char.IsAsciiDigit(text[_at]))
                {
                    _at++;
                }
            }

            return new Num(text[start.._at]);
        }

        private string ParseString()
        {
            int opening = _at;
            _at++;
            var value = new StringBuilder();
            while (true)
            {
                if (_at >= text.Length)
                {
                    throw Incomplete();
                }

                char c = text[_at++];
                if (c == '"')
                {
                    return value.ToString();
                }

                if (c != '\\')
                {
                    value.Append(c);
                    continue;
                }

                // An escape's fault is reported at the string's first character (x_str_escape_bad).
                int inside = opening + 1;
                if (_at >= text.Length)
                {
                    throw Incomplete();
                }

                char escape = text[_at++];
                switch (escape)
                {
                    case '"': value.Append('"'); break;
                    case '\\': value.Append('\\'); break;
                    case '/': value.Append('/'); break;
                    case 'b': value.Append('\b'); break;
                    case 'f': value.Append('\f'); break;
                    case 'n': value.Append('\n'); break;
                    case 'r': value.Append('\r'); break;
                    case 't': value.Append('\t'); break;
                    case 'u':
                        value.Append(UnicodeEscape(inside));
                        break;
                    default:
                        throw At("DecodeCharInvalidEscape", inside, "Escape character is invalid");
                }
            }
        }

        private char UnicodeEscape(int inside)
        {
            if (_at + 4 > text.Length || text.AsSpan(_at, 4).ContainsAny('"', '\\'))
            {
                throw IncompleteAt(inside); // "\u12" ends the text before four digits (x_str_unicode_bad)
            }

            if (!int.TryParse(text.AsSpan(_at, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int unit))
            {
                throw At("DecodeCharInvalidUnicodeEscape", inside, "Unicode escape sequence is invalid");
            }

            _at += 4;
            if (char.IsHighSurrogate((char)unit) && (_at + 6 > text.Length || text[_at] != '\\' || text[_at + 1] != 'u'))
            {
                throw IncompleteAt(inside); // a lone high surrogate wants its pair (x_str_lone_surrogate)
            }

            return (char)unit;
        }

        private Node ParseArray()
        {
            _at++;
            var items = new List<Node>();
            SkipBlanks();
            if (_at < text.Length && text[_at] == ']')
            {
                _at++;
                return new Arr(items);
            }

            while (true)
            {
                SkipBlanks();
                if (_at >= text.Length)
                {
                    throw Incomplete();
                }

                if (text[_at] is ',' or ']')
                {
                    throw Unexpected(_at); // [,1] and [1,]
                }

                items.Add(ParseValue());
                SkipBlanks();
                if (_at >= text.Length)
                {
                    throw Incomplete();
                }

                char c = text[_at];
                if (c == ']')
                {
                    _at++;
                    return new Arr(items);
                }

                if (c != ',')
                {
                    throw Unexpected(_at);
                }

                _at++;
            }
        }

        private Node ParseObject()
        {
            _at++;
            var members = new List<(string, Node)>();
            SkipBlanks();
            if (_at < text.Length && text[_at] == '}')
            {
                _at++;
                return new Obj(members);
            }

            while (true)
            {
                SkipBlanks();
                if (_at >= text.Length)
                {
                    throw Incomplete();
                }

                if (text[_at] != '"')
                {
                    throw Unexpected(_at);
                }

                string name = ParseString();
                SkipBlanks();
                if (_at >= text.Length)
                {
                    throw Incomplete();
                }

                if (text[_at] != ':')
                {
                    throw At("DecodeObjectNoColonAfterName", _at, "Name in JSON object must be followed by ':'");
                }

                _at++;
                SkipBlanks();
                if (_at >= text.Length)
                {
                    throw Incomplete();
                }

                if (text[_at] is '}' or ',')
                {
                    throw Unexpected(_at);
                }

                members.Add((name, ParseValue()));
                SkipBlanks();
                if (_at >= text.Length)
                {
                    throw Incomplete();
                }

                char c = text[_at];
                if (c == '}')
                {
                    _at++;
                    return new Obj(members);
                }

                if (c != ',')
                {
                    throw Unexpected(_at);
                }

                _at++;
            }
        }

        private void SkipBlanks()
        {
            while (_at < text.Length && text[_at] is ' ' or '\t' or '\n' or '\r')
            {
                _at++;
            }
        }

        private Refused Incomplete() => IncompleteAt(_at);

        private Refused IncompleteAt(int offset) => At("DecodeIncomplete", offset, "Unexpected end of text");

        private Refused Unexpected(int offset) => At("DecodeUnexpectedValue", offset, "Unexpected value");

        /// <summary>R2025b's syntax error: the line (from 1), the column and character (from 0), then the line's text and a caret under the column.</summary>
        private Refused At(string mnemonic, int offset, string what)
        {
            int line = 1;
            int lineStart = 0;
            for (int i = 0; i < offset && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    lineStart = i + 1;
                }
            }

            int lineEnd = text.IndexOf('\n', lineStart);
            string lineText = lineEnd < 0 ? text[lineStart..] : text[lineStart..lineEnd];
            int column = offset - lineStart;
            return Error(mnemonic,
                $"JSON syntax error at line {line}, column {column} (character {offset}): {what}.\n\n {lineText}\n {new string(' ', column)}^");
        }
    }
}
