using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The packed fast path under the transform family — <c>fft</c>, <c>ifft</c> and, one dimension at a
/// time, <c>fft2</c>/<c>fftn</c> and their inverses. When the subject is a packed array of plain
/// doubles the answer comes from <see cref="FftKernels"/> reading the storage where it lies;
/// anything else returns false untouched and the boxed road runs exactly as before, so the odd
/// forms and every error message keep their one home.
/// </summary>
/// <remarks>
/// <para>
/// The boxed road cost far more than a transform. A four-million-point <c>fft</c> boxed a
/// <see cref="System.Numerics.Complex"/> per sample on the way in, split it into two double arrays,
/// copied every slice into an array of its own, built a third array to transform, copied the answer
/// back out into two more, joined those, and boxed a Complex per sample again on the way out —
/// nine passes over sixty-four megabytes to wrap one that was the actual work.
/// </para>
/// <para>
/// What the kernels have to match is the butterfly, and they do: the same bit-reversal, the same
/// stage order and the same twiddles as the transform that stood here before, so for every length
/// under the factoring threshold the answer is the old one to the bit. The lengths above it are
/// factored and round differently — ADR 0096 says so, and says it is the same choice on every run.
/// </para>
/// </remarks>
internal static class PackedTransformOps
{
    /// <summary>
    /// One transform per slice along <paramref name="dim"/>, each padded or cut to
    /// <paramref name="length"/> first. False leaves the call to the boxed assembly untouched.
    /// </summary>
    public static bool TryTransform(
        JgsValue value, int? length, int dim, bool inverse, bool symmetric,
        out JgsValue result, out int[] shape)
    {
        result = value;
        shape = [];
        if (!IsTransformable(value))
        {
            return false;
        }

        int[] dims = JgsMatrix.DimsOf(value);  // only arrays reach here, so the shape is its own
        ReduceKernels.Split split = PackedReduceOps.SplitAlong(dims, dim);
        if (split.Total != value.ArrayLength)
        {
            return false; // a shape the wrapper and the storage do not agree on is the boxed road's
        }

        int n = length ?? split.Count;
        if (n < 1)
        {
            return false; // the boxed road throws the length error in its own words
        }

        long total = (long)split.Inner * n * split.Outer;
        if (total is 0 or > int.MaxValue)
        {
            return false;
        }

        bool complex = value.IsPackedComplex;
        NumericBuffer sourceRe = complex ? value.AsPackedComplex.Re : value.AsBuffer;
        NumericBuffer? sourceIm = complex ? value.AsPackedComplex.Im : null;
        NumericBuffer re = JgsPacking.Allocate(total);
        NumericBuffer im = JgsPacking.Allocate(total);
        FftKernels.TransformAlong(sourceRe, sourceIm, re, im, split, n, inverse, symmetric);
        GC.KeepAlive(value);

        result = Mint(re, im);
        shape = JgsMatrix.ShapeAlong(dims, dim, n);
        return true;
    }

    /// <summary>
    /// Whether this value takes the fast path: packing on, a non-empty packed array of plain
    /// doubles, real or complex. A logical array, a single, an integer class or a boxed array is
    /// left to the road that knows what class its answer comes back as.
    /// </summary>
    private static bool IsTransformable(JgsValue value)
    {
        if (!JgsPacking.Enabled
            || value.Type != JgsType.Array
            || value.ArrayLength == 0
            || value.NumericClass != JgsNumericClass.Double)
        {
            return false;
        }

        return value.IsPackedComplex
            || (value.IsPacked && value.PackedKind == JgsPackedKind.Number);
    }

    /// <summary>
    /// The answer, minted the way the boxed assembly mints it: an array with nothing imaginary
    /// anywhere comes back as plain numbers, because that is what a Complex with a zero imaginary
    /// part reads back as one element at a time.
    /// </summary>
    private static JgsValue Mint(NumericBuffer re, NumericBuffer im)
    {
        bool imaginary = false;
        foreach (double part in im.AsSpan())
        {
            if (part != 0)
            {
                imaginary = true;
                break;
            }
        }

        if (imaginary)
        {
            return JgsValue.PackedComplexArray(new JgsPackedComplex(re, im));
        }

        im.Dispose();
        return JgsValue.Packed(re);
    }
}
