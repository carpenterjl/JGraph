namespace JGraph.Numerics;

/// <summary>
/// <c>bvp5c</c>: the four-stage Lobatto IIIa formula, implemented as an implicit Runge–Kutta method
/// whose stage values are unknowns of the algebraic system rather than being condensed away.
/// </summary>
/// <remarks>
/// <para>
/// Where <c>bvp4c</c> keeps only the mesh values and eliminates the interior stages analytically,
/// <c>bvp5c</c> carries three unknown vectors per mesh interval and solves the larger system
/// directly. That is why its mesh array is three times as long as its answer: the solution
/// structure reports every third point, and the two collocation points between them are what made
/// the quartic that <c>deval</c> reads.
/// </para>
/// <para>
/// It also controls a different quantity. The error estimate samples the quartic against the
/// equation at the midpoint of each interval, and — once every interval passes — at two further
/// points where the error of a fifth-order formula peaks, so that a mesh is never declared finished
/// on the strength of one lucky sample. Unknown parameters are not handled specially: they become
/// extra state components with a zero derivative and a continuity condition at each interface,
/// which is what makes the augmented system square.
/// </para>
/// </remarks>
public static partial class BoundaryValueSolvers
{
    /// <summary>Solves the problem by the four-stage Lobatto IIIa formula with error control.</summary>
    public static BvpSolution Bvp5c(BvpOdeFunction ode, BvpVectorOdeFunction? vectorOde,
        BvpBoundaryFunction bc, BvpGuess guess, BvpOptions options)
    {
        int guessCount = guess.X.Length;
        if (guessCount < 2)
        {
            throw new BvpException("MATLAB:bvparguments:SolinitXNotEnoughPts",
                "Error calling BVP5C(ODEFUN,BCFUN,SOLINIT): SOLINIT.x must have at least two entries.");
        }

        int bare = guess.Y.Length / guessCount;
        int npar = guess.Parameters?.Length ?? 0;
        int[] guessInterfaces = InterfacesOf(guess.X);
        int nregions = guessInterfaces.Length + 1;
        bool ismbvp = nregions > 1;

        double rtol = options.RelativeTolerance;
        if (rtol < 100 * Epsilon)
        {
            rtol = 100 * Epsilon;
            options.Warn?.Invoke(
                $"Warning calling BVP5C(ODEFUN,BCFUN,SOLINIT): RelTol has been increased to {rtol:G}.");
        }

        double[] atol = options.AbsoluteTolerance is { Length: > 0 } given
            ? given.Length == 1 ? Filled(bare, given[0]) : (double[])given.Clone()
            : Filled(bare, 1e-6);

        int neqn = bare + npar;
        var tolerances = new double[neqn];
        Array.Copy(atol, tolerances, bare);
        for (int i = bare; i < neqn; i++)
        {
            tolerances[i] = rtol;
        }

        var threshold = new double[neqn];
        for (int i = 0; i < neqn; i++)
        {
            threshold[i] = tolerances[i] / rtol;
        }

        int nmax = options.MaxMeshPoints ?? (int)Math.Floor(10000.0 / bare);
        bool vectorized = options.Vectorized && vectorOde is not null;

        // The unknown parameters become state components with a zero derivative; the boundary
        // conditions gain one continuity equation per parameter per interface, which is what keeps
        // the augmented system square.
        BvpOdeFunction derivative = ode;
        BvpVectorOdeFunction vector = vectorOde ?? ((xs, ys, region, p) =>
        {
            var answer = new double[xs.Length][];
            for (int i = 0; i < xs.Length; i++)
            {
                answer[i] = ode(xs[i], ys[i], region, p);
            }

            return answer;
        });
        BvpBoundaryFunction boundary = bc;
        BvpOdeJacobianFunction? jacobian = options.JacobianFunction;
        double[,]? constantJacobian = options.ConstantJacobian;
        double[,]? constantParameterJacobian = options.ConstantParameterJacobian;
        BvpBoundaryJacobianFunction? boundaryJacobian = options.BoundaryJacobianFunction;

        if (npar > 0)
        {
            BvpOdeFunction bareOde = derivative;
            BvpVectorOdeFunction bareVector = vector;
            derivative = (x, y, region, _) =>
            {
                double[] p = y[bare..];
                double[] value = bareOde(x, y[..bare], region, p);
                var answer = new double[neqn];
                Array.Copy(value, answer, bare);
                return answer;
            };
            vector = (xs, ys, region, _) =>
            {
                double[] p = ys[0][bare..];
                var trimmed = new double[ys.Length][];
                for (int i = 0; i < ys.Length; i++)
                {
                    trimmed[i] = ys[i][..bare];
                }

                double[][] values = bareVector(xs, trimmed, region, p);
                var answer = new double[ys.Length][];
                for (int i = 0; i < ys.Length; i++)
                {
                    answer[i] = new double[neqn];
                    Array.Copy(values[i], answer[i], bare);
                }

                return answer;
            };

            BvpBoundaryFunction bareBc = boundary;
            boundary = (ya, yb, _) =>
            {
                double[] p = ya[0][bare..];
                var trimmedA = new double[ya.Length][];
                var trimmedB = new double[yb.Length][];
                for (int i = 0; i < ya.Length; i++)
                {
                    trimmedA[i] = ya[i][..bare];
                    trimmedB[i] = yb[i][..bare];
                }

                double[] first = bareBc(trimmedA, trimmedB, p);
                var answer = new double[first.Length + (npar * (ya.Length - 1))];
                Array.Copy(first, answer, first.Length);
                int at = first.Length;
                for (int r = 1; r < ya.Length; r++)
                {
                    for (int k = 0; k < npar; k++)
                    {
                        answer[at++] = ya[r][bare + k] - yb[r - 1][bare + k];
                    }
                }

                return answer;
            };

            if (jacobian is { } bareJacobian)
            {
                jacobian = (x, y, region, _) =>
                {
                    (double[,] dy, double[,]? dp) = bareJacobian(x, y[..bare], region, y[bare..]);
                    var answer = new double[neqn, neqn];
                    for (int r = 0; r < bare; r++)
                    {
                        for (int q = 0; q < bare; q++)
                        {
                            answer[r, q] = dy[r, q];
                        }

                        for (int q = 0; q < npar; q++)
                        {
                            answer[r, bare + q] = dp![r, q];
                        }
                    }

                    return (answer, null);
                };
            }
            else if (constantJacobian is { } given2 && constantParameterJacobian is { } givenP)
            {
                var answer = new double[neqn, neqn];
                for (int r = 0; r < bare; r++)
                {
                    for (int q = 0; q < bare; q++)
                    {
                        answer[r, q] = given2[r, q];
                    }

                    for (int q = 0; q < npar; q++)
                    {
                        answer[r, bare + q] = givenP[r, q];
                    }
                }

                constantJacobian = answer;
            }

            if (boundaryJacobian is { } bareBcJacobian)
            {
                boundaryJacobian = (ya, yb, _) =>
                {
                    var trimmedA = new double[ya.Length][];
                    var trimmedB = new double[yb.Length][];
                    for (int i = 0; i < ya.Length; i++)
                    {
                        trimmedA[i] = ya[i][..bare];
                        trimmedB[i] = yb[i][..bare];
                    }

                    (double[,] dya, double[,] dyb, double[,]? dp) =
                        bareBcJacobian(trimmedA, trimmedB, ya[0][bare..]);
                    return BoundaryParameterJacobian(dya, dyb, dp!, bare, npar, nregions);
                };
            }
            else if (options.ConstantBoundaryJacobianYa is { } cya)
            {
                (double[,] dya, double[,] dyb, double[,]? dp) = BoundaryParameterJacobian(
                    cya, options.ConstantBoundaryJacobianYb!, options.ConstantBoundaryJacobianP!, bare, npar, nregions);
                boundaryJacobian = (_, _, _) => (dya, dyb, dp);
            }
        }
        else if (options.ConstantBoundaryJacobianYa is { } cya)
        {
            double[,] dya = cya;
            double[,] dyb = options.ConstantBoundaryJacobianYb!;
            boundaryJacobian = (_, _, _) => (dya, dyb, null);
        }

        double[] guessY = (double[])guess.Y.Clone();
        bool singular = false;
        double[,]? projection = null;
        if (options.SingularTerm is { } s)
        {
            if (guess.X[0] != 0 || guess.X[^1] <= guess.X[0])
            {
                throw new BvpException("MATLAB:bvpsingular:SingBVPInvalidInterval",
                    "Singular BVPs must be posed on the interval [0,b] with b > 0.");
            }

            if (s.GetLength(0) != bare || s.GetLength(1) != bare)
            {
                throw new BvpException("MATLAB:bvpsingular:SingBVPInvalidS",
                    "The 'SingularTerm' option must be set to a constant matrix with as many rows and columns as there are equations.");
            }

            singular = true;
            projection = Subtract(Identity(bare), Multiply(PseudoInverse(s), s));
            double[] head = new double[bare];
            Array.Copy(guessY, head, bare);
            double[] moved = Multiply(projection, head);
            Array.Copy(moved, guessY, bare);

            var padded = new double[neqn, neqn];
            for (int r = 0; r < bare; r++)
            {
                for (int q = 0; q < bare; q++)
                {
                    padded[r, q] = s[r, q];
                }
            }

            double[,] resolvent = PseudoInverse(Subtract(Identity(neqn), padded));
            BvpOdeFunction bareOde = derivative;
            derivative = (x, y, region, p) =>
            {
                double[] value = bareOde(x, y, region, p);
                if (x == 0)
                {
                    return Multiply(resolvent, value);
                }

                double[] extra = Multiply(padded, y);
                for (int i = 0; i < value.Length; i++)
                {
                    value[i] += extra[i] / x;
                }

                return value;
            };

            BvpVectorOdeFunction bareVector = vector;
            vector = (xs, ys, region, p) =>
            {
                double[][] values = bareVector(xs, ys, region, p);
                for (int i = 0; i < xs.Length; i++)
                {
                    if (xs[i] == 0)
                    {
                        values[i] = Multiply(resolvent, values[i]);
                    }
                    else
                    {
                        double[] extra = Multiply(padded, ys[i]);
                        for (int r = 0; r < values[i].Length; r++)
                        {
                            values[i][r] += extra[r] / xs[i];
                        }
                    }
                }

                return values;
            };

            if (jacobian is { } bareJacobian)
            {
                jacobian = (x, y, region, p) =>
                {
                    (double[,] dy, double[,]? dp) = bareJacobian(x, y, region, p);
                    if (x == 0)
                    {
                        return (Multiply(resolvent, dy), dp);
                    }

                    var moved2 = new double[neqn, neqn];
                    for (int r = 0; r < neqn; r++)
                    {
                        for (int c = 0; c < neqn; c++)
                        {
                            moved2[r, c] = dy[r, c] + (padded[r, c] / x);
                        }
                    }

                    return (moved2, dp);
                };
            }
            else if (constantJacobian is { } kept)
            {
                double[,] held = kept;
                jacobian = (x, _, _, _) =>
                {
                    if (x == 0)
                    {
                        return (Multiply(resolvent, held), null);
                    }

                    var moved2 = new double[neqn, neqn];
                    for (int r = 0; r < neqn; r++)
                    {
                        for (int c = 0; c < neqn; c++)
                        {
                            moved2[r, c] = held[r, c] + (padded[r, c] / x);
                        }
                    }

                    return (moved2, null);
                };
                constantJacobian = null;
            }
        }

        double sq5 = Math.Sqrt(5);
        double[] c = [(5 - sq5) / 10, (5 + sq5) / 10];
        double[,] a =
        {
            { (11 + sq5) / 120, (25 - sq5) / 120, (25 - (13 * sq5)) / 120, (-1 + sq5) / 120 },
            { (11 - sq5) / 120, (25 + (13 * sq5)) / 120, (25 + sq5) / 120, (-1 - sq5) / 120 },
            { 1.0 / 12, 5.0 / 12, 5.0 / 12, 1.0 / 12 },
        };
        double[,] b =
        {
            { -(5 + (17 * sq5)) / 10, (5 + sq5) / 2, (-5 + (3 * sq5)) / 2, (5 - (3 * sq5)) / 10 },
            { (-5 + (17 * sq5)) / 10, -(5 + (3 * sq5)) / 2, (5 - sq5) / 2, (5 + (3 * sq5)) / 10 },
            { -7, 5 * sq5, -5 * sq5, 7 },
        };
        double[,] blockIdentity = { { 1, -1, 0, 0 }, { 1, 0, -1, 0 }, { 1, 0, 0, -1 } };
        const int Stages = 3;

        int odeEvaluations = 1;
        int bcEvaluations = 1;

        (double[] x, double[][] yGuess) = InterpolateGuess(guess, guessY, bare, npar, c, guessInterfaces);
        double[][] y = yGuess;
        int[] interfaces = InterfacesOf(x);

        const int MaxNewtonIterations = 4;
        const int MaxProbes = 4;
        bool needGlobalJacobian = true;
        bool refinedMesh = false;
        var meshHistory = new List<(int Count, double Error)> { (0, 0) };
        const double ErrorReductionGuard = 1e-4;
        bool verifying = false;
        bool done = false;
        double minimumCondition = double.MaxValue;

        var jacobianOptions = new OdeJacobianOptions { Threshold = Filled(neqn, JacobianThreshold) };
        var boundaryOptionsA = new OdeJacobianOptions { Threshold = Filled(nregions * neqn, JacobianThreshold) };
        var boundaryOptionsB = new OdeJacobianOptions { Threshold = Filled(nregions * neqn, JacobianThreshold) };

        double[][] f = [];
        double[][] ymid = [];
        double[] error = [];

        while (!done)
        {
            CollocationLu? factored = null;
            double[][] latest = y;
            double[][] latestF = f;
            for (int iteration = 0; iteration < MaxNewtonIterations; iteration++)
            {
                if (!refinedMesh)
                {
                    f = EvaluateAll(x, y, interfaces, vectorized, derivative, vector, ref odeEvaluations);
                }

                refinedMesh = false;
                (double[] rhs, double[] bcValue) = Bvp5cRhs(x, y, f, interfaces, boundary, Stages, neqn, a);
                bcEvaluations++;

                if (needGlobalJacobian)
                {
                    if (jacobian is null && constantJacobian is null)
                    {
                        jacobianOptions.Increments = null;
                    }

                    List<(int, int, double)> triplets = Bvp5cJacobian(x, y, f, bcValue, interfaces, neqn,
                        Stages, blockIdentity, a, c, derivative, vector, vectorized, boundary, jacobian,
                        constantJacobian, boundaryJacobian, jacobianOptions, boundaryOptionsA, boundaryOptionsB,
                        ref odeEvaluations, ref bcEvaluations);
                    factored = CollocationLu.Factor(neqn * x.Length, triplets);
                    double rc = factored?.ReciprocalCondition() ?? 0;
                    if (factored is null || rc == 0 || double.IsNaN(rc))
                    {
                        throw new BvpException("MATLAB:bvp5c:SingJac",
                            "Unable to solve the collocation equations -- a singular Jacobian encountered.");
                    }

                    minimumCondition = Math.Min(minimumCondition, rc);
                }

                double[] direction = factored!.Solve(rhs);
                double distance = TwoNorm(direction);

                double lambda = 1;
                double distanceNew = 0;
                for (int probe = 0; probe < MaxProbes; probe++)
                {
                    latest = new double[x.Length][];
                    for (int i = 0; i < x.Length; i++)
                    {
                        latest[i] = new double[neqn];
                        for (int r = 0; r < neqn; r++)
                        {
                            latest[i][r] = y[i][r] - (lambda * direction[(i * neqn) + r]);
                        }
                    }

                    if (singular)
                    {
                        double[] head = latest[0][..bare];
                        double[] moved = Multiply(projection!, head);
                        Array.Copy(moved, latest[0], bare);
                    }

                    latestF = EvaluateAll(x, latest, interfaces, vectorized, derivative, vector, ref odeEvaluations);
                    (double[] rhsNew, _) = Bvp5cRhs(x, latest, latestF, interfaces, boundary, Stages, neqn, a);
                    bcEvaluations++;
                    distanceNew = TwoNorm(factored.Solve(rhsNew));
                    if (distanceNew < 0.9 * distance)
                    {
                        break;
                    }

                    lambda *= 0.5;
                }

                needGlobalJacobian = distanceNew > 0.1 * distance;
                if (distanceNew < 0.1 * rtol)
                {
                    double interpolationResidual = MaxCollocationResidual(x, latest, latestF, interfaces,
                        Stages, threshold, b, sq5, neqn);
                    if (interpolationResidual < 0.1 * rtol)
                    {
                        break;
                    }
                }

                y = latest;
            }

            y = latest;
            f = latestF;
            ymid = InterpolateMidpoints(x, y, f, interfaces, neqn, Stages, sq5);

            if (verifying)
            {
                error = ErrorEstimate(x, y, ymid, f, 3, interfaces, Stages, threshold, vectorized,
                    derivative, vector, ref odeEvaluations);
            }
            else
            {
                error = ErrorEstimate(x, y, ymid, f, 1, interfaces, Stages, threshold, vectorized,
                    derivative, vector, ref odeEvaluations);
                if (Max(error) < rtol)
                {
                    verifying = true;
                    double[] second = ErrorEstimate(x, y, ymid, f, 2, interfaces, Stages, threshold,
                        vectorized, derivative, vector, ref odeEvaluations);
                    for (int i = 0; i < error.Length; i++)
                    {
                        error[i] = Math.Max(error[i], second[i]);
                    }
                }
            }

            if (verifying && Max(error) <= rtol)
            {
                done = true;
                continue;
            }

            (double[] nextX, double[][] nextY, double[][] nextF) = NewSolutionProfile(x, y, ymid, f, error,
                ErrorReductionGuard, interfaces, Stages, rtol, c, neqn, vectorized, derivative, vector,
                meshHistory, ref odeEvaluations);
            if (nextX.Length > nmax)
            {
                options.Warn?.Invoke(
                    $"Unable to meet the tolerance without using more than {nmax} mesh points. \n"
                    + $" The last mesh of {error.Length + 1} points and the solution are available in the output argument. \n"
                    + $" The maximum error is {Max(error):G}, while requested accuracy is {rtol:G}.");
                done = true;
            }
            else
            {
                refinedMesh = true;
                x = nextX;
                y = nextY;
                f = nextF;
                interfaces = InterfacesOf(x);
                needGlobalJacobian = true;
            }
        }

        if (minimumCondition < CollocationLu.IllConditioned)
        {
            options.Warn?.Invoke(
                $"Matrix is singular, close to singular or badly scaled.\n         Results may be inaccurate. RCOND = {minimumCondition:G}.");
        }

        (double[] outX, double[] outY, double[] outYp, double[] outYmid) =
            Bvp5cOutput(x, y, f, ymid, interfaces, Stages, bare);
        double[]? parameters = npar > 0 ? y[0][bare..] : null;
        double maximum = Max(error);

        if (options.Stats)
        {
            options.Print?.Invoke($"The solution was obtained on a mesh of {outX.Length} points.");
            options.Print?.Invoke($"The maximum error is {maximum:0.000e+00}. ");
            options.Print?.Invoke($"There were {odeEvaluations} calls to the ODE function. ");
            options.Print?.Invoke($"There were {bcEvaluations} calls to the BC function. ");
        }

        return new BvpSolution
        {
            Solver = Bvp5cName,
            X = outX,
            Y = outY,
            Yp = outYp,
            Ymid = outYmid,
            Parameters = parameters,
            MeshPoints = outX.Length,
            MaxResidual = maximum,
            OdeEvaluations = odeEvaluations,
            BoundaryEvaluations = bcEvaluations,
        };
    }

    /// <summary>The boundary Jacobian of the augmented system: the parameters' continuity rows added below.</summary>
    private static (double[,] Dya, double[,] Dyb, double[,]? Dp) BoundaryParameterJacobian(
        double[,] dya, double[,] dyb, double[,] dp, int bare, int npar, int nregions)
    {
        int neqn = bare + npar;
        int nbcs = (nregions * bare) + npar;
        var ya = new double[nregions * neqn, nregions * neqn];
        var yb = new double[nregions * neqn, nregions * neqn];
        for (int region = 0; region < nregions; region++)
        {
            for (int r = 0; r < nbcs; r++)
            {
                for (int k = 0; k < bare; k++)
                {
                    ya[r, (region * neqn) + k] = dya[r, (region * bare) + k];
                    yb[r, (region * neqn) + k] = dyb[r, (region * bare) + k];
                }
            }

            if (region == 0)
            {
                for (int r = 0; r < nbcs; r++)
                {
                    for (int k = 0; k < npar; k++)
                    {
                        ya[r, bare + k] = dp[r, k];
                    }
                }
            }
            else
            {
                for (int k = 0; k < npar; k++)
                {
                    ya[nbcs + ((region - 1) * npar) + k, (region * neqn) + bare + k] = 1;
                }
            }

            if (region < nregions - 1)
            {
                for (int k = 0; k < npar; k++)
                {
                    yb[nbcs + (region * npar) + k, (region * neqn) + bare + k] = -1;
                }
            }
        }

        return (ya, yb, null);
    }

    /// <summary>The initial guess laid out over the collocation points of every mesh interval.</summary>
    private static (double[] X, double[][] Y) InterpolateGuess(BvpGuess guess, double[] guessY, int bare,
        int npar, double[] c, int[] interfaces)
    {
        double[] mesh = guess.X;
        int nregions = interfaces.Length + 1;
        (int[] left, int[] right) = RegionBounds(interfaces, mesh.Length);
        double[]? parameters = guess.Parameters;
        int neqn = bare + npar;

        var x = new List<double>();
        var y = new List<double[]>();
        for (int region = 0; region < nregions; region++)
        {
            int first = left[region];
            int last = right[region];
            int intervals = last - first;
            var lower = new double[intervals];
            var upper = new double[intervals];
            for (int i = 0; i < intervals; i++)
            {
                double h = mesh[first + i + 1] - mesh[first + i];
                lower[i] = mesh[first + i] + (c[0] * h);
                upper[i] = mesh[first + i] + (c[1] * h);
            }

            double[][]? lowerValues = null;
            double[][]? upperValues = null;
            if (guess.Kind == BvpGuessKind.Solution && guess.Interpolate is { } read)
            {
                lowerValues = read(lower);
                upperValues = read(upper);
            }
            else if (guess.Kind == BvpGuessKind.BvpInit && guess.PointGuess is { } point)
            {
                lowerValues = new double[intervals][];
                upperValues = new double[intervals][];
                for (int i = 0; i < intervals; i++)
                {
                    lowerValues[i] = point(lower[i], region + 1);
                    upperValues[i] = point(upper[i], region + 1);
                }
            }
            else if (guess.Kind == BvpGuessKind.BvpInit && guess.ConstantGuess is { } constant)
            {
                lowerValues = new double[intervals][];
                upperValues = new double[intervals][];
                for (int i = 0; i < intervals; i++)
                {
                    lowerValues[i] = constant;
                    upperValues[i] = constant;
                }
            }

            for (int i = 0; i < intervals; i++)
            {
                var start = new double[neqn];
                Array.Copy(guessY, (first + i) * bare, start, 0, bare);
                double[] one = new double[neqn];
                double[] two = new double[neqn];
                if (lowerValues is not null)
                {
                    Array.Copy(lowerValues[i], one, bare);
                    Array.Copy(upperValues![i], two, bare);
                }
                else
                {
                    for (int r = 0; r < bare; r++)
                    {
                        double delta = guessY[((first + i + 1) * bare) + r] - guessY[((first + i) * bare) + r];
                        one[r] = start[r] + (c[0] * delta);
                        two[r] = start[r] + (c[1] * delta);
                    }
                }

                if (npar > 0)
                {
                    for (int k = 0; k < npar; k++)
                    {
                        start[bare + k] = parameters![k];
                        one[bare + k] = parameters[k];
                        two[bare + k] = parameters[k];
                    }
                }

                x.Add(mesh[first + i]);
                x.Add(lower[i]);
                x.Add(upper[i]);
                y.Add(start);
                y.Add(one);
                y.Add(two);
            }

            var end = new double[neqn];
            Array.Copy(guessY, last * bare, end, 0, bare);
            if (npar > 0)
            {
                for (int k = 0; k < npar; k++)
                {
                    end[bare + k] = parameters![k];
                }
            }

            x.Add(mesh[last]);
            y.Add(end);
        }

        return ([.. x], [.. y]);
    }

    /// <summary>The derivative at every collocation point, region by region.</summary>
    private static double[][] EvaluateAll(double[] x, double[][] y, int[] interfaces, bool vectorized,
        BvpOdeFunction derivative, BvpVectorOdeFunction vector, ref int evaluations)
    {
        int nregions = interfaces.Length + 1;
        (int[] left, int[] right) = RegionBounds(interfaces, x.Length);
        var f = new double[x.Length][];
        for (int region = 0; region < nregions; region++)
        {
            int count = right[region] - left[region] + 1;
            var xreg = new double[count];
            var yreg = new double[count][];
            for (int i = 0; i < count; i++)
            {
                xreg[i] = x[left[region] + i];
                yreg[i] = y[left[region] + i];
            }

            if (vectorized)
            {
                double[][] values = vector(xreg, yreg, region + 1, []);
                for (int i = 0; i < count; i++)
                {
                    f[left[region] + i] = values[i];
                }

                evaluations++;
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    f[left[region] + i] = derivative(xreg[i], yreg[i], region + 1, []);
                }

                evaluations += count;
            }
        }

        return f;
    }

    /// <summary>The collocation equations of the four-stage formula, with the boundary residual on top.</summary>
    private static (double[] Rhs, double[] BoundaryValue) Bvp5cRhs(double[] x, double[][] y, double[][] f,
        int[] interfaces, BvpBoundaryFunction boundary, int stages, int neqn, double[,] a)
    {
        int nregions = interfaces.Length + 1;
        (int[] left, int[] right) = RegionBounds(interfaces, x.Length);
        var ya = new double[nregions][];
        var yb = new double[nregions][];
        for (int region = 0; region < nregions; region++)
        {
            ya[region] = y[left[region]];
            yb[region] = y[right[region]];
        }

        double[] bcValue = boundary(ya, yb, []);
        var rhs = new double[bcValue.Length + (neqn * (x.Length - nregions))];
        Array.Copy(bcValue, rhs, bcValue.Length);

        int at = bcValue.Length;
        for (int region = 0; region < nregions; region++)
        {
            int first = left[region];
            int intervals = (right[region] - left[region]) / stages;
            for (int i = 0; i < intervals; i++)
            {
                int start = first + (i * stages);
                double h = x[start + stages] - x[start];
                for (int j = 0; j < stages; j++)
                {
                    for (int r = 0; r < neqn; r++)
                    {
                        double sum = y[start][r];
                        for (int k = 0; k <= stages; k++)
                        {
                            sum += f[start + k][r] * h * a[j, k];
                        }

                        rhs[at + (j * neqn) + r] = sum - y[start + j + 1][r];
                    }
                }

                at += neqn * stages;
            }
        }

        return (rhs, bcValue);
    }

    /// <summary>The global Jacobian of the four-stage collocation equations, as triplets.</summary>
    private static List<(int, int, double)> Bvp5cJacobian(double[] x, double[][] y, double[][] f,
        double[] bcValue, int[] interfaces, int neqn, int stages, double[,] blockIdentity, double[,] a,
        double[] c, BvpOdeFunction derivative, BvpVectorOdeFunction vector, bool vectorized,
        BvpBoundaryFunction boundary, BvpOdeJacobianFunction? jacobian, double[,]? constantJacobian,
        BvpBoundaryJacobianFunction? boundaryJacobian, OdeJacobianOptions jacobianOptions,
        OdeJacobianOptions boundaryOptionsA, OdeJacobianOptions boundaryOptionsB,
        ref int odeEvaluations, ref int bcEvaluations)
    {
        int nregions = interfaces.Length + 1;
        (int[] left, int[] right) = RegionBounds(interfaces, x.Length);
        var triplets = new List<(int, int, double)>();

        var ya = new double[nregions][];
        var yb = new double[nregions][];
        for (int region = 0; region < nregions; region++)
        {
            ya[region] = y[left[region]];
            yb[region] = y[right[region]];
        }

        double[,] dya;
        double[,] dyb;
        if (boundaryJacobian is { } bcjac)
        {
            (dya, dyb, _) = bcjac(ya, yb, []);
        }
        else
        {
            double[] flatYa = Flatten(ya);
            double[] flatYb = Flatten(yb);
            dya = OdeNumericalJacobian.Compute(v => boundary(Split(v, neqn, nregions), yb, []), null,
                flatYa, bcValue, boundaryOptionsA, out int callsA);
            bcEvaluations += callsA;
            dyb = OdeNumericalJacobian.Compute(v => boundary(ya, Split(v, neqn, nregions), []), null,
                flatYb, bcValue, boundaryOptionsB, out int callsB);
            bcEvaluations += callsB;
        }

        int nbcs = neqn * nregions;
        for (int region = 0; region < nregions; region++)
        {
            for (int k = 0; k < neqn; k++)
            {
                for (int r = 0; r < nbcs; r++)
                {
                    triplets.Add((r, (left[region] * neqn) + k, dya[r, (region * neqn) + k]));
                    triplets.Add((r, (right[region] * neqn) + k, dyb[r, (region * neqn) + k]));
                }
            }
        }

        int rowOffset = 0;
        int columnOffset = 0;
        for (int region = 0; region < nregions; region++)
        {
            int first = left[region];
            int intervals = (right[region] - left[region]) / stages;
            double[,]? propagated = null;
            for (int i = 0; i < intervals; i++)
            {
                int start = first + (i * stages);
                double h = x[start + stages] - x[start];
                double[,] jn;
                double[,] jc1;
                double[,] jc2;
                double[,] jnext;

                if (constantJacobian is { } constant)
                {
                    jn = constant;
                    jc1 = constant;
                    jc2 = constant;
                    jnext = constant;
                }
                else if (jacobian is { } jac)
                {
                    jn = propagated ?? jac(x[start], y[start], region + 1, []).Dy;
                    jc1 = jac(x[start + 1], y[start + 1], region + 1, []).Dy;
                    jc2 = jac(x[start + 2], y[start + 2], region + 1, []).Dy;
                    jnext = jac(x[start + 3], y[start + 3], region + 1, []).Dy;
                }
                else
                {
                    if (propagated is null)
                    {
                        propagated = OdeNumericalJacobian.Compute(
                            v => derivative(x[start], v, region + 1, []),
                            VectorizedAt(vector, x[start], region + 1, vectorized),
                            y[start], f[start], jacobianOptions, out int firstCalls);
                        odeEvaluations += vectorized ? 1 + Math.Max(0, firstCalls - neqn) : firstCalls;
                    }

                    jn = propagated;
                    jnext = OdeNumericalJacobian.Compute(
                        v => derivative(x[start + 3], v, region + 1, []),
                        VectorizedAt(vector, x[start + 3], region + 1, vectorized),
                        y[start + 3], f[start + 3], jacobianOptions, out int calls);
                    odeEvaluations += vectorized ? 1 + Math.Max(0, calls - neqn) : calls;

                    if (OneNormOfDifference(jnext, jn) <= 0.25 * (OneNorm(jnext) + OneNorm(jn)))
                    {
                        jc1 = Blend(jn, jnext, c[0]);
                        jc2 = Blend(jn, jnext, c[1]);
                    }
                    else
                    {
                        jc1 = OdeNumericalJacobian.Compute(
                            v => derivative(x[start + 1], v, region + 1, []),
                            VectorizedAt(vector, x[start + 1], region + 1, vectorized),
                            y[start + 1], f[start + 1], jacobianOptions, out int calls1);
                        jc2 = OdeNumericalJacobian.Compute(
                            v => derivative(x[start + 2], v, region + 1, []),
                            VectorizedAt(vector, x[start + 2], region + 1, vectorized),
                            y[start + 2], f[start + 2], jacobianOptions, out int calls2);
                        odeEvaluations += vectorized
                            ? 2 + Math.Max(0, calls1 - neqn) + Math.Max(0, calls2 - neqn)
                            : calls1 + calls2;
                    }
                }

                propagated = jnext;
                double[][,] stageBlocks = [jn, jc1, jc2, jnext];
                int rowBase = nbcs + rowOffset + (i * stages * neqn);
                int columnBase = columnOffset + (i * stages * neqn);
                for (int j = 0; j < stages; j++)
                {
                    for (int k = 0; k <= stages; k++)
                    {
                        double weight = a[j, k] * h;
                        double diagonal = blockIdentity[j, k];
                        for (int r = 0; r < neqn; r++)
                        {
                            for (int q = 0; q < neqn; q++)
                            {
                                double value = (r == q ? diagonal : 0) + (weight * stageBlocks[k][r, q]);
                                if (value != 0)
                                {
                                    triplets.Add((rowBase + (j * neqn) + r, columnBase + (k * neqn) + q, value));
                                }
                            }
                        }
                    }
                }
            }

            rowOffset = (right[region] - region) * neqn;
            columnOffset = (right[region] + 1) * neqn;
        }

        return triplets;
    }

    private static Func<double[][], double[][]>? VectorizedAt(BvpVectorOdeFunction vector, double x, int region, bool on)
    {
        if (!on)
        {
            return null;
        }

        return states =>
        {
            var repeated = new double[states.Length];
            Array.Fill(repeated, x);
            return vector(repeated, states, region, []);
        };
    }

    private static double[,] Blend(double[,] a, double[,] b, double t)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        var answer = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int q = 0; q < cols; q++)
            {
                answer[r, q] = ((1 - t) * a[r, q]) + (t * b[r, q]);
            }
        }

        return answer;
    }

    /// <summary>
    /// The largest residual in the collocation equations themselves, measured through the stage
    /// derivatives — the test that says a Newton iteration has converged well enough to trust.
    /// </summary>
    private static double MaxCollocationResidual(double[] x, double[][] y, double[][] f, int[] interfaces,
        int stages, double[] threshold, double[,] b, double sq5, int neqn)
    {
        int nregions = interfaces.Length + 1;
        (int[] left, int[] right) = RegionBounds(interfaces, x.Length);
        double worst = 0;
        for (int region = 0; region < nregions; region++)
        {
            int first = left[region];
            int intervals = (right[region] - left[region]) / stages;
            for (int i = 0; i < intervals; i++)
            {
                int start = first + (i * stages);
                double h = x[start + stages] - x[start];
                double largest = 0;
                for (int j = 0; j < stages; j++)
                {
                    double extra = j switch
                    {
                        0 => -sq5 / 5,
                        1 => sq5 / 5,
                        _ => -1,
                    };
                    for (int r = 0; r < neqn; r++)
                    {
                        double value = extra * f[start][r];
                        for (int k = 0; k <= stages; k++)
                        {
                            value += y[start + k][r] * b[j, k] / h;
                        }

                        double residual = value - f[start + j + 1][r];
                        double weight = Math.Max(Math.Abs(y[start + j + 1][r]), threshold[r]);
                        largest = Math.Max(largest, Math.Abs(residual / weight));
                    }
                }

                worst = Math.Max(worst, Math.Abs(h) * largest);
            }
        }

        return worst;
    }

    /// <summary>The solution at the midpoints of the mesh intervals, from the four stage slopes.</summary>
    private static double[][] InterpolateMidpoints(double[] x, double[][] y, double[][] f, int[] interfaces,
        int neqn, int stages, double sq5)
    {
        int nregions = interfaces.Length + 1;
        (int[] left, int[] right) = RegionBounds(interfaces, x.Length);
        var midpoints = new List<double[]>();
        for (int region = 0; region < nregions; region++)
        {
            if (region > 0)
            {
                midpoints.Add(new double[neqn]);
            }

            int first = left[region];
            int intervals = (right[region] - left[region]) / stages;
            for (int i = 0; i < intervals; i++)
            {
                int start = first + (i * stages);
                double h = x[start + stages] - x[start];
                var value = new double[neqn];
                for (int r = 0; r < neqn; r++)
                {
                    value[r] = y[start][r] + (((17.0 / 192 * f[start][r])
                        + ((40 + (15 * sq5)) / 192 * f[start + 1][r])
                        + ((40 - (15 * sq5)) / 192 * f[start + 2][r])
                        - (1.0 / 192 * f[start + stages][r])) * h);
                }

                midpoints.Add(value);
            }
        }

        return [.. midpoints];
    }

    /// <summary>
    /// The error of the quartic on each mesh interval, sampled at the midpoint alone while the mesh
    /// is still being adapted and at the two places a fifth-order error peaks once it is not.
    /// </summary>
    private static double[] ErrorEstimate(double[] x, double[][] y, double[][] ymid, double[][] f,
        int samples, int[] interfaces, int stages, double[] threshold, bool vectorized,
        BvpOdeFunction derivative, BvpVectorOdeFunction vector, ref int evaluations)
    {
        double[] nodes = samples switch
        {
            1 => [0.5],
            2 => [0.5 - (Math.Sqrt(15) / 10), 0.5 + (Math.Sqrt(15) / 10)],
            _ => [0.5 - (Math.Sqrt(15) / 10), 0.5, 0.5 + (Math.Sqrt(15) / 10)],
        };

        int nregions = interfaces.Length + 1;
        (int[] left, int[] right) = RegionBounds(interfaces, x.Length);
        var estimate = new List<double>();
        int midpoint = 0;
        for (int region = 0; region < nregions; region++)
        {
            if (region > 0)
            {
                estimate.Add(0);
                midpoint++;
            }

            int first = left[region];
            int intervals = (right[region] - left[region]) / stages;
            for (int i = 0; i < intervals; i++)
            {
                int start = first + (i * stages);
                double h = x[start + stages] - x[start];
                var xres = new double[nodes.Length];
                var yres = new double[nodes.Length][];
                var ypres = new double[nodes.Length][];
                for (int s = 0; s < nodes.Length; s++)
                {
                    xres[s] = x[start] + (nodes[s] * h);
                    (yres[s], ypres[s]) = Hermite4(xres[s], x[start], y[start], x[start + stages],
                        y[start + stages], ymid[midpoint], f[start], f[start + stages]);
                }

                double[][] values;
                if (vectorized)
                {
                    values = vector(xres, yres, region + 1, []);
                    evaluations++;
                }
                else
                {
                    values = new double[nodes.Length][];
                    for (int s = 0; s < nodes.Length; s++)
                    {
                        values[s] = derivative(xres[s], yres[s], region + 1, []);
                    }

                    evaluations += nodes.Length;
                }

                double largest = 0;
                for (int s = 0; s < nodes.Length; s++)
                {
                    for (int r = 0; r < yres[s].Length; r++)
                    {
                        double weight = Math.Max(Math.Abs(yres[s][r]), threshold[r]);
                        largest = Math.Max(largest, Math.Abs((ypres[s][r] - values[s][r]) / weight));
                    }
                }

                estimate.Add(Math.Abs(h) * largest);
                midpoint++;
            }
        }

        return [.. estimate];
    }

    /// <summary>
    /// The mesh redistributed: an interval over the tolerance is split in two, or in three when it
    /// is more than two hundred and fifty times over; three quiet ones become two.
    /// </summary>
    private static (double[] X, double[][] Y, double[][] F) NewSolutionProfile(double[] x, double[][] y,
        double[][] ymid, double[][] f, double[] error, double guard, int[] interfaces, int stages,
        double rtol, double[] c, int neqn, bool vectorized, BvpOdeFunction derivative,
        BvpVectorOdeFunction vector, List<(int Count, double Error)> meshHistory, ref int evaluations)
    {
        double worst = Max(error);
        bool oscillating = false;
        foreach ((int count, double before) in meshHistory)
        {
            if (count == error.Length && Math.Abs(before - worst) / worst < guard)
            {
                oscillating = true;
                break;
            }
        }

        meshHistory.Add((error.Length, worst));
        bool canRemovePoints = !oscillating;

        const double Power = 5;
        int nregions = interfaces.Length + 1;
        (int[] left, int[] right) = RegionBounds(interfaces, x.Length);
        var newX = new List<double>();
        var newY = new List<double[]>();
        var newF = new List<double[]>();
        int midpoint = 0;
        int errorAt = 0;

        for (int region = 0; region < nregions; region++)
        {
            if (region > 0)
            {
                midpoint++;
                errorAt++;
            }

            int first = left[region];
            int intervals = (right[region] - left[region]) / stages;
            int direction = Math.Sign(x[right[region]] - x[first]);
            newX.Add(x[first]);
            newY.Add(y[first]);
            newF.Add(f[first]);

            int i = 0;
            int at = first;
            while (i < intervals)
            {
                double[] places;
                double[][] values;
                double[][] slopes;
                if (error[errorAt + i] <= rtol)
                {
                    bool merged = false;
                    if (canRemovePoints && i + 2 < intervals
                        && error[errorAt + i] < 0.1 * rtol && error[errorAt + i + 1] < 0.1 * rtol
                        && error[errorAt + i + 2] < 0.1 * rtol)
                    {
                        double xi = x[at];
                        double xi3 = x[at + (3 * stages)];
                        double hnew = (xi3 - xi) / 2;
                        double c1 = error[errorAt + i] / Math.Pow(Math.Abs(x[at + stages] - xi), Power);
                        double c2 = error[errorAt + i + 1] / Math.Pow(Math.Abs(x[at + (2 * stages)] - x[at + stages]), Power);
                        double c3 = error[errorAt + i + 2] / Math.Pow(Math.Abs(xi3 - x[at + (2 * stages)]), Power);
                        double predicted = Math.Max(c1, Math.Max(c2, c3)) * Math.Pow(Math.Abs(hnew), Power);
                        if (predicted < 0.5 * rtol)
                        {
                            places = [xi + (hnew * c[0]), xi + (hnew * c[1]), xi + hnew,
                                xi + (hnew * (1 + c[0])), xi + (hnew * (1 + c[1]))];
                            values = new double[places.Length][];
                            for (int s = 0; s < places.Length; s++)
                            {
                                int piece = direction * (places[s] - x[at + stages]) <= 0 ? 0
                                    : direction * (places[s] - x[at + (2 * stages)]) <= 0 ? 1 : 2;
                                int bottom = at + (piece * stages);
                                values[s] = Hermite4(places[s], x[bottom], y[bottom], x[bottom + stages],
                                    y[bottom + stages], ymid[midpoint + piece], f[bottom], f[bottom + stages]).Value;
                            }

                            slopes = Evaluate(places, values, region + 1, vectorized, derivative, vector, ref evaluations);
                            Append(newX, newY, newF, places, values, slopes);
                            newX.Add(xi3);
                            newY.Add(y[at + (3 * stages)]);
                            newF.Add(f[at + (3 * stages)]);
                            midpoint += 3;
                            i += 3;
                            at += 3 * stages;
                            merged = true;
                        }
                    }

                    if (!merged)
                    {
                        for (int s = 1; s <= stages; s++)
                        {
                            newX.Add(x[at + s]);
                            newY.Add(y[at + s]);
                            newF.Add(f[at + s]);
                        }

                        midpoint++;
                        i++;
                        at += stages;
                    }

                    continue;
                }

                double hnew2 = error[errorAt + i] > 250 * rtol
                    ? (x[at + stages] - x[at]) / 3
                    : (x[at + stages] - x[at]) / 2;
                places = error[errorAt + i] > 250 * rtol
                    ? [x[at] + (hnew2 * c[0]), x[at] + (hnew2 * c[1]), x[at] + hnew2,
                        x[at] + (hnew2 * (1 + c[0])), x[at] + (hnew2 * (1 + c[1])), x[at] + (2 * hnew2),
                        x[at] + (hnew2 * (2 + c[0])), x[at] + (hnew2 * (2 + c[1]))]
                    : [x[at] + (hnew2 * c[0]), x[at] + (hnew2 * c[1]), x[at] + hnew2,
                        x[at] + (hnew2 * (1 + c[0])), x[at] + (hnew2 * (1 + c[1]))];
                values = new double[places.Length][];
                for (int s = 0; s < places.Length; s++)
                {
                    values[s] = Hermite4(places[s], x[at], y[at], x[at + stages], y[at + stages],
                        ymid[midpoint], f[at], f[at + stages]).Value;
                }

                slopes = Evaluate(places, values, region + 1, vectorized, derivative, vector, ref evaluations);
                Append(newX, newY, newF, places, values, slopes);
                newX.Add(x[at + stages]);
                newY.Add(y[at + stages]);
                newF.Add(f[at + stages]);
                midpoint++;
                i++;
                at += stages;
            }

            errorAt += intervals;
        }

        return ([.. newX], [.. newY], [.. newF]);
    }

    private static void Append(List<double> newX, List<double[]> newY, List<double[]> newF,
        double[] places, double[][] values, double[][] slopes)
    {
        for (int s = 0; s < places.Length; s++)
        {
            newX.Add(places[s]);
            newY.Add(values[s]);
            newF.Add(slopes[s]);
        }
    }

    private static double[][] Evaluate(double[] places, double[][] values, int region, bool vectorized,
        BvpOdeFunction derivative, BvpVectorOdeFunction vector, ref int evaluations)
    {
        if (vectorized)
        {
            evaluations++;
            return vector(places, values, region, []);
        }

        var slopes = new double[places.Length][];
        for (int s = 0; s < places.Length; s++)
        {
            slopes[s] = derivative(places[s], values[s], region, []);
        }

        evaluations += places.Length;
        return slopes;
    }

    /// <summary>Every third collocation point, which is the mesh the answer reports.</summary>
    private static (double[] X, double[] Y, double[] Yp, double[] Ymid) Bvp5cOutput(double[] x, double[][] y,
        double[][] f, double[][] ymid, int[] interfaces, int stages, int bare)
    {
        int nregions = interfaces.Length + 1;
        (int[] left, int[] right) = RegionBounds(interfaces, x.Length);
        var outX = new List<double>();
        var outY = new List<double>();
        var outYp = new List<double>();
        var outYmid = new List<double>();
        int midpoint = 0;
        for (int region = 0; region < nregions; region++)
        {
            if (region > 0)
            {
                outYmid.AddRange(new double[bare]);
                midpoint++;
            }

            for (int i = left[region]; i <= right[region]; i += stages)
            {
                outX.Add(x[i]);
                outY.AddRange(y[i][..bare]);
                outYp.AddRange(f[i][..bare]);
            }

            int intervals = (right[region] - left[region]) / stages;
            for (int i = 0; i < intervals; i++)
            {
                outYmid.AddRange(ymid[midpoint + i][..bare]);
            }

            midpoint += intervals;
        }

        return ([.. outX], [.. outY], [.. outYp], [.. outYmid]);
    }
}
