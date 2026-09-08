using System;
using System.Numerics;
using JGraph.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Signal;

/// <summary>What <c>invfreqz</c> and <c>invfreqs</c> are asked to fit.</summary>
public sealed class ResponseFitRequest
{
    /// <summary>The numerator order.</summary>
    public int NumeratorOrder { get; set; }

    /// <summary>The denominator order.</summary>
    public int DenominatorOrder { get; set; }

    /// <summary>One weight per frequency, or null for equal weights.</summary>
    public double[]? Weights { get; set; }

    /// <summary>How many Gauss–Newton iterations to allow.</summary>
    public int MaxIterations { get; set; } = 30;

    /// <summary>The norm of the search direction below which the iteration stops.</summary>
    public double Tolerance { get; set; } = 0.01;

    /// <summary>Whether to refine the linear solution by Gauss–Newton at all.</summary>
    public bool Refine { get; set; }

    /// <summary>Whether the answer must have real coefficients.</summary>
    public bool Real { get; set; } = true;
}

/// <summary>
/// MATLAB's <c>invfreqz</c> and <c>invfreqs</c>: the rational filter whose frequency response best
/// matches a given one, in the discrete and the analogue variable.
/// </summary>
/// <remarks>
/// The first answer comes from an equation-error solve, which is linear because multiplying through
/// by the denominator removes it from the denominator. That answer is biased towards frequencies
/// where the denominator happens to be large, so a Gauss–Newton refinement on the true output error
/// follows when the caller asks for it, stabilising the denominator at every step.
/// </remarks>
public static class FrequencyResponseFit
{
    /// <summary>The fit in <c>z</c>, on the unit circle.</summary>
    public static (Complex[] Numerator, Complex[] Denominator) Discrete(
        Complex[] g, double[] w, ResponseFitRequest request)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(request);

        int na = request.DenominatorOrder;
        int nb = request.NumeratorOrder + 1;
        int points = w.Length;
        int nm = System.Math.Max(na, nb - 1);
        var basis = new Complex[nm + 1][];
        for (int k = 0; k <= nm; k++)
        {
            basis[k] = new Complex[points];
            for (int i = 0; i < points; i++)
            {
                basis[k][i] = Complex.Exp(new Complex(0, -k * w[i]));
            }
        }

        double[] weights = RootWeights(request.Weights, points);
        var design = new Complex[points, na + nb];
        var rhs = new Complex[points, 1];
        for (int i = 0; i < points; i++)
        {
            for (int j = 0; j < na; j++)
            {
                design[i, j] = basis[j + 1][i] * g[i] * weights[i];
            }

            for (int j = 0; j < nb; j++)
            {
                design[i, na + j] = -basis[j][i] * weights[i];
            }

            rhs[i, 0] = -g[i] * weights[i];
        }

        Complex[] theta = NormalSolve(design, rhs, request.Real);
        var a = new Complex[na + 1];
        var b = new Complex[nb];
        a[0] = 1;
        for (int i = 0; i < na; i++)
        {
            a[i + 1] = theta[i];
        }

        for (int i = 0; i < nb; i++)
        {
            b[i] = theta[na + i];
        }

        if (!request.Refine)
        {
            return (b, a);
        }

        return Refine(g, weights, basis, a, b, na, nb, request, discrete: true);
    }

    /// <summary>The fit in <c>s</c>, on the imaginary axis.</summary>
    public static (Complex[] Numerator, Complex[] Denominator) Analogue(
        Complex[] g, double[] w, ResponseFitRequest request)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(request);

        int na = request.DenominatorOrder;
        int nb = request.NumeratorOrder + 1;
        int points = w.Length;
        int nm = System.Math.Max(na + 1, nb);
        var powers = new Complex[nm][];
        for (int k = 0; k < nm; k++)
        {
            powers[k] = new Complex[points];
            for (int i = 0; i < points; i++)
            {
                powers[k][i] = k == 0 ? Complex.One : Complex.Pow(new Complex(0, w[i]), k);
            }
        }

        // The analogue fit writes its polynomials with the highest power first, so the basis rows
        // are read backwards: the coefficient of s^(na-j) multiplies row na-j.
        Complex[][] basis = powers;
        double[] weights = RootWeights(request.Weights, points);
        var design = new Complex[points, na + nb];
        var rhs = new Complex[points, 1];
        for (int i = 0; i < points; i++)
        {
            for (int j = 0; j < na; j++)
            {
                design[i, j] = basis[na - 1 - j][i] * g[i] * weights[i];
            }

            for (int j = 0; j < nb; j++)
            {
                design[i, na + j] = -basis[nb - 1 - j][i] * weights[i];
            }

            rhs[i, 0] = -g[i] * basis[na][i] * weights[i];
        }

        Complex[] theta = NormalSolve(design, rhs, request.Real);
        var a = new Complex[na + 1];
        var b = new Complex[nb];
        a[0] = 1;
        for (int i = 0; i < na; i++)
        {
            a[i + 1] = theta[i];
        }

        for (int i = 0; i < nb; i++)
        {
            b[i] = theta[na + i];
        }

        if (!request.Refine)
        {
            return (b, a);
        }

        return Refine(g, weights, basis, a, b, na, nb, request, discrete: false);
    }

    /// <summary>
    /// The Gauss–Newton refinement both fits share: a search direction from the output error, then
    /// a halving line search that stabilises the denominator at every trial.
    /// </summary>
    private static (Complex[] Numerator, Complex[] Denominator) Refine(
        Complex[] g,
        double[] weights,
        Complex[][] basis,
        Complex[] a,
        Complex[] b,
        int na,
        int nb,
        ResponseFitRequest request,
        bool discrete)
    {
        int points = g.Length;
        a = Stabilise(a, request.Real, discrete);
        Complex[] response = Response(a, b, basis, na, nb, points, discrete);
        double best = SquaredError(response, g, weights);
        Complex[] parameters = Pack(a, b, na, nb);
        Complex[] trial = parameters;

        double direction = (2 * request.Tolerance) + 1;
        int iterations = 0;
        bool stalled = false;
        while (direction > request.Tolerance && iterations < request.MaxIterations && !stalled)
        {
            parameters = trial;
            iterations++;
            var gradient = new Complex[points, na + nb];
            var error = new Complex[points, 1];
            for (int i = 0; i < points; i++)
            {
                Complex denominator = Evaluate(a, basis, na, i, discrete);
                for (int j = 0; j < na; j++)
                {
                    Complex row = discrete ? basis[j + 1][i] : basis[na - 1 - j][i];
                    gradient[i, j] = row * -response[i] / denominator * weights[i];
                }

                for (int j = 0; j < nb; j++)
                {
                    Complex row = discrete ? basis[j][i] : basis[nb - 1 - j][i];
                    gradient[i, na + j] = row / denominator * weights[i];
                }

                error[i, 0] = (response[i] - g[i]) * weights[i];
            }

            Complex[] step = NormalSolve(gradient, error, request.Real);
            double scale = 1;
            double trialError = best + 1;
            int attempts = 0;
            while (trialError > best && attempts < 20)
            {
                var next = new Complex[na + nb];
                for (int i = 0; i < next.Length; i++)
                {
                    next[i] = attempts == 19 ? parameters[i] : parameters[i] - (scale * step[i]);
                }

                var candidate = new Complex[na + 1];
                candidate[0] = 1;
                for (int i = 0; i < na; i++)
                {
                    candidate[i + 1] = next[i];
                }

                candidate = Stabilise(candidate, request.Real, discrete);
                for (int i = 0; i < na; i++)
                {
                    next[i] = candidate[i + 1];
                }

                var numerator = new Complex[nb];
                for (int i = 0; i < nb; i++)
                {
                    numerator[i] = next[na + i];
                }

                a = candidate;
                b = numerator;
                response = Response(a, b, basis, na, nb, points, discrete);
                trialError = SquaredError(response, g, weights);
                trial = Pack(a, b, na, nb);
                scale /= 2;
                attempts++;
                if (attempts == 10)
                {
                    // Ten halvings without improvement: fall back to a scaled gradient step,
                    // which is what MATLAB does rather than give up on the iteration.
                    double norm = SpectralNorm(gradient, na + nb);
                    Complex[] plain = NormalRightHandSide(gradient, error, request.Real);
                    for (int i = 0; i < step.Length; i++)
                    {
                        step[i] = plain[i] / norm * (na + nb);
                    }

                    scale = 1;
                }

                if (attempts == 20)
                {
                    stalled = true;
                }
            }

            best = trialError;
            direction = Norm(step);
        }

        return (b, a);
    }

    /// <summary>The response of the current fit at every frequency.</summary>
    private static Complex[] Response(
        Complex[] a, Complex[] b, Complex[][] basis, int na, int nb, int points, bool discrete)
    {
        var response = new Complex[points];
        for (int i = 0; i < points; i++)
        {
            Complex numerator = 0;
            for (int j = 0; j < nb; j++)
            {
                numerator += b[j] * (discrete ? basis[j][i] : basis[nb - 1 - j][i]);
            }

            response[i] = numerator / Evaluate(a, basis, na, i, discrete);
        }

        return response;
    }

    private static Complex Evaluate(Complex[] a, Complex[][] basis, int na, int i, bool discrete)
    {
        Complex sum = 0;
        for (int j = 0; j <= na; j++)
        {
            sum += a[j] * (discrete ? basis[j][i] : basis[na - j][i]);
        }

        return sum;
    }

    private static double SquaredError(Complex[] response, Complex[] g, double[] weights)
    {
        double sum = 0;
        for (int i = 0; i < g.Length; i++)
        {
            Complex e = (response[i] - g[i]) * weights[i];
            sum += (e.Real * e.Real) + (e.Imaginary * e.Imaginary);
        }

        return sum;
    }

    private static Complex[] Pack(Complex[] a, Complex[] b, int na, int nb)
    {
        var packed = new Complex[na + nb];
        for (int i = 0; i < na; i++)
        {
            packed[i] = a[i + 1];
        }

        for (int i = 0; i < nb; i++)
        {
            packed[na + i] = b[i];
        }

        return packed;
    }

    /// <summary>
    /// <c>polystab</c> for a discrete filter, which reflects every root outside the unit circle
    /// back inside it, and <c>apolystab</c> for an analogue one, which reflects every root in the
    /// right half plane into the left.
    /// </summary>
    private static Complex[] Stabilise(Complex[] a, bool real, bool discrete)
    {
        Complex[] roots = Polynomials.Roots(a);
        for (int i = 0; i < roots.Length; i++)
        {
            if (discrete)
            {
                if (roots[i] != Complex.Zero && roots[i].Magnitude > 1)
                {
                    roots[i] = 1 / Complex.Conjugate(roots[i]);
                }
            }
            else if (roots[i].Real > 0)
            {
                roots[i] = -roots[i];
            }
        }

        Complex[] rebuilt = Polynomials.FromRoots(roots);
        Complex lead = discrete ? Leading(a) : Complex.One;
        var stabilised = new Complex[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            Complex value = i < rebuilt.Length ? rebuilt[i] * lead : Complex.Zero;
            stabilised[i] = real ? value.Real : value;
        }

        return stabilised;
    }

    private static Complex Leading(Complex[] a)
    {
        foreach (Complex value in a)
        {
            if (value != Complex.Zero)
            {
                return value;
            }
        }

        return Complex.One;
    }

    /// <summary>The normal-equation solution MATLAB reaches by <c>(D'D)\(D'v)</c>.</summary>
    private static Complex[] NormalSolve(Complex[,] design, Complex[,] rhs, bool real)
    {
        int points = design.GetLength(0);
        int n = design.GetLength(1);
        var gram = new Complex[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                Complex sum = 0;
                for (int i = 0; i < points; i++)
                {
                    sum += Complex.Conjugate(design[i, r]) * design[i, c];
                }

                gram[r, c] = real ? sum.Real : sum;
            }
        }

        Complex[] vector = NormalRightHandSide(design, rhs, real);
        var column = new Complex[n, 1];
        for (int i = 0; i < n; i++)
        {
            column[i, 0] = vector[i];
        }

        Complex[,] solution = HouseholderQr.BasicSolution(gram, column, -1, out _);
        var answer = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            answer[i] = solution[i, 0];
        }

        return answer;
    }

    private static Complex[] NormalRightHandSide(Complex[,] design, Complex[,] rhs, bool real)
    {
        int points = design.GetLength(0);
        int n = design.GetLength(1);
        var vector = new Complex[n];
        for (int r = 0; r < n; r++)
        {
            Complex sum = 0;
            for (int i = 0; i < points; i++)
            {
                sum += Complex.Conjugate(design[i, r]) * rhs[i, 0];
            }

            vector[r] = real ? sum.Real : sum;
        }

        return vector;
    }

    /// <summary>The largest singular value of the Gram matrix, by power iteration.</summary>
    private static double SpectralNorm(Complex[,] design, int n)
    {
        var gram = new Complex[n, n];
        int points = design.GetLength(0);
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                Complex sum = 0;
                for (int i = 0; i < points; i++)
                {
                    sum += Complex.Conjugate(design[i, r]) * design[i, c];
                }

                gram[r, c] = sum;
            }
        }

        var v = new Complex[n];
        Array.Fill(v, Complex.One / System.Math.Sqrt(n));
        double norm = 0;
        for (int step = 0; step < 200; step++)
        {
            var next = new Complex[n];
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    next[r] += gram[r, c] * v[c];
                }
            }

            norm = Norm(next);
            if (norm == 0)
            {
                return 0;
            }

            for (int i = 0; i < n; i++)
            {
                v[i] = next[i] / norm;
            }
        }

        return norm;
    }

    private static double Norm(Complex[] v)
    {
        double sum = 0;
        foreach (Complex value in v)
        {
            sum += (value.Real * value.Real) + (value.Imaginary * value.Imaginary);
        }

        return System.Math.Sqrt(sum);
    }

    private static double[] RootWeights(double[]? weights, int points)
    {
        var root = new double[points];
        for (int i = 0; i < points; i++)
        {
            root[i] = System.Math.Sqrt(weights is null ? 1 : weights[i]);
        }

        return root;
    }
}
