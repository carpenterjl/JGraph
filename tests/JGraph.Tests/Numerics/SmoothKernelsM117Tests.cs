using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// M117: the shaped-window smoothers stop rebuilding a normal system per output sample and start
/// applying one row of weights worked out once. What has to hold is that the answer did not move,
/// so every case below is measured against a reference that is the walk this replaced — the window
/// gathered into a pair of lists, tricube weights measured from the furthest reading in it, a
/// normal system built by an outer product per reading and solved by the same elimination.
/// </summary>
/// <remarks>
/// <para>
/// The comparison is to a tolerance rather than bit for bit, and deliberately so: a fit reached by
/// solving a system per window and one reached by applying that system's answer as a row of weights
/// are two routes to the same number, not the same arithmetic. What the tolerance is there to catch
/// is a different answer, not a differently rounded one — it is tight enough that a wrong kernel, a
/// misplaced offset or a window off by one fails it by many orders of magnitude.
/// </para>
/// <para>
/// The exact claims are made separately, and they are the ones worth making exactly: a polynomial of
/// the degree being fitted comes back unchanged, and a window that cannot support the degree asked
/// of it retreats where the walk retreated.
/// </para>
/// </remarks>
public class SmoothKernelsM117Tests
{
    /// <summary>Windows that reach behind, ahead, both, neither, and past both ends at once.</summary>
    public static TheoryData<int, int> Windows() => new()
    {
        { 0, 0 },
        { 1, 0 },
        { 0, 1 },
        { 1, 1 },
        { 2, 2 },
        { 4, 3 },
        { 3, 4 },
        { 9, 9 },
        { 17, 0 },
        { 60, 60 }, // wider than the data, so no window is a whole one
    };

    public static TheoryData<int, bool> Fits() => new()
    {
        { 1, true },  // lowess
        { 2, true },  // loess
        { 1, false }, // sgolay, degree one
        { 2, false }, // sgolay, its usual degree
        { 3, false },
        { 4, false },
    };

    /// <summary>Series that make each rule matter, none of them holding anything infinite.</summary>
    public static TheoryData<string> Series() => new()
    {
        "ramp",
        "wave",
        "noise",
        "flat",
        "step",
        "spike",
        "tiny",
        "huge",
    };

    [Theory]
    [MemberData(nameof(Windows))]
    public void AGaussianWindowAnswersWhatTheWalkAnswered(int behind, int ahead)
    {
        foreach (string shape in new[] { "ramp", "wave", "noise", "flat", "step", "spike", "tiny", "huge" })
        {
            double[] values = Made(shape, 50);
            double window = behind + ahead + 1;
            double[] wanted = WalkedGaussian(values, behind, ahead, window);
            double[] got = SmoothKernels.Gaussian(values, behind, ahead, window);
            AssertAgrees(wanted, got, values, $"gaussian {shape} [{behind},{ahead}]");
        }
    }

    [Theory]
    [MemberData(nameof(Fits))]
    public void ALocalPolynomialAnswersWhatTheWalkAnswered(int degree, bool weighted)
    {
        foreach (string shape in new[] { "ramp", "wave", "noise", "flat", "step", "spike", "tiny", "huge" })
        {
            double[] values = Made(shape, 50);
            foreach ((int behind, int ahead) in new[] { (0, 0), (1, 1), (2, 2), (4, 3), (9, 9), (60, 60) })
            {
                double[] wanted = WalkedFit(values, behind, ahead, degree, weighted);
                double[] got = SmoothKernels.LocalPolynomial(values, behind, ahead, degree, weighted);
                AssertAgrees(wanted, got, values, $"fit {shape} d{degree} w{weighted} [{behind},{ahead}]");
            }
        }
    }

    /// <summary>
    /// A polynomial of the degree being fitted is reproduced, which is the one property a local
    /// polynomial smoother has to have and the one an off-by-one kernel cannot fake.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void APolynomialOfTheDegreeFittedComesBackUnchanged(int degree)
    {
        var values = new double[80];
        for (int i = 0; i < values.Length; i++)
        {
            double x = (i - 40) / 20.0;
            values[i] = degree switch
            {
                1 => 3 - (0.5 * x),
                2 => 3 - (0.5 * x) + (2 * x * x),
                _ => 3 - (0.5 * x) + (2 * x * x) - (x * x * x),
            };
        }

        foreach (bool weighted in new[] { true, false })
        {
            double[] got = SmoothKernels.LocalPolynomial(values, 12, 12, degree, weighted);
            for (int i = 0; i < values.Length; i++)
            {
                Assert.True(
                    Math.Abs(got[i] - values[i]) < 1e-8,
                    $"degree {degree} weighted {weighted} at {i}: wanted {values[i]}, got {got[i]}");
            }
        }
    }

    /// <summary>A window of one reading is that reading, however it is being smoothed.</summary>
    [Fact]
    public void AWindowOfOneIsTheReadingItself()
    {
        double[] values = Made("noise", 20);
        Assert.Equal(values, SmoothKernels.Gaussian(values, 0, 0, 1));
        Assert.Equal(values, SmoothKernels.LocalPolynomial(values, 0, 0, 1, weighted: true));
        Assert.Equal(values, SmoothKernels.LocalPolynomial(values, 0, 0, 2, weighted: false));
    }

    /// <summary>
    /// A window with fewer readings than the degree needs cannot pin a polynomial, and the answer
    /// there is their plain mean — the one retreat available when there is nothing to fit through.
    /// </summary>
    /// <remarks>
    /// Every window here holds three readings, the ends included: a fit does not let its window
    /// shrink at the ends, so the first answer is the mean of the same three the second reads.
    /// </remarks>
    [Fact]
    public void TooFewReadingsForTheDegreeIsTheirPlainMean()
    {
        double[] values = [1, 2, 4, 8, 16, 32];

        // Degree 4 over a window of three: every window in this series is too small for it.
        double[] got = SmoothKernels.LocalPolynomial(values, 1, 1, 4, weighted: false);
        Assert.Equal((1 + 2 + 4) / 3.0, got[0], 12);
        Assert.Equal((1 + 2 + 4) / 3.0, got[1], 12);
        Assert.Equal((8 + 16 + 32) / 3.0, got[^1], 12);
    }

    /// <summary>Nothing to smooth is nothing smoothed, rather than an exception.</summary>
    [Fact]
    public void NothingIsSmoothedToNothing()
    {
        Assert.Empty(SmoothKernels.Gaussian([], 3, 3, 7));
        Assert.Empty(SmoothKernels.LocalPolynomial([], 3, 3, 2, weighted: true));
        Assert.Equal(0, SmoothKernels.Missing([]));
        Assert.Equal(2, SmoothKernels.Missing([1, double.NaN, 3, double.NaN]));
        Assert.Equal(0, SmoothKernels.Missing([1, double.PositiveInfinity, 3]));
    }

    /// <summary>
    /// A missing reading reaches exactly the windows that hold it and no others — which is the
    /// claim the tiling makes, and the one a tile boundary in the wrong place would break.
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(8200)]
    public void AMissingReadingReachesItsOwnWindowsAndNoOthers(int at)
    {
        var values = new double[12000];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = 1 + (i % 7);
        }

        values[at] = double.NaN;

        const int Behind = 5;
        const int Ahead = 5;
        foreach (double[] got in new[]
        {
            SmoothKernels.Gaussian(values, Behind, Ahead, Behind + Ahead + 1),
            SmoothKernels.LocalPolynomial(values, Behind, Ahead, 2, weighted: false),
        })
        {
            for (int i = 0; i < values.Length; i++)
            {
                bool touches = i >= at - Ahead && i <= at + Behind;
                Assert.True(
                    double.IsNaN(got[i]) == touches,
                    $"at {i} (missing at {at}): touches {touches} but answered {got[i]}");
            }
        }
    }

    /// <summary>
    /// The fit read at one place and the same fit read at another agree, which is what lets the
    /// robust passes solve once and evaluate many times rather than solving once per residual.
    /// </summary>
    [Fact]
    public void OneFitAnswersEverywhereItIsAsked()
    {
        double[] xs = [0, 1, 2, 3, 4, 5, 6];
        double[] ys = [1, 4, 2, 8, 5, 9, 3];
        double[] weights = [1, 1, 1, 1, 1, 1, 1];
        var normal = new double[3 * 4];
        var powers = new double[3];
        var atThree = new double[3];
        var atFive = new double[3];

        SmoothKernels.Fit(xs, ys, weights, 2, 3, normal, powers, atThree);
        SmoothKernels.Fit(xs, ys, weights, 2, 5, normal, powers, atFive);

        // Centred at three and read at five, against centred at five and read where it stands.
        Assert.Equal(atFive[0], SmoothKernels.At(atThree, 5 - 3), 9);
        Assert.Equal(atThree[0], SmoothKernels.At(atFive, 3 - 5), 9);
    }

    /// <summary>A system that will not factor leaves the weighted mean, as a constant polynomial.</summary>
    [Fact]
    public void ASystemThatWillNotFactorLeavesTheWeightedMean()
    {
        // Every reading at the same place: a straight line through them is not determined.
        double[] xs = [2, 2, 2, 2];
        double[] ys = [1, 3, 5, 11];
        double[] weights = [1, 1, 2, 0];
        var normal = new double[2 * 3];
        var powers = new double[2];
        var found = new double[2];

        SmoothKernels.Fit(xs, ys, weights, 1, 2, normal, powers, found);

        Assert.Equal(((1 * 1) + (3 * 1) + (5 * 2)) / 4.0, found[0], 12);
        Assert.Equal(0, found[1]);
    }

    private static void AssertAgrees(double[] wanted, double[] got, double[] values, string what)
    {
        Assert.Equal(wanted.Length, got.Length);
        double scale = 0;
        foreach (double value in values)
        {
            scale = Math.Max(scale, Math.Abs(value));
        }

        double slack = Math.Max(scale, 1) * 1e-8;
        for (int i = 0; i < wanted.Length; i++)
        {
            if (double.IsNaN(wanted[i]) && double.IsNaN(got[i]))
            {
                continue;
            }

            Assert.True(
                Math.Abs(wanted[i] - got[i]) <= slack,
                $"{what} at {i}: wanted {wanted[i]}, got {got[i]}");
        }
    }

    private static double[] Made(string shape, int n)
    {
        var values = new double[n];
        var seed = new Random(shape.Length * 977);
        for (int i = 0; i < n; i++)
        {
            values[i] = shape switch
            {
                "ramp" => (i * 0.75) - 12,
                "wave" => Math.Sin(i * 0.4) + (0.25 * Math.Cos(i * 1.7)),
                "noise" => (seed.NextDouble() * 6) - 3,
                "flat" => 2.5,
                "step" => i < n / 2 ? -1 : 4,
                "spike" => i == n / 3 ? 90 : 0.5,
                "tiny" => (i - (n / 2.0)) * 1e-9,
                _ => (i - (n / 2.0)) * 1e9,
            };
        }

        return values;
    }

    // --- the walk, written out again -------------------------------------------------------------
    //
    // These are the rules measured off MATLAB in M118, not the ones JGraph used to follow: the
    // Gaussian's standard deviation is a fifth of its window rather than a quarter, and a fit at
    // the ends reads the width nearest the point rather than a window cut short by the end of the
    // readings. Writing the walk out again is still worth it -- it says the kernel and the walk
    // agree -- but what it is written against is MATLAB.

    private static double[] WalkedGaussian(double[] values, int behind, int ahead, double window)
    {
        var result = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            int from = Math.Max(0, i - behind);
            int to = Math.Min(values.Length - 1, i + ahead);
            double sigma = Math.Max(window / 5.0, 1e-12);
            double total = 0;
            double weight = 0;
            for (int j = from; j <= to; j++)
            {
                double z = (j - i) / sigma;
                double w = Math.Exp(-0.5 * z * z);
                total += w * values[j];
                weight += w;
            }

            result[i] = weight == 0 ? double.NaN : total / weight;
        }

        return result;
    }

    private static double[] WalkedFit(double[] values, int behind, int ahead, int degree, bool weighted)
    {
        var result = new double[values.Length];
        int width = Math.Min(behind + ahead + 1, values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            // A fit reads the width nearest the point: at the ends the window stops sliding
            // rather than shrinking, which is why a fit reproduces a polynomial right up to the
            // first and last reading where a weighted average cannot.
            int from = Math.Clamp(i - behind, 0, values.Length - width);
            int to = from + width - 1;
            var xs = new List<double>();
            var ys = new List<double>();
            for (int j = from; j <= to; j++)
            {
                xs.Add(j);
                ys.Add(values[j]);
            }

            result[i] = LocalFit([.. xs], [.. ys], i, degree, weighted);
        }

        return result;
    }

    private static double LocalFit(double[] xs, double[] ys, double at, int degree, bool weighted)
    {
        int n = xs.Length;
        if (n == 0)
        {
            return double.NaN;
        }

        if (n <= degree)
        {
            return ys.Average();
        }

        double furthest = 0;
        foreach (double x in xs)
        {
            furthest = Math.Max(furthest, Math.Abs(x - at));
        }

        var weights = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (!weighted || furthest == 0)
            {
                weights[i] = 1;
                continue;
            }

            double u = Math.Abs(xs[i] - at) / furthest;
            double tri = 1 - (u * u * u);
            weights[i] = Math.Max(0, tri * tri * tri);
        }

        // A window that cannot pin a polynomial of this degree will often pin a lower one, and
        // that is what a least-squares solve of the rank-deficient system amounts to: it still
        // passes through the readings the window can see. Tricube weights make this ordinary
        // rather than exotic -- the outermost reading of a window carries a weight of exactly
        // zero, so a window of three readings is a window of two as far as the fit is concerned.
        for (int use = degree; use >= 1; use--)
        {
            double pinned = WeightedPolynomialAt(xs, ys, weights, use, at);
            if (!double.IsNaN(pinned))
            {
                return pinned;
            }
        }

        double whole = weights.Sum();
        if (whole == 0)
        {
            return ys.Average();
        }

        double leaning = 0;
        for (int i = 0; i < n; i++)
        {
            leaning += weights[i] * ys[i];
        }

        return leaning / whole;
    }

    /// <summary>The fit, or NaN when the window cannot pin a polynomial of this degree.</summary>
    private static double WeightedPolynomialAt(
        double[] xs, double[] ys, double[] weights, int degree, double at)
    {
        int terms = degree + 1;
        var normal = new double[terms, terms + 1];
        for (int i = 0; i < xs.Length; i++)
        {
            if (weights[i] <= 0)
            {
                continue;
            }

            var powers = new double[terms];
            double running = 1;
            for (int p = 0; p < terms; p++)
            {
                powers[p] = running;
                running *= xs[i] - at;
            }

            for (int r = 0; r < terms; r++)
            {
                for (int c = 0; c < terms; c++)
                {
                    normal[r, c] += weights[i] * powers[r] * powers[c];
                }

                normal[r, terms] += weights[i] * powers[r] * ys[i];
            }
        }

        for (int pivot = 0; pivot < terms; pivot++)
        {
            int best = pivot;
            for (int r = pivot + 1; r < terms; r++)
            {
                if (Math.Abs(normal[r, pivot]) > Math.Abs(normal[best, pivot]))
                {
                    best = r;
                }
            }

            if (Math.Abs(normal[best, pivot]) < 1e-12)
            {
                // Not pinned at this degree. The caller drops a degree and tries again, and only
                // retreats to a weighted mean when even a straight line will not stand up.
                return double.NaN;
            }

            if (best != pivot)
            {
                for (int c = pivot; c <= terms; c++)
                {
                    (normal[pivot, c], normal[best, c]) = (normal[best, c], normal[pivot, c]);
                }
            }

            for (int r = pivot + 1; r < terms; r++)
            {
                double factor = normal[r, pivot] / normal[pivot, pivot];
                for (int c = pivot; c <= terms; c++)
                {
                    normal[r, c] -= factor * normal[pivot, c];
                }
            }
        }

        var solution = new double[terms];
        for (int r = terms - 1; r >= 0; r--)
        {
            double sum = normal[r, terms];
            for (int c = r + 1; c < terms; c++)
            {
                sum -= normal[r, c] * solution[c];
            }

            solution[r] = sum / normal[r, r];
        }

        return solution[0];
    }
}
