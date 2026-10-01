namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// The checks R2025b's device classes make with <c>validateattributes</c> and <c>validatestring</c>,
/// in those functions' own identifiers and sentences (device classes plan, stage 1): an identifier of
/// <c>MATLAB:&lt;function&gt;:expectedPositive</c> when a function name is given and
/// <c>MATLAB:expectedPositive</c> when not; "Expected input number 2, BAUDRATE, to be positive." with
/// an argument index, "Expected Timeout to be finite." with only a name. Each check names its
/// attributes in the order the R2025b source lists them, which is the order they are tested.
/// </summary>
internal static class DeviceChecks
{
    /// <summary>Who is being checked: the function the identifier names, the variable's name, its position.</summary>
    internal readonly record struct Subject(string? Function, string? Name, int Index = 0)
    {
        public string Words => Index > 0 ? (Name is null ? $"input number {Index}" : $"input number {Index}, {Name},") : Name ?? "input";

        public string Id(string key) => Function is null ? $"MATLAB:{key}" : $"MATLAB:{Function}:{key}";
    }

    /// <summary>The classes <c>'numeric'</c> stands for, in the order validateattributes lists them.</summary>
    internal static readonly string[] NumericClasses = ["double", "single", "uint8", "uint16", "uint32", "uint64", "int8", "int16", "int32", "int64"];

    public static JgsRuntimeException Expected(Subject who, string key, string phrase, int line, int col) =>
        new(line, col, who.Id(key), $"Expected {who.Words} to be {phrase}.");

    /// <summary>
    /// validateattributes' class test: <paramref name="classes"/> may hold <c>numeric</c>, which
    /// admits every numeric class and is listed as them. A cell is refused without the "Instead its
    /// type was" sentence, as R2025b words it.
    /// </summary>
    public static void Classes(JgsValue value, string[] classes, Subject who, int line, int col, bool namesCell = false)
    {
        string actual = ClassOf(value);
        bool numeric = Array.IndexOf(classes, "numeric") >= 0;
        if (Array.IndexOf(classes, actual) >= 0 || (numeric && Array.IndexOf(NumericClasses, actual) >= 0))
        {
            return;
        }

        throw TypeRefusal(who, ListedClasses(classes), actual, line, col, namesCell);
    }

    /// <summary>
    /// The "to be one of these types" refusal, with its "Instead" sentence — which validateattributes
    /// leaves out for a cell, and a transport's own check does not (<paramref name="namesCell"/>).
    /// </summary>
    public static JgsRuntimeException TypeRefusal(Subject who, string listed, string? actual, int line, int col, bool namesCell = false)
    {
        string text = $"Expected {who.Words} to be one of these types:\n\n{listed}";
        if (actual is not null && (namesCell || actual is not ("cell" or "char")))
        {
            text += $"\n\nInstead its type was {actual}.";
        }

        return new JgsRuntimeException(line, col, who.Id("invalidType"), text);
    }

    public static string ListedClasses(string[] classes) =>
        string.Join(", ", classes.SelectMany(static c => c == "numeric" ? NumericClasses : [c]));

    /// <summary>
    /// validateattributes' attributes, tested in the order given: <c>scalar</c>, <c>nonempty</c>,
    /// <c>positive</c>, <c>nonnegative</c>, <c>nonzero</c>, <c>integer</c>, <c>finite</c>,
    /// <c>nonnan</c>, <c>real</c>, <c>row</c>, <c>vector</c>, and <c>'&gt;', 0</c> spelled <c>gt0</c>.
    /// </summary>
    public static void Attributes(JgsValue value, string[] attributes, Subject who, int line, int col)
    {
        foreach (string attribute in attributes)
        {
            // Bounds are written "ge:1", "le:65535": "a scalar with value >= 1", as validateattributes words them.
            if (attribute.Length > 3 && attribute[2] == ':' && attribute[..2] is "ge" or "le" or "gt" or "lt")
            {
                double bound = double.Parse(attribute[3..], System.Globalization.CultureInfo.InvariantCulture);
                string op = attribute[..2] switch { "ge" => ">=", "le" => "<=", "gt" => ">", _ => "<" };
                Func<double, bool> ok = attribute[..2] switch
                {
                    "ge" => x => x >= bound,
                    "le" => x => x <= bound,
                    "gt" => x => x > bound,
                    _ => x => x < bound,
                };
                if (Any(value, x => !ok(x)))
                {
                    // "a scalar with value" when the value is one and scalar is asked for; a lone
                    // value checked without scalar is "an array with all of the values" (probe_midi_msg).
                    string what = Count(value) == 1 && Array.IndexOf(attributes, "scalar") >= 0 ? $"a scalar with value {op} {bound.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                        : $"an array with all of the values {op} {bound.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                    string key = attribute[..2] switch { "ge" => "notGreaterEqual", "le" => "notLessEqual", "gt" => "notGreater", _ => "notLess" };
                    throw Expected(who, key, what, line, col);
                }

                continue;
            }

            switch (attribute)
            {
                case "scalar" when Count(value) != 1:
                    throw Expected(who, "expectedScalar", "a scalar", line, col);
                case "nonempty" when Count(value) == 0:
                    throw Expected(who, "expectedNonempty", "nonempty", line, col);
                case "positive" when Any(value, static x => !(x > 0)):
                    throw Expected(who, "expectedPositive", "positive", line, col);
                case "gt0" when Any(value, static x => !(x > 0)):
                    throw Expected(who, "expectedPositive", "positive", line, col);
                case "nonnegative" when Any(value, static x => x < 0 || double.IsNaN(x)):
                    throw Expected(who, "expectedNonnegative", "nonnegative", line, col);
                case "nonzero" when Any(value, static x => x == 0):
                    throw Expected(who, "expectedNonZero", "nonzero", line, col);
                case "integer" when Any(value, static x => !double.IsFinite(x) || x != Math.Floor(x)):
                    throw Expected(who, "expectedInteger", "integer-valued", line, col);
                case "finite" when Any(value, static x => !double.IsFinite(x)):
                    throw Expected(who, "expectedFinite", "finite", line, col);
                case "real" when JgsBuiltins.HasComplexPart(value):
                    throw Expected(who, "expectedReal", "real", line, col);
                case "nonnan" when Any(value, double.IsNaN):
                    throw Expected(who, "expectedNonNaN", "non-NaN", line, col);
                case "row" when !(value.Rows == 1):
                    throw Expected(who, "expectedRow", "a row vector", line, col);
                case "vector" when !(value.Rows == 1 || value.Cols == 1):
                    throw Expected(who, "expectedVector", "a vector", line, col);
            }
        }
    }

    /// <summary>
    /// validatestring: a case-blind match of a leading part of one of <paramref name="options"/>. Several
    /// matches resolve to an exact one, or to the shortest when it leads all the others ("C" is "CR",
    /// not "CR/LF"); otherwise the match is ambiguous.
    /// </summary>
    public static string ValidateString(JgsValue value, string[] options, Subject who, int line, int col)
    {
        string listed = string.Join(", ", options.Select(static o => $"'{o}'"));
        string? text = value.Type == JgsType.String ? value.AsString
            : value.IsStringArray && value.ArrayLength == 1 && value.ElementAt(0).Type == JgsType.String ? value.ElementAt(0).AsString
            : null;
        if (text is null)
        {
            throw new JgsRuntimeException(line, col, who.Id("unrecognizedStringChoice"),
                $"Expected {who.Words} to match one of these values:\n\n{listed}\n\nThe input did not match any of the valid values.");
        }

        if (Match(text, options, out string? found, out bool ambiguous))
        {
            return found!;
        }

        if (ambiguous)
        {
            throw new JgsRuntimeException(line, col, who.Id("ambiguousStringChoice"),
                $"Expected {who.Words} to match one of these values:\n\n{listed}\n\nThe input, '{text}', matched more than one valid value.");
        }

        throw new JgsRuntimeException(line, col, who.Id("unrecognizedStringChoice"),
            $"Expected {who.Words} to match one of these values:\n\n{listed}\n\nThe input, '{text}', did not match any of the valid values.");
    }

    /// <summary>validatestring's match without its refusals.</summary>
    public static bool Match(string text, string[] options, out string? found, out bool ambiguous)
    {
        found = null;
        ambiguous = false;
        if (text.Length == 0)
        {
            return false;
        }

        var hits = options.Where(o => o.StartsWith(text, StringComparison.OrdinalIgnoreCase)).ToList();
        if (hits.Count == 0)
        {
            return false;
        }

        if (hits.Count == 1)
        {
            found = hits[0];
            return true;
        }

        string? exact = hits.Find(h => h.Equals(text, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            found = exact;
            return true;
        }

        string shortest = hits.OrderBy(static h => h.Length).First();
        if (hits.TrueForAll(h => h.StartsWith(shortest, StringComparison.OrdinalIgnoreCase)))
        {
            found = shortest;
            return true;
        }

        ambiguous = true;
        return false;
    }

    /// <summary>The class a value reports, as validateattributes reads it.</summary>
    public static string ClassOf(JgsValue value) => JgsBuiltins.ClassOf(value, JgsDialect.Matlab);

    /// <summary>How many elements a value has.</summary>
    public static int Count(JgsValue value) => value.Type switch
    {
        JgsType.Null => 0,
        JgsType.String => value.AsString.Length,
        JgsType.Cell => value.AsCell.Length,
        JgsType.Array => value.ArrayLength,
        JgsType.Struct => value.AsStructArray.Elements.Count(),
        _ => 1,
    };

    private static bool Any(JgsValue value, Func<double, bool> test)
    {
        foreach (double x in Numbers(value))
        {
            if (test(x))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A value's elements as doubles (a char's codes, a logical's 0 and 1); nothing for anything else.</summary>
    public static IEnumerable<double> Numbers(JgsValue value)
    {
        switch (value.Type)
        {
            case JgsType.Number or JgsType.Bool:
                yield return value.AsNumber;
                break;
            case JgsType.Complex:
                yield return value.AsComplex.Real;
                break;
            case JgsType.String:
                foreach (char c in value.AsString)
                {
                    yield return c;
                }

                break;
            case JgsType.Array:
                // A packed real array is its buffer (ADR 0197): a megabyte written to a socket spent
                // a quarter of a second being walked an element and an iterator at a time.
                if (value.IsPacked && !value.IsPackedComplex && value.PackedKind == JgsPackedKind.Number)
                {
                    double[] raw = value.AsBuffer.AsSpan().ToArray();
                    foreach (double x in raw)
                    {
                        yield return x;
                    }

                    break;
                }

                for (int i = 0; i < value.ArrayLength; i++)
                {
                    JgsValue element = value.ElementAt(i);
                    if (element.Type is JgsType.Number or JgsType.Bool)
                    {
                        yield return element.AsNumber;
                        continue;
                    }

                    foreach (double x in Numbers(element))
                    {
                        yield return x;
                    }
                }

                break;
        }
    }

    /// <summary><see cref="Numbers"/> as an array: a packed real array's buffer copied once, anything else walked.</summary>
    public static double[] NumberArray(JgsValue value) =>
        value.Type == JgsType.Array && value.IsPacked && !value.IsPackedComplex && value.PackedKind == JgsPackedKind.Number
            ? value.AsBuffer.AsSpan().ToArray()
            : Numbers(value).ToArray();

    /// <summary>Whether a value is a scalar string or a char row: the text the device functions take.</summary>
    public static bool IsText(JgsValue value) => JgsBuiltins.IsTextScalar(value);

    /// <summary>The text of a scalar string or a char row.</summary>
    public static string Text(JgsValue value) => JgsBuiltins.TextOf(value);
}
