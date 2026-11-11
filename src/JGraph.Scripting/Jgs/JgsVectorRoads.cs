using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The every-row plan's I5 (ADR 0328): the data-preparation names over a packed array of plain
/// doubles, read where it lies and answered into packed storage, where the general roads flatten
/// the array into a managed copy, make a list of what is not NaN, and build the answer from boxed
/// pieces.
/// </summary>
/// <remarks>
/// <para>
/// A road is taken only for the plainest call of each name (the array alone, the default method)
/// and only for an input it answers exactly as the general road does. Whatever the general road
/// has a rule for (a NaN to step over, an infinity, a constant signal, a one-element answer) sends
/// the call back to it, so each rule keeps one home. The arithmetic per element is the general
/// road's, in the same order, so the answers agree to the bit.
/// </para>
/// <para><c>JGRAPH_VECTOR_FAST=0</c> turns every road here off, which is what the measurement's
/// off condition and the tests use.</para>
/// </remarks>
internal static class JgsVectorRoads
{
    /// <summary>Whether the roads are taken (<c>JGRAPH_VECTOR_FAST</c>, on unless it is <c>0</c>).</summary>
    internal static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("JGRAPH_VECTOR_FAST") != "0";

    /// <summary>Above this many readings the general road selects a median rather than sorting for
    /// it, and so does the road here.</summary>
    private const int SelectionWorthwhile = 64;

    private static long _roads;

    /// <summary>How many calls have taken a road (the tests read it to see a road taken).</summary>
    internal static long Roads => Interlocked.Read(ref _roads);

    internal static void Took() => Interlocked.Increment(ref _roads);

    /// <summary>A packed row or column of plain doubles with at least two elements.</summary>
    private static bool IsLongVector(JgsValue value) =>
        Enabled
        && JgsSetKernels.IsPlainDoubles(value)
        && value.ArrayLength >= 2
        && (JgsMatrix.RowCount(value) == 1 || JgsMatrix.ColCount(value) == 1);

    private static JgsValue Like(JgsValue subject, NumericBuffer answer, JgsPackedKind kind = JgsPackedKind.Number) =>
        JgsValue.Shaped(answer, JgsMatrix.RowCount(subject), JgsMatrix.ColCount(subject), kind);

    /// <summary>
    /// <c>normalize(x)</c>, the z-score of a vector: the mean and the deviation are each folded in
    /// grains across threads, and the transform is one pass. A sum that is not finite (a NaN, an
    /// infinity) is the general road's.
    /// </summary>
    internal static bool TryNormalize(JgsValue subject, out JgsValue result)
    {
        result = JgsValue.Null;
        if (!IsLongVector(subject))
        {
            return false;
        }

        NumericBuffer x = subject.AsBuffer;
        int n = x.Length;
        double sum = ReduceKernels.GrainSum(x, 0, n);
        if (!double.IsFinite(sum))
        {
            return false;
        }

        double centre = sum / n;
        double scale = Math.Sqrt(ReduceKernels.GrainSquaredDeviations(x, 0, n, centre) / (n - 1));
        if (scale == 0 || double.IsNaN(scale))
        {
            scale = 1;
        }

        NumericBuffer dest = JgsPacking.AllocateForOverwrite(n);
        VectorKernels.CentreAndScale(x, dest, centre, scale);
        GC.KeepAlive(subject);
        Took();
        result = Like(subject, dest);
        return true;
    }

    /// <summary>
    /// <c>rescale(A)</c> onto [0, 1]: one scan for the range, one pass for the stretch. A NaN and
    /// a constant array are the general road's.
    /// </summary>
    internal static bool TryRescale(JgsValue subject, out JgsValue result)
    {
        result = JgsValue.Null;
        if (!Enabled || !JgsSetKernels.IsPlainDoubles(subject) || subject.ArrayLength < 2)
        {
            return false;
        }

        NumericBuffer x = subject.AsBuffer;
        if (!VectorKernels.TryRange(x, out double from, out double to))
        {
            return false;
        }

        double span = to - from;
        if (span == 0 || double.IsNaN(span))
        {
            return false;
        }

        NumericBuffer dest = JgsPacking.AllocateForOverwrite(x.Length);
        VectorKernels.Stretch(x, dest, from, span);
        GC.KeepAlive(subject);
        Took();
        result = Like(subject, dest);
        return true;
    }

    /// <summary><c>gradient(v)</c> of a vector at unit spacing.</summary>
    internal static bool TryGradient(JgsValue subject, out JgsValue result)
    {
        result = JgsValue.Null;
        if (!IsLongVector(subject))
        {
            return false;
        }

        NumericBuffer x = subject.AsBuffer;
        NumericBuffer dest = JgsPacking.AllocateForOverwrite(x.Length);
        VectorKernels.CentralDifferences(x, dest);
        GC.KeepAlive(subject);
        Took();
        result = Like(subject, dest);
        return true;
    }

    /// <summary>
    /// <c>[FX, FY] = gradient(F)</c> of a matrix at unit spacing (the every-row plan's I9, ADR
    /// 0330): each field is whole columns subtracted at a time, strips of columns across threads,
    /// where the general road copied every row out, differenced it and copied it back on one
    /// thread. The second field is made only when it is asked for.
    /// </summary>
    internal static bool TryGradientField(JgsValue subject, int wanted, out JgsValue[] results)
    {
        results = [];
        if (!Enabled || !JgsSetKernels.IsPlainDoubles(subject))
        {
            return false;
        }

        int rows = JgsMatrix.RowCount(subject);
        int cols = JgsMatrix.ColCount(subject);
        NumericBuffer f = subject.AsBuffer;
        if (rows < 2 || cols < 2 || (long)rows * cols != f.Length)
        {
            return false;
        }

        NumericBuffer across = JgsPacking.AllocateForOverwrite(f.Length);
        VectorKernels.CentralDifferencesAcross(f, rows, cols, across);
        if (wanted <= 1)
        {
            results = [Like(subject, across)];
        }
        else
        {
            NumericBuffer down = JgsPacking.AllocateForOverwrite(f.Length);
            VectorKernels.CentralDifferencesDown(f, rows, cols, down);
            results = [Like(subject, across), Like(subject, down)];
        }

        GC.KeepAlive(subject);
        Took();
        return true;
    }

    /// <summary><c>cumtrapz(v)</c> of a vector at unit spacing.</summary>
    internal static bool TryCumtrapz(JgsValue subject, out JgsValue result)
    {
        result = JgsValue.Null;
        if (!IsLongVector(subject))
        {
            return false;
        }

        NumericBuffer x = subject.AsBuffer;
        NumericBuffer dest = JgsPacking.AllocateForOverwrite(x.Length);
        VectorKernels.RunningTrapezoid(x, dest);
        GC.KeepAlive(subject);
        Took();
        result = Like(subject, dest);
        return true;
    }

    /// <summary>
    /// <c>[B, TF] = rmmissing(v)</c> of a vector: the NaN are counted grain by grain, the survivors
    /// copied to where the counts say they go, and the mask is made only when it is asked for. An
    /// answer of fewer than two elements is the general road's.
    /// </summary>
    internal static bool TryRemoveMissing(JgsValue subject, int wanted, out JgsValue[] results)
    {
        results = [];
        if (!IsLongVector(subject))
        {
            return false;
        }

        NumericBuffer x = subject.AsBuffer;
        int n = x.Length;
        int[] missing = VectorKernels.NaNCounts(x);
        int kept = n;
        foreach (int count in missing)
        {
            kept -= count;
        }

        if (kept < 2)
        {
            return false;
        }

        NumericBuffer dest = JgsPacking.AllocateForOverwrite(kept);
        VectorKernels.CopyWithoutNaN(x, missing, dest);
        bool column = JgsMatrix.RowCount(subject) > 1;
        JgsValue values = JgsValue.Shaped(dest, column ? kept : 1, column ? 1 : kept);
        if (wanted < 2)
        {
            results = [values];
        }
        else
        {
            NumericBuffer mask = JgsPacking.AllocateForOverwrite(n);
            PackedMath.Mask(PackedMath.MaskTest.IsNaN, x, mask);
            results = [values, Like(subject, mask, JgsPackedKind.Bool)];
        }

        GC.KeepAlive(subject);
        Took();
        return true;
    }

    /// <summary>
    /// <c>isoutlier(v)</c> by the default rule, more than <paramref name="threshold"/> scaled
    /// median absolute deviations from the median: two selections over one scratch copy and one
    /// pass for the mask. Anything not finite is the general road's, and so is a vector short
    /// enough that the general road sorts for its median.
    /// </summary>
    internal static bool TryOutliers(JgsValue subject, double threshold, double madFactor, out JgsValue mask)
    {
        mask = JgsValue.Null;
        if (!IsLongVector(subject) || subject.ArrayLength <= SelectionWorthwhile)
        {
            return false;
        }

        NumericBuffer x = subject.AsBuffer;
        if (!VectorKernels.AllFinite(x))
        {
            return false;
        }

        int n = x.Length;
        double[] scratch = GC.AllocateUninitializedArray<double>(n);
        x.AsSpan().CopyTo(scratch);
        double centre = MiddleOf(scratch);
        VectorKernels.AbsoluteDeviations(x, scratch, centre);
        double spread = madFactor * MiddleOf(scratch);
        double low = centre - (threshold * spread);
        double high = centre + (threshold * spread);

        NumericBuffer dest = JgsPacking.AllocateForOverwrite(n);
        VectorKernels.OutsideFences(x, dest, low, high);
        GC.KeepAlive(subject);
        Took();
        mask = Like(subject, dest, JgsPackedKind.Bool);
        return true;
    }

    /// <summary>The median of more than <see cref="SelectionWorthwhile"/> values holding no NaN,
    /// by selection; the array is reordered.</summary>
    private static double MiddleOf(double[] values)
    {
        int mid = values.Length / 2;
        if (values.Length % 2 == 1)
        {
            Span<int> one = [mid];
            SelectKernels.PartialSort(values, one);
            return values[mid];
        }

        Span<int> pair = [mid - 1, mid];
        SelectKernels.PartialSort(values, pair);
        return (values[mid - 1] + values[mid]) / 2.0;
    }

    /// <summary>
    /// <c>isprime(A)</c> where the values are dense in their own range: one sieve up to the
    /// greatest, one lookup per element. A NaN, a value past 2^31 and a range much wider than the
    /// array is long are the general road's (which divides each value on its own).
    /// </summary>
    internal static bool TryIsPrime(JgsValue subject, out JgsValue result)
    {
        result = JgsValue.Null;
        if (!Enabled || !JgsSetKernels.IsPlainDoubles(subject) || subject.ArrayLength < 2)
        {
            return false;
        }

        NumericBuffer x = subject.AsBuffer;
        if (!VectorKernels.TryRange(x, out _, out double greatest))
        {
            return false;
        }

        long reach = (64L * x.Length) + (1 << 20);
        if (!(greatest <= Math.Min(reach, int.MaxValue - 2)))
        {
            return false;
        }

        byte[] composite = PrimeKernels.OddComposites(Math.Max(3, (long)greatest));
        NumericBuffer dest = JgsPacking.AllocateForOverwrite(x.Length);
        PrimeKernels.LookUp(x, composite, dest);
        GC.KeepAlive(subject);
        Took();
        result = Like(subject, dest, JgsPackedKind.Bool);
        return true;
    }
}
