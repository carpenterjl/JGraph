namespace JGraph.Scripting.Jgs;

/// <summary>
/// The methods every enumeration class has without writing them (stage K9, ADR 0265; measured in
/// k9_enumerations): a member's name as text, comparisons by name with members and with text,
/// <c>isenum</c>, membership, sorting by the order the block declares, and the refusal to be a
/// number. A class's own method of the same name wins, as any method does.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>The verbs an enumeration answers itself.</summary>
    private static readonly HashSet<string> EnumerationMethods = new(StringComparer.Ordinal)
    {
        "char", "string", "cellstr", "isequal", "isequaln", "ismember", "strcmp", "strcmpi", "double", "single",
        "mat2str", "sort", "eq", "ne", "isenum",
    };

    /// <summary>Whether <paramref name="value"/> is a member of an enumeration class, or an array of them.</summary>
    internal static bool IsEnumerationValue(JgsValue value) => value.Type == JgsType.Object && value.ObjectClass.IsEnumeration;

    /// <summary>The member names of an enumeration value, column-major.</summary>
    internal static string[] EnumerationNamesOf(JgsValue value) =>
        value.IsObjectArray
            ? [.. value.AsObjectArray.Elements.Select(static e => e.EnumerationName ?? string.Empty)]
            : [value.AsObject.EnumerationName ?? string.Empty];

    /// <summary><c>disp</c> of members: their names, a row of them on one line, four spaces before each.</summary>
    internal static string EnumerationDisp(JgsValue value)
    {
        string[] names = EnumerationNamesOf(value);
        int[] dims = SizeDims(value);
        int rows = dims[0];
        int cols = names.Length / Math.Max(rows, 1);
        return string.Join("\n", Enumerable.Range(0, rows).Select(r =>
            "    " + string.Join("    ", Enumerable.Range(0, cols).Select(c => names[r + (c * rows)]))));
    }

    /// <summary>The method a call reaches on an enumeration member when its class writes none of that name.</summary>
    internal static bool TryEnumerationMethod(Interpreter interpreter, string name, JgsValue dominant, out IJgsCallable? callable)
    {
        callable = null;
        if (!IsEnumerationValue(dominant) || !EnumerationMethods.Contains(name))
        {
            return false;
        }

        callable = new EnumerationMethod(interpreter, name);
        return true;
    }

    private sealed class EnumerationMethod(Interpreter interpreter, string name) : IJgsCallable, IJgsMultiCallable
    {
        public string Name => name;

        public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column) =>
            CallMultiple(arguments, 1, line, column) is [var first, ..] ? first : JgsValue.Null;

        public JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column) =>
            EnumerationCall(interpreter, name, arguments, wanted, line, column);
    }

    private static JgsValue[] EnumerationCall(Interpreter interpreter, string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        JgsValue subject = args.First(IsEnumerationValue);
        JgsClass definition = subject.ObjectClass;
        int[] dims = SizeDims(subject);
        string[] names = EnumerationNamesOf(subject);
        JgsValue Shaped(JgsValue[] values, bool cell)
        {
            JgsValue made = cell ? JgsValue.Cell(values) : JgsValue.StringArray(values, dims[0], values.Length / Math.Max(dims[0], 1));
            made.ReshapeDims(dims);
            return made;
        }

        switch (name)
        {
            case "char" when names.Length == 1:
                return [JgsValue.Str(names[0])];
            case "char":
                // R2025b hands an array of members back unconverted; their names are what char means (a divergence).
                return [JgsValue.CharMatrix(names)];
            case "string":
                return [Shaped([.. names.Select(JgsValue.Str)], cell: false)];
            case "cellstr":
                return [Shaped([.. names.Select(JgsValue.Str)], cell: true)];
            case "isenum":
                return [JgsValue.True];
            case "double" or "single":
                throw new JgsRuntimeException(line, col, "MATLAB:invalidConversion",
                    $"Conversion to {name} from {definition.Name} is not possible.");
            case "mat2str":
            {
                string[] spelled = [.. names.Select(n => definition.Name + "." + n)];
                return [JgsValue.Str(spelled.Length == 1 ? spelled[0] : "[" + string.Join(" ", spelled) + "]")];
            }

            case "sort":
            {
                // Members sort by the order the enumeration block declares them.
                IReadOnlyList<string> order = definition.EnumerationNames;
                bool descend = args.Skip(1).Any(static a => IsTextScalar(a) && TextOf(a).Equals("descend", StringComparison.OrdinalIgnoreCase));
                IEnumerable<string> sorted = descend
                    ? names.OrderByDescending(n => IndexOfName(order, n))
                    : names.OrderBy(n => IndexOfName(order, n));
                return [Members(definition, [.. sorted], dims, line, col)];
            }

            case "eq" or "ne":
                return [EnumerationEquality(args[0], args[1], name == "eq", line, col)
                    ?? throw new JgsRuntimeException(line, col, "MATLAB:math:mustBeNumericCharOrLogical",
                        "Invalid data type. Argument must be numeric, char, or logical.")];
            case "isequal" or "isequaln":
            {
                bool all = args.Count >= 2;
                for (int i = 1; i < args.Count && all; i++)
                {
                    all = SameEnumeration(args[0], args[i]);
                }

                return [JgsValue.Bool(all)];
            }

            case "strcmp" or "strcmpi":
            {
                bool ignoreCase = name == "strcmpi";
                if (args.Count != 2)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
                }

                string[]? left = NamesOrText(args[0]);
                string[]? right = NamesOrText(args[1]);
                if (left is not { Length: 1 } || right is not { Length: 1 })
                {
                    return [JgsValue.False];
                }

                return [JgsValue.Bool(string.Equals(left[0], right[0], ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))];
            }

            case "ismember":
            {
                string[] these = NamesOrText(args[0]) ?? [];
                string[] those = args.Count > 1 ? NamesOrText(args[1]) ?? [] : [];
                var found = new JgsValue[these.Length];
                var where = new JgsValue[these.Length];
                for (int i = 0; i < these.Length; i++)
                {
                    int at = Array.IndexOf(those, these[i]);
                    found[i] = JgsValue.Bool(at >= 0);
                    where[i] = JgsValue.Number(at + 1);
                }

                int[] shape = SizeDims(args[0]);
                JgsValue tf = found.Length == 1 ? found[0] : JgsMatrix.FromElementsDims(found, shape);
                JgsValue loc = where.Length == 1 ? where[0] : JgsMatrix.FromElementsDims(where, shape);
                return wanted >= 2 ? [tf, loc] : [tf];
            }

            default:
                throw new JgsRuntimeException(line, col, $"'{name}' is not answered for an enumeration.");
        }

        static int IndexOfName(IReadOnlyList<string> order, string name)
        {
            for (int i = 0; i < order.Count; i++)
            {
                if (order[i] == name)
                {
                    return i;
                }
            }

            return order.Count;
        }
    }

    /// <summary>The members <paramref name="names"/> of <paramref name="definition"/> laid out as <paramref name="dims"/>.</summary>
    private static JgsValue Members(JgsClass definition, string[] names, int[] dims, int line, int col)
    {
        var elements = new JgsObject[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            elements[i] = definition.TryMember(names[i], out JgsValue member)
                ? member.AsObject
                : throw new JgsRuntimeException(line, col, $"'{names[i]}' is not a member of {definition.Name}.");
        }

        return elements.Length == 1
            ? JgsValue.Object(elements[0])
            : JgsValue.ObjectArray(new JgsObjectArray(definition, elements), dims[0], elements.Length / Math.Max(dims[0], 1));
    }

    /// <summary>A side of a comparison as names: an enumeration's members, or text - a char row, a string array, a cell of char rows.</summary>
    private static string[]? NamesOrText(JgsValue value)
    {
        if (IsEnumerationValue(value))
        {
            return EnumerationNamesOf(value);
        }

        if (value.IsStringArray)
        {
            return [.. value.BoxedElements().Select(static e => e.AsString)];
        }

        if (value.Type == JgsType.String && !value.IsCharMatrix)
        {
            return [value.AsString];
        }

        if (value.Type == JgsType.Cell && Array.TrueForAll(value.AsCell, static c => c.Type == JgsType.String))
        {
            return [.. value.AsCell.Select(static c => c.AsString)];
        }

        return null;
    }

    /// <summary>
    /// <c>==</c> or <c>~=</c> with an enumeration on either side: element by element by name, a
    /// single member or a name expanded against an array, text that names no member unequal; null
    /// when neither side is an enumeration or the other is not text or a member.
    /// </summary>
    internal static JgsValue? EnumerationEquality(JgsValue left, JgsValue right, bool equal, int line, int col)
    {
        if (!IsEnumerationValue(left) && !IsEnumerationValue(right))
        {
            return null;
        }

        string[]? a = NamesOrText(left);
        string[]? b = NamesOrText(right);
        if (a is null || b is null)
        {
            return null;
        }

        JgsValue shapeOf = a.Length >= b.Length ? left : right;
        int[] dims = a.Length == 1 && b.Length == 1 ? [1, 1] : SizeDims(a.Length >= b.Length ? left : right);
        if (a.Length != b.Length && a.Length != 1 && b.Length != 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:sizeDimensionsMustMatch", "Arrays have incompatible sizes for this operation.");
        }

        int count = Math.Max(a.Length, b.Length);
        var results = new JgsValue[count];
        for (int i = 0; i < count; i++)
        {
            bool same = string.Equals(a[a.Length == 1 ? 0 : i], b[b.Length == 1 ? 0 : i], StringComparison.Ordinal);
            results[i] = JgsValue.Bool(same == equal);
        }

        _ = shapeOf;
        return count == 1 ? results[0] : JgsMatrix.FromElementsDims(results, dims);
    }

    /// <summary>isequal of an enumeration with another value: the same member names in the same shape, or a single member and its name.</summary>
    private static bool SameEnumeration(JgsValue left, JgsValue right)
    {
        string[]? a = NamesOrText(left);
        string[]? b = NamesOrText(right);
        if (a is null || b is null || a.Length != b.Length)
        {
            return false;
        }

        if (a.Length != 1 && !SizeDims(left).SequenceEqual(SizeDims(right)))
        {
            return false;
        }

        if (IsEnumerationValue(left) && IsEnumerationValue(right) && !ReferenceEquals(left.ObjectClass, right.ObjectClass))
        {
            return false;
        }

        return a.SequenceEqual(b, StringComparer.Ordinal);
    }

    /// <summary>
    /// Converts <paramref name="value"/> to a member of the enumeration <paramref name="definition"/>
    /// for an <c>arguments</c> line (k9_enumerations): a member as itself, a name as its member, and
    /// anything else refused in R2025b's words after <paramref name="prefix"/>.
    /// </summary>
    internal static JgsValue ToEnumerationMember(JgsClass definition, JgsValue value, string prefix, int line, int col)
    {
        if (IsEnumerationValue(value) && ReferenceEquals(value.ObjectClass, definition))
        {
            return value;
        }

        IReadOnlyList<string> order = definition.EnumerationNames;
        string choices = order.Count switch
        {
            1 => $"'{order[0]}'",
            2 => $"'{order[0]}' or '{order[1]}'",
            _ => string.Join(", ", order.Take(order.Count - 1).Select(static n => $"'{n}'")) + $", or '{order[^1]}'",
        };
        if (IsTextScalar(value))
        {
            string text = TextOf(value);
            if (definition.TryMember(text, out JgsValue member))
            {
                return member;
            }

            throw new JgsRuntimeException(line, col, "MATLAB:validation:UnableToConvert", $"{prefix}'{text}' is invalid. Value must be {choices}.");
        }

        throw new JgsRuntimeException(line, col, "MATLAB:validation:UnableToConvert", $"{prefix}Value must be {choices}.");
    }

    /// <summary>
    /// <c>enumeration</c> of a user enumeration class, named by text or by one of its members: the
    /// members as a column, their names as a second output, or the listing printed (k9_enumerations).
    /// </summary>
    internal static JgsValue[]? UserEnumeration(Interpreter interpreter, JgsValue asked, int wanted, int line, int col)
    {
        JgsClass? definition = IsEnumerationValue(asked)
            ? asked.ObjectClass
            : IsTextScalar(asked) && interpreter.ClassForLoad(TextOf(asked)) is { IsEnumeration: true } named ? named : null;
        if (definition is null)
        {
            return null;
        }

        IReadOnlyList<string> order = definition.EnumerationNames;
        if (wanted == 0)
        {
            interpreter.Host?.print($"\nEnumeration members for class '{definition.Name}':\n\n"
                + string.Concat(order.Select(static n => "    " + n + "\n")));
            return [];
        }

        JgsValue members = Members(definition, [.. order], [order.Count, 1], line, col);
        JgsValue names = JgsValue.Cell([.. order.Select(JgsValue.Str)]);
        names.Reshape(order.Count, 1);
        return wanted >= 2 ? [members, names] : [members];
    }
}
