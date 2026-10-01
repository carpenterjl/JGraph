using JGraph.Scripting.Jgs.Devices;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The functions an array of external values answers as methods of its class (device classes plan,
/// stage D10b, ADR 0194): its shape (<c>size</c>, <c>numel</c>, <c>isempty</c> and the <c>is…</c>
/// questions), <c>isequal</c>, the rearrangements (<c>repmat</c>, <c>reshape</c>, <c>transpose</c>,
/// <c>horzcat</c>, <c>vertcat</c>, <c>cat</c>), <c>num2cell</c>, <c>arrayfun</c> (as <c>cellfun</c> over its
/// elements), <c>disp</c>, <c>properties</c> and <c>methods</c>; and what a midimsgtype member answers.
/// They sit in the user-method layer (M145), where a device object's methods are.
/// </summary>
internal static partial class JgsBuiltins
{
    private static readonly HashSet<string> ExternalArrayMethods =
    [
        "size", "numel", "length", "isempty", "ndims", "isscalar", "isvector", "isrow", "iscolumn", "ismatrix",
        "isequal", "isequaln", "repmat", "reshape", "transpose", "ctranspose", "horzcat", "vertcat", "cat",
        "num2cell", "arrayfun", "disp", "display", "properties", "fieldnames", "methods", "isprop",
        "double", "single", "logical", "int8", "int16", "int32", "int64", "uint8", "uint16", "uint32", "uint64", "char",
    ];

    private static readonly HashSet<string> MidiTypeMethods =
    [
        "double", "single", "int8", "int16", "int32", "int64", "uint8", "uint16", "uint32", "uint64",
        "char", "string", "cellstr", "isequal", "isenum", "isnumeric", "isinteger", "ismember",
    ];

    /// <summary>The method a call <c>name(…, a, …)</c> reaches when <paramref name="dominant"/> is an array of external values or a midimsgtype member.</summary>
    internal static bool TryExternalArrayMethod(Interpreter interpreter, string name, JgsValue dominant, out IJgsCallable? callable)
    {
        callable = null;
        if (dominant.AsExternalOrNull() is MidiTypeValue && MidiTypeMethods.Contains(name))
        {
            callable = new TypeMethod(interpreter, name);
            return true;
        }

        if (dominant.AsExternalOrNull() is IJgsExternalArray && ExternalArrayMethods.Contains(name))
        {
            callable = new ArrayMethod(interpreter, name);
            return true;
        }

        if (dominant.AsExternalOrNull() is DeviceArray && DeviceRowMethods.Contains(name))
        {
            callable = new DeviceRowMethod(interpreter, name);
            return true;
        }

        return false;
    }

    /// <summary>
    /// The questions about its shape that the row <c>serialportfind</c> answers takes (ADR 0197): R2025b's
    /// is a 1-by-N object array (probe_dev_arrays), so <c>numel</c> is N where an external value's is 1.
    /// </summary>
    private static readonly HashSet<string> DeviceRowMethods =
        ["size", "numel", "length", "isempty", "ndims", "isscalar", "isvector", "isrow", "iscolumn", "ismatrix"];

    /// <summary>Answers a shape question about a row of device objects by asking it of a 1-by-N row of numbers.</summary>
    private sealed class DeviceRowMethod(Interpreter interpreter, string name) : IJgsCallable, IJgsMultiCallable
    {
        public string Name => name;

        public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column) =>
            CallMultiple(arguments, 1, line, column) is [var first, ..] ? first : JgsValue.Null;

        public JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column)
        {
            JgsValue[] shaped =
            [
                .. arguments.Select(static v => v.AsExternalOrNull() is DeviceArray row
                    ? JgsMatrix.FromColumnMajor(new double[row.Items.Count], 1, row.Items.Count)
                    : v),
            ];
            return CallBuiltinNamed(interpreter, name, shaped, wanted, line, column);
        }
    }

    private sealed class ArrayMethod(Interpreter interpreter, string name) : IJgsCallable, IJgsMultiCallable
    {
        public string Name => name;

        public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column) =>
            CallMultiple(arguments, 1, line, column) is [var first, ..] ? first : JgsValue.Null;

        public JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column) =>
            ExternalArrayCall(interpreter, name, arguments, wanted, line, column);
    }

    private sealed class TypeMethod(Interpreter interpreter, string name) : IJgsCallable, IJgsMultiCallable
    {
        public string Name => name;

        public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column) =>
            CallMultiple(arguments, 1, line, column) is [var first, ..] ? first : JgsValue.Null;

        public JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column) =>
            MidiTypeCall(interpreter, name, arguments, line, column);
    }

    private static JgsValue Wrap(IJgsExternalArray array) => JgsValue.External(array);

    private static int[] ShapeArgs(string name, IReadOnlyList<JgsValue> args, int from, int count, int line, int col)
    {
        var dims = new List<int>();
        for (int i = from; i < args.Count; i++)
        {
            if (args[i].Type == JgsType.Array && args[i].ArrayLength == 0)
            {
                dims.Add(-1); // reshape's [] placeholder
                continue;
            }

            dims.AddRange(ToDoubles(name, args[i], line, col).Select(static d => (int)d));
        }

        if (dims.Count == 1 && name == "repmat")
        {
            dims.Add(dims[0]);
        }

        if (dims.Count >= 2 && dims.Skip(2).All(static d => d == 1))
        {
            dims = dims.Take(2).ToList();
        }

        if (dims.Count != 2)
        {
            throw new JgsRuntimeException(line, col, "JGraph:externalArray:NDims",
                "An object array of more than two dimensions is not supported in JGraph.");
        }

        if (dims.Contains(-1))
        {
            int known = dims.Where(static d => d >= 0).Aggregate(1, static (a, b) => a * b);
            dims[dims.IndexOf(-1)] = known == 0 ? 0 : count / known;
        }

        return [.. dims];
    }

    private static JgsValue[] ExternalArrayCall(Interpreter interpreter, string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        IJgsExternalArray a = args.Select(static v => v.AsExternalOrNull()).OfType<IJgsExternalArray>().First();
        int count = ExternalArrays.Count(a);
        int rows = a.Rows;
        int columns = a.Columns;
        JgsValue Bool(bool b) => JgsValue.Bool(b);
        IJgsExternalArray First() => args[0].AsExternalOrNull() as IJgsExternalArray
            ?? throw new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction",
                $"Undefined function '{name}' for input arguments of type '{ClassOf(args[0], JgsDialect.Matlab)}'.");
        switch (name)
        {
            case "size":
            {
                First();
                if (args.Count == 1 && wanted > 1)
                {
                    var outs = new JgsValue[wanted];
                    for (int i = 0; i < wanted; i++)
                    {
                        outs[i] = JgsValue.Number(i == 0 ? rows : i == 1 ? columns : 1);
                    }

                    if (wanted == 2)
                    {
                        outs[1] = JgsValue.Number(columns);
                    }

                    return outs;
                }

                if (args.Count == 1)
                {
                    return [TransportClient.Row([rows, columns])];
                }

                double[] asked = args.Skip(1).SelectMany(v => ToDoubles("size", v, line, col)).ToArray();
                double[] extents = asked.Select(d => d == 1 ? rows : d == 2 ? (double)columns : 1).ToArray();
                return [extents.Length == 1 ? JgsValue.Number(extents[0]) : TransportClient.Row(extents)];
            }

            case "numel":
                return [JgsValue.Number(count)];
            case "length":
                return [JgsValue.Number(count == 0 ? 0 : Math.Max(rows, columns))];
            case "isempty":
                return [Bool(count == 0)];
            case "ndims":
                return [JgsValue.Number(2)];
            case "isscalar":
                return [Bool(count == 1)];
            case "isvector":
                return [Bool((rows == 1 || columns == 1) && count >= 1)];
            case "isrow":
                return [Bool(rows == 1)];
            case "iscolumn":
                return [Bool(columns == 1)];
            case "ismatrix":
                return [Bool(true)];
            case "isequal" or "isequaln":
            {
                if (args.Count < 2)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
                }

                bool same = args[0].AsExternalOrNull() is IJgsExternalArray lead
                    && args.Skip(1).All(v => v.AsExternalOrNull() is IJgsExternalArray other && other.ClassName == lead.ClassName && lead.SameAs(other));
                return [Bool(same)];
            }

            case "repmat":
            {
                int[] dims = ShapeArgs("repmat", args, 1, count, line, col);
                return [Wrap(ExternalArrays.Tiled(First(), Math.Max(0, dims[0]), Math.Max(0, dims[1])))];
            }

            case "reshape":
            {
                int[] dims = ShapeArgs("reshape", args, 1, count, line, col);
                if (dims[0] * dims[1] != count)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:getReshapeDims:notSameNumel",
                        "Number of elements must not change. Use [] as one of the size inputs to automatically calculate the appropriate size for that dimension.");
                }

                return [Wrap(ExternalArrays.Reshaped(First(), dims[0], dims[1]))];
            }

            case "transpose" or "ctranspose":
                return [Wrap(ExternalArrays.Transposed(First()))];
            case "horzcat":
                return [Wrap(ExternalArrays.Join(a, [args.ToArray()], line, col))];
            case "vertcat":
                return [Wrap(ExternalArrays.Join(a, args.Select(static v => new[] { v }).ToList(), line, col))];
            case "cat":
            {
                double dim = args.Count > 0 && args[0].AsExternalOrNull() is null ? ToDoubles("cat", args[0], line, col).FirstOrDefault() : 0;
                JgsValue[] pieces = args.Skip(1).ToArray();
                return dim switch
                {
                    1 => [Wrap(ExternalArrays.Join(a, pieces.Select(static v => new[] { v }).ToList(), line, col))],
                    2 => [Wrap(ExternalArrays.Join(a, [pieces], line, col))],
                    _ => throw new JgsRuntimeException(line, col, "JGraph:externalArray:NDims",
                        "An object array of more than two dimensions is not supported in JGraph."),
                };
            }

            case "num2cell":
                return [ExternalArrays.ToCell(First())];
            case "arrayfun":
            {
                JgsValue[] converted = args.Select(static v => v.AsExternalOrNull() is IJgsExternalArray each ? ExternalArrays.ToCell(each) : v).ToArray();
                for (int i = 1; i < converted.Length; i++)
                {
                    if (converted[i].Type is JgsType.Array or JgsType.Number or JgsType.Bool && args[i].AsExternalOrNull() is null
                        && !(i >= 2 && IsTextScalar(args[i - 1]) && TextOf(args[i - 1]) is "UniformOutput" or "ErrorHandler"))
                    {
                        converted[i] = CallBuiltinNamed(interpreter, "num2cell", [converted[i]], 1, line, col)[0];
                    }
                }

                return CallBuiltinNamed(interpreter, "cellfun", converted, Math.Max(1, wanted), line, col);
            }

            case "disp" or "display":
                interpreter.Host?.print(First().Disp());
                return [];
            case "properties" or "fieldnames":
            {
                string[] names = a is MidiMsgValue ? MidiMsgs.PropertyNames : [];
                if (wanted == 0 && name == "properties")
                {
                    interpreter.Host?.print($"\nProperties for class {a.ClassName}:\n\n" + string.Concat(names.Select(static n => $"    {n}\n")));
                    return [];
                }

                JgsValue cell = JgsValue.Cell(names.Select(JgsValue.Str).ToArray());
                cell.Reshape(names.Length, names.Length == 0 ? 0 : 1);
                return [cell];
            }

            case "methods":
            {
                string[] names = [a.ClassName];
                if (wanted == 0)
                {
                    interpreter.Host?.print($"\nMethods for class {a.ClassName}:\n\n{a.ClassName}  \n");
                    return [];
                }

                JgsValue cell = JgsValue.Cell(names.Select(JgsValue.Str).ToArray());
                cell.Reshape(names.Length, 1);
                return [cell];
            }

            case "isprop":
                return [Bool(args.Count == 2 && IsTextScalar(args[1]) && a is MidiMsgValue && MidiMsgs.PropertyNames.Contains(TextOf(args[1])))];
            default:
                // double(m), char(m) and the other conversions (R2025b, probe_midi_msg).
                throw new JgsRuntimeException(line, col, "MATLAB:invalidConversion",
                    $"Conversion to {name} from {a.ClassName} is not possible.");
        }
    }

    /// <summary>A builtin called by name from a method, with the output count asked.</summary>
    private static JgsValue[] CallBuiltinNamed(Interpreter interpreter, string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (!interpreter.Globals.Builtins.TryGet(name, out JgsValue function) || function.Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col, $"The builtin '{name}' is missing.");
        }

        return function.AsCallable is IJgsMultiCallable multi
            ? multi.CallMultiple(args, wanted, line, col)
            : [function.AsCallable.Call(args, line, col)];
    }

    // --- midimsgtype members ----------------------------------------------------------------------------------

    private static JgsValue[] MidiTypeCall(Interpreter interpreter, string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        var members = (MidiTypeValue)args.First(static v => v.AsExternalOrNull() is MidiTypeValue).AsExternal;
        JgsValue Shaped(JgsValue[] values)
        {
            JgsValue cell = JgsValue.Cell(values);
            cell.Reshape(members.Rows, members.Columns);
            return cell;
        }

        string[] names = members.Codes.Select(MidiTypeValue.NameOf).ToArray();
        switch (name)
        {
            case "char" when members.IsScalar:
                return [JgsValue.Str(members.Name)];
            case "char":
                throw new JgsRuntimeException(line, col, "MATLAB:invalidConversion", "Conversion to char from midimsgtype is not possible.");
            case "string" when members.IsScalar:
                return [JgsValue.StringScalar(members.Name)];
            case "string":
                return [CallBuiltinNamed(interpreter, "string", [Shaped(names.Select(JgsValue.Str).ToArray())], 1, line, col)[0]];
            case "cellstr":
                return [Shaped(names.Select(JgsValue.Str).ToArray())];
            case "isequal":
                return [JgsValue.Bool(args.Count >= 2 && args.All(v => ReferenceEquals(v.AsExternalOrNull(), members)
                    || (v.AsExternalOrNull() is MidiTypeValue other ? other.SameAs(members) : members.IsScalar && members.Matches(v))))];
            case "isenum" or "isnumeric" or "isinteger":
                return [JgsValue.True];
            case "ismember":
            {
                JgsValue set = args.Count > 1 ? args[1] : JgsValue.Null;
                var found = new JgsValue[members.Codes.Length];
                for (int i = 0; i < found.Length; i++)
                {
                    var one = (MidiTypeValue)members.Build([(members, i)], 1, 1);
                    found[i] = JgsValue.Bool(set.Type == JgsType.Cell ? set.AsCell.Any(one.Matches)
                        : set.AsExternalOrNull() is MidiTypeValue many ? many.Codes.Contains(one.Code) : one.Matches(set));
                }

                if (found.Length == 1)
                {
                    return [found[0]];
                }

                JgsValue mask = JgsValue.Array(found);
                mask.Reshape(members.Rows, members.Columns);
                return [mask];
            }

            default:
                return [members.Numbers(JgsNumericClasses.Parse(name) ?? JgsNumericClass.Int32)];
        }
    }

    /// <summary>An operator with a midimsg or a midimsgtype member on either side (device classes plan, stage D10b).</summary>
    internal static bool TryMidiOperator(TokenType op, JgsValue left, JgsValue right, int line, int col,
        Func<TokenType, JgsValue, JgsValue, JgsValue> apply, out JgsValue result)
    {
        result = JgsValue.Null;
        MidiTypeValue? a = left.AsExternalOrNull() as MidiTypeValue;
        MidiTypeValue? b = right.AsExternalOrNull() as MidiTypeValue;
        if (a is not null || b is not null)
        {
            // == and ~= ask which members the other side names: members, a name, or numbers.
            if (op is TokenType.EqualEqual or TokenType.BangEqual)
            {
                JgsValue same = (a ?? b)!.Equal(a is null ? left : right)
                    ?? throw new JgsRuntimeException(line, col, "MATLAB:sizeDimensionsMustMatch", "Arrays have incompatible sizes for this operation.");
                result = op == TokenType.EqualEqual ? same : apply(TokenType.EqualEqual, same, JgsValue.Bool(false));
                return true;
            }

            result = apply(op, a is null ? left : a.Numbers(), b is null ? right : b.Numbers());
            return true;
        }

        if (left.AsExternalOrNull() is IJgsExternalArray || right.AsExternalOrNull() is IJgsExternalArray)
        {
            string className = (left.AsExternalOrNull() as IJgsExternalArray ?? (IJgsExternalArray)right.AsExternal).ClassName;
            throw op is TokenType.EqualEqual or TokenType.BangEqual
                ? new JgsRuntimeException(line, col, "MATLAB:math:mustBeNumericCharOrLogical", "Invalid data type. Argument must be numeric, char, or logical.")
                : new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction", $"Operator '{OperatorWord(op)}' is not supported for operands of type '{className}'.");
        }

        return false;
    }

    // --- statics ------------------------------------------------------------------------------------------------------

    private static bool s_midiTrace;

    /// <summary><c>midimsgtype.NoteOn</c>, <c>midicontrols.trace</c> and <c>midicontrols.devices</c>.</summary>
    internal static JgsValue MidiClassStatic(Interpreter interpreter, string owner, string member, bool autoCall, int line, int col)
    {
        interpreter.NoteNet(); // a call with a member among its arguments must now look for the member's methods
        if (owner == "midimsgtype")
        {
            return MidiTypeStatic(member, line, col);
        }

        switch (member)
        {
            case "trace":
                var trace = new BuiltinFunction("midicontrols.trace", (args, l, c) =>
                {
                    bool was = s_midiTrace;
                    if (args.Count > 0)
                    {
                        if (args[0].Type != JgsType.Bool && !(args[0].Type == JgsType.Array && ClassOf(args[0], JgsDialect.Matlab) == "logical"))
                        {
                            throw new JgsRuntimeException(l, c, "audio:midicontrols:invalidtracearg", "Invalid trace argument.");
                        }

                        s_midiTrace = args[0].IsTruthy;
                    }

                    return JgsValue.Bool(was);
                });
                return autoCall ? trace.Call([], line, col) : JgsValue.Function(trace);
            case "devices" when interpreter.Host is { } host:
            {
                var elements = MidiShared.Backend(host.Devices).Devices().Select(static d => new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["interf"] = JgsValue.Str(d.Interface),
                    ["name"] = JgsValue.Str(d.Name),
                    ["input"] = JgsValue.Bool(d.Input),
                }).ToArray();
                return elements.Length == 0
                    ? JgsValue.StructArray(new JgsStructArray([], ["interf", "name", "input"]), 1, 0)
                    : JgsValue.StructArray(elements);
            }

            default:
                throw new JgsRuntimeException(line, col, "MATLAB:subscripting:classHasNoPropertyOrMethod",
                    $"The class midicontrols has no Constant property or Static method named '{member}'.");
        }
    }
}
