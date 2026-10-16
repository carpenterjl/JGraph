namespace JGraph.Scripting.Jgs;

/// <summary>
/// What <c>matlab.lang.OnOffSwitchState</c> answers as a class (open item 129, probe_129 and
/// probe_129b): its constructor and members by name, <c>enumeration</c> of it, its conversions to
/// text and numbers, <c>isequal</c>, <c>strcmp</c> and <c>ismember</c> by name, <c>sort</c> and
/// <c>~</c> as members, and its operators. Every other verb it inherits from <c>logical</c> and is
/// asked of the logical values the members are.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>The verbs the class answers itself.</summary>
    private static readonly HashSet<string> OnOffOwnMethods = new(StringComparer.Ordinal)
    {
        "char", "string", "cellstr", "logical", "double", "single", "int8", "int16", "int32", "int64",
        "uint8", "uint16", "uint32", "uint64", "isequal", "isequaln", "ismember", "strcmp", "strcmpi",
        "isenum", "islogical", "isobject", "isnumeric", "not", "sort", "properties", "fieldnames", "sprintf", "eq", "ne",
        "mat2str", "isprop",
    };

    /// <summary>The verbs it inherits from <c>logical</c>: asked of the members' truth.</summary>
    private static readonly HashSet<string> OnOffAsLogical = new(StringComparer.Ordinal)
    {
        "and", "or", "xor", "plus", "minus", "times", "mtimes", "rdivide", "ldivide", "mrdivide", "power", "mpower",
        "uminus", "uplus", "any", "all", "find", "nnz", "sum", "prod", "cumsum", "cumprod", "max", "min", "mean",
        "median", "mode", "abs", "floor", "ceil", "round", "fix", "sign", "isnan", "isinf", "isfinite", "num2str",
        "int2str", "diff", "nonzeros", "bitand", "bitor", "bitxor", "sqrt", "exp", "log", "mod", "rem",
        "lt", "gt", "le", "ge", "fprintf",
    };

    /// <summary>The method a call reaches when <paramref name="dominant"/> is an OnOffSwitchState.</summary>
    internal static bool TryOnOffMethod(Interpreter interpreter, string name, JgsValue dominant, out IJgsCallable? callable)
    {
        callable = null;
        if (dominant.AsExternalOrNull() is not OnOffSwitchStateValue
            || !(OnOffOwnMethods.Contains(name) || OnOffAsLogical.Contains(name)))
        {
            return false;
        }

        callable = new OnOffMethod(interpreter, name);
        return true;
    }

    /// <summary>The members of an OnOffSwitchState argument, or null for any other value.</summary>
    private static OnOffSwitchStateValue? OnOff(JgsValue value) => value.AsExternalOrNull() as OnOffSwitchStateValue;

    private sealed class OnOffMethod(Interpreter interpreter, string name) : IJgsCallable, IJgsMultiCallable
    {
        public string Name => name;

        public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column) =>
            CallMultiple(arguments, 1, line, column) is [var first, ..] ? first : JgsValue.Null;

        public JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column) =>
            OnOffCall(interpreter, name, arguments, wanted, line, column);
    }

    private static JgsValue[] OnOffCall(Interpreter interpreter, string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        OnOffSwitchStateValue members = args.Select(OnOff).First(static m => m is not null)!;
        JgsValue Shaped(JgsValue[] values, bool cell)
        {
            JgsValue made = cell ? JgsValue.Cell(values) : JgsValue.StringArray(values, members.Rows, members.Columns);
            made.Reshape(members.Rows, members.Columns);
            return made;
        }

        JgsValue Bool(bool b) => JgsValue.Bool(b);
        switch (name)
        {
            case "char" when members.States.Length == 1:
                return [JgsValue.Str(members.Names[0])];
            case "char":
                return [JgsValue.CharMatrix(members.Names)];
            case "string":
                return [Shaped([.. members.Names.Select(JgsValue.Str)], cell: false)];
            case "cellstr":
                return [Shaped([.. members.Names.Select(JgsValue.Str)], cell: true)];
            case "logical":
                return [members.Logical()];
            case "isenum" or "islogical" or "isobject":
                return [JgsValue.True];
            case "isnumeric":
                return [JgsValue.False];
            case "properties" or "fieldnames":
            {
                JgsValue none = JgsValue.Cell([]);
                none.Reshape(0, 1);
                if (wanted == 0 && name == "properties")
                {
                    interpreter.Host?.print($"\nNo properties for class {OnOffSwitchStateValue.Name}.\n");
                    return [];
                }

                return [none];
            }

            case "not":
                return [OnOffSwitchStateValue.Of([.. members.States.Select(static s => !s)], members.Rows, members.Columns)];
            case "isprop":
                return [JgsValue.False];
            case "mat2str":
            {
                // A member spelled the way it is named in code (probe_129).
                string[] spelled = [.. members.Names.Select(static n => OnOffSwitchStateValue.Name + "." + n)];
                return [JgsValue.Str(spelled.Length == 1 ? spelled[0] : "[" + string.Join(" ", spelled) + "]")];
            }
            case "sort":
            {
                bool descend = args.Skip(1).Any(static a => IsTextScalar(a) && TextOf(a).Equals("descend", StringComparison.OrdinalIgnoreCase));
                bool[] sorted = descend ? [.. members.States.OrderByDescending(static s => s)] : [.. members.States.OrderBy(static s => s)];
                return [OnOffSwitchStateValue.Of(sorted, members.Rows, members.Columns)];
            }

            case "eq" or "ne":
            {
                JgsValue other = OnOff(args[0]) is not null ? args[1] : args[0];
                JgsValue same = members.Equal(other)
                    ?? throw new JgsRuntimeException(line, col, "MATLAB:sizeDimensionsMustMatch", "Arrays have incompatible sizes for this operation.");
                return [name == "eq" ? same : CallBuiltinNamed(interpreter, "not", [same], 1, line, col)[0]];
            }

            case "isequal" or "isequaln":
            {
                bool all = args.Count >= 2;
                foreach (JgsValue other in args)
                {
                    if (ReferenceEquals(other.AsExternalOrNull(), members))
                    {
                        continue;
                    }

                    int[] mine = [members.Rows, members.Columns];
                    int[] theirs = other.AsExternalOrNull() is OnOffSwitchStateValue o ? [o.Rows, o.Columns]
                        : other.Type == JgsType.String ? [1, 1] : SizeDims(other);
                    JgsValue? same = members.Equal(other);
                    all &= same is not null && mine.SequenceEqual(theirs)
                        && (same.Type == JgsType.Bool ? same.AsBool : Enumerable.Range(0, same.ArrayLength).All(i => same.ElementAt(i).AsBool));
                }

                return [Bool(all)];
            }

            case "strcmp" or "strcmpi":
            {
                if (args.Count != 2)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
                }

                JgsValue other = OnOff(args[0]) is not null ? args[1] : args[0];
                if (OnOff(other) is { } both)
                {
                    return [Bool(members.SameAs(both))];
                }

                bool insensitive = name == "strcmpi";
                string[] mine = members.Names;
                string?[] theirs = other.Type == JgsType.String ? [other.AsString]
                    : other.IsStringArray || other.Type == JgsType.Cell
                        ? [.. (other.Type == JgsType.Cell ? other.AsCell : other.BoxedElements()).Select(static e => e.Type == JgsType.String ? e.AsString : null)]
                        : [];
                if (theirs.Length == 0)
                {
                    return [JgsValue.False];
                }

                int count = Math.Max(mine.Length, theirs.Length);
                if (mine.Length != 1 && theirs.Length != 1 && mine.Length != theirs.Length)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:strcmp:InputsSizeMismatch", "Inputs must be the same size or either one can be a scalar.");
                }

                var flags = new JgsValue[count];
                for (int i = 0; i < count; i++)
                {
                    string? t = theirs[theirs.Length == 1 ? 0 : i];
                    flags[i] = Bool(t is not null && string.Equals(mine[mine.Length == 1 ? 0 : i], t,
                        insensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
                }

                if (count == 1)
                {
                    return [flags[0]];
                }

                JgsValue mask = JgsValue.Array(flags);
                if (mine.Length == count)
                {
                    mask.Reshape(members.Rows, members.Columns);
                }

                return [mask];
            }

            case "ismember":
            {
                if (args.Count < 2)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
                }

                string[] Names(JgsValue v) => OnOff(v) is { } m ? m.Names
                    : v.Type == JgsType.String ? [v.AsString]
                    : v.IsStringArray || v.Type == JgsType.Cell
                        ? [.. (v.Type == JgsType.Cell ? v.AsCell : v.BoxedElements()).Where(static e => e.Type == JgsType.String).Select(static e => e.AsString)]
                        : [.. Enumerable.Range(0, v.Type == JgsType.Array ? v.ArrayLength : 1)
                            .Select(i => (v.Type == JgsType.Array ? v.ElementAt(i) : v).AsNumber != 0 ? "on" : "off")];
                string[] asked = Names(args[0]);
                var set = new HashSet<string>(Names(args[1]), StringComparer.Ordinal);
                JgsValue[] found = [.. asked.Select(n => Bool(set.Contains(n)))];
                if (found.Length == 1)
                {
                    return [found[0]];
                }

                JgsValue mask = JgsValue.Array(found);
                if (OnOff(args[0]) is { } first)
                {
                    mask.Reshape(first.Rows, first.Columns);
                }

                return [mask];
            }

            case "sprintf" or "fprintf":
            {
                // A member printed by %s is its name, by a number conversion its truth (probe_129b),
                // each by the conversion it meets: sprintf('%d %s', n, h.Visible) prints the name
                // (open item 132: a GUIDE file's log).
                int formatAt = name == "fprintf" && args.Count > 1 && args[0].Type is JgsType.Number && args[1].Type == JgsType.String ? 1 : 0;
                bool[] texts = args.Count > formatAt && args[formatAt].Type == JgsType.String ? ConversionsAreText(args[formatAt].AsString) : [];
                int consumed = 0;
                var given = new JgsValue[args.Count];
                for (int i = 0; i < args.Count; i++)
                {
                    JgsValue a = args[i];
                    if (i <= formatAt)
                    {
                        given[i] = a;
                        continue;
                    }

                    bool text = texts.Length > 0 && texts[consumed % texts.Length];
                    given[i] = OnOff(a) is { } m ? text && m.States.Length == 1 ? JgsValue.Str(m.Names[0]) : m.Logical() : a;
                    consumed += OnOff(a) is { } counted ? counted.States.Length : a.Type == JgsType.String ? 1 : Math.Max(1, a.Type == JgsType.Array ? a.ArrayLength : 1);
                }

                return CallBuiltinNamed(interpreter, name, given, name == "fprintf" ? wanted : Math.Max(wanted, 1), line, col);
            }
        }

        if (JgsNumericClasses.Parse(name) is { } numericClass)
        {
            double[] values = [.. members.States.Select(static s => s ? 1.0 : 0.0)];
            JgsValue plain = values.Length == 1 ? JgsValue.Number(values[0]) : JgsMatrix.FromColumnMajor(values, members.Rows, members.Columns);
            return [JgsNumericClasses.Stamp(plain, numericClass)];
        }

        // Everything logical answers: the members' truth goes in instead of the members.
        JgsValue[] asLogical = [.. args.Select(static a => OnOff(a) is { } m ? m.Logical() : a)];
        return CallBuiltinNamed(interpreter, name, asLogical, Math.Max(wanted, name == "fprintf" ? 0 : 1), line, col);
    }

    /// <summary>
    /// An operator with an OnOffSwitchState on either side (open item 129): <c>==</c> and <c>~=</c>
    /// compare members, names and truth; every other operator is the logical one over the members'
    /// truth, answering a logical or a number as logical's does.
    /// </summary>
    internal static bool TryOnOffOperator(TokenType op, JgsValue left, JgsValue right, int line, int col,
        Func<TokenType, JgsValue, JgsValue, JgsValue> apply, out JgsValue result)
    {
        result = JgsValue.Null;
        OnOffSwitchStateValue? a = OnOff(left);
        OnOffSwitchStateValue? b = OnOff(right);
        if (a is null && b is null)
        {
            return false;
        }

        if (op is TokenType.EqualEqual or TokenType.BangEqual)
        {
            JgsValue same = (a ?? b)!.Equal(a is null ? left : right)
                ?? throw new JgsRuntimeException(line, col, "MATLAB:sizeDimensionsMustMatch", "Arrays have incompatible sizes for this operation.");
            result = op == TokenType.EqualEqual ? same : apply(TokenType.EqualEqual, same, JgsValue.Bool(false));
            return true;
        }

        result = apply(op, a is null ? left : a.Logical(), b is null ? right : b.Logical());
        return true;
    }

    /// <summary>
    /// A bracket with OnOffSwitchState members and text in it (probe_129b): text wins, each member
    /// joining as its name, so <c>[on 'off']</c> is the char row <c>'onoff'</c>.
    /// </summary>
    internal static bool TryOnOffWithText(IReadOnlyList<JgsValue[]> rows, out IReadOnlyList<JgsValue[]> asText)
    {
        asText = rows;
        bool members = rows.Any(static row => row.Any(static e => e.AsExternalOrNull() is OnOffSwitchStateValue));
        bool text = rows.Any(static row => row.Any(static e => e.Type == JgsType.String && !e.IsStringArray || e.IsCharMatrix));
        if (!members || !text)
        {
            return false;
        }

        asText = [.. rows.Select(static row => row.Select(static e => e.AsExternalOrNull() is OnOffSwitchStateValue m
            ? JgsValue.Str(string.Concat(m.Names))
            : e).ToArray())];
        return true;
    }

    /// <summary>
    /// <c>matlab.lang.OnOffSwitchState</c> named in full: the constructor, a member (<c>.on</c>,
    /// <c>.off</c>) or the empty array (<c>.empty</c>). Null for any other name.
    /// </summary>
    internal static JgsValue? OnOffStatic(string dotted, bool autoCall, int line, int col)
    {
        string prefix = OnOffSwitchStateValue.Name + ".";
        if (dotted == OnOffSwitchStateValue.Name)
        {
            JgsValue constructor = JgsValue.Function(new BuiltinFunction(OnOffSwitchStateValue.Name, OnOffSwitchStateValue.Construct)
            {
                KeepsStringArguments = true,
            });
            return autoCall ? OnOffSwitchStateValue.Construct([], line, col) : constructor;
        }

        if (!dotted.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        return dotted[prefix.Length..] switch
        {
            "on" => OnOffSwitchStateValue.Of(true),
            "off" => OnOffSwitchStateValue.Of(false),
            "empty" => OnOffSwitchStateValue.Of([], 0, 0),
            string other => throw new JgsRuntimeException(line, col, "MATLAB:subscripting:classHasNoPropertyOrMethod",
                $"The class {OnOffSwitchStateValue.Name} has no Constant property or Static method named '{other}'."),
        };
    }

    /// <summary>
    /// <c>enumeration('matlab.lang.OnOffSwitchState')</c> or of a member: asked for nothing, the
    /// listing R2025b prints; asked for outputs, the members as a column and their names (probe_129b).
    /// Null for any other subject.
    /// </summary>
    internal static JgsValue[]? OnOffEnumeration(Interpreter interpreter, JgsValue asked, int wanted)
    {
        bool named = (IsTextScalar(asked) && TextOf(asked) == OnOffSwitchStateValue.Name) || OnOff(asked) is not null;
        if (!named)
        {
            return null;
        }

        interpreter.NoteNet(); // the members answered are external values with methods of their own

        if (wanted == 0)
        {
            interpreter.Host?.print($"\nEnumeration members for class '{OnOffSwitchStateValue.Name}':\n\n    off\n    on\n");
            return [];
        }

        JgsValue names = JgsValue.Cell([JgsValue.Str("off"), JgsValue.Str("on")]);
        names.Reshape(2, 1);
        return wanted >= 2
            ? [OnOffSwitchStateValue.Of([false, true], 2, 1), names]
            : [OnOffSwitchStateValue.Of([false, true], 2, 1)];
    }
}
