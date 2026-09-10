using System.Numerics;
using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The Signal Processing Toolbox's ways of running a filter over a signal: zero phase, cascaded
/// sections, block convolution, initial conditions, lattices, the running median and the local
/// polynomial fit (M133).
/// </summary>
/// <remarks>
/// <para>
/// These are the names a script reaches for once it has a filter. They share one habit worth naming:
/// each of them treats a row vector as one signal rather than as many one-sample signals, walks down
/// the columns of anything wider, and gives back the shape it was handed. Getting that wrong is
/// silent, because a transposed answer is still an answer.
/// </para>
/// <para>
/// <c>filtfilt</c> is the one with the most readings. It takes a numerator and a denominator, or a
/// cascade of sections and its scale values, or the three-part cascaded form under a <c>'ctf'</c>
/// flag — and it decides between the second and the third by looking at the shapes, which is the
/// same guess MATLAB makes and warns about when it is close.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the ten filtering names.</summary>
    internal static void RegisterFilterPassBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        Define("filtfilt", ZeroPhaseFiltered);
        Define("sosfilt", CascadeFiltered);
        Define("fftfilt", BlockFiltered);
        Define("filtic", FilterConditions);
        Define("latcfilt", (args, line, col) => LatticeFiltered(args, 1, line, col)[0],
            (args, wanted, line, col) => LatticeFiltered(args, wanted, line, col));
        Define("medfilt1", MedianFiltered);
        Define("hampel", (args, line, col) => HampelFiltered(args, 1, line, col)[0],
            (args, wanted, line, col) => HampelFiltered(args, wanted, line, col));
        Define("sgolay", (args, line, col) => SavitzkyGolayMatrices(args, 1, line, col)[0],
            (args, wanted, line, col) => SavitzkyGolayMatrices(args, wanted, line, col));
        Define("sgolayfilt", SavitzkyGolayFiltered);
        Define("filtstates", FilterStates);
    }

    /// <summary>
    /// A signal read the way every one of these names reads it: a row is one signal, anything else
    /// is one signal per column.
    /// </summary>
    private static (double[] Flat, int Rows, int Columns, bool WasRow, int[] Dims) FilterSignal(
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

        if (rows == 1 && dims.Length <= 2 && columns >= 1)
        {
            return (flat, columns, 1, columns != 1, dims);
        }

        return (flat, rows, columns, false, dims);
    }

    /// <summary>An answer given back in the shape its signal arrived in.</summary>
    private static JgsValue FilterShaped(double[] flat, bool wasRow, int[] dims) =>
        wasRow ? JgsMatrix.FromColumnMajor(flat, 1, flat.Length) : JgsMatrix.FromColumnMajorDims(flat, dims);

    // --- Zero phase ------------------------------------------------------------------------------

    /// <summary><c>filtfilt(b, a, x)</c>, <c>filtfilt(sos, g, x)</c> and the cascaded form.</summary>
    private static JgsValue ZeroPhaseFiltered(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (TryDigitalFilterPass("filtfilt", args, line, col, out JgsValue designed))
        {
            return designed;
        }

        ArityRange("filtfilt", args, 2, 4, line, col);

        if (args.Count == 2)
        {
            throw new JgsRuntimeException(line, col,
                "filtfilt's digitalFilter form is not implemented; give it a numerator and a denominator.");
        }

        bool cascadeFlag = args.Count == 4 && IsTextScalar(args[3])
            && StrOf("filtfilt", args[3], line, col).Equals("ctf", StringComparison.OrdinalIgnoreCase);

        (double[] num, int numRows, int numColumns) = FilterMatrix("filtfilt", args[0], line, col);
        (double[] den, int denRows, int denColumns) = FilterMatrix("filtfilt", args[1], line, col);
        (double[] x, int rows, int columns, bool wasRow, int[] dims) =
            FilterSignal("filtfilt", args[2], line, col);

        if (num.Length == 0 || den.Length == 0 || x.Length == 0)
        {
            return JgsMatrix.FromColumnMajor([], 0, 0);
        }

        double[,] b;
        double[,] a;
        int stages;

        bool numIsVector = numRows == 1 || numColumns == 1;
        bool denIsVector = denRows == 1 || denColumns == 1;
        bool isCascade = !cascadeFlag && !numIsVector && denIsVector && numColumns == 6
            && (numRows > 1 || (num[3 * numRows] == 1 && den.Length <= 2));

        if (isCascade)
        {
            stages = numRows;
            if (den.Length > stages + 1)
            {
                throw new JgsRuntimeException(line, col,
                    $"filtfilt takes at most {stages + 1} scale values for {stages} sections.");
            }

            var scaled = new double[num.Length];
            Array.Copy(num, scaled, num.Length);
            int count = den.Length;
            if (count == stages + 1)
            {
                for (int c = 0; c < 3; c++)
                {
                    scaled[(c * stages) + stages - 1] *= den[stages];
                }

                count--;
            }

            for (int s = 0; s < count; s++)
            {
                for (int c = 0; c < 3; c++)
                {
                    scaled[(c * stages) + s] *= den[s];
                }
            }

            b = new double[stages, 3];
            a = new double[stages, 3];
            for (int s = 0; s < stages; s++)
            {
                for (int c = 0; c < 3; c++)
                {
                    b[s, c] = scaled[(c * stages) + s];
                    a[s, c] = scaled[((3 + c) * stages) + s];
                }
            }
        }
        else
        {
            (b, a, stages) = CascadedCoefficients("filtfilt", num, numRows, numColumns,
                den, denRows, denColumns, line, col);
        }

        double[] y;
        try
        {
            y = FilterPasses.ZeroPhase(b, a, stages, x, rows, columns);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"filtfilt: {ex.Message}");
        }

        return FilterShaped(y, wasRow, dims);
    }

    /// <summary>Two coefficient blocks read as one stage or as a cascade of them.</summary>
    private static (double[,] B, double[,] A, int Stages) CascadedCoefficients(
        string name, double[] num, int numRows, int numColumns,
        double[] den, int denRows, int denColumns, int line, int col)
    {
        bool numIsVector = numRows == 1 || numColumns == 1;
        bool denIsVector = denRows == 1 || denColumns == 1;

        if (numIsVector && denIsVector)
        {
            var b = new double[1, num.Length];
            var a = new double[1, den.Length];
            for (int i = 0; i < num.Length; i++)
            {
                b[0, i] = num[i];
            }

            for (int i = 0; i < den.Length; i++)
            {
                a[0, i] = den[i];
            }

            return (b, a, 1);
        }

        if (!numIsVector && den.Length == 1)
        {
            var b = Rectangular(num, numRows, numColumns);
            var a = new double[numRows, 1];
            for (int s = 0; s < numRows; s++)
            {
                a[s, 0] = den[0];
            }

            return (b, a, numRows);
        }

        if (!denIsVector && num.Length == 1)
        {
            var a = Rectangular(den, denRows, denColumns);
            var b = new double[denRows, 1];
            for (int s = 0; s < denRows; s++)
            {
                b[s, 0] = num[0];
            }

            return (b, a, denRows);
        }

        if (numRows != denRows)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs as many numerator rows as denominator rows.");
        }

        return (Rectangular(num, numRows, numColumns), Rectangular(den, denRows, denColumns), numRows);
    }

    /// <summary>A column-major block read as a row-indexed matrix.</summary>
    private static double[,] Rectangular(double[] flat, int rows, int columns)
    {
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

    // --- Cascade and block convolution -------------------------------------------------------------

    /// <summary><c>sosfilt(sos, x)</c> and <c>sosfilt(sos, x, dim)</c>.</summary>
    private static JgsValue CascadeFiltered(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("sosfilt", args, 2, 3, line, col);
        (double[] sos, int sections) = ReadSections("sosfilt", args[0], line, col);

        int[] dims = SizeDims(args[1]);
        int dim = args.Count >= 3 && !IsEmptyValue(args[2])
            ? DimensionArgument("sosfilt", args, 2, line, col)
            : JgsMatrix.DefaultDim(dims);

        double[] x = ToDoubles("sosfilt", args[1], line, col);
        (double[][] slices, _) = JgsMatrix.SlicesAlong(x, dims, dim);

        var filtered = new double[slices.Length][];
        for (int i = 0; i < slices.Length; i++)
        {
            try
            {
                filtered[i] = FilterPasses.Cascade(sos, sections, slices[i], slices[i].Length, 1);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"sosfilt: {ex.Message}");
            }
        }

        return JoinedSlices(filtered, dims, dim);
    }

    /// <summary><c>fftfilt(b, x)</c> and <c>fftfilt(b, x, n)</c>.</summary>
    private static JgsValue BlockFiltered(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (TryDigitalFilterPass("fftfilt", args, line, col, out JgsValue designedBlock))
        {
            return designedBlock;
        }

        ArityRange("fftfilt", args, 2, 3, line, col);

        int[] bDims = SizeDims(args[0]);
        int[] xDims = SizeDims(args[1]);
        Complex[] b = ElementCount(args[0]) == 0 ? [] : ComplexArrayOf("fftfilt", args[0], line, col);
        Complex[] x = ElementCount(args[1]) == 0 ? [] : ComplexArrayOf("fftfilt", args[1], line, col);

        if (b.Length == 0 || x.Length == 0)
        {
            return JgsMatrix.FromColumnMajor([], 0, 0);
        }

        bool wasRow = xDims.Length == 2 && xDims[0] == 1 && xDims[1] > 1;
        int rows = wasRow ? xDims[1] : xDims.Length > 0 ? xDims[0] : 1;
        int columns = wasRow ? 1 : x.Length / System.Math.Max(1, rows);

        bool bIsVector = bDims.Length < 2 || bDims[0] == 1 || bDims[1] == 1;
        int bRows = bIsVector ? b.Length : bDims[0];
        int bColumns = bIsVector ? 1 : bDims[1];

        if (!bIsVector && bColumns != columns && columns > 1)
        {
            throw new JgsRuntimeException(line, col,
                "fftfilt needs one filter column for each signal column.");
        }

        int? nfft = args.Count >= 3 && !IsEmptyValue(args[2])
            ? Count("fftfilt", args, 2, line, col)
            : null;

        Complex[] y = FilterPasses.BlockConvolve(b, bRows, bColumns, x, rows, columns, nfft);

        bool real = IsRealValue(args[0]) && IsRealValue(args[1]);
        int wide = System.Math.Max(bColumns, columns);
        int outRows = wasRow && wide == 1 ? 1 : rows;
        int outColumns = wasRow && wide == 1 ? rows : wide;

        if (!real)
        {
            return ComplexShaped(y, [outRows, outColumns]);
        }

        var flat = new double[y.Length];
        for (int i = 0; i < y.Length; i++)
        {
            flat[i] = y[i].Real;
        }

        return JgsMatrix.FromColumnMajor(flat, outRows, outColumns);
    }

    /// <summary>Whether a value has no imaginary part anywhere.</summary>
    private static bool IsRealValue(JgsValue value)
    {
        if (ElementCount(value) == 0)
        {
            return true;
        }

        try
        {
            foreach (Complex v in ComplexArrayOf("value", value, 0, 0))
            {
                if (v.Imaginary != 0)
                {
                    return false;
                }
            }
        }
        catch (JgsRuntimeException)
        {
            return true;
        }

        return true;
    }

    /// <summary><c>filtic(b, a, y)</c> and <c>filtic(b, a, y, x)</c>.</summary>
    private static JgsValue FilterConditions(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("filtic", args, 3, 4, line, col);
        double[] b = FilterVector("filtic", args[0], line, col);
        double[] a = FilterVector("filtic", args[1], line, col);
        double[] past = ElementCount(args[2]) == 0 ? [] : FilterVector("filtic", args[2], line, col);
        double[] input = args.Count >= 4 && ElementCount(args[3]) > 0
            ? FilterVector("filtic", args[3], line, col)
            : [0.0];

        double[] state;
        try
        {
            state = FilterPasses.InitialConditions(b, a, past, input);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"filtic: {ex.Message}");
        }

        return JgsMatrix.FromColumnMajor(state, 1, state.Length);
    }

    // --- Lattices ---------------------------------------------------------------------------------

    /// <summary><c>[f, g, zf] = latcfilt(k, ...)</c> in its four readings.</summary>
    private static JgsValue[] LatticeFiltered(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("latcfilt", args, 2, 6, line, col);

        double[] k = FilterVector("latcfilt", args[0], line, col);
        double[] v = [];
        int at = 1;
        bool feedback = false;

        if (args.Count >= 3 && !IsTextScalar(args[2]))
        {
            // Three or more arguments with a numeric third: the second is the ladder.
            double[] ladder = ElementCount(args[1]) == 0 ? [] : FilterVector("latcfilt", args[1], line, col);
            feedback = true;
            v = ladder.Length == 1 && ladder[0] == 1 ? [] : ladder;
            at = 2;
        }

        if (k.Length == 0 || ElementCount(args[at]) == 0)
        {
            throw new JgsRuntimeException(line, col, "latcfilt reads a lattice and a signal.");
        }

        (double[] x, int rows, int columns, bool wasRow, int[] dims) =
            FilterSignal("latcfilt", args[at], line, col);

        double[] zi = [];
        for (int i = at + 1; i < args.Count; i++)
        {
            if (IsTextScalar(args[i]))
            {
                string word = StrOf("latcfilt", args[i], line, col);
                if (!word.Equals("ic", StringComparison.OrdinalIgnoreCase))
                {
                    throw new JgsRuntimeException(line, col,
                        $"latcfilt takes 'ic' before its initial conditions, but got '{word}'.");
                }

                if (i + 1 < args.Count && !IsEmptyValue(args[i + 1]))
                {
                    zi = FilterVector("latcfilt", args[i + 1], line, col);
                }

                i++;
                continue;
            }

            if (!IsEmptyValue(args[i]))
            {
                // The sixth argument is a dimension, and processing an array along one is not
                // implemented; saying so beats walking the columns and calling it the answer.
                throw new JgsRuntimeException(line, col,
                    "latcfilt's dimension argument is not implemented; it works down the columns.");
            }
        }

        var forward = new double[x.Length];
        var backward = new double[x.Length];
        var final = new double[k.Length * columns];
        var column = new double[rows];

        for (int c = 0; c < columns; c++)
        {
            Array.Copy(x, c * rows, column, 0, rows);
            LatticeFilters.LatticeRun run = feedback
                ? LatticeFilters.RunFeedback(k, v, column, zi)
                : LatticeFilters.RunFeedForward(k, column, zi);

            Array.Copy(run.Forward, 0, forward, c * rows, rows);
            Array.Copy(run.Backward, 0, backward, c * rows, rows);
            Array.Copy(run.Final, 0, final, c * k.Length, k.Length);
        }

        JgsValue f = FilterShaped(forward, wasRow, dims);
        JgsValue g = FilterShaped(backward, wasRow, dims);
        return wanted <= 1
            ? [f]
            : wanted == 2
                ? [f, g]
                : [f, g, JgsMatrix.FromColumnMajor(final, k.Length, columns)];
    }

    // --- Median, outliers and local fits -----------------------------------------------------------

    /// <summary><c>medfilt1(x, n, blksz, dim, ...)</c>.</summary>
    private static JgsValue MedianFiltered(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("medfilt1", args, 1, 6, line, col);

        int width = 3;
        if (args.Count >= 2 && !IsEmptyValue(args[1]) && !IsTextScalar(args[1]))
        {
            width = Count("medfilt1", args, 1, line, col);
            if (width < 0)
            {
                throw new JgsRuntimeException(line, col, "medfilt1's window is a whole number that is not negative.");
            }
        }

        int[] dims = SizeDims(args[0]);
        int dim = args.Count >= 4 && !IsEmptyValue(args[3]) && !IsTextScalar(args[3])
            ? DimensionArgument("medfilt1", args, 3, line, col)
            : JgsMatrix.DefaultDim(dims);

        bool zeroPad = true;
        bool includeNan = true;
        for (int i = 1; i < args.Count; i++)
        {
            if (!IsTextScalar(args[i]))
            {
                continue;
            }

            switch (StrOf("medfilt1", args[i], line, col).ToLowerInvariant())
            {
                case "zeropad":
                    zeroPad = true;
                    break;
                case "truncate":
                    zeroPad = false;
                    break;
                case "includenan":
                    includeNan = true;
                    break;
                case "omitnan":
                    includeNan = false;
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        "medfilt1 takes 'zeropad', 'truncate', 'includenan' and 'omitnan'.");
            }
        }

        double[] x = ToDoubles("medfilt1", args[0], line, col);
        (double[][] slices, _) = JgsMatrix.SlicesAlong(x, dims, dim);

        var filtered = new double[slices.Length][];
        for (int i = 0; i < slices.Length; i++)
        {
            filtered[i] = SmoothingFilters.RunningMedian(
                slices[i], slices[i].Length, 1, width, zeroPad, includeNan);
        }

        return JoinedSlices(filtered, dims, dim);
    }

    /// <summary><c>[y, i, xmedian, xsigma] = hampel(x, k, nsigma)</c>.</summary>
    private static JgsValue[] HampelFiltered(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("hampel", args, 1, 3, line, col);
        int k = args.Count >= 2 && !IsEmptyValue(args[1]) ? Count("hampel", args, 1, line, col) : 3;
        double sigmas = args.Count >= 3 && !IsEmptyValue(args[2]) ? Num("hampel", args, 2, line, col) : 3;

        if (k <= 0)
        {
            throw new JgsRuntimeException(line, col, "hampel's half-window is a whole number above zero.");
        }

        if (sigmas < 0)
        {
            throw new JgsRuntimeException(line, col, "hampel's threshold is not negative.");
        }

        (double[] x, int rows, int columns, bool wasRow, int[] dims) =
            FilterSignal("hampel", args[0], line, col);

        SmoothingFilters.Hampel answer = SmoothingFilters.HampelFilter(x, rows, columns, k, sigmas);

        var flags = new double[answer.Outliers.Length];
        for (int i = 0; i < flags.Length; i++)
        {
            flags[i] = answer.Outliers[i] ? 1 : 0;
        }

        JgsValue[] all =
        [
            FilterShaped(answer.Filtered, wasRow, dims),
            LogicalShaped(flags, wasRow, dims),
            FilterShaped(answer.Median, wasRow, dims),
            FilterShaped(answer.Sigma, wasRow, dims),
        ];

        return all[..System.Math.Clamp(wanted, 1, 4)];
    }

    /// <summary>A flag array given back as logicals rather than as ones and zeros.</summary>
    private static JgsValue LogicalShaped(double[] flags, bool wasRow, int[] dims)
    {
        var elements = new JgsValue[flags.Length];
        for (int i = 0; i < flags.Length; i++)
        {
            elements[i] = JgsValue.Bool(flags[i] != 0);
        }

        JgsValue array = JgsValue.Array(elements);
        array.ReshapeDims(wasRow ? [1, flags.Length] : dims);
        return array;
    }

    /// <summary><c>[b, g] = sgolay(order, framelen, weights)</c>.</summary>
    private static JgsValue[] SavitzkyGolayMatrices(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("sgolay", args, 2, 3, line, col);
        int order = Count("sgolay", args, 0, line, col);
        int frame = Count("sgolay", args, 1, line, col);
        double[] weights = args.Count >= 3 && ElementCount(args[2]) > 0
            ? ToDoubles("sgolay", args[2], line, col)
            : [];

        if (weights.Length > 0 && weights.Length != frame)
        {
            throw new JgsRuntimeException(line, col, "sgolay needs one weight for each sample of the frame.");
        }

        foreach (double w in weights)
        {
            if (w <= 0)
            {
                throw new JgsRuntimeException(line, col, "sgolay's weights are above zero.");
            }
        }

        double[,] b;
        double[,] g;
        try
        {
            (b, g) = SmoothingFilters.SavitzkyGolay(order, frame, weights);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"sgolay: {ex.Message}");
        }

        return wanted <= 1
            ? [FilterMatrixValue(b)]
            : [FilterMatrixValue(b), FilterMatrixValue(g)];
    }

    /// <summary><c>sgolayfilt(x, order, framelen, weights, dim)</c>.</summary>
    private static JgsValue SavitzkyGolayFiltered(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("sgolayfilt", args, 3, 5, line, col);
        int order = Count("sgolayfilt", args, 1, line, col);
        int frame = Count("sgolayfilt", args, 2, line, col);
        double[] weights = args.Count >= 4 && ElementCount(args[3]) > 0
            ? ToDoubles("sgolayfilt", args[3], line, col)
            : [];

        if (frame % 2 == 0)
        {
            throw new JgsRuntimeException(line, col, "sgolayfilt's frame length is odd.");
        }

        if (order > frame - 1)
        {
            throw new JgsRuntimeException(line, col,
                "sgolayfilt's polynomial degree is below its frame length.");
        }

        if (weights.Length > 0 && weights.Length != frame)
        {
            throw new JgsRuntimeException(line, col,
                "sgolayfilt needs one weight for each sample of the frame.");
        }

        int[] dims = SizeDims(args[0]);
        int dim = args.Count >= 5 && !IsEmptyValue(args[4])
            ? DimensionArgument("sgolayfilt", args, 4, line, col)
            : JgsMatrix.DefaultDim(dims);

        double[] x = ToDoubles("sgolayfilt", args[0], line, col);
        (double[][] slices, _) = JgsMatrix.SlicesAlong(x, dims, dim);

        var filtered = new double[slices.Length][];
        for (int i = 0; i < slices.Length; i++)
        {
            try
            {
                filtered[i] = SmoothingFilters.SavitzkyGolayFilter(
                    slices[i], slices[i].Length, 1, order, frame, weights);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, $"sgolayfilt: {ex.Message}");
            }
        }

        return JoinedSlices(filtered, dims, dim);
    }

    /// <summary>
    /// <c>filtstates</c>: MATLAB's namespace of filter-state values, reached here as a struct whose
    /// fields are the constructors.
    /// </summary>
    /// <remarks>
    /// MATLAB spells these <c>filtstates.dfiir(num, den)</c> and <c>filtstates.cic(int, comb)</c>,
    /// which is a package rather than a function. This interpreter has no packages, so the name is a
    /// function that answers a struct of handles: the spelling at the call site is the same, and
    /// <c>filtstates.dfiir</c> on its own is a handle rather than an empty object.
    /// </remarks>
    private static JgsValue FilterStates(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            return JgsValue.Struct(new Dictionary<string, JgsValue>
            {
                ["dfiir"] = JgsValue.Function(new BuiltinFunction("dfiir", DirectFormStates)),
                ["cic"] = JgsValue.Function(new BuiltinFunction("cic", IntegratorCombStates)),
            });
        }

        return DirectFormStates(args, line, col);
    }

    /// <summary>A direct-form IIR filter's two state vectors.</summary>
    private static JgsValue DirectFormStates(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("filtstates.dfiir", args, 0, 2, line, col);
        JgsValue numerator = args.Count >= 1 ? args[0] : JgsValue.Number(0);
        JgsValue denominator = args.Count >= 2 ? args[1] : JgsValue.Number(0);
        return JgsValue.Struct(new Dictionary<string, JgsValue>
        {
            ["Numerator"] = numerator,
            ["Denominator"] = denominator,
        });
    }

    /// <summary>A cascaded integrator-comb filter's two state vectors.</summary>
    private static JgsValue IntegratorCombStates(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("filtstates.cic", args, 0, 3, line, col);
        JgsValue integrator = args.Count >= 1 ? args[0] : JgsValue.Number(0);
        JgsValue comb = args.Count >= 2 ? args[1] : JgsValue.Number(0);
        return JgsValue.Struct(new Dictionary<string, JgsValue>
        {
            ["Integrator"] = integrator,
            ["Comb"] = comb,
        });
    }
}
