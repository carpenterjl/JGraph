using JGraph.Numerics;
using JGraph.Numerics.LinearAlgebra;
using JGraph.Numerics.LinearAlgebra.Native;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// The every-row plan's I3 (ADR 0332): the copies into and out of the dense drivers are threaded
/// and fused, chol copies only the triangle it reads, qr expands Q where the reflectors lie, the
/// singular values are taken in the caller's copy, expm, hess and schur run column-major, and a
/// long thin factorization is sized by its footprint. Every new road is checked against the old
/// one, bit for bit, on one thread and on many.
/// </summary>
public class DenseDriverFillsI3Tests
{
    private static double[] Field(int rows, int cols, double a = 0.7, double b = 1.3)
    {
        var values = new double[(long)rows * cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                values[((long)c * rows) + r] = Math.Sin((a * (r + 1)) + (b * (c + 1) * (c + 1) / cols)) + (0.01 * r);
            }
        }

        return values;
    }

    private static double[] Definite(int n)
    {
        double[] m = Field(n, n);
        var s = new double[(long)n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                double sum = 0;
                for (int k = 0; k < n; k += 7)
                {
                    sum += m[((long)k * n) + r] * m[((long)k * n) + c];
                }

                s[((long)c * n) + r] = sum + (r == c ? n : 0);
            }
        }

        return s;
    }

    private static void OnThreads(Action check)
    {
        int degree = ParallelKernels.MaxDegree;
        try
        {
            ParallelKernels.MaxDegree = 1;
            check();
            ParallelKernels.MaxDegree = Math.Max(degree, 4);
            check();
        }
        finally
        {
            ParallelKernels.MaxDegree = degree;
        }
    }

    // --- the fills ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(1_048_579)]
    public void ACopyIsTheSourceWhateverTheThreads(int length) => OnThreads(() =>
    {
        double[] source = Field(length, 1);
        var copy = new double[length + 3];
        copy[^1] = 42;
        FactorFills.Copy(source, copy);
        Assert.Equal(source, copy.AsSpan(0, length).ToArray());
        Assert.Equal(42, copy[^1]);
    });

    [Theory]
    [InlineData(5, 5, 5)]
    [InlineData(3, 7, 5)]
    [InlineData(7, 3, 7)]
    [InlineData(1100, 1100, 1100)]
    [InlineData(900, 1300, 1200)]
    [InlineData(0, 4, 1)]
    public void TheUpperTrapezoidIsTheLoopsAnswer(int rows, int cols, int lead) => OnThreads(() =>
    {
        double[] source = Field(lead, cols);
        foreach (bool clear in new[] { false, true })
        {
            var expected = new double[(long)rows * cols];
            var got = new double[(long)rows * cols];
            Array.Fill(expected, -7.0);
            Array.Fill(got, -7.0);
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    if (r <= c)
                    {
                        expected[((long)c * rows) + r] = source[((long)c * lead) + r];
                    }
                    else if (clear)
                    {
                        expected[((long)c * rows) + r] = 0;
                    }
                }
            }

            FactorFills.Upper(source, lead, rows, cols, got, clear);
            Assert.Equal(expected, got);
        }
    });

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(1100)]
    public void TheLowerTriangleIsTheLoopsAnswer(int n) => OnThreads(() =>
    {
        double[] source = Field(n, n);
        int[] order = Enumerable.Range(0, n).Select(i => (i * 5 + 3) % n).ToArray();
        if (new HashSet<int>(order).Count != n)
        {
            order = Enumerable.Range(0, n).Reverse().ToArray();
        }

        foreach (bool unit in new[] { false, true })
        {
            foreach (bool clear in new[] { false, true })
            {
                var expected = new double[(long)n * n];
                var got = new double[(long)n * n];
                Array.Fill(expected, -7.0);
                Array.Fill(got, -7.0);
                for (int c = 0; c < n; c++)
                {
                    for (int r = 0; r < n; r++)
                    {
                        long at = ((long)c * n) + r;
                        expected[at] = r > c ? source[at] : r == c ? (unit ? 1 : source[at]) : clear ? 0 : -7.0;
                    }
                }

                FactorFills.Lower(source, n, got, unit, clear, rowOrder: null);
                Assert.Equal(expected, got);
            }

            var scattered = new double[(long)n * n];
            var reference = new double[(long)n * n];
            for (int c = 0; c < n; c++)
            {
                reference[((long)c * n) + order[c]] = unit ? 1 : source[((long)c * n) + c];
                for (int s = c + 1; s < n; s++)
                {
                    reference[((long)c * n) + order[s]] = source[((long)c * n) + s];
                }
            }

            FactorFills.Lower(source, n, scattered, unit, clearRest: false, order);
            Assert.Equal(reference, scattered);
        }

        Assert.Throws<ArgumentException>(() =>
            FactorFills.Lower(source, n, new double[(long)n * n], true, clearRest: true, order));
    });

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(1100)]
    public void ClearingTheOtherTriangleLeavesTheFactor(int n) => OnThreads(() =>
    {
        foreach (bool keepLower in new[] { false, true })
        {
            double[] got = Field(n, n);
            double[] expected = (double[])got.Clone();
            for (int c = 0; c < n; c++)
            {
                for (int r = 0; r < n; r++)
                {
                    if (keepLower ? r < c : r > c)
                    {
                        expected[((long)c * n) + r] = 0;
                    }
                }
            }

            FactorFills.ClearOtherTriangle(got, n, keepLower);
            Assert.Equal(expected, got);
        }
    });

    // --- the drivers ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(9)]
    [InlineData(1100)]
    public void CholeskyFromACopiedTriangleIsTheFactorOfTheWholeCopy(int n) => OnThreads(() =>
    {
        double[] s = Definite(n);
        foreach (bool lower in new[] { false, true })
        {
            Cholesky whole = Cholesky.FactorAdopting((double[])s.Clone(), n, lower);
            var triangle = new double[(long)n * n];
            if (lower)
            {
                FactorFills.Lower(s, n, triangle, unitDiagonal: false, clearRest: true, rowOrder: null);
            }
            else
            {
                FactorFills.Upper(s, n, n, n, triangle, clearRest: true);
            }

            Cholesky fused = Cholesky.FactorAdoptingTriangle(triangle, n, lower);
            Assert.True(whole.IsPositiveDefinite);
            Assert.Equal(whole.ColumnMajor, fused.ColumnMajor);
        }

        // A matrix that stops being definite part of the way: the same order, the same partial factor.
        double[] bad = (double[])s.Clone();
        int at = Math.Min(n - 1, 5);
        bad[((long)at * n) + at] = -1;
        Cholesky failedWhole = Cholesky.FactorAdopting((double[])bad.Clone(), n, false);
        var badTriangle = new double[(long)n * n];
        FactorFills.Upper(bad, n, n, n, badTriangle, clearRest: true);
        Cholesky failedFused = Cholesky.FactorAdoptingTriangle(badTriangle, n, false);
        Assert.False(failedFused.IsPositiveDefinite);
        Assert.Equal(failedWhole.FailedAt, failedFused.FailedAt);
        Assert.Equal(failedWhole.ColumnMajor, failedFused.ColumnMajor);
    });

    [Theory]
    [InlineData(300, 300)]
    [InlineData(500, 200)]
    [InlineData(200, 500)]
    [InlineData(1, 1)]
    [InlineData(1200, 1200)]
    public void QExpandedInPlaceIsQExpandedFromACopy(int m, int n) => OnThreads(() =>
    {
        double[] a = Field(m, n);
        foreach (bool full in new[] { false, true })
        {
            QrDecomposition reference = QrDecomposition.Factor(a, m, n, pivot: false);
            QrDecomposition taken = QrDecomposition.Factor(a, m, n, pivot: false);
            int rows = full ? m : Math.Min(m, n);

            // R the old way: element by element over a zeroed array.
            var oldR = new double[(long)rows * n];
            double[] packed = (double[])a.Clone();
            QrDecomposition.FactorAdopting(packed, m, n, pivot: false);
            for (int c = 0; c < n; c++)
            {
                for (int i = 0; i <= Math.Min(c, rows - 1); i++)
                {
                    oldR[((long)c * rows) + i] = packed[((long)c * m) + i];
                }
            }

            double[] r = reference.RColumnMajor(full);
            Assert.Equal(oldR, r);
            var written = new double[(long)rows * n];
            taken.WriteR(full, written);
            Assert.Equal(r, written);

            double[] q = reference.QColumnMajor(full);
            Assert.Equal(q, taken.TakeQColumnMajor(full));
            if ((full ? m : Math.Min(m, n)) == n)
            {
                Assert.Throws<InvalidOperationException>(() => taken.RColumnMajor(full));
            }
        }
    });

    [Theory]
    [InlineData(1, 1)]
    [InlineData(40, 25)]
    [InlineData(25, 40)]
    [InlineData(800, 800)]
    public void SingularValuesTakenInTheCopyAreTheValues(int m, int n)
    {
        double[] a = Field(m, n);
        double[] expected = Svd.SingularValues(a, m, n);
        Assert.True(Svd.TrySingularValuesAdopting((double[])a.Clone(), m, n, out double[] got));
        Assert.Equal(expected, got);
    }

    /// <summary>The exponential exactly as it was written before I3, on rectangles.</summary>
    private static double[,] ExponentialAsItWas(double[,] matrix)
    {
        int n = matrix.GetLength(0);
        double norm = 0;
        for (int r = 0; r < n; r++)
        {
            double rowSum = 0;
            for (int c = 0; c < n; c++)
            {
                rowSum += Math.Abs(matrix[r, c]);
            }

            norm = Math.Max(norm, rowSum);
        }

        int squarings = norm > 0.5 ? (int)Math.Max(0, Math.Floor(Math.Log2(norm)) + 2) : 0;
        double scale = Math.Pow(2, -squarings);
        var a = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                a[r, c] = matrix[r, c] * scale;
            }
        }

        const int Order = 6;
        double[,] numerator = Linear.Identity(n);
        double[,] denominator = Linear.Identity(n);
        double[,] power = Linear.Identity(n);
        double coefficient = 1;
        bool negative = true;
        for (int k = 1; k <= Order; k++)
        {
            coefficient = coefficient * (Order - k + 1) / (((2 * Order) - k + 1) * (double)k);
            power = Linear.Multiply(a, power);
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    double term = coefficient * power[r, c];
                    numerator[r, c] += term;
                    denominator[r, c] += negative ? -term : term;
                }
            }

            negative = !negative;
        }

        double[,] result = Linear.Solve(denominator, numerator);
        for (int i = 0; i < squarings; i++)
        {
            result = Linear.Multiply(result, result);
        }

        return result;
    }

    private static double[,] Rect(double[] columnMajor, int rows, int cols)
    {
        var rect = new double[rows, cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                rect[r, c] = columnMajor[((long)c * rows) + r];
            }
        }

        return rect;
    }

    [Theory]
    [InlineData(1, 0.3)]
    [InlineData(5, 0.2)]
    [InlineData(5, 3.0)]
    [InlineData(150, 0.004)]
    [InlineData(150, 0.05)]
    public void TheExponentialColumnMajorIsTheExponentialAsItWas(int n, double scale)
    {
        double[] a = Field(n, n);
        for (int i = 0; i < a.Length; i++)
        {
            a[i] *= scale;
        }

        double[,] expected = ExponentialAsItWas(Rect(a, n, n));
        Assert.Equal(expected, Rect(MatrixFunctions.Exponential(a, n), n, n));
        Assert.Equal(expected, MatrixFunctions.Exponential(Rect(a, n, n)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(60)]
    [InlineData(400)]
    public void TheColumnMajorHessenbergIsTheRectanglesReduction(int n)
    {
        if (!LinalgProvider.Current.IsNative)
        {
            return;
        }

        double[] a = Field(n, n);
        Hessenberg reference = Hessenberg.Reduce(Rect(a, n, n));

        double[] hOnly = (double[])a.Clone();
        Assert.True(Hessenberg.TryReduceColumnMajor(hOnly, n, wantQ: false, out double[]? none));
        Assert.Null(none);
        Assert.Equal(reference.H, Rect(hOnly, n, n));

        double[] both = (double[])a.Clone();
        Assert.True(Hessenberg.TryReduceColumnMajor(both, n, wantQ: true, out double[]? q));
        Assert.Equal(reference.H, Rect(both, n, n));
        Assert.Equal(reference.Q, Rect(q!, n, n));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(400)]
    public void TheColumnMajorSchurFormIsTheRectanglesForm(int n)
    {
        double[] a = Field(n, n);
        Schur reference = Schur.Factor(Rect(a, n, n));
        (double[] t, double[] u) = Schur.FactorColumnMajor((double[])a.Clone(), n);
        Assert.Equal(reference.T, Rect(t, n, n));
        Assert.Equal(reference.U, Rect(u, n, n));
    }

    // --- a long thin factorization's threads -------------------------------------------------------

    [Fact]
    public void ALongThinFactorizationIsSizedByItsFootprint()
    {
        bool before = NativeThreads.TallFactorThreads;
        try
        {
            NativeThreads.TallFactorThreads = true;
            Assert.Equal(1200, NativeThreads.FactorSize(1200, 1200));
            Assert.Equal(300, NativeThreads.FactorSize(600, 300));
            Assert.Equal(9, NativeThreads.FactorSize(400_000, 9));            // 3.6M elements: below the line
            Assert.Equal(4242, NativeThreads.FactorSize(2_000_000, 9));       // √18M
            Assert.Equal(2048, NativeThreads.FactorSize(2048, 2048));
            NativeThreads.TallFactorThreads = false;
            Assert.Equal(9, NativeThreads.FactorSize(2_000_000, 9));
        }
        finally
        {
            NativeThreads.TallFactorThreads = before;
        }
    }

    [Fact]
    public void ALongThinLeastSquaresSolveAnswersTheSameOnEitherSizing()
    {
        const int m = 500_000;
        const int terms = 9;
        var design = new double[(long)m * terms];
        var y = new double[m];
        for (int r = 0; r < m; r++)
        {
            double x = (r + 1) * 0.618033988749895;
            x -= Math.Floor(x);
            double power = 1;
            for (int c = terms - 1; c >= 0; c--)
            {
                design[((long)c * m) + r] = power;
                power *= x;
            }

            y[r] = (3 * x) - (x * x);
        }

        bool before = NativeThreads.TallFactorThreads;
        try
        {
            NativeThreads.TallFactorThreads = false;
            double[] narrow = QrDecomposition.Factor(design, m, terms, pivot: false)
                .SolveColumnMajor((double[])y.Clone(), 1);
            NativeThreads.TallFactorThreads = true;
            double[] wide = QrDecomposition.Factor(design, m, terms, pivot: false)
                .SolveColumnMajor((double[])y.Clone(), 1);
            Assert.Equal(narrow, wide);
        }
        finally
        {
            NativeThreads.TallFactorThreads = before;
        }
    }
}
