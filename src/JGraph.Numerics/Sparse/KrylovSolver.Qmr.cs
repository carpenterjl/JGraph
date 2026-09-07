namespace JGraph.Numerics.Sparse;

/// <summary>
/// The quasi-minimal residual pair, <c>qmr</c> and <c>tfqmr</c>, and the least-squares solver
/// <c>lsqr</c> — the three whose stopping rule is not simply "the residual is small enough".
/// </summary>
public static partial class KrylovSolver
{
    /// <summary>
    /// <c>qmr</c>: the two-sided Lanczos process with a quasi-minimization of the residual over the
    /// resulting basis. Like <c>bicg</c> it needs <c>A'</c>, and like <c>bicg</c> it can break down
    /// on a zero inner product, which is flag 4.
    /// </summary>
    public static KrylovResult Qmr(KrylovOperator a, double[] b, KrylovOptions options)
    {
        int n = b.Length;
        Setup setup = Prepare("qmr", n, b, options, rejectToleranceAtEpsilon: false, Math.Min(n, 20));
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

        KrylovResult Stop(int stopFlag) =>
            new((double[])x.Clone(), stopFlag, normr / setup.RightHandSideNorm, [0], [normr])
            {
                Warnings = setup.Warnings,
            };

        double[] vt = r;
        var history = new double[setup.MaxIterations + 1];
        history[0] = normr;
        double normrmin = normr;

        double[] y = vt;
        if (options.Left is not null)
        {
            y = options.Left.Apply(vt);
            if (!AllFinite(y))
            {
                return Stop(KrylovFlag.IllConditionedPreconditioner);
            }
        }

        double rho = Norm(y);
        double[] wt = r;
        double[] z = wt;
        if (options.Right is not null)
        {
            z = options.Right.ApplyTransposed(wt);
            if (!AllFinite(z))
            {
                return Stop(KrylovFlag.IllConditionedPreconditioner);
            }
        }

        double psi = Norm(z);
        double gamm = 1;
        double eta = -1;
        double epsilon = 0;
        double thet = 0;
        double[] p = Zeros(n);
        double[] q = Zeros(n);
        double[] d = Zeros(n);
        double[] s = Zeros(n);
        int stagnation = 0;
        int moreSteps = 0;
        int maxMoreSteps = setup.MoreStepsAllowed(5);
        double iteration = 0;
        int ii;

        for (ii = 1; ii <= setup.MaxIterations; ii++)
        {
            if (rho == 0 || double.IsInfinity(rho) || psi == 0 || double.IsInfinity(psi))
            {
                flag = KrylovFlag.ScalarBreakdown;
                break;
            }

            double[] v = Scale(1 / rho, vt);
            y = Scale(1 / rho, y);
            double[] w = Scale(1 / psi, wt);
            z = Scale(1 / psi, z);
            double delta = Dot(z, y);
            if (delta == 0 || double.IsInfinity(delta))
            {
                flag = KrylovFlag.ScalarBreakdown;
                break;
            }

            double[] yt = y;
            if (options.Right is not null)
            {
                yt = options.Right.Apply(y);
                if (!AllFinite(yt))
                {
                    flag = KrylovFlag.IllConditionedPreconditioner;
                    break;
                }
            }

            double[] zt = z;
            if (options.Left is not null)
            {
                zt = options.Left.ApplyTransposed(z);
                if (!AllFinite(zt))
                {
                    flag = KrylovFlag.IllConditionedPreconditioner;
                    break;
                }
            }

            if (ii == 1)
            {
                p = yt;
                q = zt;
            }
            else
            {
                double pde = psi * delta / epsilon;
                if (pde == 0 || !double.IsFinite(pde))
                {
                    flag = KrylovFlag.ScalarBreakdown;
                    break;
                }

                double rde = rho * (delta / epsilon);
                if (rde == 0 || !double.IsFinite(rde))
                {
                    flag = KrylovFlag.ScalarBreakdown;
                    break;
                }

                p = Add(yt, -pde, p);
                q = Add(zt, -rde, q);
            }

            double[] pt = a.Apply(p);
            epsilon = Dot(q, pt);
            if (epsilon == 0 || double.IsInfinity(epsilon))
            {
                flag = KrylovFlag.ScalarBreakdown;
                break;
            }

            double beta = epsilon / delta;
            if (beta == 0 || double.IsInfinity(beta))
            {
                flag = KrylovFlag.ScalarBreakdown;
                break;
            }

            vt = Add(pt, -beta, v);
            y = vt;
            if (options.Left is not null)
            {
                y = options.Left.Apply(vt);
                if (!AllFinite(y))
                {
                    flag = KrylovFlag.IllConditionedPreconditioner;
                    break;
                }
            }

            double rho1 = rho;
            rho = Norm(y);
            wt = Add(a.ApplyTransposed(q), -beta, w);
            z = wt;
            if (options.Right is not null)
            {
                z = options.Right.ApplyTransposed(wt);
                if (!AllFinite(z))
                {
                    flag = KrylovFlag.IllConditionedPreconditioner;
                    break;
                }
            }

            psi = Norm(z);
            double thet1 = thet;
            thet = rho / (gamm * Math.Abs(beta));
            double gamm1 = gamm;
            gamm = 1 / Math.Sqrt(1 + (thet * thet));
            if (gamm == 0)
            {
                flag = KrylovFlag.ScalarBreakdown;
                break;
            }

            eta = -eta * rho1 * gamm * gamm / (beta * gamm1 * gamm1);
            if (double.IsInfinity(eta))
            {
                flag = KrylovFlag.ScalarBreakdown;
                break;
            }

            if (ii == 1)
            {
                d = Scale(eta, p);
                s = Scale(eta, pt);
            }
            else
            {
                double carry = thet1 * gamm * (thet1 * gamm);
                d = Add(Scale(eta, p), carry, d);
                s = Add(Scale(eta, pt), carry, s);
            }

            stagnation = Norm(d) < Epsilon * Norm(x) ? stagnation + 1 : 0;

            x = Add(x, 1, d);
            r = Add(r, -1, s);
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

    /// <summary>
    /// <c>tfqmr</c>: the transpose-free variant, which takes two half-steps per Bi-CG step. The
    /// inner loop counts half-steps and runs to <c>2*maxit</c>; the <c>iter</c> the caller sees is
    /// that count halved and floored, which is why asking for 10 iterations and being told it
    /// converged at 7 means fourteen half-steps and not seven.
    /// </summary>
    public static KrylovResult Tfqmr(KrylovOperator a, double[] b, KrylovOptions options)
    {
        int n = b.Length;
        Setup setup = Prepare("tfqmr", n, b, options, rejectToleranceAtEpsilon: false, Math.Min(n, 20));
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

        double[] r0 = r;
        double[] um = r;
        double[] w = r;
        double[]? pum = Precondition(options, um);
        if (pum is null)
        {
            return new KrylovResult((double[])x.Clone(), KrylovFlag.IllConditionedPreconditioner,
                normr / setup.RightHandSideNorm, [0], [normr])
            {
                Warnings = setup.Warnings,
            };
        }

        double[] v = a.Apply(pum);
        double[] au = v;
        double[] d = Zeros(n);
        double[] ad = d;
        double tau = normr;
        double theta = 0;
        double eta = 0;
        double rho = Dot(r, r);
        double rhoOld = rho;
        double alpha = 0;
        double beta = 0;
        int stagnation = 0;
        int moreSteps = 0;
        int maxMoreSteps = setup.MoreStepsAllowed(10);
        var history = new double[(2 * setup.MaxIterations) + 1];
        history[0] = normr;
        double normrmin = history[0];
        bool even = true;
        double iteration = 0;
        int kept = -1;
        int mm;

        for (mm = 1; mm <= setup.MaxIterations * 2; mm++)
        {
            double[] ump1 = um;
            if (even)
            {
                alpha = rho / Dot(r0, v);
                ump1 = Add(um, -alpha, v);
            }

            w = Add(w, -alpha, au);
            double sigma = theta * theta / alpha * eta;
            d = Add(pum, sigma, d);
            ad = Add(au, sigma, ad);
            theta = Norm(w) / tau;
            double cmp1 = 1 / Math.Sqrt(1 + (theta * theta));
            tau = tau * theta * cmp1;
            eta = cmp1 * cmp1 * alpha;

            stagnation = Math.Abs(eta) * Norm(d) < Epsilon * Norm(x) ? stagnation + 1 : 0;

            x = Add(x, eta, d);
            r = Add(r, -eta, ad);
            normr = Norm(r);
            actual = normr;
            history[mm] = normr;

            if (normr <= toleranceNorm || stagnation >= MaxStagnationSteps || moreSteps > 0)
            {
                r = Add(b, -1, a.Apply(x));
                actual = Norm(r);
                history[mm] = actual;
                if (actual <= toleranceNorm)
                {
                    flag = KrylovFlag.Converged;
                    iteration = mm;
                    kept = mm + 1;
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
                    iteration = mm;
                    kept = mm + 1;
                    break;
                }
            }

            if (actual < normrmin)
            {
                normrmin = actual;
                xmin = x;
                iterationMinimum = mm;
            }

            if (stagnation >= MaxStagnationSteps)
            {
                flag = KrylovFlag.Stagnated;
                break;
            }

            if (!even)
            {
                rho = Dot(r0, w);
                beta = rho / rhoOld;
                rhoOld = rho;
                ump1 = Add(w, beta, um);
            }

            pum = Precondition(options, ump1);
            if (pum is null)
            {
                flag = KrylovFlag.IllConditionedPreconditioner;
                kept = mm + 1;
                break;
            }

            double[] auNew = a.Apply(pum);
            if (!even)
            {
                v = Add(auNew, beta, Add(au, beta, v));
            }

            au = auNew;
            um = ump1;
            even = !even;
        }

        mm = Math.Min(mm, setup.MaxIterations * 2);

        double relative;
        double[] answer = x;
        double[] iterationOut;
        if (flag == KrylovFlag.Converged)
        {
            relative = actual / setup.RightHandSideNorm;
            iterationOut = [iteration];
        }
        else
        {
            double[] minimumResidual = Add(b, -1, a.Apply(xmin));
            if (flag is KrylovFlag.MaxIterations or KrylovFlag.Stagnated)
            {
                actual = Norm(Add(b, -1, a.Apply(x)));
            }

            double minimumNorm = Norm(minimumResidual);
            if (minimumNorm <= actual)
            {
                answer = xmin;
                iterationOut = [iterationMinimum];
                relative = minimumNorm / setup.RightHandSideNorm;
            }
            else
            {
                iterationOut = [mm];
                relative = actual / setup.RightHandSideNorm;
            }

            kept = mm + 1;
        }

        // The half-steps are the algorithm's business, not the caller's: MATLAB halves and floors
        // the count on the way out, so the reported iteration is the Bi-CG step it belongs to.
        iterationOut = [Math.Floor(iterationOut[0] / 2)];
        return new KrylovResult(answer, flag, relative, iterationOut,
            Truncate(history, kept < 0 ? mm + 1 : kept))
        {
            Warnings = setup.Warnings,
            StoppedAt = [mm],
        };
    }

    /// <summary>
    /// <c>lsqr</c>: Paige and Saunders' bidiagonalization, which solves <c>min norm(b - A*x)</c> and
    /// so is the one solver here that accepts a rectangular matrix.
    /// </summary>
    /// <remarks>
    /// Its convergence test is the one that is genuinely different. It stops when
    /// <c>normar/(norma*normr) &lt;= tol</c> — the least-squares residual relative to an estimate of
    /// <c>norm(A)</c> — or when the ordinary <c>normr &lt;= tolb</c> is met, and it checks both
    /// <em>before</em> taking the step, so the iteration it reports is one less than the loop index.
    /// Nothing is ever re-measured against <c>b - A*x</c>: the residual norm is propagated as
    /// <c>abs(s)*normr</c> through the rotations, which is exact enough for the estimate the method
    /// is built on.
    /// </remarks>
    public static KrylovResult Lsqr(KrylovOperator a, double[] b, int columns, KrylovOptions options)
    {
        int m = b.Length;
        Setup setup = Prepare("lsqr", m, b, options, rejectToleranceAtEpsilon: true, Math.Min(m, 20),
            startMatchesOrder: false);
        bool maxitSpecified = options.MaxIterations is not null;
        double toleranceNorm = setup.ToleranceTimesNorm;
        bool started = options.InitialGuess is not null;
        double[] x = started ? (double[])options.InitialGuess!.Clone() : [];

        int flag = KrylovFlag.MaxIterations;
        double[] u = b;
        if (started)
        {
            u = Add(b, -1, a.Apply(x));
        }

        double beta = Norm(u);
        double normr = beta;
        if (beta != 0)
        {
            u = Scale(1 / beta, u);
        }

        double[] v = a.ApplyTransposed(u);

        // How many columns A has is only known here when the caller handed over a function handle
        // and no preconditioner: A'u is the first vector in the other space, and its length is n.
        int n = columns > 0 ? columns : v.Length;
        if (!started)
        {
            x = Zeros(n);
        }

        KrylovResult Stop(int stopFlag, double relative) =>
            new((double[])x.Clone(), stopFlag, relative, [0], [normr])
            {
                LeastSquaresNorms = [],
                Warnings = setup.Warnings,
            };

        if (options.Right is not null)
        {
            v = options.Right.ApplyTransposed(v);
            if (!AllFinite(v))
            {
                return Stop(KrylovFlag.IllConditionedPreconditioner, normr / setup.RightHandSideNorm);
            }
        }

        if (options.Left is not null)
        {
            v = options.Left.ApplyTransposed(v);
            if (!AllFinite(v))
            {
                return Stop(KrylovFlag.IllConditionedPreconditioner, normr / setup.RightHandSideNorm);
            }
        }

        double alpha = Norm(v);
        if (alpha != 0)
        {
            v = Scale(1 / alpha, v);
        }

        double[] d = Zeros(n);
        double normar = alpha * beta;
        if (normar == 0)
        {
            return Stop(KrylovFlag.Converged, normr / setup.RightHandSideNorm);
        }

        int maxit = maxitSpecified ? setup.MaxIterations : Math.Min(n, setup.MaxIterations);
        double norma = 0;
        var history = new double[maxit + 1];
        var lsHistory = new double[maxit + 1];
        history[0] = normr;
        int keptResidual = maxit + 1;
        int keptLeastSquares = maxit;
        int stagnation = 0;
        double c = 1;
        double s = 0;
        double phibar = beta;
        int iteration = maxit;
        int ii;

        for (ii = 1; ii <= maxit; ii++)
        {
            double[] z = v;
            if (options.Left is not null)
            {
                z = options.Left.Apply(v);
                if (!AllFinite(z))
                {
                    flag = KrylovFlag.IllConditionedPreconditioner;
                    iteration = ii - 1;
                    keptResidual = iteration + 1;
                    keptLeastSquares = iteration;
                    break;
                }
            }

            if (options.Right is not null)
            {
                z = options.Right.Apply(z);
                if (!AllFinite(z))
                {
                    flag = KrylovFlag.IllConditionedPreconditioner;
                    iteration = ii - 1;
                    keptResidual = iteration + 1;
                    keptLeastSquares = iteration;
                    break;
                }
            }

            u = Add(a.Apply(z), -alpha, u);
            beta = Norm(u);
            u = Scale(1 / beta, u);
            norma = Norm([norma, alpha, beta]);
            lsHistory[ii - 1] = normar / norma;
            double thet = -s * alpha;
            double rhot = c * alpha;
            double rho = Math.Sqrt((rhot * rhot) + (beta * beta));
            c = rhot / rho;
            s = -beta / rho;
            double phi = c * phibar;
            if (phi == 0)
            {
                stagnation = 1;
            }

            phibar = s * phibar;
            d = Scale(1 / rho, Add(z, -thet, d));

            stagnation = Math.Abs(phi) * Norm(d) < Epsilon * Norm(x) ? stagnation + 1 : 0;

            if (normar / (norma * normr) <= setup.Tolerance || normr <= toleranceNorm)
            {
                flag = KrylovFlag.Converged;
                iteration = ii - 1;
                keptResidual = iteration + 1;
                keptLeastSquares = iteration + 1;
                break;
            }

            if (stagnation >= MaxStagnationSteps)
            {
                flag = KrylovFlag.Stagnated;
                iteration = ii - 1;
                keptResidual = iteration + 1;
                keptLeastSquares = iteration + 1;
                break;
            }

            if (!double.IsFinite(phi) || rho == 0 || !double.IsFinite(rho))
            {
                flag = KrylovFlag.ScalarBreakdown;
                iteration = ii - 1;
                keptResidual = iteration + 1;
                keptLeastSquares = iteration + 1;
                break;
            }

            x = Add(x, phi, d);
            normr = Math.Abs(s) * normr;
            history[ii] = normr;
            double[] vt = a.ApplyTransposed(u);
            if (options.Right is not null)
            {
                vt = options.Right.ApplyTransposed(vt);
                if (!AllFinite(vt))
                {
                    flag = KrylovFlag.IllConditionedPreconditioner;
                    iteration = ii;
                    keptResidual = iteration + 1;
                    keptLeastSquares = iteration;
                    break;
                }
            }

            if (options.Left is not null)
            {
                vt = options.Left.ApplyTransposed(vt);
                if (!AllFinite(vt))
                {
                    flag = KrylovFlag.IllConditionedPreconditioner;
                    iteration = ii;
                    keptResidual = iteration + 1;
                    keptLeastSquares = iteration;
                    break;
                }
            }

            v = Add(vt, -beta, v);
            alpha = Norm(v);
            if ((alpha == 0 || !double.IsFinite(alpha)) && ii < maxit)
            {
                flag = KrylovFlag.ScalarBreakdown;
                iteration = ii;
                keptResidual = iteration + 1;
                keptLeastSquares = iteration;
                break;
            }

            v = Scale(1 / alpha, v);
            normar = alpha * Math.Abs(s * phi);
        }

        if (flag == KrylovFlag.MaxIterations
            && (normar / (norma * normr) <= setup.Tolerance || normr <= toleranceNorm))
        {
            flag = KrylovFlag.Converged;
            iteration = maxit;
        }

        return new KrylovResult(x, flag, normr / setup.RightHandSideNorm, [iteration],
            Truncate(history, keptResidual))
        {
            LeastSquaresNorms = Truncate(lsHistory, keptLeastSquares),
            Warnings = setup.Warnings,
            StoppedAt = [Math.Min(ii, maxit)],
        };
    }
}
