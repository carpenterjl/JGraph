using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Numerics;

/// <summary>
/// Consistent initial conditions for <c>M(t, y)·y' = f(t, y)</c> when <c>M</c> is singular —
/// MATLAB's <c>daeic12</c> and <c>daeic3</c>.
/// </summary>
/// <remarks>
/// <para>
/// A differential-algebraic equation has a solution only from a state that already satisfies its
/// algebraic constraints, and a caller who writes down a plausible <c>y0</c> has almost never
/// written down one that does. Both routines here take <c>y0</c> and <c>yp0</c> as guesses and move
/// as little as they can. For a constant mass matrix the singular value decomposition separates the
/// differential directions, in which <c>y'</c> is determined by <c>f</c>, from the algebraic ones,
/// in which <c>y</c> itself must satisfy <c>f = 0</c>; a damped Newton iteration is then run in the
/// algebraic directions alone. For a state-dependent mass matrix no such fixed decomposition
/// exists, and one short implicit Euler step stands in for it.
/// </para>
/// <para>
/// If the algebraic block of the Jacobian is itself singular the problem has index greater than
/// one, and no amount of moving the guess will help. That is reported rather than iterated on.
/// </para>
/// </remarks>
internal static class DaeInitialConditions
{
    private const double Epsilon = OdeStiffSupport.Epsilon;

    /// <summary>What the search settled on.</summary>
    /// <param name="Y">The consistent state.</param>
    /// <param name="Yp">The consistent slope.</param>
    /// <param name="F"><c>f(t0, Y)</c>.</param>
    /// <param name="Jacobian">df/dy there.</param>
    /// <param name="Evaluations">Derivative evaluations spent.</param>
    /// <param name="PartialDerivatives">Jacobians formed.</param>
    internal readonly record struct Consistent(double[] Y, double[] Yp, double[] F, double[,] Jacobian,
        int Evaluations, int PartialDerivatives);

    /// <summary>
    /// <c>daeic12</c>: the mass matrix is constant or depends on the time alone, so one
    /// decomposition serves the whole search. <paramref name="diagonalMass"/> takes the cheaper
    /// route in which the decomposition is the identity and the singular values are the diagonal.
    /// </summary>
    public static Consistent Type12(OdeFunction f, double t0, bool diagonalMass, double[,] m0, double[] y,
        double[] slopeGuess, double[] value, double relativeTolerance, OdeJacobianSource jacobian)
    {
        int n = y.Length;
        int evaluations = 0;
        int derivatives = 0;
        double[,] dfdy;
        if (jacobian.Cached is { } already)
        {
            dfdy = already;
        }
        else
        {
            dfdy = jacobian.Evaluate(f, t0, y, value, out int spent);
            evaluations += spent;
            derivatives = 1;
        }

        double[] singular;
        double[,] left;
        double[,] right;
        if (diagonalMass)
        {
            singular = new double[n];
            for (int i = 0; i < n; i++)
            {
                singular[i] = m0[i, i];
            }

            left = OdeStiffSupport.Identity(n);
            right = OdeStiffSupport.Identity(n);
        }
        else
        {
            Svd svd = Svd.Factor(m0);
            singular = (double[])svd.Values.Clone();
            left = svd.U;
            right = svd.V;
            double largest = 0;
            foreach (double s in singular)
            {
                largest = Math.Max(largest, s);
            }

            double tolerance = n * largest * Epsilon;
            for (int i = 0; i < n; i++)
            {
                if (singular[i] <= tolerance)
                {
                    singular[i] = 0;
                }
            }
        }

        var algebraic = new List<int>();
        var differential = new List<int>();
        for (int i = 0; i < n; i++)
        {
            (singular[i] == 0 ? algebraic : differential).Add(i);
        }

        double[] transformed = TransposeTimes(left, value);
        double[,] rotated = Sandwich(left, dfdy, right);
        double[] state = TransposeTimes(right, y);
        double[] slope = TransposeTimes(right, slopeGuess);

        double[] SlopeFrom(double[] atValue)
        {
            var filled = (double[])slope.Clone();
            foreach (int i in differential)
            {
                filled[i] = atValue[i] / singular[i];
            }

            return OdeStiffSupport.Multiply(right, filled);
        }

        if (algebraic.Count == 0)
        {
            // Reachable only when the caller said the mass matrix was singular and it was not.
            var quotient = new double[n];
            for (int i = 0; i < n; i++)
            {
                quotient[i] = transformed[i] / singular[i];
            }

            return new Consistent(y, OdeStiffSupport.Multiply(right, quotient), value, dfdy, evaluations, derivatives);
        }

        double[,] block = Submatrix(rotated, algebraic);
        int blockNonZero = OdeStiffSupport.NonZeroCount(block);
        if (blockNonZero == 0 || Epsilon * blockNonZero * OdeStiffSupport.ConditionEstimate(block) > 1)
        {
            throw new OdeArgumentException("MATLAB:daeic12:IndexGTOne",
                "This DAE appears to be of index greater than 1.");
        }

        if (OdeStiffSupport.Norm(Pick(transformed, algebraic)) <= 1000 * Epsilon * OdeStiffSupport.Norm(transformed))
        {
            return new Consistent(y, SlopeFrom(transformed), value, dfdy, evaluations, derivatives);
        }

        LuDecomposition factored = LuDecomposition.Factor(block);
        bool refresh = false;
        for (int iteration = 0; iteration < 15; iteration++)
        {
            if (refresh)
            {
                dfdy = jacobian.Evaluate(f, t0, y, value, out int spent);
                evaluations += spent;
                derivatives++;
                rotated = Sandwich(left, dfdy, right);
                block = Submatrix(rotated, algebraic);
                factored = LuDecomposition.Factor(block);
                refresh = false;
            }

            double[] correction = factored.Solve(Negated(Pick(transformed, algebraic)));
            double residual = OdeStiffSupport.Norm(correction);

            // A weak line search: the step is halved until it improves the residual measured in the
            // same factorization, which is what makes the test independent of the problem's scaling.
            double lambda = 1;
            var candidate = (double[])state.Clone();
            double[] candidateValue = value;
            double[] candidateTransformed = transformed;
            double candidateResidual = residual;
            for (int probe = 0; probe < 3; probe++)
            {
                for (int i = 0; i < algebraic.Count; i++)
                {
                    candidate[algebraic[i]] = state[algebraic[i]] + (lambda * correction[i]);
                }

                double[] moved = OdeStiffSupport.Multiply(right, candidate);
                candidateValue = f(t0, moved);
                candidateTransformed = TransposeTimes(left, candidateValue);
                evaluations++;
                if (OdeStiffSupport.Norm(Pick(candidateTransformed, algebraic))
                    <= 1e-3 * relativeTolerance * OdeStiffSupport.Norm(candidateTransformed))
                {
                    return new Consistent(moved, SlopeFrom(candidateTransformed), candidateValue, dfdy,
                        evaluations, derivatives);
                }

                candidateResidual = OdeStiffSupport.Norm(factored.Solve(Pick(candidateTransformed, algebraic)));
                if (candidateResidual < 0.9 * residual)
                {
                    break;
                }

                lambda *= 0.5;
            }

            double size = Math.Max(OdeStiffSupport.Norm(Pick(state, algebraic)),
                OdeStiffSupport.Norm(Pick(candidate, algebraic)));
            if (size == 0)
            {
                size = Epsilon;
            }

            state = candidate;
            y = OdeStiffSupport.Multiply(right, candidate);
            value = candidateValue;
            transformed = candidateTransformed;
            if (candidateResidual <= 1e-3 * relativeTolerance * size)
            {
                return new Consistent(y, SlopeFrom(transformed), value, dfdy, evaluations, derivatives);
            }

            refresh = candidateResidual > 0.1 * residual;
        }

        throw new OdeArgumentException("MATLAB:daeic12:NeedBetterY0",
            "Unable to compute consistent initial conditions; supply a better guess for y0.");
    }

    /// <summary>
    /// <c>daeic3</c>: the mass matrix depends on the state, or is sparse and not diagonal, so no one
    /// decomposition serves. A short implicit Euler step of length <c>h</c> defines the slope as
    /// <c>(y − y0)/h</c> and the state is corrected until the residual is small; <c>h</c> is
    /// reduced and the search repeated when it is not.
    /// </summary>
    /// <remarks>
    /// The first <c>h</c> is deliberately not tiny. A very short step makes the iteration matrix
    /// almost all mass matrix, and a mass matrix that is almost singular then makes it almost
    /// singular too; balancing the two terms by their Frobenius norms is what keeps the
    /// factorization worth having.
    /// </remarks>
    public static Consistent Type3(OdeFunction f, OdeOptions options, OdeMassType massType, double[] tspan,
        double? firstStep, double[,] m0, double[] y0, double[] slopeGuess, double[] value,
        double relativeTolerance, OdeJacobianSource jacobian, OdeMassVectorJacobian? massVector)
    {
        int n = y0.Length;
        double t0 = tspan[0];
        double t1 = tspan[1];
        int evaluations = 0;
        int derivatives = 0;

        double tried = 1e-4 * Math.Abs(t0);
        if (tried == 0)
        {
            tried = 1e-4 * Math.Abs(t1);
        }

        if (firstStep is { } asked)
        {
            tried = Math.Min(Math.Abs(asked), tried);
        }

        double length = Math.Min(tried, Math.Abs(t1 - t0));

        double[,] dfdy;
        if (jacobian.Cached is { } already)
        {
            dfdy = already;
        }
        else
        {
            dfdy = jacobian.Evaluate(f, t0, y0, value, out int spent);
            evaluations += spent;
            derivatives = 1;
        }

        double[,]? massSlope = massType == OdeMassType.StateDependentStrong
            ? massVector!.Evaluate(t0, y0, slopeGuess)
            : null;

        double jacobianSize = Frobenius(dfdy);
        double massSize = Frobenius(m0);
        if (jacobianSize > 0 && massSize < length * jacobianSize)
        {
            length = massSize / jacobianSize;
        }

        double h = Math.Sign(t1 - t0) * Math.Max(length, 4 * OdeSetup.Spacing(t0));
        double best = OdeStiffSupport.Norm(
            OdeStiffSupport.Subtract(OdeStiffSupport.Multiply(m0, slopeGuess), value));

        double[] start = y0;
        bool refresh = false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            bool refactor = true;
            double[] y = start;
            double[] f0 = value;
            double[] yp = slopeGuess;
            LuDecomposition? factored = null;
            double[] rowScale = [];
            bool converged = false;

            for (int pass = 0; pass < 2 && !converged; pass++)
            {
                yp = slopeGuess;
                double[] residualVector = OdeStiffSupport.Subtract(OdeStiffSupport.Multiply(m0, yp), f0);

                for (int iteration = 0; iteration < 15; iteration++)
                {
                    if (refresh)
                    {
                        if (!jacobian.Constant)
                        {
                            dfdy = jacobian.Evaluate(f, t0, y, f0, out int spent);
                            evaluations += spent;
                            derivatives++;
                        }

                        refresh = false;
                        if (massType >= OdeMassType.StateDependentWeak)
                        {
                            m0 = options.MassFunction!(t0, y);
                        }

                        if (massType == OdeMassType.StateDependentStrong)
                        {
                            massSlope = massVector!.Evaluate(t0, y, yp);
                        }

                        refactor = true;
                    }

                    if (refactor)
                    {
                        var matrix = new double[n, n];
                        for (int r = 0; r < n; r++)
                        {
                            for (int c = 0; c < n; c++)
                            {
                                matrix[r, c] = (m0[r, c] / h) - dfdy[r, c];
                            }
                        }

                        if (massSlope is not null)
                        {
                            OdeStiffSupport.AddInPlace(matrix, massSlope);
                        }

                        for (int r = 0; r < n; r++)
                        {
                            bool any = false;
                            for (int c = 0; c < n && !any; c++)
                            {
                                any = matrix[r, c] != 0;
                            }

                            if (!any)
                            {
                                throw new OdeArgumentException("MATLAB:daeic3:IndexGTOne",
                                    "This DAE appears to be of index greater than 1.");
                            }
                        }

                        rowScale = OdeStiffSupport.ScaleRows(matrix);
                        factored = LuDecomposition.Factor(matrix);
                        refactor = false;
                    }

                    double[] correction = factored!.Solve(Negated(Scaled(rowScale, residualVector)));
                    double residual = OdeStiffSupport.Norm(correction);

                    double lambda = 1;
                    bool settled = false;
                    double candidateResidual = residual;
                    var candidate = new double[n];
                    var candidateSlope = new double[n];
                    double[] candidateValue = f0;
                    double[] candidateResidualVector = residualVector;
                    for (int probe = 0; probe < 3; probe++)
                    {
                        for (int i = 0; i < n; i++)
                        {
                            candidate[i] = y[i] + (lambda * correction[i]);
                            candidateSlope[i] = (candidate[i] - start[i]) / h;
                        }

                        if (massType >= OdeMassType.StateDependentWeak)
                        {
                            m0 = options.MassFunction!(t0, candidate);
                        }

                        double[] leftSide = OdeStiffSupport.Multiply(m0, candidateSlope);
                        candidateValue = f(t0, candidate);
                        evaluations++;
                        candidateResidualVector = OdeStiffSupport.Subtract(leftSide, candidateValue);
                        double size = OdeStiffSupport.Norm(candidateResidualVector);
                        if (size <= 1e-3 * relativeTolerance
                                * Math.Max(OdeStiffSupport.Norm(leftSide), OdeStiffSupport.Norm(candidateValue))
                            && size <= best)
                        {
                            best = size;
                            settled = true;
                            break;
                        }

                        candidateResidual = OdeStiffSupport.Norm(
                            factored.Solve(Scaled(rowScale, candidateResidualVector)));
                        if (candidateResidual < 0.9 * residual)
                        {
                            break;
                        }

                        lambda *= 0.5;
                    }

                    if (settled)
                    {
                        y = candidate;
                        yp = candidateSlope;
                        f0 = candidateValue;
                        converged = true;
                        break;
                    }

                    double norm = Math.Max(OdeStiffSupport.Norm(y), OdeStiffSupport.Norm(candidate));
                    if (norm == 0)
                    {
                        norm = Epsilon;
                    }

                    y = (double[])candidate.Clone();
                    yp = (double[])candidateSlope.Clone();
                    f0 = candidateValue;
                    residualVector = candidateResidualVector;
                    double residualSize = OdeStiffSupport.Norm(residualVector);
                    if (candidateResidual <= 1e-3 * relativeTolerance * norm && residualSize <= best)
                    {
                        best = residualSize;
                        converged = true;
                        break;
                    }

                    refresh = candidateResidual > 0.1 * residual;
                }

                if (!converged)
                {
                    break;
                }

                // A second pass from the state just found, so that the slope it implies is small.
                start = y;
                if (massType >= OdeMassType.StateDependentWeak)
                {
                    m0 = options.MassFunction!(t0, y);
                    refactor = true;
                }
            }

            if (converged)
            {
                return new Consistent(y, yp, f0, dfdy, evaluations, derivatives);
            }

            h /= 10;
        }

        throw new OdeArgumentException("MATLAB:daeic3:NeedBetterY0",
            "Unable to compute consistent initial conditions; supply a better guess for y0 and yp0.");
    }

    private static double[] Pick(double[] v, List<int> indices)
    {
        var picked = new double[indices.Count];
        for (int i = 0; i < indices.Count; i++)
        {
            picked[i] = v[indices[i]];
        }

        return picked;
    }

    private static double[,] Submatrix(double[,] a, List<int> indices)
    {
        int m = indices.Count;
        var block = new double[m, m];
        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j < m; j++)
            {
                block[i, j] = a[indices[i], indices[j]];
            }
        }

        return block;
    }

    /// <summary><c>aᵀ·v</c>.</summary>
    private static double[] TransposeTimes(double[,] a, double[] v)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        var answer = new double[cols];
        for (int c = 0; c < cols; c++)
        {
            double sum = 0;
            for (int r = 0; r < rows; r++)
            {
                sum += a[r, c] * v[r];
            }

            answer[c] = sum;
        }

        return answer;
    }

    /// <summary><c>aᵀ·b·c</c>.</summary>
    private static double[,] Sandwich(double[,] a, double[,] b, double[,] c)
    {
        int n = a.GetLength(0);
        var middle = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                double sum = 0;
                for (int k = 0; k < n; k++)
                {
                    sum += a[k, i] * b[k, j];
                }

                middle[i, j] = sum;
            }
        }

        var answer = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                double sum = 0;
                for (int k = 0; k < n; k++)
                {
                    sum += middle[i, k] * c[k, j];
                }

                answer[i, j] = sum;
            }
        }

        return answer;
    }

    private static double[] Negated(double[] v)
    {
        var answer = new double[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            answer[i] = -v[i];
        }

        return answer;
    }

    private static double[] Scaled(double[] scale, double[] v)
    {
        var answer = new double[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            answer[i] = scale[i] * v[i];
        }

        return answer;
    }

    private static double Frobenius(double[,] a)
    {
        double sum = 0;
        for (int i = 0; i < a.GetLength(0); i++)
        {
            for (int j = 0; j < a.GetLength(1); j++)
            {
                sum += a[i, j] * a[i, j];
            }
        }

        return Math.Sqrt(sum);
    }
}
