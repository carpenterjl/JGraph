using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Numerics;

/// <summary>The derivative of a boundary value problem: <c>f(x, y)</c>, or <c>f(x, y, region)</c>, or with unknown parameters.</summary>
public delegate double[] BvpOdeFunction(double x, double[] y, int region, double[] parameters);

/// <summary>The same derivative over several points at once — MATLAB's <c>Vectorized</c>.</summary>
public delegate double[][] BvpVectorOdeFunction(double[] x, double[][] y, int region, double[] parameters);

/// <summary>The residual in the boundary conditions; one column of <paramref name="ya"/> per region.</summary>
public delegate double[] BvpBoundaryFunction(double[][] ya, double[][] yb, double[] parameters);

/// <summary><c>df/dy</c> at a point, and <c>df/dp</c> beside it when the problem has unknown parameters.</summary>
public delegate (double[,] Dy, double[,]? Dp) BvpOdeJacobianFunction(double x, double[] y, int region, double[] parameters);

/// <summary>The partial derivatives of the boundary conditions.</summary>
public delegate (double[,] Dya, double[,] Dyb, double[,]? Dp) BvpBoundaryJacobianFunction(double[][] ya, double[][] yb, double[] parameters);

/// <summary>Everything <c>bvpset</c> can say, read by both collocation solvers.</summary>
public sealed record BvpOptions
{
    /// <summary>Relative tolerance on the residual; MATLAB's default is 1e-3.</summary>
    public double RelativeTolerance { get; init; } = 1e-3;

    /// <summary>Absolute tolerance, one entry per equation; null is MATLAB's 1e-6 throughout.</summary>
    public double[]? AbsoluteTolerance { get; init; }

    /// <summary>The most mesh points allowed; null is MATLAB's <c>floor(10000/n)</c>.</summary>
    public int? MaxMeshPoints { get; init; }

    /// <summary>Whether the derivative answers a whole row of points in one call.</summary>
    public bool Vectorized { get; init; }

    /// <summary>Print the cost of the run when it ends.</summary>
    public bool Stats { get; init; }

    /// <summary>The constant matrix <c>S</c> of <c>y' = S·y/x + f(x, y)</c>, or null.</summary>
    public double[,]? SingularTerm { get; init; }

    /// <summary><c>FJacobian</c> as a function.</summary>
    public BvpOdeJacobianFunction? JacobianFunction { get; init; }

    /// <summary><c>FJacobian</c> as a constant matrix, n-by-n·nregions.</summary>
    public double[,]? ConstantJacobian { get; init; }

    /// <summary>The <c>df/dp</c> half of a constant <c>FJacobian</c> cell, n-by-npar·nregions.</summary>
    public double[,]? ConstantParameterJacobian { get; init; }

    /// <summary><c>BCJacobian</c> as a function.</summary>
    public BvpBoundaryJacobianFunction? BoundaryJacobianFunction { get; init; }

    /// <summary>The <c>dbc/dya</c> of a constant <c>BCJacobian</c> cell.</summary>
    public double[,]? ConstantBoundaryJacobianYa { get; init; }

    /// <summary>The <c>dbc/dyb</c> of a constant <c>BCJacobian</c> cell.</summary>
    public double[,]? ConstantBoundaryJacobianYb { get; init; }

    /// <summary>The <c>dbc/dp</c> of a constant <c>BCJacobian</c> cell.</summary>
    public double[,]? ConstantBoundaryJacobianP { get; init; }

    /// <summary>Where a warning goes; null drops it.</summary>
    public Action<string>? Warn { get; init; }

    /// <summary>Where <see cref="Stats"/> prints; null drops the lines.</summary>
    public Action<string>? Print { get; init; }
}

/// <summary>The guess a solver starts from — <c>bvpinit</c>'s structure, in the numerics layer's own shape.</summary>
/// <param name="X">The initial mesh, with a repeated entry at every interface of a multipoint problem.</param>
/// <param name="Y">The guess at the solution, n-by-N and column-major.</param>
/// <param name="Parameters">A guess at the unknown parameters, or null when there are none.</param>
public sealed record BvpGuess(double[] X, double[] Y, double[]? Parameters)
{
    /// <summary>
    /// Where the guess came from, which decides how <c>bvp5c</c> fills in its collocation points:
    /// a structure of unknown origin is interpolated linearly between mesh points, one from
    /// <c>bvpinit</c> is asked for the value at the point, and one from a previous solve is read
    /// off that solution's own interpolant.
    /// </summary>
    public BvpGuessKind Kind { get; init; } = BvpGuessKind.Unknown;

    /// <summary>The <c>bvpinit</c> guess as a function of the point and the region, when it was one.</summary>
    public Func<double, int, double[]>? PointGuess { get; init; }

    /// <summary>The <c>bvpinit</c> guess as a constant column, when it was one.</summary>
    public double[]? ConstantGuess { get; init; }

    /// <summary>A previous solution read at the points asked for — <c>deval</c> on the guess.</summary>
    public Func<double[], double[][]>? Interpolate { get; init; }
}

/// <summary>Where a <c>bvp5c</c> guess came from, which decides how its collocation points are filled in.</summary>
public enum BvpGuessKind
{
    /// <summary>A structure with a mesh and values and nothing else: interpolate linearly.</summary>
    Unknown,

    /// <summary><c>bvpinit</c>'s own structure, which remembers the guess it was given.</summary>
    BvpInit,

    /// <summary>A solution from a previous solve, which can be read at any point.</summary>
    Solution,
}

/// <summary>What a collocation solver answered.</summary>
public sealed class BvpSolution
{
    /// <summary>Which solver ran.</summary>
    public required string Solver { get; init; }

    /// <summary>The mesh it chose.</summary>
    public required double[] X { get; init; }

    /// <summary>The solution there, n-by-N and column-major.</summary>
    public required double[] Y { get; init; }

    /// <summary>The slope there, n-by-N and column-major.</summary>
    public required double[] Yp { get; init; }

    /// <summary>The solution at the interval midpoints — <c>bvp5c</c>'s quartic needs it; null for <c>bvp4c</c>.</summary>
    public double[]? Ymid { get; init; }

    /// <summary>The unknown parameters that were found, or null.</summary>
    public double[]? Parameters { get; init; }

    /// <summary>How many mesh points the answer has.</summary>
    public int MeshPoints { get; init; }

    /// <summary>The largest residual (<c>bvp4c</c>) or error (<c>bvp5c</c>) over the mesh.</summary>
    public double MaxResidual { get; init; }

    /// <summary>Calls of the derivative.</summary>
    public int OdeEvaluations { get; init; }

    /// <summary>Calls of the boundary condition function.</summary>
    public int BoundaryEvaluations { get; init; }
}

/// <summary>
/// The two collocation solvers for two-point and multipoint boundary value problems: <c>bvp4c</c>,
/// the three-stage Lobatto IIIa formula with residual control, and <c>bvp5c</c>, the four-stage one
/// with control of the error itself.
/// </summary>
/// <remarks>
/// <para>
/// A boundary value problem has no direction to march in, so there is no step to accept or reject.
/// What there is instead is a mesh, a collocation polynomial on every interval of it, and one large
/// system of algebraic equations saying that the polynomial satisfies the differential equation at
/// the collocation points and the boundary conditions at the ends. Newton's method solves that
/// system; the residual of the resulting continuous solution says which intervals are too coarse;
/// the mesh is redistributed and the whole thing is done again. The count of mesh points in the
/// answer is therefore decided by the residual estimate and by the redistribution rule, and both are
/// reproduced here from MATLAB's own — the quadrature weights, the two Lobatto points, the factors
/// of a hundred and a half that decide how many points an interval gains or loses.
/// </para>
/// <para>
/// The difference between the two solvers is what they control. <c>bvp4c</c> measures the residual
/// <c>S'(x) − f(x, S(x))</c> of the collocation polynomial and drives that below the tolerance, which
/// bounds the true error only indirectly; <c>bvp5c</c> measures the error of the quartic against the
/// equation at points chosen where that error peaks, and so answers a coarser mesh for the same
/// tolerance. That is why the two disagree on mesh size for the same problem and neither is wrong.
/// </para>
/// <para>
/// The global Jacobian is a band with a border — every interval couples its two end points, the
/// boundary conditions reach both ends of the mesh, and the unknown parameters fill a column each.
/// It is factored by <see cref="CollocationLu"/> once per Newton iteration and solved against the
/// residual and against every probe of the line search.
/// </para>
/// </remarks>
public static partial class BoundaryValueSolvers
{
    private const double Epsilon = 2.220446049250313e-16;
    private const double JacobianThreshold = 1e-6;

    /// <summary>MATLAB's name for the three-stage solver.</summary>
    public const string Bvp4cName = "bvp4c";

    /// <summary>MATLAB's name for the four-stage solver.</summary>
    public const string Bvp5cName = "bvp5c";

    // --- bvp4c ----------------------------------------------------------------------------------

    /// <summary>Solves the problem by the three-stage Lobatto IIIa formula with residual control.</summary>
    public static BvpSolution Bvp4c(BvpOdeFunction ode, BvpVectorOdeFunction? vectorOde,
        BvpBoundaryFunction bc, BvpGuess guess, BvpOptions options)
    {
        var problem = BvpProblem.Create(Bvp4cName, ode, vectorOde, bc, guess, options);
        int n = problem.N;
        int npar = problem.Npar;
        int nregions = problem.Regions;
        double rtol = problem.RelativeTolerance;
        double[] threshold = problem.Threshold;
        int nmax = problem.MaxMeshPoints;

        double[] x = (double[])problem.X.Clone();
        double[] y = (double[])problem.Y.Clone();
        double[] parameters = problem.Parameters;
        int meshCount = x.Length;
        int nBCs = (n * nregions) + npar;
        int[] interfaces = InterfacesOf(x);

        int odeEvaluations = 1;
        int bcEvaluations = 1;

        const int MaxNewtonIterations = 4;
        const int MaxProbes = 4;
        bool needGlobalJacobian = true;
        var meshHistory = new List<(int Count, double Residual)> { (0, 0) };
        const double ResidualReductionGuard = 1e-4;
        bool done = false;
        double minimumCondition = double.MaxValue;

        double[] yp = [];
        double[] fmid = [];
        double[] residual = [];
        var jacobianState = new BvpJacobianState(n, npar, problem.Vectorized);

        while (!done)
        {
            int size = (n * meshCount) + npar;
            var big = new double[size];
            Array.Copy(y, big, n * meshCount);
            Array.Copy(parameters, 0, big, n * meshCount, npar);

            (double[] rhs, double[] slopes, double[] mid, int calls) =
                CollocationRhs(problem, n, x, big, interfaces, parameters);
            odeEvaluations += calls;
            bcEvaluations++;
            yp = slopes;
            fmid = mid;

            CollocationLu? factored = null;
            double[] newest = big;
            double distanceNew = 0;
            for (int iteration = 0; iteration < MaxNewtonIterations; iteration++)
            {
                if (needGlobalJacobian)
                {
                    List<(int, int, double)> triplets = CollocationJacobian(problem, n, x, big, yp, fmid,
                        interfaces, parameters, jacobianState, out int jacobianCalls, out int bcCalls);
                    odeEvaluations += jacobianCalls;
                    bcEvaluations += bcCalls;
                    factored = CollocationLu.Factor(size, triplets);
                    double rc = factored?.ReciprocalCondition() ?? 0;
                    if (factored is null || rc == 0 || double.IsNaN(rc))
                    {
                        throw new BvpException("MATLAB:bvp4c:SingJac",
                            "Unable to solve the collocation equations -- a singular Jacobian encountered.");
                    }

                    minimumCondition = Math.Min(minimumCondition, rc);
                }

                double[] direction = factored!.Solve(rhs);
                double distance = TwoNorm(direction);

                // A weak line search: halve the step until the Newton direction at the new point is
                // clearly shorter than at the old one, and give up after four halvings either way.
                double lambda = 1;
                for (int probe = 0; probe < MaxProbes; probe++)
                {
                    newest = new double[size];
                    for (int i = 0; i < size; i++)
                    {
                        newest[i] = big[i] - (lambda * direction[i]);
                    }

                    if (problem.Singular)
                    {
                        double[] head = new double[n];
                        Array.Copy(newest, head, n);
                        double[] projected = Multiply(problem.BoundaryProjection!, head);
                        Array.Copy(projected, newest, n);
                    }

                    if (npar > 0)
                    {
                        Array.Copy(newest, n * meshCount, parameters, 0, npar);
                    }

                    (rhs, yp, fmid, calls) = CollocationRhs(problem, n, x, newest, interfaces, parameters);
                    odeEvaluations += calls;
                    bcEvaluations++;
                    distanceNew = TwoNorm(factored.Solve(rhs));
                    if (distanceNew < 0.9 * distance)
                    {
                        break;
                    }

                    lambda *= 0.5;
                }

                needGlobalJacobian = distanceNew > 0.1 * distance;
                if (distanceNew < 0.1 * rtol)
                {
                    break;
                }

                Array.Copy(newest, big, size);
            }

            y = new double[n * meshCount];
            Array.Copy(newest, y, n * meshCount);

            (residual, calls) = Residual(problem, n, x, y, yp, fmid, rhs, threshold, nBCs, interfaces, parameters);
            odeEvaluations += calls;
            double maxResidual = Max(residual);
            if (maxResidual < rtol)
            {
                done = true;
                continue;
            }

            // A mesh that has been here before with the same count and much the same residual is
            // oscillating; the redistribution is then only allowed to add points, never to drop any.
            bool oscillating = false;
            foreach ((int count, double before) in meshHistory)
            {
                if (count == meshCount && Math.Abs(before - maxResidual) / maxResidual < ResidualReductionGuard)
                {
                    oscillating = true;
                    break;
                }
            }

            meshHistory.Add((meshCount, maxResidual));
            (int newCount, double[] newX, double[] newY, int[] newInterfaces) =
                NewProfile(n, x, y, yp, residual, interfaces, rtol, nmax, !oscillating);
            if (newCount > nmax)
            {
                problem.Warn?.Invoke(
                    $"Unable to meet the tolerance without using more than {nmax} mesh points. \n"
                    + $" The last mesh of {newX.Length} points and the solution are available in the output argument. \n"
                    + $" The maximum residual is {maxResidual:G}, while requested accuracy is {rtol:G}.");
                done = true;
            }

            meshCount = newCount;
            x = newX;
            y = newY;
            interfaces = newInterfaces;
            needGlobalJacobian = true;
        }

        if (minimumCondition < CollocationLu.IllConditioned)
        {
            problem.Warn?.Invoke(
                $"Matrix is singular, close to singular or badly scaled.\n         Results may be inaccurate. RCOND = {minimumCondition:G}.");
        }

        double maximum = Max(residual);
        if (problem.Stats)
        {
            problem.Print?.Invoke($"The solution was obtained on a mesh of {meshCount} points.");
            problem.Print?.Invoke($"The maximum residual is {maximum:0.000e+00}. ");
            problem.Print?.Invoke($"There were {odeEvaluations} calls to the ODE function. ");
            problem.Print?.Invoke($"There were {bcEvaluations} calls to the BC function. ");
        }

        return new BvpSolution
        {
            Solver = Bvp4cName,
            X = x,
            Y = y,
            Yp = yp,
            Parameters = npar > 0 ? parameters : null,
            MeshPoints = meshCount,
            MaxResidual = maximum,
            OdeEvaluations = odeEvaluations,
            BoundaryEvaluations = bcEvaluations,
        };
    }

    // --- the collocation equations --------------------------------------------------------------

    /// <summary>
    /// The system <c>Phi(Y)</c>: the boundary conditions, then the Lobatto IIIa formula on every
    /// mesh interval. The derivative at the interval midpoints comes back too, because the residual
    /// estimate reads it and the Jacobian's midpoint rule reads it again.
    /// </summary>
    private static (double[] Phi, double[] F, double[] Fmid, int Calls) CollocationRhs(
        BvpProblem problem, int n, double[] x, double[] big, int[] interfaces, double[] parameters)
    {
        int meshCount = x.Length;
        int nregions = interfaces.Length + 1;
        int npar = problem.Npar;
        int nBCs = (n * nregions) + npar;
        (int[] left, int[] right) = RegionBounds(interfaces, meshCount);

        var f = new double[n * meshCount];
        var fmid = new double[n * Math.Max(0, meshCount - 1)];
        var phi = new double[nBCs + (n * (meshCount - nregions))];

        double[] boundary = problem.Boundary(Columns(big, n, left), Columns(big, n, right), parameters);
        Array.Copy(boundary, phi, Math.Min(boundary.Length, nBCs));

        int at = nBCs;
        int calls = 0;
        for (int region = 0; region < nregions; region++)
        {
            int first = left[region];
            int last = right[region];
            int points = last - first + 1;
            int intervals = points - 1;

            var xreg = new double[points];
            var yreg = new double[points][];
            for (int i = 0; i < points; i++)
            {
                xreg[i] = x[first + i];
                yreg[i] = Column(big, n, first + i);
            }

            double[][] freg;
            if (problem.Vectorized)
            {
                freg = problem.VectorDerivative(xreg, yreg, region + 1, parameters);
                calls = 1;
            }
            else
            {
                freg = new double[points][];
                for (int i = 0; i < points; i++)
                {
                    freg[i] = problem.Derivative(xreg[i], yreg[i], region + 1, parameters);
                }

                calls = points;
            }

            var xmid = new double[intervals];
            var ymid = new double[intervals][];
            for (int i = 0; i < intervals; i++)
            {
                double h = xreg[i + 1] - xreg[i];
                xmid[i] = (xreg[i] + xreg[i + 1]) / 2;
                var midpoint = new double[n];
                for (int r = 0; r < n; r++)
                {
                    midpoint[r] = ((yreg[i][r] + yreg[i + 1][r]) / 2) - ((freg[i + 1][r] - freg[i][r]) * (h / 8));
                }

                ymid[i] = midpoint;
            }

            double[][] fmidreg;
            if (problem.Vectorized)
            {
                fmidreg = problem.VectorDerivative(xmid, ymid, region + 1, parameters);
                calls++;
            }
            else
            {
                fmidreg = new double[intervals][];
                for (int i = 0; i < intervals; i++)
                {
                    fmidreg[i] = problem.Derivative(xmid[i], ymid[i], region + 1, parameters);
                }

                calls += intervals;
            }

            for (int i = 0; i < intervals; i++)
            {
                double h = xreg[i + 1] - xreg[i];
                for (int r = 0; r < n; r++)
                {
                    phi[at + (i * n) + r] = yreg[i + 1][r] - yreg[i][r]
                        - ((freg[i + 1][r] + (4 * fmidreg[i][r]) + freg[i][r]) * (h / 6));
                }
            }

            for (int i = 0; i < points; i++)
            {
                Array.Copy(freg[i], 0, f, (first + i) * n, n);
            }

            for (int i = 0; i < intervals; i++)
            {
                Array.Copy(fmidreg[i], 0, fmid, (first + i) * n, n);
            }

            at += n * intervals;
        }

        return (phi, f, fmid, calls);
    }

    /// <summary>
    /// The L2 norm of the residual on every mesh interval, by five-point Lobatto quadrature: the
    /// midpoint value comes from the collocation equations themselves, and the two interior Lobatto
    /// points are read off the interval's Hermite cubic and compared with the equation there.
    /// </summary>
    private static (double[] Residual, int Calls) Residual(BvpProblem problem, int n, double[] x, double[] y,
        double[] yp, double[] fmid, double[] rhs, double[] threshold, int nBCs, int[] interfaces, double[] parameters)
    {
        int meshCount = x.Length;
        int nregions = interfaces.Length + 1;
        (int[] left, int[] right) = RegionBounds(interfaces, meshCount);

        double lob4 = (1 + Math.Sqrt(3.0 / 7)) / 2;
        double lob2 = (1 - Math.Sqrt(3.0 / 7)) / 2;
        const double Lobw24 = 49.0 / 90;
        const double Lobw3 = 32.0 / 45;

        // The Newton residual laid back out over the intervals, with the interface intervals — the
        // ones that are not intervals at all — left at zero.
        var newtonResidual = new double[n * Math.Max(0, meshCount - 1)];
        int source = nBCs;
        for (int region = 0; region < nregions; region++)
        {
            for (int i = left[region]; i < right[region]; i++)
            {
                Array.Copy(rhs, source, newtonResidual, i * n, n);
                source += n;
            }
        }

        var residual = new double[Math.Max(0, meshCount - 1)];
        int calls = 0;
        for (int region = 0; region < nregions; region++)
        {
            int first = left[region];
            int last = right[region];
            int intervals = last - first;
            if (intervals <= 0)
            {
                continue;
            }

            var h = new double[intervals];
            for (int i = 0; i < intervals; i++)
            {
                h[i] = x[first + i + 1] - x[first + i];
            }

            var value = new double[intervals];
            for (int i = 0; i < intervals; i++)
            {
                double sum = 0;
                for (int r = 0; r < n; r++)
                {
                    double t = newtonResidual[((first + i) * n) + r] * (1.5 / h[i])
                        / Math.Max(Math.Abs(fmid[((first + i) * n) + r]), threshold[r]);
                    sum += t * t;
                }

                value[i] = Lobw3 * sum;
            }

            foreach (double node in (double[])[lob2, lob4])
            {
                var xlob = new double[intervals];
                var ylob = new double[intervals][];
                var yplob = new double[intervals][];
                for (int i = 0; i < intervals; i++)
                {
                    xlob[i] = x[first + i] + (node * h[i]);
                    (ylob[i], yplob[i]) = HermiteInside(n, node, h[i],
                        Column(y, n, first + i), Column(y, n, first + i + 1),
                        Column(yp, n, first + i), Column(yp, n, first + i + 1));
                }

                double[][] flob;
                if (problem.Vectorized)
                {
                    flob = problem.VectorDerivative(xlob, ylob, region + 1, parameters);
                    calls++;
                }
                else
                {
                    flob = new double[intervals][];
                    for (int i = 0; i < intervals; i++)
                    {
                        flob[i] = problem.Derivative(xlob[i], ylob[i], region + 1, parameters);
                    }

                    calls += intervals;
                }

                for (int i = 0; i < intervals; i++)
                {
                    double sum = 0;
                    for (int r = 0; r < n; r++)
                    {
                        double t = (yplob[i][r] - flob[i][r]) / Math.Max(Math.Abs(flob[i][r]), threshold[r]);
                        sum += t * t;
                    }

                    value[i] += Lobw24 * sum;
                }
            }

            for (int i = 0; i < intervals; i++)
            {
                residual[first + i] = Math.Sqrt(Math.Abs(h[i] / 2) * value[i]);
            }
        }

        return (residual, calls);
    }

    /// <summary>The Hermite cubic of one interval and its slope, at the fraction <paramref name="node"/> along it.</summary>
    private static (double[] Value, double[] Slope) HermiteInside(int n, double node, double h,
        double[] y, double[] ynext, double[] yp, double[] ypnext)
    {
        var value = new double[n];
        var slope = new double[n];
        double scale = 1 / h;
        double s = node * h;
        for (int r = 0; r < n; r++)
        {
            double gradient = (ynext[r] - y[r]) * scale;
            double c = ((3 * gradient) - (2 * yp[r]) - ypnext[r]) * scale;
            double d = (yp[r] + ypnext[r] - (2 * gradient)) * scale * scale;
            value[r] = ((((d * s) + c) * s) + yp[r]) * s + y[r];
            slope[r] = (((3 * d * s) + (2 * c)) * s) + yp[r];
        }

        return (value, slope);
    }

    /// <summary>MATLAB's <c>ntrp3h</c>: the Hermite cubic through two points and their slopes.</summary>
    public static (double[] Value, double[] Slope) Hermite3(double at, double t, double[] y,
        double tnew, double[] ynew, double[] yp, double[] ypnew)
    {
        int n = y.Length;
        double h = tnew - t;
        double s = (at - t) / h;
        double s2 = s * s;
        double s3 = s * s2;
        var value = new double[n];
        var slope = new double[n];
        for (int r = 0; r < n; r++)
        {
            double gradient = (ynew[r] - y[r]) / h;
            double c = (3 * gradient) - (2 * yp[r]) - ypnew[r];
            double d = yp[r] + ypnew[r] - (2 * gradient);
            value[r] = y[r] + ((h * d * s3) + (h * c * s2) + (h * yp[r] * s));
            slope[r] = yp[r] + ((3 * d * s2) + (2 * c * s));
        }

        return (value, slope);
    }

    /// <summary>MATLAB's <c>ntrp4h</c>: the quartic through both ends, both slopes and the midpoint.</summary>
    public static (double[] Value, double[] Slope) Hermite4(double at, double t, double[] y,
        double tnew, double[] ynew, double[] ymid, double[] yp, double[] ypnew)
    {
        int n = y.Length;
        double h = tnew - t;
        double s = (at - t) / h;
        double s2 = s * s;
        double s3 = s * s2;
        double s4 = s * s3;
        var value = new double[n];
        var slope = new double[n];
        for (int r = 0; r < n; r++)
        {
            double y0p = h * yp[r];
            double y1p = h * ypnew[r];
            double del1 = ymid[r] - y[r];
            double del2 = ynew[r] - ymid[r];
            double del3 = y1p - y0p;
            double a2 = ((11 * del1) - (5 * del2) + del3) - (3 * y0p);
            double a3 = ((-18 * del1) + (14 * del2) - (3 * del3)) + (2 * y0p);
            double a4 = ((8 * del1) - (8 * del2)) + (2 * del3);
            value[r] = y[r] + ((y0p * s) + (a2 * s2) + (a3 * s3) + (a4 * s4));
            slope[r] = (y0p + ((2 * a2 * s) + (3 * a3 * s2) + (4 * a4 * s3))) / h;
        }

        return (value, slope);
    }

    /// <summary>
    /// The mesh redistributed: an interval whose residual is over the tolerance gains a point, or
    /// two when it is a hundred times over; three quiet intervals in a row are replaced by two when
    /// the residual that would predict says they can be.
    /// </summary>
    private static (int Count, double[] X, double[] Y, int[] Interfaces) NewProfile(int n, double[] x, double[] y,
        double[] yp, double[] residual, int[] interfaces, double rtol, int nmax, bool canRemovePoints)
    {
        int meshCount = x.Length;
        int nregions = interfaces.Length + 1;
        (int[] left, int[] right) = RegionBounds(interfaces, meshCount);

        var newInterfaces = new List<int>();
        var newX = new List<double>();
        var newY = new List<double>();
        int total = 0;

        for (int region = 0; region < nregions; region++)
        {
            int first = left[region];
            int last = right[region];
            int points = last - first + 1;
            int lastInterval = points - 1;

            var xreg = new List<double> { x[first] };
            var yreg = new List<double[]> { Column(y, n, first) };
            int i = 0;
            while (i < lastInterval)
            {
                double here = residual[first + i];
                if (here > rtol)
                {
                    int added = here > 100 * rtol ? 2 : 1;
                    double h = (x[first + i + 1] - x[first + i]) / (added + 1);
                    double bottom = xreg[^1];
                    for (int j = 1; j <= added; j++)
                    {
                        double place = bottom + (j * h);
                        xreg.Add(place);
                        yreg.Add(Hermite3(place, x[first + i], Column(y, n, first + i),
                            x[first + i + 1], Column(y, n, first + i + 1),
                            Column(yp, n, first + i), Column(yp, n, first + i + 1)).Value);
                    }
                }
                else if (canRemovePoints && i <= lastInterval - 3
                         && residual[first + i + 1] < rtol && residual[first + i + 2] < rtol)
                {
                    double h0 = x[first + i + 1] - x[first + i];
                    double h1 = x[first + i + 2] - x[first + i + 1];
                    double h2 = x[first + i + 3] - x[first + i + 2];
                    double hnew = (h0 + h1 + h2) / 2;
                    double predicted = Math.Max(
                        residual[first + i] / Math.Pow(h0 / hnew, 3.5),
                        Math.Max(residual[first + i + 1] / Math.Pow(h1 / hnew, 3.5),
                            residual[first + i + 2] / Math.Pow(h2 / hnew, 3.5)));
                    if (predicted < 0.5 * rtol)
                    {
                        double place = xreg[^1] + hnew;
                        xreg.Add(place);
                        yreg.Add(Hermite3(place, x[first + i + 1], Column(y, n, first + i + 1),
                            x[first + i + 2], Column(y, n, first + i + 2),
                            Column(yp, n, first + i + 1), Column(yp, n, first + i + 2)).Value);
                        i += 2;
                    }
                }

                xreg.Add(x[first + i + 1]);
                yreg.Add(Column(y, n, first + i + 1));
                i++;
            }

            total += xreg.Count;
            if (total > nmax)
            {
                // Over the ceiling: the previous mesh and solution are what the caller gets back.
                return (total, (double[])x.Clone(), (double[])y.Clone(), interfaces);
            }

            newX.AddRange(xreg);
            foreach (double[] column in yreg)
            {
                newY.AddRange(column);
            }

            if (region < nregions - 1)
            {
                newInterfaces.Add(total - 1);
            }
        }

        return (total, [.. newX], [.. newY], [.. newInterfaces]);
    }

    // --- the global Jacobian ---------------------------------------------------------------------

    /// <summary>What the numerically differenced Jacobians remember between calls.</summary>
    private sealed class BvpJacobianState(int n, int npar, bool vectorized)
    {
        public OdeJacobianOptions Ode { get; } = new() { Threshold = Filled(n, JacobianThreshold) };

        public OdeJacobianOptions Parameter { get; } = new() { Threshold = Filled(Math.Max(npar, 1), JacobianThreshold) };

        public bool Vectorized { get; } = vectorized;
    }

    /// <summary>
    /// The global Jacobian of the collocation equations, as triplets: the boundary conditions on
    /// top, then two n-by-n blocks per mesh interval, then a column apiece for the unknown
    /// parameters.
    /// </summary>
    private static List<(int, int, double)> CollocationJacobian(BvpProblem problem, int n, double[] x,
        double[] big, double[] f, double[] fmid, int[] interfaces, double[] parameters,
        BvpJacobianState state, out int odeCalls, out int bcCalls)
    {
        int meshCount = x.Length;
        int nregions = interfaces.Length + 1;
        int npar = problem.Npar;
        int nBCs = (n * nregions) + npar;
        (int[] left, int[] right) = RegionBounds(interfaces, meshCount);
        int parameterColumn = n * meshCount;

        var triplets = new List<(int, int, double)>();
        double[][] ya = Columns(big, n, left);
        double[][] yb = Columns(big, n, right);

        double[,] dGdya;
        double[,] dGdyb;
        double[,]? dGdp = null;
        bcCalls = 0;
        if (problem.BoundaryJacobian is { } bcjac)
        {
            (dGdya, dGdyb, dGdp) = bcjac(ya, yb, parameters);
        }
        else if (problem.ConstantBoundaryJacobianYa is { } constantYa)
        {
            dGdya = constantYa;
            dGdyb = problem.ConstantBoundaryJacobianYb!;
            dGdp = problem.ConstantBoundaryJacobianP;
        }
        else
        {
            (dGdya, dGdyb, dGdp, bcCalls) = BoundaryNumericalJacobian(problem, n, nregions, npar, ya, yb, parameters);
        }

        for (int region = 0; region < nregions; region++)
        {
            for (int c = 0; c < n; c++)
            {
                for (int r = 0; r < nBCs; r++)
                {
                    triplets.Add((r, (left[region] * n) + c, dGdya[r, (region * n) + c]));
                    triplets.Add((r, (right[region] * n) + c, dGdyb[r, (region * n) + c]));
                }
            }
        }

        if (npar > 0 && dGdp is not null)
        {
            for (int c = 0; c < npar; c++)
            {
                for (int r = 0; r < nBCs; r++)
                {
                    triplets.Add((r, parameterColumn + c, dGdp[r, c]));
                }
            }
        }

        odeCalls = 0;
        int rowOffset = 0;
        int columnOffset = 0;
        int parameterRow = 0;
        for (int region = 0; region < nregions; region++)
        {
            int first = left[region];
            int last = right[region];
            int intervals = last - first;
            state.Ode.Increments = null;
            state.Parameter.Increments = null;

            double[,]? previous = null;
            double[,]? previousParameter = null;
            for (int i = 0; i < intervals; i++)
            {
                double h = x[first + i + 1] - x[first + i];
                double[] yi = Column(big, n, first + i);
                double[] ynext = Column(big, n, first + i + 1);
                double[] fi = Column(f, n, first + i);
                double[] fnext = Column(f, n, first + i + 1);
                double xmid = (x[first + i] + x[first + i + 1]) / 2;

                double[,] ji;
                double[,] jnext;
                double[,]? jmid = null;
                double[,]? dpi = null;
                double[,]? dpnext = null;
                double[,]? dpmid = null;

                if (problem.ConstantJacobian is { } constant)
                {
                    double[,] j = Block(constant, n, region * n, n);
                    double[,] j2 = Multiply(j, j);
                    var lower = new double[n, n];
                    var upper = new double[n, n];
                    for (int r = 0; r < n; r++)
                    {
                        for (int c = 0; c < n; c++)
                        {
                            double identity = r == c ? 1 : 0;
                            double half = h / 2 * j[r, c];
                            double twelfth = h * h / 12 * j2[r, c];
                            lower[r, c] = -(identity + half + twelfth);
                            upper[r, c] = identity - half + twelfth;
                        }
                    }

                    Emit(triplets, lower, upper, nBCs + rowOffset + (i * n), columnOffset + (i * n), n);
                    if (npar > 0 && problem.ConstantParameterJacobian is { } constantP)
                    {
                        for (int r = 0; r < n; r++)
                        {
                            for (int c = 0; c < npar; c++)
                            {
                                triplets.Add((nBCs + parameterRow + r, parameterColumn + c,
                                    -h * constantP[r, (region * npar) + c]));
                            }
                        }
                    }

                    parameterRow += n;
                    continue;
                }

                if (problem.JacobianFunction is { } jac)
                {
                    if (previous is null)
                    {
                        (previous, previousParameter) = jac(x[first], Column(big, n, first), region + 1, parameters);
                    }

                    ji = previous;
                    dpi = previousParameter;
                    (jnext, dpnext) = jac(x[first + i + 1], ynext, region + 1, parameters);
                    var ymid = new double[n];
                    for (int r = 0; r < n; r++)
                    {
                        ymid[r] = ((yi[r] + ynext[r]) / 2) - (h / 8 * (fnext[r] - fi[r]));
                    }

                    (jmid, dpmid) = jac(xmid, ymid, region + 1, parameters);
                    previous = jnext;
                    previousParameter = dpnext;
                }
                else
                {
                    if (previous is null)
                    {
                        previous = OdeNumericalJacobian.Compute(
                            v => problem.Derivative(x[first], v, region + 1, parameters),
                            problem.VectorizedFor(x[first], region + 1, parameters, state.Vectorized),
                            Column(big, n, first), Column(f, n, first), state.Ode, out int firstCalls);
                        odeCalls += state.Vectorized ? 1 + Math.Max(0, firstCalls - n) : firstCalls;
                        if (npar > 0)
                        {
                            previousParameter = ParameterJacobian(problem, x[first], Column(big, n, first),
                                region + 1, parameters, Column(f, n, first), state, out int parCalls);
                            odeCalls += parCalls;
                        }
                    }

                    ji = previous;
                    dpi = previousParameter;
                    jnext = OdeNumericalJacobian.Compute(
                        v => problem.Derivative(x[first + i + 1], v, region + 1, parameters),
                        problem.VectorizedFor(x[first + i + 1], region + 1, parameters, state.Vectorized),
                        ynext, fnext, state.Ode, out int calls);
                    odeCalls += state.Vectorized ? 1 + Math.Max(0, calls - n) : calls;
                    if (npar > 0)
                    {
                        dpnext = ParameterJacobian(problem, x[first + i + 1], ynext, region + 1, parameters,
                            fnext, state, out int parCalls);
                        odeCalls += parCalls;
                    }

                    // The midpoint Jacobian is averaged rather than computed whenever the two ends
                    // agree to within a quarter of their own size — which is most of the mesh, and
                    // most of what makes a numerical Jacobian affordable here.
                    bool close = OneNormOfDifference(jnext, ji) <= 0.25 * (OneNorm(ji) + OneNorm(jnext));
                    if (npar > 0)
                    {
                        close &= OneNormOfDifference(dpnext!, dpi!) <= 0.25 * (OneNorm(dpi!) + OneNorm(dpnext!));
                    }

                    if (close)
                    {
                        jmid = Average(ji, jnext);
                        if (npar > 0)
                        {
                            dpmid = Average(dpi!, dpnext!);
                        }
                    }
                    else
                    {
                        var ymid = new double[n];
                        for (int r = 0; r < n; r++)
                        {
                            ymid[r] = ((yi[r] + ynext[r]) / 2) - (h / 8 * (fnext[r] - fi[r]));
                        }

                        double[] fmidpoint = Column(fmid, n, first + i);
                        jmid = OdeNumericalJacobian.Compute(
                            v => problem.Derivative(xmid, v, region + 1, parameters),
                            problem.VectorizedFor(xmid, region + 1, parameters, state.Vectorized),
                            ymid, fmidpoint, state.Ode, out int midCalls);
                        odeCalls += state.Vectorized ? 1 + Math.Max(0, midCalls - n) : midCalls;
                        if (npar > 0)
                        {
                            dpmid = ParameterJacobian(problem, xmid, ymid, region + 1, parameters,
                                fmidpoint, state, out int parCalls);
                            odeCalls += parCalls;
                        }
                    }

                    previous = jnext;
                    previousParameter = dpnext;
                }

                double[,] twiceMid = Scale(jmid!, 2);
                double[,] lowerBlock = MinusIdentityTerm(ji, twiceMid, h, n, true);
                double[,] upperBlock = MinusIdentityTerm(jnext, twiceMid, h, n, false);
                Emit(triplets, lowerBlock, upperBlock, nBCs + rowOffset + (i * n), columnOffset + (i * n), n);

                if (npar > 0)
                {
                    double[,] difference = new double[n, npar];
                    for (int r = 0; r < n; r++)
                    {
                        for (int c = 0; c < npar; c++)
                        {
                            difference[r, c] = dpnext![r, c] - dpi![r, c];
                        }
                    }

                    double[,] correction = Multiply(jmid!, difference);
                    for (int r = 0; r < n; r++)
                    {
                        for (int c = 0; c < npar; c++)
                        {
                            triplets.Add((nBCs + parameterRow + r, parameterColumn + c,
                                (-h * dpmid![r, c]) + (h * h / 12 * correction[r, c])));
                        }
                    }
                }

                parameterRow += n;
            }

            rowOffset = (right[region] - region) * n;
            columnOffset = (right[region] + 1) * n;
        }

        return triplets;
    }

    /// <summary><c>-(I + h/6·(J + 2J½·(I + h/4·J)))</c> and its mirror at the right end of the interval.</summary>
    private static double[,] MinusIdentityTerm(double[,] j, double[,] twiceMid, double h, int n, bool lower)
    {
        double sign = lower ? 1 : -1;
        var inner = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                inner[r, c] = (r == c ? 1 : 0) + (sign * h / 4 * j[r, c]);
            }
        }

        double[,] product = Multiply(twiceMid, inner);
        var block = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                double value = (r == c ? 1 : 0) + (sign * h / 6 * (j[r, c] + product[r, c]));
                block[r, c] = lower ? -value : value;
            }
        }

        return block;
    }

    private static void Emit(List<(int, int, double)> triplets, double[,] lower, double[,] upper,
        int row, int column, int n)
    {
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                triplets.Add((row + r, column + c, lower[r, c]));
                triplets.Add((row + r, column + n + c, upper[r, c]));
            }
        }
    }

    /// <summary><c>df/dp</c> by differences, one call of the derivative per unknown parameter.</summary>
    private static double[,] ParameterJacobian(BvpProblem problem, double x, double[] y, int region,
        double[] parameters, double[] value, BvpJacobianState state, out int calls)
    {
        double[,] jacobian = OdeNumericalJacobian.Compute(
            p => problem.Derivative(x, y, region, p), null, parameters, value, state.Parameter, out int evaluations);
        calls = evaluations;
        return jacobian;
    }

    /// <summary>
    /// <c>dbc/dya</c>, <c>dbc/dyb</c> and <c>dbc/dp</c> by differences. The boundary conditions see
    /// every region's end at once, so the differentiation runs over the whole stacked vector.
    /// </summary>
    private static (double[,] Dya, double[,] Dyb, double[,]? Dp, int Calls) BoundaryNumericalJacobian(
        BvpProblem problem, int n, int nregions, int npar, double[][] ya, double[][] yb, double[] parameters)
    {
        double[] flatYa = Flatten(ya);
        double[] flatYb = Flatten(yb);
        double[] value = problem.Boundary(ya, yb, parameters);
        int calls = 1;

        var options = new OdeJacobianOptions { Threshold = Filled(nregions * n, JacobianThreshold) };
        double[,] dya = OdeNumericalJacobian.Compute(
            v => problem.Boundary(Split(v, n, nregions), yb, parameters), null, flatYa, value, options, out int callsYa);
        calls += callsYa;

        var optionsB = new OdeJacobianOptions { Threshold = Filled(nregions * n, JacobianThreshold) };
        double[,] dyb = OdeNumericalJacobian.Compute(
            v => problem.Boundary(ya, Split(v, n, nregions), parameters), null, flatYb, value, optionsB, out int callsYb);
        calls += callsYb;

        double[,]? dp = null;
        if (npar > 0)
        {
            var optionsP = new OdeJacobianOptions { Threshold = Filled(npar, JacobianThreshold) };
            dp = OdeNumericalJacobian.Compute(
                p => problem.Boundary(ya, yb, p), null, parameters, value, optionsP, out int callsP);
            calls += callsP;
        }

        return (dya, dyb, dp, calls);
    }

    // --- shared plumbing --------------------------------------------------------------------------

    /// <summary>The problem as the two solvers read it, with the singular term already folded in.</summary>
    private sealed class BvpProblem
    {
        public required int N { get; init; }

        public required int Npar { get; init; }

        public required int Regions { get; init; }

        public required double[] X { get; init; }

        public required double[] Y { get; init; }

        public required double[] Parameters { get; init; }

        public required double RelativeTolerance { get; init; }

        public required double[] Threshold { get; init; }

        public required int MaxMeshPoints { get; init; }

        public required bool Vectorized { get; init; }

        public required bool Stats { get; init; }

        public required BvpOdeFunction Derivative { get; init; }

        public required BvpVectorOdeFunction VectorDerivative { get; init; }

        public required BvpBoundaryFunction Boundary { get; init; }

        public BvpOdeJacobianFunction? JacobianFunction { get; init; }

        public double[,]? ConstantJacobian { get; init; }

        public double[,]? ConstantParameterJacobian { get; init; }

        public BvpBoundaryJacobianFunction? BoundaryJacobian { get; init; }

        public double[,]? ConstantBoundaryJacobianYa { get; init; }

        public double[,]? ConstantBoundaryJacobianYb { get; init; }

        public double[,]? ConstantBoundaryJacobianP { get; init; }

        public bool Singular { get; init; }

        public double[,]? BoundaryProjection { get; init; }

        public Action<string>? Warn { get; init; }

        public Action<string>? Print { get; init; }

        /// <summary>The derivative over the perturbed states of one numerical Jacobian column sweep.</summary>
        public Func<double[][], double[][]>? VectorizedFor(double x, int region, double[] parameters, bool on)
        {
            if (!on)
            {
                return null;
            }

            return states =>
            {
                var repeated = new double[states.Length];
                Array.Fill(repeated, x);
                return VectorDerivative(repeated, states, region, parameters);
            };
        }

        public static BvpProblem Create(string solver, BvpOdeFunction ode, BvpVectorOdeFunction? vectorOde,
            BvpBoundaryFunction bc, BvpGuess guess, BvpOptions options)
        {
            double[] x = guess.X;
            int meshCount = x.Length;
            if (meshCount < 2)
            {
                throw new BvpException("MATLAB:bvparguments:SolinitXNotEnoughPts",
                    $"Error calling {solver.ToUpperInvariant()}(ODEFUN,BCFUN,SOLINIT): SOLINIT.x must have at least two entries.");
            }

            int n = guess.Y.Length / meshCount;
            int npar = guess.Parameters?.Length ?? 0;
            int nregions = InterfacesOf(x).Length + 1;

            double rtol = options.RelativeTolerance;
            if (rtol < 100 * Epsilon)
            {
                rtol = 100 * Epsilon;
                options.Warn?.Invoke(
                    $"Warning calling {solver.ToUpperInvariant()}(ODEFUN,BCFUN,SOLINIT): RelTol has been increased to {rtol:G}.");
            }

            double[] atol = options.AbsoluteTolerance is { Length: > 0 } given
                ? given.Length == 1 ? Filled(n, given[0]) : (double[])given.Clone()
                : Filled(n, 1e-6);
            var threshold = new double[n];
            for (int i = 0; i < n; i++)
            {
                threshold[i] = atol[i] / rtol;
            }

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
            BvpOdeJacobianFunction? jacobian = options.JacobianFunction;
            double[,]? constantJacobian = options.ConstantJacobian;
            double[] y = (double[])guess.Y.Clone();
            bool singular = false;
            double[,]? projection = null;

            if (options.SingularTerm is { } s)
            {
                if (x[0] != 0 || x[^1] <= x[0])
                {
                    throw new BvpException("MATLAB:bvpsingular:SingBVPInvalidInterval",
                        "Singular BVPs must be posed on the interval [0,b] with b > 0.");
                }

                if (s.GetLength(0) != n || s.GetLength(1) != n)
                {
                    throw new BvpException("MATLAB:bvpsingular:SingBVPInvalidS",
                        "The 'SingularTerm' option must be set to a constant matrix with as many rows and columns as there are equations.");
                }

                singular = true;

                // The necessary condition S·y(0) = 0 is imposed by projecting the first column of
                // the guess, and again on every Newton iterate.
                projection = Subtract(Identity(n), Multiply(PseudoInverse(s), s));
                double[] head = new double[n];
                Array.Copy(y, head, n);
                double[] moved = Multiply(projection, head);
                Array.Copy(moved, y, n);

                double[,] resolvent = PseudoInverse(Subtract(Identity(n), s));
                BvpOdeFunction bare = derivative;
                derivative = (xx, yy, region, p) =>
                {
                    double[] value = bare(xx, yy, region, p);
                    if (xx == 0)
                    {
                        return Multiply(resolvent, value);
                    }

                    double[] extra = Multiply(s, yy);
                    var answer = new double[value.Length];
                    for (int i = 0; i < value.Length; i++)
                    {
                        answer[i] = value[i] + (extra[i] / xx);
                    }

                    return answer;
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
                            double[] extra = Multiply(s, ys[i]);
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
                    jacobian = (xx, yy, region, p) =>
                    {
                        (double[,] dy, double[,]? dp) = bareJacobian(xx, yy, region, p);
                        if (xx == 0)
                        {
                            return (Multiply(resolvent, dy), dp is null ? null : Multiply(resolvent, dp));
                        }

                        var moved2 = new double[n, n];
                        for (int r = 0; r < n; r++)
                        {
                            for (int c = 0; c < n; c++)
                            {
                                moved2[r, c] = dy[r, c] + (s[r, c] / xx);
                            }
                        }

                        return (moved2, dp);
                    };
                }
                else if (constantJacobian is { } bareConstant)
                {
                    double[,] kept = bareConstant;
                    jacobian = (xx, _, region, _) =>
                    {
                        double[,] dy = Block(kept, n, (region - 1) * n, n);
                        if (xx == 0)
                        {
                            return (Multiply(resolvent, dy), null);
                        }

                        var moved2 = new double[n, n];
                        for (int r = 0; r < n; r++)
                        {
                            for (int c = 0; c < n; c++)
                            {
                                moved2[r, c] = dy[r, c] + (s[r, c] / xx);
                            }
                        }

                        return (moved2, null);
                    };

                    constantJacobian = null;
                }
            }

            return new BvpProblem
            {
                N = n,
                Npar = npar,
                Regions = nregions,
                X = x,
                Y = y,
                Parameters = guess.Parameters is null ? [] : (double[])guess.Parameters.Clone(),
                RelativeTolerance = rtol,
                Threshold = threshold,
                MaxMeshPoints = options.MaxMeshPoints ?? (int)Math.Floor(10000.0 / n),
                Vectorized = options.Vectorized && vectorOde is not null,
                Stats = options.Stats,
                Derivative = derivative,
                VectorDerivative = vector,
                Boundary = bc,
                JacobianFunction = jacobian,
                ConstantJacobian = constantJacobian,
                ConstantParameterJacobian = options.ConstantParameterJacobian,
                BoundaryJacobian = options.BoundaryJacobianFunction,
                ConstantBoundaryJacobianYa = options.ConstantBoundaryJacobianYa,
                ConstantBoundaryJacobianYb = options.ConstantBoundaryJacobianYb,
                ConstantBoundaryJacobianP = options.ConstantBoundaryJacobianP,
                Singular = singular,
                BoundaryProjection = projection,
                Warn = options.Warn,
                Print = options.Print,
            };
        }
    }

    /// <summary>The indices, zero-based, of the mesh points a multipoint problem repeats.</summary>
    public static int[] InterfacesOf(double[] x)
    {
        var found = new List<int>();
        for (int i = 0; i + 1 < x.Length; i++)
        {
            if (x[i + 1] == x[i])
            {
                found.Add(i);
            }
        }

        return [.. found];
    }

    /// <summary>The first and last mesh index of every region.</summary>
    private static (int[] Left, int[] Right) RegionBounds(int[] interfaces, int meshCount)
    {
        int nregions = interfaces.Length + 1;
        var left = new int[nregions];
        var right = new int[nregions];
        left[0] = 0;
        for (int i = 0; i < interfaces.Length; i++)
        {
            right[i] = interfaces[i];
            left[i + 1] = interfaces[i] + 1;
        }

        right[nregions - 1] = meshCount - 1;
        return (left, right);
    }

    private static double[] Column(double[] flat, int n, int index)
    {
        var column = new double[n];
        Array.Copy(flat, index * n, column, 0, n);
        return column;
    }

    private static double[][] Columns(double[] flat, int n, int[] indices)
    {
        var columns = new double[indices.Length][];
        for (int i = 0; i < indices.Length; i++)
        {
            columns[i] = Column(flat, n, indices[i]);
        }

        return columns;
    }

    private static double[] Flatten(double[][] columns)
    {
        var flat = new double[columns.Length * columns[0].Length];
        for (int i = 0; i < columns.Length; i++)
        {
            Array.Copy(columns[i], 0, flat, i * columns[0].Length, columns[0].Length);
        }

        return flat;
    }

    private static double[][] Split(double[] flat, int n, int count)
    {
        var columns = new double[count][];
        for (int i = 0; i < count; i++)
        {
            columns[i] = new double[n];
            Array.Copy(flat, i * n, columns[i], 0, n);
        }

        return columns;
    }

    private static double[] Filled(int count, double value)
    {
        var array = new double[count];
        Array.Fill(array, value);
        return array;
    }

    private static double TwoNorm(double[] v)
    {
        double sum = 0;
        foreach (double value in v)
        {
            sum += value * value;
        }

        return Math.Sqrt(sum);
    }

    private static double Max(double[] v)
    {
        double best = double.NegativeInfinity;
        foreach (double value in v)
        {
            best = Math.Max(best, value);
        }

        return v.Length == 0 ? 0 : best;
    }

    private static double OneNorm(double[,] a)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        double best = 0;
        for (int c = 0; c < cols; c++)
        {
            double sum = 0;
            for (int r = 0; r < rows; r++)
            {
                sum += Math.Abs(a[r, c]);
            }

            best = Math.Max(best, sum);
        }

        return best;
    }

    private static double OneNormOfDifference(double[,] a, double[,] b)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        double best = 0;
        for (int c = 0; c < cols; c++)
        {
            double sum = 0;
            for (int r = 0; r < rows; r++)
            {
                sum += Math.Abs(a[r, c] - b[r, c]);
            }

            best = Math.Max(best, sum);
        }

        return best;
    }

    private static double[,] Average(double[,] a, double[,] b)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        var answer = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                answer[r, c] = 0.5 * (a[r, c] + b[r, c]);
            }
        }

        return answer;
    }

    private static double[,] Scale(double[,] a, double factor)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        var answer = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                answer[r, c] = factor * a[r, c];
            }
        }

        return answer;
    }

    private static double[,] Multiply(double[,] a, double[,] b)
    {
        int rows = a.GetLength(0);
        int inner = a.GetLength(1);
        int cols = b.GetLength(1);
        var answer = new double[rows, cols];
        for (int c = 0; c < cols; c++)
        {
            for (int k = 0; k < inner; k++)
            {
                double value = b[k, c];
                if (value == 0)
                {
                    continue;
                }

                for (int r = 0; r < rows; r++)
                {
                    answer[r, c] += a[r, k] * value;
                }
            }
        }

        return answer;
    }

    private static double[] Multiply(double[,] a, double[] v)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        var answer = new double[rows];
        for (int r = 0; r < rows; r++)
        {
            double sum = 0;
            for (int c = 0; c < cols; c++)
            {
                sum += a[r, c] * v[c];
            }

            answer[r] = sum;
        }

        return answer;
    }

    private static double[,] Subtract(double[,] a, double[,] b)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        var answer = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                answer[r, c] = a[r, c] - b[r, c];
            }
        }

        return answer;
    }

    private static double[,] Identity(int n)
    {
        var answer = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            answer[i, i] = 1;
        }

        return answer;
    }

    /// <summary>One n-by-<paramref name="width"/> block of a wide matrix, starting at <paramref name="from"/>.</summary>
    private static double[,] Block(double[,] a, int rows, int from, int width)
    {
        var answer = new double[rows, width];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < width; c++)
            {
                answer[r, c] = a[r, from + c];
            }
        }

        return answer;
    }

    /// <summary>The Moore–Penrose pseudoinverse, at MATLAB's default tolerance.</summary>
    private static double[,] PseudoInverse(double[,] a)
    {
        int m = a.GetLength(0);
        int n = a.GetLength(1);
        var flat = new double[m * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < m; r++)
            {
                flat[r + (c * m)] = a[r, c];
            }
        }

        Svd svd = Svd.Factor(flat, m, n);
        double[] values = svd.Values;
        double tolerance = Math.Max(m, n) * Epsilon * (values.Length > 0 ? values[0] : 0);
        double[,] u = svd.U;
        double[,] v = svd.V;
        var answer = new double[n, m];
        for (int k = 0; k < values.Length; k++)
        {
            if (values[k] <= tolerance)
            {
                continue;
            }

            double inverse = 1 / values[k];
            for (int r = 0; r < n; r++)
            {
                double left = v[r, k] * inverse;
                for (int c = 0; c < m; c++)
                {
                    answer[r, c] += left * u[c, k];
                }
            }
        }

        return answer;
    }
}

/// <summary>A refusal from a boundary value or delay solver, carrying MATLAB's own identifier.</summary>
public sealed class BvpException(string identifier, string message) : Exception(message)
{
    /// <summary>MATLAB's message identifier for this refusal.</summary>
    public string Identifier { get; } = identifier;
}
