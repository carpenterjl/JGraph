using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Numerics;

/// <summary>
/// <c>decic</c>: consistent initial conditions for <c>ode15i</c> — the smallest change to a guess
/// at <c>y0</c> and <c>y0'</c> that makes <c>f(t0, y0, y0') = 0</c>.
/// </summary>
/// <remarks>
/// <para>
/// A fully implicit problem constrains its own starting point, and the constraint is usually not
/// obvious: a circuit's charges and a mechanism's velocities are related by equations the modeller
/// never wrote down separately. <c>decic</c> reads them off the residual instead. The system
/// <c>0 = f + (df/dy)·Δy + (df/dy')·Δy'</c> is underdetermined — there are 2n unknowns and n
/// equations — so it is solved with as many components of the change as possible set to zero, and
/// components the caller pinned removed from the unknowns first.
/// </para>
/// <para>
/// A step is refused if it would move the guess by more than a factor of two in norm, which is the
/// trust region that keeps a bad Jacobian from throwing the search across the state space. What is
/// returned always has a residual no larger than the guess's.
/// </para>
/// </remarks>
public static class Decic
{
    private const double Epsilon = OdeStiffSupport.Epsilon;

    /// <summary>What the search settled on.</summary>
    /// <param name="Y">The consistent state.</param>
    /// <param name="Yp">The consistent slope.</param>
    /// <param name="ResidualNorm">The norm of <c>f(t0, Y, Yp)</c>.</param>
    public readonly record struct Consistent(double[] Y, double[] Yp, double ResidualNorm);

    /// <summary>
    /// Moves <paramref name="y0"/> and <paramref name="yp0"/> as little as it can until the
    /// residual vanishes.
    /// </summary>
    /// <param name="f">The residual <c>f(t, y, y')</c>.</param>
    /// <param name="t0">Where the problem starts.</param>
    /// <param name="y0">A guess at the state.</param>
    /// <param name="fixedY">Which components of the state may not move; null or empty is none.</param>
    /// <param name="yp0">A guess at the slope.</param>
    /// <param name="fixedYp">Which components of the slope may not move; null or empty is none.</param>
    /// <param name="options">The tolerances and the partial derivatives.</param>
    public static Consistent Solve(ImplicitOdeFunction f, double t0, double[] y0, bool[]? fixedY, double[] yp0,
        bool[]? fixedYp, ImplicitOdeOptions options)
    {
        int n = y0.Length;
        int[] freeY = FreeOf(fixedY, n);
        int[] freeYp = FreeOf(fixedYp, n);
        if (freeY.Length + freeYp.Length < n)
        {
            throw new OdeArgumentException("MATLAB:decic:TooManySpecified",
                $"You cannot fix more than {n} components of y0 and yp0 together.");
        }

        double rtol = options.Common.RelativeTolerance;
        if (rtol <= 0)
        {
            throw new OdeArgumentException("MATLAB:decic:OptRelTolNotPosScalar", "RelTol must be a positive scalar.");
        }

        if (rtol < 100 * Epsilon)
        {
            rtol = 100 * Epsilon;
            options.Common.Warn?.Invoke($"RelTol has been increased to {rtol:G6}.");
        }

        double[] atol = options.Common.AbsoluteTolerance ?? [1e-6];
        foreach (double a in atol)
        {
            if (a <= 0)
            {
                throw new OdeArgumentException("MATLAB:decic:OptAbsTolNotPos", "AbsTol must be positive.");
            }
        }

        var y = (double[])y0.Clone();
        var yp = (double[])yp0.Clone();
        double[] residual = f(t0, y, yp);
        ImplicitJacobianPair jacobians = ImplicitJacobianPair.Create(f, t0, y, yp, residual, options, out _);
        double startNorm = OdeStiffSupport.Norm(residual);
        double toleranceNorm = OdeStiffSupport.Norm(atol);

        LinearizedSystem? system = null;
        for (int counter = 0; counter < 10; counter++)
        {
            if (!jacobians.Constant || counter == 0)
            {
                system = LinearizedSystem.Build(jacobians.Y, jacobians.Yp, n, freeY, freeYp);
            }

            for (int chord = 0; chord < 3; chord++)
            {
                (double[] dy, double[] dyp) = system!.Solve(residual, n, freeY, freeYp);

                // The trust region: a correction more than twice the size of what it corrects is
                // evidence about the Jacobian, not about the answer.
                double size = Math.Max(OdeStiffSupport.Norm([.. y, .. yp]), toleranceNorm);
                double correction = OdeStiffSupport.Norm([.. dy, .. dyp]);
                if (correction > 2 * size)
                {
                    double factor = 2 * size / correction;
                    for (int i = 0; i < n; i++)
                    {
                        dy[i] *= factor;
                        dyp[i] *= factor;
                    }

                    correction *= factor;
                }

                for (int i = 0; i < n; i++)
                {
                    y[i] += dy[i];
                    yp[i] += dyp[i];
                }

                residual = f(t0, y, yp);
                double norm = OdeStiffSupport.Norm(residual);
                if (norm <= startNorm && correction <= 1e-3 * rtol * size)
                {
                    return new Consistent(y, yp, norm);
                }
            }

            jacobians.Update(f, t0, y, yp, residual, out _);
        }

        throw new OdeArgumentException("MATLAB:decic:ConvergenceFail",
            "Unable to compute consistent initial conditions; supply a better guess for y0 and yp0.");
    }

    private static int[] FreeOf(bool[]? pinned, int n)
    {
        if (pinned is null || pinned.Length == 0)
        {
            var all = new int[n];
            for (int i = 0; i < n; i++)
            {
                all[i] = i;
            }

            return all;
        }

        var free = new List<int>(n);
        for (int i = 0; i < n; i++)
        {
            if (i >= pinned.Length || !pinned[i])
            {
                free.Add(i);
            }
        }

        return [.. free];
    }

    /// <summary>
    /// The factorization of the underdetermined system <c>0 = res + (df/dy')·Δy' + (df/dy)·Δy</c>
    /// over the free components, held across the chord iterations that reuse it.
    /// </summary>
    private sealed class LinearizedSystem
    {
        private QrDecomposition _qr = null!;
        private double[,]? _crossed;   // Qᵀ·(df/dy) over the free state components
        private int _rank;
        private bool _slopeOnly;
        private bool _stateOnly;

        public static LinearizedSystem Build(double[,] dfdy, double[,] dfdyp, int n, int[] freeY, int[] freeYp)
        {
            int pinned = (n - freeY.Length) + (n - freeYp.Length);
            var system = new LinearizedSystem();

            if (freeY.Length == 0)
            {
                system._slopeOnly = true;
                system._qr = QrDecomposition.Factor(dfdyp, pivot: true);
                system._rank = RankOf(system._qr, n, n);
                Check(n - system._rank, pinned);
                return system;
            }

            if (freeYp.Length == 0)
            {
                system._stateOnly = true;
                system._qr = QrDecomposition.Factor(dfdy, pivot: true);
                system._rank = RankOf(system._qr, n, n);
                Check(n - system._rank, pinned);
                return system;
            }

            double[,] slopeBlock = Columns(dfdyp, freeYp);
            double[,] stateBlock = Columns(dfdy, freeY);
            system._qr = QrDecomposition.Factor(slopeBlock, pivot: true);
            system._rank = RankOf(system._qr, n, freeYp.Length);
            if (system._rank != n)
            {
                // What the slope's range cannot reach, the state must: the rank of the remaining
                // rows of Qᵀ·(df/dy) is what decides whether the problem is of index one.
                system._crossed = ApplyTranspose(system._qr, stateBlock, n);
                var tail = new double[n - system._rank, freeY.Length];
                for (int r = system._rank; r < n; r++)
                {
                    for (int c = 0; c < freeY.Length; c++)
                    {
                        tail[r - system._rank, c] = system._crossed[r, c];
                    }
                }

                Check(n - (system._rank + NumericalRank(tail)), pinned);
            }

            return system;
        }

        /// <summary>
        /// The change with as many components as possible set to zero — the basic solution of the
        /// underdetermined system.
        /// </summary>
        public (double[] Dy, double[] Dyp) Solve(double[] residual, int n, int[] freeY, int[] freeYp)
        {
            var dy = new double[n];
            var dyp = new double[n];
            double[] d = ApplyTranspose(_qr, Negated(residual), n);
            int[] order = _qr.PivotVector;
            double[,] r = Upper(_qr, n);

            if (_slopeOnly)
            {
                double[] solved = TriangularSolve(r, d, n);
                for (int i = 0; i < n; i++)
                {
                    dyp[order[i]] = solved[i];
                }

                return (dy, dyp);
            }

            if (_stateOnly)
            {
                double[] solved = TriangularSolve(r, d, n);
                for (int i = 0; i < n; i++)
                {
                    dy[order[i]] = solved[i];
                }

                return (dy, dyp);
            }

            if (_rank == n)
            {
                double[] solved = TriangularSolve(r, d, n);
                for (int i = 0; i < n && i < freeYp.Length; i++)
                {
                    dyp[freeYp[order[i]]] = solved[i];
                }

                return (dy, dyp);
            }

            // Below the slope block's rank the equations involve only the state, so they settle the
            // state's change first; the slope's change then follows by back substitution.
            int rows = n - _rank;
            var tail = new double[rows, freeY.Length];
            var tailRight = new double[rows];
            for (int i = 0; i < rows; i++)
            {
                for (int c = 0; c < freeY.Length; c++)
                {
                    tail[i, c] = _crossed![_rank + i, c];
                }

                tailRight[i] = d[_rank + i];
            }

            double[] w = BasicSolve(tail, tailRight);
            var head = new double[_rank];
            for (int i = 0; i < _rank; i++)
            {
                double sum = 0;
                for (int c = 0; c < freeY.Length; c++)
                {
                    sum += _crossed![i, c] * w[c];
                }

                head[i] = d[i] - sum;
            }

            double[] slopeHead = TriangularSolve(r, head, _rank);
            for (int i = 0; i < freeY.Length; i++)
            {
                dy[freeY[i]] = w[i];
            }

            for (int i = 0; i < _rank; i++)
            {
                dyp[freeYp[order[i]]] = slopeHead[i];
            }

            return (dy, dyp);
        }

        private static void Check(int deficiency, int pinned)
        {
            if (deficiency <= 0)
            {
                return;
            }

            throw new OdeArgumentException(
                deficiency <= pinned ? "MATLAB:decic:TooManyFixed" : "MATLAB:decic:IndexGTOne",
                deficiency <= pinned
                    ? $"Too many components of y0 and yp0 are fixed: {deficiency} fewer would do."
                    : "This DAE appears to be of index greater than 1.");
        }

        private static int RankOf(QrDecomposition qr, int rows, int cols)
        {
            double[,] r = Upper(qr, rows);
            double tolerance = Math.Max(rows, cols) * Epsilon * Math.Abs(r[0, 0]);
            int rank = 0;
            for (int i = 0; i < Math.Min(rows, r.GetLength(1)); i++)
            {
                if (Math.Abs(r[i, i]) > tolerance)
                {
                    rank = i + 1;
                }
            }

            return rank;
        }

        private static int NumericalRank(double[,] a)
        {
            int rows = a.GetLength(0);
            int cols = a.GetLength(1);
            if (rows == 0 || cols == 0)
            {
                return 0;
            }

            QrDecomposition qr = QrDecomposition.Factor(a, pivot: true);
            double[,] r = Upper(qr, rows);
            double tolerance = Math.Max(rows, cols) * Epsilon * Math.Abs(r[0, 0]);
            int rank = 0;
            for (int i = 0; i < Math.Min(rows, cols); i++)
            {
                if (Math.Abs(r[i, i]) > tolerance)
                {
                    rank++;
                }
            }

            return rank;
        }

        /// <summary>The basic least-squares solution — at most rank nonzeros, in the pivoted order.</summary>
        private static double[] BasicSolve(double[,] a, double[] b)
        {
            int rows = a.GetLength(0);
            int cols = a.GetLength(1);
            var answer = new double[cols];
            if (rows == 0 || cols == 0)
            {
                return answer;
            }

            QrDecomposition qr = QrDecomposition.Factor(a, pivot: true);
            double[] d = ApplyTranspose(qr, b, rows);
            double[,] r = Upper(qr, rows);
            double tolerance = Math.Max(rows, cols) * Epsilon * Math.Abs(r[0, 0]);
            int rank = 0;
            for (int i = 0; i < Math.Min(rows, cols); i++)
            {
                if (Math.Abs(r[i, i]) > tolerance)
                {
                    rank++;
                }
            }

            double[] head = TriangularSolve(r, d, rank);
            int[] order = qr.PivotVector;
            for (int i = 0; i < rank; i++)
            {
                answer[order[i]] = head[i];
            }

            return answer;
        }

        private static double[] TriangularSolve(double[,] r, double[] b, int size)
        {
            var x = new double[size];
            for (int i = size - 1; i >= 0; i--)
            {
                double sum = b[i];
                for (int j = i + 1; j < size; j++)
                {
                    sum -= r[i, j] * x[j];
                }

                x[i] = sum / r[i, i];
            }

            return x;
        }

        private static double[,] Upper(QrDecomposition qr, int rows)
        {
            double[] flat = qr.RColumnMajor(full: true);
            int cols = flat.Length / Math.Max(rows, 1);
            var r = new double[rows, cols];
            for (int c = 0; c < cols; c++)
            {
                for (int i = 0; i < rows; i++)
                {
                    r[i, c] = flat[(c * rows) + i];
                }
            }

            return r;
        }

        private static double[] ApplyTranspose(QrDecomposition qr, double[] b, int rows)
        {
            var flat = new double[rows];
            Array.Copy(b, flat, rows);
            qr.ApplyTransposeInPlace(flat, 1);
            return flat;
        }

        private static double[,] ApplyTranspose(QrDecomposition qr, double[,] b, int rows)
        {
            int cols = b.GetLength(1);
            var flat = new double[rows * cols];
            for (int c = 0; c < cols; c++)
            {
                for (int i = 0; i < rows; i++)
                {
                    flat[(c * rows) + i] = b[i, c];
                }
            }

            qr.ApplyTransposeInPlace(flat, cols);
            var answer = new double[rows, cols];
            for (int c = 0; c < cols; c++)
            {
                for (int i = 0; i < rows; i++)
                {
                    answer[i, c] = flat[(c * rows) + i];
                }
            }

            return answer;
        }

        private static double[,] Columns(double[,] a, int[] which)
        {
            int rows = a.GetLength(0);
            var block = new double[rows, which.Length];
            for (int c = 0; c < which.Length; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    block[r, c] = a[r, which[c]];
                }
            }

            return block;
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
    }
}
