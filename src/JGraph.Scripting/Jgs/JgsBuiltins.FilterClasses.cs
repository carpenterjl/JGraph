using System.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>filter</c>'s refusals in R2025b's order and words, and the road for the calls the double
/// kernels do not take: a complex coefficient, signal or <c>zi</c>, a single anywhere among them,
/// and an empty <c>b</c> or <c>a</c> (ADR 0350).
/// </summary>
/// <remarks>
/// R2025b walks a single filter in single arithmetic, the transposed direct form II with <c>b</c>
/// and <c>a</c> divided by <c>a(1)</c> first, and every product and sum rounded to single. The road
/// here does the same operations in the same order as the double kernels, in <see cref="float"/>,
/// so a single answer agrees with R2025b's to the bit (probed on random filters of orders 1 to 6,
/// 200 samples each, with and without <c>zi</c>). Rounding a double computation once differs from
/// it in the last bit or two, which is what ADR 0345 recorded as a divergence.
/// </remarks>
internal static partial class JgsBuiltins
{
    private const string FilterInputTypeId = "MATLAB:filter:inputType";

    /// <summary>
    /// Every argument read in order, as R2025b reads them: the first one that is neither numeric
    /// nor logical is <c>MATLAB:filter:inputType</c>, the first sparse one
    /// <c>MATLAB:filter:sparseInput</c>, whichever comes first (the dimension included).
    /// </summary>
    private static void FilterArgumentKinds(IReadOnlyList<JgsValue> args, int line, int col)
    {
        foreach (JgsValue value in args)
        {
            if (value.Type == JgsType.Sparse)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:filter:sparseInput",
                    "Sparse inputs are not supported.");
            }

            bool numeric = value.Type is JgsType.Null or JgsType.Number or JgsType.Bool
                    or JgsType.Complex or JgsType.Image
                || (value.Type == JgsType.Array && !value.IsStringArray);
            if (!numeric)
            {
                throw new JgsRuntimeException(line, col, FilterInputTypeId,
                    "Invalid data type. Input arrays must be numeric or logical.");
            }
        }
    }

    /// <summary>
    /// <c>b</c> and <c>a</c> must each be a vector in R2025b's sense: two dimensions, one of them 1.
    /// A 1-by-0 is one (and filters as nothing), a 0-by-0 <c>[]</c> is not.
    /// </summary>
    private static void FilterCoefficientShapes(IReadOnlyList<JgsValue> args, int line, int col)
    {
        for (int i = 0; i < 2; i++)
        {
            JgsValue value = args[i];
            int[] size = value.Type switch
            {
                JgsType.Null => [0, 0],
                JgsType.Number or JgsType.Bool or JgsType.Complex => [1, 1],
                _ => SizeDims(value),
            };

            bool vector = size.Length == 2 && (size[0] == 1 || size[1] == 1);
            if (!vector)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:filter:notVectorInput",
                    "First two arguments must be vectors.");
            }
        }
    }

    /// <summary>R2025b's two refusals of a leading denominator coefficient.</summary>
    private static void FilterLeadingCoefficient(double re, double im, int line, int col)
    {
        if (re == 0 && im == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:filter:firstElementOfDenominatorFilterZero",
                "First denominator filter coefficient must be non-zero.");
        }

        if (!double.IsFinite(re) || !double.IsFinite(im))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:filter:firstElementOfDenominatorFilterNotFinite",
                "First denominator filter coefficient must be finite.");
        }
    }

    /// <summary>
    /// A numeric value's elements in column-major order as a real plane and, when any element is
    /// complex, an imaginary one (null otherwise).
    /// </summary>
    private static (double[] Re, double[]? Im) FilterPlanes(JgsValue value, int line, int col)
    {
        switch (value.Type)
        {
            case JgsType.Null:
                return ([], null);
            case JgsType.Number or JgsType.Bool:
                return ([value.AsNumber], null);
            case JgsType.Complex:
                return ([value.AsComplex.Real], [value.AsComplex.Imaginary]);
        }

        if (value.Type == JgsType.Array && value.IsPackedComplex)
        {
            JgsPackedComplex planes = value.AsPackedComplex;
            double[] re = planes.Re.AsSpan().ToArray();
            double[] im = planes.Im.AsSpan().ToArray();
            GC.KeepAlive(planes);
            return (re, im);
        }

        if (!HasComplexElements(value))
        {
            return (FlattenColumnMajor("filter", value, line, col), null);
        }

        Complex[] flat = ComplexArrayOf("filter", value, line, col);
        var real = new double[flat.Length];
        var imaginary = new double[flat.Length];
        for (int i = 0; i < flat.Length; i++)
        {
            real[i] = flat[i].Real;
            imaginary[i] = flat[i].Imaginary;
        }

        return (real, imaginary);
    }

    /// <summary>
    /// Whether this call leaves the double kernels: a complex or single argument among
    /// <c>b</c>, <c>a</c>, <c>x</c> and <c>zi</c> (the dimension's class does not count), or an empty
    /// coefficient vector.
    /// </summary>
    private static bool FilterNeedsWideRoad(IReadOnlyList<JgsValue> args, out bool single, out bool complex)
    {
        single = false;
        complex = false;
        bool empty = false;
        for (int i = 0; i < System.Math.Min(args.Count, 4); i++)
        {
            single |= args[i].NumericClass == JgsNumericClass.Single;
            complex |= HasComplexElements(args[i]);
            if (i < 2)
            {
                empty |= args[i].Type == JgsType.Null
                    || (args[i].Type == JgsType.Array && args[i].ArrayLength == 0);
            }
        }

        return single || complex || empty;
    }

    /// <summary>
    /// <c>filter</c> for a complex or single call, or one with an empty <c>b</c> or <c>a</c>, after
    /// the argument kinds, the coefficient shapes and the dimension have been checked.
    /// </summary>
    private static JgsValue[] FilterWide(IReadOnlyList<JgsValue> args, bool single, int[] dims, int dim,
        int wanted, int line, int col)
    {
        (double[] bRe, double[]? bIm) = FilterPlanes(args[0], line, col);
        (double[] aRe, double[]? aIm) = FilterPlanes(args[1], line, col);
        if (aRe.Length > 0)
        {
            FilterLeadingCoefficient(aRe[0], aIm?[0] ?? 0, line, col);
        }

        int delays = System.Math.Max(System.Math.Max(bRe.Length, aRe.Length) - 1, 0);
        (bool hasInitial, bool initialPerSlice) = args.Count >= 4
            ? FilterInitialLayout(args[3], delays, dims, dim, line, col)
            : (false, false);
        (double[] ziRe, double[]? ziIm) = hasInitial ? FilterPlanes(args[3], line, col) : ([], null);

        (double[] xRe, double[]? xIm) = FilterPlanes(args[2], line, col);
        (double[][] reSlices, _) = JgsMatrix.SlicesAlong(xRe, dims, dim);
        double[][]? imSlices = xIm is null ? null : JgsMatrix.SlicesAlong(xIm, dims, dim).Item1;

        int slices = reSlices.Length;
        var yRe = new double[slices][];
        var yIm = new double[slices][];
        var zRe = new double[slices * delays];
        var zIm = new double[slices * delays];
        bool complex = bIm is not null || aIm is not null || xIm is not null || ziIm is not null;

        for (int s = 0; s < slices; s++)
        {
            int n = reSlices[s].Length;
            yRe[s] = new double[n];
            yIm[s] = new double[n];
            double[] sliceIm = imSlices?[s] ?? new double[n];
            var stateRe = new double[delays];
            var stateIm = new double[delays];

            if (bRe.Length == 0 || aRe.Length == 0)
            {
                // R2025b answers zeros for an empty b or a. With a empty its zf is the plain sum of
                // taps of b on the tail of the signal, undivided; with b empty it is zeros.
                if (bRe.Length > 0)
                {
                    FilterTail(bRe, bIm, reSlices[s], sliceIm, stateRe, stateIm, single);
                }
            }
            else
            {
                if (hasInitial)
                {
                    int from = initialPerSlice ? s * delays : 0;
                    System.Array.Copy(ziRe, from, stateRe, 0, delays);
                    if (ziIm is not null)
                    {
                        System.Array.Copy(ziIm, from, stateIm, 0, delays);
                    }
                }

                FilterLine(bRe, bIm, aRe, aIm, reSlices[s], sliceIm, yRe[s], yIm[s], stateRe, stateIm,
                    single, complex);
            }

            stateRe.CopyTo(zRe, s * delays);
            stateIm.CopyTo(zIm, s * delays);
        }

        int length = dim <= dims.Length ? dims[dim - 1] : 1;
        double[] joinedRe = slices == 0 ? [] : JgsMatrix.JoinAlong(yRe, dims, dim).ColumnMajor;
        double[] joinedIm = slices == 0 ? [] : JgsMatrix.JoinAlong(yIm, dims, dim).ColumnMajor;
        JgsValue answer = FilterOutput(joinedRe, joinedIm, JgsMatrix.ShapeAlong(dims, dim, length), single);
        if (wanted <= 1)
        {
            return [answer];
        }

        JgsValue rest = FilterOutput(zRe, zIm, PackedFilterOps.FinalConditionsShape(dims, dim, delays), single);
        return [answer, rest];
    }

    /// <summary>
    /// One output of the wide road: real when every imaginary part is zero, as R2025b answers
    /// (each output on its own), and single when the call was.
    /// </summary>
    private static JgsValue FilterOutput(double[] re, double[] im, int[] shape, bool single)
    {
        var values = new Complex[re.Length];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = new Complex(re[i], im[i]);
        }

        JgsValue result = FromComplexArray(values);
        Shape(result, shape);
        return single ? JgsNumericClasses.Stamp(result, JgsNumericClass.Single) : result;
    }

    /// <summary>
    /// One slice through the transposed direct form II, complex where any part is, single where the
    /// call is: <c>b</c> and <c>a</c> divided by <c>a(1)</c>, then <c>y = b0·x + d0</c> and
    /// <c>d_j = (b_{j+1}·x + d_{j+1}) − a_{j+1}·y</c>, the operations of the double kernels.
    /// </summary>
    private static void FilterLine(double[] bRe, double[]? bIm, double[] aRe, double[]? aIm,
        double[] xRe, double[] xIm, double[] yRe, double[] yIm, double[] dRe, double[] dIm,
        bool single, bool complex)
    {
        int order = dRe.Length + 1;
        var nbRe = new double[order];
        var nbIm = new double[order];
        var naRe = new double[order];
        var naIm = new double[order];
        Complex a0 = new(aRe[0], aIm?[0] ?? 0);
        for (int i = 0; i < bRe.Length; i++)
        {
            (nbRe[i], nbIm[i]) = FilterDivide(bRe[i], bIm?[i] ?? 0, a0, single);
        }

        for (int i = 0; i < aRe.Length; i++)
        {
            (naRe[i], naIm[i]) = FilterDivide(aRe[i], aIm?[i] ?? 0, a0, single);
        }

        if (single)
        {
            FilterLineIn(ToSingles(nbRe), ToSingles(nbIm), ToSingles(naRe), ToSingles(naIm),
                xRe, xIm, yRe, yIm, dRe, dIm, complex);
        }
        else
        {
            FilterLineIn(nbRe, nbIm, naRe, naIm, xRe, xIm, yRe, yIm, dRe, dIm, complex);
        }
    }

    /// <summary>A coefficient divided by <c>a(1)</c>, a plane at a time when <c>a(1)</c> is real.</summary>
    private static (double Re, double Im) FilterDivide(double re, double im, Complex a0, bool single)
    {
        if (a0.Imaginary == 0)
        {
            return single
                ? ((float)re / (float)a0.Real, (float)im / (float)a0.Real)
                : (re / a0.Real, im / a0.Real);
        }

        Complex q = new Complex(re, im) / a0;
        return single ? ((float)q.Real, (float)q.Imaginary) : (q.Real, q.Imaginary);
    }

    private static float[] ToSingles(double[] values)
    {
        var singles = new float[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            singles[i] = (float)values[i];
        }

        return singles;
    }

    /// <summary>The recurrence in <typeparamref name="T"/>'s own arithmetic.</summary>
    private static void FilterLineIn<T>(T[] bRe, T[] bIm, T[] aRe, T[] aIm,
        double[] xRe, double[] xIm, double[] yRe, double[] yIm, double[] dRe, double[] dIm, bool complex)
        where T : struct, IFloatingPointIeee754<T>
    {
        int delays = dRe.Length;
        var sRe = new T[delays];
        var sIm = new T[delays];
        for (int j = 0; j < delays; j++)
        {
            sRe[j] = T.CreateTruncating(dRe[j]);
            sIm[j] = T.CreateTruncating(dIm[j]);
        }

        for (int i = 0; i < xRe.Length; i++)
        {
            T inRe = T.CreateTruncating(xRe[i]);
            if (!complex)
            {
                T output = (bRe[0] * inRe) + (delays > 0 ? sRe[0] : T.Zero);
                for (int j = 0; j < delays; j++)
                {
                    T next = j + 1 < delays ? sRe[j + 1] : T.Zero;
                    sRe[j] = ((bRe[j + 1] * inRe) + next) - (aRe[j + 1] * output);
                }

                yRe[i] = double.CreateTruncating(output);
                continue;
            }

            T inIm = T.CreateTruncating(xIm[i]);
            (T pRe, T pIm) = Times(bRe[0], bIm[0], inRe, inIm);
            T outRe = pRe + (delays > 0 ? sRe[0] : T.Zero);
            T outIm = pIm + (delays > 0 ? sIm[0] : T.Zero);
            for (int j = 0; j < delays; j++)
            {
                T nextRe = j + 1 < delays ? sRe[j + 1] : T.Zero;
                T nextIm = j + 1 < delays ? sIm[j + 1] : T.Zero;
                (T fRe, T fIm) = Times(bRe[j + 1], bIm[j + 1], inRe, inIm);
                (T gRe, T gIm) = Times(aRe[j + 1], aIm[j + 1], outRe, outIm);
                sRe[j] = (fRe + nextRe) - gRe;
                sIm[j] = (fIm + nextIm) - gIm;
            }

            yRe[i] = double.CreateTruncating(outRe);
            yIm[i] = double.CreateTruncating(outIm);
        }

        for (int j = 0; j < delays; j++)
        {
            dRe[j] = double.CreateTruncating(sRe[j]);
            dIm[j] = double.CreateTruncating(sIm[j]);
        }

        static (T Re, T Im) Times(T ar, T ai, T br, T bi) =>
            ((ar * br) - (ai * bi), (ar * bi) + (ai * br));
    }

    /// <summary>
    /// R2025b's <c>zf</c> for an empty <c>a</c>: each delay the sum of the taps of <c>b</c> still
    /// waiting on the signal's last samples, undivided, the way a feed-forward state is summed.
    /// </summary>
    private static void FilterTail(double[] bRe, double[]? bIm, double[] xRe, double[] xIm,
        double[] dRe, double[] dIm, bool single)
    {
        int delays = dRe.Length;
        int n = xRe.Length;
        for (int j = 0; j < delays; j++)
        {
            int reach = System.Math.Min(delays - 1 - j, n - 1);
            double accRe = 0, accIm = 0;
            for (int m = reach; m >= 0; m--)
            {
                double br = bRe[j + 1 + m], bi = bIm?[j + 1 + m] ?? 0;
                double xr = xRe[n - 1 - m], xi = xIm[n - 1 - m];
                accRe = Round((Round(br * xr) - Round(bi * xi)) + accRe);
                accIm = Round((Round(br * xi) + Round(bi * xr)) + accIm);
            }

            dRe[j] = accRe;
            dIm[j] = accIm;
        }

        double Round(double v) => single ? (float)v : v;
    }
}
