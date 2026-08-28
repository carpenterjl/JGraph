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
        ArgumentSpec spec, JgsValue value, int line, int col, JgsEnvironment env)
    {
        if (spec.Dims is { } dims)
        {
            value = CheckSize(spec.Name, dims, value, line, col);
        }

        return spec.ClassName is { } className ? CoerceToClass(spec.Name, className, value, line, col, env) : value;
    }

    /// <summary>
    /// Checks a value's size against a declared one, answering the value the frame should hold. A
    /// null entry is <c>:</c> and matches anything; everything else must match exactly, which is why
    /// <c>(1,1)</c> is the way a script says scalar. The one value that is refitted rather than
    /// measured is an empty — see <see cref="TryFitEmpty"/>.
    /// </summary>
    private static JgsValue CheckSize(
        string name, IReadOnlyList<Expr?> declared, JgsValue value, int line, int col)
    {
        int[] actual = SizeDims(value);

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
                throw new JgsRuntimeException(line, col,
                    $"'{name}' must be {DescribeSize(declared)}, but it is {string.Join("-by-", actual)}.");
            }
        }

        // A value with more dimensions than the declaration named is not the declared shape either:
        // (1,1) means scalar, and a 1-by-1-by-3 is not one.
        for (int i = declared.Count; i < actual.Length; i++)
        {
            if (actual[i] != 1)
            {
                throw new JgsRuntimeException(line, col,
                    $"'{name}' must be {DescribeSize(declared)}, but it is {string.Join("-by-", actual)}.");
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

    private static string DescribeSize(IReadOnlyList<Expr?> declared) =>
        string.Join("-by-", declared.Select(static d =>
            d is NumberLiteral n ? ((int)System.Math.Round(n.Value)).ToString(System.Globalization.CultureInfo.InvariantCulture) : ":"));

    /// <summary>
    /// Brings a value to its declared class. MATLAB converts where a conversion exists and refuses
    /// otherwise; JGraph converts through the class's own constructor builtin, which is the same rule
    /// expressed with the machinery already here.
    /// </summary>
    private static JgsValue CoerceToClass(
        string name, string className, JgsValue value, int line, int col, JgsEnvironment env)
    {
        if (string.Equals(ClassOf(value, JgsDialect.Matlab), className, StringComparison.Ordinal))
        {
            return value;
        }

        // The container classes have constructors that mean something else entirely — cell(3) builds
        // a 3-by-3 cell rather than converting anything — so those are checked, never converted.
        if (className is "cell" or "struct" or "table" or "function_handle" or "MException"
            || !env.TryGet(className, out JgsValue constructor) || constructor.Type != JgsType.Function)
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
                        throw new JgsRuntimeException(line, col, $"Value must be {requirement}.");
                    }
                }

                return JgsValue.Null;
            });

        Elementwise("mustBePositive", static x => x > 0, "positive");
        Elementwise("mustBeNonnegative", static x => x >= 0, "nonnegative");
        Elementwise("mustBeNegative", static x => x < 0, "negative");
        Elementwise("mustBeNonpositive", static x => x <= 0, "nonpositive");
        Elementwise("mustBeFinite", static x => !double.IsNaN(x) && !double.IsInfinity(x), "finite");
        Elementwise("mustBeNonNan", static x => !double.IsNaN(x), "a number, not NaN");
        Elementwise("mustBeNonzero", static x => x != 0, "nonzero");
        Elementwise("mustBeInteger", static x => double.IsInteger(x), "an integer");

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
                        throw new JgsRuntimeException(line, col,
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
                    throw new JgsRuntimeException(line, col,
                        $"Value must be in the range {Format(low)} to {Format(high)}.");
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
                    : throw new JgsRuntimeException(line, col, $"Value must be {requirement}.");
            });

        Whole("mustBeNumeric", IsNumericValue, "numeric");
        Whole("mustBeNumericOrLogical",
            static v => IsNumericValue(v) || IsLogicalValue(v), "numeric or logical");
        Whole("mustBeFloat", IsNumericValue, "floating-point");
        Whole("mustBeReal", static v => v.Type != JgsType.Complex, "real");
        Whole("mustBeNonempty", static v => !IsEmptyValue(v), "nonempty");
        Whole("mustBeScalarOrEmpty",
            static v => IsEmptyValue(v) || SizeDims(v).All(static d => d == 1), "scalar or empty");
        Whole("mustBeVector", static v =>
        {
            int[] dims = SizeDims(v);
            return dims.Length == 2 && (dims[0] == 1 || dims[1] == 1) && !IsEmptyValue(v);
        }, "a vector");
        Whole("mustBeText", static v => v.Type == JgsType.String || IsCellOfText(v), "text");
        Whole("mustBeTextScalar", static v => v.Type == JgsType.String, "a single piece of text");

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
                    throw new JgsRuntimeException(line, col,
                        $"Value must be one of: {string.Join(", ", allowed.Select(static a => a.Display()))}.");
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
                : throw new JgsRuntimeException(line, col,
                    $"Value must be {string.Join(" or ", wanted)}, but it is {actual}.");
        });

        Define("validateattributes", (args, line, col) =>
        {
            ArityRange("validateattributes", args, 3, 6, line, col);
            JgsValue value = args[0];
            string what = args.Count > 3 ? Str("validateattributes", args, 3, line, col) : "Input";

            // The classes list is a cell of names, any one of which the value may be.
            if (args[1].Type != JgsType.Cell)
            {
                throw new JgsRuntimeException(line, col, "validateattributes expects a cell of class names.");
            }

            string actual = ClassOf(value, JgsDialect.Matlab);
            var classes = args[1].AsCell.Select(static c => c.AsString).ToList();
            bool classOk = classes.Count == 0 || classes.Exists(c => c switch
            {
                "numeric" => IsNumericValue(value),
                "float" => IsNumericValue(value),
                "integer" => IsNumericValue(value),
                _ => string.Equals(c, actual, StringComparison.Ordinal),
            });

            if (!classOk)
            {
                throw new JgsRuntimeException(line, col,
                    $"{what} must be {string.Join(" or ", classes)}, but it is {actual}.");
            }

            if (args[2].Type != JgsType.Cell)
            {
                throw new JgsRuntimeException(line, col, "validateattributes expects a cell of attributes.");
            }

            CheckAttributes(what, value, args[2].AsCell, line, col);
            return JgsValue.Null;
        });
    }

    /// <summary>Runs <c>validateattributes</c>' attribute list, which mixes bare words with word/value pairs.</summary>
    private static void CheckAttributes(
        string what, JgsValue value, JgsValue[] attributes, int line, int col)
    {
        double[] numbers = value.Type is JgsType.String or JgsType.Cell
            ? []
            : ToDoubles("validateattributes", value, line, col);
        int[] dims = SizeDims(value);

        void Require(bool ok, string requirement)
        {
            if (!ok)
            {
                throw new JgsRuntimeException(line, col, $"{what} must be {requirement}.");
            }
        }

        for (int i = 0; i < attributes.Length; i++)
        {
            string attribute = attributes[i].Type == JgsType.String
                ? attributes[i].AsString
                : throw new JgsRuntimeException(line, col, "validateattributes: each attribute is a word.");

            // The paired attributes read the number that follows them, so the loop steps twice.
            double Paired()
            {
                if (++i >= attributes.Length)
                {
                    throw new JgsRuntimeException(line, col, $"validateattributes: '{attribute}' needs a value after it.");
                }

                return attributes[i].AsNumber;
            }

            switch (attribute)
            {
                case "positive": Require(System.Array.TrueForAll(numbers, static x => x > 0), "positive"); break;
                case "nonnegative": Require(System.Array.TrueForAll(numbers, static x => x >= 0), "nonnegative"); break;
                case "negative": Require(System.Array.TrueForAll(numbers, static x => x < 0), "negative"); break;
                case "nonpositive": Require(System.Array.TrueForAll(numbers, static x => x <= 0), "nonpositive"); break;
                case "nonzero": Require(System.Array.TrueForAll(numbers, static x => x != 0), "nonzero"); break;
                case "finite": Require(System.Array.TrueForAll(numbers, static x => !double.IsNaN(x) && !double.IsInfinity(x)), "finite"); break;
                case "nonnan": Require(System.Array.TrueForAll(numbers, static x => !double.IsNaN(x)), "free of NaN"); break;
                case "integer": Require(System.Array.TrueForAll(numbers, double.IsInteger), "made of integers"); break;
                case "real": Require(value.Type != JgsType.Complex, "real"); break;
                case "nonempty": Require(!IsEmptyValue(value), "nonempty"); break;
                case "scalar": Require(System.Array.TrueForAll(dims, static d => d == 1), "a scalar"); break;
                case "vector": Require(dims.Length == 2 && (dims[0] == 1 || dims[1] == 1), "a vector"); break;
                case "row": Require(dims.Length == 2 && dims[0] == 1, "a row vector"); break;
                case "column": Require(dims.Length == 2 && dims[1] == 1, "a column vector"); break;
                case "2d": Require(dims.Length == 2, "two-dimensional"); break;
                case "square": Require(dims.Length == 2 && dims[0] == dims[1], "square"); break;
                case "increasing": Require(IsOrdered(numbers, static (a, b) => b > a), "increasing"); break;
                case "decreasing": Require(IsOrdered(numbers, static (a, b) => b < a), "decreasing"); break;
                case "nondecreasing": Require(IsOrdered(numbers, static (a, b) => b >= a), "nondecreasing"); break;
                case "nonincreasing": Require(IsOrdered(numbers, static (a, b) => b <= a), "nonincreasing"); break;

                case ">": { double bound = Paired(); Require(System.Array.TrueForAll(numbers, x => x > bound), $"greater than {Format(bound)}"); break; }
                case ">=": { double bound = Paired(); Require(System.Array.TrueForAll(numbers, x => x >= bound), $"greater than or equal to {Format(bound)}"); break; }
                case "<": { double bound = Paired(); Require(System.Array.TrueForAll(numbers, x => x < bound), $"less than {Format(bound)}"); break; }
                case "<=": { double bound = Paired(); Require(System.Array.TrueForAll(numbers, x => x <= bound), $"less than or equal to {Format(bound)}"); break; }

                case "numel": { double count = Paired(); Require(numbers.Length == (int)count, $"{(int)count} elements long"); break; }
                case "ncols": { double count = Paired(); Require(dims.Length > 1 && dims[1] == (int)count, $"{(int)count} columns wide"); break; }
                case "nrows": { double count = Paired(); Require(dims[0] == (int)count, $"{(int)count} rows tall"); break; }

                default:
                    throw new JgsRuntimeException(line, col,
                        $"validateattributes: '{attribute}' is not an attribute it knows.");
            }
        }
    }

    private static bool IsOrdered(double[] values, Func<double, double, bool> ok)
    {
        for (int i = 1; i < values.Length; i++)
        {
            if (!ok(values[i - 1], values[i]))
            {
                return false;
            }
        }

        return true;
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

    private static string Format(double value) =>
        value.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
}
