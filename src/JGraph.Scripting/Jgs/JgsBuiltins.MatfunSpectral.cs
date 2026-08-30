using System;
using System.Collections.Generic;
using System.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The three spectral names of M107: the polynomial eigenproblem (<c>polyeig</c>), a general
/// function of a matrix (<c>funm</c>), and the generalized singular value decomposition
/// (<c>gsvd</c>).
/// </summary>
/// <remarks>
/// <para>
/// All three reduce to something already here rather than iterating on their own.
/// <c>polyeig</c> lays the polynomial out as one large pencil and hands it to <c>eig</c>;
/// <c>funm</c> triangularizes and then does arithmetic; <c>gsvd</c> stacks the pair, factors once,
/// and describes the two halves against each other. That is the shape of all three published
/// algorithms and it is also what keeps their answers consistent with the verbs they are built on —
/// <c>polyeig</c> of a linear polynomial is <c>eig</c> of the pencil, to the last bit, because it
/// literally is.
/// </para>
/// <para>
/// <c>funm</c> is the one that reaches back into the script. A function of a matrix with repeated
/// eigenvalues needs the function's derivatives, not just its values, so the handle is called as
/// <c>f(x, k)</c> for the k-th derivative — and the six functions MATLAB knows by name are answered
/// from their own derivative rules rather than by asking.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the three spectral names into <paramref name="env"/>.</summary>
    internal static void RegisterMatfunSpectralBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) => both(args, 1, line, col)[0])
            {
                MultiOutput = both,
            }));

        DefineBoth("polyeig", PolynomialEigen);
        DefineBoth("funm", (args, wanted, line, col) => MatrixFunctionOf(args, wanted, host, line, col));
        DefineBoth("gsvd", GeneralizedSingularValues);
    }

    // --- polyeig ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>e = polyeig(A0, A1, ..., Ap)</c> and its <c>[X, e]</c> and <c>[X, e, s]</c> forms: the
    /// values of λ at which the matrix polynomial <c>A0 + λA1 + ... + λᵖAp</c> becomes singular.
    /// </summary>
    /// <remarks>
    /// The polynomial is linearized: a companion pencil of p times the order, whose eigenvalues are
    /// exactly the polynomial's. Each eigenvector of that pencil holds p copies of the answer, one
    /// for each power, and they should agree — so the one whose residual against the original
    /// polynomial is smallest is the one reported, which is both MATLAB's rule and the only way to
    /// choose when rounding has made them differ.
    /// </remarks>
    private static JgsValue[] PolynomialEigen(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        if (wanted > 2 && args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:polyeig:tooFewInputs",
                "Must provide at least two matrices.");
        }

        var coefficients = new Complex[args.Count][,];
        for (int i = 0; i < args.Count; i++)
        {
            coefficients[i] = MatBlock("polyeig", args[i], line, col);
            if (IsComplexBlock(coefficients[i]))
            {
                throw new JgsRuntimeException(line, col,
                    "polyeig: the pencil behind a polynomial eigenproblem is real here; "
                    + "a complex coefficient matrix is not supported.");
            }
        }

        int n = Math.Max(coefficients[0].GetLength(0), coefficients[0].GetLength(1));
        int degree = args.Count - 1;
        int size = degree == 0 ? n : n * degree;

        var a = new Complex[size, size];
        var b = new Complex[size, size];
        for (int i = 0; i < size; i++)
        {
            a[i, i] = Complex.One;
        }

        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                a[r, c] = coefficients[0][r, c];
            }
        }

        if (degree == 0)
        {
            for (int i = 0; i < n; i++)
            {
                b[i, i] = Complex.One;
            }

            degree = 1;
        }
        else
        {
            // The pencil's lower blocks are one shifted identity, which is what makes an eigenvector
            // hold the successive powers of λ times the same vector.
            for (int k = 0; k + n < size; k++)
            {
                b[n + k, k] = Complex.One;
            }

            for (int k = 1; k <= degree; k++)
            {
                int start = (k - 1) * n;
                for (int c = 0; c < n; c++)
                {
                    for (int r = 0; r < n; r++)
                    {
                        b[r, start + c] = -coefficients[k][r, c];
                    }
                }
            }
        }

        double[] flatA = FlatReal(a, size);
        double[] flatB = FlatReal(b, size);
        if (wanted <= 1)
        {
            Complex[] only = Eigen.PencilSpectrum(flatA, flatB, size);
            return [MatValue(AsColumn(only))];
        }

        (Complex[] values, Complex[,] vectors) = Eigen.PencilFactor(flatA, flatB, size);
        var chosen = new Complex[n, size];
        if (args.Count > 2)
        {
            for (int j = 0; j < size; j++)
            {
                var candidates = new Complex[n, degree];
                for (int t = 0; t < size; t++)
                {
                    candidates[t % n, t / n] = vectors[t, j];
                }

                Complex[,] residual = PolynomialAt(coefficients, values[j], n);
                Complex[,] applied = NormEstimators.Product(residual, candidates, conjugateTranspose: false);
                int best = 0;
                double smallest = double.PositiveInfinity;
                for (int c = 0; c < degree; c++)
                {
                    double top = 0.0;
                    double bottom = 0.0;
                    for (int r = 0; r < n; r++)
                    {
                        top += applied[r, c].Magnitude;
                        bottom += candidates[r, c].Magnitude;
                    }

                    double ratio = top / bottom;
                    if (ratio < smallest)
                    {
                        smallest = ratio;
                        best = c;
                    }
                }

                double length = 0.0;
                for (int r = 0; r < n; r++)
                {
                    length += (candidates[r, best].Real * candidates[r, best].Real)
                        + (candidates[r, best].Imaginary * candidates[r, best].Imaginary);
                }

                length = Math.Sqrt(length);
                for (int r = 0; r < n; r++)
                {
                    chosen[r, j] = new Complex(
                        candidates[r, best].Real / length, candidates[r, best].Imaginary / length);
                }
            }
        }
        else
        {
            for (int j = 0; j < size; j++)
            {
                for (int r = 0; r < n; r++)
                {
                    chosen[r, j] = vectors[r, j];
                }
            }
        }

        JgsValue column = MatValue(AsColumn(values));
        if (wanted <= 2)
        {
            return [MatValue(chosen), column];
        }

        return [MatValue(chosen), column,
            MatValue(AsColumn(EigenvalueSensitivities(coefficients, values, chosen, n, degree, line, col)))];
    }

    /// <summary>The matrix polynomial evaluated at one eigenvalue, or its leading term at infinity.</summary>
    private static Complex[,] PolynomialAt(Complex[][,] coefficients, Complex at, int n)
    {
        int degree = coefficients.Length - 1;
        var r = new Complex[n, n];
        for (int row = 0; row < n; row++)
        {
            for (int c = 0; c < n; c++)
            {
                r[row, c] = coefficients[degree][row, c];
            }
        }

        if (double.IsInfinity(at.Real) || double.IsInfinity(at.Imaginary))
        {
            return r;
        }

        for (int k = degree; k >= 1; k--)
        {
            for (int row = 0; row < n; row++)
            {
                for (int c = 0; c < n; c++)
                {
                    r[row, c] = coefficients[k - 1][row, c] + (at * r[row, c]);
                }
            }
        }

        return r;
    }

    /// <summary>
    /// How sensitive each eigenvalue of the polynomial is: the norm of the coefficients weighted by
    /// the eigenvalue's powers, over what the derivative of the polynomial does between the left and
    /// right eigenvectors.
    /// </summary>
    private static Complex[] EigenvalueSensitivities(
        Complex[][,] coefficients, Complex[] values, Complex[,] right, int n, int degree, int line, int col)
    {
        int size = n * degree;
        Complex[,] leading = coefficients[degree];
        Complex[,] trailing = coefficients[0];
        double conditionLeading = ReciprocalCondition(leading, n);
        double conditionTrailing = ReciprocalCondition(trailing, n);
        if (Math.Max(conditionLeading, conditionTrailing) <= DoubleSpacing)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:polyeig:nonSingularCoeffMatrix",
                "Either the leading or the trailing coefficient matrix must be nonsingular.");
        }

        bool byLeading = conditionLeading >= conditionTrailing;
        Complex[,] divisor = byLeading ? leading : trailing;
        var scaled = new Complex[size];
        for (int j = 0; j < size; j++)
        {
            scaled[j] = byLeading ? values[j] : Complex.One / values[j];
        }

        // The left eigenvectors of the linearization, recovered by solving rather than by asking the
        // eigensolver for them a second time — the same rows, and one factorization instead of two.
        var y = new Complex[size, size];
        for (int j = 0; j < size; j++)
        {
            for (int r = 0; r < n; r++)
            {
                y[r, j] = right[r, j];
            }

            Complex power = Complex.One;
            for (int i = 1; i < degree; i++)
            {
                power *= scaled[j];
                for (int r = 0; r < n; r++)
                {
                    y[(i * n) + r, j] = right[r, j] * power;
                }
            }
        }

        var target = new Complex[size, n];
        for (int i = 0; i < n; i++)
        {
            target[size - n + i, i] = Complex.One;
        }

        Complex[,] left = HouseholderQr.MinimumNormSolution(y, target, 0.0, out _);
        Complex[,] over = RightDivide(left, divisor, n);
        for (int i = 0; i < size; i++)
        {
            double length = 0.0;
            for (int c = 0; c < n; c++)
            {
                length += (over[i, c].Real * over[i, c].Real) + (over[i, c].Imaginary * over[i, c].Imaginary);
            }

            length = Math.Sqrt(length);
            if (length == 0)
            {
                continue;
            }

            for (int c = 0; c < n; c++)
            {
                over[i, c] = new Complex(over[i, c].Real / length, over[i, c].Imaginary / length);
            }
        }

        var norms = new double[degree + 1];
        for (int i = 0; i <= degree; i++)
        {
            double sum = 0.0;
            foreach (Complex value in coefficients[i])
            {
                sum += (value.Real * value.Real) + (value.Imaginary * value.Imaginary);
            }

            norms[i] = Math.Sqrt(sum);
        }

        var s = new Complex[size];
        for (int j = 0; j < size; j++)
        {
            bool infinite = double.IsInfinity(values[j].Real) || double.IsInfinity(values[j].Imaginary);
            Complex alpha = infinite ? Complex.One : values[j];
            Complex beta = infinite ? Complex.Zero : Complex.One;

            var ab = new Complex[degree];
            for (int k = 0; k < degree; k++)
            {
                ab[k] = Power(alpha, k) * Power(beta, degree - 1 - k);
            }

            var da = new Complex[n, n];
            var db = new Complex[n, n];
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    da[r, c] = ab[0] * coefficients[1][r, c];
                    db[r, c] = degree * ab[0] * coefficients[0][r, c];
                }
            }

            for (int k = 2; k <= degree; k++)
            {
                for (int r = 0; r < n; r++)
                {
                    for (int c = 0; c < n; c++)
                    {
                        da[r, c] += k * ab[k - 1] * coefficients[k][r, c];
                        db[r, c] += (degree - k + 1) * ab[k - 1] * coefficients[k - 1][r, c];
                    }
                }
            }

            double weighted = 0.0;
            for (int i = 0; i <= degree; i++)
            {
                Complex term = Power(alpha, i) * Power(beta, degree - i) * norms[i];
                weighted += (term.Real * term.Real) + (term.Imaginary * term.Imaginary);
            }

            Complex middle = Complex.Zero;
            for (int r = 0; r < n; r++)
            {
                Complex row = Complex.Zero;
                for (int c = 0; c < n; c++)
                {
                    row += over[j, c] * ((Complex.Conjugate(beta) * da[c, r]) - (Complex.Conjugate(alpha) * db[c, r]));
                }

                middle += row * right[r, j];
            }

            s[j] = new Complex(Math.Sqrt(weighted) / middle.Magnitude, 0.0);
        }

        return s;
    }

    /// <summary>A power that answers one at the zeroth, including for an argument of nought.</summary>
    private static Complex Power(Complex value, int exponent)
    {
        Complex answer = Complex.One;
        for (int i = 0; i < exponent; i++)
        {
            answer *= value;
        }

        return answer;
    }

    /// <summary>The reciprocal condition number in the one-norm, estimated exactly for small orders.</summary>
    private static double ReciprocalCondition(Complex[,] a, int n)
    {
        if (n == 0)
        {
            return 1.0;
        }

        var identity = new Complex[n, n];
        for (int i = 0; i < n; i++)
        {
            identity[i, i] = Complex.One;
        }

        Complex[,] inverse = HouseholderQr.MinimumNormSolution(a, identity, -1.0, out int rank);
        if (rank < n)
        {
            return 0.0;
        }

        return 1.0 / (NormEstimators.OneNormOf(a) * NormEstimators.OneNormOf(inverse));
    }

    /// <summary>The right division <c>A / B</c> for a square B.</summary>
    private static Complex[,] RightDivide(Complex[,] a, Complex[,] b, int n)
    {
        int rows = a.GetLength(0);
        var transposed = new Complex[n, rows];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < n; c++)
            {
                transposed[c, r] = a[r, c];
            }
        }

        var divisor = new Complex[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                divisor[r, c] = b[c, r];
            }
        }

        Complex[,] solved = HouseholderQr.MinimumNormSolution(divisor, transposed, -1.0, out _);
        var answer = new Complex[rows, n];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < n; c++)
            {
                answer[r, c] = solved[c, r];
            }
        }

        return answer;
    }

    /// <summary>A block's real parts, flat and column-major.</summary>
    private static double[] FlatReal(Complex[,] a, int n)
    {
        var flat = new double[n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                flat[(c * n) + r] = a[r, c].Real;
            }
        }

        return flat;
    }

    // --- funm ------------------------------------------------------------------------------------

    /// <summary>
    /// <c>F = funm(A, fun)</c>, with options and trailing arguments, and the <c>exitflag</c> and
    /// <c>output</c> forms.
    /// </summary>
    private static JgsValue[] MatrixFunctionOf(
        IReadOnlyList<JgsValue> args, int wanted, JGraphScriptGlobals host, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        Complex[,] a = MatBlock("funm", args[0], line, col);
        int n = a.GetLength(0);
        if (n != a.GetLength(1))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:funm:InputDim",
                "First input must be a single or double square matrix.");
        }

        string known = KnownFunction(args[1]);
        var options = new MatrixFunction.Options();
        int[]? given = null;
        int display = 0;
        if (args.Count > 2 && args[2].Type == JgsType.Struct)
        {
            Dictionary<string, JgsValue> fields = args[2].AsStruct;
            if (fields.TryGetValue("Display", out JgsValue? shown) && shown is not null && IsTextScalar(shown))
            {
                string word = shown.AsString;
                display = word.Length >= 2 && "on".StartsWith(word, StringComparison.OrdinalIgnoreCase) ? 1
                    : word.Length >= 1 && "verbose".StartsWith(word, StringComparison.OrdinalIgnoreCase) ? 2
                    : 0;
            }

            double block = ReadOption(fields, "TolBlk", 0.1, "NegTolBlk", "TolBlk must be positive.", line, col);
            double series = ReadOption(fields, "TolTay", DoubleSpacing, "NegTolTay",
                "TolTay must be positive.", line, col);
            double terms = ReadOption(fields, "MaxTerms", 250, "NegMaxTerms",
                "MaxTerms must be positive.", line, col);
            double roots = ReadOption(fields, "MaxSqrt", 100, "NegMaxSqrt",
                "MaxSqrt must be positive.", line, col);
            if (fields.TryGetValue("Ord", out JgsValue? ord) && ord is not null && ord.Type != JgsType.Null)
            {
                double[] order = FlattenColumnMajor("funm", ord, line, col);
                if (order.Length != n)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:funm:WrongDimOrd",
                        "Incorrect dimension for Ord.");
                }

                given = Array.ConvertAll(order, static value => (int)value);
            }

            options = new MatrixFunction.Options(block, series, (int)terms, (int)roots, given);
        }

        var extra = new List<JgsValue>();
        for (int i = 3; i < args.Count; i++)
        {
            extra.Add(args[i]);
        }

        bool real = !IsComplexBlock(a);
        (Complex[,] u, Complex[,] t) = FunmSchur(a, n, real);

        var eigenvalues = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            eigenvalues[i] = t[i, i];
        }

        if (known == "log")
        {
            bool anyZero = false;
            bool anyNonPositive = false;
            foreach (Complex value in eigenvalues)
            {
                anyZero |= value == Complex.Zero;
                anyNonPositive |= value.Imaginary == 0 && value.Real <= 0;
            }

            if (anyZero && !IsUpperTriangular(a, n))
            {
                host.WriteErr("Warning: At least one zero eigenvalue is detected in SCHUR(A).");
                host.WriteErr("         Matrix A may be singular or badly scaled.");
            }

            if (anyNonPositive)
            {
                host.WriteErr("Warning: Principal matrix logarithm is not defined for A with nonpositive "
                    + "real eigenvalues. A non-principal matrix logarithm is returned.");
            }
        }

        MatrixFunction.Derivative derivative = Derivatives(known, args[1], extra, line, col);
        MatrixFunction.Kind kind = known switch
        {
            "exp" => MatrixFunction.Kind.Exponential,
            "log" => MatrixFunction.Kind.Logarithm,
            _ => MatrixFunction.Kind.General,
        };

        MatrixFunction.Result result = MatrixFunction.Evaluate(u, t, derivative, kind, options);
        if (display > 0)
        {
            ReportBlocks(host, result, display);
        }

        Complex[,] f = result.F;
        if (real && IsComplexBlock(f))
        {
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    f[r, c] = new Complex(f[r, c].Real, 0.0);
                }
            }
        }

        int flag = 0;
        if (result.Stalled)
        {
            flag = 1;
            host.WriteErr("Warning: Taylor series failed to converge.");
            host.WriteErr("         Possible that function does not have an infinite radius of convergence.");
            host.WriteErr("         Try decreasing options.TolBlk or increasing options.TolTay or options.MaxTerms.");
        }

        if (result.TooManyRoots)
        {
            flag = 1;
            host.WriteErr("Warning: Maximum number of matrix square roots exceeded.");
            host.WriteErr("         Try decreasing options.TolBlk or increasing options.MaxSqrt.");
        }

        if (wanted <= 1)
        {
            return [MatValue(f)];
        }

        if (wanted <= 2)
        {
            return [MatValue(f), JgsValue.Number(flag)];
        }

        var cells = new JgsValue[result.Blocks.Length];
        var counts = new double[result.Blocks.Length];
        int highest = 0;
        foreach (int label in result.Order)
        {
            highest = Math.Max(highest, label);
        }

        var ordering = new double[result.Order.Length];
        for (int i = 0; i < result.Order.Length; i++)
        {
            ordering[i] = highest - result.Order[i] + 1;
        }

        for (int i = 0; i < result.Blocks.Length; i++)
        {
            var positions = new double[result.Blocks[i].Length];
            for (int k = 0; k < positions.Length; k++)
            {
                positions[k] = result.Blocks[i][k] + 1;
            }

            cells[i] = JgsMatrix.FromColumnMajorDims(positions, [1, positions.Length]);
            counts[i] = result.Terms[i];
        }

        JgsValue report = Structure(
            ("terms", JgsMatrix.FromColumnMajorDims(counts, [1, counts.Length])),
            ("ind", JgsValue.Cell(cells)),
            ("ord", JgsMatrix.FromColumnMajorDims(ordering, [1, ordering.Length])),
            ("T", MatValue(result.T)));
        return [MatValue(f), JgsValue.Number(flag), report];
    }

    /// <summary>
    /// The block table <c>Display</c> asks for: which diagonal positions each block covered and how
    /// many series terms — or, for the logarithm, how many square roots — it took.
    /// </summary>
    private static void ReportBlocks(JGraphScriptGlobals host, MatrixFunction.Result result, int display)
    {
        if (display >= 2)
        {
            foreach (int[] block in result.Blocks)
            {
                if (block.Length > 1)
                {
                    host.WriteErr($"Evaluating function of block ({block[0] + 1}:{block[^1] + 1})");
                }
            }
        }

        host.WriteErr("  Block   Number of Taylor series terms");
        host.WriteErr("          (or matrix square roots in case of log):");
        host.WriteErr("  ----------------------------------------");
        for (int i = 0; i < result.Blocks.Length; i++)
        {
            host.WriteErr($" ({result.Blocks[i][0] + 1}:{result.Blocks[i][^1] + 1})      {result.Terms[i]}");
        }
    }

    /// <summary>
    /// The triangular form <c>funm</c> works over: the matrix itself when it is already triangular,
    /// a complex Schur form otherwise.
    /// </summary>
    /// <remarks>
    /// For a real matrix the complex Schur form is not computed directly. The real one is, and then
    /// <c>rsf2csf</c> — which this milestone also brought — turns it into the complex one. That is
    /// both cheaper and what MATLAB does, and it means the two names share their answer rather than
    /// each having its own triangularization.
    /// </remarks>
    private static (Complex[,] U, Complex[,] T) FunmSchur(Complex[,] a, int n, bool real)
    {
        if (IsUpperTriangular(a, n))
        {
            var identity = new Complex[n, n];
            for (int i = 0; i < n; i++)
            {
                identity[i, i] = Complex.One;
            }

            return (identity, (Complex[,])a.Clone());
        }

        if (!real)
        {
            return ComplexEigen.Schur(a);
        }

        var block = new double[n, n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                block[r, c] = a[r, c].Real;
            }
        }

        Schur schur = Schur.Factor(block);
        return SchurConversion.RealToComplex(Widen(schur.U), Widen(schur.T));
    }

    /// <summary>The six functions MATLAB knows the derivatives of by name, or the empty string.</summary>
    private static string KnownFunction(JgsValue fun)
    {
        string name = fun.Type == JgsType.Function ? fun.AsCallable.Name
            : IsTextScalar(fun) ? fun.AsString
            : string.Empty;
        return name is "cos" or "sin" or "cosh" or "sinh" or "exp" or "log" ? name : string.Empty;
    }

    /// <summary>One field of the options struct, refused by name when it is not positive.</summary>
    private static double ReadOption(
        Dictionary<string, JgsValue> fields, string name, double fallback,
        string key, string message, int line, int col)
    {
        if (!fields.TryGetValue(name, out JgsValue? value) || value is null || value.Type == JgsType.Null)
        {
            return fallback;
        }

        double found = value.AsNumber;
        if (found <= 0)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:funm:{key}", message);
        }

        return found;
    }

    /// <summary>
    /// The function and its derivatives, either from the rule for one of the six known names or by
    /// calling the script's own handle with the derivative order as its second argument.
    /// </summary>
    private static MatrixFunction.Derivative Derivatives(
        string known, JgsValue fun, IReadOnlyList<JgsValue> extra, int line, int col)
    {
        if (known.Length > 0)
        {
            return (x, k) => Array.ConvertAll(x, value => KnownDerivative(known, value, k));
        }

        return (x, k) =>
        {
            var call = new List<JgsValue> { MatValue(AsColumn(x)), JgsValue.Number(k) };
            call.AddRange(extra);
            JgsValue answer = fun.AsCallable.Call(call, line, col);
            Complex[] flat = ComplexElements("funm", answer, line, col);
            if (flat.Length == x.Length)
            {
                return flat;
            }

            // A handle that ignores the shape it was handed and answers one value stands for that
            // value everywhere, which is what a constant derivative looks like.
            var spread = new Complex[x.Length];
            Array.Fill(spread, flat.Length > 0 ? flat[0] : Complex.Zero);
            return spread;
        };
    }

    /// <summary>The k-th derivative of one of the six named functions, at one point.</summary>
    private static Complex KnownDerivative(string name, Complex x, int k) => name switch
    {
        "exp" => Complex.Exp(x),
        "log" => Complex.Log(x),
        "cos" => (k % 2 != 0 ? Complex.Sin(x) : Complex.Cos(x))
            * (Math.Ceiling(k / 2.0) % 2 != 0 ? -1 : 1),
        "sin" => (k % 2 != 0 ? Complex.Cos(x) : Complex.Sin(x))
            * (Math.Ceiling((k - 1) / 2.0) % 2 != 0 ? -1 : 1),
        "cosh" => k % 2 != 0 ? Sinh(x) : Cosh(x),
        _ => k % 2 != 0 ? Cosh(x) : Sinh(x),
    };

    private static Complex Sinh(Complex z) => (Complex.Exp(z) - Complex.Exp(-z)) / 2.0;

    private static Complex Cosh(Complex z) => (Complex.Exp(z) + Complex.Exp(-z)) / 2.0;

    /// <summary>Whether everything below the diagonal is nought.</summary>
    private static bool IsUpperTriangular(Complex[,] a, int n)
    {
        for (int c = 0; c < n; c++)
        {
            for (int r = c + 1; r < n; r++)
            {
                if (a[r, c] != Complex.Zero)
                {
                    return false;
                }
            }
        }

        return true;
    }

    // --- gsvd ------------------------------------------------------------------------------------

    /// <summary>
    /// <c>[U, V, X, C, S] = gsvd(A, B)</c>, its economy form, and <c>sigma = gsvd(A, B)</c>.
    /// </summary>
    private static JgsValue[] GeneralizedSingularValues(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("gsvd", args, 2, 3, line, col);
        Complex[,] a = MatBlock("gsvd", args[0], line, col);
        Complex[,] b = MatBlock("gsvd", args[1], line, col);
        if (a.GetLength(1) != b.GetLength(1))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:gsvd:MatrixColMismatch",
                "Matrices must have the same number of columns.");
        }

        bool economy = false;
        if (args.Count > 2)
        {
            bool zero = args[2].Type is JgsType.Number or JgsType.Bool && args[2].AsNumber == 0;
            bool word = IsTextScalar(args[2]) && args[2].AsString.Length > 0
                && "econ".StartsWith(args[2].AsString, StringComparison.OrdinalIgnoreCase);
            if (!zero && !word)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:gsvd:InvalidFlag",
                    "Third input must be 'econ' or 0.");
            }

            economy = true;
        }

        if (wanted < 2)
        {
            return [MatValue(AsColumn(Array.ConvertAll(
                GeneralizedSvd.Values(a, b), static v => new Complex(v, 0.0))))];
        }

        GeneralizedSvd.Factors factors = GeneralizedSvd.Factor(a, b, economy);
        return
        [
            MatValue(factors.U), MatValue(factors.V), MatValue(factors.X),
            MatValue(factors.C), MatValue(factors.S),
        ];
    }
}
