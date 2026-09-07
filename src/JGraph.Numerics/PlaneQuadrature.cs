using System;

namespace JGraph.Numerics;

/// <summary>
/// One tile of a two-dimensional integration, evaluated as a whole: the integrand is asked about a
/// 14-by-14 array of points and answers an array of the same shape.
/// </summary>
/// <param name="X">The abscissae, column-major.</param>
/// <param name="Y">The ordinates, column-major and the same length.</param>
/// <param name="Rows">How many rows the two arrays have.</param>
/// <param name="Cols">How many columns they have.</param>
public delegate double[] PlaneIntegrand(double[] X, double[] Y, int Rows, int Cols);

/// <summary>
/// Adaptive quadrature over a plane region — the engine behind <c>quad2d</c> and
/// <c>integral2</c>'s <c>'tiled'</c> method, and the innermost of <c>integral3</c>'s three levels.
/// </summary>
/// <remarks>
/// <para>
/// This is Shampine's TwoD. The region between the two curves <c>ymin(x)</c> and <c>ymax(x)</c> is
/// read as a rectangle in <c>(θ, φ)</c>, a product Gauss–Kronrod (3, 7) rule is laid over each tile,
/// and whichever tile currently holds the most of the error is quartered. Two rules over one set of
/// points again give the error estimate for nothing, exactly as in one dimension.
/// </para>
/// <para>
/// The thing that makes it fast through an interpreter is that a tile is <em>one</em> call. The rule
/// is a product of two seven-point rules over two halves each, so a tile carries 14 abscissae in
/// each direction; forming the whole 196-point array and handing it over once is the difference
/// between one trip into the interpreter per tile and a hundred and ninety-six. It is also how
/// MATLAB does it, which matters here for more than speed: an integrand that is not properly
/// vectorised is caught by evaluating six of those points a second time, in a differently shaped
/// array, and complaining when the two disagree.
/// </para>
/// <para>
/// The <em>singularity-weakening</em> transform is on by default and is why an integrand that is
/// infinite at a corner still has an integral here. Both variables are read through a cosine, so the
/// rate the map moves at vanishes at every edge of the region: the abscissae crowd towards a
/// boundary and never land on it, and <c>1/√(x+y)</c> over the unit triangle is answered without
/// <c>1/0</c> being formed. <c>quad2d</c> lets it be turned off, and then the rule is the plain
/// product rule over <c>x</c> and a normalised <c>y</c>.
/// </para>
/// </remarks>
public static class PlaneQuadrature
{
    /// <summary>What a plane integration ran into, if anything.</summary>
    public enum Trouble
    {
        /// <summary>Nothing: the error bound met the tolerance.</summary>
        None,

        /// <summary>A tile could not be quartered without landing on the boundary of the region.</summary>
        MinimumTileSize,

        /// <summary>The ceiling on integrand calls was reached with tiles still unconverged.</summary>
        MaximumFunctionEvaluations,

        /// <summary>The running value or its error bound stopped being a number.</summary>
        NonFiniteResult,
    }

    /// <summary>What a plane integration answered.</summary>
    /// <param name="Value">The integral.</param>
    /// <param name="ErrorBound">An approximate upper bound on <c>|Q - I|</c>.</param>
    /// <param name="Trouble">What stopped it short, if anything did.</param>
    /// <param name="FunctionEvaluations">How many times the integrand was called.</param>
    /// <param name="Vectorized">Whether the integrand answered the same values in two array shapes.</param>
    public readonly record struct Result(
        double Value, double ErrorBound, Trouble Trouble, int FunctionEvaluations, bool Vectorized);

    /// <summary>MATLAB's default ceiling on integrand calls for <c>quad2d</c>.</summary>
    public const int DefaultMaximumFunctionEvaluations = 2000;

    /// <summary>The ceiling <c>integral2</c>'s tiled method uses, which is five times as generous.</summary>
    public const int TiledMaximumFunctionEvaluations = 10000;

    /// <summary>Half the abscissae of one tile in one direction; the rule is a product of two of these.</summary>
    private const int NodeCount = 7;

    /// <summary>The spacing of doubles near one.</summary>
    private const double Ulp = 2.220446049250313e-16;

    /// <summary>A hundred ulps: the finest relative tolerance a tiled integration will honour.</summary>
    private const double HundredUlps = 100.0 * Ulp;

    /// <summary>
    /// The Gauss–Kronrod (3, 7) nodes: degrees of precision 5 and 11 over one set of seven points.
    /// </summary>
    private static readonly double[] Nodes =
    [
        -0.9604912687080202, -0.7745966692414834, -0.4342437493468026,
        0.0,
        0.4342437493468026, 0.7745966692414834, 0.9604912687080202,
    ];

    /// <summary>The three-point Gauss weights, laid out over all seven nodes.</summary>
    private static readonly double[] GaussWeights = [0.0, 5.0 / 9.0, 0.0, 8.0 / 9.0, 0.0, 5.0 / 9.0, 0.0];

    /// <summary>The seven-point Kronrod weights.</summary>
    private static readonly double[] KronrodWeights =
    [
        0.1046562260264672, 0.2684880898683334, 0.4013974147759622,
        0.4509165386584744,
        0.4013974147759622, 0.2684880898683334, 0.1046562260264672,
    ];

    /// <summary>
    /// Integrates <paramref name="f"/> over <c>xmin ≤ x ≤ xmax</c>, <c>ymin(x) ≤ y ≤ ymax(x)</c>.
    /// </summary>
    /// <param name="f">The integrand, asked about a whole tile at once.</param>
    /// <param name="xmin">The lower limit in x, which must be finite.</param>
    /// <param name="xmax">The upper limit in x, which must be finite.</param>
    /// <param name="ymin">The lower curve, asked about a row of abscissae at once.</param>
    /// <param name="ymax">The upper curve.</param>
    /// <param name="absoluteTolerance">The floor under the tolerance.</param>
    /// <param name="relativeTolerance">
    /// How much of the answer's own size the error may be; raised to a hundred ulps if it is finer.
    /// </param>
    /// <param name="singular">Whether to read both variables through the boundary-weakening cosine.</param>
    /// <param name="maximumFunctionEvaluations">The ceiling on calls of <paramref name="f"/>.</param>
    /// <param name="vectorizationTest">
    /// The six linear indices into the tile that are evaluated a second time, in a 2-by-3 array, to
    /// see whether the integrand really is elementwise. <c>quad2d</c> and <c>integral2</c> pick
    /// different ones, and which six they are is visible to an integrand that counts its callers.
    /// </param>
    public static Result Integrate(
        PlaneIntegrand f,
        double xmin,
        double xmax,
        Func<double[], double[]> ymin,
        Func<double[], double[]> ymax,
        double absoluteTolerance,
        double relativeTolerance,
        bool singular,
        int maximumFunctionEvaluations,
        int[] vectorizationTest)
    {
        var state = new Tiling(f, xmin, xmax, ymin, ymax, absoluteTolerance, relativeTolerance,
            singular, maximumFunctionEvaluations, vectorizationTest);
        return state.Run();
    }

    /// <summary>
    /// One running tiled integration. It is a class rather than a pile of locals because the tile
    /// list, the sorted error list and the cross-reference between them have to be kept in step, and
    /// because the integrand call has to be able to say "I declined to divide this tile".
    /// </summary>
    private sealed class Tiling
    {
        private readonly PlaneIntegrand f;
        private readonly double xmin;
        private readonly double xmax;
        private readonly Func<double[], double[]> ymin;
        private readonly Func<double[], double[]> ymax;
        private readonly bool singular;
        private readonly int maximumFunctionEvaluations;
        private readonly int[] vectorizationTest;
        private readonly double[] fractions = new double[2 * NodeCount];

        private double absolute;
        private double relative;
        private bool first = true;
        private bool vectorized = true;
        private int calls;

        // The tile list, kept the way MATLAB keeps it: tiles in arrival order, their adjusted errors
        // in ascending order in a second list, and an index from the second into the first. The
        // largest error is then the last entry of the sorted list, and taking it costs no search.
        private double[] error = new double[64];
        private double[] leftEdge = new double[64];
        private double[] rightEdge = new double[64];
        private double[] bottomEdge = new double[64];
        private double[] topEdge = new double[64];
        private double[] tileValue = new double[64];
        private double[] sortedError = new double[64];
        private int[] crossReference = new int[64];
        private int tiles;

        internal Tiling(
            PlaneIntegrand f, double xmin, double xmax,
            Func<double[], double[]> ymin, Func<double[], double[]> ymax,
            double absoluteTolerance, double relativeTolerance, bool singular,
            int maximumFunctionEvaluations, int[] vectorizationTest)
        {
            this.f = f;
            this.xmin = xmin;
            this.xmax = xmax;
            this.ymin = ymin;
            this.ymax = ymax;
            this.singular = singular;
            this.maximumFunctionEvaluations = maximumFunctionEvaluations;
            this.vectorizationTest = vectorizationTest;
            this.absolute = absoluteTolerance;
            this.relative = relativeTolerance;
            for (int i = 0; i < NodeCount; i++)
            {
                // The two halves of [0, 1]: the product rule is applied to each half of each side of
                // a tile, which is what makes one call cover the tile's four quarters.
                this.fractions[i] = (Nodes[i] + 1.0) / 4.0;
                this.fractions[i + NodeCount] = (Nodes[i] + 3.0) / 4.0;
            }
        }

        internal Result Run()
        {
            double thetaL = singular ? 0.0 : xmin;
            double thetaR = singular ? Math.PI : xmax;
            double phiB = 0.0;
            double phiT = singular ? Math.PI : 1.0;
            double area = (thetaR - thetaL) * (phiT - phiB);

            if (!Tile(thetaL, thetaR, phiB, phiT, out double[] quarters, out double[] quarterError))
            {
                return new(double.NaN, double.NaN, Trouble.NonFiniteResult, calls, vectorized);
            }

            double value = quarters[0] + quarters[1] + quarters[2] + quarters[3];
            if (relative < HundredUlps)
            {
                relative = HundredUlps;
            }

            double relativeEighth = Math.Max(relative / 8.0, HundredUlps);
            double absoluteEighth = absolute / 8.0;

            // The first tolerance is deliberately unreachable, so the very first tile is always
            // divided: four quarters of a region are not enough to trust any error estimate.
            double tolerance = HundredUlps * Math.Abs(value);
            double settledError = 0.0;
            double adjust = 1.0;
            bool minimumTile = false;
            bool exhausted = false;

            double bound = Save(quarters, quarterError, thetaL, thetaR, phiB, phiT,
                tolerance, area, adjust, ref settledError);
            while (tiles > 0 && bound > tolerance)
            {
                Next(out double previous, out double raw, out thetaL, out thetaR, out phiB, out phiT,
                    out double adjusted);
                if (!Tile(thetaL, thetaR, phiB, phiT, out quarters, out quarterError))
                {
                    // Quartering would have put an abscissa on the boundary of the region, where the
                    // integrand may not be defined at all. The tile keeps the estimate it has.
                    minimumTile = true;
                    settledError += adjusted;
                    bound = settledError + SumOfSortedErrors();
                    continue;
                }

                double refined = quarters[0] + quarters[1] + quarters[2] + quarters[3];

                // The stored estimate is conservative on purpose. How conservative is measurable: the
                // four quarters together are a much better answer than the tile was, so their
                // difference from it says by what factor the estimator overstated itself, and that
                // factor is carried forward to the quarters' own estimates.
                adjust = Math.Min(1.0, Math.Abs(previous - refined) / raw);
                value += refined - previous;
                tolerance = Math.Max(absoluteEighth, relativeEighth * Math.Abs(value));
                bound = Save(quarters, quarterError, thetaL, thetaR, phiB, phiT,
                    tolerance, area, adjust, ref settledError);
                if (calls >= maximumFunctionEvaluations)
                {
                    exhausted = true;
                    break;
                }
            }

            Trouble trouble = !double.IsFinite(value) || !double.IsFinite(bound) ? Trouble.NonFiniteResult
                : exhausted ? Trouble.MaximumFunctionEvaluations
                : minimumTile ? Trouble.MinimumTileSize
                : Trouble.None;
            return new(value, bound, trouble, calls, vectorized);
        }

        /// <summary>
        /// Measures one tile's four quarters. Answers false when the tile cannot be divided without
        /// an abscissa landing exactly on the region's boundary, which is the one case where the
        /// caller must keep what it already had.
        /// </summary>
        private bool Tile(
            double thetaL, double thetaR, double phiB, double phiT,
            out double[] quarters, out double[] quarterError)
        {
            quarters = [];
            quarterError = [];
            int side = 2 * NodeCount;
            double dtheta = thetaR - thetaL;
            double dphi = phiT - phiB;
            var theta = new double[side];
            var x = new double[side];
            for (int k = 0; k < side; k++)
            {
                theta[k] = thetaL + (fractions[k] * dtheta);
                x[k] = singular
                    ? (0.5 * (xmax + xmin)) + (0.5 * (xmax - xmin) * Math.Cos(theta[k]))
                    : theta[k];
            }

            if (!first)
            {
                bool onTheEdge = singular
                    ? x[0] == xmax || x[side - 1] == xmin
                    : x[0] == xmin || x[side - 1] == xmax;
                if (onTheEdge)
                {
                    return false;
                }
            }

            double[] bottom = ymin(x);
            double[] top = ymax(x);
            if (bottom.Length != side || top.Length != side)
            {
                throw new ArgumentException(
                    $"a limit answered {Math.Min(bottom.Length, top.Length)} value(s) for {side} point(s).");
            }

            var height = new double[side];
            for (int k = 0; k < side; k++)
            {
                height[k] = top[k] - bottom[k];
            }

            var phi = new double[side];
            for (int j = 0; j < side; j++)
            {
                phi[j] = phiB + (fractions[j] * dphi);
            }

            var xs = new double[side * side];
            var ys = new double[side * side];
            for (int k = 0; k < side; k++)
            {
                for (int j = 0; j < side; j++)
                {
                    xs[j + (side * k)] = x[k];
                    ys[j + (side * k)] = singular
                        ? bottom[k] + ((0.5 + (0.5 * Math.Cos(phi[j]))) * height[k])
                        : bottom[k] + (phi[j] * height[k]);
                }
            }

            if (!first)
            {
                for (int k = 0; k < side; k++)
                {
                    double low = ys[side * k];
                    double high = ys[side - 1 + (side * k)];
                    bool onTheEdge = singular
                        ? low == top[k] || high == bottom[k]
                        : low == bottom[k] || high == top[k];
                    if (onTheEdge)
                    {
                        return false;
                    }
                }
            }

            double[] z = f(xs, ys, side, side);
            calls++;
            if (z.Length != xs.Length)
            {
                throw new ArgumentException(
                    $"the integrand answered {z.Length} value(s) for {xs.Length} point(s).");
            }

            if (first)
            {
                var sampleX = new double[vectorizationTest.Length];
                var sampleY = new double[vectorizationTest.Length];
                for (int i = 0; i < vectorizationTest.Length; i++)
                {
                    sampleX[i] = xs[vectorizationTest[i]];
                    sampleY[i] = ys[vectorizationTest[i]];
                }

                double[] again = f(sampleX, sampleY, 2, vectorizationTest.Length / 2);
                calls++;
                if (again.Length != vectorizationTest.Length)
                {
                    throw new ArgumentException(
                        $"the integrand answered {again.Length} value(s) for {vectorizationTest.Length} point(s).");
                }

                for (int i = 0; i < vectorizationTest.Length; i++)
                {
                    double was = z[vectorizationTest[i]];
                    double allowed = Math.Max(absolute, relative * Math.Max(Math.Abs(again[i]), Math.Abs(was)));
                    if (Math.Abs(again[i] - was) > allowed)
                    {
                        vectorized = false;
                    }
                }

                first = false;
            }

            var weighted = new double[side * side];
            for (int k = 0; k < side; k++)
            {
                for (int j = 0; j < side; j++)
                {
                    double rate = singular
                        ? 0.25 * (xmax - xmin) * Math.Sin(phi[j]) * height[k] * Math.Sin(theta[k])
                        : height[k];
                    weighted[j + (side * k)] = z[j + (side * k)] * rate;
                }
            }

            // The four quarters, in the order (left, bottom), (right, bottom), (left, top),
            // (right, top): the first seven abscissae of each side are its lower half.
            double scale = dtheta / 4.0 * (dphi / 4.0);
            quarters = new double[4];
            quarterError = new double[4];
            for (int quarter = 0; quarter < 4; quarter++)
            {
                int columnBase = (quarter % 2) * NodeCount;
                int rowBase = (quarter / 2) * NodeCount;
                double kronrod = 0.0;
                double gauss = 0.0;
                for (int k = 0; k < NodeCount; k++)
                {
                    double kronrodColumn = 0.0;
                    double gaussColumn = 0.0;
                    for (int j = 0; j < NodeCount; j++)
                    {
                        double v = weighted[rowBase + j + (side * (columnBase + k))];
                        kronrodColumn += KronrodWeights[j] * v;
                        gaussColumn += GaussWeights[j] * v;
                    }

                    kronrod += KronrodWeights[k] * kronrodColumn;
                    gauss += GaussWeights[k] * gaussColumn;
                }

                quarters[quarter] = kronrod * scale;
                quarterError[quarter] = Math.Abs((gauss * scale) - quarters[quarter]);
            }

            return true;
        }

        /// <summary>
        /// Files four quarters: each one whose adjusted error is over its share of the tolerance goes
        /// on the list to be divided again, and the rest have their error added to the settled total.
        /// Answers the updated bound on the whole integration's error.
        /// </summary>
        private double Save(
            double[] quarters, double[] quarterError,
            double thetaL, double thetaR, double phiB, double phiT,
            double tolerance, double area, double adjust, ref double settledError)
        {
            double halfTheta = (thetaR - thetaL) / 2.0;
            double thetaM = thetaL + halfTheta;
            double halfPhi = (phiT - phiB) / 2.0;
            double phiM = phiB + halfPhi;
            double sum = quarters[0] + quarters[1] + quarters[2] + quarters[3];
            double local = Math.Max(
                Math.Abs(tolerance * halfTheta * halfPhi / area), HundredUlps * Math.Abs(sum));
            for (int quarter = 0; quarter < 4; quarter++)
            {
                double adjusted = adjust * quarterError[quarter];
                if (adjusted <= local)
                {
                    settledError += adjusted;
                    continue;
                }

                double left = (quarter % 2) == 0 ? thetaL : thetaM;
                double right = (quarter % 2) == 0 ? thetaM : thetaR;
                double bottom = quarter < 2 ? phiB : phiM;
                double top = quarter < 2 ? phiM : phiT;
                Add(quarters[quarter], quarterError[quarter], left, right, bottom, top, adjusted);
            }

            return settledError + SumOfSortedErrors();
        }

        /// <summary>Adds one tile, keeping the error list in ascending order.</summary>
        private void Add(double value, double raw, double left, double right, double bottom, double top,
            double adjusted)
        {
            if (tiles >= crossReference.Length)
            {
                int grown = crossReference.Length + 64;
                Array.Resize(ref error, grown);
                Array.Resize(ref leftEdge, grown);
                Array.Resize(ref rightEdge, grown);
                Array.Resize(ref bottomEdge, grown);
                Array.Resize(ref topEdge, grown);
                Array.Resize(ref tileValue, grown);
                Array.Resize(ref sortedError, grown);
                Array.Resize(ref crossReference, grown);
            }

            int at = tiles;
            for (int i = 0; i < tiles; i++)
            {
                if (adjusted < sortedError[i])
                {
                    at = i;
                    break;
                }
            }

            for (int i = tiles; i > at; i--)
            {
                sortedError[i] = sortedError[i - 1];
                crossReference[i] = crossReference[i - 1];
            }

            sortedError[at] = adjusted;
            crossReference[at] = tiles;
            error[tiles] = raw;
            leftEdge[tiles] = left;
            rightEdge[tiles] = right;
            bottomEdge[tiles] = bottom;
            topEdge[tiles] = top;
            tileValue[tiles] = value;
            tiles++;
        }

        /// <summary>
        /// Takes the tile with the largest adjusted error off the lists — or, once there are more
        /// than two thousand of them, the smallest, which is what stops the list growing without end
        /// on a problem the rule cannot resolve.
        /// </summary>
        private void Next(
            out double value, out double raw, out double left, out double right,
            out double bottom, out double top, out double adjusted)
        {
            bool smallestFirst = tiles > 2000;
            int index = smallestFirst ? crossReference[0] : crossReference[tiles - 1];
            adjusted = smallestFirst ? sortedError[0] : sortedError[tiles - 1];
            raw = error[index];
            left = leftEdge[index];
            right = rightEdge[index];
            bottom = bottomEdge[index];
            top = topEdge[index];
            value = tileValue[index];
            if (index != tiles - 1)
            {
                error[index] = error[tiles - 1];
                leftEdge[index] = leftEdge[tiles - 1];
                rightEdge[index] = rightEdge[tiles - 1];
                bottomEdge[index] = bottomEdge[tiles - 1];
                topEdge[index] = topEdge[tiles - 1];
                tileValue[index] = tileValue[tiles - 1];
                for (int i = 0; i < tiles; i++)
                {
                    if (crossReference[i] == tiles - 1)
                    {
                        crossReference[i] = index;
                        break;
                    }
                }
            }

            if (smallestFirst)
            {
                for (int i = 0; i + 1 < tiles; i++)
                {
                    crossReference[i] = crossReference[i + 1];
                    sortedError[i] = sortedError[i + 1];
                }
            }

            tiles--;
            crossReference[tiles] = 0;
            sortedError[tiles] = 0.0;
        }

        private double SumOfSortedErrors()
        {
            double sum = 0.0;
            for (int i = 0; i < tiles; i++)
            {
                sum += sortedError[i];
            }

            return sum;
        }
    }
}
