using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Numerics.Sparse;

/// <summary>How a solver stopped, in MATLAB's numbering.</summary>
public static class KrylovFlag
{
    /// <summary>Converged to the requested tolerance within the iteration limit.</summary>
    public const int Converged = 0;

    /// <summary>Ran out of iterations.</summary>
    public const int MaxIterations = 1;

    /// <summary>A preconditioner solve produced a non-finite vector.</summary>
    public const int IllConditionedPreconditioner = 2;

    /// <summary>Stagnated: three consecutive iterates were the same to rounding.</summary>
    public const int Stagnated = 3;

    /// <summary>A scalar in the recurrence became too small or too large to go on.</summary>
    public const int ScalarBreakdown = 4;

    /// <summary>The preconditioner is not symmetric positive definite (<c>minres</c>, <c>symmlq</c>).</summary>
    public const int PreconditionerNotSpd = 5;
}

/// <summary>Everything the caller may say about how the iteration should be run.</summary>
public sealed class KrylovOptions
{
    /// <summary>Relative residual to stop at; MATLAB's default is 1e-6.</summary>
    public double? Tolerance { get; init; }

    /// <summary>Iteration cap. Each solver has its own default, which the frame applies.</summary>
    public int? MaxIterations { get; init; }

    /// <summary>The first (or only) preconditioner, applied as <c>M1\x</c>.</summary>
    public KrylovOperator? Left { get; init; }

    /// <summary>The second preconditioner, applied as <c>M2\x</c>.</summary>
    public KrylovOperator? Right { get; init; }

    /// <summary>The starting iterate; an all-zero vector when absent.</summary>
    public double[]? InitialGuess { get; init; }

    /// <summary><c>gmres</c>'s restart length. Null (or the matrix order) means no restarting.</summary>
    public int? Restart { get; init; }

    /// <summary><c>bicgstabl</c>'s degree, MATLAB's <c>ell</c>; 2 by default.</summary>
    public int Ell { get; init; } = 2;
}

/// <summary>What every solver hands back, in the order MATLAB's five outputs come in.</summary>
/// <param name="Solution">The iterate returned — the first one with minimal residual.</param>
/// <param name="Flag">One of <see cref="KrylovFlag"/>.</param>
/// <param name="RelativeResidual"><c>norm(b - A*x)/norm(b)</c> at that iterate.</param>
/// <param name="Iteration">
/// The iteration it was computed at. One number for most solvers, a half-integer for the
/// <c>bicgstab</c> pair, and the two-element <c>[outer inner]</c> for <c>gmres</c>.
/// </param>
/// <param name="ResidualNorms">The residual norm at every iteration, starting from the initial guess.</param>
public sealed record KrylovResult(
    double[] Solution,
    int Flag,
    double RelativeResidual,
    double[] Iteration,
    double[] ResidualNorms)
{
    /// <summary><c>lsqr</c>'s sixth output: the least-squares residual estimate per iteration.</summary>
    public double[]? LeastSquaresNorms { get; init; }

    /// <summary><c>minres</c> and <c>symmlq</c>'s sixth output: the residual of the CG iterate.</summary>
    public double[]? ConjugateGradientNorms { get; init; }

    /// <summary>Warnings the run raised, as the message text MATLAB prints.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Where the loop stopped, which is not always where the returned iterate came from: the
    /// printed message says both, "stopped at" from this and "the iterate returned" from
    /// <see cref="Iteration"/>.
    /// </summary>
    public double[] StoppedAt { get; init; } = [];
}

/// <summary>
/// The eleven iterative solvers of MATLAB's <c>sparfun</c>, on one frame.
/// </summary>
/// <remarks>
/// <para>
/// Every one of them is the same shape around a different recurrence, and the shape is the part
/// that decides the answer a script can check. The residual norm each iteration reports, the
/// moment the loop stops, the extra sweeps taken when the recurrence's residual and the true
/// residual disagree, the iterate finally returned when the last one is not the best one — those
/// are the frame, and they are why <c>iter</c> and <c>flag</c> can be matched exactly rather than
/// approximately. The recurrences themselves are textbook; the bookkeeping is not.
/// </para>
/// <para>
/// Three rules from MATLAB's sources run through all of them and are worth naming once. A
/// convergence test that passes on the recurrence's residual is re-tested against
/// <c>b - A*x</c> before it is believed, because the recurrence drifts. When that re-test fails,
/// the solver does not stop: it grants itself up to <c>min(floor(n/50), 5, n-maxit)</c> further
/// iterations — ten for the <c>bicgstab</c> pair, <c>4*ell</c> for <c>bicgstabl</c> — and only
/// then calls the run stagnant. And three consecutive iterates whose update is below
/// <c>eps*norm(x)</c> is stagnation, whatever the residual says.
/// </para>
/// <para>
/// Read against R2025b's <c>pcg.m</c>, <c>bicg.m</c>, <c>bicgstab.m</c>, <c>bicgstabl.m</c>,
/// <c>cgs.m</c>, <c>gmres.m</c>, <c>lsqr.m</c>, <c>minres.m</c>, <c>qmr.m</c>, <c>symmlq.m</c>,
/// <c>tfqmr.m</c> and <c>private\iterchk.m</c>, <c>private\iterapp.m</c>, <c>private\itermsg.m</c>.
/// </para>
/// </remarks>
public static partial class KrylovSolver
{
    /// <summary>MATLAB's <c>eps</c>, the yardstick every stagnation test is measured against.</summary>
    private const double Epsilon = 2.220446049250313e-16;

    /// <summary>Three consecutive stagnant iterates ends the run.</summary>
    private const int MaxStagnationSteps = 3;

    /// <summary>The 2-norm the solvers compare against <c>tolb</c>, correctly rounded as MATLAB's is.</summary>
    private static double Norm(double[] x) => NormEstimators.VectorNorm(x);

    private static double Dot(double[] a, double[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }

    private static bool AllFinite(double[] x)
    {
        foreach (double v in x)
        {
            if (!double.IsFinite(v))
            {
                return false;
            }
        }

        return true;
    }

    private static double[] Zeros(int n) => new double[n];

    /// <summary>a + s·b, allocating the answer — the shape every recurrence line below is written in.</summary>
    private static double[] Add(double[] a, double s, double[] b)
    {
        var y = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            y[i] = a[i] + (s * b[i]);
        }

        return y;
    }

    private static double[] Scale(double s, double[] a)
    {
        var y = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            y[i] = s * a[i];
        }

        return y;
    }

    /// <summary>The prefix of a residual history that is actually filled in.</summary>
    private static double[] Truncate(double[] history, int count) =>
        count <= 0 ? [] : history[..Math.Min(count, history.Length)];

    /// <summary>
    /// The argument checking and the two early answers every solver shares. The tolerance clamp is
    /// per-solver: <c>pcg</c> and <c>lsqr</c> reject a tolerance equal to <c>eps</c> where the
    /// others accept it, which is a difference in <c>&lt;</c> against <c>&lt;=</c> in their sources
    /// and nothing more, but it decides whether a warning is raised.
    /// </summary>
    private sealed class Setup
    {
        public required string Name { get; init; }

        public required int Order { get; init; }

        public required double Tolerance { get; init; }

        public required int MaxIterations { get; init; }

        public required double[] Start { get; init; }

        public required double RightHandSideNorm { get; init; }

        public required bool Warned { get; init; }

        public List<string> Warnings { get; } = [];

        public double ToleranceTimesNorm => Tolerance * RightHandSideNorm;

        /// <summary>MATLAB's <c>maxmsteps</c>: how many extra sweeps a failed re-test may buy.</summary>
        public int MoreStepsAllowed(int cap) =>
            Math.Min(Math.Min((int)Math.Floor(Order / 50.0), cap), Order - MaxIterations);
    }

    private static Setup Prepare(string name, int order, double[] b, KrylovOptions options,
        bool rejectToleranceAtEpsilon, int defaultMaxIterations, int leastMaxIterations = 0,
        bool startMatchesOrder = true)
    {
        double tolerance = options.Tolerance ?? 1e-6;
        bool warned = false;
        var warnings = new List<string>();
        if (rejectToleranceAtEpsilon ? tolerance <= Epsilon : tolerance < Epsilon)
        {
            warnings.Add("Tolerance may not be achievable. Use a larger tolerance.");
            warned = true;
            tolerance = Epsilon;
        }
        else if (tolerance >= 1)
        {
            warnings.Add($"Input tol is bigger than 1 \n         Try to use a smaller tolerance.");
            warned = true;
            tolerance = 1 - Epsilon;
        }

        int maxit = Math.Max(options.MaxIterations ?? defaultMaxIterations, leastMaxIterations);
        // lsqr's initial guess has as many entries as A has columns, which for a rectangular
        // system is not the length of b — so it is the one solver whose guess is not checked here.
        double[] start = options.InitialGuess is null ? Zeros(order) : (double[])options.InitialGuess.Clone();
        if (startMatchesOrder && start.Length != order)
        {
            throw new ArgumentException($"{name}: the initial guess must have {order} elements.");
        }

        var setup = new Setup
        {
            Name = name,
            Order = order,
            Tolerance = tolerance,
            MaxIterations = maxit,
            Start = start,
            RightHandSideNorm = Norm(b),
            Warned = warned,
        };

        setup.Warnings.AddRange(warnings);
        return setup;
    }

    /// <summary>The answer when the right-hand side is all zeros: no iteration, and 0/0 for a residual.</summary>
    private static KrylovResult ZeroRightHandSide(Setup setup, int iterationParts = 1) =>
        new(Zeros(setup.Order), KrylovFlag.Converged, 0,
            iterationParts == 2 ? [0, 0] : [0], [0])
        { Warnings = setup.Warnings };

    private static KrylovResult GoodEnoughStart(Setup setup, double residualNorm, int iterationParts = 1) =>
        new((double[])setup.Start.Clone(), KrylovFlag.Converged,
            residualNorm / setup.RightHandSideNorm,
            iterationParts == 2 ? [0, 0] : [0], [residualNorm])
        { Warnings = setup.Warnings };

    /// <summary>
    /// Both preconditioners applied in order, or null when one of them produced a non-finite
    /// vector — which is flag 2 wherever it happens.
    /// </summary>
    private static double[]? Precondition(KrylovOptions options, double[] x)
    {
        double[] y = x;
        if (options.Left is not null)
        {
            y = options.Left.Apply(y);
            if (!AllFinite(y))
            {
                return null;
            }
        }

        if (options.Right is not null)
        {
            y = options.Right.Apply(y);
            if (!AllFinite(y))
            {
                return null;
            }
        }

        return y;
    }

    /// <summary>
    /// The closing bookkeeping the short-recurrence solvers share: the iterate returned is the
    /// first one that achieved the smallest residual, unless the last one is at least as good.
    /// </summary>
    private static KrylovResult Finish(Setup setup, KrylovOperator a, double[] b,
        int flag, double[] x, double[] xmin, double iterationMinimum, double lastIteration,
        double lastResidual, double[] history, int historyCount, double stoppedAt)
    {
        double relative;
        double[] iteration;
        double[] answer = x;
        if (flag == KrylovFlag.Converged)
        {
            relative = lastResidual / setup.RightHandSideNorm;
            iteration = [lastIteration];
        }
        else
        {
            double[] residual = Add(b, -1, a.Apply(xmin));
            double minimumNorm = Norm(residual);
            if (minimumNorm <= lastResidual)
            {
                answer = xmin;
                iteration = [iterationMinimum];
                relative = minimumNorm / setup.RightHandSideNorm;
            }
            else
            {
                iteration = [lastIteration];
                relative = lastResidual / setup.RightHandSideNorm;
            }
        }

        return new KrylovResult(answer, flag, relative, iteration, Truncate(history, historyCount))
        {
            Warnings = setup.Warnings,
            StoppedAt = [stoppedAt],
        };
    }

    // --- pcg -----------------------------------------------------------------------------------

    /// <summary>
    /// Preconditioned conjugate gradients: the three-term recurrence for a symmetric positive
    /// definite system. A non-positive <c>p'q</c> is where the method finds out the matrix was not
    /// definite after all, and MATLAB reports that as flag 4 rather than as an error.
    /// </summary>
    public static KrylovResult Pcg(KrylovOperator a, double[] b, KrylovOptions options)
    {
        int n = b.Length;
        Setup setup = Prepare("pcg", n, b, options, rejectToleranceAtEpsilon: true, Math.Min(n, 20));
        if (setup.RightHandSideNorm == 0)
        {
            return ZeroRightHandSide(setup);
        }

        int flag = KrylovFlag.MaxIterations;
        double[] x = setup.Start;
        double[] xmin = x;
        double iterationMinimum = 0;
        double toleranceNorm = setup.ToleranceTimesNorm;
        double[] r = Add(b, -1, a.Apply(x));
        double normr = Norm(r);
        double actual = normr;
        if (normr <= toleranceNorm)
        {
            return GoodEnoughStart(setup, normr);
        }

        var history = new double[setup.MaxIterations + 1];
        history[0] = normr;
        double normrmin = normr;
        double rho = 1;
        int stagnation = 0;
        int moreSteps = 0;
        int maxMoreSteps = setup.MoreStepsAllowed(5);
        double[] p = Zeros(n);
        double iteration = 0;
        int ii = 0;

        for (ii = 1; ii <= setup.MaxIterations; ii++)
        {
            double[]? z = Precondition(options, r);
            if (z is null)
            {
                flag = KrylovFlag.IllConditionedPreconditioner;
                break;
            }

            double rho1 = rho;
            rho = Dot(r, z);
            if (rho == 0 || double.IsInfinity(rho))
            {
                flag = KrylovFlag.ScalarBreakdown;
                break;
            }

            if (ii == 1)
            {
                p = z;
            }
            else
            {
                double beta = rho / rho1;
                if (beta == 0 || double.IsInfinity(beta))
                {
                    flag = KrylovFlag.ScalarBreakdown;
                    break;
                }

                p = Add(z, beta, p);
            }

            double[] q = a.Apply(p);
            double pq = Dot(p, q);
            if (pq <= 0 || double.IsInfinity(pq))
            {
                flag = KrylovFlag.ScalarBreakdown;
                break;
            }

            double alpha = rho / pq;
            if (double.IsInfinity(alpha))
            {
                flag = KrylovFlag.ScalarBreakdown;
                break;
            }

            stagnation = Norm(p) * Math.Abs(alpha) < Epsilon * Norm(x) ? stagnation + 1 : 0;

            x = Add(x, alpha, p);
            r = Add(r, -alpha, q);
            normr = Norm(r);
            actual = normr;
            history[ii] = normr;

            if (normr <= toleranceNorm || stagnation >= MaxStagnationSteps || moreSteps > 0)
            {
                r = Add(b, -1, a.Apply(x));
                actual = Norm(r);
                history[ii] = actual;
                if (actual <= toleranceNorm)
                {
                    flag = KrylovFlag.Converged;
                    iteration = ii;
                    break;
                }

                if (stagnation >= MaxStagnationSteps && moreSteps == 0)
                {
                    stagnation = 0;
                }

                moreSteps++;
                if (moreSteps >= maxMoreSteps)
                {
                    if (!setup.Warned)
                    {
                        setup.Warnings.Add("Tolerance may not be achievable. Use a larger tolerance.");
                    }

                    flag = KrylovFlag.Stagnated;
                    iteration = ii;
                    break;
                }
            }

            if (actual < normrmin)
            {
                normrmin = actual;
                xmin = x;
                iterationMinimum = ii;
            }

            if (stagnation >= MaxStagnationSteps)
            {
                flag = KrylovFlag.Stagnated;
                break;
            }
        }

        ii = Math.Min(ii, setup.MaxIterations);
        int kept = flag is <= KrylovFlag.MaxIterations or KrylovFlag.Stagnated ? ii + 1 : ii;
        return Finish(setup, a, b, flag, x, xmin, iterationMinimum,
            flag == KrylovFlag.Converged ? iteration : ii, actual, history, kept, ii);
    }

    // --- bicg ----------------------------------------------------------------------------------

    /// <summary>
    /// The biconjugate gradient method: two coupled recurrences, one against <c>A</c> and one
    /// against <c>A'</c>. It is the only solver here besides <c>qmr</c> and <c>lsqr</c> that needs
    /// the transpose, which is why a function handle given to it must accept a transpose flag.
    /// </summary>
    public static KrylovResult Bicg(KrylovOperator a, double[] b, KrylovOptions options)
    {
        int n = b.Length;
        Setup setup = Prepare("bicg", n, b, options, rejectToleranceAtEpsilon: false, Math.Min(n, 20));
        if (setup.RightHandSideNorm == 0)
        {
            return ZeroRightHandSide(setup);
        }

        int flag = KrylovFlag.MaxIterations;
        double[] x = setup.Start;
        double[] xmin = x;
        double iterationMinimum = 0;
        double toleranceNorm = setup.ToleranceTimesNorm;
        double[] r = Add(b, -1, a.Apply(x));
        double normr = Norm(r);
        double actual = normr;
        if (normr <= toleranceNorm)
        {
            return GoodEnoughStart(setup, normr);
        }

        double[] rt = r;
        var history = new double[setup.MaxIterations + 1];
        history[0] = normr;
        double normrmin = normr;
        double rho = 1;
        int stagnation = 0;
        int moreSteps = 0;
        int maxMoreSteps = setup.MoreStepsAllowed(5);
        double[] p = Zeros(n);
        double[] pt = Zeros(n);
        double iteration = 0;
        int ii;

        for (ii = 1; ii <= setup.MaxIterations; ii++)
        {
            double[] y = r;
            if (options.Left is not null)
            {
                y = options.Left.Apply(r);
                if (!AllFinite(y))
                {
                    flag = KrylovFlag.IllConditionedPreconditioner;
                    break;
                }
            }

            double[] z;
            double[] yt;
            if (options.Right is not null)
            {
                z = options.Right.Apply(y);
                yt = options.Right.ApplyTransposed(rt);
                if (!AllFinite(z) || !AllFinite(yt))
                {
                    flag = KrylovFlag.IllConditionedPreconditioner;
                    break;
                }
            }
            else
            {
                z = y;
                yt = rt;
            }

            double[] zt = yt;
            if (options.Left is not null)
            {
                zt = options.Left.ApplyTransposed(yt);
                if (!AllFinite(zt))
                {
                    flag = KrylovFlag.IllConditionedPreconditioner;
                    break;
                }
            }

            double rho1 = rho;
            rho = Dot(rt, z);
            if (rho == 0 || double.IsInfinity(rho))
            {
                flag = KrylovFlag.ScalarBreakdown;
                break;
            }

            if (ii == 1)
            {
                p = z;
                pt = zt;
            }
            else
            {
                double beta = rho / rho1;
                if (beta == 0 || double.IsInfinity(beta))
                {
                    flag = KrylovFlag.ScalarBreakdown;
                    break;
                }

                p = Add(z, beta, p);
                pt = Add(zt, beta, pt);
            }

            double[] q = a.Apply(p);
            double[] qt = a.ApplyTransposed(pt);
            double ptq = Dot(pt, q);
            if (ptq == 0)
            {
                flag = KrylovFlag.ScalarBreakdown;
                break;
            }

            double alpha = rho / ptq;
            if (double.IsInfinity(alpha))
            {
                flag = KrylovFlag.ScalarBreakdown;
                break;
            }

            stagnation = Math.Abs(alpha) * Norm(p) < Epsilon * Norm(x) ? stagnation + 1 : 0;

            x = Add(x, alpha, p);
            r = Add(r, -alpha, q);
            rt = Add(rt, -alpha, qt);
            normr = Norm(r);
            actual = normr;
            history[ii] = normr;

            if (normr <= toleranceNorm || stagnation >= MaxStagnationSteps || moreSteps > 0)
            {
                r = Add(b, -1, a.Apply(x));
                actual = Norm(r);
                history[ii] = actual;
                if (actual <= toleranceNorm)
                {
                    flag = KrylovFlag.Converged;
                    iteration = ii;
                    break;
                }

                if (stagnation >= MaxStagnationSteps && moreSteps == 0)
                {
                    stagnation = 0;
                }

                moreSteps++;
                if (moreSteps >= maxMoreSteps)
                {
                    if (!setup.Warned)
                    {
                        setup.Warnings.Add("Tolerance may not be achievable. Use a larger tolerance.");
                    }

                    flag = KrylovFlag.Stagnated;
                    iteration = ii;
                    break;
                }
            }

            if (actual < normrmin)
            {
                normrmin = actual;
                xmin = x;
                iterationMinimum = ii;
            }

            if (stagnation >= MaxStagnationSteps)
            {
                flag = KrylovFlag.Stagnated;
                break;
            }
        }

        ii = Math.Min(ii, setup.MaxIterations);

        // bicg re-measures the last iterate before comparing it with the best one when it ran out
        // of iterations: at flag 1 the loop's own normr came from the recurrence, not from b - A*x.
        if (flag == KrylovFlag.MaxIterations)
        {
            actual = Norm(Add(b, -1, a.Apply(x)));
        }

        int kept = flag is <= KrylovFlag.MaxIterations or KrylovFlag.Stagnated ? ii + 1 : ii;
        return Finish(setup, a, b, flag, x, xmin, iterationMinimum,
            flag == KrylovFlag.Converged ? iteration : ii, actual, history, kept, ii);
    }

    // --- cgs -----------------------------------------------------------------------------------

    /// <summary>
    /// Conjugate gradients squared: <c>bicg</c>'s two recurrences folded into one by squaring the
    /// polynomial, so the transpose is never needed. The price is a residual that can swing wildly
    /// between iterations, which is exactly why the frame keeps the best iterate rather than the
    /// last.
    /// </summary>
    public static KrylovResult Cgs(KrylovOperator a, double[] b, KrylovOptions options)
    {
        int n = b.Length;
        Setup setup = Prepare("cgs", n, b, options, rejectToleranceAtEpsilon: false, Math.Min(n, 20));
        if (setup.RightHandSideNorm == 0)
        {
            return ZeroRightHandSide(setup);
        }

        int flag = KrylovFlag.MaxIterations;
        double[] x = setup.Start;
        double[] xmin = x;
        double iterationMinimum = 0;
        double toleranceNorm = setup.ToleranceTimesNorm;
        double[] r = Add(b, -1, a.Apply(x));
        double normr = Norm(r);
        double actual = normr;
        if (normr <= toleranceNorm)
        {
            return GoodEnoughStart(setup, normr);
        }

        double[] rt = r;
        var history = new double[setup.MaxIterations + 1];
        history[0] = normr;
        double normrmin = normr;
        double rho = 1;
        int stagnation = 0;
        int moreSteps = 0;
        int maxMoreSteps = setup.MoreStepsAllowed(5);
        double[] p = Zeros(n);
        double[] q = Zeros(n);
        double iteration = 0;
        int ii;

        for (ii = 1; ii <= setup.MaxIterations; ii++)
        {
            double rho1 = rho;
            rho = Dot(rt, r);
            if (rho == 0 || double.IsInfinity(rho))
            {
                flag = KrylovFlag.ScalarBreakdown;
                break;
            }

            double[] u;
            if (ii == 1)
            {
                u = r;
                p = u;
            }
            else
            {
                double beta = rho / rho1;
                if (beta == 0 || double.IsInfinity(beta))
                {
                    flag = KrylovFlag.ScalarBreakdown;
                    break;
                }

                u = Add(r, beta, q);
                p = Add(u, beta, Add(q, beta, p));
            }

            double[]? ph = Precondition(options, p);
            if (ph is null)
            {
                flag = KrylovFlag.IllConditionedPreconditioner;
                break;
            }

            double[] vh = a.Apply(ph);
            double rtvh = Dot(rt, vh);
            if (rtvh == 0)
            {
                flag = KrylovFlag.ScalarBreakdown;
                break;
            }

            double alpha = rho / rtvh;
            if (double.IsInfinity(alpha))
            {
                flag = KrylovFlag.ScalarBreakdown;
                break;
            }

            q = Add(u, -alpha, vh);
            double[]? uh = Precondition(options, Add(u, 1, q));
            if (uh is null)
            {
                flag = KrylovFlag.IllConditionedPreconditioner;
                break;
            }

            stagnation = Math.Abs(alpha) * Norm(uh) < Epsilon * Norm(x) ? stagnation + 1 : 0;

            x = Add(x, alpha, uh);
            double[] qh = a.Apply(uh);
            r = Add(r, -alpha, qh);
            normr = Norm(r);
            actual = normr;
            history[ii] = normr;

            if (normr <= toleranceNorm || stagnation >= MaxStagnationSteps || moreSteps > 0)
            {
                r = Add(b, -1, a.Apply(x));
                actual = Norm(r);
                history[ii] = actual;
                if (actual <= toleranceNorm)
                {
                    flag = KrylovFlag.Converged;
                    iteration = ii;
                    break;
                }

                if (stagnation >= MaxStagnationSteps && moreSteps == 0)
                {
                    stagnation = 0;
                }

                moreSteps++;
                if (moreSteps >= maxMoreSteps)
                {
                    if (!setup.Warned)
                    {
                        setup.Warnings.Add("Tolerance may not be achievable. Use a larger tolerance.");
                    }

                    flag = KrylovFlag.Stagnated;
                    iteration = ii;
                    break;
                }
            }

            if (actual < normrmin)
            {
                normrmin = actual;
                xmin = x;
                iterationMinimum = ii;
            }

            if (stagnation >= MaxStagnationSteps)
            {
                flag = KrylovFlag.Stagnated;
                break;
            }
        }

        ii = Math.Min(ii, setup.MaxIterations);
        int kept = flag is <= KrylovFlag.MaxIterations or KrylovFlag.Stagnated ? ii + 1 : ii;
        return Finish(setup, a, b, flag, x, xmin, iterationMinimum,
            flag == KrylovFlag.Converged ? iteration : ii, actual, history, kept, ii);
    }
}
