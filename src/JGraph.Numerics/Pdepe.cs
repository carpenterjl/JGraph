namespace JGraph.Numerics;

/// <summary>
/// The coefficients of one equation of the system at a point: the diagonal of <c>c</c>, the flux
/// <c>f</c> and the source <c>s</c>, each one entry per unknown.
/// </summary>
public readonly record struct PdeCoefficientReading(double[] C, double[] F, double[] S);

/// <summary>
/// <c>[c, f, s] = pdefun(x, t, u, dudx)</c> — the quantities that define
/// <c>c·∂u/∂t = x^-m ∂(x^m f)/∂x + s</c> at one point of one subinterval.
/// </summary>
public delegate PdeCoefficientReading PdeCoefficients(double x, double t, double[] u, double[] dudx);

/// <summary><c>u0 = icfun(x)</c> — the initial values of every unknown at one mesh point.</summary>
public delegate double[] PdeInitialCondition(double x);

/// <summary>What the boundary conditions say at one time: <c>p + q·f = 0</c> at each end.</summary>
public readonly record struct PdeBoundaryReading(double[] PL, double[] QL, double[] PR, double[] QR);

/// <summary><c>[pl, ql, pr, qr] = bcfun(xl, ul, xr, ur, t)</c>.</summary>
public delegate PdeBoundaryReading PdeBoundary(double xl, double[] ul, double xr, double[] ur, double t);

/// <summary>
/// The event function <c>pdepe</c> hands the solver: it sees the whole mesh solution rather than one
/// state, which is why it cannot simply be the ODE family's own <c>Events</c>.
/// </summary>
public delegate OdeEventReading PdeEventFunction(double t, double[] umesh);

/// <summary>What one <c>pdepe</c> run answered.</summary>
public sealed class PdeResult
{
    /// <summary>The times the solution is reported at, which are the requested ones up to a terminal event.</summary>
    public required double[] Times { get; init; }

    /// <summary>The mesh solution at each of those times, one entry per unknown per mesh point, unknown fastest.</summary>
    public required double[][] States { get; init; }

    /// <summary>The times events were located at.</summary>
    public required double[] EventTimes { get; init; }

    /// <summary>The mesh solution at each of those times.</summary>
    public required double[][] EventStates { get; init; }

    /// <summary>Which event fired each time, 0-based.</summary>
    public required int[] EventIndices { get; init; }

    /// <summary>How many unknowns the system has.</summary>
    public required int Npde { get; init; }

    /// <summary>How many mesh points there are.</summary>
    public required int Nx { get; init; }

    /// <summary>What the underlying <c>ode15s</c> run cost.</summary>
    public required OdeResult Ode { get; init; }
}

/// <summary>
/// <c>pdepe</c>: initial-boundary value problems for a small system of parabolic and elliptic
/// equations in one space variable, by the Skeel–Berzins discretisation on the caller's mesh and
/// the method of lines through <see cref="Ode15s"/>.
/// </summary>
/// <remarks>
/// <para>
/// The discretisation is what makes this more than a difference scheme wrapped round a stiff solver.
/// Skeel and Berzins integrate the equation over a half-cell either side of each mesh point and
/// approximate the flux at one interior point <c>ξ</c> of each subinterval — the point at which the
/// quadrature is second order for the symmetry the problem has, which is why <c>ξ</c> is the
/// midpoint for a slab, a logarithmic mean for a cylinder and a harmonic-flavoured mean for a
/// sphere. Where the interval starts at the axis and <c>m &gt; 0</c> the ordinary formulas divide by
/// zero, so a second family is used on every subinterval instead of just the first: keeping one
/// formula throughout is what keeps the scheme's order uniform.
/// </para>
/// <para>
/// The result is not an ODE system but a differential-algebraic one. An equation whose <c>c</c> is
/// identically zero is elliptic and contributes no time derivative, and a boundary condition with
/// <c>q = 0</c> is a constraint on the solution rather than an equation for its rate. Both are
/// expressed by zeroing the matching diagonal entry of a constant mass matrix and letting the
/// right-hand side carry the residual, which is exactly the shape <c>ode15s</c> has solved since
/// M126. The Jacobian is block tridiagonal because each mesh point is coupled only to its
/// neighbours, and saying so through <c>JPattern</c> turns the numerical Jacobian from one
/// derivative evaluation per unknown into a handful.
/// </para>
/// </remarks>
public static class Pdepe
{
    /// <summary>Solves the problem on the caller's mesh and answers the solution at the requested times.</summary>
    /// <param name="m">0, 1 or 2 — slab, cylindrical or spherical symmetry.</param>
    /// <param name="pde">The coefficients of the system.</param>
    /// <param name="ic">The initial conditions.</param>
    /// <param name="bc">The boundary conditions.</param>
    /// <param name="xmesh">A strictly increasing mesh of at least three points.</param>
    /// <param name="tspan">Strictly increasing times, at least three.</param>
    /// <param name="options">The <c>odeset</c> settings <c>pdepe</c> passes through, or null.</param>
    /// <param name="events">The event function, or null when nothing is watched.</param>
    public static PdeResult Solve(int m, PdeCoefficients pde, PdeInitialCondition ic, PdeBoundary bc,
        double[] xmesh, double[] tspan, OdeOptions? options, PdeEventFunction? events)
    {
        options ??= new OdeOptions();
        if (m is not (0 or 1 or 2))
        {
            throw new OdeArgumentException("MATLAB:pdepe:InvalidM",
                "The parameter m must be 0, 1, or 2.");
        }

        int nt = tspan.Length;
        if (nt < 3)
        {
            throw new OdeArgumentException("MATLAB:pdepe:TSPANnotEnoughPts",
                "The length of TSPAN must be at least 3.");
        }

        for (int j = 1; j < nt; j++)
        {
            if (tspan[j] - tspan[j - 1] <= 0)
            {
                throw new OdeArgumentException("MATLAB:pdepe:TSPANnotIncreasing",
                    "The entries of TSPAN must be strictly increasing.");
            }
        }

        int nx = xmesh.Length;
        if (m > 0 && xmesh[0] < 0)
        {
            throw new OdeArgumentException("MATLAB:pdepe:NegXMESHwithPosM",
                "For m > 0, the entries of XMESH must be non-negative.");
        }

        if (nx < 3)
        {
            throw new OdeArgumentException("MATLAB:pdepe:XMESHnotEnoughPts",
                "The length of XMESH must be at least 3.");
        }

        for (int j = 1; j < nx; j++)
        {
            if (xmesh[j] - xmesh[j - 1] <= 0)
            {
                throw new OdeArgumentException("MATLAB:pdepe:XMESHnotIncreasing",
                    "The entries of XMESH must be strictly increasing.");
            }
        }

        var geometry = Geometry.For(m, xmesh);
        bool singular = geometry.Singular;

        double[] first = ic(xmesh[0]);
        int npde = first.Length;
        int n = npde * nx;

        // The unknowns run with the equation index fastest, so that a mesh point's block is
        // contiguous and the Jacobian's block tridiagonal pattern is a banded one.
        var y0 = new double[n];
        Array.Copy(first, 0, y0, 0, npde);
        for (int j = 1; j < nx; j++)
        {
            double[] column = ic(xmesh[j]);
            if (column.Length != npde)
            {
                throw new OdeArgumentException("MATLAB:pdepe:InvalidOutputICFUN",
                    "ICFUN must return a column vector.");
            }

            Array.Copy(column, 0, y0, j * npde, npde);
        }

        // Classify the equations at one point that is not in the mesh: an entry of c is documented to
        // be identically zero or to vanish only at mesh points, so one interior sample settles it.
        double[] uLeft = Slice(y0, 0, npde);
        double[] uSecond = Slice(y0, npde, npde);
        Interpolate(singular, m, xmesh[0], uLeft, xmesh[1], uSecond, geometry.Xi[0],
            out double[] u0, out double[] ux0);
        PdeCoefficientReading probe = pde(geometry.Xi[0], tspan[0], u0, ux0);
        if (probe.C.Length != npde || probe.F.Length != npde || probe.S.Length != npde)
        {
            throw new OdeArgumentException("MATLAB:pdepe:UnexpectedOutputPDEFUN",
                $"PDEFUN must return column vectors of length {npde}.");
        }

        double[] uRight = Slice(y0, (nx - 1) * npde, npde);
        PdeBoundaryReading edges = bc(xmesh[0], uLeft, xmesh[nx - 1], uRight, tspan[0]);
        if (edges.PL.Length != npde || edges.QL.Length != npde
            || edges.PR.Length != npde || edges.QR.Length != npde)
        {
            throw new OdeArgumentException("MATLAB:pdepe:UnexpectedOutputBCFUN",
                $"BCFUN must return column vectors of length {npde}.");
        }

        // A zero on the diagonal of the mass matrix is an algebraic equation: an elliptic component
        // everywhere, and a boundary point whose condition prescribes the solution rather than the
        // flux. The right-hand side is written to carry the residual there, so the two must agree.
        var mass = new double[n, n];
        bool anyAlgebraic = false;
        for (int j = 0; j < nx; j++)
        {
            for (int i = 0; i < npde; i++)
            {
                bool differential = probe.C[i] != 0
                    && !(j == 0 && !singular && edges.QL[i] == 0)
                    && !(j == nx - 1 && edges.QR[i] == 0);
                mass[(j * npde) + i, (j * npde) + i] = differential ? 1 : 0;
                anyAlgebraic |= !differential;
            }
        }

        var pattern = new bool[n, n];
        for (int j = 0; j < nx; j++)
        {
            for (int block = Math.Max(0, j - 1); block <= Math.Min(nx - 1, j + 1); block++)
            {
                for (int i = 0; i < npde; i++)
                {
                    for (int k = 0; k < npde; k++)
                    {
                        pattern[(j * npde) + i, (block * npde) + k] = true;
                    }
                }
            }
        }

        var settings = options with
        {
            Mass = mass,
            MassFunction = null,
            MassDependsOnState = false,
            MassStronglyStateDependent = false,
            MassVectorPattern = null,
            MassSingular = anyAlgebraic ? OdeMassSingularity.Yes : OdeMassSingularity.No,
            Jacobian = null,
            JacobianFunction = null,
            JacobianPattern = pattern,
            JacobianConstant = false,
            Vectorized = false,
            VectorizedDerivative = null,
            Events = events is null ? null : (t, y) => events(t, y),
            Refine = 1,
            OutputFunction = null,
            OutputSelection = null,
            NonNegative = null,
            RecordSteps = false,
            CollectOutput = true,
        };

        double[] Derivative(double t, double[] y) =>
            Residual(t, y, npde, nx, singular, m, xmesh, geometry, pde, bc);

        OdeResult run;
        try
        {
            run = Ode15s.Run(Derivative, tspan, y0, settings);
        }
        catch (OdeArgumentException ex) when (ex.Identifier == "MATLAB:daeic12:IndexGTOne")
        {
            throw new OdeArgumentException("MATLAB:pdepe:SpatialDiscretizationFailed",
                "Unable to meet integration tolerances: the spatial discretization has failed. Try refining the mesh.");
        }
        catch (OdeArgumentException ex) when (ex.Identifier == "MATLAB:ode15s:MassMatrixAllZero")
        {
            throw new OdeArgumentException("MATLAB:pdepe:NoParabolic",
                "At least one equation must be parabolic.");
        }

        double[] times = [.. run.Times];
        if (times.Length > 0 && times[^1] != tspan[^1]
            && (events is null || run.EventTimes.Count == 0 || run.EventTimes[^1] != times[^1]))
        {
            options.Warn?.Invoke($"Time integration has failed. Solution is available at requested time points up to t={times[^1]:e}.");
        }

        return new PdeResult
        {
            Times = times,
            States = [.. run.States],
            EventTimes = [.. run.EventTimes],
            EventStates = [.. run.EventStates],
            EventIndices = [.. run.EventIndices],
            Npde = npde,
            Nx = nx,
            Ode = run,
        };
    }

    /// <summary>
    /// <c>[uout, duoutdx] = pdeval(m, xmesh, ui, xout)</c> — one component of a <c>pdepe</c> answer and
    /// its derivative with respect to x, off the same interpolant the discretisation is built on.
    /// </summary>
    public static void Evaluate(int m, double[] xmesh, double[] ui, double[] xout,
        double[] uout, double[] duoutdx)
    {
        int nx = xmesh.Length;
        foreach (double point in xout)
        {
            if (point < xmesh[0] || xmesh[^1] < point)
            {
                throw new OdeArgumentException("MATLAB:pdeval:SolOutsideInterval",
                    "The points in XOUT must be in the interval spanned by XMESH.");
            }
        }

        bool singular = xmesh[0] == 0 && m > 0;

        // The sweep walks the mesh once and takes the leading run of output points that fall inside
        // each subinterval, which is what MATLAB does; it is why the output points must be sorted.
        int evaluated = 0;
        int bottom = 0;
        while (bottom < nx - 1)
        {
            int count = 0;
            while (evaluated + count < xout.Length && xout[evaluated + count] - xmesh[bottom + 1] < 0)
            {
                count++;
            }

            for (int k = 0; k < count; k++)
            {
                Interpolate(singular, m, xmesh[bottom], [ui[bottom]], xmesh[bottom + 1], [ui[bottom + 1]],
                    xout[evaluated + k], out double[] value, out double[] slope);
                uout[evaluated + k] = value[0];
                duoutdx[evaluated + k] = slope[0];
            }

            evaluated += count;
            bottom++;
        }

        // Whatever is left sits at the right end of the mesh. Its derivative comes off the last
        // subinterval's interpolant, but the value is taken from the mesh itself.
        for (int k = evaluated; k < xout.Length; k++)
        {
            Interpolate(singular, m, xmesh[bottom - 1], [ui[bottom - 1]], xmesh[bottom], [ui[bottom]],
                xout[k], out _, out double[] slope);
            uout[k] = ui[bottom];
            duoutdx[k] = slope[0];
        }
    }

    /// <summary>
    /// The interpolant the scheme is built on: linear in x for a slab, in <c>log x</c> for a cylinder,
    /// in <c>1/x</c> for a sphere, and in <c>x²</c> on every subinterval when the axis is in the mesh.
    /// </summary>
    internal static void Interpolate(bool singular, int m, double xL, double[] uL, double xR, double[] uR,
        double xout, out double[] u, out double[] ux)
    {
        int npde = uL.Length;
        u = new double[npde];
        ux = new double[npde];
        double weight;
        double slope;
        if (singular)
        {
            weight = ((xout * xout) - (xL * xL)) / ((xR * xR) - (xL * xL));
            slope = 2 * xout / ((xR * xR) - (xL * xL));
        }
        else
        {
            switch (m)
            {
                case 0:
                    weight = (xout - xL) / (xR - xL);
                    slope = 1 / (xR - xL);
                    break;
                case 1:
                    weight = Math.Log(xout / xL) / Math.Log(xR / xL);
                    slope = (1 / xout) / Math.Log(xR / xL);
                    break;
                default:
                    weight = (xR / xout) * ((xout - xL) / (xR - xL));
                    slope = (xR / xout) * (xL / xout) / (xR - xL);
                    break;
            }
        }

        for (int i = 0; i < npde; i++)
        {
            double difference = uR[i] - uL[i];
            u[i] = uL[i] + (difference * weight);
            ux[i] = difference * slope;
        }
    }

    /// <summary>
    /// The right-hand side of the method-of-lines system: the difference equations of the
    /// discretisation, already divided by the coefficient the mass matrix would otherwise carry.
    /// </summary>
    /// <remarks>
    /// Dividing here rather than putting <c>c</c> into the mass matrix is what lets the mass matrix
    /// be constant — <c>c</c> may depend on the solution, the mass matrix may not without costing a
    /// refactorization every step. Where the divisor is zero the row is algebraic, its mass entry is
    /// zero and the divisor is replaced by one so that the residual is left as it stands.
    /// </remarks>
    private static double[] Residual(double t, double[] y, int npde, int nx, bool singular, int m,
        double[] xmesh, Geometry geometry, PdeCoefficients pde, PdeBoundary bc)
    {
        var up = new double[npde * nx];
        double[] uL = Slice(y, 0, npde);
        double[] uNext = Slice(y, npde, npde);
        Interpolate(singular, m, xmesh[0], uL, xmesh[1], uNext, geometry.Xi[0],
            out double[] u, out double[] ux);
        PdeCoefficientReading left = pde(geometry.Xi[0], t, u, ux);
        double[] cL = left.C;
        double[] fL = left.F;
        double[] sL = left.S;

        double[] uEnd = Slice(y, (nx - 1) * npde, npde);
        PdeBoundaryReading edges = bc(xmesh[0], uL, xmesh[nx - 1], uEnd, t);

        double[] xi = geometry.Xi;
        double[] xim = geometry.Xim;
        double[] zxmp1 = geometry.ZetaMinusLeft;
        double[] xzmp1 = geometry.RightMinusZeta;

        if (singular)
        {
            for (int i = 0; i < npde; i++)
            {
                double denominator = cL[i] == 0 ? 1 : cL[i];
                up[i] = (sL[i] + ((m + 1) * fL[i] / xi[0])) / denominator;
            }
        }
        else
        {
            double scale = Math.Pow(xmesh[0], m);
            for (int i = 0; i < npde; i++)
            {
                if (edges.QL[i] == 0)
                {
                    up[i] = edges.PL[i];
                    continue;
                }

                double share = edges.QL[i] / scale;
                double denominator = share * (zxmp1[0] * cL[i]);
                if (denominator == 0)
                {
                    denominator = 1;
                }

                up[i] = (edges.PL[i] + (share * ((xim[0] * fL[i]) + (zxmp1[0] * sL[i])))) / denominator;
            }
        }

        for (int ii = 1; ii < nx - 1; ii++)
        {
            double[] uCell = Slice(y, ii * npde, npde);
            double[] uAhead = Slice(y, (ii + 1) * npde, npde);
            Interpolate(singular, m, xmesh[ii], uCell, xmesh[ii + 1], uAhead, xi[ii], out u, out ux);
            PdeCoefficientReading right = pde(xi[ii], t, u, ux);
            for (int i = 0; i < npde; i++)
            {
                double denominator = (zxmp1[ii] * right.C[i]) + (xzmp1[ii - 1] * cL[i]);
                if (denominator == 0)
                {
                    denominator = 1;
                }

                up[(ii * npde) + i] = ((xim[ii] * right.F[i]) - (xim[ii - 1] * fL[i])
                    + ((zxmp1[ii] * right.S[i]) + (xzmp1[ii - 1] * sL[i]))) / denominator;
            }

            cL = right.C;
            fL = right.F;
            sL = right.S;
        }

        double endScale = Math.Pow(xmesh[nx - 1], m);
        int last = (nx - 1) * npde;
        for (int i = 0; i < npde; i++)
        {
            if (edges.QR[i] == 0)
            {
                up[last + i] = edges.PR[i];
                continue;
            }

            double share = edges.QR[i] / endScale;
            double denominator = -(share * (xzmp1[nx - 2] * cL[i]));
            if (denominator == 0)
            {
                denominator = 1;
            }

            up[last + i] = (edges.PR[i] + (share * ((xim[nx - 2] * fL[i]) - (xzmp1[nx - 2] * sL[i]))))
                / denominator;
        }

        return up;
    }

    private static double[] Slice(double[] source, int start, int length)
    {
        var piece = new double[length];
        Array.Copy(source, start, piece, 0, length);
        return piece;
    }

    /// <summary>
    /// The quadrature points and weights the symmetry decides, formed once: the flux point <c>ξ</c> of
    /// each subinterval, the cell divider <c>ζ</c> between two mesh points, and the two half-cell
    /// volumes either side of it.
    /// </summary>
    private sealed class Geometry
    {
        public required bool Singular { get; init; }

        /// <summary>Where the flux is evaluated on each subinterval.</summary>
        public required double[] Xi { get; init; }

        /// <summary>The area factor at <c>ξ</c> — <c>ξ^m</c>, or the form that stays finite on the axis.</summary>
        public required double[] Xim { get; init; }

        /// <summary>The volume between the left mesh point and the cell divider.</summary>
        public required double[] ZetaMinusLeft { get; init; }

        /// <summary>The volume between the cell divider and the right mesh point.</summary>
        public required double[] RightMinusZeta { get; init; }

        public static Geometry For(int m, double[] xmesh)
        {
            int cells = xmesh.Length - 1;
            bool singular = xmesh[0] == 0 && m > 0;
            var xi = new double[cells];
            var zeta = new double[cells];
            var xim = new double[cells];
            var zxmp1 = new double[cells];
            var xzmp1 = new double[cells];
            for (int k = 0; k < cells; k++)
            {
                double xL = xmesh[k];
                double xR = xmesh[k + 1];
                double xM = xL + (0.5 * (xR - xL));
                switch (m)
                {
                    case 0:
                        xi[k] = xM;
                        zeta[k] = xi[k];
                        break;
                    case 1:
                        xi[k] = singular
                            ? 2.0 / 3.0 * ((xL * xL) + (xL * xR) + (xR * xR)) / (xL + xR)
                            : (xR - xL) / Math.Log(xR / xL);
                        zeta[k] = Math.Pow(xi[k] * xM, 1.0 / 2.0);
                        break;
                    default:
                        xi[k] = singular
                            ? 2.0 / 3.0 * ((xL * xL) + (xL * xR) + (xR * xR)) / (xL + xR)
                            : xL * xR * Math.Log(xR / xL) / (xR - xL);
                        zeta[k] = Math.Pow(xL * xR * xM, 1.0 / 3.0);
                        break;
                }

                xim[k] = singular ? Math.Pow(zeta[k], m + 1) / xi[k] : Math.Pow(xi[k], m);
                zxmp1[k] = (Math.Pow(zeta[k], m + 1) - Math.Pow(xL, m + 1)) / (m + 1);
                xzmp1[k] = (Math.Pow(xR, m + 1) - Math.Pow(zeta[k], m + 1)) / (m + 1);
            }

            return new Geometry
            {
                Singular = singular,
                Xi = xi,
                Xim = xim,
                ZetaMinusLeft = zxmp1,
                RightMinusZeta = xzmp1,
            };
        }
    }
}
