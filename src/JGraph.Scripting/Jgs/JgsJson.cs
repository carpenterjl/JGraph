using System.Globalization;
using System.Text;
using JGraph.Data;
using JGraph.Scripting.Jgs.Devices;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// MATLAB's JSON, as R2025b writes and reads it (app-building plan, U9b; probes <c>u9b_json</c>,
/// <c>u9b_num</c>, <c>u9b_bridge</c>, <c>u9b_sends</c>). Three writers and two readers share one
/// tree of nodes:
/// <list type="bullet">
/// <item><see cref="Encode"/> is <c>jsonencode</c>, and what a <c>uihtml</c>'s <c>Data</c> goes to a
/// page as.</item>
/// <item><see cref="EncodeEvent"/> is what <c>sendEventToHTMLSource</c> sends, a different encoder in
/// R2025b: <c>NaN</c> is the text <c>"NaN"</c>, a string scalar is an array of one, a datetime is
/// its month, day and year, an on/off state is a logical.</item>
/// <item><see cref="Decode"/> is <c>jsondecode</c>, and what a page's <c>Data</c> comes back as;
/// <see cref="DecodeEventData"/> is what a page's <c>sendEventToMATLAB</c> data come back as —
/// <c>jsondecode</c>'s answer with its vectors laid as rows.</item>
/// </list>
/// </summary>
internal static class JgsJson
{
    // --- the tree -------------------------------------------------------------------------------

    private abstract record Node;

    private sealed record Num(string Text) : Node;

    private sealed record Str(string Text) : Node;

    private sealed record Lit(string Text) : Node; // true, false, null

    private sealed record Arr(List<Node> Items) : Node;

    private sealed record Obj(List<(string Name, Node Value)> Members) : Node;

    private static readonly Lit Null = new("null");

    /// <summary>The two options <c>jsonencode</c> takes; <see cref="Default"/> is R2025b's.</summary>
    internal readonly record struct Options(bool ConvertInfAndNaN, bool PrettyPrint)
    {
        public static Options Default => new(ConvertInfAndNaN: true, PrettyPrint: false);
    }

    /// <summary>A value that cannot be written, with R2025b's identifier and message.</summary>
    internal sealed class Unwritable(string identifier, string message) : Exception(message)
    {
        public string Identifier { get; } = identifier;
    }

    // --- jsonencode -----------------------------------------------------------------------------

    /// <summary><c>jsonencode(value, ...)</c>: the text, or <see cref="Unwritable"/>.</summary>
    public static string Encode(JgsValue value, Options options)
    {
        Node node = new Writer(options, events: false).Write(value);
        var text = new StringBuilder();
        Emit(text, node, options.PrettyPrint, 0);
        return text.ToString();
    }

    /// <summary>What <c>sendEventToHTMLSource</c> sends for <paramref name="value"/>, or <see cref="Unwritable"/>.</summary>
    public static string EncodeEvent(JgsValue value)
    {
        Node node = new Writer(Options.Default, events: true).Write(value);
        var text = new StringBuilder();
        Emit(text, node, pretty: false, 0);
        return text.ToString();
    }

    private sealed class Writer(Options options, bool events)
    {
        public Node Write(JgsValue value)
        {
            switch (value.Type)
            {
                case JgsType.Function:
                    if (events)
                    {
                        // A named function is its name, an anonymous one its text (probe u9b_more).
                        string text = JgsBuiltins.SourceTextOf("sendEventToHTMLSource", value, 0, 0);
                        return new Str(value.AsCallable is AnonymousFunction ? text : text.TrimStart('@'));
                    }

                    throw Unsupported("function_handle");

                case JgsType.Sparse:
                    throw new Unwritable("MATLAB:json:UnsupportedSparseDataType",
                        "Unable to encode sparse objects of class double as JSON-formatted text.");

                case JgsType.Complex:
                    throw Complex(value);

                case JgsType.Table:
                    return TableRows(value);

                case JgsType.External:
                    if (value.AsExternalOrNull() is DeviceEnumValue member)
                    {
                        return member.ClassName == DeviceEnumValue.OnOffSwitchState && events
                            ? new Lit(member.Choice == "on" ? "true" : "false")
                            : new Str(member.Choice);
                    }

                    throw Unsupported(value.AsExternal.ClassName);

                case JgsType.Object:
                    return ObjectMembers(value);

                case JgsType.Image:
                    throw Unsupported("image");

                case JgsType.Null:
                    return new Arr([]);
            }

            if (value.IsTime)
            {
                return events ? TimeParts(value) : TimeTexts(value);
            }

            if (value.IsStringArray)
            {
                return Strings(value);
            }

            if (value.IsCharMatrix)
            {
                return CharRows(value);
            }

            if (value.Type == JgsType.String)
            {
                return new Str(value.AsString);
            }

            if (value.Type == JgsType.Cell)
            {
                return new Arr([.. value.AsCell.Select(Write)]);
            }

            if (value.Type == JgsType.Struct)
            {
                return Structs(value);
            }

            return Numbers(value);
        }

        private static Unwritable Unsupported(string className) =>
            new("MATLAB:json:UnsupportedDataType", $"Unable to encode objects of class {className} as JSON-formatted text.");

        private static Unwritable Complex(JgsValue value) =>
            new("MATLAB:json:UnsupportedComplexDataType",
                $"Unable to encode complex-valued objects of class {value.NumericClass.MatlabName()} as JSON-formatted text.");

        // A number array: a scalar is a number, an empty is [], a vector is flat whichever way it
        // lies, and anything else nests by its dimensions, the first outermost.
        private Node Numbers(JgsValue value)
        {
            if (value.IsPackedComplex)
            {
                throw Complex(value);
            }

            bool logical = JgsBuiltins.IsLogicalValue(value);
            bool single = value.NumericClass == JgsNumericClass.Single;
            bool integer = !logical && value.NumericClass is not (JgsNumericClass.Double or JgsNumericClass.Single);
            Node Element(JgsValue e)
            {
                if (e.Type == JgsType.Complex)
                {
                    throw Complex(value);
                }

                double x = e.AsNumber;
                if (logical || e.Type == JgsType.Bool)
                {
                    return new Lit(x != 0 ? "true" : "false");
                }

                return Number(x, single, integer);
            }

            if (value.Type is JgsType.Number or JgsType.Bool)
            {
                return Element(value);
            }

            return Nest(value.Dims, value.ArrayLength, i => Element(value.ElementAt(i)));
        }

        private Node Number(double x, bool single, bool integer)
        {
            if (double.IsNaN(x) || double.IsInfinity(x))
            {
                // The event encoder writes a double's NaN and Inf as text and a single's as null
                // (probes u9b_sends, u9b_more).
                if (events)
                {
                    return single ? Null : new Str(double.IsNaN(x) ? "NaN" : x > 0 ? "Inf" : "-Inf");
                }

                if (options.ConvertInfAndNaN)
                {
                    return Null;
                }

                return new Num(double.IsNaN(x) ? "NaN" : x > 0 ? "Infinity" : "-Infinity");
            }

            if (integer)
            {
                return new Num(x.ToString("F0", CultureInfo.InvariantCulture));
            }

            return new Num(single ? FormatSingle((float)x) : FormatDouble(x));
        }

        private Node Strings(JgsValue value)
        {
            JgsValue[] items = value.BoxedElements();
            Node Element(int i)
            {
                string text = items[i].AsString;
                return JgsBuiltins.IsMissingText(text) ? events ? new Str(string.Empty) : Null : new Str(text);
            }

            // The event encoder writes a string scalar as an array of one (probe u9b_sends).
            if (value.ArrayLength == 1)
            {
                return events ? new Arr([Element(0)]) : Element(0);
            }

            return Nest(value.Dims, value.ArrayLength, Element);
        }

        private static Node CharRows(JgsValue value)
        {
            string[] rows = value.CharMatrixRows();
            if (rows.Length == 0 || rows[0].Length == 0)
            {
                return new Str(string.Empty);
            }

            return rows.Length == 1 ? new Str(rows[0]) : new Arr([.. rows.Select(static r => (Node)new Str(r))]);
        }

        private Node Structs(JgsValue value)
        {
            if (!value.IsStructArray)
            {
                if (JgsBuiltins.IsKeyedCollection(value))
                {
                    return Keyed(value);
                }

                return Members(value.AsStruct);
            }

            Dictionary<string, JgsValue>[] elements = value.AsStructArray.Elements;
            if (elements.Length == 0)
            {
                return new Arr([]);
            }

            return Nest(value.Dims, elements.Length, i => Members(elements[i]), keepScalar: false);
        }

        private Node Members(IEnumerable<KeyValuePair<string, JgsValue>> fields) =>
            new Obj([.. fields.Select(f => (f.Key, Write(f.Value)))]);

        private Node Keyed(JgsValue map)
        {
            if (events)
            {
                return new Arr([]); // R2025b sends a containers.Map as [] (probe u9b_sends)
            }

            Dictionary<string, JgsValue> fields = map.AsStruct;
            if (map.ClassName != JgsBuiltins.MapClassName || fields["KeyType"].AsString != "char")
            {
                throw new Unwritable("MATLAB:json:UnsupportedKeyType",
                    $"Unable to encode objects of class {map.ClassName} as JSON-formatted text. KeyType must be char.");
            }

            JgsValue[] keys = fields["keys"].AsCell;
            JgsValue[] values = fields["values"].AsCell;
            return new Obj([.. keys.Select((k, i) => (k.AsString, Write(values[i])))]);
        }

        private Node ObjectMembers(JgsValue value)
        {
            JgsObject instance = value.AsObject;
            return new Obj([.. instance.Class.ListedProperties
                .Where(p => instance.Fields.ContainsKey(p.Spec.Name))
                .Select(p => (p.Spec.Name, Write(instance.Fields[p.Spec.Name])))]);
        }

        private Node TableRows(JgsValue value)
        {
            Table table = value.AsTable;
            var variables = table.Columns.Select(c => (c.Name, Value: JgsBuiltins.TableColumnValue(table, c.Name, 0, 0))).ToList();
            var rows = new List<Node>(table.RowCount);
            for (int r = 0; r < table.RowCount; r++)
            {
                var members = new List<(string, Node)>(variables.Count);
                foreach ((string name, JgsValue column) in variables)
                {
                    members.Add((name, Write(RowOf(column, r, table.RowCount))));
                }

                rows.Add(new Obj(members));
            }

            return new Arr(rows);
        }

        private static JgsValue RowOf(JgsValue column, int row, int rowCount)
        {
            if (column.Type == JgsType.Cell)
            {
                return column.AsCell[row];
            }

            if (column.Type is JgsType.Number or JgsType.Bool or JgsType.String)
            {
                return column;
            }

            int width = rowCount == 0 ? 0 : column.ArrayLength / rowCount;
            if (width <= 1)
            {
                return column.ElementAt(row);
            }

            JgsValue[] across = new JgsValue[width];
            for (int c = 0; c < width; c++)
            {
                across[c] = column.ElementAt(row + c * rowCount);
            }

            return JgsMatrix.FromElements(across, 1, width);
        }

        private static Node TimeTexts(JgsValue value)
        {
            Node Element(int i)
            {
                string text = JgsBuiltins.TimeText(value, i);
                return value.IsDatetime && double.IsNaN(value.ElementAt(i).AsNumber) ? Null : new Str(text);
            }

            return value.ArrayLength == 1 ? Element(0) : Nest(value.Dims, value.ArrayLength, Element);
        }

        // R2025b's event encoder writes a datetime as its parts, a scalar's as numbers and an
        // array's as arrays; NaT is the text "NaT", and a duration is [] (probe u9b_sends).
        private Node TimeParts(JgsValue value)
        {
            if (!value.IsDatetime)
            {
                return new Arr([]);
            }

            if (value.ArrayLength == 1 && double.IsNaN(value.ElementAt(0).AsNumber))
            {
                return new Str("NaT");
            }

            var months = new List<Node>();
            var days = new List<Node>();
            var years = new List<Node>();
            for (int i = 0; i < value.ArrayLength; i++)
            {
                double ms = value.ElementAt(i).AsNumber;
                if (double.IsNaN(ms))
                {
                    months.Add(Null);
                    days.Add(Null);
                    years.Add(Null);
                    continue;
                }

                DateTime wall = JgsTime.WallClock(ms, value.TimeTag);
                months.Add(new Num(wall.Month.ToString(CultureInfo.InvariantCulture)));
                days.Add(new Num(wall.Day.ToString(CultureInfo.InvariantCulture)));
                years.Add(new Num(wall.Year.ToString(CultureInfo.InvariantCulture)));
            }

            Node Part(List<Node> parts) => parts.Count == 1 ? parts[0] : new Arr(parts);
            return new Obj([("Month", Part(months)), ("Day", Part(days)), ("Year", Part(years))]);
        }

        /// <summary>
        /// An array laid out as R2025b nests it: empty is [], a vector (one dimension longer than
        /// one) is flat, and anything else nests by every dimension, the first outermost.
        /// </summary>
        private static Node Nest(int[] dims, int count, Func<int, Node> element, bool keepScalar = true)
        {
            if (count == 0)
            {
                return new Arr([]);
            }

            if (count == 1 && keepScalar)
            {
                return element(0);
            }

            if (dims.Count(static d => d != 1) <= 1)
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
                    items.Add(Level(d + 1, offset + i * strides[d]));
                }

                return new Arr(items);
            }

            return Level(0, 0);
        }
    }

    // --- numbers ----------------------------------------------------------------------------------

    /// <summary>
    /// A double as R2025b's <c>jsonencode</c> writes it (probe <c>u9b_num</c>): the digits of
    /// <c>%.15g</c>, or of <c>%.17g</c> when those do not read back as the same number; a magnitude
    /// from a million up as <c>d.dddE+n</c> with at least one decimal (<c>1.0E+6</c>); one under
    /// 1e-4 as <c>dE-n</c> (<c>1E-5</c>); anything between in plain decimals.
    /// </summary>
    internal static string FormatDouble(double x)
    {
        string digits = x.ToString("E14", CultureInfo.InvariantCulture);
        if (double.Parse(digits, CultureInfo.InvariantCulture) != x)
        {
            digits = x.ToString("E16", CultureInfo.InvariantCulture);
        }

        return Layout(x, digits);
    }

    /// <summary>A single, the same way with <c>%.6g</c> and <c>%.9g</c>.</summary>
    internal static string FormatSingle(float x)
    {
        string digits = x.ToString("E5", CultureInfo.InvariantCulture);
        if (float.Parse(digits, CultureInfo.InvariantCulture) != x)
        {
            digits = x.ToString("E8", CultureInfo.InvariantCulture);
        }

        return Layout(x, digits);
    }

    private static string Layout(double x, string scientific)
    {
        if (x == 0)
        {
            return double.IsNegative(x) ? "-0" : "0";
        }

        bool negative = scientific[0] == '-';
        int e = scientific.IndexOf('E');
        string mantissa = scientific[(negative ? 1 : 0)..e].Replace(".", string.Empty, StringComparison.Ordinal).TrimEnd('0');
        int exponent = int.Parse(scientific[(e + 1)..], CultureInfo.InvariantCulture);
        string sign = negative ? "-" : string.Empty;
        string rest = mantissa[1..];
        double magnitude = Math.Abs(x);
        if (magnitude >= 1e6)
        {
            return $"{sign}{mantissa[0]}.{(rest.Length > 0 ? rest : "0")}E+{exponent}";
        }

        if (exponent < -4)
        {
            return $"{sign}{mantissa[0]}{(rest.Length > 0 ? "." + rest : string.Empty)}E-{-exponent}";
        }

        if (exponent < 0)
        {
            return $"{sign}0.{new string('0', -exponent - 1)}{mantissa}";
        }

        if (mantissa.Length <= exponent + 1)
        {
            return sign + mantissa + new string('0', exponent + 1 - mantissa.Length);
        }

        return $"{sign}{mantissa[..(exponent + 1)]}.{mantissa[(exponent + 1)..]}";
    }

    // --- writing text ---------------------------------------------------------------------------

    private static void Emit(StringBuilder text, Node node, bool pretty, int indent)
    {
        switch (node)
        {
            case Num n:
                text.Append(n.Text);
                return;
            case Lit l:
                text.Append(l.Text);
                return;
            case Str s:
                Quote(text, s.Text);
                return;
            case Arr { Items.Count: 0 }:
                text.Append("[]");
                return;
            case Obj { Members.Count: 0 }:
                text.Append("{}");
                return;
            case Arr a:
                text.Append('[');
                for (int i = 0; i < a.Items.Count; i++)
                {
                    text.Append(i > 0 ? "," : string.Empty);
                    Break(text, pretty, indent + 2);
                    Emit(text, a.Items[i], pretty, indent + 2);
                }

                Break(text, pretty, indent);
                text.Append(']');
                return;
            case Obj o:
                text.Append('{');
                for (int i = 0; i < o.Members.Count; i++)
                {
                    text.Append(i > 0 ? "," : string.Empty);
                    Break(text, pretty, indent + 2);
                    Quote(text, o.Members[i].Name);
                    text.Append(pretty ? ": " : ":");
                    Emit(text, o.Members[i].Value, pretty, indent + 2);
                }

                Break(text, pretty, indent);
                text.Append('}');
                return;
        }
    }

    private static void Break(StringBuilder text, bool pretty, int indent)
    {
        if (pretty)
        {
            text.Append('\n').Append(' ', indent);
        }
    }

    private static void Quote(StringBuilder text, string value)
    {
        text.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': text.Append("\\\""); break;
                case '\\': text.Append("\\\\"); break;
                case '\n': text.Append("\\n"); break;
                case '\r': text.Append("\\r"); break;
                case '\t': text.Append("\\t"); break;
                case '\b': text.Append("\\b"); break;
                case '\f': text.Append("\\f"); break;
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

    // --- reading text -----------------------------------------------------------------------------

    /// <summary>A JSON syntax error, with R2025b's identifier and message.</summary>
    internal sealed class Malformed(string identifier, string message) : Exception(message)
    {
        public string Identifier { get; } = identifier;
    }

    /// <summary><c>jsondecode(text)</c>, or <see cref="Malformed"/>.</summary>
    public static JgsValue Decode(string text) => Value(new Parser(text).ParseDocument());

    /// <summary>
    /// The data of a page's <c>sendEventToMATLAB</c> (probe <c>u9b_bridge</c>): <c>jsondecode</c>'s
    /// answer with each vector laid as a row — a cell's, but not inside a cell; a struct array keeps
    /// its shape and its fields are laid out the same way.
    /// </summary>
    public static JgsValue DecodeEventData(string text) => Rows(Decode(text));

    private static JgsValue Rows(JgsValue value)
    {
        if (value.Type == JgsType.Struct)
        {
            if (!value.IsStructArray)
            {
                Dictionary<string, JgsValue> fields = value.AsStruct;
                return JgsValue.Struct(fields.ToDictionary(static f => f.Key, static f => Rows(f.Value), StringComparer.Ordinal));
            }

            Dictionary<string, JgsValue>[] elements = value.AsStructArray.Elements;
            var laid = elements.Select(e => e.ToDictionary(static f => f.Key, static f => Rows(f.Value), StringComparer.Ordinal)).ToArray();
            return JgsValue.StructArray(new JgsStructArray(laid), value.Rows, value.Cols);
        }

        if (value.Type is JgsType.Array or JgsType.Cell && !value.IsNd && value.Cols == 1 && value.Rows > 1)
        {
            JgsValue row = value.Type == JgsType.Cell ? JgsValue.Cell([.. value.AsCell]) : JgsMatrix.FromElements(value.BoxedElements(), 1, value.Rows);
            if (row.Type == JgsType.Cell)
            {
                row.Reshape(1, value.Rows);
            }

            return row;
        }

        return value;
    }

    private sealed class Parser(string text)
    {
        private int _at;

        public Node ParseDocument()
        {
            SkipBlanks();
            if (_at >= text.Length)
            {
                throw new Malformed("MATLAB:json:ExpectedValueAtEnd", "JSON syntax error: expected value but found end of text.");
            }

            Node node = ParseValue();
            SkipBlanks();
            if (_at < text.Length)
            {
                throw At("MATLAB:json:ExtraText", _at, "extra text.");
            }

            return node;
        }

        private Node ParseValue()
        {
            SkipBlanks();
            if (_at >= text.Length)
            {
                throw new Malformed("MATLAB:json:ExpectedValueAtEnd", "JSON syntax error: expected value but found end of text.");
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
                    return Literal("true", new Lit("true"));
                case 'f':
                    return Literal("false", new Lit("false"));
                case 'n':
                    return Literal("null", Null);
                case 'N':
                    return Literal("NaN", new Num("NaN"));
                case 'I':
                    return Literal("Infinity", new Num("Infinity"));
            }

            if (c == '-' && _at + 1 < text.Length && text[_at + 1] == 'I')
            {
                _at++;
                Literal("Infinity", Null);
                return new Num("-Infinity");
            }

            if (c == '-' || char.IsAsciiDigit(c))
            {
                return ParseNumber();
            }

            throw At("MATLAB:json:ExpectedValue", _at, $"expected value but found '{c}'.");
        }

        private Node Literal(string word, Node node)
        {
            int start = _at;
            if (string.CompareOrdinal(text, _at, word, 0, word.Length) == 0)
            {
                _at += word.Length;
                return node;
            }

            int end = start;
            while (end < text.Length && char.IsAsciiLetter(text[end]))
            {
                end++;
            }

            throw At("MATLAB:json:ExpectedLiteral", start, $"expected '{word}' but found '{text[start..end]}'.");
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
                if (_at >= text.Length)
                {
                    throw new Malformed("MATLAB:json:ExpectedNumberAtEnd", "JSON syntax error: expected number but found end of text.");
                }

                throw At("MATLAB:json:ExpectedNumber", _at, $"expected number but found '{text[_at]}'.");
            }

            if (_at < text.Length && text[_at] == '.')
            {
                _at++;
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

                while (_at < text.Length && char.IsAsciiDigit(text[_at]))
                {
                    _at++;
                }
            }

            return new Num(text[start.._at]);
        }

        private string ParseString()
        {
            _at++; // the opening quote
            var value = new StringBuilder();
            while (true)
            {
                if (_at >= text.Length)
                {
                    throw new Malformed("MATLAB:json:ExpectedQuoteAtEnd", "JSON syntax error: expected '\"' but found end of text.");
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

                if (_at >= text.Length)
                {
                    throw new Malformed("MATLAB:json:ExpectedQuoteAtEnd", "JSON syntax error: expected '\"' but found end of text.");
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
                        if (_at + 4 > text.Length
                            || !int.TryParse(text.AsSpan(_at, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int unit))
                        {
                            throw At("MATLAB:json:InvalidEscape", _at - 2, "invalid escape sequence.");
                        }

                        value.Append((char)unit);
                        _at += 4;
                        break;
                    default:
                        throw At("MATLAB:json:InvalidEscape", _at - 2, "invalid escape sequence.");
                }
            }
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
                items.Add(ParseValue());
                SkipBlanks();
                if (_at >= text.Length)
                {
                    throw new Malformed("MATLAB:json:ExpectedCommaOrEndAtEnd", "JSON syntax error: expected ',' or ']' but found end of text.");
                }

                char c = text[_at];
                if (c == ']')
                {
                    _at++;
                    return new Arr(items);
                }

                if (c != ',')
                {
                    throw At("MATLAB:json:ExpectedCommaOrEnd", _at, $"expected ',' or ']' but found '{c}'.");
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

            // The first name may be the end instead, and R2025b says so (probe u9b_json).
            bool first = true;
            while (true)
            {
                SkipBlanks();
                string orEnd = first ? " or '}'" : string.Empty;
                string atEnd = first ? "MATLAB:json:ExpectedNameOrEndAtEnd" : "MATLAB:json:ExpectedNameAtEnd";
                if (_at >= text.Length)
                {
                    throw new Malformed(atEnd, $"JSON syntax error: expected quoted name{orEnd} but found end of text.");
                }

                if (text[_at] != '"')
                {
                    throw At(first ? "MATLAB:json:ExpectedNameOrEnd" : "MATLAB:json:ExpectedName", _at,
                        $"expected quoted name{orEnd} but found '{text[_at]}'.");
                }

                first = false;

                string name = ParseString();
                SkipBlanks();
                if (_at >= text.Length)
                {
                    throw new Malformed("MATLAB:json:ExpectedColonAtEnd", "JSON syntax error: expected ':' but found end of text.");
                }

                if (text[_at] != ':')
                {
                    throw At("MATLAB:json:ExpectedColon", _at, $"expected ':' but found '{text[_at]}'.");
                }

                _at++;
                members.Add((name, ParseValue()));
                SkipBlanks();
                if (_at >= text.Length)
                {
                    throw new Malformed("MATLAB:json:ExpectedCommaOrEndAtEnd", "JSON syntax error: expected ',' or '}' but found end of text.");
                }

                char c = text[_at];
                if (c == '}')
                {
                    _at++;
                    return new Obj(members);
                }

                if (c != ',')
                {
                    throw At("MATLAB:json:ExpectedCommaOrEnd", _at, $"expected ',' or '}}' but found '{c}'.");
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

        private Malformed At(string identifier, int offset, string what)
        {
            int line = 1;
            int column = 1;
            for (int i = 0; i < offset && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    column = 1;
                }
                else
                {
                    column++;
                }
            }

            return new Malformed(identifier,
                $"JSON syntax error at line {line}, column {column} (character {offset + 1}): {what}");
        }
    }

    // --- nodes to MATLAB values (jsondecode's rules, probe u9b_json) --------------------------------

    private static JgsValue Value(Node node) => node switch
    {
        Num n => JgsValue.Number(NumberOf(n.Text)),
        Lit { Text: "true" } => JgsValue.Bool(true),
        Lit { Text: "false" } => JgsValue.Bool(false),
        Lit => Empty(),
        Str s => JgsValue.Str(s.Text),
        Obj o => JgsValue.Struct(Fields(o)),
        Arr a => ArrayValue(a.Items),
        _ => Empty(),
    };

    private static double NumberOf(string text) => text switch
    {
        "NaN" => double.NaN,
        "Infinity" => double.PositiveInfinity,
        "-Infinity" => double.NegativeInfinity,
        _ => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
    };

    private static JgsValue Empty() => JgsMatrix.FromColumnMajor([], 0, 0);

    private static JgsValue ArrayValue(List<Node> items)
    {
        if (items.Count == 0)
        {
            return Empty();
        }

        // Numbers and nulls are a column of doubles, a null a NaN; booleans a logical column;
        // strings a column of char rows; objects with the same names in the same order a struct
        // column.
        if (items.All(static i => i is Num or Lit { Text: "null" }))
        {
            double[] numbers = [.. items.Select(static i => i is Num n ? NumberOf(n.Text) : double.NaN)];
            return numbers.Length == 1 ? JgsValue.Number(numbers[0]) : JgsMatrix.FromColumnMajor(numbers, numbers.Length, 1);
        }

        if (items.All(static i => i is Lit { Text: "true" or "false" }))
        {
            JgsValue[] flags = [.. items.Select(static i => JgsValue.Bool(((Lit)i).Text == "true"))];
            return flags.Length == 1 ? flags[0] : JgsMatrix.FromElements(flags, flags.Length, 1);
        }

        if (items.All(static i => i is Str))
        {
            JgsValue texts = JgsValue.Cell([.. items.Select(static i => JgsValue.Str(((Str)i).Text))]);
            texts.Reshape(items.Count, 1);
            return texts;
        }

        if (items.All(static i => i is Obj) && SameNames(items.Cast<Obj>().ToList()))
        {
            Dictionary<string, JgsValue>[] elements = [.. items.Select(static i => Fields((Obj)i))];
            return JgsValue.StructArray(new JgsStructArray(elements), elements.Length, 1);
        }

        // Anything else: each element decoded on its own, then stacked along a new first dimension
        // if all are numbers (or all logicals, or all struct arrays with the same names) of one size,
        // else a column cell.
        JgsValue[] children = [.. items.Select(Value)];
        if (Stack(children) is { } stacked)
        {
            return stacked;
        }

        JgsValue cell = JgsValue.Cell(children);
        cell.Reshape(children.Length, 1);
        return cell;
    }

    private static bool SameNames(List<Obj> objects)
    {
        List<string> first = [.. objects[0].Members.Select(static m => m.Name)];
        return objects.All(o => o.Members.Select(static m => m.Name).SequenceEqual(first));
    }

    private enum Kind
    {
        Other,
        Number,
        Logical,
        Structs,
    }

    private static Kind KindOf(JgsValue value)
    {
        if (value.Type == JgsType.Struct)
        {
            return Kind.Structs;
        }

        if (value.Type == JgsType.Number)
        {
            return Kind.Number;
        }

        if (value.Type is JgsType.Number or JgsType.Array && !value.IsStringArray && !value.IsCharMatrix && value.ArrayLength > 0)
        {
            return JgsBuiltins.IsLogicalValue(value) ? Kind.Logical : Kind.Number;
        }

        return value.Type == JgsType.Bool ? Kind.Logical : Kind.Other;
    }

    /// <summary>The dimensions a decoded child stacks by: a column vector counts as one dimension.</summary>
    private static int[] StackDims(JgsValue value)
    {
        int[] dims = value.Type is JgsType.Number or JgsType.Bool ? [1, 1] : value.Dims;
        if (dims.Length == 2 && dims[1] == 1)
        {
            return dims[0] == 1 ? [] : [dims[0]];
        }

        return dims;
    }

    private static JgsValue? Stack(JgsValue[] children)
    {
        Kind kind = KindOf(children[0]);
        if (kind == Kind.Other || children.Any(c => KindOf(c) != kind))
        {
            return null;
        }

        int[] dims = StackDims(children[0]);
        if (children.Any(c => !StackDims(c).SequenceEqual(dims)))
        {
            return null;
        }

        int n = children.Length;
        int[] result = dims.Length == 0 ? [n, 1] : [n, .. dims];
        int each = dims.Aggregate(1, static (a, b) => a * b);
        if (kind == Kind.Structs)
        {
            Dictionary<string, JgsValue>[][] arrays = [.. children.Select(StructElements)];
            List<string> names = [.. arrays[0][0].Keys];
            if (result.Length > 2 || arrays.Any(a => a.Any(e => !e.Keys.SequenceEqual(names))))
            {
                return null;
            }

            var stackedStructs = new Dictionary<string, JgsValue>[n * each];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < each; j++)
                {
                    stackedStructs[i + n * j] = arrays[i][j];
                }
            }

            return JgsValue.StructArray(new JgsStructArray(stackedStructs), result[0], result[1]);
        }

        var stacked = new JgsValue[n * each];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < each; j++)
            {
                JgsValue e = children[i].Type is JgsType.Number or JgsType.Bool ? children[i] : children[i].ElementAt(j);
                stacked[i + n * j] = e;
            }
        }

        return JgsMatrix.FromElementsDims(stacked, result);
    }

    private static Dictionary<string, JgsValue>[] StructElements(JgsValue value) =>
        value.IsStructArray ? value.AsStructArray.Elements : [value.AsStruct];

    private static Dictionary<string, JgsValue> Fields(Obj obj)
    {
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach ((string name, Node member) in obj.Members)
        {
            string valid = ValidName(name);
            string unique = valid;
            for (int k = 1; fields.ContainsKey(unique); k++)
            {
                unique = $"{valid}_{k}";
            }

            fields[unique] = Value(member);
        }

        return fields;
    }

    /// <summary>
    /// A JSON name as a struct field, as <c>jsondecode</c> makes one (probe <c>u9b_json</c>): blanks
    /// at the ends dropped, a blank inside dropped and the letter after it made upper case, any
    /// other character but a letter, a digit or an underscore made an underscore, and an <c>x</c>
    /// in front of a name that does not start with a letter. Keywords and long names are kept.
    /// </summary>
    internal static string ValidName(string name)
    {
        var built = new StringBuilder(name.Length + 1);
        bool raise = false;
        foreach (char c in name.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                raise = true;
                continue;
            }

            char kept = char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_';
            built.Append(raise && char.IsAsciiLetter(kept) ? char.ToUpperInvariant(kept) : kept);
            raise = false;
        }

        if (built.Length == 0 || !char.IsAsciiLetter(built[0]))
        {
            built.Insert(0, 'x');
        }

        return built.ToString();
    }
}
