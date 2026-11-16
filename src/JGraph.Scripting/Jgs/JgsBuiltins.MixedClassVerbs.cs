namespace JGraph.Scripting.Jgs;

/// <summary>
/// The two-operand verbs R2025b holds to its class rules (open item 340, ADR 0379): <c>max</c> and
/// <c>min</c> of two arrays, <c>rem</c>, <c>mod</c>, <c>bitand</c>, <c>bitor</c>, <c>bitxor</c> and
/// <c>idivide</c>, and <c>logical</c> of a NaN.
/// </summary>
/// <remarks>
/// <para>
/// Measured in R2025b 25.2 (lane 7C's probes 1 and 4, eighteen operands of every class, scalar,
/// array and empty, in both orders). An integer combines only with its own class or a scalar double
/// (a scalar char is its code), as under the arithmetic operators (ADR 0370): <c>max</c>,
/// <c>min</c>, <c>rem</c> and <c>mod</c> say <c>MATLAB:mixedClasses</c>, the bit verbs
/// <c>MATLAB:Bitand:mixedClasses</c> and their siblings, <c>idivide</c>
/// <c>MATLAB:idivide:mixedClasses</c> (and R2025b's <c>idivide.m</c> is transcribed for the order
/// of its refusals and its answer). The two arrays expand into each other or are
/// <c>MATLAB:sizeDimensionsMustMatch</c>; the answer is the integer class, else single, else double
/// (a logical pair stays logical under <c>max</c>, <c>min</c> and the bit verbs).
/// </para>
/// <para>
/// Each wrapper is asked in MATLAB code only and hands anything it does not judge (a sparse matrix,
/// a time, an object, a char matrix, a complex operand <c>max</c> does not refuse) to the verb as it
/// stands; JGS code keeps its answers.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the wrappers; called after the complex flag's (ADR 0370) so they wrap it.</summary>
    private static void WrapMixedClassVerbs(JgsEnvironment env, JgsRunningDialect dialect)
    {
        void Wrap(string name, Func<BuiltinFunction, IReadOnlyList<JgsValue>, int, int, JgsValue?> single,
            Func<BuiltinFunction, IReadOnlyList<JgsValue>, int, int, int, JgsValue[]?>? multi = null, bool keepsStrings = false)
        {
            if (!env.Builtins.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function
                || existing.AsCallable is not BuiltinFunction inner)
            {
                return;
            }

            var wrapped = new BuiltinFunction(name, (args, line, col) =>
                (dialect.IsMatlab ? single(inner, args, line, col) : null) ?? inner.Call(args, line, col))
            {
                MultiOutput = inner.MultiOutput is null
                    ? null
                    : (args, wanted, line, col) =>
                        (dialect.IsMatlab ? (multi ?? SingleAsMulti)(inner, args, wanted, line, col) : null)
                        ?? inner.CallMultiple(args, wanted, line, col),
                BindsAnsAsStatement = inner.BindsAnsAsStatement,
                AutoCallsBare = inner.AutoCallsBare,
                KnowsWhenDiscarded = inner.KnowsWhenDiscarded,
                TakesOutputCount = inner.TakesOutputCount,
                ScriptFile = inner.ScriptFile,
                // A wrapper that keeps a string sees it as one; the verb under it still gets its char.
                KeepsStringArguments = keepsStrings || inner.KeepsStringArguments,
            };
            env.Builtins.Register(name, JgsValue.Function(wrapped));

            JgsValue[]? SingleAsMulti(BuiltinFunction inner, IReadOnlyList<JgsValue> args, int wanted, int line, int col) =>
                wanted <= 1 && single(inner, args, line, col) is { } answer ? [answer] : null;
        }

        foreach ((string name, bool takeMin) in new[] { ("max", false), ("min", true) })
        {
            Wrap(name,
                (_, args, line, col) => McTwoArrayExtreme(name, takeMin, args, line, col),
                (_, args, wanted, line, col) =>
                {
                    if (wanted >= 2 && McIsTwoArrayForm(args))
                    {
                        throw new JgsRuntimeException(line, col, $"MATLAB:{name}:TwoInTwoOutCaseNotSupported",
                            "Two output arguments are not supported for two input arrays.");
                    }

                    return wanted <= 1 && McTwoArrayExtreme(name, takeMin, args, line, col) is { } answer ? [answer] : null;
                });
        }

        // rem and mod keep a string to refuse it (open item 351, ADR 0383): it reached them as its char.
        Wrap("rem", (_, args, line, col) => McRemMod(false, args, line, col), keepsStrings: true);
        Wrap("mod", (_, args, line, col) => McRemMod(true, args, line, col), keepsStrings: true);
        Wrap("bitand", (_, args, line, col) => McBitwise("bitand", "MATLAB:Bitand", static (a, b) => a & b, args, line, col));
        Wrap("bitor", (_, args, line, col) => McBitwise("bitor", "MATLAB:Bitor", static (a, b) => a | b, args, line, col));
        Wrap("bitxor", (_, args, line, col) => McBitwise("bitxor", "MATLAB:bitxor", static (a, b) => a ^ b, args, line, col));
        Wrap("idivide", (_, args, line, col) => McIntegerDivide(args, line, col));
        Wrap("logical", (_, args, line, col) => McLogical(args, line, col));

        // Open item 351 and item 346's kron (ADR 0383): bitshift's own refusals and arithmetic,
        // kron's class rule, atan2's and hypot's complex and size rules.
        Wrap("bitshift", (_, args, line, col) => McBitShift(args, line, col));
        Wrap("kron", (inner, args, line, col) => McKron(inner, args, line, col), keepsStrings: true);
        Wrap("atan2", (inner, args, line, col) => McAtan2Hypot(inner, "atan2", args, line, col));
        Wrap("hypot", (inner, args, line, col) => McAtan2Hypot(inner, "hypot", args, line, col));
    }

    /// <summary>
    /// One real operand read as R2025b's two-operand verbs read it: its values column-major, its
    /// size, its class, whether it is a logical or a char.
    /// </summary>
    private sealed record McOperand(double[] Values, int[] Dims, JgsNumericClass Class, bool Logical, bool Char)
    {
        public bool Scalar => Values.Length == 1;

        public bool Integer => Class.IsInteger();
    }

    /// <summary>
    /// The operand as numbers, or null when it is not a real number, logical or char row (a complex,
    /// sparse, time, categorical, string, cell, char matrix or anything else is not this file's).
    /// </summary>
    private static McOperand? McOperandOf(JgsValue value)
    {
        switch (value.Type)
        {
            case JgsType.Number:
                return new McOperand([value.AsNumber], [1, 1], value.NumericClass, false, false);
            case JgsType.Bool:
                return new McOperand([value.AsNumber], [1, 1], JgsNumericClass.Double, true, false);
            case JgsType.String when !value.IsStringArray:
            {
                string text = value.AsString;
                var codes = new double[text.Length];
                for (int i = 0; i < codes.Length; i++)
                {
                    codes[i] = text[i];
                }

                return new McOperand(codes, SizeDims(value), JgsNumericClass.Double, false, true);
            }

            case JgsType.Array when !value.IsStringArray && !value.IsTime && !value.IsCategorical
                && !value.IsPackedComplex && !value.IsCharMatrix && !JgsMatrix.IsNested(value):
            {
                int length = value.ArrayLength;
                double[] values;
                if (value.IsPacked)
                {
                    values = value.AsBuffer.AsSpan().ToArray();
                }
                else
                {
                    values = new double[length];
                    for (int i = 0; i < length; i++)
                    {
                        JgsValue element = value.ElementAt(i);
                        if (element.Type is not (JgsType.Number or JgsType.Bool))
                        {
                            return null;
                        }

                        values[i] = element.AsNumber;
                    }
                }

                return new McOperand(values, JgsMatrix.DimsOf(value), value.NumericClass, IsLogicalValue(value), false);
            }

            default:
                return null;
        }
    }

    /// <summary>Whether a value holds a complex number anywhere, the zero-imaginary flag included.</summary>
    internal static bool McAnyComplex(JgsValue value)
    {
        if (value.Type == JgsType.Complex || value.IsPackedComplex)
        {
            return true;
        }

        if (value.Type != JgsType.Array || value.IsPacked || value.IsStringArray || value.IsCategorical)
        {
            return false;
        }

        for (int i = 0; i < value.ArrayLength; i++)
        {
            if (value.ElementAt(i).Type == JgsType.Complex)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether an integer operand meets a partner R2025b does not combine it with: another integer
    /// class, a single, a logical, or a double (or char) that is not a scalar.
    /// </summary>
    private static bool McMixesClasses(McOperand a, McOperand b)
    {
        if (!a.Integer && !b.Integer)
        {
            return false;
        }

        if (a.Integer && b.Integer)
        {
            return a.Class != b.Class;
        }

        McOperand other = a.Integer ? b : a;
        return other.Class == JgsNumericClass.Single || other.Logical || !other.Scalar;
    }

    /// <summary>The size two operands expand to, or null when they do not expand into each other.</summary>
    private static int[]? McBroadcastDims(int[] a, int[] b)
    {
        int rank = Math.Max(a.Length, b.Length);
        var size = new int[rank];
        for (int d = 0; d < rank; d++)
        {
            int x = d < a.Length ? a[d] : 1;
            int y = d < b.Length ? b[d] : 1;
            if (x != y && x != 1 && y != 1)
            {
                return null;
            }

            size[d] = x == 1 ? y : x;
        }

        return size;
    }

    /// <summary>Applies <paramref name="f"/> pairwise over two operands expanded to <paramref name="size"/>.</summary>
    private static double[] McPairwise(McOperand a, McOperand b, int[] size, Func<double, double, double> f)
    {
        long total = 1;
        foreach (int extent in size)
        {
            total *= extent;
        }

        var result = new double[total];
        if (a.Values.Length == total && b.Values.Length == total)
        {
            for (int i = 0; i < total; i++)
            {
                result[i] = f(a.Values[i], b.Values[i]);
            }

            return result;
        }

        if (a.Scalar && b.Values.Length == total)
        {
            for (int i = 0; i < total; i++)
            {
                result[i] = f(a.Values[0], b.Values[i]);
            }

            return result;
        }

        if (b.Scalar && a.Values.Length == total)
        {
            for (int i = 0; i < total; i++)
            {
                result[i] = f(a.Values[i], b.Values[0]);
            }

            return result;
        }

        int rank = size.Length;
        var strideA = new long[rank];
        var strideB = new long[rank];
        long runA = 1;
        long runB = 1;
        for (int d = 0; d < rank; d++)
        {
            int extentA = d < a.Dims.Length ? a.Dims[d] : 1;
            int extentB = d < b.Dims.Length ? b.Dims[d] : 1;
            strideA[d] = extentA == 1 ? 0 : runA;
            strideB[d] = extentB == 1 ? 0 : runB;
            runA *= extentA;
            runB *= extentB;
        }

        var counter = new int[rank];
        for (long n = 0; n < total; n++)
        {
            long slotA = 0;
            long slotB = 0;
            for (int d = 0; d < rank; d++)
            {
                slotA += counter[d] * strideA[d];
                slotB += counter[d] * strideB[d];
            }

            result[n] = f(a.Values[slotA], b.Values[slotB]);
            for (int d = 0; d < rank; d++)
            {
                if (++counter[d] < size[d])
                {
                    break;
                }

                counter[d] = 0;
            }
        }

        return result;
    }

    /// <summary>
    /// An answer of numbers in <paramref name="size"/>, in the class two operands combine to: an
    /// integer class, else single, else double; a logical when both were and <paramref name="keepsLogical"/>.
    /// </summary>
    private static JgsValue McAnswer(double[] values, int[] size, McOperand a, McOperand b, bool keepsLogical)
    {
        if (keepsLogical && a.Logical && b.Logical)
        {
            return McLogicalAnswer(Array.ConvertAll(values, static v => v != 0), size);
        }

        JgsNumericClass numericClass = a.Integer ? a.Class
            : b.Integer ? b.Class
            : a.Class == JgsNumericClass.Single || b.Class == JgsNumericClass.Single ? JgsNumericClass.Single
            : JgsNumericClass.Double;
        JgsValue shaped = values.Length == 1 && size.All(static d => d == 1)
            ? JgsValue.Number(values[0])
            : JgsMatrix.FromColumnMajorDims(values, size);
        return JgsNumericClasses.Stamp(shaped, numericClass);
    }

    /// <summary>A logical answer in <paramref name="size"/>: a bare logical for one element, an empty logical for none.</summary>
    internal static JgsValue McLogicalAnswer(bool[] flags, int[] size)
    {
        if (flags.Length == 1 && size.All(static d => d == 1))
        {
            return JgsValue.Bool(flags[0]);
        }

        if (flags.Length == 0)
        {
            JgsValue empty = EmptyLogical(size.Length > 0 ? size[0] : 0, size.Length > 1 ? size[1] : 0);
            if (size.Length > 2)
            {
                empty.ReshapeDims(size);
            }

            return empty;
        }

        return JgsMatrix.FromElementsDims(Array.ConvertAll(flags, JgsValue.Bool), size);
    }

    private static JgsRuntimeException McMixedClasses(int line, int col) =>
        new(line, col, "MATLAB:mixedClasses", MixedClassesRefusal);

    private static JgsRuntimeException McSizesDiffer(int line, int col) =>
        new(line, col, "MATLAB:sizeDimensionsMustMatch", "Arrays have incompatible sizes for this operation.");

    // --- max and min of two arrays -------------------------------------------------------------

    /// <summary>
    /// Whether a call is the two-array form: two arguments, or more with a NaN flag or
    /// <c>'ComparisonMethod'</c> third (open item 351) after a second that is not the reducing <c>[]</c>.
    /// </summary>
    private static bool McIsTwoArrayForm(IReadOnlyList<JgsValue> args) =>
        args.Count == 2
        || (args.Count >= 3 && (McNanFlag(args[2]) is not null || McIsComparisonMethodName(args[2]))
            && !(args[1].Type == JgsType.Array && args[1].ArrayLength == 0 && args[1].NumericClass == JgsNumericClass.Double
                && !args[1].IsStringArray && !IsLogicalValue(args[1])));

    /// <summary>True for an include flag, false for an omit flag, null for anything else.</summary>
    private static bool? McNanFlag(JgsValue value)
    {
        if (!IsTextScalar(value))
        {
            return null;
        }

        return TextOf(value).ToLowerInvariant() switch
        {
            "omitnan" or "omitmissing" => false,
            "includenan" or "includemissing" => true,
            _ => null,
        };
    }

    /// <summary>
    /// <c>max(a, b)</c> or <c>min(a, b)</c> (with an optional NaN flag and comparison method) as
    /// R2025b answers it, or null for a call it leaves to the verb: the reducing forms, a sparse
    /// operand, and any operand that is not a number, logical or char row.
    /// </summary>
    private static JgsValue? McTwoArrayExtreme(string name, bool takeMin, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (!McIsTwoArrayForm(args))
        {
            return null;
        }

        JgsValue left = args[0];
        JgsValue right = args[1];
        if (args.Count == 2 && IsTextScalar(right))
        {
            string text = TextOf(right).ToLowerInvariant();
            if (text.Length > 0 && "all".StartsWith(text, StringComparison.Ordinal))
            {
                throw new JgsRuntimeException(line, col, $"MATLAB:{name}:secondInputDimAll",
                    $"Invalid second argument. To operate over all elements of the input array, use {name}(A,[],\"all\").");
            }

            if (text is "omitnan" or "includenan" or "omitmissing" or "includemissing" or "linear")
            {
                throw new JgsRuntimeException(line, col, $"MATLAB:{name}:secondInputFlag",
                    $"Invalid second argument. To specify an option, use {name}(A,[],option).");
            }

            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:wrongSecondInputText",
                $"Invalid second argument. To specify an additional argument, use {name}(A,[],...).");
        }

        if (left.IsStringArray || left.Type is JgsType.Cell or JgsType.Struct)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:wrongInput", "First input array is an invalid data type.");
        }

        if (McExtremeOptions(name, args, line, col) is not (bool includeNan, string method))
        {
            return null;
        }

        if (McAnyComplex(left) || McAnyComplex(right))
        {
            if (left.NumericClass.IsInteger() || right.NumericClass.IsInteger())
            {
                throw new JgsRuntimeException(line, col, "MATLAB:complexInts", "Complex integer arithmetic is not supported.");
            }

            // By magnitude, then angle (or by real part, then imaginary), answered complex only
            // when a picked value is (open item 351, ADR 0383).
            return McComplexExtreme(takeMin, left, right, includeNan, method == "real", line, col);
        }

        if (McOperandOf(left) is not { } a || McOperandOf(right) is not { } b)
        {
            return null;
        }

        if (McMixesClasses(a, b))
        {
            throw McMixedClasses(line, col);
        }

        int[] size = McBroadcastDims(a.Dims, b.Dims) ?? throw McSizesDiffer(line, col);
        bool byMagnitude = method == "abs";
        double[] picked = McPairwise(a, b, size, (x, y) =>
        {
            if (double.IsNaN(x))
            {
                return includeNan ? x : y;
            }

            if (double.IsNaN(y))
            {
                return includeNan ? y : x;
            }

            if (byMagnitude)
            {
                // 'abs' of real numbers: the magnitude, then the angle (a negative's is pi), the first on a tie.
                int order = McRealMagnitudeOrder(y, x);
                return (takeMin ? order < 0 : order > 0) ? y : x;
            }

            return takeMin ? Math.Min(x, y) : Math.Max(x, y);
        });

        return McAnswer(picked, size, a, b, keepsLogical: true);
    }

    // --- rem and mod -----------------------------------------------------------------------------

    /// <summary>MATLAB's <c>rem(x, y)</c> for one pair of doubles: NaN for a zero divisor or an infinite dividend, x for an infinite divisor.</summary>
    internal static double MatlabRem(double x, double y)
    {
        if (y == 0 || double.IsInfinity(x) || double.IsNaN(x) || double.IsNaN(y))
        {
            return double.NaN;
        }

        if (double.IsInfinity(y))
        {
            return x;
        }

        return x - (y * Math.Truncate(x / y));
    }

    /// <summary>
    /// MATLAB's <c>mod(x, y)</c> for one pair of doubles: x for a zero divisor, NaN for an infinite
    /// dividend, and for an infinite divisor x when it has the divisor's sign (or is zero) and the
    /// divisor otherwise.
    /// </summary>
    internal static double MatlabMod(double x, double y)
    {
        if (y == 0)
        {
            return x;
        }

        if (double.IsInfinity(x) || double.IsNaN(x) || double.IsNaN(y))
        {
            return double.NaN;
        }

        if (double.IsInfinity(y))
        {
            return x == 0 || Math.Sign(x) == Math.Sign(y) ? x : y;
        }

        return x - (Math.Floor(x / y) * y);
    }

    /// <summary>MATLAB's <c>min(x, y)</c> of two doubles: a NaN is left out (the compiled loop's kernel too).</summary>
    internal static double MatlabMin2(double x, double y) => double.IsNaN(x) ? y : double.IsNaN(y) ? x : Math.Min(x, y);

    /// <summary>MATLAB's <c>max(x, y)</c> of two doubles: a NaN is left out (the compiled loop's kernel too).</summary>
    internal static double MatlabMax2(double x, double y) => double.IsNaN(x) ? y : double.IsNaN(y) ? x : Math.Max(x, y);

    /// <summary><c>rem(a, b)</c> or <c>mod(a, b)</c> as R2025b answers it, or null for an operand it leaves to the verb.</summary>
    private static JgsValue? McRemMod(bool modulo, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count != 2)
        {
            return null;
        }

        JgsValue left = args[0];
        JgsValue right = args[1];
        // A string scalar too (the wrapper keeps it), a struct, a function handle and a datetime (open item 351).
        static bool NotNumericCharOrLogical(JgsValue v) =>
            v.IsStringArray || v.IsDatetime || v.Type is JgsType.Cell or JgsType.Struct or JgsType.Function;
        if (NotNumericCharOrLogical(left) || NotNumericCharOrLogical(right))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:math:mustBeNumericCharOrLogical",
                "Invalid data type. Argument must be numeric, char, or logical.");
        }

        if ((McAnyComplex(left) && left.Type != JgsType.Sparse) || (McAnyComplex(right) && right.Type != JgsType.Sparse))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:math:mustBeReal", "Argument must be real.");
        }

        if (McOperandOf(left) is not { } a || McOperandOf(right) is not { } b)
        {
            return null;
        }

        if (McMixesClasses(a, b))
        {
            throw McMixedClasses(line, col);
        }

        int[] size = McBroadcastDims(a.Dims, b.Dims) ?? throw McSizesDiffer(line, col);
        double[] values = McPairwise(a, b, size, modulo ? MatlabMod : MatlabRem);
        return McAnswer(values, size, a, b, keepsLogical: false);
    }

    // --- bitand, bitor, bitxor ---------------------------------------------------------------------

    /// <summary>
    /// A two-operand bit verb as R2025b refuses and answers it: a char or string is
    /// <c>operandsNotNumeric</c>, a complex part <c>complexInputs</c>, a single with no integer
    /// <c>firstArgsNotInt</c>, operands that do not expand <c>sizeMismatch</c>, an integer beside
    /// anything but its own class or a scalar double <c>mixedClasses</c>; then a double that is not a
    /// whole number in range is <c>outOfRange</c> (<c>outOfRangeMixedType</c> beside an integer). Null
    /// for the assumed-type form and for what it leaves to the verb.
    /// </summary>
    private static JgsValue? McBitwise(string name, string prefix, Func<long, long, long> op,
        IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count != 2 || args[0].Type == JgsType.External || args[1].Type == JgsType.External
            || args[0].Type == JgsType.Sparse || args[1].Type == JgsType.Sparse)
        {
            return null;
        }

        JgsValue left = args[0];
        JgsValue right = args[1];
        static bool Text(JgsValue v) => v.Type == JgsType.String || v.IsStringArray || v.IsCharMatrix;
        if (Text(left) || Text(right))
        {
            throw new JgsRuntimeException(line, col, $"{prefix}:operandsNotNumeric", "Operands to BIT Ops must be numeric.");
        }

        if (McAnyComplex(left) || McAnyComplex(right))
        {
            throw new JgsRuntimeException(line, col, $"{prefix}:complexInputs", "Inputs must be real.");
        }

        if (McOperandOf(left) is not { } a || McOperandOf(right) is not { } b)
        {
            return null;
        }

        if (!a.Integer && !b.Integer && (a.Class == JgsNumericClass.Single || b.Class == JgsNumericClass.Single))
        {
            throw new JgsRuntimeException(line, col, $"{prefix}:firstArgsNotInt", "Invalid data type. First two arguments must be integer or double.");
        }

        // Two non-empty operands of one integer class, or two logicals, are refused in the operators'
        // words; any other pair in the verb's own (lane 7C's probe 5).
        bool alike = ((a.Integer && a.Class == b.Class) || (a.Logical && b.Logical)) && a.Values.Length > 0 && b.Values.Length > 0;
        int[] size = McBroadcastDims(a.Dims, b.Dims)
            ?? throw (alike ? McSizesDiffer(line, col)
                : new JgsRuntimeException(line, col, $"{prefix}:sizeMismatch", "Inputs must have the same size."));
        if (McMixesClasses(a, b))
        {
            throw new JgsRuntimeException(line, col, $"{prefix}:mixedClasses",
                "Inputs must be signed or unsigned integers of the same class or scalar doubles.");
        }

        JgsNumericClass integer = a.Integer ? a.Class : b.Integer ? b.Class : JgsNumericClass.Double;
        McOperand[] doubles = integer == JgsNumericClass.Double ? [a, b] : a.Integer ? [b] : [a];
        foreach (McOperand operand in doubles)
        {
            if (operand.Integer)
            {
                continue;
            }

            foreach (double v in operand.Values)
            {
                bool whole = double.IsFinite(v) && v == Math.Floor(v);
                if (integer == JgsNumericClass.Double && (!whole || v < 0 || v > 9007199254740992.0))
                {
                    throw new JgsRuntimeException(line, col, $"{prefix}:outOfRange",
                        "Double inputs must have integer values in the range of ASSUMEDTYPE.");
                }

                if (integer != JgsNumericClass.Double)
                {
                    if (!whole)
                    {
                        throw new JgsRuntimeException(line, col, $"{prefix}:outOfRangeMixedType",
                            "Double inputs must have integer values in the integer range when mixed with integer inputs.");
                    }

                    (double low, double high) = JgsNumericClasses.Range(integer);
                    if (v < low || v > high)
                    {
                        return null; // not measured: the verb's own words
                    }
                }
            }
        }

        if (integer is JgsNumericClass.UInt64 or JgsNumericClass.Int64)
        {
            return null; // a double cannot carry every 64-bit pattern; the verb's road
        }

        double[] values = McPairwise(a, b, size, (x, y) => op((long)x, (long)y));
        return McAnswer(values, size, a, b, keepsLogical: true);
    }

    // --- idivide ------------------------------------------------------------------------------------

    /// <summary>
    /// <c>idivide(a, b, opt)</c> transcribed from R2025b's <c>idivide.m</c>: the sizes first
    /// (<c>dimagree</c>), then an integer on one side (<c>OneArgumentMustBeInteger</c>), then the
    /// classes (<c>mixedClasses</c>, <c>scalarDoubleWith64BitInt</c>), then a complex part
    /// (<c>complexInts</c>), then the rounding option (partly matched). The quotient is rounded in
    /// double and cast to the integer class, so a zero divisor saturates.
    /// </summary>
    private static JgsValue? McIntegerDivide(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count is < 2 or > 3 || args[0].Type == JgsType.Sparse || args[1].Type == JgsType.Sparse)
        {
            return null;
        }

        JgsValue left = args[0];
        JgsValue right = args[1];
        int[] sizeA = SizeDims(left);
        int[] sizeB = SizeDims(right);
        bool scalarA = sizeA.All(static d => d == 1);
        bool scalarB = sizeB.All(static d => d == 1);
        int[]? size = McBroadcastDims(sizeA, sizeB);
        if (size is null)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:idivide:dimagree", "Matrix dimensions must agree.");
        }

        bool intA = left.NumericClass.IsInteger();
        bool intB = right.NumericClass.IsInteger();
        if (!intA && !intB)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:idivide:OneArgumentMustBeInteger",
                "At least one argument must belong to an integer class.");
        }

        static bool PlainDouble(JgsValue v) =>
            v.NumericClass == JgsNumericClass.Double && !IsLogicalValue(v) && v.Type is JgsType.Number or JgsType.Complex or JgsType.Array
            && !v.IsStringArray && !v.IsCharMatrix && !v.IsTime && !v.IsCategorical;
        if (!(intA && intB && left.NumericClass == right.NumericClass))
        {
            if (!(intA && PlainDouble(right) && scalarB) && !(intB && PlainDouble(left) && scalarA))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:idivide:mixedClasses", MixedClassesRefusal);
            }

            if (left.NumericClass is JgsNumericClass.Int64 or JgsNumericClass.UInt64
                || right.NumericClass is JgsNumericClass.Int64 or JgsNumericClass.UInt64)
            {
                // Measured since (open item 351, ADR 0383): a double scalar beside int64 or uint64,
                // a complex one included, is refused before the complex check.
                throw new JgsRuntimeException(line, col, "MATLAB:idivide:scalarDoubleWith64BitInt",
                    "Mixed 64-bit integer and scalar double inputs are not supported. 64-bit integers can only be combined with integers of the same class.");
            }
        }

        if (McAnyComplex(left) || McAnyComplex(right))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:idivide:complexInts", "Complex integer arithmetic is not supported.");
        }

        Func<double, double> rounding = Math.Truncate;
        if (args.Count == 3)
        {
            string option = IsTextScalar(args[2]) ? TextOf(args[2]) : "\0";
            rounding = McPartialMatch(option, "fix", 2) ? Math.Truncate
                : McPartialMatch(option, "floor", 2) ? Math.Floor
                : McPartialMatch(option, "ceil", 1) ? Math.Ceiling
                : McPartialMatch(option, "round", 1) ? static q => Math.Round(q, MidpointRounding.AwayFromZero)
                : throw new JgsRuntimeException(line, col, "MATLAB:idivide:InvalidRoundingOption", "Unrecognized rounding option.");
        }

        if (McOperandOf(left) is not { } a || McOperandOf(right) is not { } b)
        {
            return null;
        }

        double[] quotients = McPairwise(a, b, size, (x, y) => rounding(x / y));
        return McAnswer(quotients, size, a, b, keepsLogical: false);
    }

    /// <summary>R2025b's <c>matlab.internal.math.partialMatch</c>: a case-blind prefix of at least <paramref name="least"/> characters.</summary>
    private static bool McPartialMatch(string given, string option, int least) =>
        given.Length >= least && given.Length <= option.Length
        && option.StartsWith(given, StringComparison.OrdinalIgnoreCase);

    // --- arrayfun and cellfun -----------------------------------------------------------------------

    /// <summary>
    /// The elements <c>arrayfun</c> hands its function: in MATLAB code each one keeps the array's
    /// class (an int8 array hands int8 scalars, a single array singles) and a complex array's
    /// zero-imaginary elements stay complex, as R2025b's do (open item 340).
    /// </summary>
    private static JgsValue[] ClassedElementsOf(JgsValue value)
    {
        JgsValue[] elements = ElementsOf("arrayfun", value);
        if (!JgsRunningDialect.ThreadIsMatlab || value.Type != JgsType.Array || value.IsCharMatrix || value.IsStringArray
            || value.IsTime || (value.NumericClass == JgsNumericClass.Double && !value.IsPackedComplex))
        {
            return elements;
        }

        JgsNumericClass numericClass = value.NumericClass;
        var classed = new JgsValue[elements.Length];
        for (int i = 0; i < elements.Length; i++)
        {
            JgsValue element = elements[i];
            if (value.IsPackedComplex && element.Type is JgsType.Number or JgsType.Complex)
            {
                System.Numerics.Complex z = element.Type == JgsType.Complex ? element.AsComplex : new System.Numerics.Complex(element.AsNumber, 0);
                JgsValue flagged = ComplexStorage([z], [1, 1], preserveComplex: true);
                flagged.SetNumericClass(numericClass);
                classed[i] = flagged;
            }
            else if (element.Type == JgsType.Number)
            {
                classed[i] = JgsNumericClasses.Stamp(element, numericClass);
            }
            else if (element.Type == JgsType.Complex && numericClass == JgsNumericClass.Single)
            {
                JgsValue single = JgsValue.ComplexNum(element.AsComplex);
                single.SetNumericClass(numericClass);
                classed[i] = single;
            }
            else
            {
                classed[i] = element;
            }
        }

        return classed;
    }

    /// <summary>
    /// A uniform answer after the first, held to the first's class in MATLAB code (R2025b's
    /// <c>MismatchInOutputTypes</c>, asked before the answer's own size): a non-scalar first answer is
    /// refused at index 1 first, as R2025b refuses it there. Cells, structs, handles, times and
    /// objects are ADR 0371's and the object gatherer's to judge. <paramref name="index"/> and
    /// <paramref name="output"/> are 1-based.
    /// </summary>
    internal static void CheckUniformClass(string verb, JgsValue first, JgsValue answer, int index, int output, int line, int col)
    {
        if (!JgsRunningDialect.ThreadIsMatlab || index < 2 || IsGatheredKind(first) || IsGatheredKind(answer)
            || first.Type is JgsType.Object or JgsType.External || answer.Type is JgsType.Object or JgsType.External
            || first.Type == JgsType.Null || answer.Type == JgsType.Null)
        {
            return;
        }

        if (!IsUniformScalar(first))
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{verb}:NotAScalarOutput",
                $"Non-scalar in Uniform output, at index 1, output {output}.\nSet 'UniformOutput' to false.");
        }

        string had = ClassOf(first, JgsDialect.Matlab);
        string has = ClassOf(answer, JgsDialect.Matlab);
        if (!string.Equals(had, has, StringComparison.Ordinal))
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{verb}:MismatchInOutputTypes",
                $"Mismatch in type of outputs, at index {index}, output {output} ({had} versus {has}).\nSet 'UniformOutput' to false.");
        }
    }

    /// <summary>
    /// A uniform result in the class its answers share, in MATLAB code: int8 answers make an int8
    /// array, singles a single one (R2025b; they were gathered as doubles).
    /// </summary>
    internal static JgsValue UniformClassOf(JgsValue result, JgsValue[] answers)
    {
        if (!JgsRunningDialect.ThreadIsMatlab || answers.Length == 0)
        {
            return result;
        }

        JgsValue first = answers[0];
        JgsNumericClass numericClass = first.NumericClass;
        if (numericClass == JgsNumericClass.Double || first.Type is not (JgsType.Number or JgsType.Array)
            || first.IsStringArray || first.IsCategorical || first.IsTime || first.IsCharMatrix
            || result.NumericClass == numericClass || result.Type is not (JgsType.Number or JgsType.Array)
            || result.IsPackedComplex || result.IsStringArray || result.IsCategorical || result.IsTime)
        {
            return result;
        }

        return JgsNumericClasses.Stamp(result, numericClass);
    }

    // --- logical -----------------------------------------------------------------------------------

    /// <summary>
    /// <c>logical(x)</c> of a real array holding a NaN is <c>MATLAB:nologicalnan</c> (open item 341),
    /// and a char row is its codes; null for anything else, which the verb answers.
    /// </summary>
    private static JgsValue? McLogical(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count != 1 || McAnyComplex(args[0]))
        {
            return null;
        }

        JgsValue value = args[0];
        if (value.Type == JgsType.String && !value.IsStringArray && McOperandOf(value) is { } text)
        {
            return McLogicalAnswer(Array.ConvertAll(text.Values, static v => v != 0), text.Dims);
        }

        if (McAnyNaN(value))
        {
            throw NoLogicalNaN(line, col);
        }

        return null;
    }

    /// <summary>R2025b's refusal of a NaN where a logical is needed.</summary>
    internal static JgsRuntimeException NoLogicalNaN(int line, int col) =>
        new(line, col, "MATLAB:nologicalnan", "NaN values cannot be converted to logicals.");

    /// <summary>Whether a real number or numeric array holds a NaN (a complex value's parts are not asked).</summary>
    internal static bool McAnyNaN(JgsValue value)
    {
        if (value.IsTime || value.IsCategorical || value.IsStringArray)
        {
            return false;
        }

        switch (value.Type)
        {
            case JgsType.Number:
                return double.IsNaN(value.AsNumber);
            case JgsType.Array when value.IsPacked:
                foreach (double v in value.AsBuffer.AsSpan())
                {
                    if (double.IsNaN(v))
                    {
                        return true;
                    }
                }

                return false;
            case JgsType.Array when !value.IsStringArray && !value.IsCategorical && !value.IsPackedComplex && !value.IsTime:
                for (int i = 0; i < value.ArrayLength; i++)
                {
                    JgsValue element = value.ElementAt(i);
                    if (element.Type == JgsType.Number && double.IsNaN(element.AsNumber))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }
}
