using System.Numerics;
using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The Signal Processing Toolbox's coefficient conversions: between transfer functions, roots, state
/// space, cascaded sections, lattices and partial fractions (M133).
/// </summary>
/// <remarks>
/// <para>
/// Twenty-four names for what is really one triangle. A filter is a ratio of polynomials, or the
/// roots of those polynomials, or a first-order recurrence in a state vector, or a chain of short
/// ratios; every conversion in this file walks one edge of that shape, and the ones with longer
/// names walk two.
/// </para>
/// <para>
/// The care is all in the shapes and the edges. A numerator may be a matrix, in which case its rows
/// are separate outputs of one system and its zeros come back as one column each. A cascade may or
/// may not carry its own gain. A lattice's ladder coefficients may be shorter than its reflection
/// coefficients and get padded. None of that is arithmetic and all of it changes the answer.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the twenty-four coefficient conversions.</summary>
    internal static void RegisterFilterConversionBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void DefineMulti(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            Define(name, (args, line, col) => body(args, 1, line, col)[0], body);

        DefineMulti("tf2zp", TfToZeroPole);
        DefineMulti("tf2zpk", TfToZeroPoleK);
        DefineMulti("zp2tf", ZeroPoleToTf);
        DefineMulti("tf2ss", TfToStateSpace);
        DefineMulti("ss2tf", StateSpaceToTf);
        DefineMulti("ss2zp", StateSpaceToZeroPole);
        DefineMulti("zp2ss", ZeroPoleToStateSpace);
        DefineMulti("eqtflength", EqualiseLengths);
        Define("polystab", StabilisePolynomial);
        Define("polyscale", ScalePolynomial);
        DefineMulti("residuez", ResidueZ);

        DefineMulti("zp2sos", ZeroPoleToSections);
        DefineMulti("tf2sos", TfToSections);
        DefineMulti("ss2sos", StateSpaceToSections);
        DefineMulti("sos2zp", SectionsToZeroPole);
        DefineMulti("sos2tf", SectionsToTf);
        DefineMulti("sos2ss", SectionsToStateSpace);
        DefineMulti("sos2ctf", SectionsToCascade);
        DefineMulti("zp2ctf", ZeroPoleToCascade);
        Define("sos2cell", SectionsToCell);
        DefineMulti("cell2sos", CellToSections);
        Define("scaleFilterSections", ScaleCascadeSections);

        DefineMulti("tf2latc", TfToLattice);
        DefineMulti("latc2tf", LatticeToTf);
    }

    // --- Shape helpers ---------------------------------------------------------------------------

    /// <summary>One argument read as a two-dimensional block, column major.</summary>
    private static (double[] Flat, int Rows, int Columns) FilterMatrix(
        string name, JgsValue value, int line, int col)
    {
        double[] flat = ToDoubles(name, value, line, col);
        int[] dims = SizeDims(value);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int columns = 1;
        for (int i = 1; i < dims.Length; i++)
        {
            columns *= dims[i];
        }

        if (dims.Length > 2)
        {
            throw new JgsRuntimeException(line, col, $"{name} reads a matrix, not a higher-dimensional array.");
        }

        return (flat, rows, columns);
    }

    /// <summary>A row-indexed matrix turned back into a value.</summary>
    private static JgsValue FilterMatrixValue(double[,] m)
    {
        int rows = m.GetLength(0);
        int columns = m.GetLength(1);
        var flat = new double[rows * columns];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                flat[(c * rows) + r] = m[r, c];
            }
        }

        return JgsMatrix.FromColumnMajor(flat, rows, columns);
    }

    /// <summary>A vector argument read as a row of coefficients.</summary>
    private static double[] FilterVector(string name, JgsValue value, int line, int col)
    {
        int[] dims = SizeDims(value);
        if (dims.Length > 2 || (dims.Length == 2 && dims[0] != 1 && dims[1] != 1 && ElementCount(value) > 0))
        {
            throw new JgsRuntimeException(line, col, $"{name} reads a vector of coefficients.");
        }

        return ToDoubles(name, value, line, col);
    }

    /// <summary>A root list read as complex, whatever shape it arrived in.</summary>
    private static Complex[] FilterRoots(string name, JgsValue value, int line, int col) =>
        ElementCount(value) == 0 ? [] : ComplexArrayOf(name, value, line, col);

    /// <summary>The word an option argument carries, lower-cased and matched by its prefix.</summary>
    private static string FilterWord(
        string name, JgsValue value, int line, int col, params string[] allowed)
    {
        string given = StrOf(name, value, line, col).ToLowerInvariant();
        foreach (string candidate in allowed)
        {
            if (given.Length > 0 && given.Length <= candidate.Length
                && candidate.StartsWith(given, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        throw new JgsRuntimeException(line, col,
            $"{name} takes {string.Join(" or ", allowed.Select(w => $"'{w}'"))}, but got '{given}'.");
    }

    // --- Transfer function and roots ---------------------------------------------------------------

    /// <summary><c>[z, p, k] = tf2zp(num, den)</c>.</summary>
    private static JgsValue[] TfToZeroPole(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("tf2zp", args, 2, line, col);
        (double[] num, int rows, int columns) = FilterMatrix("tf2zp", args[0], line, col);
        double[] den = FilterVector("tf2zp", args[1], line, col);

        if (columns > den.Length)
        {
            throw new JgsRuntimeException(line, col,
                "tf2zp needs a numerator no longer than its denominator.");
        }

        var numerator = new Complex[num.Length];
        for (int i = 0; i < num.Length; i++)
        {
            numerator[i] = num[i];
        }

        var denominator = new Complex[den.Length];
        for (int i = 0; i < den.Length; i++)
        {
            denominator[i] = den[i];
        }

        FilterCoefficients.Zpk answer;
        try
        {
            answer = FilterCoefficients.TfToZp(numerator, rows, columns, denominator);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"tf2zp: {ex.Message}");
        }

        JgsValue zeros = ComplexShaped(answer.Zeros, [answer.ZeroRows, rows]);
        return wanted <= 1
            ? [zeros]
            : wanted == 2
                ? [zeros, ComplexColumn(answer.Poles)]
                : [zeros, ComplexColumn(answer.Poles), ComplexColumn(answer.Gains)];
    }

    /// <summary><c>[z, p, k] = tf2zpk(b, a)</c>: the same after both sides are padded.</summary>
    private static JgsValue[] TfToZeroPoleK(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("tf2zpk", args, 1, 2, line, col);
        double[] b = FilterVector("tf2zpk", args[0], line, col);
        double[] a = args.Count >= 2 && ElementCount(args[1]) > 0
            ? FilterVector("tf2zpk", args[1], line, col)
            : [1.0];

        FilterCoefficients.Zpk answer;
        try
        {
            answer = FilterCoefficients.TfToZpk(b, a);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"tf2zpk: {ex.Message}");
        }

        JgsValue zeros = ComplexColumn(answer.Zeros);
        return wanted <= 1
            ? [zeros]
            : wanted == 2
                ? [zeros, ComplexColumn(answer.Poles)]
                : [zeros, ComplexColumn(answer.Poles), ComplexScalarValue(answer.Gains)];
    }

    /// <summary>A one-entry gain list read back as a scalar, real when it has no imaginary part.</summary>
    private static JgsValue ComplexScalarValue(Complex[] gains)
    {
        Complex k = gains.Length > 0 ? gains[0] : Complex.Zero;
        return k.Imaginary == 0 ? JgsValue.Number(k.Real) : JgsValue.ComplexNum(k);
    }

    /// <summary><c>[num, den] = zp2tf(z, p, k)</c>.</summary>
    private static JgsValue[] ZeroPoleToTf(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("zp2tf", args, 3, line, col);
        int[] zeroDims = SizeDims(args[0]);
        Complex[] zeros = FilterRoots("zp2tf", args[0], line, col);
        Complex[] poles = FilterRoots("zp2tf", args[1], line, col);
        double[] gains = ToDoubles("zp2tf", args[2], line, col);

        int zeroRows = zeros.Length == 0 ? 0 : zeroDims.Length > 0 ? zeroDims[0] : 0;
        int zeroColumns = zeros.Length == 0 ? 0 : zeros.Length / System.Math.Max(1, zeroRows);
        if (zeroRows == 1 && zeroColumns > 1)
        {
            // A row of zeros is one system's zeros, not one zero each for several systems.
            (zeroRows, zeroColumns) = (zeroColumns, 1);
        }

        if (zeros.Length > 0 && zeroColumns != gains.Length)
        {
            throw new JgsRuntimeException(line, col,
                "zp2tf needs one gain for each column of zeros.");
        }

        (double[] num, int rows, int columns, double[] den) =
            FilterCoefficients.ZpToTf(zeros, zeroRows, zeroColumns, poles, gains);

        JgsValue numerator = JgsMatrix.FromColumnMajor(num, rows, columns);
        return wanted <= 1
            ? [numerator]
            : [numerator, JgsMatrix.FromColumnMajor(den, 1, den.Length)];
    }

    /// <summary><c>[a, b, c, d] = tf2ss(num, den)</c>.</summary>
    private static JgsValue[] TfToStateSpace(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("tf2ss", args, 2, line, col);
        (double[] num, int rows, int columns) = FilterMatrix("tf2ss", args[0], line, col);
        double[] den = FilterVector("tf2ss", args[1], line, col);

        FilterCoefficients.StateSpace ss;
        try
        {
            ss = FilterCoefficients.TfToSs(num, rows, columns, den);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"tf2ss: {ex.Message}");
        }

        return StateSpaceValues(ss, wanted);
    }

    /// <summary>A state-space quadruple as one to four values.</summary>
    private static JgsValue[] StateSpaceValues(FilterCoefficients.StateSpace ss, int wanted)
    {
        JgsValue[] all =
        [
            FilterMatrixValue(ss.A),
            FilterMatrixValue(ss.B),
            FilterMatrixValue(ss.C),
            FilterMatrixValue(ss.D),
        ];

        return all[..System.Math.Clamp(wanted, 1, 4)];
    }

    /// <summary><c>[num, den] = ss2tf(a, b, c, d, iu)</c>.</summary>
    private static JgsValue[] StateSpaceToTf(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("ss2tf", args, 4, 5, line, col);
        (double[,] a, double[,] b, double[,] c, double[,] d, int input) =
            ReadStateSpace("ss2tf", args, 4, line, col);

        (double[] num, double[] den) = FilterCoefficients.SsToTf(a, b, c, d, input);
        JgsValue numerator = JgsMatrix.FromColumnMajor(num, 1, num.Length);
        return wanted <= 1
            ? [numerator]
            : [numerator, JgsMatrix.FromColumnMajor(den, 1, den.Length)];
    }

    /// <summary><c>[z, p, k] = ss2zp(a, b, c, d, iu)</c>.</summary>
    private static JgsValue[] StateSpaceToZeroPole(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("ss2zp", args, 4, 5, line, col);
        (double[,] a, double[,] b, double[,] c, double[,] d, int input) =
            ReadStateSpace("ss2zp", args, 4, line, col);

        (Complex[] zeros, Complex[] poles, double gain) = FilterCoefficients.SsToZp(a, b, c, d, input);
        return wanted <= 1
            ? [ComplexColumn(zeros)]
            : wanted == 2
                ? [ComplexColumn(zeros), ComplexColumn(poles)]
                : [ComplexColumn(zeros), ComplexColumn(poles), JgsValue.Number(gain)];
    }

    /// <summary><c>[a, b, c, d] = zp2ss(z, p, k)</c>.</summary>
    private static JgsValue[] ZeroPoleToStateSpace(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("zp2ss", args, 3, line, col);
        Complex[] zeros = FilterRoots("zp2ss", args[0], line, col);
        Complex[] poles = FilterRoots("zp2ss", args[1], line, col);
        double gain = Num("zp2ss", args, 2, line, col);

        if (zeros.Length > poles.Length)
        {
            throw new JgsRuntimeException(line, col, "zp2ss needs no more zeros than poles.");
        }

        FilterCoefficients.StateSpace ss = FilterCoefficients.ZpToSs(zeros, poles, gain);
        return StateSpaceValues(ss, wanted);
    }

    /// <summary>Four matrices and the input index that selects a column of B and D.</summary>
    private static (double[,] A, double[,] B, double[,] C, double[,] D, int Input) ReadStateSpace(
        string name, IReadOnlyList<JgsValue> args, int at, int line, int col)
    {
        double[,] a = ReadBlock(name, args[0], line, col);
        double[,] b = ReadBlock(name, args[1], line, col);
        double[,] c = ReadBlock(name, args[2], line, col);
        double[,] d = ReadBlock(name, args[3], line, col);

        int input = 0;
        if (args.Count > at && !IsEmptyValue(args[at]))
        {
            input = Count(name, args, at, line, col) - 1;
        }

        if (input < 0 || (d.GetLength(1) > 0 && input >= d.GetLength(1)))
        {
            throw new JgsRuntimeException(line, col, $"{name}'s input index is outside the system's inputs.");
        }

        if (c.GetLength(0) > 1)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} reads a system with one output; several outputs are not implemented.");
        }

        return (a, b, c, d, input);
    }

    /// <summary>One argument read as a row-indexed matrix.</summary>
    private static double[,] ReadBlock(string name, JgsValue value, int line, int col)
    {
        (double[] flat, int rows, int columns) = FilterMatrix(name, value, line, col);
        var block = new double[rows, columns];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                block[r, c] = flat[(c * rows) + r];
            }
        }

        return block;
    }

    /// <summary><c>[b, a, n, m] = eqtflength(num, den)</c>.</summary>
    private static JgsValue[] EqualiseLengths(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("eqtflength", args, 2, line, col);
        double[] num = ElementCount(args[0]) == 0 ? [0.0] : FilterVector("eqtflength", args[0], line, col);
        double[] den = FilterVector("eqtflength", args[1], line, col);

        double[] b;
        double[] a;
        int n;
        int m;
        try
        {
            (b, a, n, m) = FilterCoefficients.EqualLengths(num, den);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"eqtflength: {ex.Message}");
        }

        JgsValue[] all =
        [
            JgsMatrix.FromColumnMajor(b, 1, b.Length),
            JgsMatrix.FromColumnMajor(a, 1, a.Length),
            JgsValue.Number(n),
            JgsValue.Number(m),
        ];

        return all[..System.Math.Clamp(wanted, 1, 4)];
    }

    /// <summary><c>polystab(a)</c>.</summary>
    private static JgsValue StabilisePolynomial(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("polystab", args, 1, line, col);
        if (ElementCount(args[0]) <= 1)
        {
            return args[0];
        }

        double[] a = FilterVector("polystab", args[0], line, col);
        double[] b = FilterCoefficients.PolyStabilise(a, out _);
        return SignalIsRow(args[0])
            ? JgsMatrix.FromColumnMajor(b, 1, b.Length)
            : JgsMatrix.FromColumnMajor(b, b.Length, 1);
    }

    /// <summary><c>polyscale(a, scale)</c>.</summary>
    private static JgsValue ScalePolynomial(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("polyscale", args, 2, line, col);
        double[] p = ToDoubles("polyscale", args[0], line, col);
        double scale = Num("polyscale", args, 1, line, col);
        double[] y = FilterCoefficients.PolyScale(p, scale);
        int[] dims = SizeDims(args[0]);
        return JgsMatrix.FromColumnMajorDims(y, dims);
    }

    /// <summary><c>[r, p, k] = residuez(b, a)</c> and its inverse <c>[b, a] = residuez(r, p, k)</c>.</summary>
    private static JgsValue[] ResidueZ(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("residuez", args, 2, 3, line, col);

        if (args.Count == 2)
        {
            double[] b = FilterVector("residuez", args[0], line, col);
            double[] a = FilterVector("residuez", args[1], line, col);

            FilterCoefficients.Expansion expansion;
            try
            {
                expansion = FilterCoefficients.Residuez(b, a);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"residuez: {ex.Message}");
            }

            JgsValue residues = ComplexColumn(expansion.Residues);
            return wanted <= 1
                ? [residues]
                : wanted == 2
                    ? [residues, ComplexColumn(expansion.Poles)]
                    : [residues, ComplexColumn(expansion.Poles), ComplexRow(expansion.Direct)];
        }

        Complex[] r = FilterRoots("residuez", args[0], line, col);
        Complex[] p = FilterRoots("residuez", args[1], line, col);
        Complex[] k = ElementCount(args[2]) == 0 ? [] : FilterRoots("residuez", args[2], line, col);

        if (r.Length != p.Length)
        {
            throw new JgsRuntimeException(line, col,
                "residuez needs as many residues as poles.");
        }

        (Complex[] numerator, Complex[] denominator) = FilterCoefficients.ResiduezInverse(r, p, k);
        return wanted <= 1
            ? [ComplexRow(numerator)]
            : [ComplexRow(numerator), ComplexRow(denominator)];
    }

    // --- Second-order sections ---------------------------------------------------------------------

    /// <summary>A cascade argument read as its column-major block and its section count.</summary>
    private static (double[] Rows, int Sections) ReadSections(
        string name, JgsValue value, int line, int col)
    {
        (double[] flat, int rows, int columns) = FilterMatrix(name, value, line, col);
        if (columns != 6)
        {
            throw new JgsRuntimeException(line, col, $"{name} reads a cascade of six columns.");
        }

        return (flat, rows);
    }

    /// <summary>The ordering and scaling words <c>zp2sos</c> and its relatives share.</summary>
    private static (SecondOrderSections.Direction Direction, SecondOrderSections.Scaling Scaling) ReadCascadeOptions(
        string name, IReadOnlyList<JgsValue> args, int at, int line, int col)
    {
        var direction = SecondOrderSections.Direction.Up;
        var scaling = SecondOrderSections.Scaling.None;

        if (args.Count > at && !IsEmptyValue(args[at]))
        {
            direction = FilterWord(name, args[at], line, col, "up", "down") == "down"
                ? SecondOrderSections.Direction.Down
                : SecondOrderSections.Direction.Up;
        }

        if (args.Count > at + 1 && !IsEmptyValue(args[at + 1]))
        {
            JgsValue given = args[at + 1];
            if (IsTextScalar(given))
            {
                scaling = FilterWord(name, given, line, col, "none", "inf", "two") switch
                {
                    "inf" => SecondOrderSections.Scaling.Infinity,
                    "two" => SecondOrderSections.Scaling.Two,
                    _ => SecondOrderSections.Scaling.None,
                };
            }
            else
            {
                double value = Num(name, args, at + 1, line, col);
                scaling = double.IsPositiveInfinity(value)
                    ? SecondOrderSections.Scaling.Infinity
                    : value == 2
                        ? SecondOrderSections.Scaling.Two
                        : throw new JgsRuntimeException(line, col,
                            $"{name}'s scaling is 'none', Inf or 2.");
            }
        }

        return (direction, scaling);
    }

    /// <summary><c>[sos, g] = zp2sos(z, p, k, dir, scale, krz)</c>.</summary>
    private static JgsValue[] ZeroPoleToSections(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("zp2sos", args, 2, 6, line, col);
        Complex[] zeros = FilterRoots("zp2sos", args[0], line, col);
        Complex[] poles = FilterRoots("zp2sos", args[1], line, col);
        double gain = args.Count >= 3 && !IsEmptyValue(args[2]) ? Num("zp2sos", args, 2, line, col) : 1;

        (SecondOrderSections.Direction direction, SecondOrderSections.Scaling scaling) =
            ReadCascadeOptions("zp2sos", args, 3, line, col);

        bool keepRealPairs = args.Count >= 6 && !IsEmptyValue(args[5])
            && Num("zp2sos", args, 5, line, col) != 0;

        SecondOrderSections.Cascade cascade;
        try
        {
            cascade = SecondOrderSections.FromRoots(zeros, poles, gain, direction, scaling, keepRealPairs);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"zp2sos: {ex.Message}");
        }

        return CascadeValues(cascade, wanted);
    }

    /// <summary>A cascade as one or two values, the gain folded in when only one is wanted.</summary>
    private static JgsValue[] CascadeValues(SecondOrderSections.Cascade cascade, int wanted)
    {
        double[] rows = cascade.Sections;
        if (wanted <= 1)
        {
            var folded = new double[rows.Length];
            Array.Copy(rows, folded, rows.Length);
            for (int c = 0; c < 3; c++)
            {
                folded[c * cascade.Rows] *= cascade.Gain;
            }

            return [JgsMatrix.FromColumnMajor(folded, cascade.Rows, cascade.Columns)];
        }

        return
        [
            JgsMatrix.FromColumnMajor(rows, cascade.Rows, cascade.Columns),
            JgsValue.Number(cascade.Gain),
        ];
    }

    /// <summary><c>[sos, g] = tf2sos(b, a, dir, scale)</c>.</summary>
    private static JgsValue[] TfToSections(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("tf2sos", args, 2, 4, line, col);
        double[] b = FilterVector("tf2sos", args[0], line, col);
        double[] a = FilterVector("tf2sos", args[1], line, col);

        FilterCoefficients.Zpk roots;
        try
        {
            roots = FilterCoefficients.TfToZpk(b, a);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"tf2sos: {ex.Message}");
        }

        (SecondOrderSections.Direction direction, SecondOrderSections.Scaling scaling) =
            ReadCascadeOptions("tf2sos", args, 2, line, col);

        double gain = roots.Gains.Length > 0 ? roots.Gains[0].Real : 1;
        SecondOrderSections.Cascade cascade =
            SecondOrderSections.FromRoots(roots.Zeros, roots.Poles, gain, direction, scaling, false);
        return CascadeValues(cascade, wanted);
    }

    /// <summary><c>[sos, g] = ss2sos(a, b, c, d, iu, dir, scale)</c>.</summary>
    private static JgsValue[] StateSpaceToSections(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("ss2sos", args, 4, 7, line, col);

        int at = 4;
        int input = 0;
        if (args.Count > at && !IsEmptyValue(args[at]) && !IsTextScalar(args[at]))
        {
            input = Count("ss2sos", args, at, line, col) - 1;
            at++;
        }

        double[,] a = ReadBlock("ss2sos", args[0], line, col);
        double[,] b = ReadBlock("ss2sos", args[1], line, col);
        double[,] c = ReadBlock("ss2sos", args[2], line, col);
        double[,] d = ReadBlock("ss2sos", args[3], line, col);

        if (input < 0 || (d.GetLength(1) > 0 && input >= d.GetLength(1)))
        {
            throw new JgsRuntimeException(line, col, "ss2sos's input index is outside the system's inputs.");
        }

        (Complex[] zeros, Complex[] poles, double gain) = FilterCoefficients.SsToZp(a, b, c, d, input);
        (SecondOrderSections.Direction direction, SecondOrderSections.Scaling scaling) =
            ReadCascadeOptions("ss2sos", args, at, line, col);

        SecondOrderSections.Cascade cascade =
            SecondOrderSections.FromRoots(zeros, poles, gain, direction, scaling, false);
        return CascadeValues(cascade, wanted);
    }

    /// <summary><c>[z, p, k] = sos2zp(sos, g)</c>.</summary>
    private static JgsValue[] SectionsToZeroPole(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("sos2zp", args, 1, 2, line, col);
        (double[] rows, int sections) = ReadSections("sos2zp", args[0], line, col);
        double gain = args.Count >= 2 ? Num("sos2zp", args, 1, line, col) : 1;

        (Complex[] zeros, Complex[] poles, Complex k) = SecondOrderSections.ToRoots(rows, sections, gain);
        return wanted <= 1
            ? [ComplexColumn(zeros)]
            : wanted == 2
                ? [ComplexColumn(zeros), ComplexColumn(poles)]
                : [ComplexColumn(zeros), ComplexColumn(poles), ComplexScalarValue([k])];
    }

    /// <summary><c>[b, a] = sos2tf(sos, g)</c>.</summary>
    private static JgsValue[] SectionsToTf(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("sos2tf", args, 1, 2, line, col);
        (double[] rows, int sections) = ReadSections("sos2tf", args[0], line, col);
        double gain = 1;
        if (args.Count >= 2)
        {
            foreach (double v in ToDoubles("sos2tf", args[1], line, col))
            {
                gain *= v;
            }
        }

        (double[] b, double[] a) = SecondOrderSections.ToTransferFunction(rows, sections, gain);
        JgsValue numerator = JgsMatrix.FromColumnMajor(b, 1, b.Length);
        return wanted <= 1
            ? [numerator]
            : [numerator, JgsMatrix.FromColumnMajor(a, 1, a.Length)];
    }

    /// <summary><c>[a, b, c, d] = sos2ss(sos, g)</c>.</summary>
    private static JgsValue[] SectionsToStateSpace(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("sos2ss", args, 1, 2, line, col);
        (double[] rows, int sections) = ReadSections("sos2ss", args[0], line, col);
        double gain = args.Count >= 2 ? Num("sos2ss", args, 1, line, col) : 1;

        (double[] num, double[] den) = SecondOrderSections.ToTransferFunction(rows, sections, gain);

        // Trailing zeros shared by both sides are section padding rather than filter order.
        int spare = 0;
        while (spare < num.Length - 1 && num[^(spare + 1)] == 0 && den[^(spare + 1)] == 0)
        {
            spare++;
        }

        FilterCoefficients.StateSpace ss = FilterCoefficients.TfToSs(
            num[..(num.Length - spare)], 1, num.Length - spare, den[..(den.Length - spare)]);
        return StateSpaceValues(ss, wanted);
    }

    /// <summary><c>[b, a] = sos2ctf(sos, g)</c>: the cascade split into its two halves.</summary>
    private static JgsValue[] SectionsToCascade(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("sos2ctf", args, 1, 2, line, col);
        (double[] rows, int sections) = ReadSections("sos2ctf", args[0], line, col);

        var numerators = new double[sections * 3];
        var denominators = new double[sections * 3];
        Array.Copy(rows, 0, numerators, 0, sections * 3);
        Array.Copy(rows, sections * 3, denominators, 0, sections * 3);

        if (args.Count >= 2)
        {
            double[] scales = ToDoubles("sos2ctf", args[1], line, col);
            if (scales.Length != 1 && scales.Length != sections + 1)
            {
                throw new JgsRuntimeException(line, col,
                    $"sos2ctf takes one scale value or {sections + 1} of them.");
            }

            numerators = SecondOrderSections.ScaleSections(numerators, sections, 3, scales);
        }

        JgsValue b = JgsMatrix.FromColumnMajor(numerators, sections, 3);
        return wanted <= 1
            ? [b]
            : [b, JgsMatrix.FromColumnMajor(denominators, sections, 3)];
    }

    /// <summary><c>[num, den, g] = zp2ctf(z, p, k, Name=Value)</c>.</summary>
    private static JgsValue[] ZeroPoleToCascade(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "zp2ctf reads zeros and poles at least.");
        }

        Complex[] zeros = FilterRoots("zp2ctf", args[0], line, col);
        Complex[] poles = FilterRoots("zp2ctf", args[1], line, col);

        int at = 2;
        double gain = 1;
        if (args.Count > 2 && !IsTextScalar(args[2]))
        {
            gain = IsEmptyValue(args[2]) ? 1 : Num("zp2ctf", args, 2, line, col);
            at = 3;
        }

        int sectionOrder = 2;
        var direction = SecondOrderSections.Direction.Up;
        var scaling = SecondOrderSections.Scaling.None;

        for (int i = at; i + 1 < args.Count; i += 2)
        {
            string option = StrOf("zp2ctf", args[i], line, col).ToLowerInvariant();
            switch (option)
            {
                case "sectionorder":
                    sectionOrder = Count("zp2ctf", args, i + 1, line, col);
                    if (sectionOrder != 2 && sectionOrder != 4)
                    {
                        throw new JgsRuntimeException(line, col, "zp2ctf's SectionOrder is 2 or 4.");
                    }

                    break;

                case "direction":
                    direction = FilterWord("zp2ctf", args[i + 1], line, col, "up", "down") == "down"
                        ? SecondOrderSections.Direction.Down
                        : SecondOrderSections.Direction.Up;
                    break;

                case "scale":
                    scaling = FilterWord("zp2ctf", args[i + 1], line, col, "none", "inf", "l2") switch
                    {
                        "inf" => SecondOrderSections.Scaling.Infinity,
                        "l2" => SecondOrderSections.Scaling.Two,
                        _ => SecondOrderSections.Scaling.None,
                    };
                    break;

                default:
                    throw new JgsRuntimeException(line, col,
                        $"zp2ctf takes SectionOrder, Direction and Scale, but got '{option}'.");
            }
        }

        SecondOrderSections.Cascade cascade;
        try
        {
            cascade = SecondOrderSections.FromRoots(
                zeros, poles, gain, sectionOrder, direction, scaling, false);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"zp2ctf: {ex.Message}");
        }

        int half = cascade.Columns / 2;
        var numerators = new double[cascade.Rows * half];
        var denominators = new double[cascade.Rows * half];
        Array.Copy(cascade.Sections, 0, numerators, 0, numerators.Length);
        Array.Copy(cascade.Sections, numerators.Length, denominators, 0, denominators.Length);

        if (wanted < 3)
        {
            numerators = SecondOrderSections.ScaleSections(
                numerators, cascade.Rows, half, [cascade.Gain]);
        }

        JgsValue num = JgsMatrix.FromColumnMajor(numerators, cascade.Rows, half);
        JgsValue den = JgsMatrix.FromColumnMajor(denominators, cascade.Rows, half);
        return wanted <= 1
            ? [num]
            : wanted == 2
                ? [num, den]
                : [num, den, JgsValue.Number(cascade.Gain)];
    }

    /// <summary><c>scaleFilterSections(ctfNum, sv)</c>.</summary>
    private static JgsValue ScaleCascadeSections(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("scaleFilterSections", args, 2, line, col);
        (double[] flat, int rows, int columns) =
            FilterMatrix("scaleFilterSections", args[0], line, col);
        double[] scales = ToDoubles("scaleFilterSections", args[1], line, col);

        if (scales.Length != 1 && scales.Length != rows + 1)
        {
            throw new JgsRuntimeException(line, col,
                $"scaleFilterSections takes one scale value or {rows + 1} of them.");
        }

        double[] scaled = SecondOrderSections.ScaleSections(flat, rows, columns, scales);
        return JgsMatrix.FromColumnMajor(scaled, rows, columns);
    }

    /// <summary><c>c = sos2cell(s, g)</c>.</summary>
    private static JgsValue SectionsToCell(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("sos2cell", args, 1, 2, line, col);
        (double[] rows, int sections) = ReadSections("sos2cell", args[0], line, col);

        double? gain = null;
        if (args.Count >= 2 && !IsEmptyValue(args[1]))
        {
            double given = Num("sos2cell", args, 1, line, col);
            if (given != 1)
            {
                gain = given;
            }
        }

        // A single trivial section absorbs the gain rather than earning a cell of its own.
        if (sections == 1 && gain is double only
            && rows[0] == 1 && rows[1] == 0 && rows[2] == 0
            && rows[3] == 1 && rows[4] == 0 && rows[5] == 0)
        {
            rows = (double[])rows.Clone();
            rows[0] *= only;
            gain = null;
        }

        var cells = new List<JgsValue>();
        if (gain is double g)
        {
            cells.Add(JgsValue.Cell([JgsValue.Number(g), JgsValue.Number(1)]));
        }

        for (int s = 0; s < sections; s++)
        {
            cells.Add(JgsValue.Cell(
            [
                TrimmedRow(rows, sections, s, 0),
                TrimmedRow(rows, sections, s, 3),
            ]));
        }

        JgsValue answer = JgsValue.Cell([.. cells]);
        answer.Reshape(1, cells.Count);
        return answer;
    }

    /// <summary>Half a section's row with its trailing zeros dropped.</summary>
    private static JgsValue TrimmedRow(double[] rows, int sections, int section, int from)
    {
        var slice = new double[3];
        for (int i = 0; i < 3; i++)
        {
            slice[i] = rows[((from + i) * sections) + section];
        }

        int last = -1;
        for (int i = 2; i >= 0; i--)
        {
            if (slice[i] != 0)
            {
                last = i;
                break;
            }
        }


        return last < 0
            ? JgsMatrix.FromColumnMajor([], 1, 0)
            : JgsMatrix.FromColumnMajor(slice[..(last + 1)], 1, last + 1);
    }

    /// <summary><c>[s, g] = cell2sos(c)</c>.</summary>
    private static JgsValue[] CellToSections(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("cell2sos", args, 1, line, col);
        if (args[0].Type != JgsType.Cell)
        {
            throw new JgsRuntimeException(line, col, "cell2sos reads a cell array of sections.");
        }

        JgsValue[] cells = args[0].AsCell;
        var pairs = new List<(double[] B, double[] A)>();
        foreach (JgsValue entry in cells)
        {
            if (entry.Type != JgsType.Cell || entry.AsCell.Length != 2)
            {
                throw new JgsRuntimeException(line, col,
                    "cell2sos reads each section as a cell of a numerator and a denominator.");
            }

            pairs.Add((
                ToDoubles("cell2sos", entry.AsCell[0], line, col),
                ToDoubles("cell2sos", entry.AsCell[1], line, col)));
        }

        double gain = 1;
        if (wanted >= 2 && pairs.Count > 0 && pairs[0].B.Length == 1 && pairs[0].A.Length == 1)
        {
            gain = pairs[0].B[0] / pairs[0].A[0];
            pairs.RemoveAt(0);
        }

        int sections = pairs.Count;
        var flat = new double[sections * 6];
        for (int s = 0; s < sections; s++)
        {
            for (int i = 0; i < 3; i++)
            {
                flat[(i * sections) + s] = i < pairs[s].B.Length ? pairs[s].B[i] : 0;
                flat[((3 + i) * sections) + s] = i < pairs[s].A.Length ? pairs[s].A[i] : 0;
            }
        }

        JgsValue sos = JgsMatrix.FromColumnMajor(flat, sections, 6);
        return wanted <= 1 ? [sos] : [sos, JgsValue.Number(gain)];
    }

    // --- Lattices --------------------------------------------------------------------------------

    /// <summary><c>[k, v] = tf2latc(num, den, phase)</c>.</summary>
    private static JgsValue[] TfToLattice(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("tf2latc", args, 1, 3, line, col);
        double[] num = ElementCount(args[0]) == 0 ? [0.0] : FilterVector("tf2latc", args[0], line, col);

        double[] den = [1.0];
        string phase = "none";
        if (args.Count == 2)
        {
            if (IsTextScalar(args[1]))
            {
                phase = FilterWord("tf2latc", args[1], line, col, "none", "min", "max");
            }
            else
            {
                den = FilterVector("tf2latc", args[1], line, col);
            }
        }
        else if (args.Count == 3)
        {
            den = FilterVector("tf2latc", args[1], line, col);
            phase = FilterWord("tf2latc", args[2], line, col, "none", "min", "max");
        }

        double largest = 0;
        foreach (double v in num)
        {
            largest = System.Math.Max(largest, System.Math.Abs(v));
        }

        if (largest == 0)
        {
            throw new JgsRuntimeException(line, col, "tf2latc needs a numerator that is not all zeros.");
        }

        bool feedForward = den.Length == 0 || IsScalarDenominator(den);
        if (!feedForward && phase != "none")
        {
            throw new JgsRuntimeException(line, col,
                "tf2latc's 'min' and 'max' readings are for filters with no poles.");
        }

        if (feedForward && wanted >= 2)
        {
            // MATLAB's own answer for an FIR asked for two outputs: the ladder is the numerator and
            // the reflection coefficients are zero, which is the direct form written as a lattice.
            double[] scaled = ScaleByLead(num, den);
            var zeros = new double[System.Math.Max(0, scaled.Length - 1)];
            return
            [
                JgsMatrix.FromColumnMajor(zeros, zeros.Length, 1),
                JgsMatrix.FromColumnMajor(scaled, scaled.Length, 1),
            ];
        }

        if (feedForward)
        {
            double[] scaled = ScaleByLead(num, den);
            if (phase == "max")
            {
                scaled = FilterCoefficients.Reversed(scaled);
            }

            double[] reflection = LatticeFilters.ReflectionCoefficients(scaled);
            return [JgsMatrix.FromColumnMajor(reflection, reflection.Length, 1)];
        }

        double[] reflectionOut;
        double[] ladderOut;
        try
        {
            (reflectionOut, ladderOut) = LatticeFilters.ToLattice(num, den);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"tf2latc: {ex.Message}");
        }

        JgsValue reflectionValue = JgsMatrix.FromColumnMajor(reflectionOut, reflectionOut.Length, 1);
        return wanted <= 1
            ? [reflectionValue]
            : [reflectionValue, JgsMatrix.FromColumnMajor(ladderOut, ladderOut.Length, 1)];
    }

    /// <summary>Whether a denominator has no feedback in it.</summary>
    private static bool IsScalarDenominator(double[] den)
    {
        for (int i = 1; i < den.Length; i++)
        {
            if (den[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A numerator divided by its denominator's leading coefficient.</summary>
    private static double[] ScaleByLead(double[] num, double[] den)
    {
        double head = den.Length > 0 && den[0] != 0 ? den[0] : 1;
        var scaled = new double[num.Length];
        for (int i = 0; i < num.Length; i++)
        {
            scaled[i] = num[i] / head;
        }

        int last = scaled.Length - 1;
        while (last > 0 && scaled[last] == 0)
        {
            last--;
        }

        return scaled[..(last + 1)];
    }

    /// <summary><c>[num, den] = latc2tf(k, v)</c> and its four word forms.</summary>
    private static JgsValue[] LatticeToTf(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("latc2tf", args, 1, 2, line, col);
        double[] k = ElementCount(args[0]) == 0 ? [] : FilterVector("latc2tf", args[0], line, col);

        double[] num;
        double[] den;
        if (args.Count == 1)
        {
            (num, den) = LatticeFilters.FromLattice(k, "fir");
        }
        else if (IsTextScalar(args[1]))
        {
            string kind = StrOf("latc2tf", args[1], line, col).ToLowerInvariant();
            (num, den) = kind switch
            {
                "allpass" => LatticeFilters.FromLattice(k, "allpass"),
                "allpole" or "iir" => LatticeFilters.FromLadder(k, [1.0]),
                "fir" => LatticeFilters.FromLattice(k, "fir"),
                "min" => LatticeFilters.FromLattice(k, "min"),
                "max" => LatticeFilters.FromLattice(k, "max"),
                _ => throw new JgsRuntimeException(line, col,
                    "latc2tf takes 'fir', 'min', 'max', 'allpass' or 'allpole'."),
            };
        }
        else
        {
            double[] v = ElementCount(args[1]) == 0 ? [] : FilterVector("latc2tf", args[1], line, col);
            (num, den) = LatticeFilters.FromLadder(k, v);
        }

        JgsValue numerator = JgsMatrix.FromColumnMajor(num, 1, num.Length);
        return wanted <= 1
            ? [numerator]
            : [numerator, JgsMatrix.FromColumnMajor(den, 1, den.Length)];
    }
}
