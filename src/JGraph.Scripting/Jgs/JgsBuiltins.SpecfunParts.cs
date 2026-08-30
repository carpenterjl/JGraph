using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The seven remaining <c>specfun</c> names (M106): the elliptic integrals and functions
/// (<c>ellipke</c>, <c>ellipj</c>), the exponential integral (<c>expint</c>), the associated
/// Legendre functions (<c>legendre</c>), the rational approximations (<c>rat</c>, <c>rats</c>) and
/// the assignment problem (<c>matchpairs</c>).
/// </summary>
/// <remarks>
/// <para>
/// Two of these answer in text rather than in numbers. <c>rat</c> asked for one output writes the
/// continued fraction out — <c>3 + 1/(7 + 1/(16))</c> for π — and <c>rats</c> writes a whole matrix
/// as a column-aligned table of fractions, one row of characters per row of the matrix. Both are a
/// stack of char rows padded to a common width, which is a value this build only learned to hold in
/// M105; before that these two names had nothing to answer with.
/// </para>
/// <para>
/// The three iterative names hand the whole array to their engine rather than looping here, because
/// for all three the number of passes is a property of the array and not of the element. See
/// <see cref="EllipticFunctions"/>.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The spacing of one at double precision, which is every routine's default tolerance.</summary>
    private const double DoubleSpacing = 2.220446049250313e-16;

    /// <summary>Registers the remaining special functions into <paramref name="env"/>.</summary>
    internal static void RegisterSpecfunPartBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) => both(args, 1, line, col)[0])
            {
                MultiOutput = both,
            }));

        DefineBoth("ellipke", CompleteEllipticIntegrals);
        DefineBoth("ellipj", JacobiEllipticFunctions);
        Define("expint", ExponentialIntegralOf);
        Define("legendre", AssociatedLegendre);
        DefineBoth("rat", RationalApproximation);
        Define("rats", RationalTable);
        DefineBoth("matchpairs", MatchedPairs);
    }

    // --- ellipke ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>K = ellipke(m)</c>, <c>[K, E] = ellipke(m)</c> and <c>[K, E] = ellipke(m, tol)</c>: the
    /// complete elliptic integrals of the first and second kind.
    /// </summary>
    private static JgsValue[] CompleteEllipticIntegrals(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:ellipke:NotEnoughInputs",
                "Not enough input arguments.");
        }

        ArityRange("ellipke", args, 1, 2, line, col);
        if (HasComplexPart(args[0]) || (args.Count > 1 && HasComplexPart(args[1])))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:ellipke:ComplexInputs",
                "Input arguments must be real.");
        }

        int[] dims = JgsMatrix.DimsOf(args[0]);
        double[] m = FlattenColumnMajor("ellipke", args[0], line, col);

        // An empty parameter is answered before anything is checked, which is why ellipke([], -1)
        // is two empties rather than a complaint about the tolerance.
        if (m.Length == 0)
        {
            return Outputs(wanted, ShapedNumbers(m, dims), ShapedNumbers(new double[0], dims));
        }

        foreach (double value in m)
        {
            if (value < 0 || value > 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:ellipke:MOutOfRange",
                    "M must be in the range 0 <= M <= 1.");
            }
        }

        double tol = Tolerance("ellipke", args, 1, "Second", line, col);
        (double[] k, double[] e) = EllipticFunctions.Complete(m, tol);
        return Outputs(wanted, ShapedNumbers(k, dims), ShapedNumbers(e, dims));
    }

    // --- ellipj ----------------------------------------------------------------------------------

    /// <summary>
    /// <c>[SN, CN, DN] = ellipj(u, m)</c> and <c>… = ellipj(u, m, tol)</c>: the Jacobi elliptic
    /// functions.
    /// </summary>
    private static JgsValue[] JacobiEllipticFunctions(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:ellipj:NotEnoughInputs",
                "Not enough input arguments.");
        }

        ArityRange("ellipj", args, 2, 3, line, col);
        if (HasComplexPart(args[0]) || HasComplexPart(args[1])
            || (args.Count > 2 && HasComplexPart(args[2])))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:ellipj:ComplexInputs",
                "Input arguments must be real.");
        }

        double[] u = FlattenColumnMajor("ellipj", args[0], line, col);
        double[] m = FlattenColumnMajor("ellipj", args[1], line, col);
        int[] dims = JgsMatrix.DimsOf(args[0]);

        // A scalar on either side is spread over the other — and only a scalar, because ellipj is
        // one of the few numeric names in MATLAB that does not expand implicitly.
        if (m.Length == 1 && u.Length != 1)
        {
            m = Spread(m[0], u.Length);
        }
        else if (u.Length == 1 && m.Length != 1)
        {
            u = Spread(u[0], m.Length);
            dims = JgsMatrix.DimsOf(args[1]);
        }

        if (u.Length != m.Length)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:ellipj:InputSizeMismatch",
                "U and M must be the same size.");
        }

        foreach (double value in m)
        {
            if (!(value >= 0) || value > 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:ellipj:MOutOfRange",
                    "M must be in the range 0 <= M <= 1.");
            }
        }

        double tol = Tolerance("ellipj", args, 2, "Third", line, col);
        (double[] sn, double[] cn, double[] dn) = EllipticFunctions.Jacobi(u, m, tol);
        return Outputs(wanted, ShapedNumbers(sn, dims), ShapedNumbers(cn, dims), ShapedNumbers(dn, dims));
    }

    /// <summary>The optional trailing tolerance, checked the way both elliptic names check it.</summary>
    private static double Tolerance(
        string name, IReadOnlyList<JgsValue> args, int index, string ordinal, int line, int col)
    {
        if (args.Count <= index)
        {
            return DoubleSpacing;
        }

        JgsValue given = args[index];
        bool scalar = given.Type is JgsType.Number or JgsType.Bool
            || (given.Type == JgsType.Array && given.ArrayLength == 1);
        double tol = scalar ? Num(name, args, index, line, col) : double.NaN;
        if (!scalar || tol < 0 || !double.IsFinite(tol))
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:NegativeTolerance",
                $"{ordinal} argument TOL must be a finite nonnegative scalar.");
        }

        return tol;
    }

    private static double[] Spread(double value, int count)
    {
        var made = new double[count];
        Array.Fill(made, value);
        return made;
    }

    // --- expint ----------------------------------------------------------------------------------

    /// <summary>
    /// <c>Y = expint(X)</c>: the exponential integral E₁, which leaves the reals on the negative
    /// axis and so answers in complex there.
    /// </summary>
    private static JgsValue ExponentialIntegralOf(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        Arity("expint", args, 1, line, col);
        int[] dims = JgsMatrix.DimsOf(args[0]);
        Complex[] x = ComplexElements("expint", args[0], line, col);
        return ShapedFlatComplex(ExponentialIntegral.E1(x), dims);
    }

    // --- legendre --------------------------------------------------------------------------------

    /// <summary>
    /// <c>P = legendre(n, X)</c> and <c>P = legendre(n, X, normalization)</c>: every order of the
    /// associated Legendre functions of one degree, stacked down the first dimension.
    /// </summary>
    private static JgsValue AssociatedLegendre(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        ArityRange("legendre", args, 2, 3, line, col);
        JgsValue given = args[0];
        bool wholeScalar = given.Type is JgsType.Number or JgsType.Bool
            && !HasComplexPart(given)
            && double.IsFinite(given.AsNumber)
            && given.AsNumber >= 0
            && given.AsNumber == Math.Floor(given.AsNumber);
        if (!wholeScalar)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:legendre:InvalidN",
                "N must be a positive scalar integer.");
        }

        int n = (int)given.AsNumber;
        if (HasComplexPart(args[1]) || args[1].Type == JgsType.String || args[1].IsCharMatrix)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:legendre:InvalidX",
                "X must be real and in the range (-1,1).");
        }

        int[] sizeX = JgsMatrix.DimsOf(args[1]);
        double[] x = FlattenColumnMajor("legendre", args[1], line, col);

        // MATLAB reaches its range test through a `||`, and an empty argument makes that test an
        // empty rather than a yes or a no — which is an error before legendre has said anything.
        if (x.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:nonLogicalConditional",
                "Operands to the logical AND (&&) and OR (||) operators must be convertible to "
                + "logical scalar values. Use the ANY or ALL functions to reduce operands to "
                + "logical scalar values.");
        }

        foreach (double value in x)
        {
            if (Math.Abs(value) > 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:legendre:InvalidX",
                    "X must be real and in the range (-1,1).");
            }
        }

        LegendreScaling scaling = LegendreScaling.Unnormalized;
        if (args.Count > 2)
        {
            string word = TextOf(args[2]);
            scaling = word switch
            {
                "unnorm" => LegendreScaling.Unnormalized,
                "sch" => LegendreScaling.Schmidt,
                "norm" => LegendreScaling.Full,
                _ => throw new JgsRuntimeException(line, col, "MATLAB:legendre:InvalidNormalize",
                    $"Normalization option {word} not recognized"),
            };
        }

        double[] flat = LegendreFunctions.Associated(n, x, scaling);

        // Degree nought is the one case that keeps the argument's own shape; every other degree
        // stacks the orders down a new leading dimension.
        if (n == 0)
        {
            return IsVectorShape(sizeX)
                ? ShapedNumbers(flat, [1, x.Length])
                : ShapedNumbers(flat, sizeX);
        }

        return IsVectorShape(sizeX)
            ? ShapedNumbers(flat, [n + 1, x.Length])
            : ShapedNumbers(flat, [n + 1, .. sizeX]);
    }

    /// <summary>Whether a shape is a plain vector — two dimensions, one of them singleton.</summary>
    private static bool IsVectorShape(int[] dims) =>
        dims.Length <= 2 && (dims.Length < 2 || dims[0] == 1 || dims[1] == 1);

    // --- rat and rats ----------------------------------------------------------------------------

    /// <summary>
    /// <c>R = rat(X)</c>, <c>R = rat(X, tol)</c> and <c>[N, D] = rat(___)</c>: the continued
    /// fraction, spelled out when one output is asked for and reduced to a ratio when two are.
    /// </summary>
    private static JgsValue[] RationalApproximation(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        ArityRange("rat", args, 1, 2, line, col);
        int[] dims = JgsMatrix.DimsOf(args[0]);
        Complex[] x = ComplexElements("rat", args[0], line, col);
        double tol = args.Count > 1
            ? Num("rat", args, 1, line, col)
            : 1e-6 * FiniteNorm(Array.ConvertAll(x, static z => z.Real));

        bool complex = false;
        foreach (Complex z in x)
        {
            complex |= z.Imaginary != 0;
        }

        if (complex)
        {
            return ComplexRational(x, dims, tol, wanted, line, col);
        }

        double[] real = Array.ConvertAll(x, static z => z.Real);
        if (wanted > 1)
        {
            (double[] numerators, double[] denominators) = Ratios(real, tol);
            return [ShapedNumbers(numerators, dims), ShapedNumbers(denominators, dims)];
        }

        return [CharRows(Spellings(real, tol))];
    }

    /// <summary>
    /// The complex reading: an imaginary part small beside the real one is dropped outright,
    /// otherwise the two halves are approximated separately and put back together over their
    /// common denominator — or, when only one output is asked for, stacked as text with a marker
    /// row between them.
    /// </summary>
    private static JgsValue[] ComplexRational(
        Complex[] x, int[] dims, double tol, int wanted, int line, int col)
    {
        var real = Array.ConvertAll(x, static z => z.Real);
        var imaginary = Array.ConvertAll(x, static z => z.Imaginary);
        if (FiniteNorm(imaginary) <= tol * FiniteNorm(real))
        {
            return wanted > 1
                ? Pair(Ratios(real, tol), dims)
                : [CharRows(Spellings(real, tol))];
        }

        if (wanted > 1)
        {
            (double[] nr, double[] dr) = Ratios(real, tol);
            (double[] ni, double[] di) = Ratios(imaginary, tol);
            var numerators = new Complex[x.Length];
            var denominators = new double[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                double shared = LeastCommonMultiple(dr[i], di[i]);
                denominators[i] = shared;
                numerators[i] = new Complex(shared / dr[i] * nr[i], shared / di[i] * ni[i]);
            }

            return [ShapedFlatComplex(numerators, dims), ShapedNumbers(denominators, dims)];
        }

        var rows = new List<string>();
        rows.AddRange(Spellings(real, tol));
        rows.Add(" +i* ...");
        rows.AddRange(Spellings(imaginary, tol));
        return [CharRows([.. rows])];
    }

    private static JgsValue[] Pair((double[] N, double[] D) ratios, int[] dims) =>
        [ShapedNumbers(ratios.N, dims), ShapedNumbers(ratios.D, dims)];

    /// <summary>Every element's convergent, in the order the elements are stored.</summary>
    private static (double[] N, double[] D) Ratios(double[] values, double tol)
    {
        var numerators = new double[values.Length];
        var denominators = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            double value = values[i];
            if (!double.IsFinite(value))
            {
                // An infinity is a whole number over nothing; a NaN is nothing over nothing.
                numerators[i] = double.IsNaN(value) ? 0 : Math.Sign(value);
                denominators[i] = 0;
                continue;
            }

            ContinuedFractions.Expansion expansion = ContinuedFractions.Expand(value, tol);
            numerators[i] = expansion.Numerator;
            denominators[i] = expansion.Denominator;
        }

        return (numerators, denominators);
    }

    /// <summary>Every element's expansion written out, in the order the elements are stored.</summary>
    private static string[] Spellings(double[] values, double tol)
    {
        var rows = new string[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            rows[i] = double.IsFinite(values[i])
                ? ContinuedFractions.Spell(ContinuedFractions.Expand(values[i], tol).Terms)
                : ContinuedFractions.Whole(values[i]);
        }

        return rows;
    }

    /// <summary>
    /// <c>S = rats(X)</c> and <c>S = rats(X, strlen)</c>: the matrix written as a table of
    /// fractions, every entry given the same width whether it needs it or not.
    /// </summary>
    private static JgsValue RationalTable(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        ArityRange("rats", args, 1, 2, line, col);
        double width = args.Count > 1 ? Num("rats", args, 1, line, col) : 13;
        if (!double.IsFinite(width))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:nonaninf", "NaN and Inf not allowed.");
        }

        int[] dims = JgsMatrix.DimsOf(args[0]);
        Complex[] x = ComplexElements("rats", args[0], line, col);
        if (x.Length == 0)
        {
            return JgsValue.Str(string.Empty);
        }

        int rows = dims.Length > 0 ? dims[0] : 1;
        int columns = rows == 0 ? 0 : x.Length / Math.Max(rows, 1);
        int len = (int)width;
        double half = (width - 1) / 2.0;
        int top = (int)Math.Floor(half);
        int bottom = (int)Math.Ceiling(half);

        bool complex = false;
        foreach (Complex z in x)
        {
            complex |= z.Imaginary != 0;
        }

        var real = Array.ConvertAll(x, static z => z.Real);
        double realTol = Math.Min(Math.Pow(10, -half) * FiniteNorm(real), 0.1);
        (double[] nr, double[] dr) = Ratios(real, realTol);
        double[] ni = [];
        double[] di = [];
        if (complex)
        {
            var imaginary = Array.ConvertAll(x, static z => z.Imaginary);
            double imaginaryTol = Math.Min(Math.Pow(10, -half) * FiniteNorm(imaginary), 0.1);
            (ni, di) = Ratios(imaginary, imaginaryTol);
        }

        var lines = new string[rows];
        for (int r = 0; r < rows; r++)
        {
            var built = new StringBuilder();
            for (int c = 0; c < columns; c++)
            {
                int at = (c * rows) + r;
                built.Append(' ');
                built.Append(Entry(nr[at], dr[at], len, top, bottom));
                if (complex)
                {
                    built.Append(ImaginaryEntry(ni[at], di[at], len, top, bottom));
                }
            }

            lines[r] = built.ToString();
        }

        return CharRows(lines);
    }

    /// <summary>One real entry of the table: a fraction, or a whole number centred in the width.</summary>
    private static string Entry(double numerator, double denominator, int len, int top, int bottom)
    {
        string written = denominator != 1
            ? Right(numerator, top) + "/" + Left(denominator, bottom)
            : Centre(ContinuedFractions.Whole(numerator), len);
        return written.Length > len ? Centre("*", len) : written;
    }

    /// <summary>One imaginary entry, which carries its own sign in front of it.</summary>
    private static string ImaginaryEntry(double numerator, double denominator, int len, int top, int bottom)
    {
        string sign = numerator >= 0 ? "+" : "-";
        double size = Math.Abs(numerator);
        string written = denominator != 1
            ? sign + Right(size, top) + "/" + PadRight(ContinuedFractions.Whole(denominator) + "i", bottom + 1)
            : sign + Centre(ContinuedFractions.Whole(size) + "i", len + 1);
        return written.Length > len + 2 ? sign + Centre("*i", len + 1) : written;
    }

    private static string Right(double value, int width) =>
        ContinuedFractions.Whole(value).PadLeft(width);

    private static string Left(double value, int width) =>
        ContinuedFractions.Whole(value).PadRight(width);

    private static string PadRight(string text, int width) =>
        text.Length >= width ? text : text.PadRight(width);

    /// <summary>Centres text in a width, leaning left when the padding will not halve evenly.</summary>
    private static string Centre(string text, int width)
    {
        int padding = width - text.Length;
        if (padding <= 0)
        {
            return text;
        }

        return new string(' ', padding / 2) + text + new string(' ', padding - (padding / 2));
    }

    /// <summary>The one-norm over the finite entries, which is what both names measure against.</summary>
    private static double FiniteNorm(double[] values)
    {
        double total = 0;
        foreach (double value in values)
        {
            if (double.IsFinite(value))
            {
                total += Math.Abs(value);
            }
        }

        return total;
    }

    private static double LeastCommonMultiple(double a, double b)
    {
        double divisor = GreatestCommonDivisor(Math.Abs(a), Math.Abs(b));
        return divisor == 0 ? 0 : Math.Abs(a / divisor * b);
    }

    private static double GreatestCommonDivisor(double a, double b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }

    /// <summary>
    /// A stack of char rows as one value — a plain char row when there is only one of them, which
    /// is what <c>rat(pi)</c> is.
    /// </summary>
    private static JgsValue CharRows(string[] rows) =>
        rows.Length == 1 ? JgsValue.Str(rows[0]) : JgsValue.CharMatrix(rows);

    // --- matchpairs ------------------------------------------------------------------------------

    /// <summary>
    /// <c>M = matchpairs(Cost, costUnmatched)</c>, <c>[M, uR, uC] = …</c> and
    /// <c>… = matchpairs(Cost, costUnmatched, goal)</c>: the cheapest way to pair rows with columns
    /// when leaving one out has a price of its own.
    /// </summary>
    private static JgsValue[] MatchedPairs(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        ArityRange("matchpairs", args, 2, 3, line, col);
        int[] dims = JgsMatrix.DimsOf(args[0]);
        if (dims.Length > 2 || HasComplexPart(args[0]) || !IsFloatValue(args[0]))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:matchpairs:InvalidCost",
                "Cost must be a real matrix of data type double or single.");
        }

        double[] cost = FlattenColumnMajor("matchpairs", args[0], line, col);
        foreach (double value in cost)
        {
            if (double.IsNaN(value))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:matchpairs:NonFiniteCost",
                    "Cost must not contain NaN.");
            }
        }

        JgsValue given = args[1];
        bool scalar = given.Type is JgsType.Number or JgsType.Bool
            || (given.Type == JgsType.Array && given.ArrayLength == 1);
        if (!scalar || HasComplexPart(given) || !IsFloatValue(given))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:matchpairs:InvalidCostUnmatched",
                "costUnmatched must be a real scalar of data type double or single.");
        }

        double unmatched = Num("matchpairs", args, 1, line, col);
        if (!double.IsFinite(unmatched))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:matchpairs:NonFiniteCostUnmatched",
                "costUnmatched must be finite.");
        }

        bool minimize = true;
        if (args.Count > 2)
        {
            if (!IsTextScalar(args[2]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:matchpairs:InvalidOption",
                    "Option must be 'min' or 'max'.");
            }

            string goal = TextOf(args[2]);
            bool wantsMin = goal.Length > 0 && "min".StartsWith(goal, StringComparison.OrdinalIgnoreCase);
            bool wantsMax = goal.Length > 0 && "max".StartsWith(goal, StringComparison.OrdinalIgnoreCase);
            if (wantsMin == wantsMax)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:matchpairs:InvalidOption",
                    "Option must be 'min' or 'max'.");
            }

            minimize = wantsMin;
        }

        if (!minimize)
        {
            cost = Array.ConvertAll(cost, static c => -c);
            unmatched = -unmatched;
        }

        int m = dims.Length > 0 ? dims[0] : 0;
        int n = dims.Length > 1 ? dims[1] : 0;
        return AssignedPairs(cost, m, n, unmatched, wanted);
    }

    /// <summary>
    /// The matching itself. The rectangular problem is squared off by writing the cost matrix into
    /// the top-left of an (m+n)-by-(m+n) block and its transpose into the bottom-right, with the
    /// price of leaving a row or a column out sitting where the two blocks meet — so a perfect
    /// matching of the square block is exactly a partial matching of the original, and "unmatched"
    /// becomes a pairing like any other rather than a case to be handled apart.
    /// </summary>
    private static JgsValue[] AssignedPairs(double[] cost, int m, int n, double unmatched, int wanted)
    {
        int side = m + n;
        double leaveOut = 2 * unmatched;
        var padded = new double[side * side];
        Array.Fill(padded, double.PositiveInfinity);
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < m; i++)
            {
                padded[(j * side) + i] = cost[(j * m) + i];
                padded[((n + i) * side) + m + j] = cost[(j * m) + i];
            }
        }

        for (int i = 0; i < m; i++)
        {
            padded[((n + i) * side) + i] = leaveOut;
        }

        for (int j = 0; j < n; j++)
        {
            padded[(j * side) + m + j] = leaveOut;
        }

        (int[] columnToRow, int[] rowToColumn) = side == 0
            ? ([], [])
            : Assignment.PerfectMatching(padded, side);

        // A pair that costs exactly what leaving both out would cost is left out: the two readings
        // are worth the same, and this is the one MATLAB reports.
        for (int c = 0; c < n; c++)
        {
            int r = columnToRow[c];
            if (r < m && cost[(c * m) + r] == leaveOut)
            {
                columnToRow[c] = side;
                rowToColumn[r] = side;
            }
        }

        var pairs = new List<double>();
        var columns = new List<double>();
        for (int c = 0; c < n; c++)
        {
            if (columnToRow[c] < m)
            {
                pairs.Add(columnToRow[c] + 1);
                columns.Add(c + 1);
            }
        }

        var flat = new double[pairs.Count * 2];
        for (int i = 0; i < pairs.Count; i++)
        {
            flat[i] = pairs[i];
            flat[pairs.Count + i] = columns[i];
        }

        JgsValue matching = pairs.Count == 0
            ? JgsEmpty.Shaped(0, 2)
            : JgsMatrix.FromColumnMajor(flat, pairs.Count, 2);

        var freeRows = new List<double>();
        for (int r = 0; r < m; r++)
        {
            if (rowToColumn[r] >= n)
            {
                freeRows.Add(r + 1);
            }
        }

        var freeColumns = new List<double>();
        for (int c = 0; c < n; c++)
        {
            if (columnToRow[c] >= m)
            {
                freeColumns.Add(c + 1);
            }
        }

        return Outputs(wanted, matching, Column(freeRows), Column(freeColumns));
    }

    /// <summary>A list of indices as a column, empty or not.</summary>
    private static JgsValue Column(List<double> values) =>
        values.Count == 0 ? JgsEmpty.Shaped(0, 1) : ShapedNumbers([.. values], [values.Count, 1]);

    /// <summary>Whether a value's class is one MATLAB counts as floating point.</summary>
    private static bool IsFloatValue(JgsValue value) =>
        !IsLogicalValue(value)
        && !value.IsCharMatrix
        && value.Type is JgsType.Number or JgsType.Complex or JgsType.Array
        && value.NumericClass is JgsNumericClass.Double or JgsNumericClass.Single;
}
