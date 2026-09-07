using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Numerics;

/// <summary>What kind of mass matrix a problem carries — MATLAB's <c>Mtype</c>.</summary>
internal enum OdeMassType
{
    /// <summary>None: <c>y' = f(t, y)</c>.</summary>
    None = 0,

    /// <summary>A constant matrix.</summary>
    Constant = 1,

    /// <summary>A function of the time alone (<c>MStateDependence</c> <c>'none'</c>).</summary>
    TimeDependent = 2,

    /// <summary>A function of time and state, weakly (<c>'weak'</c>).</summary>
    StateDependentWeak = 3,

    /// <summary>A function of time and state, strongly (<c>'strong'</c>): <c>d(M·v)/dy</c> joins the iteration matrix.</summary>
    StateDependentStrong = 4,
}

/// <summary>
/// The pieces every stiff solver shares that the explicit family had no use for: the mass matrix
/// kept as a matrix rather than folded into the derivative, the Jacobian by whichever of the four
/// routes <c>odeset</c> named, and the small matrix arithmetic the iteration matrix is built from.
/// </summary>
internal static class OdeStiffSupport
{
    /// <summary>Machine epsilon, written out because the constants it appears in are the reference's.</summary>
    public const double Epsilon = 2.220446049250313e-16;

    /// <summary>Which of MATLAB's five mass-matrix kinds the options describe.</summary>
    public static OdeMassType MassTypeOf(OdeOptions options)
    {
        if (options.Mass is not null)
        {
            return OdeMassType.Constant;
        }

        if (options.MassFunction is null)
        {
            return OdeMassType.None;
        }

        if (!options.MassDependsOnState)
        {
            return OdeMassType.TimeDependent;
        }

        return options.MassStronglyStateDependent ? OdeMassType.StateDependentStrong : OdeMassType.StateDependentWeak;
    }

    /// <summary>The mass matrix at (<paramref name="t"/>, <paramref name="y"/>), or the identity when there is none.</summary>
    public static double[,] MassAt(OdeOptions options, OdeMassType type, double t, double[] y, int n) => type switch
    {
        OdeMassType.None => Identity(n),
        OdeMassType.Constant => options.Mass!,
        _ => options.MassFunction!(t, y),
    };

    /// <summary>The n-by-n identity.</summary>
    public static double[,] Identity(int n)
    {
        var identity = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            identity[i, i] = 1;
        }

        return identity;
    }

    /// <summary>How many entries of <paramref name="a"/> are not zero.</summary>
    public static int NonZeroCount(double[,] a)
    {
        int count = 0;
        for (int i = 0; i < a.GetLength(0); i++)
        {
            for (int j = 0; j < a.GetLength(1); j++)
            {
                if (a[i, j] != 0)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Whether everything off <paramref name="a"/>'s diagonal is zero.</summary>
    public static bool IsDiagonal(double[,] a)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                if (i != j && a[i, j] != 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary><c>a·v</c>.</summary>
    public static double[] Multiply(double[,] a, double[] v)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        var answer = new double[rows];
        for (int i = 0; i < rows; i++)
        {
            double sum = 0;
            for (int j = 0; j < cols; j++)
            {
                sum += a[i, j] * v[j];
            }

            answer[i] = sum;
        }

        return answer;
    }

    /// <summary><c>m − scale·j</c>, as a fresh matrix — the iteration matrix before any row scaling.</summary>
    public static double[,] IterationMatrix(double[,] m, double scale, double[,] j)
    {
        int n = m.GetLength(0);
        var iteration = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                iteration[r, c] = m[r, c] - (scale * j[r, c]);
            }
        }

        return iteration;
    }

    /// <summary>
    /// <c>M·v</c>, or <paramref name="v"/> itself when there is no mass matrix.
    /// </summary>
    /// <remarks>
    /// This sits inside the Newton iteration, so on a problem of four hundred equations the
    /// difference between the two branches is a hundred and sixty thousand multiplications per
    /// pass against none. MATLAB reaches the same place by keeping a <em>sparse</em> identity where
    /// there is no mass matrix; the identity's product is the vector, so the branch says so.
    /// </remarks>
    public static double[] MassTimes(OdeMassType type, double[,] m, double[] v) =>
        type == OdeMassType.None ? v : Multiply(m, v);

    /// <summary>Adds <paramref name="addend"/> into <paramref name="into"/> in place.</summary>
    public static void AddInPlace(double[,] into, double[,] addend)
    {
        int rows = into.GetLength(0);
        int cols = into.GetLength(1);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                into[r, c] += addend[r, c];
            }
        }
    }

    /// <summary>
    /// Divides each row of <paramref name="a"/> by its own largest magnitude and answers the
    /// reciprocals used.
    /// </summary>
    /// <remarks>
    /// A differential-algebraic problem leaves rows of the iteration matrix whose size is set by
    /// the step and rows whose size is not, and the difference grows without bound as the step
    /// shrinks. Scaling each row explicitly is what keeps the factorization meaningful; MATLAB does
    /// the same and calls it <c>RowScale</c>.
    /// </remarks>
    public static double[] ScaleRows(double[,] a)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        var scale = new double[rows];
        for (int r = 0; r < rows; r++)
        {
            double largest = 0;
            for (int c = 0; c < cols; c++)
            {
                largest = Math.Max(largest, Math.Abs(a[r, c]));
            }

            scale[r] = 1 / largest;
            for (int c = 0; c < cols; c++)
            {
                a[r, c] *= scale[r];
            }
        }

        return scale;
    }

    /// <summary>
    /// The one-norm condition number of <paramref name="a"/>, estimated from its factorization.
    /// </summary>
    /// <remarks>
    /// MATLAB's test for a singular mass matrix is <c>eps·nnz(M)·condest(M) &gt; 1</c>, and its
    /// <c>condest</c> is the block one-norm estimator started from a random matrix. What the test
    /// asks is whether the matrix is singular to working precision, which is a question about
    /// orders of magnitude; the estimate the factorization already carries answers it, and answers
    /// it the same way every run.
    /// </remarks>
    public static double ConditionEstimate(double[,] a)
    {
        int n = a.GetLength(0);
        double norm = 0;
        for (int c = 0; c < n; c++)
        {
            double column = 0;
            for (int r = 0; r < n; r++)
            {
                column += Math.Abs(a[r, c]);
            }

            norm = Math.Max(norm, column);
        }

        if (norm == 0)
        {
            return double.PositiveInfinity;
        }

        double reciprocal = LuDecomposition.Factor(a).ReciprocalCondition(norm);
        return reciprocal > 0 ? 1 / reciprocal : double.PositiveInfinity;
    }

    /// <summary>The largest of <c>|v(i)| / w(i)</c>.</summary>
    public static double WeightedMax(double[] v, double[] w)
    {
        double largest = 0;
        for (int i = 0; i < v.Length; i++)
        {
            largest = Math.Max(largest, Math.Abs(v[i]) / w[i]);
        }

        return largest;
    }

    /// <summary>The largest of <c>|v(i)·s(i)|</c>, where <paramref name="s"/> is a reciprocal weight.</summary>
    public static double ScaledMax(double[] v, double[] s)
    {
        double largest = 0;
        for (int i = 0; i < v.Length; i++)
        {
            largest = Math.Max(largest, Math.Abs(v[i] * s[i]));
        }

        return largest;
    }

    /// <summary>The 2-norm.</summary>
    public static double Norm(double[] v) => NormEstimators.VectorNorm(v);

    /// <summary><c>a − b</c>.</summary>
    public static double[] Subtract(double[] a, double[] b)
    {
        var answer = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            answer[i] = a[i] - b[i];
        }

        return answer;
    }

    /// <summary>Whether any of <paramref name="indices"/> holds a negative value.</summary>
    public static bool AnyNegative(double[] state, int[] indices)
    {
        foreach (int index in indices)
        {
            if (state[index] < 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>MATLAB's message when a solver gives up because the step cannot shrink further.</summary>
    public static string ToleranceWarning(double t, double smallestStep) =>
        $"Failure at t={t:E6}.  Unable to meet integration tolerances without reducing the step size below the smallest value allowed ({smallestStep:E6}) at time t.";
}

/// <summary>
/// What a stiff solver does before its first step and the explicit family did not have to: keep the
/// mass matrix out of the derivative, and say so when a non-negativity constraint cannot be honoured
/// beside one.
/// </summary>
internal static class OdeStiffPrelude
{
    /// <summary>
    /// The same options with the mass matrix removed, so that <see cref="OdeSetup"/> leaves the
    /// derivative alone. A linearly implicit problem also drops <c>NonNegative</c>, with MATLAB's
    /// warning, because the constraint is written for <c>y' = f</c> and there is no <c>f</c> here.
    /// </summary>
    public static OdeOptions WithoutMass(OdeOptions options, OdeMassType massType, string solver)
    {
        if (massType == OdeMassType.None)
        {
            return options;
        }

        if (options.NonNegative is { Length: > 0 })
        {
            options.Warn?.Invoke(
                $"{solver.ToUpperInvariant()} ignores the NonNegative property for linearly implicit systems.");
        }

        return options with { Mass = null, MassFunction = null, NonNegative = null };
    }

    /// <summary>The same options with the mass matrix and the constraint both removed.</summary>
    public static OdeOptions WithoutMassOrConstraint(OdeOptions options, string solver, bool warnConstraint)
    {
        if (warnConstraint && options.NonNegative is { Length: > 0 })
        {
            options.Warn?.Invoke($"{solver.ToUpperInvariant()} ignores the NonNegative property.");
        }

        return options with { Mass = null, MassFunction = null, NonNegative = null };
    }

    /// <summary>Refuses a mass matrix that is not square and the size of the state.</summary>
    public static void CheckMassSize(string solver, double[,] mass, int n)
    {
        if (mass.GetLength(0) != n || mass.GetLength(1) != n)
        {
            throw new OdeArgumentException("MATLAB:odemass:MassSize",
                $"{solver}: the mass matrix must be {n}-by-{n}.");
        }
    }
}

/// <summary>
/// Where a stiff solver's Jacobian comes from: a matrix the caller gave, a function the caller
/// gave, or forward differences — and whether it is constant, in which case it is formed once.
/// </summary>
internal sealed class OdeJacobianSource
{
    private readonly Func<double, double[], double[,]>? _analytic;
    private readonly OdeJacobianOptions? _numeric;
    private readonly Func<double, double[][], double[][]>? _vectorized;
    private double[,]? _constant;

    private OdeJacobianSource(bool constant, double[,]? value, Func<double, double[], double[,]>? analytic,
        OdeJacobianOptions? numeric, Func<double, double[][], double[][]>? vectorized)
    {
        Constant = constant;
        _constant = value;
        _analytic = analytic;
        _numeric = numeric;
        _vectorized = vectorized;
    }

    /// <summary>Whether the Jacobian never changes, so the iteration matrix stays current.</summary>
    public bool Constant { get; }

    /// <summary>Whether it is given in closed form, so forming it costs no derivative evaluations.</summary>
    public bool Analytic => _analytic is not null || _constant is not null;

    /// <summary>The value already formed for a constant Jacobian, or null.</summary>
    public double[,]? Cached => _constant;

    /// <summary>Reads the options the way MATLAB's <c>odejacobian</c> reads them.</summary>
    public static OdeJacobianSource Read(OdeOptions options, double[] y0)
    {
        if (options.Jacobian is { } given)
        {
            return new OdeJacobianSource(true, given, null, null, null);
        }

        if (options.JacobianFunction is { } analytic)
        {
            return new OdeJacobianSource(options.JacobianConstant, null, analytic, null, null);
        }

        var numeric = new OdeJacobianOptions
        {
            Threshold = ThresholdOf(options, y0.Length),
            Pattern = options.JacobianPattern,
            Groups = options.JacobianPattern is { } pattern ? OdeNumericalJacobian.ColumnGroups(pattern) : null,
        };

        return new OdeJacobianSource(options.JacobianConstant, null, null, numeric,
            options.Vectorized ? options.VectorizedDerivative : null);
    }

    /// <summary>The absolute tolerance as one number per component — what <c>numjac</c> calls <c>thresh</c>.</summary>
    public static double[] ThresholdOf(OdeOptions options, int n)
    {
        double[] atol = options.AbsoluteTolerance ?? [1e-6];
        var threshold = new double[n];
        for (int i = 0; i < n; i++)
        {
            threshold[i] = atol[atol.Length == 1 ? 0 : i];
        }

        return threshold;
    }

    /// <summary>
    /// The Jacobian at (<paramref name="t"/>, <paramref name="y"/>), where <paramref name="value"/>
    /// is <c>f(t, y)</c>. Answers how many derivative evaluations the difference cost — nought when
    /// the Jacobian is analytic or already formed.
    /// </summary>
    public double[,] Evaluate(OdeFunction f, double t, double[] y, double[] value, out int evaluations)
    {
        evaluations = 0;
        if (_constant is { } kept)
        {
            return kept;
        }

        double[,] jacobian;
        if (_analytic is { } analytic)
        {
            jacobian = analytic(t, y);
        }
        else
        {
            jacobian = OdeNumericalJacobian.Compute(
                state => f(t, state),
                _vectorized is null ? null : states => _vectorized(t, states),
                y, value, _numeric!, out evaluations);
        }

        if (Constant)
        {
            _constant = jacobian;
        }

        return jacobian;
    }
}

/// <summary>
/// <c>d(M(t, y)·v)/dy</c> by forward differences — the extra term a strongly state-dependent mass
/// matrix puts into the iteration matrix, MATLAB's <c>odenumjac(@odemxv, ...)</c>.
/// </summary>
internal sealed class OdeMassVectorJacobian
{
    private readonly Func<double, double[], double[,]> _mass;
    private readonly OdeJacobianOptions _options;

    /// <summary>Reads the options — <c>MvPattern</c> and the absolute tolerance.</summary>
    public OdeMassVectorJacobian(OdeOptions options, int n)
    {
        _mass = options.MassFunction!;
        _options = new OdeJacobianOptions
        {
            Threshold = OdeJacobianSource.ThresholdOf(options, n),
            Pattern = options.MassVectorPattern,
            Groups = options.MassVectorPattern is { } pattern
                ? OdeNumericalJacobian.ColumnGroups(pattern)
                : null,
        };
    }

    /// <summary>The derivative of <c>M(t, y)·v</c> in <c>y</c>, at (<paramref name="t"/>, <paramref name="y"/>).</summary>
    public double[,] Evaluate(double t, double[] y, double[] v) =>
        OdeNumericalJacobian.Compute(
            state => OdeStiffSupport.Multiply(_mass(t, state), v),
            null,
            y,
            OdeStiffSupport.Multiply(_mass(t, y), v),
            _options,
            out _);
}
