namespace JGraph.Scripting.Jgs;

/// <summary>
/// The shape verbs over an array of objects (open item 116): <c>reshape</c>, <c>repmat</c>,
/// <c>fliplr</c>, <c>cat</c> and their kind are run over a stand-in array of the element positions,
/// and the positions that come back are the elements, so every verb keeps exactly the arithmetic it
/// has for numbers and an array of objects answers as R2025b's does (probe_116).
/// </summary>
internal static partial class JgsBuiltins
{
    private static readonly string[] ShapeVerbsOverObjects =
    [
        "reshape", "repmat", "fliplr", "flipud", "circshift", "rot90", "permute", "squeeze",
        "transpose", "ctranspose", "cat", "horzcat", "vertcat", "sortrows",
        "sort", "unique", "ismember", "setdiff", "intersect", "union",
    ];

    /// <summary>Wraps each shape verb so an object argument goes through it by a stand-in of positions.</summary>
    private static void WrapShapeVerbsOverObjects(JgsEnvironment env)
    {
        foreach (string name in ShapeVerbsOverObjects)
        {
            if (!env.Builtins.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function)
            {
                continue;
            }

            IJgsCallable inner = existing.AsCallable;
            JgsValue[] Both(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
            {
                JgsValue? over = OverHandles(name, args, line, col) ?? OverObjects(name, args, line, col, inner) ?? OverCategoricals(name, args, line, col, inner);
                if (over is not null)
                {
                    return [over];
                }

                return inner is IJgsMultiCallable multi ? multi.CallMultiple(args, Math.Max(wanted, 1), line, col) : [inner.Call(args, line, col)];
            }

            var wrapper = new BuiltinFunction(name, (args, line, col) => Both(args, 1, line, col)[0])
            {
                MultiOutput = Both,
                BindsAnsAsStatement = inner is not BuiltinFunction { BindsAnsAsStatement: false },
                AutoCallsBare = inner is BuiltinFunction { AutoCallsBare: true },
                KnowsWhenDiscarded = inner is BuiltinFunction { KnowsWhenDiscarded: true },
                TakesOutputCount = inner is BuiltinFunction { TakesOutputCount: true },
            };
            if (inner is BuiltinFunction { KeepsStringArguments: true })
            {
                wrapper.KeepsStringArguments = true;
            }

            env.Builtins.Register(name, JgsValue.Function(wrapper));
        }
    }

    private static readonly HashSet<string> HandleOrderVerbs = new(StringComparer.Ordinal)
    {
        "sort", "unique", "ismember", "setdiff", "intersect", "union",
    };

    /// <summary>
    /// <c>sort</c>, <c>unique</c>, <c>ismember</c>, <c>setdiff</c>, <c>intersect</c> and <c>union</c>
    /// over handle objects, by identity and in creation order (open item 116; R2025b's order is an
    /// address it does not publish, a divergence). A value object is R2025b's refusal. Null for
    /// any other argument.
    /// </summary>
    private static JgsValue? OverHandles(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (!HandleOrderVerbs.Contains(name) || args.Count == 0 || args[0].Type != JgsType.Object)
        {
            return null;
        }

        JgsClass definition = args[0].ObjectClass;
        if (!definition.IsHandle)
        {
            throw name == "ismember"
                ? new JgsRuntimeException(line, col, "MATLAB:math:mustBeNumericCharOrLogical", "Invalid data type. Argument must be numeric, char, or logical.")
                : new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction", $"Undefined function '{(name == "unique" ? "sort" : name)}' for input arguments of type '{definition.Name}'.");
        }

        static JgsObject[] Objects(JgsValue value) => value.IsObjectArray ? value.AsObjectArray.Elements : [value.AsObject];
        static JgsValue Row(JgsClass definition, IEnumerable<JgsObject> picked)
        {
            JgsObject[] elements = [.. picked];
            return JgsValue.ObjectArray(new JgsObjectArray(definition, elements), elements.Length == 0 ? 0 : 1, elements.Length);
        }

        JgsObject[] first = Objects(args[0]);
        if (name == "sort")
        {
            JgsObject[] sorted = [.. first.OrderBy(static o => o.Serial)];
            return JgsValue.ObjectArray(new JgsObjectArray(definition, sorted), args[0].Rows, args[0].Cols);
        }

        if (name == "unique")
        {
            return Row(definition, first.Distinct().OrderBy(static o => o.Serial));
        }

        if (args.Count < 2 || args[1].Type != JgsType.Object)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:math:mustBeNumericCharOrLogical", "Invalid data type. Argument must be numeric, char, or logical.");
        }

        JgsObject[] second = Objects(args[1]);
        var inSecond = new HashSet<JgsObject>(second);
        switch (name)
        {
            case "ismember":
            {
                var flags = new JgsValue[first.Length];
                for (int i = 0; i < flags.Length; i++)
                {
                    flags[i] = JgsValue.Bool(inSecond.Contains(first[i]));
                }

                return args[0].IsObjectArray ? JgsMatrix.FromElements(flags, args[0].Rows, args[0].Cols) : flags[0];
            }

            case "setdiff":
                return Row(definition, first.Where(o => !inSecond.Contains(o)).Distinct().OrderBy(static o => o.Serial));
            case "intersect":
                return Row(definition, first.Where(inSecond.Contains).Distinct().OrderBy(static o => o.Serial));
            default:
                return Row(definition, first.Concat(second).Distinct().OrderBy(static o => o.Serial));
        }
    }

    /// <summary>
    /// The verb over an object argument's elements by position, or null when no argument is an
    /// object. The pieces of a concatenation must all be objects of the one class (an empty is
    /// left out); a verb's numeric arguments, such as <c>cat</c>'s dimension, pass through.
    /// </summary>
    private static JgsValue? OverObjects(string name, IReadOnlyList<JgsValue> args, int line, int col, IJgsCallable inner)
    {
        if (!args.Any(static a => a.Type == JgsType.Object))
        {
            return null;
        }

        bool concatenation = name is "cat" or "horzcat" or "vertcat";
        int firstPiece = name == "cat" ? 1 : 0;
        JgsClass? definition = null;
        var pool = new List<JgsObject>();
        var standIns = new JgsValue[args.Count];
        for (int i = 0; i < args.Count; i++)
        {
            JgsValue argument = args[i];
            if (argument.Type != JgsType.Object)
            {
                bool empty = argument.Type == JgsType.Array && argument.ArrayLength == 0 && !argument.IsStringArray
                    || (argument.Type == JgsType.Cell && argument.AsCell.Length == 0)
                    || (argument.Type == JgsType.String && argument.AsString.Length == 0);
                if (concatenation && i >= firstPiece && !empty)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:class:NoConversionDefined",
                        $"Cannot convert from '{ClassOf(argument, JgsDialect.Matlab)}' to an array of objects in {name}; write the bracket form to convert through the constructor.");
                }

                standIns[i] = concatenation && i >= firstPiece ? JgsMatrix.FromColumnMajor([], 0, 0) : argument;
                continue;
            }

            definition ??= argument.ObjectClass;
            if (!ReferenceEquals(argument.ObjectClass, definition))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:class:NoConversionDefined",
                    $"Cannot convert from '{argument.ObjectClass.Name}' to '{definition.Name}'.");
            }

            (JgsObject[] elements, int rows, int cols) = argument.IsObjectArray
                ? (argument.AsObjectArray.Elements, argument.Rows, argument.Cols)
                : ([argument.AsObject], 1, 1);
            var positions = new JgsValue[elements.Length];
            for (int k = 0; k < elements.Length; k++)
            {
                positions[k] = JgsValue.Number(pool.Count + k);
            }

            pool.AddRange(elements);
            standIns[i] = argument.IsObjectArray && argument.IsNd
                ? JgsObjectArray.PositionsLike(argument, pool.Count - elements.Length)
                : elements.Length == 0
                    ? JgsMatrix.FromColumnMajor([], rows, cols)
                    : JgsMatrix.FromElements(positions, rows, cols);
        }

        JgsValue result = inner.Call(standIns, line, col);
        int count = result.Type == JgsType.Number ? 1 : result.ArrayLength;
        var chosen = new JgsObject[count];
        for (int k = 0; k < count; k++)
        {
            JgsValue position = result.Type == JgsType.Number ? result : result.ElementAt(k);
            chosen[k] = pool[(int)position.AsNumber];
            if (!definition!.IsHandle)
            {
                JgsHolders.Share(chosen[k]); // the answer is a second holder of a value element
            }
        }

        return result.Type == JgsType.Number
            ? JgsObjectArray.ValueOf(definition!, chosen, 1, 1)
            : JgsObjectArray.ValueLike(definition!, chosen, result);
    }

    /// <summary>
    /// A shape verb over a categorical (open item 115): the codes go through the verb and come back
    /// under the tag, the tags of several pieces merged in order and a text piece joined as names.
    /// Null when no argument is a categorical.
    /// </summary>
    private static JgsValue? OverCategoricals(string name, IReadOnlyList<JgsValue> args, int line, int col, IJgsCallable inner)
    {
        if (!args.Any(static a => a.IsCategorical))
        {
            return null;
        }

        bool concatenation = name is "cat" or "horzcat" or "vertcat";
        int firstPiece = name == "cat" ? 1 : 0;
        JgsCategoricalTag? tag = null;
        foreach (JgsValue argument in args)
        {
            if (argument.IsCategorical)
            {
                tag = tag is null ? argument.CategoricalTag! : JgsCategorical.Merge(tag, argument.CategoricalTag!, line, col);
            }
        }

        var plain = new JgsValue[args.Count];
        for (int i = 0; i < args.Count; i++)
        {
            JgsValue argument = args[i];
            if (argument.IsCategorical)
            {
                double[] codes = JgsCategorical.Remap(JgsCategorical.CodesOf(argument), argument.CategoricalTag!, tag!);
                plain[i] = argument.IsNd
                    ? JgsMatrix.FromColumnMajorDims(codes, argument.Dims) // a third dimension keeps its place (open item 127)
                    : JgsMatrix.FromColumnMajor(codes, argument.Rows, argument.Cols);
            }
            else if (concatenation && i >= firstPiece && !(argument.Type == JgsType.Array && argument.ArrayLength == 0 && !argument.IsStringArray))
            {
                string?[] names = JgsCategorical.TextNames(argument)
                    ?? throw new JgsRuntimeException(line, col, "MATLAB:categorical:cat:TypeMismatch",
                        $"Unable to concatenate a {ClassOf(argument, JgsDialect.Matlab)} array and a categorical array.");
                (double[] codes, tag) = JgsCategorical.Absorb(tag!, names, line, col);
                plain[i] = argument.Type == JgsType.Cell || argument.IsStringArray
                    ? JgsMatrix.FromColumnMajor(codes, argument.Rows, argument.Cols)
                    : JgsMatrix.FromColumnMajor(codes, 1, codes.Length);
            }
            else
            {
                plain[i] = argument;
            }
        }

        return JgsCategorical.Wrap(inner.Call(plain, line, col), tag!);
    }

    /// <summary><c>a'</c> and <c>a.'</c> over an array of objects: the grid turned (open item 116).</summary>
    internal static JgsValue TransposeObjects(JgsValue value)
    {
        if (!value.IsObjectArray)
        {
            return value;
        }

        JgsObjectArray array = value.AsObjectArray;
        int rows = value.Rows;
        int cols = value.Cols;
        var turned = new JgsObject[array.Length];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                JgsObject element = array.Elements[r + (c * rows)];
                turned[c + (r * cols)] = element;
                if (!array.Class.IsHandle)
                {
                    JgsHolders.Share(element);
                }
            }
        }

        return JgsObjectArray.ValueOf(array.Class, turned, cols, rows);
    }

    /// <summary>
    /// The elements of an object value as values of their own: a counted share of each value-class
    /// instance, a handle as itself. What <c>arrayfun</c> and <c>num2cell</c> walk.
    /// </summary>
    internal static JgsValue[] ObjectElementValues(JgsValue value)
    {
        JgsClass definition = value.ObjectClass;
        JgsObject[] elements = value.IsObjectArray ? value.AsObjectArray.Elements : [value.AsObject];
        var values = new JgsValue[elements.Length];
        for (int i = 0; i < elements.Length; i++)
        {
            values[i] = definition.IsHandle ? JgsValue.Object(elements[i]) : JgsValue.Share(JgsValue.Object(elements[i]));
        }

        return values;
    }

    /// <summary>
    /// Objects collected by <c>arrayfun</c>, <c>cellfun</c> or <c>cell2mat</c> as one array, when
    /// every one is of the one class; null when they are not all objects. Two classes among them is
    /// R2025b's mismatch (<c>MATLAB:arrayfun:MismatchInOutputTypes</c>).
    /// </summary>
    internal static JgsValue? ObjectsAsArray(string verb, JgsValue[] values, int rows, int cols, int line, int col)
    {
        if (values.Length == 0 || !values.All(static v => v.Type == JgsType.Object))
        {
            if (values.Length > 0 && values.Any(static v => v.Type == JgsType.Object))
            {
                int at = Array.FindIndex(values, v => v.Type != values[0].Type);
                throw new JgsRuntimeException(line, col, $"MATLAB:{verb}:MismatchInOutputTypes",
                    $"Mismatch in type of outputs, at index {at + 1}, output 1 ({ClassOf(values[0], JgsDialect.Matlab)} versus {ClassOf(values[at], JgsDialect.Matlab)}). Set 'UniformOutput' to false.");
            }

            return null;
        }

        JgsClass definition = values[0].ObjectClass;
        var elements = new JgsObject[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            if (!ReferenceEquals(values[i].ObjectClass, definition))
            {
                throw new JgsRuntimeException(line, col, $"MATLAB:{verb}:MismatchInOutputTypes",
                    $"Mismatch in type of outputs, at index {i + 1}, output 1 ({definition.Name} versus {values[i].ObjectClass.Name}). Set 'UniformOutput' to false.");
            }

            if (values[i].IsObjectArray)
            {
                throw new JgsRuntimeException(line, col, $"MATLAB:{verb}:NotAScalarOutput",
                    $"Non-scalar in Uniform output, at index {i + 1}, output 1. Set 'UniformOutput' to false.");
            }

            elements[i] = values[i].AsObject;
            if (!definition.IsHandle)
            {
                JgsHolders.Share(elements[i]);
            }
        }

        return JgsObjectArray.ValueOf(definition, elements, rows, cols);
    }
}
