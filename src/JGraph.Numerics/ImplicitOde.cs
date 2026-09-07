namespace JGraph.Numerics;

/// <summary>A fully implicit equation <c>f(t, y, y') = 0</c>, answered as the residual.</summary>
public delegate double[] ImplicitOdeFunction(double t, double[] y, double[] yp);

/// <summary>The event function of a fully implicit problem, which sees the slope as well.</summary>
public delegate OdeEventReading ImplicitOdeEventFunction(double t, double[] y, double[] yp);

/// <summary>
/// What <c>odeset</c> can say that only the fully implicit solvers read. Everything else — the
/// tolerances, the step limits, the output function, <c>Refine</c>, <c>Stats</c>, <c>MaxOrder</c> —
/// is in <see cref="Common"/>.
/// </summary>
/// <remarks>
/// <c>ode15i</c> takes each of <c>Jacobian</c>, <c>JPattern</c> and <c>Vectorized</c> as a pair,
/// because there are two partial derivatives and they are rarely alike: the one in <c>y</c> is
/// usually dense and the one in <c>y'</c> is usually the mass matrix, which is often diagonal and
/// often constant. Either half may be given and the other differenced.
/// </remarks>
public sealed record ImplicitOdeOptions
{
    /// <summary>The options every solver shares.</summary>
    public OdeOptions Common { get; init; } = new();

    /// <summary><c>df/dy</c> as a matrix, when the caller gave one.</summary>
    public double[,]? JacobianY { get; init; }

    /// <summary><c>df/dy'</c> as a matrix, when the caller gave one.</summary>
    public double[,]? JacobianYp { get; init; }

    /// <summary>A function answering both partial derivatives at a point.</summary>
    public Func<double, double[], double[], (double[,] Y, double[,] Yp)>? JacobianFunction { get; init; }

    /// <summary>Which entries of <c>df/dy</c> can be nonzero; null is a full matrix.</summary>
    public bool[,]? PatternY { get; init; }

    /// <summary>Which entries of <c>df/dy'</c> can be nonzero; null is a full matrix.</summary>
    public bool[,]? PatternYp { get; init; }

    /// <summary>Whether the residual answers a matrix of states in one call.</summary>
    public bool VectorizedY { get; init; }

    /// <summary>Whether the residual answers a matrix of slopes in one call.</summary>
    public bool VectorizedYp { get; init; }

    /// <summary>Whether both partial derivatives are constant, so they are formed once.</summary>
    public bool JacobianConstant { get; init; }

    /// <summary>The event function, or null when nothing is watched.</summary>
    public ImplicitOdeEventFunction? Events { get; init; }
}

/// <summary>
/// The two partial derivatives of <c>f(t, y, y')</c>, by whichever route the options named, with
/// the differencing storage each carries between calls — MATLAB's <c>ode15ipdinit</c> and
/// <c>ode15ipdupdate</c>.
/// </summary>
internal sealed class ImplicitJacobianPair
{
    private readonly Func<double, double[], double[], (double[,] Y, double[,] Yp)>? _analytic;
    private readonly OdeJacobianOptions? _numericY;
    private readonly OdeJacobianOptions? _numericYp;

    private ImplicitJacobianPair(Func<double, double[], double[], (double[,], double[,])>? analytic,
        OdeJacobianOptions? numericY, OdeJacobianOptions? numericYp, bool constant, double[,] y, double[,] yp)
    {
        _analytic = analytic;
        _numericY = numericY;
        _numericYp = numericYp;
        Constant = constant;
        Y = y;
        Yp = yp;
    }

    /// <summary><c>df/dy</c> as it stands.</summary>
    public double[,] Y { get; private set; }

    /// <summary><c>df/dy'</c> as it stands.</summary>
    public double[,] Yp { get; private set; }

    /// <summary>Whether both are constant, so neither is ever formed again.</summary>
    public bool Constant { get; }

    /// <summary>
    /// Reads the options and forms whichever partial derivatives were not supplied, at
    /// (<paramref name="t0"/>, <paramref name="y0"/>, <paramref name="yp0"/>).
    /// </summary>
    public static ImplicitJacobianPair Create(ImplicitOdeFunction f, double t0, double[] y0, double[] yp0,
        double[] residual, ImplicitOdeOptions options, out int evaluations)
    {
        int n = y0.Length;
        evaluations = 0;
        if (options.JacobianFunction is { } analytic)
        {
            (double[,] jy, double[,] jyp) = analytic(t0, y0, yp0);
            return new ImplicitJacobianPair(analytic, null, null, options.JacobianConstant, jy, jyp);
        }

        double[,]? givenY = options.JacobianY;
        double[,]? givenYp = options.JacobianYp;
        bool constant = options.JacobianConstant || (givenY is not null && givenYp is not null);

        double[] threshold = OdeJacobianSource.ThresholdOf(options.Common, n);
        OdeJacobianOptions? numericY = givenY is null
            ? new OdeJacobianOptions
            {
                Threshold = threshold,
                Pattern = options.PatternY,
                Groups = options.PatternY is { } py ? OdeNumericalJacobian.ColumnGroups(py) : null,
            }
            : null;
        OdeJacobianOptions? numericYp = givenYp is null
            ? new OdeJacobianOptions
            {
                Threshold = threshold,
                Pattern = options.PatternYp,
                Groups = options.PatternYp is { } pyp ? OdeNumericalJacobian.ColumnGroups(pyp) : null,
            }
            : null;

        var pair = new ImplicitJacobianPair(null, numericY, numericYp, constant,
            givenY ?? new double[n, n], givenYp ?? new double[n, n]);
        pair.Update(f, t0, y0, yp0, residual, out evaluations);
        return pair;
    }

    /// <summary>
    /// Forms the partial derivatives again at (<paramref name="t"/>, <paramref name="y"/>,
    /// <paramref name="yp"/>), where <paramref name="residual"/> is <c>f</c> there. A half the
    /// caller supplied is left alone.
    /// </summary>
    public void Update(ImplicitOdeFunction f, double t, double[] y, double[] yp, double[] residual,
        out int evaluations)
    {
        evaluations = 0;
        if (_analytic is { } analytic)
        {
            (Y, Yp) = analytic(t, y, yp);
            return;
        }

        if (_numericY is not null)
        {
            Y = OdeNumericalJacobian.Compute(state => f(t, state, yp), null, y, residual, _numericY, out int cost);
            evaluations += cost;
        }

        if (_numericYp is not null)
        {
            Yp = OdeNumericalJacobian.Compute(slope => f(t, y, slope), null, yp, residual, _numericYp, out int cost);
            evaluations += cost;
        }
    }

    /// <summary>Whether either half is differenced, so that forming it needs the residual first.</summary>
    public bool NeedsResidual => _numericY is not null || _numericYp is not null;
}

/// <summary>
/// Fornberg's weights: the coefficients with which values at distinct nodes combine into the value,
/// and the derivatives, of the polynomial that interpolates them.
/// </summary>
/// <remarks>
/// <c>ode15i</c> writes its backward differentiation formulas in Lagrange form rather than in
/// backward differences, because its mesh is not quasi-constant: a step that changes size changes
/// the formula's coefficients rather than merely rescaling a history. These weights are that
/// formula, recomputed each step from the mesh the solver actually has.
/// </remarks>
internal static class LagrangeWeights
{
    /// <summary>
    /// <c>c[j, d]</c> is the coefficient of the value at <c>x[j]</c> in the derivative of order
    /// <c>d</c> at <paramref name="xi"/>, for <c>d</c> up to <paramref name="maxDerivative"/>.
    /// </summary>
    public static double[,] Compute(ReadOnlySpan<double> x, double xi, int maxDerivative)
    {
        int n = x.Length - 1;
        var c = new double[n + 2, maxDerivative + 2];   // one-based inside, as the recurrence is written
        c[1, 1] = 1;
        double tmp1 = 1;
        double tmp4 = x[0] - xi;
        for (int i = 1; i <= n; i++)
        {
            int mn = Math.Min(i, maxDerivative);
            double tmp2 = 1;
            double tmp5 = tmp4;
            tmp4 = x[i] - xi;
            for (int j = 0; j <= i - 1; j++)
            {
                double tmp3 = x[i] - x[j];
                tmp2 *= tmp3;
                if (j == i - 1)
                {
                    for (int k = mn; k >= 1; k--)
                    {
                        c[i + 1, k + 1] = tmp1 * ((k * c[i, k]) - (tmp5 * c[i, k + 1])) / tmp2;
                    }

                    c[i + 1, 1] = -tmp1 * tmp5 * c[i, 1] / tmp2;
                }

                for (int k = mn; k >= 1; k--)
                {
                    c[j + 1, k + 1] = ((tmp4 * c[j + 1, k + 1]) - (k * c[j + 1, k])) / tmp3;
                }

                c[j + 1, 1] = tmp4 * c[j + 1, 1] / tmp3;
            }

            tmp1 = tmp2;
        }

        var weights = new double[n + 1, maxDerivative + 1];
        for (int j = 0; j <= n; j++)
        {
            for (int d = 0; d <= maxDerivative; d++)
            {
                weights[j, d] = c[j + 1, d + 1];
            }
        }

        return weights;
    }
}
