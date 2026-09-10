using System.Numerics;
using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The Signal Processing Toolbox's transforms other than the Fourier transform: <c>hilbert</c>,
/// <c>czt</c>, <c>goertzel</c>, <c>fwht</c>/<c>ifwht</c>, the three cepstra, <c>dftmtx</c> and the
/// two digit-reversal permutations (M132).
/// </summary>
/// <remarks>
/// <para>
/// Almost all of these run down columns and treat a row vector as a column, answering a row —
/// MATLAB's <c>shiftdim</c> convention, which is why <c>hilbert</c> of a row is a row and
/// <c>hilbert</c> of a matrix is one analytic signal per column rather than one per row. The
/// convention is written once here and shared, because getting it wrong is silent: the answer has
/// the right size and the wrong contents.
/// </para>
/// <para>
/// Two of them break it. <c>czt</c> permutes a row into a column and permutes the answer back, which
/// is the same rule reached by a different route, and <c>goertzel</c> takes an explicit dimension.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the eleven transform names.</summary>
    internal static void RegisterSignalTransformBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        Define("hilbert", AnalyticSignal);
        Define("czt", ChirpZTransform);
        Define("goertzel", GoertzelTransform);
        Define("fwht", (args, line, col) => WalshTransform("fwht", args, line, col));
        Define("ifwht", (args, line, col) => WalshTransform("ifwht", args, line, col));
        Define("cceps", (args, line, col) => ComplexCepstrum(args, 1, line, col)[0],
            (args, wanted, line, col) => ComplexCepstrum(args, wanted, line, col));
        Define("icceps", InverseCepstrum);
        Define("rceps", (args, line, col) => RealCepstrum(args, 1, line, col)[0],
            (args, wanted, line, col) => RealCepstrum(args, wanted, line, col));
        Define("dftmtx", (args, line, col) =>
        {
            Arity("dftmtx", args, 1, line, col);
            int n = Count("dftmtx", args, 0, line, col);
            if (n < 0)
            {
                throw new JgsRuntimeException(line, col, "dftmtx's size is a whole number that is not negative.");
            }

            return ComplexShaped(SignalTransforms.TransformMatrix(n), [n, n]);
        });

        Define("bitrevorder", (args, line, col) => Reordered("bitrevorder", args, 1, line, col)[0],
            (args, wanted, line, col) => Reordered("bitrevorder", args, wanted, line, col));
        Define("digitrevorder", (args, line, col) => Reordered("digitrevorder", args, 1, line, col)[0],
            (args, wanted, line, col) => Reordered("digitrevorder", args, wanted, line, col));
    }

    /// <summary>
    /// How a column-wise transform reads its argument: the rows it runs down, how many of those runs
    /// there are, and whether the answer has to be turned back into a row at the end.
    /// </summary>
    private static (int Rows, int Columns, bool WasRow, int[] Dims) SignalColumns(JgsValue value)
    {
        int[] dims = SizeDims(value);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int columns = 1;
        for (int i = 1; i < dims.Length; i++)
        {
            columns *= dims[i];
        }

        if (rows == 1 && columns >= 1 && dims.Length <= 2)
        {
            // A row of samples is one signal, not many signals of one sample each.
            return (columns, 1, columns != 1, dims);
        }

        return (rows, columns, false, dims);
    }

    /// <summary><c>hilbert(x)</c> and <c>hilbert(x, n)</c>.</summary>
    private static JgsValue AnalyticSignal(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("hilbert", args, 1, 2, line, col);
        double[] flat = ToDoubles("hilbert", args[0], line, col);
        (int rows, int columns, bool wasRow, int[] dims) = SignalColumns(args[0]);
        int n = args.Count >= 2 && !IsEmptyValue(args[1]) ? Count("hilbert", args, 1, line, col) : rows;
        if (n < 0)
        {
            throw new JgsRuntimeException(line, col, "hilbert's length is a whole number that is not negative.");
        }

        var y = new Complex[n * columns];
        var column = new double[rows];
        for (int c = 0; c < columns; c++)
        {
            Array.Copy(flat, c * rows, column, 0, rows);
            Complex[] analytic = SignalTransforms.Hilbert(column, n);
            Array.Copy(analytic, 0, y, c * n, n);
        }

        return ComplexShaped(y, SignalResultShape(dims, wasRow, n, columns));
    }

    /// <summary>The shape a column-wise transform's answer takes.</summary>
    private static int[] SignalResultShape(int[] dims, bool wasRow, int rows, int columns)
    {
        if (wasRow)
        {
            return [1, rows];
        }

        if (dims.Length <= 2)
        {
            return [rows, columns];
        }

        var shape = (int[])dims.Clone();
        shape[0] = rows;
        return shape;
    }

    /// <summary><c>czt(x)</c>, <c>czt(x, m)</c>, <c>czt(x, m, w)</c> and <c>czt(x, m, w, a)</c>.</summary>
    private static JgsValue ChirpZTransform(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("czt", args, 1, 4, line, col);
        Complex[] flat = ComplexArrayOf("czt", args[0], line, col);
        (int rows, int columns, bool wasRow, int[] dims) = SignalColumns(args[0]);
        int k = args.Count >= 2 && !IsEmptyValue(args[1])
            ? Count("czt", args, 1, line, col)
            : System.Math.Max(rows, columns);
        Complex w = args.Count >= 3 && !IsEmptyValue(args[2])
            ? SignalScalar("czt", args[2], line, col)
            : Complex.Exp(-Complex.ImaginaryOne * 2.0 * System.Math.PI / k);
        Complex a = args.Count >= 4 && !IsEmptyValue(args[3])
            ? SignalScalar("czt", args[3], line, col)
            : Complex.One;

        var y = new Complex[k * columns];
        var column = new Complex[rows];
        for (int c = 0; c < columns; c++)
        {
            Array.Copy(flat, c * rows, column, 0, rows);
            Complex[] transformed = SignalTransforms.ChirpZ(column, k, w, a);
            Array.Copy(transformed, 0, y, c * k, k);
        }

        return ComplexShaped(y, SignalResultShape(dims, wasRow, k, columns));
    }

    /// <summary>One complex number from an argument that has to be a scalar.</summary>
    private static Complex SignalScalar(string name, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.Complex)
        {
            return value.AsComplex;
        }

        if (value.Type == JgsType.Number)
        {
            return new Complex(value.AsNumber, 0);
        }

        Complex[] all = ComplexArrayOf(name, value, line, col);
        if (all.Length != 1)
        {
            throw new JgsRuntimeException(line, col, $"{name}: this argument is a single number.");
        }

        return all[0];
    }

    /// <summary><c>goertzel(x, indices)</c> and <c>goertzel(x, indices, dim)</c>.</summary>
    private static JgsValue GoertzelTransform(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("goertzel", args, 1, 3, line, col);
        Complex[] flat = ComplexArrayOf("goertzel", args[0], line, col);
        int[] dims = SizeDims(args[0]);
        int dim = args.Count >= 3 && !IsEmptyValue(args[2])
            ? Count("goertzel", args, 2, line, col)
            : JgsMatrix.DefaultDim(dims);
        if (dim < 1 || dim > System.Math.Max(2, dims.Length))
        {
            throw new JgsRuntimeException(line, col, "goertzel: that is not a dimension this array has.");
        }

        int along = dim <= dims.Length ? dims[dim - 1] : 1;
        double[] indices = args.Count >= 2 && !IsEmptyValue(args[1])
            ? NumericVector("goertzel", args, 1, line, col)
            : Enumerable.Range(1, along).Select(i => (double)i).ToArray();

        var real = new double[flat.Length];
        var imaginary = new double[flat.Length];
        for (int i = 0; i < flat.Length; i++)
        {
            real[i] = flat[i].Real;
            imaginary[i] = flat[i].Imaginary;
        }

        (double[][] realSlices, _) = JgsMatrix.SlicesAlong(real, dims, dim);
        (double[][] imaginarySlices, _) = JgsMatrix.SlicesAlong(imaginary, dims, dim);
        var outReal = new double[realSlices.Length][];
        var outImaginary = new double[realSlices.Length][];
        var bins = new double[indices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            bins[i] = indices[i] - 1;
        }

        for (int s = 0; s < realSlices.Length; s++)
        {
            var line1 = new Complex[realSlices[s].Length];
            for (int i = 0; i < line1.Length; i++)
            {
                line1[i] = new Complex(realSlices[s][i], imaginarySlices[s][i]);
            }

            Complex[] answer = SignalTransforms.Goertzel(line1, bins);
            outReal[s] = new double[answer.Length];
            outImaginary[s] = new double[answer.Length];
            for (int i = 0; i < answer.Length; i++)
            {
                outReal[s][i] = answer[i].Real;
                outImaginary[s][i] = answer[i].Imaginary;
            }
        }

        (double[] joinedReal, _) = JgsMatrix.JoinAlong(outReal, dims, dim);
        (double[] joinedImaginary, _) = JgsMatrix.JoinAlong(outImaginary, dims, dim);
        var combined = new Complex[joinedReal.Length];
        for (int i = 0; i < combined.Length; i++)
        {
            combined[i] = new Complex(joinedReal[i], joinedImaginary[i]);
        }

        return ComplexShaped(combined, JgsMatrix.ShapeAlong(dims, dim, indices.Length));
    }

    /// <summary><c>fwht</c> and <c>ifwht</c>, which differ by one factor of the length.</summary>
    private static JgsValue WalshTransform(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange(name, args, 1, 3, line, col);
        double[] flat = ToDoubles(name, args[0], line, col);
        (int rows, int columns, bool wasRow, int[] dims) = SignalColumns(args[0]);
        if (flat.Length == 0)
        {
            return args[0];
        }

        int n;
        if (args.Count >= 2 && !IsEmptyValue(args[1]))
        {
            n = Count(name, args, 1, line, col);
            if (n <= 0 || (n & (n - 1)) != 0)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: a Walsh-Hadamard transform's length is a power of two.");
            }
        }
        else
        {
            n = rows;
            if ((n & (n - 1)) != 0)
            {
                n = 1;
                while (n < rows)
                {
                    n <<= 1;
                }
            }
        }

        var ordering = SignalTransforms.WalshOrdering.Sequency;
        if (args.Count >= 3 && !IsEmptyValue(args[2]))
        {
            ordering = Str(name, args, 2, line, col).ToLowerInvariant() switch
            {
                "sequency" => SignalTransforms.WalshOrdering.Sequency,
                "hadamard" => SignalTransforms.WalshOrdering.Hadamard,
                "dyadic" => SignalTransforms.WalshOrdering.Dyadic,
                string other => throw new JgsRuntimeException(line, col,
                    $"{name}: the orderings are 'sequency', 'hadamard' and 'dyadic', not '{other}'."),
            };
        }

        var y = new double[n * columns];
        var column = new double[rows];
        for (int c = 0; c < columns; c++)
        {
            Array.Copy(flat, c * rows, column, 0, rows);
            double[] transformed = SignalTransforms.Walsh(column, n, ordering);
            if (name == "ifwht")
            {
                for (int i = 0; i < n; i++)
                {
                    transformed[i] *= n;
                }
            }

            Array.Copy(transformed, 0, y, c * n, n);
        }

        return JgsMatrix.FromColumnMajorDims(y, SignalResultShape(dims, wasRow, n, columns));
    }

    /// <summary><c>cceps(x)</c> and <c>cceps(x, n)</c>, with the delay it removed.</summary>
    private static JgsValue[] ComplexCepstrum(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("cceps", args, 1, 2, line, col);
        double[] flat = ToDoubles("cceps", args[0], line, col);
        (int rows, int columns, bool wasRow, _) = SignalColumns(args[0]);
        if (columns != 1 || flat.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "cceps reads one non-empty vector of real samples.");
        }

        int n = args.Count >= 2 && !IsEmptyValue(args[1]) ? Count("cceps", args, 1, line, col) : rows;
        if (n <= 0)
        {
            throw new JgsRuntimeException(line, col, "cceps's length is a positive whole number.");
        }

        (double[] cepstrum, int delay) = SignalTransforms.ComplexCepstrum(flat, n);
        JgsValue answer = wasRow
            ? JgsMatrix.FromColumnMajor(cepstrum, 1, n)
            : JgsMatrix.FromColumnMajor(cepstrum, n, 1);
        if (wanted <= 1)
        {
            return [answer];
        }

        if (wanted >= 3)
        {
            throw new JgsRuntimeException(line, col,
                "cceps's third output rebuilds the cepstrum from the signal's roots, which JGraph does not do; "
                + "ask for the first two.");
        }

        return [answer, JgsValue.Number(delay)];
    }

    /// <summary><c>icceps(xhat)</c> and <c>icceps(xhat, nd)</c>.</summary>
    private static JgsValue InverseCepstrum(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("icceps", args, 1, 2, line, col);
        double[] flat = ToDoubles("icceps", args[0], line, col);
        (int rows, int columns, bool wasRow, _) = SignalColumns(args[0]);
        if (columns != 1 || flat.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "icceps reads one non-empty vector of real samples.");
        }

        int delay = args.Count >= 2 && !IsEmptyValue(args[1]) ? Count("icceps", args, 1, line, col) : 0;
        double[] x = SignalTransforms.InverseComplexCepstrum(flat, delay);
        return wasRow ? JgsMatrix.FromColumnMajor(x, 1, rows) : JgsMatrix.FromColumnMajor(x, rows, 1);
    }

    /// <summary><c>rceps(x)</c> and its minimum-phase second output.</summary>
    private static JgsValue[] RealCepstrum(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("rceps", args, 1, line, col);
        double[] flat = ToDoubles("rceps", args[0], line, col);
        (int rows, int columns, bool wasRow, int[] dims) = SignalColumns(args[0]);
        if (flat.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "rceps reads a non-empty signal.");
        }

        var cepstra = new double[rows * columns];
        var minimum = new double[rows * columns];
        var column = new double[rows];
        for (int c = 0; c < columns; c++)
        {
            Array.Copy(flat, c * rows, column, 0, rows);
            try
            {
                (double[] cepstrum, double[] minimumPhase) =
                    SignalTransforms.RealCepstrum(column, wanted >= 2);
                Array.Copy(cepstrum, 0, cepstra, c * rows, rows);
                if (wanted >= 2)
                {
                    Array.Copy(minimumPhase, 0, minimum, c * rows, rows);
                }
            }
            catch (InvalidOperationException ex)
            {
                throw new JgsRuntimeException(line, col, $"rceps: {ex.Message}");
            }
        }

        int[] shape = SignalResultShape(dims, wasRow, rows, columns);
        JgsValue first = JgsMatrix.FromColumnMajorDims(cepstra, shape);
        return wanted <= 1 ? [first] : [first, JgsMatrix.FromColumnMajorDims(minimum, shape)];
    }

    /// <summary><c>bitrevorder</c> and <c>digitrevorder</c>, with the permutation they used.</summary>
    private static JgsValue[] Reordered(string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange(name, args, name == "bitrevorder" ? 1 : 2, name == "bitrevorder" ? 1 : 2, line, col);
        int radix = name == "bitrevorder" ? 2 : Count(name, args, 1, line, col);
        Complex[] flat = ComplexArrayOf(name, args[0], line, col);
        (int rows, int columns, bool wasRow, int[] dims) = SignalColumns(args[0]);

        int[] order;
        try
        {
            order = SignalTransforms.DigitReverse(rows, radix);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }

        var y = new Complex[flat.Length];
        for (int c = 0; c < columns; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                y[(c * rows) + r] = flat[(c * rows) + order[r]];
            }
        }

        int[] shape = SignalResultShape(dims, wasRow, rows, columns);
        JgsValue first = ComplexShaped(y, shape);
        if (wanted <= 1)
        {
            return [first];
        }

        var indices = new double[rows];
        for (int r = 0; r < rows; r++)
        {
            indices[r] = order[r] + 1;
        }

        return [first, JgsMatrix.FromColumnMajor(indices, rows, 1)];
    }
}
