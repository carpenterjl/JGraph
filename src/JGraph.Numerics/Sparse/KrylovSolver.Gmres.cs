namespace JGraph.Numerics.Sparse;

/// <summary>
/// The generalized minimal residual method, restarted or not.
/// </summary>
public static partial class KrylovSolver
{
    /// <summary>
    /// <c>gmres</c>. Two things set it apart from everything else in the family, and both show up
    /// in what a script can check.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Its iteration count is a pair. The outer number is the restart, the inner one the Arnoldi
    /// step within it, and <c>gmres(A, b, 20, tol, 5)</c> can take up to a hundred matrix products
    /// while reporting an <c>iter</c> whose largest possible value is <c>[5 20]</c>. Without a
    /// restart length there is one outer iteration and the pair is <c>[1 k]</c>.
    /// </para>
    /// <para>
    /// Its relative residual is measured against the <em>preconditioned</em> right-hand side,
    /// <c>norm(M\b)</c>, not against <c>norm(b)</c> — alone in this family. A preconditioned
    /// <c>gmres</c> and a preconditioned <c>pcg</c> that stop at the same iterate therefore report
    /// different relative residuals, and neither is wrong.
    /// </para>
    /// <para>
    /// The Arnoldi basis is built by Householder reflectors rather than by modified Gram–Schmidt,
    /// which is what MATLAB's <c>gmres.m</c> does and is the reason its residual estimates stay
    /// trustworthy at tolerances near <c>eps</c>: the reflectors are orthogonal to working accuracy
    /// whatever the conditioning, where a Gram–Schmidt basis quietly loses orthogonality and takes
    /// the residual estimate with it.
    /// </para>
    /// </remarks>
    public static KrylovResult Gmres(KrylovOperator a, double[] b, KrylovOptions options)
    {
        int n = b.Length;
        bool restarted = options.Restart is not null && options.Restart.Value != n;
        int restart = restarted ? Math.Max(options.Restart!.Value, 0) : n;
        int defaultMaxIterations = restarted
            ? Math.Min((int)Math.Ceiling(n / (double)restart), 10)
            : Math.Min(n, 10);
        Setup setup = Prepare("gmres", n, b, options, rejectToleranceAtEpsilon: false, defaultMaxIterations);
        var warnings = setup.Warnings;

        int outer;
        int inner;
        int maxit = setup.MaxIterations;
        if (restarted)
        {
            outer = maxit;
            if (restart > n)
            {
                warnings.Add($"Number of inner iterations {restart} is greater than the size of the "
                    + $"system {n}; reducing it to {n}.");
                restart = n;
            }

            inner = restart;
        }
        else
        {
            outer = 1;
            if (maxit > n)
            {
                warnings.Add($"Number of iterations {maxit} is greater than the size of the system {n}; "
                    + $"reducing it to {n}.");
                maxit = n;
            }

            inner = maxit;
        }

        if (setup.RightHandSideNorm == 0)
        {
            return ZeroRightHandSide(setup, 2);
        }

        int flag = KrylovFlag.MaxIterations;
        double[] x = setup.Start;
        double[] xmin = x;
        int imin = 0;
        int jmin = 0;
        double toleranceNorm = setup.ToleranceTimesNorm;
        int evalxm = 0;
        int stagnation = 0;
        int moreSteps = 0;
        int maxMoreSteps = setup.MoreStepsAllowed(5);
        bool minUpdated = false;

        bool startIsZero = Norm(x) == 0;
        double[] r = Add(b, -1, a.Apply(x));
        double normr = Norm(r);
        if (normr <= toleranceNorm)
        {
            return GoodEnoughStart(setup, normr, 2);
        }

        double[] minvB = b;
        if (options.Left is not null)
        {
            r = options.Left.Apply(r);
            minvB = startIsZero ? r : options.Left.Apply(b);
            if (!AllFinite(r) || !AllFinite(minvB))
            {
                return new KrylovResult(xmin, KrylovFlag.IllConditionedPreconditioner,
                    normr / setup.RightHandSideNorm, [0, 0], [normr])
                { Warnings = warnings };
            }
        }

        if (options.Right is not null)
        {
            r = options.Right.Apply(r);
            minvB = startIsZero ? r : options.Right.Apply(minvB);
            if (!AllFinite(r) || !AllFinite(minvB))
            {
                return new KrylovResult(xmin, KrylovFlag.IllConditionedPreconditioner,
                    normr / setup.RightHandSideNorm, [0, 0], [normr])
                { Warnings = warnings };
            }
        }

        normr = Norm(r);
        double normMinvB = Norm(minvB);
        toleranceNorm = setup.Tolerance * normMinvB;
        if (normr <= toleranceNorm)
        {
            return new KrylovResult((double[])x.Clone(), KrylovFlag.Converged, normr / normMinvB,
                [0, 0], [normMinvB])
            { Warnings = warnings };
        }

        var history = new double[(inner * outer) + 1];
        history[0] = normr;
        double normrmin = normr;
        double normrAct = normr;

        // MATLAB's own layout, one-based so the index arithmetic reads the way its source does.
        var rotations = new double[3, inner + 1];
        var basis = new double[inner + 2][];
        var upper = new double[inner + 1, inner + 1];
        var w = new double[inner + 2];
        for (int i = 0; i < basis.Length; i++)
        {
            basis[i] = Zeros(n);
        }

        double[] xm = x;
        int outiter = 0;
        int initer = 0;
        int[] iterationOut = [0, 0];

        for (outiter = 1; outiter <= outer; outiter++)
        {
            double[] u = (double[])r.Clone();
            normr = Norm(r);
            double beta = ScalarSign(r[0]) * normr;
            u[0] += beta;
            u = Scale(1 / Norm(u), u);
            basis[1] = u;
            w[1] = -beta;

            initer = 0;
            for (initer = 1; initer <= inner; initer++)
            {
                double[] v = Scale(-2 * u[initer - 1], u);
                v[initer - 1] += 1;
                for (int k = initer - 1; k >= 1; k--)
                {
                    v = Add(v, -2 * Dot(basis[k], v), basis[k]);
                }

                v = Scale(1 / Norm(v), v);
                v = a.Apply(v);
                double[]? preconditioned = Precondition(options, v);
                if (preconditioned is null)
                {
                    flag = KrylovFlag.IllConditionedPreconditioner;
                    break;
                }

                v = preconditioned;
                for (int k = 1; k <= initer; k++)
                {
                    v = Add(v, -2 * Dot(basis[k], v), basis[k]);
                }

                if (initer != n)
                {
                    u = (double[])v.Clone();
                    for (int i = 0; i < initer; i++)
                    {
                        u[i] = 0;
                    }

                    double alpha = Norm(u);
                    if (alpha != 0)
                    {
                        alpha = ScalarSign(v[initer]) * alpha;
                        u[initer] += alpha;
                        u = Scale(1 / Norm(u), u);
                        basis[initer + 1] = u;
                        for (int i = initer + 1; i < n; i++)
                        {
                            v[i] = 0;
                        }

                        v[initer] = -alpha;
                    }
                }

                for (int colJ = 1; colJ <= initer - 1; colJ++)
                {
                    double before = v[colJ - 1];
                    v[colJ - 1] = (rotations[1, colJ] * v[colJ - 1]) + (rotations[2, colJ] * v[colJ]);
                    v[colJ] = (-rotations[2, colJ] * before) + (rotations[1, colJ] * v[colJ]);
                }

                if (initer != n)
                {
                    double rho = Norm([v[initer - 1], v[initer]]);
                    rotations[1, initer] = v[initer - 1] / rho;
                    rotations[2, initer] = v[initer] / rho;
                    w[initer + 1] = -rotations[2, initer] * w[initer];
                    w[initer] = rotations[1, initer] * w[initer];
                    v[initer - 1] = rho;
                    v[initer] = 0;
                }

                for (int i = 1; i <= inner; i++)
                {
                    upper[i, initer] = i <= n ? v[i - 1] : 0;
                }

                normr = Math.Abs(w[initer + 1]);
                history[((outiter - 1) * inner) + initer] = normr;
                normrAct = normr;

                if (normr <= toleranceNorm || stagnation >= MaxStagnationSteps || moreSteps > 0)
                {
                    if (evalxm == 0)
                    {
                        double[] ytmp = SolveUpper(upper, w, initer);
                        double[] additive = BackTransform(basis, ytmp, initer, n);
                        stagnation = Norm(additive) < Epsilon * Norm(x) ? stagnation + 1 : 0;
                        xm = Add(x, 1, additive);
                        evalxm = 1;
                    }
                    else
                    {
                        double last = w[initer] / upper[initer, initer];
                        var addvc = new double[initer];
                        if (initer > 1)
                        {
                            var column = new double[initer - 1];
                            for (int i = 1; i <= initer - 1; i++)
                            {
                                column[i - 1] = upper[i, initer];
                            }

                            double[] partial = SolveUpperColumn(upper, column, initer - 1);
                            for (int i = 0; i < initer - 1; i++)
                            {
                                addvc[i] = -partial[i] * last;
                            }
                        }

                        addvc[initer - 1] = last;
                        stagnation = Norm(addvc) < Epsilon * Norm(xm) ? stagnation + 1 : 0;
                        double[] additive = BackTransform(basis, addvc, initer, n);
                        xm = Add(xm, 1, additive);
                    }

                    r = Add(b, -1, a.Apply(xm));
                    normrAct = Norm(r);
                    if (normrAct <= setup.Tolerance * setup.RightHandSideNorm)
                    {
                        x = xm;
                        flag = KrylovFlag.Converged;
                        iterationOut = [outiter, initer];
                        break;
                    }

                    double[]? minvR = Precondition(options, r);
                    if (minvR is null)
                    {
                        flag = KrylovFlag.IllConditionedPreconditioner;
                        break;
                    }

                    normrAct = Norm(minvR);
                    history[((outiter - 1) * inner) + initer] = normrAct;

                    if (normrAct <= normrmin)
                    {
                        normrmin = normrAct;
                        imin = outiter;
                        jmin = initer;
                        xmin = xm;
                        minUpdated = true;
                    }

                    if (normrAct <= toleranceNorm)
                    {
                        x = xm;
                        flag = KrylovFlag.Converged;
                        iterationOut = [outiter, initer];
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
                            warnings.Add(
                                "Tolerance may not be achievable. Use a larger tolerance.");
                        }

                        flag = KrylovFlag.Stagnated;
                        iterationOut = [outiter, initer];
                        break;
                    }
                }

                if (normrAct <= normrmin)
                {
                    normrmin = normrAct;
                    imin = outiter;
                    jmin = initer;
                    minUpdated = true;
                }

                if (stagnation >= MaxStagnationSteps)
                {
                    flag = KrylovFlag.Stagnated;
                    break;
                }
            }

            initer = Math.Min(initer, inner);
            evalxm = 0;

            if (flag != KrylovFlag.Converged)
            {
                int idx = minUpdated ? jmin : initer;
                if (idx > 0)
                {
                    double[] y = SolveUpper(upper, w, idx);
                    x = Add(x, 1, BackTransform(basis, y, idx, n));
                }

                xmin = x;
                r = Add(b, -1, a.Apply(x));
                double[]? minvR = Precondition(options, r);
                if (minvR is null)
                {
                    flag = KrylovFlag.IllConditionedPreconditioner;
                    break;
                }

                normrAct = Norm(minvR);
                r = minvR;
            }

            if (normrAct <= normrmin)
            {
                xmin = x;
                normrmin = normrAct;
                imin = outiter;
                jmin = initer;
            }

            if (flag == KrylovFlag.Stagnated)
            {
                break;
            }

            if (normrAct <= toleranceNorm)
            {
                flag = KrylovFlag.Converged;
                iterationOut = [outiter, initer];
                break;
            }

            minUpdated = false;
        }

        outiter = Math.Min(outiter, outer);

        double[] answer;
        double relative;
        if (flag == KrylovFlag.Converged)
        {
            answer = x;
            relative = normrAct / normMinvB;
        }
        else
        {
            answer = xmin;
            iterationOut = [imin, jmin];
            relative = normrAct / normMinvB;
        }

        int kept = (Math.Max(outiter - 1, 0) * inner) + initer + 1;
        if (flag == KrylovFlag.IllConditionedPreconditioner && initer != 0)
        {
            kept--;
        }

        return new KrylovResult(answer, flag, relative, [iterationOut[0], iterationOut[1]],
            Truncate(history, kept))
        {
            Warnings = warnings,

            // Restarted, the message names both counters; unrestarted there is only ever one
            // outer pass, and MATLAB prints the inner number alone.
            StoppedAt = restarted ? [outiter, initer] : [initer],
        };
    }

    /// <summary>MATLAB's <c>scalarsign</c>: the sign, with zero counted as positive.</summary>
    private static double ScalarSign(double d) => d < 0 ? -1 : 1;

    /// <summary>The upper-triangular solve <c>R(1:k,1:k) \ w(1:k)</c> over the one-based store.</summary>
    private static double[] SolveUpper(double[,] upper, double[] w, int k)
    {
        var y = new double[k];
        for (int i = k; i >= 1; i--)
        {
            double sum = w[i];
            for (int j = i + 1; j <= k; j++)
            {
                sum -= upper[i, j] * y[j - 1];
            }

            y[i - 1] = sum / upper[i, i];
        }

        return y;
    }

    /// <summary>The same solve against an arbitrary right-hand side already in zero-based form.</summary>
    private static double[] SolveUpperColumn(double[,] upper, double[] rhs, int k)
    {
        var y = new double[k];
        for (int i = k; i >= 1; i--)
        {
            double sum = rhs[i - 1];
            for (int j = i + 1; j <= k; j++)
            {
                sum -= upper[i, j] * y[j - 1];
            }

            y[i - 1] = sum / upper[i, i];
        }

        return y;
    }

    /// <summary>
    /// The Krylov coefficients carried back through the Householder reflectors, which is how the
    /// update to <c>x</c> is recovered without ever forming the Arnoldi basis explicitly.
    /// </summary>
    private static double[] BackTransform(double[][] basis, double[] y, int k, int n)
    {
        double[] additive = Scale(-2 * y[k - 1] * basis[k][k - 1], basis[k]);
        additive[k - 1] += y[k - 1];
        for (int j = k - 1; j >= 1; j--)
        {
            additive[j - 1] += y[j - 1];
            additive = Add(additive, -2 * Dot(basis[j], additive), basis[j]);
        }

        _ = n;
        return additive;
    }
}
