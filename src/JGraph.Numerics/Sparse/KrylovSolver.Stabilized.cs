namespace JGraph.Numerics.Sparse;

/// <summary>
/// The stabilized biconjugate family: <c>bicgstab</c>, which smooths <c>cgs</c>'s residual with one
/// steepest-descent step per iteration, and <c>bicgstabl</c>, which does it with a degree-<c>ell</c>
/// minimal-residual polynomial instead.
/// </summary>
public static partial class KrylovSolver
{
    /// <summary>
    /// <c>bicgstab</c>. Its iteration counter runs in halves because the method really does produce
    /// two iterates per pass — the "half" iterate after the <c>alpha</c> step and the full one after
    /// the <c>omega</c> step — and either of them can be the one that converges. That is why
    /// <c>iter</c> comes back as 4.5 and not as 4 or 5, and why the residual history has two entries
    /// per iteration.
    /// </summary>
    public static KrylovResult Bicgstab(KrylovOperator a, double[] b, KrylovOptions options)
    {
        int n = b.Length;
        Setup setup = Prepare("bicgstab", n, b, options, rejectToleranceAtEpsilon: false, Math.Min(n, 20));
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
        var history = new double[(2 * setup.MaxIterations) + 1];
        int kept = history.Length;
        history[0] = normr;
        double normrmin = normr;
        double rho = 1;
        double omega = 1;
        double alpha = 0;
        int stagnation = 0;
        int moreSteps = 0;
        int maxMoreSteps = setup.MoreStepsAllowed(10);
        double[] p = Zeros(n);
        double[] v = Zeros(n);
        double iteration = 0;
        int ii;

        for (ii = 1; ii <= setup.MaxIterations; ii++)
        {
            double rho1 = rho;
            rho = Dot(rt, r);
            if (rho == 0 || double.IsInfinity(rho))
            {
                flag = KrylovFlag.ScalarBreakdown;
                kept = (2 * ii) - 1;
                break;
            }

            if (ii == 1)
            {
                p = r;
            }
            else
            {
                double beta = rho / rho1 * (alpha / omega);
                if (beta == 0 || !double.IsFinite(beta))
                {
                    flag = KrylovFlag.ScalarBreakdown;
                    break;
                }

                p = Add(r, beta, Add(p, -omega, v));
            }

            double[]? ph = Precondition(options, p);
            if (ph is null)
            {
                flag = KrylovFlag.IllConditionedPreconditioner;
                kept = (2 * ii) - 1;
                break;
            }

            v = a.Apply(ph);
            double rtv = Dot(rt, v);
            if (rtv == 0 || double.IsInfinity(rtv))
            {
                flag = KrylovFlag.ScalarBreakdown;
                kept = (2 * ii) - 1;
                break;
            }

            alpha = rho / rtv;
            if (double.IsInfinity(alpha))
            {
                flag = KrylovFlag.ScalarBreakdown;
                kept = (2 * ii) - 1;
                break;
            }

            stagnation = Math.Abs(alpha) * Norm(ph) < Epsilon * Norm(x) ? stagnation + 1 : 0;

            double[] xhalf = Add(x, alpha, ph);
            double[] s = Add(r, -alpha, v);
            normr = Norm(s);
            actual = normr;
            history[(2 * ii) - 1] = normr;

            if (normr <= toleranceNorm || stagnation >= MaxStagnationSteps || moreSteps > 0)
            {
                s = Add(b, -1, a.Apply(xhalf));
                actual = Norm(s);
                history[(2 * ii) - 1] = actual;
                if (actual <= toleranceNorm)
                {
                    x = xhalf;
                    flag = KrylovFlag.Converged;
                    iteration = ii - 0.5;
                    kept = 2 * ii;
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
                        setup.Warnings.Add(
                            "Tolerance may not be achievable. Use a larger tolerance.");
                    }

                    flag = KrylovFlag.Stagnated;
                    x = xhalf;
                    kept = 2 * ii;
                    break;
                }
            }

            if (stagnation >= MaxStagnationSteps)
            {
                flag = KrylovFlag.Stagnated;
                kept = 2 * ii;
                break;
            }

            if (actual < normrmin)
            {
                normrmin = actual;
                xmin = xhalf;
                iterationMinimum = ii - 0.5;
            }

            double[]? sh = Precondition(options, s);
            if (sh is null)
            {
                flag = KrylovFlag.IllConditionedPreconditioner;
                kept = 2 * ii;
                break;
            }

            double[] t = a.Apply(sh);
            double tt = Dot(t, t);
            if (tt == 0 || double.IsInfinity(tt))
            {
                flag = KrylovFlag.ScalarBreakdown;
                kept = 2 * ii;
                break;
            }

            omega = Dot(t, s) / tt;
            if (double.IsInfinity(omega))
            {
                flag = KrylovFlag.ScalarBreakdown;
                kept = 2 * ii;
                break;
            }

            stagnation = Math.Abs(omega) * Norm(sh) < Epsilon * Norm(xhalf) ? stagnation + 1 : 0;

            x = Add(xhalf, omega, sh);
            r = Add(s, -omega, t);
            normr = Norm(r);
            actual = normr;
            history[2 * ii] = normr;

            if (normr <= toleranceNorm || stagnation >= MaxStagnationSteps || moreSteps > 0)
            {
                r = Add(b, -1, a.Apply(x));
                actual = Norm(r);
                history[2 * ii] = actual;
                if (actual <= toleranceNorm)
                {
                    flag = KrylovFlag.Converged;
                    iteration = ii;
                    kept = (2 * ii) + 1;
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
                        setup.Warnings.Add(
                            "Tolerance may not be achievable. Use a larger tolerance.");
                    }

                    flag = KrylovFlag.Stagnated;
                    kept = (2 * ii) + 1;
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
                kept = (2 * ii) + 1;
                break;
            }
        }

        ii = Math.Min(ii, setup.MaxIterations);
        return Finish(setup, a, b, flag, x, xmin, iterationMinimum,
            flag == KrylovFlag.Converged ? iteration : ii, actual, history, kept, ii);
    }

    /// <summary>
    /// <c>bicgstabl</c>: <c>ell</c> Bi-CG steps followed by one minimal-residual polynomial over the
    /// <c>ell</c> residuals collected, which is what recovers the convergence <c>bicgstab</c> loses
    /// when the spectrum has a strongly complex part.
    /// </summary>
    /// <remarks>
    /// The iteration counter is the sub-step count divided by <c>2*ell</c>, so with the default
    /// <c>ell = 2</c> it advances in quarters, and the whole method is written against the
    /// <em>preconditioned</em> unknown: <c>xt</c> is an update to <c>M\x</c>, and the answer is
    /// <c>x0 + M\xt</c>. That is why the convergence re-test has to apply the preconditioner before
    /// it can form a residual at all.
    /// </remarks>
    public static KrylovResult Bicgstabl(KrylovOperator a, double[] b, KrylovOptions options)
    {
        int n = b.Length;
        int ell = options.Ell;
        Setup setup = Prepare("bicgstabl", n, b, options, rejectToleranceAtEpsilon: false, Math.Min(n, 20));
        if (setup.RightHandSideNorm == 0)
        {
            return ZeroRightHandSide(setup);
        }

        double[] x0 = setup.Start;
        double toleranceNorm = setup.ToleranceTimesNorm;
        double[] initialResidual = Add(b, -1, a.Apply(x0));
        double normr = Norm(initialResidual);
        double actual = normr;
        if (normr <= toleranceNorm)
        {
            return GoodEnoughStart(setup, normr);
        }

        int flag = KrylovFlag.MaxIterations;
        double[] ukm1 = Zeros(n);
        double[] rk = initialResidual;
        double[] xk = Zeros(n);
        double[] r0 = rk;
        double rho0 = 1;
        double omega = 1;
        double alpha = 0;
        var ut = new double[ell + 1][];
        var rt = new double[ell + 1][];
        for (int i = 0; i <= ell; i++)
        {
            ut[i] = Zeros(n);
            rt[i] = Zeros(n);
        }

        // These five keep MATLAB's own 1-based indices, because the minimal-residual polynomial
        // below is a thicket of jj+1 and jj+2 offsets and shifting them would be an invitation.
        var tau = new double[ell + 2, ell + 2];
        var sigma = new double[ell + 2];
        var gamma = new double[ell + 2];
        var gammap = new double[ell + 2];
        var gammapp = new double[ell + 2];
        int stagnation = 0;
        int moreSteps = 0;
        int maxMoreSteps = setup.MoreStepsAllowed(4 * ell);
        var history = new double[(2 * ell * setup.MaxIterations) + 1];
        int kept = history.Length;
        history[0] = normr;
        double normrmin = normr;
        double[] xmin = Zeros(n);
        double iterationMinimum = 0;
        double iteration = 0;
        double[] xt = Zeros(n);
        double[]? preconditionedXt = null;
        int kk;

        for (kk = 1; kk <= setup.MaxIterations; kk++)
        {
            ut[0] = ukm1;
            rt[0] = rk;
            xt = xk;
            rho0 = -omega * rho0;
            int jj;
            for (jj = 1; jj <= ell; jj++)
            {
                double rho1 = Dot(r0, rt[jj - 1]);
                if (rho0 == 0 || double.IsInfinity(rho0))
                {
                    flag = KrylovFlag.ScalarBreakdown;
                    kept = (2 * ell * (kk - 1)) + jj;
                    break;
                }

                double beta = alpha * rho1 / rho0;
                rho0 = rho1;
                for (int c = 0; c < jj; c++)
                {
                    ut[c] = Add(rt[c], -beta, ut[c]);
                }

                double[]? put = Precondition(options, ut[jj - 1]);
                if (put is null)
                {
                    flag = KrylovFlag.IllConditionedPreconditioner;
                    kept = (2 * ell * (kk - 1)) + jj;
                    break;
                }

                ut[jj] = a.Apply(put);
                double gammaS = Dot(r0, ut[jj]);
                if (gammaS == 0 || double.IsInfinity(gammaS))
                {
                    flag = KrylovFlag.ScalarBreakdown;
                    kept = (2 * ell * (kk - 1)) + jj;
                    break;
                }

                alpha = rho0 / gammaS;
                for (int c = 0; c < jj; c++)
                {
                    rt[c] = Add(rt[c], -alpha, ut[c + 1]);
                }

                double[]? prt = Precondition(options, rt[jj - 1]);
                if (prt is null)
                {
                    flag = KrylovFlag.IllConditionedPreconditioner;
                    kept = (2 * ell * (kk - 1)) + jj;
                    break;
                }

                rt[jj] = a.Apply(prt);
                normr = Norm(rt[0]);
                actual = normr;
                history[(2 * ell * (kk - 1)) + jj] = actual;

                stagnation = Math.Abs(alpha) * Norm(ut[0]) < Epsilon * Norm(xt) ? stagnation + 1 : 0;
                xt = Add(xt, alpha, ut[0]);

                if (normr <= toleranceNorm || stagnation >= MaxStagnationSteps || moreSteps > 0)
                {
                    double[]? pxt = Precondition(options, xt);
                    if (pxt is null)
                    {
                        flag = KrylovFlag.IllConditionedPreconditioner;
                        kept = (2 * ell * (kk - 1)) + jj + 1;
                        break;
                    }

                    preconditionedXt = pxt;
                    rt[0] = Add(b, -1, a.Apply(Add(x0, 1, pxt)));
                    actual = Norm(rt[0]);
                    history[(2 * ell * (kk - 1)) + jj] = actual;
                    if (actual <= toleranceNorm)
                    {
                        flag = KrylovFlag.Converged;
                        iteration = ((2 * ell * (kk - 1)) + jj) / (double)(2 * ell);
                        kept = (2 * ell * (kk - 1)) + jj + 1;
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
                            setup.Warnings.Add(
                                "Tolerance may not be achievable. Use a larger tolerance.");
                        }

                        flag = KrylovFlag.Stagnated;
                        iteration = ((2 * ell * (kk - 1)) + jj) / (double)(2 * ell);
                        kept = (2 * ell * (kk - 1)) + jj + 1;
                        break;
                    }
                }

                if (actual < normrmin)
                {
                    normrmin = actual;
                    xmin = xt;
                    iterationMinimum = ((2 * ell * (kk - 1)) + jj) / (double)(2 * ell);
                }

                if (stagnation >= MaxStagnationSteps)
                {
                    flag = KrylovFlag.Stagnated;
                    break;
                }
            }

            if (flag != KrylovFlag.MaxIterations)
            {
                break;
            }

            for (jj = 2; jj <= ell + 1; jj++)
            {
                for (int i = 2; i <= jj - 1; i++)
                {
                    tau[i, jj] = Dot(rt[jj - 1], rt[i - 1]) / sigma[i];
                    rt[jj - 1] = Add(rt[jj - 1], -tau[i, jj], rt[i - 1]);
                }

                sigma[jj] = Dot(rt[jj - 1], rt[jj - 1]);
                if (sigma[jj] == 0 || double.IsInfinity(sigma[jj]))
                {
                    flag = KrylovFlag.ScalarBreakdown;
                    kept = (2 * ell * (kk - 1)) + ell + 1;
                    break;
                }

                gammap[jj] = Dot(rt[0], rt[jj - 1]) / sigma[jj];
            }

            if (flag == KrylovFlag.ScalarBreakdown)
            {
                break;
            }

            gamma[ell + 1] = gammap[ell + 1];
            omega = gamma[ell + 1];
            for (jj = ell; jj >= 2; jj--)
            {
                double sum = 0;
                for (int c = jj + 1; c <= ell + 1; c++)
                {
                    sum += tau[jj, c] * gamma[c];
                }

                gamma[jj] = gammap[jj] - sum;
            }

            for (jj = 2; jj <= ell; jj++)
            {
                double sum = 0;
                for (int c = jj + 1; c <= ell; c++)
                {
                    sum += tau[jj, c] * gamma[c + 1];
                }

                gammapp[jj] = gamma[jj + 1] + sum;
            }

            stagnation = Math.Abs(gamma[2]) * Norm(rt[0]) < Epsilon * Norm(xt) ? stagnation + 1 : 0;
            xt = Add(xt, gamma[2], rt[0]);
            rt[0] = Add(rt[0], -gammap[ell + 1], rt[ell]);
            ut[0] = Add(ut[0], -gamma[ell + 1], ut[ell]);
            normr = Norm(rt[0]);
            actual = normr;
            history[(2 * ell * (kk - 1)) + ell + 1] = actual;

            if (normr <= toleranceNorm || stagnation >= MaxStagnationSteps || moreSteps > 0)
            {
                double[]? pxt = Precondition(options, xt);
                if (pxt is null)
                {
                    flag = KrylovFlag.IllConditionedPreconditioner;
                    kept = (2 * ell * (kk - 1)) + ell + 2;
                    break;
                }

                preconditionedXt = pxt;
                rt[0] = Add(b, -1, a.Apply(Add(x0, 1, pxt)));
                actual = Norm(rt[0]);
                history[(2 * ell * (kk - 1)) + ell + 1] = actual;
                if (actual <= toleranceNorm)
                {
                    flag = KrylovFlag.Converged;
                    iteration = ((2 * ell * (kk - 1)) + ell + 1) / (double)(2 * ell);
                    kept = (2 * ell * (kk - 1)) + ell + 2;
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
                        setup.Warnings.Add(
                            "Tolerance may not be achievable. Use a larger tolerance.");
                    }

                    flag = KrylovFlag.Stagnated;
                    iteration = ((2 * ell * (kk - 1)) + ell + 1) / (double)(2 * ell);
                    kept = (2 * ell * (kk - 1)) + ell + 2;
                    break;
                }
            }

            if (actual < normrmin)
            {
                normrmin = actual;
                xmin = xt;
                iterationMinimum = ((2 * ell * (kk - 1)) + ell + 1) / (double)(2 * ell);
            }

            if (stagnation >= MaxStagnationSteps)
            {
                flag = KrylovFlag.Stagnated;
                break;
            }

            bool broke = false;
            for (jj = 2; jj <= ell; jj++)
            {
                ut[0] = Add(ut[0], -gamma[jj], ut[jj - 1]);
                stagnation = Math.Abs(gammapp[jj]) * Norm(rt[jj - 1]) < Epsilon * Norm(xt)
                    ? stagnation + 1
                    : 0;
                xt = Add(xt, gammapp[jj], rt[jj - 1]);
                rt[0] = Add(rt[0], -gammap[jj], rt[jj - 1]);
                normr = Norm(rt[0]);
                actual = normr;
                history[(2 * ell * (kk - 1)) + ell + jj] = actual;

                if (normr <= toleranceNorm || stagnation >= MaxStagnationSteps || moreSteps > 0)
                {
                    double[]? pxt = Precondition(options, xt);
                    if (pxt is null)
                    {
                        flag = KrylovFlag.IllConditionedPreconditioner;
                        kept = (2 * ell * (kk - 1)) + ell + jj;
                        broke = true;
                        break;
                    }

                    preconditionedXt = pxt;
                    rt[0] = Add(b, -1, a.Apply(Add(x0, 1, pxt)));
                    actual = Norm(rt[0]);
                    history[(2 * ell * (kk - 1)) + ell + jj] = actual;
                    if (actual <= toleranceNorm)
                    {
                        flag = KrylovFlag.Converged;
                        iteration = ((2 * ell * (kk - 1)) + ell + jj) / (double)(2 * ell);
                        kept = (2 * ell * (kk - 1)) + ell + jj + 1;
                        broke = true;
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
                            setup.Warnings.Add(
                                "Tolerance may not be achievable. Use a larger tolerance.");
                        }

                        flag = KrylovFlag.Stagnated;
                        iteration = ((2 * ell * (kk - 1)) + ell + jj) / (double)(2 * ell);
                        kept = (2 * ell * (kk - 1)) + ell + jj + 1;
                        broke = true;
                        break;
                    }
                }

                if (actual < normrmin)
                {
                    normrmin = actual;
                    xmin = xt;
                    iterationMinimum = ((2 * ell * (kk - 1)) + ell + jj) / (double)(2 * ell);
                }

                if (stagnation >= MaxStagnationSteps)
                {
                    flag = KrylovFlag.Stagnated;
                    broke = true;
                    break;
                }
            }

            if (broke || flag is KrylovFlag.Converged or KrylovFlag.IllConditionedPreconditioner
                or KrylovFlag.Stagnated)
            {
                break;
            }

            ukm1 = ut[0];
            rk = rt[0];
            xk = xt;
        }

        kk = Math.Min(kk, setup.MaxIterations);
        if (kk == 0)
        {
            xt = xmin;
        }

        if (flag == KrylovFlag.Converged)
        {
            return new KrylovResult(Add(x0, 1, preconditionedXt ?? xt), flag,
                actual / setup.RightHandSideNorm, [iteration], Truncate(history, kept))
            {
                Warnings = setup.Warnings,
                StoppedAt = [kk],
            };
        }

        double[]? minimumUpdate = Precondition(options, xmin);
        double[]? lastUpdate = Precondition(options, xt);
        if (minimumUpdate is null || lastUpdate is null)
        {
            return new KrylovResult(xmin, KrylovFlag.IllConditionedPreconditioner,
                actual / setup.RightHandSideNorm, [iterationMinimum], Truncate(history, kept))
            {
                Warnings = setup.Warnings,
                StoppedAt = [kk],
            };
        }

        double[] minimumResidual = Add(b, -1, a.Apply(Add(x0, 1, minimumUpdate)));
        double[] lastResidual = Add(b, -1, a.Apply(Add(x0, 1, lastUpdate)));
        return Norm(minimumResidual) <= Norm(lastResidual)
            ? new KrylovResult(Add(x0, 1, minimumUpdate), flag,
                Norm(minimumResidual) / setup.RightHandSideNorm, [iterationMinimum], Truncate(history, kept))
            { Warnings = setup.Warnings, StoppedAt = [kk] }
            : new KrylovResult(Add(x0, 1, lastUpdate), flag,
                Norm(lastResidual) / setup.RightHandSideNorm, [kk], Truncate(history, kept))
            { Warnings = setup.Warnings, StoppedAt = [kk] };
    }
}
