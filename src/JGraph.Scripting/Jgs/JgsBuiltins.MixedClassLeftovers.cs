using System.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The mixed-class leftovers after ADR 0379 (open item 351 and item 346's kron, ADR 0383): <c>max</c>
/// and <c>min</c> of a complex pair and their <c>'ComparisonMethod'</c>, <c>bitshift</c>'s own
/// refusals and arithmetic, <c>kron</c>'s class rule, and <c>atan2</c>'s and <c>hypot</c>'s complex
/// and size rules. Each is asked in MATLAB code only, by the wrappers <c>WrapMixedClassVerbs</c>
/// registers; JGS code keeps its answers.
/// </summary>
/// <remarks>
/// Measured in R2025b 25.2 (lane 8G's probes 1, 3 and 4). <c>max</c> and <c>min</c> of two arrays
/// compare a complex pair (the zero-imaginary flag included) by magnitude, then by angle in
/// (-pi, pi], keeping the first on a full tie; <c>'ComparisonMethod', 'real'</c> compares the real
/// part, then the imaginary one, and <c>'abs'</c> of real numbers the magnitude, then the angle (a
/// negative's is pi). A value with a NaN part is a NaN, omitted unless <c>'includenan'</c>, the first
/// kept when both are. The answer is complex only when a picked value has an imaginary part, single
/// beside a single, else double (a logical or a char beside a complex number is a double).
/// </remarks>
internal static partial class JgsBuiltins
{
    // --- max and min: the options and the complex pair ---------------------------------------------

    /// <summary>Whether a value names <c>'ComparisonMethod'</c>, a case-blind leading part of it included.</summary>
    private static bool McIsComparisonMethodName(JgsValue value) =>
        IsTextScalar(value) && TextOf(value) is { Length: > 0 } text
        && "comparisonmethod".StartsWith(text.ToLowerInvariant(), StringComparison.Ordinal);

    /// <summary>
    /// The options after <c>max(a, b, ...)</c>: NaN flags and <c>'ComparisonMethod'</c> pairs (the last
    /// one named wins; a flag after one is <c>InvalidFlagPosition</c>). Null for an argument this does
    /// not read, which the verb answers.
    /// </summary>
    private static (bool IncludeNan, string Method)? McExtremeOptions(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        bool includeNan = false;
        string method = "auto";
        bool named = false;
        for (int i = 2; i < args.Count; i++)
        {
            if (McNanFlag(args[i]) is { } flag)
            {
                if (named)
                {
                    throw new JgsRuntimeException(line, col, $"MATLAB:{name}:InvalidFlagPosition", "Specify comparison method last.");
                }

                includeNan = flag;
                continue;
            }

            if (!McIsComparisonMethodName(args[i]))
            {
                return null;
            }

            if (i + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, $"MATLAB:{name}:keyWithoutValue",
                    "Incorrect number of input arguments. 'ComparisonMethod' must be followed by a corresponding value.");
            }

            JgsValue value = args[++i];
            string word = IsTextScalar(value) ? TextOf(value).ToLowerInvariant() : string.Empty;
            if (word is not ("auto" or "abs" or "real"))
            {
                throw new JgsRuntimeException(line, col, $"MATLAB:{name}:InvalidComparisonMethod",
                    "Comparison method must be 'auto', 'abs', or 'real'.");
            }

            method = word;
            named = true;
        }

        return (includeNan, method);
    }

    /// <summary>The order of two real numbers by <c>'abs'</c>: the magnitude, then the angle (pi for a negative or -0).</summary>
    private static int McRealMagnitudeOrder(double x, double y)
    {
        int byMagnitude = Math.Abs(x).CompareTo(Math.Abs(y));
        return byMagnitude != 0 ? byMagnitude : Math.Atan2(0.0, x).CompareTo(Math.Atan2(0.0, y));
    }

    /// <summary>The order of two complex numbers: magnitude then angle, or (<paramref name="byReal"/>) real part then imaginary.</summary>
    private static int McComplexOrder(Complex x, Complex y, bool byReal)
    {
        int first = byReal
            ? x.Real.CompareTo(y.Real)
            : double.Hypot(x.Real, x.Imaginary).CompareTo(double.Hypot(y.Real, y.Imaginary));
        if (first != 0)
        {
            return first;
        }

        return byReal
            ? x.Imaginary.CompareTo(y.Imaginary)
            : Math.Atan2(x.Imaginary, x.Real).CompareTo(Math.Atan2(y.Imaginary, y.Real));
    }

    /// <summary>
    /// One operand of a complex <c>max</c>/<c>min</c> pair as complex numbers column-major, its size and
    /// the class it answers in (single or double); null for one the verb answers (sparse, a time, text
    /// other than a char row, a container, an object).
    /// </summary>
    private static (Complex[] Values, int[] Dims, JgsNumericClass Class)? McComplexOperandOf(JgsValue value, int line, int col)
    {
        if (value.Type is JgsType.Sparse or JgsType.Object or JgsType.External or JgsType.Cell or JgsType.Struct or JgsType.Function
            || value.IsTime || value.IsCategorical || value.IsStringArray || value.IsCharMatrix)
        {
            return null;
        }

        if (McAnyComplex(value))
        {
            JgsNumericClass numericClass = value.NumericClass == JgsNumericClass.Single ? JgsNumericClass.Single : JgsNumericClass.Double;
            return (ColumnComplex("max", value, line, col), SizeDims(value), numericClass);
        }

        if (McOperandOf(value) is not { } real)
        {
            return null;
        }

        return (Array.ConvertAll(real.Values, static x => new Complex(x, 0)), real.Dims,
            real.Class == JgsNumericClass.Single ? JgsNumericClass.Single : JgsNumericClass.Double);
    }

    /// <summary>
    /// <c>max(a, b)</c> or <c>min(a, b)</c> where either is complex (and neither an integer): picked
    /// pairwise over the expanded size by <see cref="McComplexOrder"/>, the first on a tie, a NaN
    /// omitted unless <paramref name="includeNan"/> (the first of two NaNs kept). Null for an operand
    /// the verb answers.
    /// </summary>
    private static JgsValue? McComplexExtreme(bool takeMin, JgsValue left, JgsValue right, bool includeNan, bool byReal, int line, int col)
    {
        if (McComplexOperandOf(left, line, col) is not { } a || McComplexOperandOf(right, line, col) is not { } b)
        {
            return null;
        }

        int[] size = McBroadcastDims(a.Dims, b.Dims) ?? throw McSizesDiffer(line, col);

        // Which element of each operand meets which, read through the real expansion.
        static double[] Positions(int count)
        {
            var positions = new double[count];
            for (int i = 0; i < count; i++)
            {
                positions[i] = i;
            }

            return positions;
        }

        var slotsA = new McOperand(Positions(a.Values.Length), a.Dims, JgsNumericClass.Double, false, false);
        var slotsB = new McOperand(Positions(b.Values.Length), b.Dims, JgsNumericClass.Double, false, false);
        double[] fromA = McPairwise(slotsA, slotsB, size, static (x, _) => x);
        double[] fromB = McPairwise(slotsA, slotsB, size, static (_, y) => y);

        static bool Missing(Complex z) => double.IsNaN(z.Real) || double.IsNaN(z.Imaginary);
        var picked = new Complex[fromA.Length];
        for (int i = 0; i < picked.Length; i++)
        {
            Complex x = a.Values[(int)fromA[i]];
            Complex y = b.Values[(int)fromB[i]];
            bool missingX = Missing(x);
            bool missingY = Missing(y);
            if (missingX || missingY)
            {
                picked[i] = missingX && missingY ? x : includeNan ? (missingX ? x : y) : (missingX ? y : x);
                continue;
            }

            int order = McComplexOrder(y, x, byReal);
            picked[i] = (takeMin ? order < 0 : order > 0) ? y : x;
        }

        JgsNumericClass numericClass = a.Class == JgsNumericClass.Single || b.Class == JgsNumericClass.Single
            ? JgsNumericClass.Single
            : JgsNumericClass.Double;
        bool one = picked.Length == 1 && Array.TrueForAll(size, static d => d == 1);
        if (Array.TrueForAll(picked, static z => z.Imaginary == 0))
        {
            double[] real = Array.ConvertAll(picked, static z => z.Real);
            JgsValue shaped = one ? JgsValue.Number(real[0]) : JgsMatrix.FromColumnMajorDims(real, size);
            return JgsNumericClasses.Stamp(shaped, numericClass);
        }

        JgsValue answer = one ? JgsValue.ComplexNum(picked[0]) : ComplexStorage(picked, size);
        if (numericClass == JgsNumericClass.Single)
        {
            answer.SetNumericClass(numericClass);
        }

        return answer;
    }

    // --- bitshift ------------------------------------------------------------------------------------

    /// <summary>
    /// <c>bitshift(A, n)</c> and <c>bitshift(A, n, assumedtype)</c> as R2025b refuses and answers them,
    /// in its order: a char, string, logical, cell, struct or function handle is <c>numericArg</c>; a
    /// single with no integer in front <c>firstArgNotInt</c>; a complex part <c>complexArgs</c>; two
    /// non-scalars of different sizes <c>sizeMismatch</c> (no implicit expansion); a type that is not an
    /// integer class name <c>unknownTypeString</c>, and an integer's other than its own
    /// <c>invalidTypeString</c>; a double outside the assumed type (uint64 by default) or not whole
    /// <c>outOfRange</c>; a shift that is not whole <c>inputsMustBeIntegers</c>. The shift is in the
    /// type's two's complement (bits pushed out are dropped, a signed right shift is arithmetic) and
    /// the answer is in A's class. Null for a sparse, time or object operand, which the verb answers.
    /// </summary>
    private static JgsValue? McBitShift(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count is < 2 or > 3)
        {
            return null;
        }

        JgsValue subject = args[0];
        JgsValue shift = args[1];
        static bool Foreign(JgsValue v) => v.Type is JgsType.Sparse or JgsType.Object or JgsType.External or JgsType.Table
            || v.IsTime || v.IsCategorical;
        if (Foreign(subject) || Foreign(shift))
        {
            return null;
        }

        static bool NotNumeric(JgsValue v) => v.Type is JgsType.String or JgsType.Cell or JgsType.Struct or JgsType.Function
            || v.IsStringArray || v.IsCharMatrix || IsLogicalValue(v);
        if (NotNumeric(subject) || NotNumeric(shift))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:bitshift:numericArg", "Inputs must be numeric.");
        }

        bool integer = subject.NumericClass.IsInteger();
        if (!integer && (subject.NumericClass == JgsNumericClass.Single || shift.NumericClass == JgsNumericClass.Single))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:bitshift:firstArgNotInt",
                "Invalid data type. First and second arguments must be integer or double.");
        }

        if (McAnyComplex(subject) || McAnyComplex(shift))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:bitshift:complexArgs", "Inputs must be real.");
        }

        if (McOperandOf(subject) is not { } a || McOperandOf(shift) is not { } n)
        {
            return null;
        }

        if (!a.Scalar && !n.Scalar && !SizeDims(subject).SequenceEqual(SizeDims(shift)))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:bitshift:sizeMismatch", "Inputs must have same size.");
        }

        (int bits, bool signed) = integer ? McIntegerWidth(subject.NumericClass) : (64, false);
        if (args.Count == 3)
        {
            string word = IsTextScalar(args[2]) ? TextOf(args[2]).ToLowerInvariant() : string.Empty;
            JgsNumericClass? assumed = word is "int8" or "uint8" or "int16" or "uint16" or "int32" or "uint32" or "int64" or "uint64"
                ? JgsNumericClasses.Parse(word)
                : null;
            if (assumed is not { } named)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:bitshift:unknownTypeString", "ASSUMEDTYPE must be an integer type name.");
            }

            if (integer && named != subject.NumericClass)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:bitshift:invalidTypeString",
                    "Integer inputs cannot be assumed as any other types than their own types.");
            }

            (bits, signed) = McIntegerWidth(named);
        }

        if (!integer)
        {
            double low = signed ? -Math.Pow(2, bits - 1) : 0;
            double above = Math.Pow(2, signed ? bits - 1 : bits);
            foreach (double v in a.Values)
            {
                if (!double.IsFinite(v) || v != Math.Floor(v) || v < low || v >= above)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:bitshift:outOfRange",
                        "Double inputs must have integer values in the range of ASSUMEDTYPE.");
                }
            }
        }

        foreach (double k in n.Values)
        {
            if (!double.IsFinite(k) || k != Math.Floor(k))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:bitshift:inputsMustBeIntegers", "Inputs must be integers.");
            }
        }

        int[] size = McBroadcastDims(a.Dims, n.Dims) ?? a.Dims;
        double[] values = McPairwise(a, n, size, (x, k) => McShiftBits(x, k, bits, signed));
        JgsValue shaped = values.Length == 1 && size.All(static d => d == 1)
            ? JgsValue.Number(values[0])
            : JgsMatrix.FromColumnMajorDims(values, size);
        return JgsNumericClasses.Stamp(shaped, subject.NumericClass);
    }

    /// <summary>The width of an integer class and whether it is signed.</summary>
    private static (int Bits, bool Signed) McIntegerWidth(JgsNumericClass numericClass) => numericClass switch
    {
        JgsNumericClass.Int8 => (8, true),
        JgsNumericClass.UInt8 => (8, false),
        JgsNumericClass.Int16 => (16, true),
        JgsNumericClass.UInt16 => (16, false),
        JgsNumericClass.Int32 => (32, true),
        JgsNumericClass.UInt32 => (32, false),
        JgsNumericClass.Int64 => (64, true),
        _ => (64, false),
    };

    /// <summary>
    /// One whole number shifted by <paramref name="k"/> bits in a <paramref name="bits"/>-wide two's
    /// complement: left drops what passes the top, right is arithmetic when <paramref name="signed"/>.
    /// </summary>
    private static double McShiftBits(double x, double k, int bits, bool signed)
    {
        ulong mask = bits == 64 ? ulong.MaxValue : (1UL << bits) - 1;
        static long SignExtended(ulong pattern, int width) =>
            width == 64 ? (long)pattern : (long)(pattern << (64 - width)) >> (64 - width);

        if (signed)
        {
            long value = x >= 9223372036854775807.0 ? long.MaxValue : x <= -9223372036854775808.0 ? long.MinValue : (long)x;
            if (k >= 0)
            {
                return k >= bits ? 0 : SignExtended(((ulong)value << (int)k) & mask, bits);
            }

            return -k >= bits ? (value < 0 ? -1 : 0) : value >> (int)-k;
        }

        ulong unsigned = x >= 18446744073709551615.0 ? ulong.MaxValue : (ulong)x;
        if (k >= 0)
        {
            return k >= bits ? 0 : (double)((unsigned << (int)k) & mask);
        }

        return -k >= bits ? 0 : (double)(unsigned >> (int)-k);
    }

    // --- kron ----------------------------------------------------------------------------------------

    /// <summary>
    /// <c>kron(A, B)</c> held to R2025b's <c>kron.m</c>, which is <c>A .* B</c> over reshaped
    /// operands: an N-D operand is <c>MATLAB:kron:TwoDInput</c>; a cell, string, struct, function
    /// handle or datetime <c>MATLAB:math:mustBeNumericCharOrLogical</c>; an integer beside a complex
    /// value <c>MATLAB:complexInts</c>, beside a single, a logical, another integer class or a double
    /// (or char) that is not a scalar <c>MATLAB:mixedClasses</c>. A char row is its codes. Null for
    /// what the verb answers as it stands (sparse, objects).
    /// </summary>
    private static JgsValue? McKron(BuiltinFunction inner, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count != 2 || args.Any(static v => v.Type is JgsType.Sparse or JgsType.Object or JgsType.External))
        {
            return null;
        }

        JgsValue left = args[0];
        JgsValue right = args[1];
        if (SizeDims(left).Length > 2 || SizeDims(right).Length > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:kron:TwoDInput", "Inputs must be 2-D.");
        }

        static bool NotNumericCharOrLogical(JgsValue v) =>
            v.IsStringArray || v.IsDatetime || v.Type is JgsType.Cell or JgsType.Struct or JgsType.Function;
        if (NotNumericCharOrLogical(left) || NotNumericCharOrLogical(right))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:math:mustBeNumericCharOrLogical",
                "Invalid data type. Argument must be numeric, char, or logical.");
        }

        bool integer = left.NumericClass.IsInteger() || right.NumericClass.IsInteger();
        if (integer && (McAnyComplex(left) || McAnyComplex(right)))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:complexInts", "Complex integer arithmetic is not supported.");
        }

        if (integer && McOperandOf(left) is { } a && McOperandOf(right) is { } b && McMixesClasses(a, b))
        {
            throw McMixedClasses(line, col);
        }

        // A char row reaches the verb as the double array of its codes.
        static JgsValue Codes(JgsValue v) =>
            v.Type == JgsType.String && !v.IsStringArray && McOperandOf(v) is { } text
                ? text.Values.Length == 1 ? JgsValue.Number(text.Values[0]) : JgsMatrix.FromColumnMajorDims(text.Values, text.Dims)
                : v;
        JgsValue codesLeft = Codes(left);
        JgsValue codesRight = Codes(right);
        return ReferenceEquals(codesLeft, left) && ReferenceEquals(codesRight, right)
            ? null
            : inner.Call([codesLeft, codesRight], line, col);
    }

    // --- atan2 and hypot -----------------------------------------------------------------------------

    /// <summary>
    /// <c>atan2(y, x)</c> and <c>hypot(a, b)</c> as R2025b refuses them, in its order: a logical, char,
    /// integer or cell is <c>MATLAB:math:mustBeFloat</c>; a complex part under <c>atan2</c> (the
    /// zero-imaginary flag included) <c>MATLAB:math:mustBeReal</c>; sizes that do not expand
    /// <c>MATLAB:sizeDimensionsMustMatch</c>. <c>hypot</c> of a complex value is of its magnitude.
    /// Null when the verb answers as it stands.
    /// </summary>
    private static JgsValue? McAtan2Hypot(BuiltinFunction inner, string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count != 2 || args.Any(static v => v.Type is JgsType.Sparse or JgsType.Object or JgsType.External))
        {
            return null;
        }

        foreach (JgsValue arg in args)
        {
            if (arg.Type == JgsType.Cell || NonFloatClassName(arg) is not null)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:math:mustBeFloat", "Invalid data type. Argument must be double or single.");
            }
        }

        bool complex = McAnyComplex(args[0]) || McAnyComplex(args[1]);
        if (complex && name == "atan2")
        {
            throw new JgsRuntimeException(line, col, "MATLAB:math:mustBeReal", "Argument must be real.");
        }

        if (McBroadcastDims(SizeDims(args[0]), SizeDims(args[1])) is null)
        {
            throw McSizesDiffer(line, col);
        }

        if (!complex)
        {
            return null;
        }

        static JgsValue Magnitude(JgsValue v, int line, int col)
        {
            if (!McAnyComplex(v))
            {
                return v;
            }

            double[] magnitudes = Array.ConvertAll(ColumnComplex("hypot", v, line, col), static z => double.Hypot(z.Real, z.Imaginary));
            int[] dims = SizeDims(v);
            JgsValue shaped = magnitudes.Length == 1 && dims.All(static d => d == 1)
                ? JgsValue.Number(magnitudes[0])
                : JgsMatrix.FromColumnMajorDims(magnitudes, dims);
            return JgsNumericClasses.Stamp(shaped, v.NumericClass == JgsNumericClass.Single ? JgsNumericClass.Single : JgsNumericClass.Double);
        }

        return inner.Call([Magnitude(args[0], line, col), Magnitude(args[1], line, col)], line, col);
    }
}
