using System.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Signal;

/// <summary>
/// The band transforms that turn an analogue lowpass prototype into the filter that was actually
/// asked for, the two maps from the s-plane to the z-plane, and the analogue frequency response
/// (M134).
/// </summary>
/// <remarks>
/// <para>
/// Every one of these has a one-line substitution behind it — <c>s → s/ω₀</c> for a lowpass,
/// <c>s → ω₀/s</c> for a highpass, <c>s → (s² + ω₀²)/(s·B)</c> for a bandpass — and MATLAB performs
/// none of them on the coefficients. They are done on the state-space quadruple instead, because a
/// substitution into a polynomial of order sixteen is an exercise in cancelling large numbers,
/// while the same transform on a state matrix is a solve. The difference shows in the sixth figure
/// of an eighth-order design and in every figure of a twentieth-order one.
/// </para>
/// <para>
/// The transfer-function forms of the same names are the state-space ones with a conversion on each
/// end, which is what MATLAB's sources do too — including taking the transformed numerator from the
/// system's transmission zeros rather than from the algebra.
/// </para>
/// </remarks>
public static class FrequencyTransforms
{
    /// <summary>A state-space quadruple, in the shape the transforms pass around.</summary>
    public readonly record struct Block(double[,] A, double[,] B, double[,] C, double[,] D);

    /// <summary><c>lp2lp</c> in state space: the prototype rescaled to a cutoff of <paramref name="cutoff"/>.</summary>
    public static Block LowpassToLowpass(Block block, double cutoff)
    {
        double[,] a = Scale(block.A, cutoff);
        double[,] b = Scale(block.B, cutoff);
        return new Block(a, b, Copy(block.C), Copy(block.D));
    }

    /// <summary><c>lp2hp</c> in state space: the state matrix inverted, which turns the band inside out.</summary>
    public static Block LowpassToHighpass(Block block, double cutoff)
    {
        int n = block.A.GetLength(0);
        if (n == 0)
        {
            return new Block(Copy(block.A), Copy(block.B), Copy(block.C), Copy(block.D));
        }

        double[,] inverse = Linear.Solve(block.A, Linear.Identity(n));
        double[,] a = Scale(inverse, cutoff);
        double[,] solved = Linear.Solve(block.A, block.B);
        double[,] b = Scale(solved, -cutoff);

        // C/A and D − C·A⁻¹·B, both written as one solve against the transpose.
        double[,] c = RightDivide(block.C, block.A);
        double[,] d = Subtract(block.D, Linear.Multiply(c, block.B));
        return new Block(a, b, c, d);
    }

    /// <summary>
    /// <c>lp2bp</c> in state space: the state is doubled, one half carrying the band's centre and
    /// the other its width, which is the whole of why a bandpass design has twice the order.
    /// </summary>
    public static Block LowpassToBandpass(Block block, double centre, double bandwidth)
    {
        int n = block.A.GetLength(0);
        int inputs = block.B.GetLength(1);
        int outputs = block.C.GetLength(0);
        double q = centre / bandwidth;

        var a = new double[2 * n, 2 * n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                a[i, j] = centre * block.A[i, j] / q;
            }

            a[i, n + i] = centre;
            a[n + i, i] = -centre;
        }

        var b = new double[2 * n, inputs];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < inputs; j++)
            {
                b[i, j] = centre * block.B[i, j] / q;
            }
        }

        var c = new double[outputs, 2 * n];
        for (int i = 0; i < outputs; i++)
        {
            for (int j = 0; j < n; j++)
            {
                c[i, j] = block.C[i, j];
            }
        }

        return new Block(a, b, c, Copy(block.D));
    }

    /// <summary><c>lp2bs</c> in state space: the bandpass shape with the state matrix inverted.</summary>
    public static Block LowpassToBandstop(Block block, double centre, double bandwidth)
    {
        int n = block.A.GetLength(0);
        int inputs = block.B.GetLength(1);
        int outputs = block.C.GetLength(0);
        double q = centre / bandwidth;

        double[,] inverse = n == 0 ? block.A : Linear.Solve(block.A, Linear.Identity(n));
        double[,] solved = n == 0 ? block.B : Linear.Solve(block.A, block.B);
        double[,] overA = n == 0 ? block.C : RightDivide(block.C, block.A);

        var a = new double[2 * n, 2 * n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                a[i, j] = centre * inverse[i, j] / q;
            }

            a[i, n + i] = centre;
            a[n + i, i] = -centre;
        }

        var b = new double[2 * n, inputs];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < inputs; j++)
            {
                b[i, j] = -centre * solved[i, j] / q;
            }
        }

        var c = new double[outputs, 2 * n];
        for (int i = 0; i < outputs; i++)
        {
            for (int j = 0; j < n; j++)
            {
                c[i, j] = overA[i, j];
            }
        }

        double[,] d = n == 0 ? Copy(block.D) : Subtract(block.D, Linear.Multiply(overA, block.B));
        return new Block(a, b, c, d);
    }

    /// <summary>
    /// The transfer-function form of a band transform: through state space and back, with the
    /// numerator read off the transformed system's transmission zeros.
    /// </summary>
    public static (double[] Numerator, double[] Denominator) TransformTransferFunction(
        ReadOnlySpan<double> numerator, ReadOnlySpan<double> denominator, Func<Block, Block> transform)
    {
        FilterCoefficients.StateSpace ss = FilterCoefficients.TfToSs(
            numerator.ToArray(), 1, numerator.Length, denominator);
        Block moved = transform(new Block(ss.A, ss.B, ss.C, ss.D));

        double[] den = FilterCoefficients.RealPolynomial(Poles(moved.A));
        (Complex[] zeros, _, double gain) = FilterCoefficients.SsToZp(moved.A, moved.B, moved.C, moved.D, 0);
        double[] zeroPoly = FilterCoefficients.RealPolynomial(zeros);
        var num = new double[zeroPoly.Length];
        for (int i = 0; i < num.Length; i++)
        {
            num[i] = gain * zeroPoly[i];
        }

        return (num, den);
    }

    // --- The bilinear transform ----------------------------------------------------------------

    /// <summary>
    /// <c>bilinear</c> on roots: each analogue root mapped by <c>z = (1 + s/2fs)/(1 − s/2fs)</c>,
    /// with the zeros the system does not have landing at <c>z = −1</c>.
    /// </summary>
    public static (Complex[] Zeros, Complex[] Poles, double Gain) BilinearRoots(
        ReadOnlySpan<Complex> zeros, ReadOnlySpan<Complex> poles, double gain, double sampleRate)
    {
        double fs = 2 * sampleRate;
        Complex zeroProduct = Complex.One;
        var mappedZeros = new List<Complex>(zeros.Length);
        foreach (Complex z in zeros)
        {
            if (!IsFinite(z))
            {
                continue;
            }

            zeroProduct *= fs - z;
            mappedZeros.Add((1 + (z / fs)) / (1 - (z / fs)));
        }

        Complex poleProduct = Complex.One;
        var mappedPoles = new Complex[poles.Length];
        for (int i = 0; i < poles.Length; i++)
        {
            poleProduct *= fs - poles[i];
            mappedPoles[i] = (1 + (poles[i] / fs)) / (1 - (poles[i] / fs));
        }

        while (mappedZeros.Count < mappedPoles.Length)
        {
            mappedZeros.Add(-Complex.One);
        }

        Complex k = gain * zeroProduct / poleProduct;
        return ([.. mappedZeros], mappedPoles, k.Real);
    }

    /// <summary>
    /// <c>bilinear</c> in state space: two shifted copies of the state matrix and one solve, which
    /// is the same map done without ever writing a polynomial down.
    /// </summary>
    public static Block BilinearBlock(Block block, double sampleRate)
    {
        int n = block.A.GetLength(0);
        double t = 1 / sampleRate;
        double r = System.Math.Sqrt(t);

        var t1 = new double[n, n];
        var t2 = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                double half = block.A[i, j] * t / 2;
                t1[i, j] = (i == j ? 1 : 0) + half;
                t2[i, j] = (i == j ? 1 : 0) - half;
            }
        }

        double[,] ad = n == 0 ? block.A : Linear.Solve(t2, t1);
        double[,] bd = n == 0 ? Copy(block.B) : Scale(Linear.Solve(t2, block.B), t / r);
        double[,] overT2 = n == 0 ? Copy(block.C) : RightDivide(block.C, t2);
        double[,] cd = Scale(overT2, r);
        double[,] dd = n == 0
            ? Copy(block.D)
            : Add(Scale(Linear.Multiply(overT2, block.B), t / 2), block.D);

        return new Block(ad, bd, cd, dd);
    }

    /// <summary>
    /// <c>bilinear</c> on coefficients: the state-space map, with the numerator recovered from the
    /// closed-loop characteristic polynomial rather than from the zeros.
    /// </summary>
    public static (double[] Numerator, double[] Denominator) BilinearTransferFunction(
        ReadOnlySpan<double> numerator, ReadOnlySpan<double> denominator, double sampleRate)
    {
        FilterCoefficients.StateSpace ss = FilterCoefficients.TfToSs(
            numerator.ToArray(), 1, numerator.Length, denominator);
        Block moved = BilinearBlock(new Block(ss.A, ss.B, ss.C, ss.D), sampleRate);

        double[] den = FilterCoefficients.RealPolynomial(Poles(moved.A));
        int n = moved.A.GetLength(0);
        var closed = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                closed[i, j] = moved.A[i, j] - (moved.B[i, 0] * moved.C[0, j]);
            }
        }

        double[] num = FilterCoefficients.RealPolynomial(Poles(closed));
        double direct = moved.D[0, 0] - 1;
        for (int i = 0; i < num.Length; i++)
        {
            num[i] += direct * den[i];
        }

        return (num, den);
    }

    /// <summary>
    /// The pre-warped sample rate a <c>bilinear</c> call with a match frequency uses, which is what
    /// makes one analogue frequency land exactly where it was asked for.
    /// </summary>
    public static double PreWarp(double sampleRate, double matchFrequency) =>
        System.Math.PI * matchFrequency / System.Math.Tan(System.Math.PI * matchFrequency / sampleRate);

    // --- Impulse invariance --------------------------------------------------------------------

    /// <summary>
    /// <c>impinvar</c>: the digital filter whose impulse response is the analogue one sampled, found
    /// by expanding into partial fractions, sampling each term, and putting the sum back together.
    /// </summary>
    /// <remarks>
    /// The partial fraction is done on the averaged poles of each repeated group rather than on the
    /// poles themselves, and the residue of a repeated pole comes from successive derivatives of the
    /// same quotient — which is the only way a double pole survives the round trip at all.
    /// </remarks>
    public static (double[] Numerator, double[] Denominator, bool Robust) ImpulseInvariant(
        ReadOnlySpan<double> bIn, ReadOnlySpan<double> aIn, double sampleRate, double tolerance)
    {
        var a = new Complex[aIn.Length];
        for (int i = 0; i < aIn.Length; i++)
        {
            a[i] = aIn[i];
        }

        var b = new Complex[bIn.Length];
        for (int i = 0; i < bIn.Length; i++)
        {
            b[i] = bIn[i];
        }

        Complex leading = a.Length == 0 ? Complex.Zero : a[0];
        if (leading != Complex.Zero)
        {
            for (int i = 0; i < a.Length; i++)
            {
                a[i] /= leading;
            }
        }

        if (b.Length > a.Length)
        {
            throw new ArgumentException("impinvar needs a numerator no longer than its denominator.", nameof(bIn));
        }

        Complex? feedThrough = null;
        if (b.Length == a.Length && b.Length > 0)
        {
            feedThrough = b[0] / a[0];
            var trimmed = new Complex[b.Length - 1];
            for (int i = 1; i < b.Length; i++)
            {
                trimmed[i - 1] = b[i] - (feedThrough.Value * a[i]);
            }

            b = trimmed;
        }

        Complex[] roots = Roots(a);
        int count = roots.Length;
        (int[] multiplicities, int[] order) = FilterCoefficients.MultiplePoles(roots, tolerance);

        var sorted = new Complex[count];
        for (int i = 0; i < count; i++)
        {
            sorted[i] = roots[order[i]];
        }

        // Each run of equal poles is replaced by its own average, so a pole pair that drifted apart
        // in the root finder comes back as one repeated pole rather than as two near ones.
        var averaged = new Complex[count];
        var runLength = new int[count];
        int start = 0;
        while (start < count)
        {
            int end = start;
            while (end + 1 < count && multiplicities[end + 1] != 1)
            {
                end++;
            }

            Complex sum = Complex.Zero;
            for (int i = start; i <= end; i++)
            {
                sum += sorted[i];
            }

            Complex mean = sum / (end - start + 1);
            for (int i = start; i <= end; i++)
            {
                averaged[i] = mean;
                runLength[i] = multiplicities[end];
            }

            start = end + 1;
        }

        var residues = new Complex[count];
        int kp = count - 1;
        while (kp >= 0)
        {
            Complex pole = averaged[kp];
            int multiplicity = runLength[kp];

            var others = new List<Complex>(count);
            for (int i = 0; i < count; i++)
            {
                if (i < kp + 1 - multiplicities[kp] || i > kp)
                {
                    others.Add(averaged[i]);
                }
            }

            Complex[] num = [.. b];
            Complex[] den = Poly(others);
            residues[kp] = Evaluate(num, pole) / Evaluate(den, pole);
            kp--;

            for (int k = 1; k <= multiplicity - 1; k++)
            {
                var scaled = new Complex[num.Length];
                for (int i = 0; i < num.Length; i++)
                {
                    scaled[i] = num[i] / k;
                }

                (num, den) = QuotientDerivative(scaled, den);
                residues[kp] = Evaluate(num, pole) / Evaluate(den, pole);
                kp--;
            }
        }

        for (int i = 0; i < count; i++)
        {
            residues[i] /= Factorial(multiplicities[i]);
        }

        var digitalPoles = new Complex[count];
        for (int i = 0; i < count; i++)
        {
            digitalPoles[i] = Complex.Exp(averaged[i] / sampleRate);
        }

        Complex[] az = Poly(digitalPoles);

        // The first n samples of the analogue impulse response, run back through the denominator.
        var h = new Complex[count];
        for (int t = 0; t < count; t++)
        {
            double time = (double)t / sampleRate;
            Complex sum = Complex.Zero;
            for (int i = 0; i < count; i++)
            {
                Complex power = multiplicities[i] - 1 == 0 ? Complex.One : Complex.Pow(time, multiplicities[i] - 1);
                sum += power * Complex.Exp(time * averaged[i]) * residues[i];
            }

            h[t] = sum;
        }

        Complex[] bz = FilterForward(az, h);
        if (feedThrough is not null)
        {
            var restored = new Complex[bz.Length + 1];
            for (int i = 0; i < az.Length; i++)
            {
                restored[i] = feedThrough.Value * az[i];
            }

            for (int i = 0; i < bz.Length; i++)
            {
                restored[i] += bz[i];
            }

            bz = restored;
        }

        double imaginary = 0;
        double total = 0;
        var num2 = new double[bz.Length];
        var den2 = new double[az.Length];
        for (int i = 0; i < bz.Length; i++)
        {
            num2[i] = bz[i].Real / sampleRate;
            imaginary += bz[i].Imaginary * bz[i].Imaginary;
            total += (bz[i].Real * bz[i].Real) + (bz[i].Imaginary * bz[i].Imaginary);
        }

        for (int i = 0; i < az.Length; i++)
        {
            den2[i] = az[i].Real * leading.Real;
            imaginary += az[i].Imaginary * az[i].Imaginary;
            total += (az[i].Real * az[i].Real) + (az[i].Imaginary * az[i].Imaginary);
        }

        bool robust = total == 0
            || System.Math.Sqrt(imaginary) / System.Math.Sqrt(total) <= 1000 * 2.220446049250313e-16;
        return (num2, den2, robust);
    }

    // --- The analogue frequency response -------------------------------------------------------

    /// <summary><c>freqs</c>: H(jω) over a given set of frequencies.</summary>
    public static Complex[] AnalogResponse(ReadOnlySpan<double> b, ReadOnlySpan<double> a, ReadOnlySpan<double> omega)
    {
        var h = new Complex[omega.Length];
        for (int i = 0; i < omega.Length; i++)
        {
            var s = new Complex(0, omega[i]);
            h[i] = EvaluateReal(b, s) / EvaluateReal(a, s);
        }

        return h;
    }

    /// <summary>
    /// The frequency grid <c>freqs</c> picks when it is given a count rather than a vector: a
    /// logarithmic sweep wide enough to hold every root, refined around the lightly damped ones.
    /// </summary>
    public static double[] FrequencyGrid(ReadOnlySpan<double> b, ReadOnlySpan<double> a, int points)
    {
        double[] wide = WideGrid(b, a, points);

        // The count MATLAB asks for is read back off the wide grid by interpolating in the log.
        var result = new double[points];
        if (points == 1)
        {
            result[0] = wide[0];
            return result;
        }

        for (int i = 0; i < points; i++)
        {
            double at = 1 + (i * (double)(wide.Length - 1) / (points - 1));
            int lower = System.Math.Min((int)System.Math.Floor(at), wide.Length - 1);
            double fraction = at - lower;
            double low = System.Math.Log10(wide[lower - 1]);
            double high = System.Math.Log10(wide[System.Math.Min(lower, wide.Length - 1)]);
            result[i] = System.Math.Pow(10, low + (fraction * (high - low)));
        }

        return result;
    }

    /// <summary>The wide grid <c>freqint</c> builds before the interpolation narrows it.</summary>
    private static double[] WideGrid(ReadOnlySpan<double> b, ReadOnlySpan<double> a, int points)
    {
        Complex[] poles = Roots(ToComplex(a));
        Complex[] zeros = Roots(ToComplex(b));
        if (poles.Length == 0)
        {
            poles = [new Complex(-1000, 0)];
        }

        var interesting = new List<Complex>();
        foreach (Complex p in poles)
        {
            if (p.Imaginary >= 0)
            {
                interesting.Add(p);
            }
        }

        foreach (Complex z in zeros)
        {
            if (Complex.Abs(z) < 1e5 && z.Imaginary >= 0)
            {
                interesting.Add(z);
            }
        }

        double high = double.NegativeInfinity;
        double low = double.PositiveInfinity;
        foreach (Complex e in interesting)
        {
            double integrator = Complex.Abs(e) < 1e-10 ? 1 : 0;
            high = System.Math.Max(high, (3 * System.Math.Abs(e.Real + integrator)) + (1.5 * e.Imaginary));
            low = System.Math.Min(low, System.Math.Abs(e.Real + integrator) + (2 * e.Imaginary));
        }

        double highFrequency = System.Math.Round(System.Math.Log10(high) + 0.5);
        double lowFrequency = System.Math.Round(System.Math.Log10(0.1 * low) - 0.5);

        int difference = poles.Length - zeros.Length;
        int oscillatory = 0;
        foreach (Complex z in zeros)
        {
            if (System.Math.Abs(z.Imaginary) < System.Math.Abs(z.Real))
            {
                oscillatory = 1;
                break;
            }
        }

        double[] w = LogSpace(lowFrequency, highFrequency, points + difference + (10 * oscillatory));

        var lightly = new List<Complex>();
        foreach (Complex e in interesting)
        {
            if (e.Imaginary > System.Math.Abs(e.Real))
            {
                lightly.Add(e);
            }
        }

        if (lightly.Count == 0)
        {
            return w;
        }

        // Each lightly damped root gets its own dense band, and the wide grid loses the points that
        // band replaces — which is what stops a resonance from falling between two samples.
        int extra = 2 + (int)(8 / System.Math.Ceiling(System.Math.Abs((difference + 2.220446049250313e-16) / 10.0)));
        lightly.Sort((x, y) => System.Math.Abs(y.Real).CompareTo(System.Math.Abs(x.Real)));

        var free = new List<double>(w);
        var dense = new List<double>();
        foreach (Complex e in lightly)
        {
            double r1 = System.Math.Max((0.8 * e.Imaginary) - (3 * System.Math.Abs(e.Real)), System.Math.Pow(10, lowFrequency));
            double r2 = (1.2 * e.Imaginary) + (4 * System.Math.Abs(e.Real));

            int inside = 0;
            foreach (double value in w)
            {
                if (value <= r2 && value >= r1)
                {
                    inside++;
                }
            }

            dense.RemoveAll(value => value <= r2 && value >= r1);
            free.RemoveAll(value => value <= r2 && value >= r1);
            dense.AddRange(LogSpace(System.Math.Log10(r1), System.Math.Log10(r2), inside + extra));
        }

        free.AddRange(dense);
        free.Sort();
        return [.. free];
    }

    // --- Small shared pieces -------------------------------------------------------------------

    /// <summary>The eigenvalues of a state matrix, which are a system's poles.</summary>
    internal static Complex[] Poles(double[,] a) =>
        a.GetLength(0) == 0 ? [] : Eigen.Factor(a).Values;

    /// <summary>Logarithmically spaced points between two powers of ten, as <c>logspace</c> gives them.</summary>
    internal static double[] LogSpace(double low, double high, int count)
    {
        if (count <= 0)
        {
            return [];
        }

        var result = new double[count];
        if (count == 1)
        {
            result[0] = System.Math.Pow(10, high);
            return result;
        }

        for (int i = 0; i < count; i++)
        {
            result[i] = System.Math.Pow(10, low + (i * (high - low) / (count - 1)));
        }

        return result;
    }

    private static Complex[] ToComplex(ReadOnlySpan<double> values)
    {
        var result = new Complex[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            result[i] = values[i];
        }

        return result;
    }

    /// <summary>The roots of a polynomial given highest power first, leading zeros dropped.</summary>
    internal static Complex[] Roots(ReadOnlySpan<Complex> coefficients)
    {
        int first = 0;
        while (first < coefficients.Length && coefficients[first] == Complex.Zero)
        {
            first++;
        }

        int last = coefficients.Length;
        while (last > first && coefficients[last - 1] == Complex.Zero)
        {
            last--;
        }

        int trailing = coefficients.Length - last;
        int degree = last - first - 1;
        if (degree < 1)
        {
            return new Complex[trailing];
        }

        var companion = new double[degree * degree];
        for (int i = 0; i < degree; i++)
        {
            companion[i * degree] = -(coefficients[first + i + 1] / coefficients[first]).Real;
        }

        for (int i = 0; i < degree - 1; i++)
        {
            companion[(i * degree) + i + 1] = 1;
        }

        Complex[] found = Eigen.Spectrum(companion, degree);
        var result = new Complex[degree + trailing];
        Array.Copy(found, result, degree);
        return result;
    }

    /// <summary>Π(z − rᵢ) over complex roots, highest power first.</summary>
    private static Complex[] Poly(IReadOnlyList<Complex> roots)
    {
        var coefficients = new Complex[roots.Count + 1];
        coefficients[0] = Complex.One;
        for (int r = 0; r < roots.Count; r++)
        {
            for (int i = r + 1; i >= 1; i--)
            {
                coefficients[i] -= roots[r] * coefficients[i - 1];
            }
        }

        return coefficients;
    }

    private static Complex Evaluate(ReadOnlySpan<Complex> coefficients, Complex x)
    {
        Complex sum = Complex.Zero;
        foreach (Complex c in coefficients)
        {
            sum = (sum * x) + c;
        }

        return sum;
    }

    private static Complex EvaluateReal(ReadOnlySpan<double> coefficients, Complex x)
    {
        Complex sum = Complex.Zero;
        foreach (double c in coefficients)
        {
            sum = (sum * x) + c;
        }

        return sum;
    }

    /// <summary>The derivative of a quotient, as <c>polyder</c> gives it with two outputs.</summary>
    private static (Complex[] Numerator, Complex[] Denominator) QuotientDerivative(Complex[] u, Complex[] v)
    {
        Complex[] du = Derivative(u);
        Complex[] dv = Derivative(v);
        Complex[] left = FilterCoefficients.Convolve(du, v);
        Complex[] right = FilterCoefficients.Convolve(u, dv);

        int length = System.Math.Max(left.Length, right.Length);
        var numerator = new Complex[length];
        for (int i = 0; i < length; i++)
        {
            Complex a = i >= length - left.Length ? left[i - (length - left.Length)] : Complex.Zero;
            Complex b = i >= length - right.Length ? right[i - (length - right.Length)] : Complex.Zero;
            numerator[i] = a - b;
        }

        return (Trim(numerator), FilterCoefficients.Convolve(v, v));
    }

    private static Complex[] Derivative(Complex[] p)
    {
        int degree = p.Length - 1;
        if (degree < 1)
        {
            return [Complex.Zero];
        }

        var d = new Complex[degree];
        for (int i = 0; i < degree; i++)
        {
            d[i] = p[i] * (degree - i);
        }

        return d;
    }

    private static Complex[] Trim(Complex[] p)
    {
        int first = 0;
        while (first < p.Length - 1 && p[first] == Complex.Zero)
        {
            first++;
        }

        return p[first..];
    }

    /// <summary>Γ(n) for a positive integer, which is the factorial of one less.</summary>
    private static double Factorial(int n)
    {
        double result = 1;
        for (int i = 2; i < n; i++)
        {
            result *= i;
        }

        return result;
    }

    /// <summary>The all-pole recurrence <c>filter(az, 1, h)</c>, which is a convolution.</summary>
    private static Complex[] FilterForward(Complex[] az, Complex[] x)
    {
        var y = new Complex[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            Complex sum = Complex.Zero;
            for (int k = 0; k < az.Length && k <= i; k++)
            {
                sum += az[k] * x[i - k];
            }

            y[i] = sum;
        }

        return y;
    }

    private static double[,] Copy(double[,] m)
    {
        var result = new double[m.GetLength(0), m.GetLength(1)];
        Array.Copy(m, result, m.Length);
        return result;
    }

    private static double[,] Scale(double[,] m, double factor)
    {
        int rows = m.GetLength(0);
        int columns = m.GetLength(1);
        var result = new double[rows, columns];
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < columns; j++)
            {
                result[i, j] = factor * m[i, j];
            }
        }

        return result;
    }

    private static double[,] Add(double[,] a, double[,] b)
    {
        int rows = a.GetLength(0);
        int columns = a.GetLength(1);
        var result = new double[rows, columns];
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < columns; j++)
            {
                result[i, j] = a[i, j] + b[i, j];
            }
        }

        return result;
    }

    private static double[,] Subtract(double[,] a, double[,] b)
    {
        int rows = a.GetLength(0);
        int columns = a.GetLength(1);
        var result = new double[rows, columns];
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < columns; j++)
            {
                result[i, j] = a[i, j] - b[i, j];
            }
        }

        return result;
    }

    /// <summary>X/A, solved as (Aᵀ \ Xᵀ)ᵀ.</summary>
    private static double[,] RightDivide(double[,] x, double[,] a) =>
        Linear.Transpose(Linear.Solve(Linear.Transpose(a), Linear.Transpose(x)));

    private static bool IsFinite(Complex value) =>
        double.IsFinite(value.Real) && double.IsFinite(value.Imaginary);
}
