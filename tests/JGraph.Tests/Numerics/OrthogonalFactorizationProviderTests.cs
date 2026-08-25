using System.Numerics;
using JGraph.Numerics.LinearAlgebra;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// The M90 provider surface: Householder QR with and without pivoting, the two ways of applying the
/// reflectors, the singular value decomposition, and the two eigensolvers. Every assertion is a
/// property that holds whichever backend computed it — ‖A − Q·R‖, ‖Qᵀ·Q − I‖, ‖A − U·Σ·Vᵀ‖,
/// ‖A·v − λ·v‖ — because LAPACK and the managed kernels agree on the answer and not on the sign of
/// any particular column of it. Where a convention <em>is</em> fixed (R's diagonal, ascending
/// symmetric eigenvalues, conjugate pairs adjacent with the positive part first) it is asserted
/// directly, because the two backends have to agree on that or nothing above them can.
/// </summary>
public class OrthogonalFactorizationProviderTests
{
    private const double Tolerance = 1e-9;

    private static readonly ManagedLinalg Managed = new();

    public static TheoryData<string> BackendNames()
    {
        var data = new TheoryData<string> { "managed" };
        if (LinalgProvider.NativeAvailable)
        {
            data.Add("native");
        }

        return data;
    }

    private static DenseLinalg Backend(string name) =>
        name == "managed" ? Managed : new OpenBlasLinalg();

    /// <summary>A deterministic m-by-n matrix of full rank, column-major.</summary>
    private static double[] Rectangular(int m, int n)
    {
        var a = new double[m * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < m; r++)
            {
                a[(c * m) + r] = Math.Sin(0.7 * (r + 1)) + Math.Cos(1.3 * (c + 1)) + (r == c ? m : 0);
            }
        }

        return a;
    }

    /// <summary>A deterministic symmetric n-by-n matrix, column-major.</summary>
    private static double[] Symmetric(int n)
    {
        var a = new double[n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = c; r < n; r++)
            {
                double value = Math.Cos(0.4 * ((r * n) + c + 1)) + (r == c ? n : 0);
                a[(c * n) + r] = value;
                a[(r * n) + c] = value;
            }
        }

        return a;
    }

    private static double[] Product(ReadOnlySpan<double> a, int m, int k,
        ReadOnlySpan<double> b, int n)
    {
        var c = new double[m * n];
        for (int j = 0; j < n; j++)
        {
            for (int p = 0; p < k; p++)
            {
                double scale = b[(j * k) + p];
                for (int i = 0; i < m; i++)
                {
                    c[(j * m) + i] += a[(p * m) + i] * scale;
                }
            }
        }

        return c;
    }

    private static double MaximumDifference(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
    {
        double worst = 0;
        for (int i = 0; i < a.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(a[i] - b[i]));
        }

        return worst;
    }

    private static void AssertOrthonormalColumns(ReadOnlySpan<double> q, int rows, int columns)
    {
        for (int i = 0; i < columns; i++)
        {
            for (int j = 0; j < columns; j++)
            {
                double dot = 0;
                for (int r = 0; r < rows; r++)
                {
                    dot += q[(i * rows) + r] * q[(j * rows) + r];
                }

                Assert.InRange(Math.Abs(dot - (i == j ? 1 : 0)), 0, Tolerance);
            }
        }
    }

    /// <summary>Expands a factorization's reflectors into an m-by-<paramref name="columns"/> Q.</summary>
    private static double[] ExpandQ(DenseLinalg backend, ReadOnlySpan<double> factored,
        ReadOnlySpan<double> tau, int m, int reflectors, int columns)
    {
        var q = new double[m * columns];
        for (int c = 0; c < Math.Min(reflectors, columns); c++)
        {
            factored.Slice(c * m, m).CopyTo(q.AsSpan(c * m, m));
        }

        backend.Orgqr(m, columns, reflectors, q, m, tau);
        return q;
    }

    /// <summary>Reads R out of a factorization: the upper trapezoid, zero below the diagonal.</summary>
    private static double[] UpperTriangle(ReadOnlySpan<double> factored, int m, int n, int rows)
    {
        var r = new double[rows * n];
        for (int c = 0; c < n; c++)
        {
            for (int i = 0; i <= Math.Min(c, rows - 1); i++)
            {
                r[(c * rows) + i] = factored[(c * m) + i];
            }
        }

        return r;
    }

    // --- QR ---------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void ReflectorsRebuildTheMatrixTheyFactored(string name)
    {
        DenseLinalg backend = Backend(name);
        foreach ((int m, int n) in new[] { (6, 4), (4, 4), (3, 7), (5, 1), (1, 5) })
        {
            double[] original = Rectangular(m, n);
            double[] factored = (double[])original.Clone();
            int p = Math.Min(m, n);
            var tau = new double[p];
            Assert.Equal(0, backend.Geqrf(m, n, factored, m, tau));

            double[] q = ExpandQ(backend, factored, tau, m, p, p);
            AssertOrthonormalColumns(q, m, p);
            double[] r = UpperTriangle(factored, m, n, p);
            Assert.InRange(MaximumDifference(Product(q, m, p, r, n), original), 0, Tolerance);
        }
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void TheFullOrthogonalFactorSpansTheWholeSpace(string name)
    {
        DenseLinalg backend = Backend(name);
        const int M = 6;
        const int N = 3;
        double[] original = Rectangular(M, N);
        double[] factored = (double[])original.Clone();
        var tau = new double[N];
        backend.Geqrf(M, N, factored, M, tau);

        double[] q = ExpandQ(backend, factored, tau, M, N, M);
        AssertOrthonormalColumns(q, M, M);

        // The full Q times the full m-by-n R is the matrix again: the extra columns of Q meet the
        // zero rows of R, which is the whole point of the pairing.
        double[] r = UpperTriangle(factored, M, N, M);
        Assert.InRange(MaximumDifference(Product(q, M, M, r, N), original), 0, Tolerance);
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void AnAlreadyTriangularColumnIsLeftAloneRatherThanNegated(string name)
    {
        // LAPACK's dlarfg takes the identity reflection when a column is already zero below the
        // diagonal, which is why qr(eye(n)) is eye(n) and not its negative — the convention JGraph's
        // own kernel adopted at M90, having previously reflected every column whether or not it
        // needed it.
        DenseLinalg backend = Backend(name);
        const int N = 4;
        var identity = new double[N * N];
        for (int i = 0; i < N; i++)
        {
            identity[(i * N) + i] = 1;
        }

        var tau = new double[N];
        backend.Geqrf(N, N, identity, N, tau);
        for (int i = 0; i < N; i++)
        {
            Assert.Equal(0, tau[i]);
            Assert.Equal(1, identity[(i * N) + i]);
        }
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void ApplyingTheReflectorsMatchesMultiplyingByTheFactorTheyStandFor(string name)
    {
        DenseLinalg backend = Backend(name);
        const int M = 5;
        const int N = 3;
        double[] factored = Rectangular(M, N);
        var tau = new double[N];
        backend.Geqrf(M, N, factored, M, tau);
        double[] q = ExpandQ(backend, factored, tau, M, N, M);

        // Qᵀ·B from the left, against the same product formed explicitly.
        double[] b = Rectangular(M, 2);
        var applied = (double[])b.Clone();
        backend.Ormqr(leftSide: true, transpose: true, M, 2, N, factored, M, tau, applied, M);

        var transposed = new double[M * M];
        for (int c = 0; c < M; c++)
        {
            for (int r = 0; r < M; r++)
            {
                transposed[(c * M) + r] = q[(r * M) + c];
            }
        }

        Assert.InRange(MaximumDifference(applied, Product(transposed, M, M, b, 2)), 0, Tolerance);

        // And C·Q from the right, which is the same reflectors walked the other way.
        double[] c2 = Rectangular(2, M);
        var right = (double[])c2.Clone();
        backend.Ormqr(leftSide: false, transpose: false, 2, M, N, factored, M, tau, right, 2);
        Assert.InRange(MaximumDifference(right, Product(c2, 2, M, q, M)), 0, Tolerance);
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void PivotingOrdersTheDiagonalAndRecordsWhereEachColumnCameFrom(string name)
    {
        DenseLinalg backend = Backend(name);
        const int M = 6;
        const int N = 4;
        double[] original = Rectangular(M, N);
        var factored = (double[])original.Clone();
        var jpvt = new int[N];
        var tau = new double[N];
        Assert.Equal(0, backend.Geqp3(M, N, factored, M, jpvt, tau));

        // The record is a permutation, 1-based.
        Assert.Equal(Enumerable.Range(1, N).ToArray(), jpvt.OrderBy(x => x).ToArray());

        // |R(k,k)| never rises: that ordering is what makes the factorization report rank honestly.
        for (int k = 1; k < N; k++)
        {
            Assert.True(Math.Abs(factored[(k * M) + k]) <= Math.Abs(factored[((k - 1) * M) + (k - 1)]) + Tolerance);
        }

        // A·P = Q·R with P read off the record.
        double[] q = ExpandQ(backend, factored, tau, M, N, N);
        double[] r = UpperTriangle(factored, M, N, N);
        double[] reassembled = Product(q, M, N, r, N);
        for (int c = 0; c < N; c++)
        {
            for (int row = 0; row < M; row++)
            {
                Assert.InRange(
                    Math.Abs(reassembled[(c * M) + row] - original[((jpvt[c] - 1) * M) + row]), 0, Tolerance);
            }
        }
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void APivotedFactorizationOfARankDeficientMatrixStopsOnTheZero(string name)
    {
        // Three columns, the third the sum of the first two: R's third diagonal is zero, and that is
        // the only place the deficiency is visible.
        DenseLinalg backend = Backend(name);
        const int M = 4;
        var a = new double[M * 3];
        for (int r = 0; r < M; r++)
        {
            a[r] = r + 1;
            a[M + r] = Math.Sin(r + 1.0);
            a[(2 * M) + r] = a[r] + a[M + r];
        }

        var jpvt = new int[3];
        var tau = new double[3];
        backend.Geqp3(M, 3, a, M, jpvt, tau);
        Assert.InRange(Math.Abs(a[(2 * M) + 2]), 0, 1e-12);
    }

    // --- SVD --------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void TheDecompositionReassemblesTheMatrixAtEveryShape(string name)
    {
        DenseLinalg backend = Backend(name);
        foreach ((int m, int n) in new[] { (5, 3), (4, 4), (2, 6), (1, 4), (4, 1) })
        {
            foreach (SvdVectors job in new[] { SvdVectors.Economy, SvdVectors.All })
            {
                int k = Math.Min(m, n);
                int uColumns = job == SvdVectors.All ? m : k;
                int vtRows = job == SvdVectors.All ? n : k;
                double[] original = Rectangular(m, n);
                var work = (double[])original.Clone();
                var s = new double[k];
                var u = new double[m * uColumns];
                var vt = new double[vtRows * n];
                Assert.Equal(0, backend.Gesdd(job, m, n, work, m, s, u, m, vt, vtRows));

                for (int i = 1; i < k; i++)
                {
                    Assert.True(s[i] <= s[i - 1] + Tolerance, $"{name} {m}x{n}: values not descending");
                }

                AssertOrthonormalColumns(u, m, uColumns);

                // Σ·Vᵀ then U·(Σ·Vᵀ): the diagonal scales Vᵀ's rows, which is what a k-by-n Σ does.
                var scaled = new double[uColumns * n];
                for (int c = 0; c < n; c++)
                {
                    for (int i = 0; i < Math.Min(k, uColumns); i++)
                    {
                        scaled[(c * uColumns) + i] = s[i] * vt[(c * vtRows) + i];
                    }
                }

                Assert.InRange(
                    MaximumDifference(Product(u, m, uColumns, scaled, n), original), 0, Tolerance);
            }
        }
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void TheValuesOnlyJobAgreesWithTheOneThatKeepsTheFactors(string name)
    {
        DenseLinalg backend = Backend(name);
        const int M = 6;
        const int N = 4;
        double[] original = Rectangular(M, N);

        var alone = new double[N];
        var work = (double[])original.Clone();
        backend.Gesdd(SvdVectors.None, M, N, work, M, alone, [], 1, [], 1);

        var withFactors = new double[N];
        work = (double[])original.Clone();
        backend.Gesdd(SvdVectors.Economy, M, N, work, M, withFactors, new double[M * N], M, new double[N * N], N);

        Assert.InRange(MaximumDifference(alone, withFactors), 0, Tolerance);
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void TheFallbackDriverFindsTheSameSingularValues(string name)
    {
        // On the native backend these are two different LAPACK algorithms — divide and conquer, and
        // QR iteration — and the second exists only so that a failure of the first is invisible.
        DenseLinalg backend = Backend(name);
        const int M = 5;
        const int N = 5;
        double[] original = Rectangular(M, N);

        var first = new double[N];
        var work = (double[])original.Clone();
        backend.Gesdd(SvdVectors.None, M, N, work, M, first, [], 1, [], 1);

        var second = new double[N];
        work = (double[])original.Clone();
        backend.Gesvd(SvdVectors.None, M, N, work, M, second, [], 1, [], 1);

        Assert.InRange(MaximumDifference(first, second), 0, Tolerance);
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void ARankDeficientMatrixStillLeavesAFullOrthonormalBasis(string name)
    {
        // The zero singular values have no direction of their own; the factor still has to carry a
        // whole basis, so the columns that go with them are completed rather than left at zero.
        DenseLinalg backend = Backend(name);
        const int M = 4;
        const int N = 3;
        var a = new double[M * N];
        for (int r = 0; r < M; r++)
        {
            a[r] = 1;
            a[M + r] = 2;
            a[(2 * M) + r] = 3;
        }

        var s = new double[N];
        var u = new double[M * M];
        var vt = new double[N * N];
        backend.Gesdd(SvdVectors.All, M, N, a, M, s, u, M, vt, N);

        Assert.InRange(s[1], 0, 1e-12);
        AssertOrthonormalColumns(u, M, M);
    }

    // --- symmetric eigenvalues ---------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void ASymmetricSpectrumComesBackAscendingWithOrthonormalVectors(string name)
    {
        DenseLinalg backend = Backend(name);
        const int N = 6;
        double[] original = Symmetric(N);
        var work = (double[])original.Clone();
        var w = new double[N];
        Assert.Equal(0, backend.Syevd(vectors: true, lower: true, N, work, N, w));

        for (int i = 1; i < N; i++)
        {
            Assert.True(w[i] >= w[i - 1] - Tolerance, "eigenvalues are not ascending");
        }

        AssertOrthonormalColumns(work, N, N);

        // A·V = V·Λ, column by column.
        double[] left = Product(original, N, N, work, N);
        for (int c = 0; c < N; c++)
        {
            for (int r = 0; r < N; r++)
            {
                Assert.InRange(Math.Abs(left[(c * N) + r] - (w[c] * work[(c * N) + r])), 0, Tolerance);
            }
        }
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void OnlyTheNamedTriangleIsRead(string name)
    {
        // Rubbish in the triangle that was not asked for must not reach the answer — the caller's
        // promise is about one half of the matrix and nothing else.
        DenseLinalg backend = Backend(name);
        const int N = 5;
        double[] clean = Symmetric(N);
        var fouled = (double[])clean.Clone();
        for (int c = 0; c < N; c++)
        {
            for (int r = 0; r < c; r++)
            {
                fouled[(c * N) + r] = 1e6; // the strict upper triangle, which 'lower' must ignore
            }
        }

        var expected = new double[N];
        var actual = new double[N];
        backend.Syevd(vectors: false, lower: true, N, (double[])clean.Clone(), N, expected);
        backend.Syevd(vectors: false, lower: true, N, fouled, N, actual);
        Assert.InRange(MaximumDifference(expected, actual), 0, Tolerance);
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void TheTwoTrianglesOfASymmetricMatrixGiveTheSameSpectrum(string name)
    {
        DenseLinalg backend = Backend(name);
        const int N = 5;
        double[] original = Symmetric(N);
        var lower = new double[N];
        var upper = new double[N];
        backend.Syevd(vectors: false, lower: true, N, (double[])original.Clone(), N, lower);
        backend.Syevd(vectors: false, lower: false, N, (double[])original.Clone(), N, upper);
        Assert.InRange(MaximumDifference(lower, upper), 0, Tolerance);
    }

    // --- general eigenvalues ------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void EveryEigenpairSatisfiesItsOwnEquation(string name)
    {
        DenseLinalg backend = Backend(name);
        const int N = 5;
        double[] original = Rectangular(N, N);
        var work = (double[])original.Clone();
        var wr = new double[N];
        var wi = new double[N];
        var vr = new double[N * N];
        Assert.Equal(0, backend.Geev(vectors: true, N, work, N, wr, wi, vr, N));

        Complex[,] vectors = DenseLinalg.ComplexVectorsOf(vr, wi, N, N);
        for (int c = 0; c < N; c++)
        {
            var value = new Complex(wr[c], wi[c]);
            for (int r = 0; r < N; r++)
            {
                Complex product = Complex.Zero;
                for (int k = 0; k < N; k++)
                {
                    product += original[(k * N) + r] * vectors[k, c];
                }

                Assert.InRange((product - (value * vectors[r, c])).Magnitude, 0, Tolerance);
            }
        }
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void AConjugatePairArrivesAdjacentWithThePositivePartFirst(string name)
    {
        // A rotation by 90°: its eigenvalues are ±i and there is no real vector to be had. The
        // packing depends on the pairing, so the pairing is part of the contract and not a habit.
        DenseLinalg backend = Backend(name);
        double[] rotation = [0, 1, -1, 0];
        var wr = new double[2];
        var wi = new double[2];
        var vr = new double[4];
        backend.Geev(vectors: true, 2, rotation, 2, wr, wi, vr, 2);

        Assert.InRange(Math.Abs(wr[0]), 0, Tolerance);
        Assert.InRange(Math.Abs(wr[1]), 0, Tolerance);
        Assert.True(wi[0] > 0, "the positive imaginary part comes first");
        Assert.InRange(Math.Abs(wi[0] + wi[1]), 0, Tolerance);

        Complex[,] vectors = DenseLinalg.ComplexVectorsOf(vr, wi, 2, 2);
        for (int r = 0; r < 2; r++)
        {
            Assert.Equal(Complex.Conjugate(vectors[r, 0]), vectors[r, 1]);
        }
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void EveryEigenvectorHasUnitLength(string name)
    {
        DenseLinalg backend = Backend(name);
        const int N = 5;
        var work = Rectangular(N, N);
        var wr = new double[N];
        var wi = new double[N];
        var vr = new double[N * N];
        backend.Geev(vectors: true, N, work, N, wr, wi, vr, N);

        Complex[,] vectors = DenseLinalg.ComplexVectorsOf(vr, wi, N, N);
        for (int c = 0; c < N; c++)
        {
            double norm = 0;
            for (int r = 0; r < N; r++)
            {
                norm += vectors[r, c].Magnitude * vectors[r, c].Magnitude;
            }

            Assert.InRange(Math.Abs(Math.Sqrt(norm) - 1), 0, Tolerance);
        }
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void AskingForNoVectorsStillGivesTheSameValues(string name)
    {
        DenseLinalg backend = Backend(name);
        const int N = 5;
        double[] original = Rectangular(N, N);

        var withVectors = new double[N];
        var imaginary = new double[N];
        backend.Geev(vectors: true, N, (double[])original.Clone(), N, withVectors, imaginary, new double[N * N], N);

        var alone = new double[N];
        var aloneImaginary = new double[N];
        backend.Geev(vectors: false, N, (double[])original.Clone(), N, alone, aloneImaginary, [], 1);

        Assert.InRange(MaximumDifference(withVectors, alone), 0, Tolerance);
        Assert.InRange(MaximumDifference(imaginary, aloneImaginary), 0, Tolerance);
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void BadlyScaledEigenvaluesSurviveBecauseTheMatrixIsBalancedFirst(string name)
    {
        // 1 and 1 ± sqrt(2), buried under six orders of magnitude of scaling. Without balancing the
        // iteration spends five digits on the scale rather than on the answer, so this is the test
        // that both backends do the balancing step and not merely one of them.
        DenseLinalg backend = Backend(name);
        double[] scaled = [1, 1e-6, 0, 1e6, 1, 1e6, 0, 1e-6, 1];
        var wr = new double[3];
        var wi = new double[3];
        backend.Geev(vectors: false, 3, scaled, 3, wr, wi, [], 1);

        double[] found = wr.OrderBy(x => x).ToArray();
        double[] exact = [1 - Math.Sqrt(2), 1, 1 + Math.Sqrt(2)];
        Assert.InRange(MaximumDifference(found, exact), 0, 1e-13);
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void APaddedLeadingDimensionIsHonouredRatherThanAssumedAway(string name)
    {
        // Every one of these takes a leading dimension that may exceed the row count, and a kernel
        // that reads the buffer as though it were compact would silently read the padding as data.
        // The padding here is loud enough that it could not be mistaken for a rounding difference.
        DenseLinalg backend = Backend(name);
        const int N = 5;
        const int Pad = N + 3;

        double[] compact = Symmetric(N);
        var padded = new double[Pad * N];
        Array.Fill(padded, 1e9);
        for (int c = 0; c < N; c++)
        {
            compact.AsSpan(c * N, N).CopyTo(padded.AsSpan(c * Pad, N));
        }

        var tight = new double[N];
        var loose = new double[N];
        backend.Syevd(vectors: false, lower: true, N, (double[])compact.Clone(), N, tight);
        backend.Syevd(vectors: false, lower: true, N, (double[])padded.Clone(), Pad, loose);
        Assert.InRange(MaximumDifference(tight, loose), 0, Tolerance);

        double[] general = Rectangular(N, N);
        var stretched = new double[Pad * N];
        Array.Fill(stretched, 1e9);
        for (int c = 0; c < N; c++)
        {
            general.AsSpan(c * N, N).CopyTo(stretched.AsSpan(c * Pad, N));
        }

        var wr = new double[N];
        var wi = new double[N];
        var paddedWr = new double[N];
        var paddedWi = new double[N];
        backend.Geev(vectors: true, N, (double[])general.Clone(), N, wr, wi, new double[N * N], N);
        backend.Geev(vectors: true, N, (double[])stretched.Clone(), Pad, paddedWr, paddedWi,
            new double[Pad * N], Pad);
        Assert.InRange(MaximumDifference(wr, paddedWr), 0, Tolerance);
        Assert.InRange(MaximumDifference(wi, paddedWi), 0, Tolerance);

        var values = new double[N];
        var paddedValues = new double[N];
        backend.Gesdd(SvdVectors.None, N, N, (double[])general.Clone(), N, values, [], 1, [], 1);
        backend.Gesdd(SvdVectors.None, N, N, (double[])stretched.Clone(), Pad, paddedValues, [], 1, [], 1);
        Assert.InRange(MaximumDifference(values, paddedValues), 0, Tolerance);

        var tau = new double[N];
        var paddedTau = new double[N];
        var factored = (double[])general.Clone();
        var paddedFactored = (double[])stretched.Clone();
        backend.Geqrf(N, N, factored, N, tau);
        backend.Geqrf(N, N, paddedFactored, Pad, paddedTau);
        Assert.InRange(MaximumDifference(tau, paddedTau), 0, Tolerance);
        for (int c = 0; c < N; c++)
        {
            for (int r = 0; r < N; r++)
            {
                Assert.InRange(Math.Abs(factored[(c * N) + r] - paddedFactored[(c * Pad) + r]), 0, Tolerance);
            }
        }
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void AnEmptyMatrixIsFactoredWithoutComplaint(string name)
    {
        DenseLinalg backend = Backend(name);
        Assert.Equal(0, backend.Geqrf(0, 0, [], 1, []));
        Assert.Equal(0, backend.Orgqr(0, 0, 0, [], 1, []));
        Assert.Equal(0, backend.Geqp3(0, 0, [], 1, [], []));
        Assert.Equal(0, backend.Gesdd(SvdVectors.All, 0, 0, [], 1, [], [], 1, [], 1));
        Assert.Equal(0, backend.Syevd(vectors: true, lower: true, 0, [], 1, []));
        Assert.Equal(0, backend.Geev(vectors: true, 0, [], 1, [], [], [], 1));
    }

    [Fact]
    public void TheUnpackingTurnsRealColumnsIntoTheComplexPairTheyStandFor()
    {
        // Two columns of reals, one imaginary part flagging the pair: out come a vector and its
        // conjugate, and a third column with no flag stays real.
        double[] vr = [1, 2, 3, 4, 5, 6, 7, 8, 9];
        double[] wi = [2, -2, 0];
        Complex[,] vectors = DenseLinalg.ComplexVectorsOf(vr, wi, 3, 3);

        Assert.Equal(new Complex(1, 4), vectors[0, 0]);
        Assert.Equal(new Complex(1, -4), vectors[0, 1]);
        Assert.Equal(new Complex(3, 6), vectors[2, 0]);
        Assert.Equal(new Complex(9, 0), vectors[2, 2]);
    }
}
