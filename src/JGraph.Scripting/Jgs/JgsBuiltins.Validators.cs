using System.Linq;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The <c>arguments</c> block's other half (M62): the size and class check a declaration line asks
/// for, and the <c>mustBe…</c> family it names. They are ordinary builtins as well as validators,
/// because MATLAB's are — <c>mustBePositive(x)</c> on its own line is how code that predates the
/// block still checks itself.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// Checks one argument against its declared size and class, answering the value the frame should
    /// hold — which is the same value unless a class conversion or an empty's refitting happened.
    /// </summary>
    internal static JgsValue CheckArgument(
        ArgumentSpec spec, JgsValue value, int line, int col, JgsEnvironment env, Interpreter? interpreter = null, bool property = false)
    {
        if (spec.Dims is { } dims)
        {
            value = CheckSize(spec.Name, dims, value, line, col, property);
        }

        return spec.ClassName is { } className ? CoerceToClass(spec.Name, className, value, line, col, env, interpreter) : value;
    }

    /// <summary>
    /// R2025b's words for a value that does not fit a property's declared size (<c>MATLAB:validation:IncompatibleSize</c>,
    /// measured by probe <c>w15_sizes</c> over the sizes App Designer's Add Property dialog writes): "Value must be a scalar.",
    /// "Value must be a vector with N elements.", "Value must be a vector.", "Value must be a matrix of size M-by-N.",
    /// "Value must be a matrix.", "Value must be a matrix with M rows." (and "with N columns", by the same rule). Null for a
    /// declared size the probe did not cover, which keeps the argument block's words.
    /// </summary>
    private static string? PropertySizeWords(IReadOnlyList<Expr?> declared)
    {
        if (declared.Count != 2)
        {
            return null;
        }

        int? rows = declared[0] is NumberLiteral r ? (int)System.Math.Round(r.Value) : null;
        int? cols = declared[1] is NumberLiteral c ? (int)System.Math.Round(c.Value) : null;
        return (rows, cols) switch
        {
            (1, 1) => "Value must be a scalar.",
            (1, int n) => $"Value must be a vector with {n} elements.",
            (int n, 1) => $"Value must be a vector with {n} elements.",
            (1, null) or (null, 1) => "Value must be a vector.",
            (null, null) => "Value must be a matrix.",
            (int m, int n) => $"Value must be a matrix of size {m}-by-{n}.",
            (int m, null) => $"Value must be a matrix with {m} rows.",
            (null, int n) => $"Value must be a matrix with {n} columns.",
        };
    }

    /// <summary>
    /// A vector handed to a declared vector shape of the other orientation - a column to <c>(1,2)</c>, a row to <c>(:,1)</c> -
    /// is turned to the declared one, as MATLAB turns it for properties and arguments alike (measured, <c>w15_sizes</c>); null
    /// when the shapes do not fit that rule.
    /// </summary>
    private static JgsValue? TryFitVector(IReadOnlyList<Expr?> declared, JgsValue value, int[] actual, int line, int col)
    {
        if (declared.Count != 2 || actual.Length != 2 || (actual[0] != 1 && actual[1] != 1) || actual[0] * actual[1] <= 1)
        {
            return null;
        }

        int count = actual[0] * actual[1];
        bool rowDeclared = declared[0] is NumberLiteral { Value: 1 } && declared[1] is null or NumberLiteral;
        bool columnDeclared = declared[1] is NumberLiteral { Value: 1 } && declared[0] is null or NumberLiteral;
        if (rowDeclared && actual[0] != 1 && (declared[1] is not NumberLiteral n1 || (int)System.Math.Round(n1.Value) == count))
        {
            return Reshape([value, JgsValue.Number(1), JgsValue.Number(count)], line, col);
        }

        if (columnDeclared && actual[1] != 1 && (declared[0] is not NumberLiteral n0 || (int)System.Math.Round(n0.Value) == count))
        {
            return Reshape([value, JgsValue.Number(count), JgsValue.Number(1)], line, col);
        }

        return null;
    }

    /// <summary>
    /// Checks a value's size against a declared one, answering the value the frame should hold. A
    /// null entry is <c>:</c> and matches anything; everything else must match exactly, which is why
    /// <c>(1,1)</c> is the way a script says scalar. The one value that is refitted rather than
    /// measured is an empty — see <see cref="TryFitEmpty"/>.
    /// </summary>
    private static JgsValue CheckSize(
        string name, IReadOnlyList<Expr?> declared, JgsValue value, int line, int col, bool property = false)
    {
        int[] actual = SizeDims(value);
        string Refusal() => property && PropertySizeWords(declared) is { } words ? words : $"'{name}' must be {DescribeSize(declared)}, but it is {string.Join("-by-", actual)}.";

        // An empty argument is fitted to a declared size that can hold nothing rather than refused
        // (M96b). MATLAB reshapes it: f([]) against `x (1,:) double` sees a 1-by-0, and against
        // `x (:,1)` it sees a 0-by-1. Only an empty with a shape it can give up does this — the
        // shapeless 0-by-0, or a vector — so zeros(0, 3) against (1,:) is still the refusal MATLAB
        // makes of it. Without this, [] and '' stopped passing every (1,:) declaration the moment
        // they became 0-by-0.
        if (TryFitEmpty(declared, value, actual, out JgsValue fitted))
        {
            return fitted;
        }

        if (TryFitVector(declared, value, actual, line, col) is { } turned)
        {
            return turned;
        }

        for (int i = 0; i < declared.Count; i++)
        {
            if (declared[i] is null)
            {
                continue;
            }

            // A declared size is a literal in MATLAB and there is nothing else it could usefully be,
            // so anything else is named as the mistake it is rather than evaluated hopefully.
            if (declared[i] is not NumberLiteral literal)
            {
                throw new JgsRuntimeException(line, col,
                    $"'{name}': a declared size must be a number or ':'.");
            }

            int want = (int)System.Math.Round(literal.Value);
            int have = i < actual.Length ? actual[i] : 1;
            if (want != have)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:validation:IncompatibleSize", Refusal());
            }
        }

        // A value with more dimensions than the declaration named is not the declared shape either:
        // (1,1) means scalar, and a 1-by-1-by-3 is not one.
        for (int i = declared.Count; i < actual.Length; i++)
        {
            if (actual[i] != 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:validation:IncompatibleSize", Refusal());
            }
        }

        return value;
    }

    /// <summary>
    /// Whether an empty argument can be refitted to its declared size, and what it becomes if so.
    /// </summary>
    /// <remarks>
    /// Three things have to hold. The value must be an empty whose shape MATLAB is willing to turn
    /// over — a 0-by-0, which carries no shape at all, or a vector, whose orientation is often
    /// incidental. The declared size must be able to hold nothing: a <c>:</c> somewhere in it, or a
    /// literal zero. And every fixed dimension it names must be one the value can actually take.
    /// A char row has no shape of its own to set, so it is accepted as it stands.
    /// </remarks>
    private static bool TryFitEmpty(
        IReadOnlyList<Expr?> declared, JgsValue value, int[] actual, out JgsValue fitted)
    {
        fitted = value;
        bool empty = value.Type == JgsType.Array
            ? value.ArrayLength == 0
            : value.Type == JgsType.String && value.AsString.Length == 0;
        if (!empty || declared.Count == 0 || actual.Length > 2)
        {
            return false;
        }

        int rows = actual.Length > 0 ? actual[0] : 1;
        int cols = actual.Length > 1 ? actual[1] : 1;
        if (!((rows == 0 && cols == 0) || rows == 1 || cols == 1))
        {
            return false;
        }

        var wanted = new int[declared.Count];
        bool holdsNothing = false;
        for (int i = 0; i < declared.Count; i++)
        {
            switch (declared[i])
            {
                case null:
                    wanted[i] = 0;
                    holdsNothing = true;
                    break;
                case NumberLiteral literal:
                    wanted[i] = (int)System.Math.Round(literal.Value);
                    holdsNothing |= wanted[i] == 0;
                    break;
                default:
                    return false; // the ordinary path names the mistake
            }
        }

        if (!holdsNothing)
        {
            return false;
        }

        // A fixed declared dimension has to be one the value can actually take. It can when the
        // value already has that many, and when it has none along that dimension — nothing lays out
        // any number of ways. zeros(1, 0) against (2,:) has a real 1 where 2 was asked for, and
        // MATLAB refuses it where it accepts zeros(0, 1), whose 0 says nothing either way.
        for (int i = 0; i < declared.Count; i++)
        {
            if (declared[i] is null)
            {
                continue;
            }

            int have = i < actual.Length ? actual[i] : 1;
            if (have != wanted[i] && have != 0)
            {
                return false;
            }
        }

        if (value.Type == JgsType.Array)
        {
            fitted = JgsNumericClasses.Stamp(
                JgsMatrix.FromColumnMajorDims([], wanted), value.NumericClass);
        }

        return true;
    }

    /// <summary>
    /// Brings an <c>arguments</c> line's value to its declared class (stage K9): converted as the
    /// class's constructor converts it, and a value that cannot be refused in R2025b's words after
    /// <paramref name="prefix"/>. A graphics class keeps its own refusals.
    /// </summary>
    internal static JgsValue ConvertDeclaredClass(
        string className, JgsValue value, string prefix, int line, int col, JgsEnvironment env, Interpreter? interpreter)
    {
        if (string.Equals(ClassOf(value, JgsDialect.Matlab), className, StringComparison.Ordinal))
        {
            return value;
        }

        JgsRuntimeException Refused() => new(line, col, "MATLAB:validation:UnableToConvert",
            $"{prefix}Value must be of type {className} or be convertible to {className}.");

        // An enumeration takes its members and their names (stage K9).
        if (interpreter?.ClassForLoad(className) is { IsEnumeration: true } enumeration)
        {
            return ToEnumerationMember(enumeration, value, prefix, line, col);
        }

        // The built-in classes convert by R2025b's rules for a declared class (k9_argument_messages),
        // which are its assignment's rather than its constructors': int8("7") is refused there,
        // logical('A') is true, char(@sin) is 'sin'.
        bool numeric = IsNumericValue(value);
        bool logical = IsLogicalValue(value);
        bool chars = value.Type == JgsType.String;
        bool strings = value.IsStringArray;
        bool complex = value.Type == JgsType.Complex;
        switch (className)
        {
            case "double" or "single" or "int8" or "uint8" or "int16" or "uint16" or "int32" or "uint32" or "int64" or "uint64":
                if (strings ? className != "double" : !(numeric || logical || chars))
                {
                    throw Refused();
                }

                break;
            case "logical":
                if (!(numeric || logical || chars) || complex || (numeric && ToDoubles(className, value, line, col).Any(double.IsNaN)))
                {
                    throw Refused();
                }

                if (chars)
                {
                    value = Stamped([.. CodesOf(value).Select(static c => c != 0 ? 1.0 : 0.0)], SizeDims(value), JgsNumericClass.Double);
                }

                break;
            case "char":
                if (value.Type == JgsType.Function)
                {
                    return JgsValue.Str(SourceTextOf("char", value, line, col, true));
                }

                if (logical || complex || !(numeric || strings))
                {
                    throw Refused();
                }

                if (numeric)
                {
                    // A negative code or NaN is character 0 in R2025b's conversion.
                    value = Stamped(ToDoubles(className, value, line, col).Select(static c => double.IsNaN(c) || c < 0 ? 0 : c).ToArray(), SizeDims(value), JgsNumericClass.Double);
                }

                break;
            case "string":
                if (value.Type is JgsType.Struct or JgsType.Function or JgsType.Object)
                {
                    throw Refused();
                }

                break;
            case "cell" or "struct" or "function_handle":
                throw Refused();
        }

        try
        {
            return CoerceToClass(className, className, value, line, col, env, interpreter);
        }
        catch (JgsRuntimeException failure) when (!failure.Identifier.StartsWith("MATLAB:graphics:", StringComparison.Ordinal))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validation:UnableToConvert",
                $"{prefix}Value must be of type {className} or be convertible to {className}.");
        }
    }

    /// <summary>Column-major <paramref name="values"/> of <paramref name="dims"/> in <paramref name="numericClass"/>.</summary>
    private static JgsValue Stamped(double[] values, int[] dims, JgsNumericClass numericClass) =>
        JgsNumericClasses.Stamp(JgsMatrix.FromColumnMajorDims(values, dims), numericClass);

    /// <summary>A scalar tiled to <paramref name="dims"/>, as the session's <c>repmat</c> tiles it, whatever its class.</summary>
    internal static JgsValue RepeatScalar(JgsValue value, int[] dims, JgsEnvironment env, int line, int col) =>
        CallSessionBuiltin(env, "repmat", [value, JgsGraphicsProperties.Row([.. dims.Select(static d => (double)d)])], line, col);

    /// <summary>A value laid out as <paramref name="dims"/>, as the session's <c>reshape</c> lays it out, whatever its class.</summary>
    internal static JgsValue Reshaped(JgsValue value, int[] dims, JgsEnvironment env, int line, int col) =>
        CallSessionBuiltin(env, "reshape", [value, JgsGraphicsProperties.Row([.. dims.Select(static d => (double)d)])], line, col);

    /// <summary>
    /// The value a property declared with a built-in class and no default starts with in a class file (open
    /// item 309, ADR 0353, measured by probe p309b): the class's zeros - false, character 0, "", {[]} - at the
    /// declared size, a ':' holding none and no size a 0-by-0; a scalar struct with no fields. Null for a class
    /// this build makes no such value of (datetime, duration, a function handle, a table), which keeps the
    /// older reading.
    /// </summary>
    internal static JgsValue? ImplicitPropertyDefault(string className, IReadOnlyList<Expr?>? declared, JgsEnvironment env, int line, int col)
    {
        int[] dims = declared is null ? [0, 0] : new int[declared.Count];
        for (int i = 0; declared is not null && i < declared.Count; i++)
        {
            switch (declared[i])
            {
                case null:
                    dims[i] = 0;
                    break;
                case NumberLiteral literal:
                    dims[i] = (int)System.Math.Round(literal.Value);
                    break;
                default:
                    return null;
            }
        }

        if (dims.Length < 2)
        {
            return null;
        }

        JgsValue[] size = [.. dims.Select(static d => JgsValue.Number(d))];
        int count = dims.Aggregate(1, static (a, b) => a * b);
        if (JgsNumericClasses.Parse(className) is { } numericClass)
        {
            return JgsNumericClasses.Stamp(JgsMatrix.FromColumnMajorDims(new double[count], dims), numericClass);
        }

        return className switch
        {
            "logical" => CallSessionBuiltin(env, "false", size, line, col),
            "char" => CallSessionBuiltin(env, "char", [JgsMatrix.FromColumnMajorDims(new double[count], dims)], line, col),
            "string" => CallSessionBuiltin(env, "strings", size, line, col),
            "cell" => CallSessionBuiltin(env, "cell", size, line, col),
            "struct" when count == 1 && dims.Length == 2 => JgsValue.EmptyStruct(),
            _ => null,
        };
    }

    /// <summary>
    /// The function <paramref name="name"/> as <paramref name="env"/> binds it: the session's
    /// builtin with the wrappers that teach it chars, strings and the rest.
    /// </summary>
    private static JgsValue CallSessionBuiltin(JgsEnvironment env, string name, JgsValue[] args, int line, int col) =>
        env.TryGet(name, out JgsValue bound) && bound.Type == JgsType.Function
            ? bound.AsCallable.Call(args, line, col)
            : throw new JgsRuntimeException(line, col, $"'{name}' is not available here.");

    /// <summary>The character codes of a char array, column-major.</summary>
    private static double[] CodesOf(JgsValue value) => [.. value.AsString.Select(static c => (double)c)];

    private static string DescribeSize(IReadOnlyList<Expr?> declared) =>
        string.Join("-by-", declared.Select(static d =>
            d is NumberLiteral n ? ((int)System.Math.Round(n.Value)).ToString(System.Globalization.CultureInfo.InvariantCulture) : ":"));

    /// <summary>
    /// Brings a value to its declared class. MATLAB converts where a conversion exists and refuses
    /// otherwise; JGraph converts through the class's own constructor builtin, which is the same rule
    /// expressed with the machinery already here.
    /// </summary>
    private static JgsValue CoerceToClass(
        string name, string className, JgsValue value, int line, int col, JgsEnvironment env, Interpreter? interpreter)
    {
        if (string.Equals(ClassOf(value, JgsDialect.Matlab), className, StringComparison.Ordinal))
        {
            return value;
        }

        // A graphics class takes the handles of its objects (U7): matlab.ui.Figure, a figure's number.
        if (JgsGraphicsClasses.IsGraphicsType(className))
        {
            return JgsGraphicsClasses.Fitting(value, className, out double stray) switch
            {
                JgsGraphicsClasses.Fit.Fits => value,
                JgsGraphicsClasses.Fit.NotAHandle => throw new JgsRuntimeException(line, col, "MATLAB:graphics:CannotConvertDoubleToHandle",
                    $"Cannot convert double value {Format(stray)} to a handle"),
                _ => throw new JgsRuntimeException(line, col, "MATLAB:validation:UnableToConvert",
                    $"Value must be of type {className} or be convertible to {className}."),
            };
        }

        // An instance of a subclass is an instance of the class asked for (U6), and keeps its own class.
        if (value.Type == JgsType.Object && value.AsObject.Class.IsA(className))
        {
            return value;
        }

        // A property typed as an enumeration takes a member's name (W7c: R2025b's generator writes a member so).
        if (interpreter?.ClassForLoad(className) is { IsEnumeration: true } enumeration)
        {
            return ToEnumerationMember(enumeration, value, string.Empty, line, col);
        }

        // The container classes have constructors that mean something else entirely — cell(3) builds
        // a 3-by-3 cell rather than converting anything — so those are checked, never converted. A
        // user class converts through its constructor, as R2025b's does (U6, measured).
        JgsValue? constructor = null;
        if (className is not ("cell" or "struct" or "table" or "function_handle" or "MException"))
        {
            constructor = env.TryGet(className, out JgsValue bound) && bound.Type == JgsType.Function
                ? bound
                : interpreter?.ClassForLoad(className)?.ConstructorValue;
        }

        if (constructor is null)
        {
            throw new JgsRuntimeException(line, col,
                $"'{name}' must be {className}, but it is {ClassOf(value, JgsDialect.Matlab)}.");
        }

        try
        {
            return constructor.AsCallable.Call([value], line, col);
        }
        catch (JgsRuntimeException)
        {
            throw new JgsRuntimeException(line, col,
                $"'{name}' must be {className}, and a {ClassOf(value, JgsDialect.Matlab)} cannot be converted to one.");
        }
    }

    /// <summary>Declares the <c>mustBe…</c> family and <c>validateattributes</c>.</summary>
    private static void RegisterValidators(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define)
    {
        // Every one-value validator is the same shape: a question asked of each element, and the
        // sentence to say when an element answers no.
        void Elementwise(string name, Func<double, bool> ok, string requirement) =>
            Define(name, (args, line, col) =>
            {
                Arity(name, args, 1, line, col);
                foreach (double x in ToDoubles(name, args[0], line, col))
                {
                    if (!ok(x))
                    {
                        throw new JgsRuntimeException(line, col, ValidatorId(name),
                            Requires(requirement));
                    }
                }

                return JgsValue.Null;
            });

        Elementwise("mustBePositive", static x => x > 0, "positive");
        Elementwise("mustBeNonnegative", static x => x >= 0, "nonnegative");
        Elementwise("mustBeNegative", static x => x < 0, "negative");
        Elementwise("mustBeNonpositive", static x => x <= 0, "not positive");
        Elementwise("mustBeFinite", static x => !double.IsNaN(x) && !double.IsInfinity(x), "finite");
        Elementwise("mustBeNonNan", static x => !double.IsNaN(x), "not NaN");
        Elementwise("mustBeNonzero", static x => x != 0, "not zero");
        Elementwise("mustBeInteger", static x => double.IsInteger(x), "integer");

        // The comparisons take the bound as their second argument and hold elementwise against it,
        // so mustBeGreaterThan(v, 0) is a whole-vector check rather than a scalar one.
        void Compare(string name, Func<double, double, bool> ok, string requirement) =>
            Define(name, (args, line, col) =>
            {
                Arity(name, args, 2, line, col);
                double bound = Num(name, args, 1, line, col);
                foreach (double x in ToDoubles(name, args[0], line, col))
                {
                    if (!ok(x, bound))
                    {
                        throw new JgsRuntimeException(line, col, ValidatorId(name),
                            $"Value must be {requirement} {Format(bound)}.");
                    }
                }

                return JgsValue.Null;
            });

        Compare("mustBeGreaterThan", static (x, b) => x > b, "greater than");
        Compare("mustBeLessThan", static (x, b) => x < b, "less than");
        Compare("mustBeGreaterThanOrEqual", static (x, b) => x >= b, "greater than or equal to");
        Compare("mustBeLessThanOrEqual", static (x, b) => x <= b, "less than or equal to");

        Define("mustBeInRange", (args, line, col) =>
        {
            ArityRange("mustBeInRange", args, 3, 5, line, col);
            double low = Num("mustBeInRange", args, 1, line, col);
            double high = Num("mustBeInRange", args, 2, line, col);

            // 'exclusive' words follow the bounds and say which end is open; the default is closed.
            bool openLow = false;
            bool openHigh = false;
            for (int i = 3; i < args.Count; i++)
            {
                switch (Str("mustBeInRange", args, i, line, col))
                {
                    case "exclude-lower": openLow = true; break;
                    case "exclude-upper": openHigh = true; break;
                    case "inclusive": break;
                    case "exclusive": openLow = openHigh = true; break;
                    case var other:
                        throw new JgsRuntimeException(line, col,
                            $"mustBeInRange: '{other}' is not 'inclusive', 'exclusive', 'exclude-lower' or 'exclude-upper'.");
                }
            }

            foreach (double x in ToDoubles("mustBeInRange", args[0], line, col))
            {
                bool ok = (openLow ? x > low : x >= low) && (openHigh ? x < high : x <= high);
                if (!ok)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeInRange",
                        $"Value must be greater than {(openLow ? string.Empty : "or equal to ")}"
                        + $"{Format(low)}, and less than {(openHigh ? string.Empty : "or equal to ")}"
                        + $"{Format(high)}.");
                }
            }

            return JgsValue.Null;
        });

        // The kind checks ask about the value as a whole rather than element by element.
        void Whole(string name, Func<JgsValue, bool> ok, string requirement) =>
            Define(name, (args, line, col) =>
            {
                Arity(name, args, 1, line, col);
                return ok(args[0])
                    ? JgsValue.Null
                    : throw new JgsRuntimeException(line, col, ValidatorId(name),
                        Requires(requirement));
            });

        Whole("mustBeNumeric", IsNumericValue, "numeric");
        Whole("mustBeNumericOrLogical",
            static v => IsNumericValue(v) || IsLogicalValue(v), "numeric or logical");
        // A float is a double or a single and nothing else: an integer class is numeric but
        // not floating-point, which is the whole point of the name.
        Whole("mustBeFloat",
            static v => IsNumericValue(v)
                && v.NumericClass is JgsNumericClass.Double or JgsNumericClass.Single,
            "a floating-point array");
        Whole("mustBeReal", static v => v.Type != JgsType.Complex, "real");
        Whole("mustBeNonempty", static v => !IsEmptyValue(v), "not empty");
        Whole("mustBeScalarOrEmpty",
            static v => IsEmptyValue(v) || SizeDims(v).All(static d => d == 1), "scalar or empty");
        Whole("mustBeVector", static v =>
        {
            int[] dims = SizeDims(v);
            return dims.Length == 2 && (dims[0] == 1 || dims[1] == 1) && !IsEmptyValue(v);
        }, "a 1-by-n vector or an n-by-1 vector");
        Whole("mustBeText", static v => v.Type == JgsType.String || IsCellOfText(v),
            "a character vector, string array, or cell array of character vectors");
        Whole("mustBeTextScalar", static v => v.Type == JgsType.String,
            "a character vector or string scalar");

        Define("mustBeMember", (args, line, col) =>
        {
            Arity("mustBeMember", args, 2, line, col);
            JgsValue[] allowed = args[1].Type == JgsType.Cell
                ? args[1].AsCell
                : args[1].Type == JgsType.Array ? args[1].BoxedElements().ToArray() : [args[1]];

            foreach (JgsValue element in Members(args[0]))
            {
                if (!System.Array.Exists(allowed, candidate => JgsValue.AreEqual(element, candidate)))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeMember",
                        "Value must be a member of this set:\n"
                        + string.Concat(allowed.Select(static a => "    "
                            + (a.Type == JgsType.String || IsStringScalar(a)
                                ? "'" + TextOf(a) + "'"
                                : a.Display()) + "\n")));
                }
            }

            return JgsValue.Null;
        });

        Define("mustBeA", (args, line, col) =>
        {
            Arity("mustBeA", args, 2, line, col);
            string[] wanted = args[1].Type == JgsType.Cell
                ? args[1].AsCell.Select(static c => c.AsString).ToArray()
                : [Str("mustBeA", args, 1, line, col)];

            string actual = ClassOf(args[0], JgsDialect.Matlab);
            return System.Array.Exists(wanted, w => string.Equals(w, actual, StringComparison.Ordinal))
                ? JgsValue.Null
                : throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeA",
                    "Value must be one of these types: "
                    + string.Join(", ", wanted.Select(static w => $"'{w}'")) + ".");
        });

        // M104's four. Each is the same shape as the family above it — a question asked of the
        // value, and MATLAB's own sentence when the answer is no — except that the three that read
        // text ask first whether there is any, and say so in mustBeNonzeroLengthText's words.
        Whole("mustBeNonsparse", static v => v.Type != JgsType.Sparse, "not sparse");

        Define("mustBeValidVariableName", (args, line, col) =>
            NamesMustHold("mustBeValidVariableName", args,
                static name => IsValidVariableName(name),
                "The following are not valid variable names", line, col));

        Define("mustBeFile", (args, line, col) =>
            NamesMustHold("mustBeFile", args,
                static path => System.IO.File.Exists(path),
                "The following files do not exist", line, col));

        Define("mustBeFolder", (args, line, col) =>
            NamesMustHold("mustBeFolder", args,
                static path => System.IO.Directory.Exists(path),
                "The following folders do not exist", line, col));
    }

    /// <summary>The elements <c>mustBeMember</c> checks: a cell's entries, an array's, or the value itself.</summary>
    private static IEnumerable<JgsValue> Members(JgsValue value) => value.Type switch
    {
        JgsType.Cell => value.AsCell,
        JgsType.Array => value.BoxedElements(),
        _ => [value],
    };

    private static bool IsCellOfText(JgsValue value) =>
        value.Type == JgsType.Cell && System.Array.TrueForAll(value.AsCell, static c => c.Type == JgsType.String);

    private static bool IsEmptyValue(JgsValue value) => value.Type switch
    {
        JgsType.Array => value.ArrayLength == 0,
        JgsType.Cell => value.AsCell.Length == 0,
        JgsType.String => value.AsString.Length == 0,
        JgsType.Null => true,
        _ => false,
    };

    /// <summary>
    /// A validator's sentence. MATLAB puts the negative before the verb — "Value must not be
    /// empty", not "Value must be not empty" — so a requirement written as a negation moves the
    /// word rather than being pasted after "be".
    /// </summary>
    private static string Requires(string requirement) =>
        requirement.StartsWith("not ", StringComparison.Ordinal)
            ? $"Value must not be {requirement[4..]}."
            : $"Value must be {requirement}.";

    /// <summary>The identifier MathWorks documents for a validator, which is its name qualified.</summary>
    private static string ValidatorId(string name) => "MATLAB:validators:" + name;

    /// <summary>
    /// The shape the three text-reading validators share: every piece of text must answer
    /// <paramref name="holds"/>, and the ones that do not are named together in one sentence. A
    /// value that is not text, or is text with no characters in it, fails the earlier check MATLAB
    /// makes first — <c>mustBeNonzeroLengthText</c>'s.
    /// </summary>
    private static JgsValue NamesMustHold(
        string name, IReadOnlyList<JgsValue> args, Func<string, bool> holds, string lead,
        int line, int col)
    {
        Arity(name, args, 1, line, col);
        string[]? texts = TextElementsOf(args[0]);
        if (texts is null || texts.Length == 0 || System.Array.Exists(texts, static t => t.Length == 0))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeNonzeroLengthText",
                "Value must be text with one or more characters.");
        }

        string[] failed = [.. texts.Where(t => !holds(t)).Select(static t => $"'{t}'")];
        return failed.Length == 0
            ? JgsValue.Null
            : throw new JgsRuntimeException(line, col, ValidatorId(name),
                $"{lead}: {JoinWithAnd(failed)}.");
    }

    /// <summary>
    /// MATLAB's own way of listing things in a sentence: two are joined with <c>and</c>, three or
    /// more with commas and a final <c>, and</c>.
    /// </summary>
    private static string JoinWithAnd(IReadOnlyList<string> items) => items.Count switch
    {
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => $"{string.Join(", ", items.Take(items.Count - 1))}, and {items[^1]}",
    };

    private static string Format(double value) =>
        value.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
}
