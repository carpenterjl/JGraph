using System.Globalization;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>validateattributes(A, classes, attributes, [funcName | argIndex], [varName], [argIndex])</c> as
/// R2025b answers it (open item 309, ADR 0353): the classes as a cell of names or a string array (a char
/// row is one name), then each attribute in the order given, each refusal in R2025b's catalog words
/// (<c>resources/MATLAB/en/validateattributes.xml</c>) under its identifier - <c>MATLAB:expectedPositive</c>,
/// <c>MATLAB:invalidType</c>, <c>MATLAB:incorrectSize</c> and the rest, <c>MATLAB:f:expectedPositive</c>
/// when a function name is given. The input is named "input", the variable, "input number N", or
/// "input number N, x,". Every case was measured by probe <c>p309</c>.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>The classes R2025b's message lists for <c>'numeric'</c>.</summary>
    private const string NumericClassList = "double, single, uint8, uint16, uint32, uint64, int8, int16, int32, int64";

    private static JgsValue ValidateAttributes(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        if (args.Count > 6)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:maxrhs", "Too many input arguments.");
        }

        JgsValue value = args[0];
        (string function, string variable, int? position) = ValidateAttributesNames(args, line, col);
        string who = (variable.Length > 0, position) switch
        {
            (false, null) => "input",
            (true, null) => variable,
            (false, { } n) => $"input number {n.ToString(CultureInfo.InvariantCulture)}",
            (true, { } n) => $"input number {n.ToString(CultureInfo.InvariantCulture)}, {variable},",
        };
        string Id(string id) => function.Length > 0 ? $"MATLAB:{function}:{id}" : $"MATLAB:{id}";

        string[] classes = ValidateAttributesClasses(args[1], line, col);
        string actual = ClassOf(value, JgsDialect.Matlab);
        if (!classes.Any(c => ValueIsA(value, c, actual, line, col)))
        {
            // R2025b leaves the second sentence off for text, a char array or a cell of them (probe p309).
            string listed = string.Join(", ", classes.Select(static c => c == "numeric" ? NumericClassList : c));
            bool text = (value.Type == JgsType.String && !value.IsStringArray) || IsCellOfText(value);
            throw new JgsRuntimeException(line, col, Id("invalidType"),
                $"Expected {who} to be one of these types:\n\n{listed}" + (text ? string.Empty : $"\n\nInstead its type was {actual}."));
        }

        JgsValue[] attributes = args[2] switch
        {
            { Type: JgsType.Cell } cell => cell.AsCell,
            { IsStringArray: true } strings => [.. strings.BoxedElements()],
            _ => throw new JgsRuntimeException(line, col, "MATLAB:validateattributes:badAttributeList",
                "Valid attributes must be a cell array or a string array."),
        };
        CheckValidateAttributes(value, attributes, who, Id, line, col);
        return JgsValue.Null;
    }

    /// <summary>The function name, variable name and argument position after the attributes, refused in R2025b's words.</summary>
    private static (string Function, string Variable, int? Position) ValidateAttributesNames(IReadOnlyList<JgsValue> args, int line, int col)
    {
        string function = string.Empty, variable = string.Empty;
        int? position = null;

        int? Position(JgsValue given)
        {
            if (given.Type is JgsType.Array && given.ArrayLength == 0 && !given.IsStringArray)
            {
                return null;
            }

            if (!(IsNumericValue(given) || IsLogicalValue(given)) || NumelOf(given) != 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:validateattributes:badArgPositionClass",
                    "The last argument must be a numeric scalar or empty, to specify argument position.");
            }

            double n = ToDoubles("validateattributes", given, line, col)[0];
            return n >= 1 && double.IsInteger(n)
                ? (int)n
                : throw new JgsRuntimeException(line, col, "MATLAB:validateattributes:badArgPosition", "The last argument must be a positive integer.");
        }

        if (args.Count >= 4)
        {
            if (args.Count == 4 && (IsNumericValue(args[3]) || IsLogicalValue(args[3])))
            {
                position = Position(args[3]);
            }
            else if (IsTextScalar(args[3]))
            {
                function = TextOf(args[3]);
            }
            else
            {
                throw new JgsRuntimeException(line, col, "MATLAB:validateattributes:badFunctionName",
                    "Fourth argument must be an ASCII string scalar or character vector to specify function name, or a scalar double to specify argument position.");
            }
        }

        if (args.Count >= 5)
        {
            variable = IsTextScalar(args[4])
                ? TextOf(args[4])
                : throw new JgsRuntimeException(line, col, "MATLAB:validateattributes:badVariableName",
                    "Fifth argument must be a string scalar or character vector to specify input variable name.");
        }

        if (args.Count == 6)
        {
            position = Position(args[5]);
        }

        return (function, variable, position);
    }

    /// <summary>The class names: a cell of char rows, a string array, or a char row naming one; anything else is badClassList.</summary>
    private static string[] ValidateAttributesClasses(JgsValue classes, int line, int col)
    {
        JgsRuntimeException Bad() => new(line, col, "MATLAB:validateattributes:badClassList",
            "Valid data types must be a string vector or cell array of character vectors.");

        if (classes.Type == JgsType.Cell)
        {
            return [.. classes.AsCell.Select(c => IsTextScalar(c) ? TextOf(c) : throw Bad())];
        }

        if (classes.IsStringArray)
        {
            return [.. classes.BoxedElements().Select(static s => s.AsString)];
        }

        if (classes.Type == JgsType.String)
        {
            return [classes.AsString];
        }

        throw Bad();
    }

    /// <summary>Whether <paramref name="value"/> is of the class or category <paramref name="name"/>, as <c>isa</c> answers.</summary>
    private static bool ValueIsA(JgsValue value, string name, string actual, int line, int col)
    {
        if (string.Equals(name, actual, StringComparison.Ordinal))
        {
            return true;
        }

        if (JgsLifetime.Current?.Interpreter is { } interpreter
            && interpreter.Globals.TryGet("isa", out JgsValue isa) && isa.Type == JgsType.Function)
        {
            return isa.AsCallable.Call([value, JgsValue.Str(name)], line, col) is { } answer && IsTruthy(answer);
        }

        JgsNumericClass? numeric = JgsNumericClasses.Parse(actual);
        return name switch
        {
            "numeric" => numeric is not null,
            "float" => numeric is JgsNumericClass.Double or JgsNumericClass.Single,
            "integer" => numeric?.IsInteger() == true,
            _ => false,
        };
    }

    /// <summary>The attribute list, read in order: a bare word, or a word and the value after it.</summary>
    private static void CheckValidateAttributes(
        JgsValue value, JgsValue[] attributes, string who, Func<string, string> id, int line, int col)
    {
        int[] dims = SizeDims(value);
        bool numericLike = IsNumericValue(value) || IsLogicalValue(value);
        double[]? numbers = null;
        double[] Numbers() => numbers ??= numericLike ? ToDoubles("validateattributes", value, line, col) : [];

        void Require(bool ok, string identifier, string phrase)
        {
            if (!ok)
            {
                throw new JgsRuntimeException(line, col, id(identifier), $"Expected {who} to be {phrase}.");
            }
        }

        // A numeric attribute fails a value that is not numeric (probe p309: 'a', {1} and false are not positive).
        bool All(Func<double, bool> holds) => numericLike && System.Array.TrueForAll(Numbers(), x => holds(x));

        bool Ordered(Func<double, double, bool> holds)
        {
            if (!numericLike)
            {
                return false;
            }

            double[] values = Numbers();
            for (int k = 1; k < values.Length; k++)
            {
                if (!holds(values[k - 1], values[k]))
                {
                    return false;
                }
            }

            return true;
        }

        for (int i = 0; i < attributes.Length; i++)
        {
            JgsValue word = attributes[i];
            if (!IsTextScalar(word))
            {
                ValidateAttributesWarning("MATLAB:validateattributes:attributeBadClass",
                    $"Attribute of class {ClassOf(word, JgsDialect.Matlab)} not supported.");
                continue;
            }

            string attribute = TextOf(word);

            // The word's value: refused when missing, and read as R2025b reads it.
            JgsValue Following()
            {
                if (i + 1 >= attributes.Length)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:validateattributes:notEnoughArguments",
                        $"Not enough arguments for attribute {attribute}.");
                }

                return attributes[++i];
            }

            switch (attribute)
            {
                case "scalar": Require(System.Array.TrueForAll(dims, static d => d == 1), "expectedScalar", "a scalar"); break;
                case "vector": Require(dims.Length == 2 && (dims[0] == 1 || dims[1] == 1), "expectedVector", "a vector"); break;
                case "row": Require(dims.Length == 2 && dims[0] == 1, "expectedRow", "a row vector"); break;
                case "column": Require(dims.Length == 2 && dims[1] == 1, "expectedColumn", "a column vector"); break;
                case "square": Require(dims.Length == 2 && dims[0] == dims[1], "expectedSquare", "a square matrix"); break;
                case "2d": Require(dims.Length == 2, "expected2D", "two-dimensional"); break;
                case "3d": Require(dims.Length <= 3, "expected3D", "three-dimensional"); break;
                case "nonempty": Require(dims.All(static d => d > 0), "expectedNonempty", "nonempty"); break;
                case "nonsparse": Require(value.Type != JgsType.Sparse, "expectedNonsparse", "nonsparse"); break;
                case "real": Require(value.Type != JgsType.Complex, "expectedReal", "real"); break;
                case "positive": Require(All(static x => x > 0), "expectedPositive", "positive"); break;
                case "nonnegative": Require(All(static x => x >= 0), "expectedNonnegative", "nonnegative"); break;
                case "nonzero": Require(All(static x => x != 0), "expectedNonZero", "nonzero"); break;
                case "integer": Require(All(static x => double.IsFinite(x) && double.IsInteger(x)), "expectedInteger", "integer-valued"); break;
                case "finite": Require(All(double.IsFinite), "expectedFinite", "finite"); break;
                case "nonnan": Require(All(static x => !double.IsNaN(x)), "expectedNonNaN", "non-NaN"); break;
                case "even": Require(All(static x => double.IsFinite(x) && double.IsInteger(x) && x % 2 == 0), "expectedEven", "even"); break;
                case "odd": Require(All(static x => double.IsFinite(x) && double.IsInteger(x) && Math.Abs(x % 2) == 1), "expectedOdd", "odd"); break;
                case "binary": Require(All(static x => x is 0 or 1), "expectedBinary", "binary"); break;
                case "diag": Require(dims.Length == 2 && IsDiagonal(Numbers(), dims[0], dims[1]) && numericLike, "expectedDiag", "a diagonal matrix"); break;
                case "increasing": Require(Ordered(static (a, b) => b > a), "expectedIncreasing", "strictly increasing"); break;
                case "decreasing": Require(Ordered(static (a, b) => b < a), "expectedDecreasing", "strictly decreasing"); break;
                case "nondecreasing": Require(Ordered(static (a, b) => b >= a), "expectedNondecreasing", "monotonically increasing"); break;
                case "nonincreasing": Require(Ordered(static (a, b) => b <= a), "expectedNonincreasing", "monotonically decreasing"); break;
                case "scalartext":
                    Require(IsScalarText(value, dims), "expectedScalartext", "a non-missing string scalar or character vector");
                    break;

                case ">" or ">=" or "<" or "<=":
                {
                    JgsValue boundValue = Following();
                    if (!(IsNumericValue(boundValue) || IsLogicalValue(boundValue)) || NumelOf(boundValue) != 1 || boundValue.Type == JgsType.Complex)
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:validateattributes:badComparison", $"Value for {attribute} must be a real scalar.");
                    }

                    if (!numericLike)
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:validateattributes:UnsupportedTypeForComparison",
                            $"Type {ClassOf(value, JgsDialect.Matlab)} is not supported in VALIDATEATTRIBUTES for {attribute}.");
                    }

                    double bound = ToDoubles("validateattributes", boundValue, line, col)[0];
                    (Func<double, bool> holds, string identifier) = attribute switch
                    {
                        ">" => ((Func<double, bool>)(x => x > bound), "notGreater"),
                        ">=" => (x => x >= bound, "notGreaterEqual"),
                        "<" => (x => x < bound, "notLess"),
                        _ => (x => x <= bound, "notLessEqual"),
                    };
                    Require(All(holds), identifier, $"an array with all of the values {attribute} {AttributeNumber(bound)}");
                    break;
                }

                case "size":
                {
                    JgsValue sizeValue = Following();
                    double[] wanted = IsNumericValue(sizeValue) && sizeValue.Type != JgsType.Complex
                        ? ToDoubles("validateattributes", sizeValue, line, col)
                        : [];
                    if (wanted.Length == 0 || wanted.Any(static d => !double.IsNaN(d) && (!double.IsInteger(d) || d < 0)))
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:validateattributes:badSizeArray",
                            "Argument 'size' must be followed by a real array of integers.");
                    }

                    bool fits = true;
                    for (int d = 0; d < Math.Max(wanted.Length, dims.Length); d++)
                    {
                        double want = d < wanted.Length ? wanted[d] : 1;
                        int have = d < dims.Length ? dims[d] : 1;
                        fits &= double.IsNaN(want) || want == have;
                    }

                    if (!fits)
                    {
                        string[] unknowns = ["M", "N", "P", "Q", "R", "S"];
                        int unknown = 0;
                        string expected = string.Join("x", wanted.Select(w => double.IsNaN(w)
                            ? unknowns[Math.Min(unknown++, unknowns.Length - 1)]
                            : ((long)w).ToString(CultureInfo.InvariantCulture)));
                        throw new JgsRuntimeException(line, col, id("incorrectSize"),
                            $"Expected {who} to be of size {expected}, but it is of size {string.Join("x", dims)}.");
                    }

                    break;
                }

                case "numel" or "ncols" or "nrows" or "ndims":
                {
                    JgsValue countValue = Following();
                    double count = IsNumericValue(countValue) && NumelOf(countValue) == 1 && countValue.Type != JgsType.Complex
                        ? ToDoubles("validateattributes", countValue, line, col)[0]
                        : double.NaN;
                    bool good = attribute == "ndims" ? count >= 2 && double.IsInteger(count) : count >= 0 && double.IsInteger(count);
                    if (!good)
                    {
                        throw attribute == "ndims"
                            ? new JgsRuntimeException(line, col, "MATLAB:validateattributes:badDimsArray",
                                "Argument 'ndims' must be followed by a real integer value greater than 1.")
                            : new JgsRuntimeException(line, col, $"MATLAB:validateattributes:bad{attribute}",
                                $"Argument '{attribute}' must be followed by a nonnegative real scalar integer.");
                    }

                    (int have, string identifier, string phrase) = attribute switch
                    {
                        "numel" => (dims.Aggregate(1, static (a, b) => a * b), "incorrectNumel", "number of elements"),
                        "ncols" => (dims.Length > 1 ? dims.Skip(1).Aggregate(1, static (a, b) => a * b) : 1, "incorrectNumcols", "number of columns"),
                        "nrows" => (dims[0], "incorrectNumrows", "number of rows"),
                        _ => (dims.Length, "incorrectNumdims", "number of dimensions"),
                    };
                    Require(have == (long)count, identifier, $"an array with {phrase} equal to {((long)count).ToString(CultureInfo.InvariantCulture)}");
                    break;
                }

                default:
                    ValidateAttributesWarning("MATLAB:validateattributes:attributeNotFound", $"Attribute {attribute} not supported.");
                    break;
            }
        }
    }

    /// <summary>A char row or <c>''</c>, or a string scalar that is not missing (probe p309: <c>''</c> passes, a column does not).</summary>
    private static bool IsScalarText(JgsValue value, int[] dims)
    {
        if (value.IsStringArray)
        {
            return NumelOf(value) == 1 && !IsMissingString(value);
        }

        return value.Type == JgsType.String && dims.Length == 2 && (dims[0] == 1 || (dims[0] == 0 && dims[1] == 0));
    }

    private static bool IsDiagonal(double[] values, int rows, int cols)
    {
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                if (r != c && c * rows + r < values.Length && values[c * rows + r] != 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>A bound as R2025b's message writes it: 5, 5.5.</summary>
    private static string AttributeNumber(double value) => value.ToString("G15", CultureInfo.InvariantCulture);

    /// <summary>The number of elements of a value.</summary>
    private static int NumelOf(JgsValue value) => SizeDims(value).Aggregate(1, static (a, b) => a * b);

    /// <summary>An attribute R2025b does not know is a warning, and the check goes on (probe p309).</summary>
    private static void ValidateAttributesWarning(string identifier, string text)
    {
        if ((JgsLifetime.Current?.Interpreter.Host ?? JgsCallbackDispatcher.Current?.Interpreter?.Host) is { } host)
        {
            Warn(host, identifier, text);
        }
    }
}
