using System.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The transform family's documented forms (M76): a length and a dimension for <c>fft</c> and
/// <c>ifft</c>, sizes for the two- and N-dimensional ones, the symmetry flag, and the initial and
/// final conditions <c>filter</c> carries between calls.
/// </summary>
/// <remarks>
/// The shape correction lives here too. A transform of a matrix used to run over all m·n elements
/// as though they were one vector; MATLAB transforms each column, and every other reduction in this
/// build already walks the first non-singleton dimension. Doing the same makes <c>fft</c> agree with
/// its neighbours and with MATLAB at once, and no frozen script transformed a matrix.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// One discrete Fourier transform per slice along <paramref name="dim"/>, padding or truncating
    /// each to <paramref name="length"/> first.
    /// </summary>
    private static JgsValue TransformAlong(string name, JgsValue value, int? length, int dim,
        bool inverse, bool symmetric, int line, int col)
    {
        int[] dims = JgsMatrix.DimsOf(value);
        Complex[] flat = ComplexArrayOf(name, value, line, col);

        var real = new double[flat.Length];
        var imaginary = new double[flat.Length];
        for (int i = 0; i < flat.Length; i++)
        {
            real[i] = flat[i].Real;
            imaginary[i] = flat[i].Imaginary;
        }

        (double[][] realSlices, _) = JgsMatrix.SlicesAlong(real, dims, dim);
        (double[][] imaginarySlices, _) = JgsMatrix.SlicesAlong(imaginary, dims, dim);

        int along = dim <= dims.Length ? dims[dim - 1] : 1;
        int n = length ?? along;
        if (n < 1)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: a transform length is a positive whole number, but got {n}.");
        }

        var outRealSlices = new double[realSlices.Length][];
        var outImaginarySlices = new double[realSlices.Length][];
        for (int s = 0; s < realSlices.Length; s++)
        {
            var buffer = new Complex[n];
            int copy = System.Math.Min(n, realSlices[s].Length);
            for (int i = 0; i < copy; i++)
            {
                buffer[i] = new Complex(realSlices[s][i], imaginarySlices[s][i]);
            }

            if (symmetric)
            {
                MakeHermitian(buffer);
            }

            JGraph.Signal.Fft.Transform(buffer, inverse);

            var outReal = new double[n];
            var outImaginary = new double[n];
            for (int i = 0; i < n; i++)
            {
                outReal[i] = buffer[i].Real;

                // 'symmetric' promises the transform of a conjugate-symmetric spectrum, whose
                // inverse is real. Dropping the imaginary part is the promise being kept rather than
                // an approximation: it is rounding, and MATLAB drops it for the same reason.
                outImaginary[i] = symmetric ? 0 : buffer[i].Imaginary;
            }

            outRealSlices[s] = outReal;
            outImaginarySlices[s] = outImaginary;
        }

        (double[] joinedReal, int[] shape) = JgsMatrix.JoinAlong(outRealSlices, dims, dim);
        (double[] joinedImaginary, _) = JgsMatrix.JoinAlong(outImaginarySlices, dims, dim);

        var combined = new Complex[joinedReal.Length];
        for (int i = 0; i < combined.Length; i++)
        {
            combined[i] = new Complex(joinedReal[i], joinedImaginary[i]);
        }

        JgsValue result = FromComplexArray(combined);
        Shape(result, shape);
        return result;
    }

    /// <summary>Forces a spectrum to be conjugate-symmetric, which is what <c>'symmetric'</c> asserts.</summary>
    private static void MakeHermitian(Complex[] spectrum)
    {
        int n = spectrum.Length;
        spectrum[0] = new Complex(spectrum[0].Real, 0);
        if (n % 2 == 0)
        {
            spectrum[n / 2] = new Complex(spectrum[n / 2].Real, 0);
        }

        for (int i = 1; i < (n + 1) / 2; i++)
        {
            spectrum[n - i] = Complex.Conjugate(spectrum[i]);
        }
    }

    /// <summary>Gives a freshly built flat value the shape its dimensions call for.</summary>
    private static void Shape(JgsValue value, IReadOnlyList<int> dims)
    {
        int total = 1;
        foreach (int size in dims)
        {
            total *= size;
        }

        if (total != value.ArrayLength || value.Type == JgsType.Number)
        {
            return;
        }

        if (dims.Count <= 2)
        {
            value.Reshape(dims.Count > 0 ? dims[0] : total, dims.Count > 1 ? dims[1] : 1);
            return;
        }

        value.ReshapeDims(dims);
    }

    /// <summary>
    /// A transform over several dimensions at once — <c>fft2</c> and <c>fftn</c> — optionally
    /// resizing each of them first.
    /// </summary>
    private static JgsValue TransformAcross(string name, JgsValue value, IReadOnlyList<int>? sizes,
        bool inverse, bool symmetric, int line, int col)
    {
        int[] dims = JgsMatrix.DimsOf(value);
        int count = sizes?.Count ?? System.Math.Max(dims.Length, 2);

        JgsValue running = value;
        for (int d = 1; d <= count; d++)
        {
            int? length = sizes is null ? null : sizes[d - 1];

            // The symmetry flag describes the whole transform, and forcing it per dimension would
            // impose it on intermediate results that are not meant to have it. It is applied on the
            // last pass, which is the one whose answer is handed back.
            running = TransformAlong(name, running, length, d, inverse, symmetric && d == count, line, col);
        }

        return running;
    }

    /// <summary>The trailing <c>'symmetric'</c>/<c>'nonsymmetric'</c> word, if there is one.</summary>
    private static bool SymmetryFlag(string name, IReadOnlyList<JgsValue> args, ref int count,
        int line, int col)
    {
        if (count == 0 || !IsTextScalar(args[count - 1]))
        {
            return false;
        }

        string word = Str(name, args, count - 1, line, col).ToLowerInvariant();
        count--;
        return word switch
        {
            "symmetric" => true,
            "nonsymmetric" => false,
            _ => throw new JgsRuntimeException(line, col,
                $"{name}: '{word}' is not 'symmetric' or 'nonsymmetric'."),
        };
    }

    /// <summary><c>fft(X)</c>, <c>fft(X, n)</c>, <c>fft(X, n, dim)</c> and the inverse's symmetry word.</summary>
    private static JgsValue OneDimensionalTransform(string name, IReadOnlyList<JgsValue> args,
        bool inverse, int line, int col)
    {
        ArityRange(name, args, 1, inverse ? 4 : 3, line, col);
        int count = args.Count;
        bool symmetric = inverse && SymmetryFlag(name, args, ref count, line, col);
        if (count > 3)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} expects between 1 and 3 argument(s), but got {count}.");
        }

        // An empty length is MATLAB's way of naming a dimension without resizing.
        int? length = count >= 2 && !IsEmptyValue(args[1])
            ? Count(name, args, 1, line, col)
            : null;

        int dim = count >= 3
            ? Count(name, args, 2, line, col)
            : JgsMatrix.DefaultDim(JgsMatrix.DimsOf(args[0]));

        if (dim < 1)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: a dimension is a positive whole number, but got {dim}.");
        }

        return TransformAlong(name, args[0], length, dim, inverse, symmetric, line, col);
    }

    /// <summary><c>fft2</c>/<c>ifft2</c> and <c>fftn</c>/<c>ifftn</c>, with their sizes.</summary>
    private static JgsValue ManyDimensionalTransform(string name, IReadOnlyList<JgsValue> args,
        bool inverse, bool planar, int line, int col)
    {
        ArityRange(name, args, 1, planar ? 4 : 3, line, col);
        int count = args.Count;
        bool symmetric = inverse && SymmetryFlag(name, args, ref count, line, col);

        List<int>? sizes = null;
        if (planar && count == 3)
        {
            sizes = [Count(name, args, 1, line, col), Count(name, args, 2, line, col)];
        }
        else if (count == 2)
        {
            // fftn takes the whole size as one vector; fft2 written with one size is not a form.
            double[] wanted = NumericVector(name, args, 1, line, col).ToArray();
            if (planar && wanted.Length != 2)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} resizes two dimensions, so it needs m and n.");
            }

            sizes = [];
            foreach (double size in wanted)
            {
                sizes.Add((int)size);
            }
        }
        else if (count > 3)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} expects between 1 and {(planar ? 3 : 2)} argument(s), but got {count}.");
        }

        if (planar && sizes is null)
        {
            int[] dims = JgsMatrix.DimsOf(args[0]);
            sizes = [dims.Length > 0 ? dims[0] : 1, dims.Length > 1 ? dims[1] : 1];
        }

        return TransformAcross(name, args[0], sizes, inverse, symmetric, line, col);
    }

    /// <summary>
    /// A value's elements as complex numbers in column-major order, whatever it is made of. The
    /// existing reader takes an argument list and refuses a complex matrix on the way in, which is
    /// why <c>ifft2(fft2(A))</c> could not be written before M76.
    /// </summary>
    private static Complex[] ComplexArrayOf(string name, JgsValue value, int line, int col) =>
        ComplexArray(name, new[] { value }, 0, line, col);

    /// <summary>
    /// <c>fftshift</c> and <c>ifftshift</c>, over one named dimension or over every dimension at
    /// once — which for a matrix is what swaps the quadrants rather than only the columns.
    /// </summary>
    private static JgsValue Rotated(string name, IReadOnlyList<JgsValue> args, bool forward,
        int line, int col)
    {
        JgsValue value = args[0];
        int[] dims = JgsMatrix.DimsOf(value);
        var elements = new JgsValue[value.ArrayLength];
        for (int i = 0; i < elements.Length; i++)
        {
            elements[i] = value.ElementAt(i);
        }

        if (args.Count == 2)
        {
            int dim = Count(name, args, 1, line, col);
            if (dim < 1)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: a dimension is a positive whole number, but got {dim}.");
            }

            elements = RotateAlong(elements, dims, dim, forward);
        }
        else
        {
            for (int dim = 1; dim <= System.Math.Max(dims.Length, 1); dim++)
            {
                if (dim <= dims.Length && dims[dim - 1] > 1)
                {
                    elements = RotateAlong(elements, dims, dim, forward);
                }
            }
        }

        JgsValue result = JgsValue.Array(elements);
        Shape(result, dims);
        return result;
    }

    /// <summary>Rotates each slice along one dimension, which is what a shift of a spectrum is.</summary>
    private static JgsValue[] RotateAlong(JgsValue[] elements, IReadOnlyList<int> dims, int dim,
        bool forward)
    {
        int inner = 1;
        for (int i = 0; i < dim - 1 && i < dims.Count; i++)
        {
            inner *= dims[i];
        }

        int length = dim <= dims.Count ? dims[dim - 1] : 1;
        int outer = length == 0 || inner == 0 ? 0 : elements.Length / (inner * length);
        int shift = forward ? length - ((length + 1) / 2) : (length + 1) / 2;

        var rotated = new JgsValue[elements.Length];
        for (int o = 0; o < outer; o++)
        {
            int page = o * inner * length;
            for (int i = 0; i < inner; i++)
            {
                for (int j = 0; j < length; j++)
                {
                    int from = ((j + length - shift) % length * inner) + i + page;
                    rotated[(j * inner) + i + page] = elements[from];
                }
            }
        }

        return rotated;
    }

    // --- filter ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>filter(b, a, x)</c> with the initial conditions it resumes from, the dimension it walks,
    /// and the final conditions it leaves off at.
    /// </summary>
    private static JgsValue[] FilterAnswer(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("filter", args, 3, 5, line, col);
        double[] numerator = NumericVector("filter", args, 0, line, col).ToArray();
        double[] denominator = NumericVector("filter", args, 1, line, col).ToArray();

        int order = System.Math.Max(numerator.Length, denominator.Length) - 1;
        double[]? initial = null;
        if (args.Count >= 4 && !IsEmptyValue(args[3]))
        {
            initial = NumericVector("filter", args, 3, line, col).ToArray();
            if (initial.Length != order)
            {
                throw new JgsRuntimeException(line, col,
                    $"filter: the initial conditions need one entry per filter delay ({order}), " +
                    $"but got {initial.Length}.");
            }
        }

        JgsValue signal = args[2];
        int[] dims = JgsMatrix.DimsOf(signal);
        int dim = args.Count >= 5
            ? Count("filter", args, 4, line, col)
            : JgsMatrix.DefaultDim(dims);

        double[] flat = FlattenColumnMajor("filter", signal, line, col);
        (double[][] slices, _) = JgsMatrix.SlicesAlong(flat, dims, dim);

        var filtered = new double[slices.Length][];
        var finals = new double[slices.Length][];
        for (int s = 0; s < slices.Length; s++)
        {
            var state = new double[System.Math.Max(order, 0)];
            initial?.CopyTo(state, 0);
            filtered[s] = JGraph.Signal.DigitalFilter.Filter(numerator, denominator, slices[s], state);
            finals[s] = state;
        }

        (double[] joined, int[] shape) = JgsMatrix.JoinAlong(filtered, dims, dim);
        JgsValue answer = Numbers(joined);
        Shape(answer, shape);
        if (wanted <= 1)
        {
            return [answer];
        }

        // The final conditions of every slice, laid out the way the initial ones arrive: one column
        // per slice, so that a matrix filtered in pieces can be resumed column by column.
        (double[] joinedFinals, int[] finalShape) = JgsMatrix.JoinAlong(finals, dims, dim);
        JgsValue rest = Numbers(joinedFinals);
        Shape(rest, finalShape);
        return [answer, rest];
    }
}
