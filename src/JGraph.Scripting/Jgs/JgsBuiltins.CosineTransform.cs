using JGraph.Imaging;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>dct</c> and <c>idct</c>, the one-dimensional cosine transforms (M123).
/// </summary>
/// <remarks>
/// <para>
/// The two-dimensional pair has been here since M46, and the line transform underneath it — an
/// orthonormal DCT-II built on one length-2n FFT rather than an n-by-n matrix product — is exactly
/// what MATLAB's <c>dct</c> computes. So the missing names were not a missing transform; they were a
/// missing argument grammar. This file is that grammar: a length to pad or crop to, a dimension to
/// run along, and the four types.
/// </para>
/// <para>
/// The types are a small family and each is a different cosine basis with a different pair of
/// endpoint weights. Two of them are already written: type 2 is the forward transform and type 3 is
/// its inverse. Types 1 and 4 are summed directly, at n-squared, because reaching them takes an
/// explicit <c>'Type'</c> and the fast road for each is a different rearrangement — a cost worth
/// paying only where somebody is paying attention to it.
/// </para>
/// <para>
/// Inverting a type means asking for another type, which is what makes <c>idct</c> a wrapper rather
/// than a second implementation: type 1 and type 4 are their own inverses, and 2 and 3 are each
/// other's.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers <c>dct</c> and <c>idct</c>.</summary>
    private static void RegisterCosineTransformBuiltins(JgsEnvironment env)
    {
        env.Declare("dct", JgsValue.Function(new BuiltinFunction("dct",
            (args, line, col) => CosineLine("dct", args, inverse: false, line, col))));
        env.Declare("idct", JgsValue.Function(new BuiltinFunction("idct",
            (args, line, col) => CosineLine("idct", args, inverse: true, line, col))));
    }

    /// <summary><c>dct(x)</c>, <c>dct(x, n)</c>, <c>dct(x, n, dim)</c> and <c>'Type', t</c>.</summary>
    private static JgsValue CosineLine(
        string name, IReadOnlyList<JgsValue> args, bool inverse, int line, int col)
    {
        ArityRange(name, args, 1, 5, line, col);
        int count = args.Count;

        int type = 2;
        if (count >= 3 && args[count - 2].Type == JgsType.String)
        {
            string word = args[count - 2].AsString;
            if (!word.Equals("Type", StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col, $"{name}: '{word}' is not an option this takes.");
            }

            type = Count(name, args, count - 1, line, col);
            if (type is < 1 or > 4)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: a cosine transform is of type 1, 2, 3 or 4, but got {type}.");
            }

            count -= 2;
        }

        if (count > 3)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} expects between 1 and 3 argument(s) before its options, but got {count}.");
        }

        int? length = count >= 2 && !IsEmptyValue(args[1]) ? Count(name, args, 1, line, col) : null;
        if (length is < 0)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: a transform length is a whole number that is not negative, but got {length}.");
        }

        int[] dims = TransformDims(args[0]);
        int dim = count >= 3 ? Count(name, args, 2, line, col) : JgsMatrix.DefaultDim(dims);
        if (dim < 1)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: a dimension is a positive whole number, but got {dim}.");
        }

        // Inverting is asking for the other type: 1 and 4 undo themselves, and 2 and 3 undo each
        // other, so there is one transform here and no second implementation to disagree with it.
        int wanted = inverse ? type switch { 2 => 3, 3 => 2, _ => type } : type;

        double[] flat = ToDoubles(name, args[0], line, col);
        (double[][] slices, _) = JgsMatrix.SlicesAlong(flat, dims, dim);
        int along = dim <= dims.Length ? dims[dim - 1] : 1;
        int n = length ?? along;

        var transformed = new double[slices.Length][];
        for (int s = 0; s < slices.Length; s++)
        {
            var buffer = new double[n];
            int copy = System.Math.Min(n, slices[s].Length);
            Array.Copy(slices[s], buffer, copy);
            transformed[s] = CosineOfType(buffer, wanted);
        }

        (double[] joined, _) = JgsMatrix.JoinAlong(transformed, dims, dim);
        JgsValue answer = JgsMatrix.FromColumnMajorDims(joined, JgsMatrix.ShapeAlong(dims, dim, n));
        return answer;
    }

    /// <summary>One line through the cosine transform of a given type, orthonormally scaled.</summary>
    private static double[] CosineOfType(double[] samples, int type)
    {
        int n = samples.Length;
        if (n <= 1)
        {
            // A single sample is its own transform under every one of the four scalings, and an
            // empty line has nothing to scale.
            return samples;
        }

        switch (type)
        {
            case 2:
                return CosineTransforms.Forward(samples);

            case 3:
                return CosineTransforms.Inverse(samples);

            case 1:
            {
                // The endpoints of a type-1 transform count half, on the way in and on the way out.
                var result = new double[n];
                double scale = System.Math.Sqrt(2.0 / (n - 1));
                double half = 1 / System.Math.Sqrt(2);
                for (int k = 0; k < n; k++)
                {
                    double sum = 0;
                    for (int j = 0; j < n; j++)
                    {
                        double weight = j == 0 || j == n - 1 ? half : 1;
                        sum += weight * samples[j] * System.Math.Cos(System.Math.PI * k * j / (n - 1.0));
                    }

                    result[k] = scale * (k == 0 || k == n - 1 ? half : 1) * sum;
                }

                return result;
            }

            default:
            {
                // Type 4: half a sample off at both ends, which is what makes it its own inverse.
                var result = new double[n];
                double scale = System.Math.Sqrt(2.0 / n);
                for (int k = 0; k < n; k++)
                {
                    double sum = 0;
                    for (int j = 0; j < n; j++)
                    {
                        sum += samples[j]
                            * System.Math.Cos(System.Math.PI * ((2 * j) + 1) * ((2 * k) + 1) / (4.0 * n));
                    }

                    result[k] = scale * sum;
                }

                return result;
            }
        }
    }
}
