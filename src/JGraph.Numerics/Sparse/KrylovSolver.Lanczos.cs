namespace JGraph.Numerics.Sparse;

/// <summary>
/// The two solvers built on the symmetric Lanczos process rather than on a conjugate-direction
/// recurrence: <c>minres</c>, which minimises the residual over the Krylov space, and
/// <c>symmlq</c>, which solves the projected tridiagonal system in LQ form.
/// </summary>
/// <remarks>
/// <para>
/// Both accept an indefinite matrix, which is the reason they exist, and both insist the
/// <em>preconditioner</em> be positive definite — a negative <c>v'*(M\v)</c> is flag 5, a value no
/// other solver here can return. The Lanczos vectors are locally reorthogonalized once at the first
/// step, exactly where MATLAB's sources do it.
/// </para>
/// <para>
/// Their residual norm has two spellings. Without a preconditioner it is read straight off the
/// recurrence — <c>abs(snprod)</c> for <c>minres</c> — and no residual vector is formed at all.
/// With one, the residual vector is carried along and its norm taken. The two differ in the last
/// bits, so a preconditioned run and an unpreconditioned run of the same problem stop at different
/// iterations for reasons that have nothing to do with the preconditioner's quality.
/// </para>
/// </remarks>
public static partial class KrylovSolver
{
    /// <summary>The minimum-residual method for a symmetric, possibly indefinite, matrix.</summary>
    public static KrylovResult Minres(KrylovOperator a, double[] b, KrylovOptions options)
    {
        int n = b.Length;
        // minres floors maxit at 1 where the rest floor it at 0: its first Lanczos step happens
        // before the loop, so "no iterations" is not a state it can be in.
        Setup setup = Prepare("minres", n, b, options, rejectToleranceAtEpsilon: false, Math.Min(n, 20),
            leastMaxIterations: 1);
        if (setup.RightHandSideNorm == 0)
        {
            return ZeroRightHandSide(setup);
        }

        bool existM = options.Left is not null || options.Right is not null;
        double toleranceNorm = setup.ToleranceTimesNorm;
        double[] x = setup.Start;
        double[] r = Add(b, -1, a.Apply(x));
        double normr = Norm(r);
        if (normr <= toleranceNorm)
        {
            return GoodEnoughStart(setup, normr);
        }

        var history = new double[setup.MaxIterations + 1];
        var cgHistory = new double[setup.MaxIterations + 2];
        history[0] = normr;
        cgHistory[0] = normr;

        KrylovResult Stop(int flag) =>
            new((double[])x.Clone(), flag, normr / setup.RightHandSideNorm, [0], [normr])
            {
                ConjugateGradientNorms = [normr],
                Warnings = setup.Warnings,
            };

        double[] vold = r;
        double[]? v = Precondition(options, vold);
        if (v is null)
        {
            return Stop(KrylovFlag.IllConditionedPreconditioner);
        }

        double beta1 = Dot(vold, v);
        if (beta1 <= 0)
        {
            return Stop(KrylovFlag.PreconditionerNotSpd);
        }

        beta1 = Math.Sqrt(beta1);
        double snprod = beta1;
        double[] vv = Scale(1 / beta1, v);
        v = a.Apply(vv);
        double[] amvv = v;
        double alpha = Dot(vv, v);
        v = Add(v, -(alpha / beta1), vold);

        // One local reorthogonalization, where MATLAB's minres.m puts it: the first Lanczos vector
        // is the one whose loss of orthogonality costs the most later.
        double numer = Dot(vv, v);
        double denom = Dot(vv, vv);
        v = Add(v, -(numer / denom), vv);
        double[] volder = vold;
        vold = v;

        v = Precondition(options, vold);
        if (v is null)
        {
            return Stop(KrylovFlag.IllConditionedPreconditioner);
        }

        double betaold = beta1;
        double beta = Dot(vold, v);
        if (beta < 0)
        {
            return Stop(KrylovFlag.PreconditionerNotSpd);
        }

        int iteration = 1;
        beta = Math.Sqrt(beta);
        double gammabar = alpha;
        double epsilon = 0;
        double deltabar = beta;
        double gamma = Math.Sqrt((gammabar * gammabar) + (beta * beta));
        double[] mold = Zeros(n);
        double[] amold = mold;
        double[] m = Scale(1 / gamma, vv);
        double[] am = Scale(1 / gamma, amvv);
        double cs = gammabar / gamma;
        double sn = beta / gamma;
        x = Add(x, snprod * cs, m);
        double snprodold = snprod;
        snprod *= sn;

        if (existM)
        {
            r = Add(r, -(snprodold * cs), am);
            normr = Norm(r);
        }
        else
        {
            normr = Math.Abs(snprod);
        }

        history[1] = normr;
        cgHistory[1] = cs == 0 ? double.PositiveInfinity : Norm(Add(r, -(snprod * (sn / cs)), am));

        if (normr <= toleranceNorm)
        {
            return new KrylovResult((double[])x.Clone(), KrylovFlag.Converged,
                normr / setup.RightHandSideNorm, [1], Truncate(history, 2))
            {
                ConjugateGradientNorms = Truncate(cgHistory, 2),
                Warnings = setup.Warnings,
            };
        }

        int flagOut = KrylovFlag.MaxIterations;
        double normrmin = normr;
        double[] xmin = x;
        double iterationMinimum = 0;
        int stagnation = 0;
        int moreSteps = 0;
        int maxMoreSteps = setup.MoreStepsAllowed(5);
        int ii;
        int loopIteration = iteration;

        for (ii = 2; ii <= setup.MaxIterations; ii++)
        {
            vv = Scale(1 / beta, v!);
            v = a.Apply(vv);
            double[] amolder = amold;
            amold = am;
            am = v;
            v = Add(v, -(beta / betaold), volder);
            alpha = Dot(vv, v);
            v = Add(v, -(alpha / beta), vold);
            volder = vold;
            vold = v;

            v = Precondition(options, vold);
            if (v is null)
            {
                flagOut = KrylovFlag.IllConditionedPreconditioner;
                break;
            }

            betaold = beta;
            beta = Dot(vold, v);
            if (beta < 0)
            {
                flagOut = KrylovFlag.PreconditionerNotSpd;
                break;
            }

            beta = Math.Sqrt(beta);
            double delta = (cs * deltabar) + (sn * alpha);
            double[] molder = mold;
            mold = m;
            m = Add(Add(vv, -delta, mold), -epsilon, molder);
            am = Add(Add(am, -delta, amold), -epsilon, amolder);
            gammabar = (sn * deltabar) - (cs * alpha);
            epsilon = sn * beta;
            deltabar = -cs * beta;
            gamma = Math.Sqrt((gammabar * gammabar) + (beta * beta));
            m = Scale(1 / gamma, m);
            am = Scale(1 / gamma, am);
            cs = gammabar / gamma;
            sn = beta / gamma;

            stagnation = snprod * cs == 0 || Math.Abs(snprod * cs) * Norm(m) < Epsilon * Norm(x)
                ? stagnation + 1
                : 0;

            x = Add(x, snprod * cs, m);
            snprodold = snprod;
            snprod *= sn;

            if (existM)
            {
                r = Add(r, -(snprodold * cs), am);
                normr = Norm(r);
            }
            else
            {
                normr = Math.Abs(snprod);
            }

            history[ii] = normr;
            cgHistory[ii + 1] = cs == 0 ? double.PositiveInfinity : Norm(Add(r, -(snprod * (sn / cs)), am));

            if (normr <= toleranceNorm || stagnation >= MaxStagnationSteps || moreSteps > 0)
            {
                r = Add(b, -1, a.Apply(x));
                normr = Norm(r);
                history[ii] = normr;
                if (normr <= toleranceNorm)
                {
                    flagOut = KrylovFlag.Converged;
                    loopIteration = ii;
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

                    flagOut = KrylovFlag.Stagnated;
                    loopIteration = ii;
                    break;
                }
            }

            if (normr < normrmin)
            {
                normrmin = normr;
                xmin = x;
                iterationMinimum = ii;
            }

            if (stagnation >= MaxStagnationSteps)
            {
                flagOut = KrylovFlag.Stagnated;
                break;
            }
        }

        ii = setup.MaxIterations < 2 ? 1 : Math.Min(ii, setup.MaxIterations);

        double relative;
        double[] answer = x;
        double[] iterationOut;
        if (flagOut == KrylovFlag.Converged)
        {
            relative = normr / setup.RightHandSideNorm;
            iterationOut = [loopIteration];
        }
        else
        {
            double minimumNorm = Norm(Add(b, -1, a.Apply(xmin)));
            if (minimumNorm <= normr)
            {
                answer = xmin;
                iterationOut = [iterationMinimum];
                relative = minimumNorm / setup.RightHandSideNorm;
            }
            else
            {
                iterationOut = [ii];
                relative = normr / setup.RightHandSideNorm;
            }
        }

        bool full = flagOut is <= KrylovFlag.MaxIterations or KrylovFlag.Stagnated;
        return new KrylovResult(answer, flagOut, relative, iterationOut,
            Truncate(history, full ? ii + 1 : ii))
        {
            ConjugateGradientNorms = Truncate(cgHistory, full ? ii + 2 : ii + 1),
            Warnings = setup.Warnings,
            StoppedAt = [ii],
        };
    }

    /// <summary>
    /// <c>symmlq</c>. The residual norms it reports are estimates read off the recurrence rather
    /// than lengths of a vector, and its convergence test watches two sequences at once: the SYMMLQ
    /// iterate's residual and the CG iterate's, either of which may cross the tolerance first. That
    /// second test is what makes the method able to return before the loop has run at all.
    /// </summary>
    public static KrylovResult Symmlq(KrylovOperator a, double[] b, KrylovOptions options)
    {
        int n = b.Length;
        Setup setup = Prepare("symmlq", n, b, options, rejectToleranceAtEpsilon: false, Math.Min(n, 20));
        if (setup.RightHandSideNorm == 0)
        {
            return ZeroRightHandSide(setup);
        }

        bool existM = options.Left is not null || options.Right is not null;
        double toleranceNorm = setup.ToleranceTimesNorm;
        double[] x = setup.Start;
        double[] r = Add(b, -1, a.Apply(x));
        double normr = Norm(r);
        if (normr <= toleranceNorm)
        {
            return GoodEnoughStart(setup, normr);
        }

        var history = new double[setup.MaxIterations + 1];
        var cgHistory = new double[setup.MaxIterations + 2];
        history[0] = normr;
        cgHistory[0] = normr;

        KrylovResult Stop(int flag) =>
            new((double[])x.Clone(), flag, normr / setup.RightHandSideNorm, [0], [normr])
            {
                ConjugateGradientNorms = [normr],
                Warnings = setup.Warnings,
            };

        double[] vold = r;
        double[]? v = Precondition(options, vold);
        if (v is null)
        {
            return Stop(KrylovFlag.IllConditionedPreconditioner);
        }

        double beta1 = Dot(vold, v);
        if (beta1 <= 0)
        {
            return Stop(KrylovFlag.PreconditionerNotSpd);
        }

        beta1 = Math.Sqrt(beta1);
        double[] vv = Scale(1 / beta1, v);
        double[] wbar = vv;
        v = a.Apply(vv);
        double alpha = Dot(vv, v);
        v = Add(v, -(alpha / beta1), vold);

        double numer = Dot(vv, v);
        double denom = Dot(vv, vv);
        v = Add(v, -(numer / denom), vv);
        double[] volder = vold;
        vold = v;

        v = Precondition(options, vold);
        if (v is null)
        {
            return Stop(KrylovFlag.IllConditionedPreconditioner);
        }

        double betaold = beta1;
        double beta = Dot(vold, v);
        if (beta < 0)
        {
            return Stop(KrylovFlag.PreconditionerNotSpd);
        }

        beta = Math.Sqrt(beta);
        double gammabar = alpha;
        double deltabar = beta;
        double gamma = Math.Sqrt((gammabar * gammabar) + (beta * beta));
        double cs = gammabar / gamma;
        double sn = beta / gamma;
        double zeta = beta1 / gamma;
        double epsilonzeta = 0;
        double normrcgcs = Math.Abs(beta1 * sn);
        double normrcg = existM
            ? Norm(Scale(beta1 / gammabar, vold))
            : cs == 0 ? double.PositiveInfinity : normrcgcs / Math.Abs(cs);
        cgHistory[1] = normrcg;

        int flagOut = KrylovFlag.MaxIterations;
        double loopIteration = 0;
        int nits;
        if (cgHistory[1] <= toleranceNorm)
        {
            nits = 0;
            x = Add(x, zeta / cs, wbar);
            flagOut = KrylovFlag.Converged;
            loopIteration = 0;
        }
        else
        {
            nits = setup.MaxIterations;
        }

        double normrAct = normr;
        double normrcgAct = normrcg;
        double normrmin = normr;
        double[] xmin = x;
        double iterationMinimum = 0;
        int stagnation = 0;
        int moreSteps = 0;
        int maxMoreSteps = setup.MoreStepsAllowed(5);
        int ii = 0;

        for (ii = 1; ii <= nits; ii++)
        {
            vv = Scale(1 / beta, v!);
            double[] w = Add(Scale(cs, wbar), sn, vv);
            stagnation = Math.Abs(zeta) * Norm(w) < Epsilon * Norm(x) ? stagnation + 1 : 0;
            x = Add(x, zeta, w);
            wbar = Add(Scale(sn, wbar), -cs, vv);
            v = a.Apply(vv);
            v = Add(v, -(beta / betaold), volder);
            alpha = Dot(vv, v);
            v = Add(v, -(alpha / beta), vold);
            volder = vold;
            vold = v;

            v = Precondition(options, vold);
            if (v is null)
            {
                flagOut = KrylovFlag.IllConditionedPreconditioner;
                break;
            }

            betaold = beta;
            beta = Dot(vold, v);
            if (beta < 0)
            {
                flagOut = KrylovFlag.PreconditionerNotSpd;
                break;
            }

            beta = Math.Sqrt(beta);
            double delta = (cs * deltabar) + (sn * alpha);
            double deltazeta = -delta * zeta;
            gammabar = (sn * deltabar) - (cs * alpha);
            double epsilon = sn * beta;
            deltabar = -cs * beta;
            gamma = Math.Sqrt((gammabar * gammabar) + (beta * beta));
            double csold = cs;
            double snzeta = sn * zeta;
            cs = gammabar / gamma;
            sn = beta / gamma;
            double epszdelz = epsilonzeta + deltazeta;
            epsilonzeta = -epsilon * zeta;
            zeta = epszdelz / gamma;

            if (existM)
            {
                normr = Norm(Add(Scale(zeta * gamma / betaold, volder), -snzeta, vold));
                normrcg = Norm(Scale((csold * epszdelz / gammabar) - snzeta, vold));
            }
            else
            {
                normr = Math.Sqrt((epszdelz * epszdelz) + (epsilonzeta * epsilonzeta));
                normrcgcs *= Math.Abs(sn);
                normrcg = cs == 0 ? double.PositiveInfinity : normrcgcs / Math.Abs(cs);
            }

            normrAct = normr;
            normrcgAct = normrcg;
            history[ii] = normr;
            cgHistory[ii + 1] = normrcg;

            if (normr <= toleranceNorm || stagnation >= MaxStagnationSteps || moreSteps > 0)
            {
                r = Add(b, -1, a.Apply(x));
                normrAct = Norm(r);
                history[ii] = normrAct;
                if (normrAct <= toleranceNorm)
                {
                    flagOut = KrylovFlag.Converged;
                    loopIteration = ii;
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

                    flagOut = KrylovFlag.Stagnated;
                    loopIteration = ii;
                    break;
                }
            }

            if (normrcg <= toleranceNorm || stagnation >= MaxStagnationSteps || moreSteps > 0)
            {
                double[] xcg = Add(x, epszdelz / gammabar, wbar);
                normrcgAct = Norm(Add(b, -1, a.Apply(xcg)));
                if (normrcgAct <= toleranceNorm)
                {
                    x = xcg;
                    flagOut = KrylovFlag.Converged;
                    loopIteration = ii;
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

                    flagOut = KrylovFlag.Stagnated;
                    loopIteration = ii;
                    break;
                }
            }

            if (normrAct < normrmin)
            {
                normrmin = normrAct;
                xmin = x;
                iterationMinimum = ii;
            }

            if (normrcgAct < normrmin)
            {
                normrmin = normrcgAct;
                xmin = Add(x, epszdelz / gammabar, wbar);
                iterationMinimum = ii;
            }

            if (stagnation >= MaxStagnationSteps)
            {
                flagOut = KrylovFlag.Stagnated;
                break;
            }
        }

        ii = nits == 0 ? 0 : Math.Min(ii, nits);

        // The returned iterate's residual is measured, not estimated: everything above was an
        // estimate off the recurrence, and the comparison that picks between x and xmin has to be
        // between two lengths of the same kind.
        double finalNorm = Norm(Add(b, -1, a.Apply(x)));
        double relative;
        double[] answer = x;
        double[] iterationOut;
        if (flagOut == KrylovFlag.Converged)
        {
            relative = finalNorm / setup.RightHandSideNorm;
            iterationOut = [loopIteration];
        }
        else
        {
            double minimumNorm = Norm(Add(b, -1, a.Apply(xmin)));
            if (minimumNorm <= finalNorm)
            {
                answer = xmin;
                iterationOut = [iterationMinimum];
                relative = minimumNorm / setup.RightHandSideNorm;
            }
            else
            {
                iterationOut = [ii];
                relative = finalNorm / setup.RightHandSideNorm;
            }
        }

        bool full = flagOut is <= KrylovFlag.MaxIterations or KrylovFlag.Stagnated;
        return new KrylovResult(answer, flagOut, relative, iterationOut,
            Truncate(history, full ? ii + 1 : ii))
        {
            ConjugateGradientNorms = Truncate(cgHistory, full ? ii + 2 : ii + 1),
            Warnings = setup.Warnings,
            StoppedAt = [ii],
        };
    }
}
