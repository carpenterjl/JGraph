using System.Numerics;
using JGraph.Numerics.LinearAlgebra;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// <c>s ^ A</c> at the kernel, without the interpreter above it (M142). Every assertion here is a
/// property of the matrix function rather than a stored answer: a Jordan block's value is written
/// out from the derivatives it must carry, and everything else is held against the matrix
/// exponential, which reaches the same number by scaling and squaring instead of Schur-Parlett.
/// </summary>
public class ScalarMatrixPowerM142Tests
{
    /// <summary>
    /// The domain rule the operator passes in: a negative base with a whole exponent stays real,
    /// and everything else that leaves the reals answers a complex number.
    /// </summary>
    private static Complex Power(Complex a, Complex b) =>
        a.Imaginary == 0 && b.Imaginary == 0 && (a.Real >= 0 || b.Real == Math.Floor(b.Real))
            ? new Complex(Math.Pow(a.Real, b.Real), 0)
            : Complex.Pow(a, b);

    private static Complex[,] Widen(double[,] a)
    {
        var wide = new Complex[a.GetLength(0), a.GetLength(1)];
        for (int r = 0; r < a.GetLength(0); r++)
        {
            for (int c = 0; c < a.GetLength(1); c++)
            {
                wide[r, c] = new Complex(a[r, c], 0.0);
            }
        }

        return wide;
    }

    private static double Distance(Complex[,] f, Complex[,] g)
    {
        double sum = 0.0;
        for (int r = 0; r < f.GetLength(0); r++)
        {
            for (int c = 0; c < f.GetLength(1); c++)
            {
                sum += Complex.Abs(f[r, c] - g[r, c]) * Complex.Abs(f[r, c] - g[r, c]);
            }
        }

        return Math.Sqrt(sum);
    }

    /// <summary>A Jordan block of order <paramref name="n"/> at <paramref name="value"/>.</summary>
    private static double[,] Block(int n, double value)
    {
        var a = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            a[i, i] = value;
            if (i + 1 < n)
            {
                a[i, i + 1] = 1.0;
            }
        }

        return a;
    }

    [Theory]

    // The definition itself. f(J) for a Jordan block carries f(λ), f'(λ), f''(λ)/2! ... along the
    // superdiagonals, and for f(x) = s^x the k-th derivative is (ln s)^k · s^x. An implementation
    // built on eig cannot produce anything but the diagonal here, because a block of order n has
    // one eigenvector and not n of them.
    [InlineData(2.0, 2, 2.0)]
    [InlineData(2.0, 3, 3.0)]
    [InlineData(3.0, 4, 1.5)]
    [InlineData(0.5, 3, 2.0)]
    [InlineData(2.0, 6, 2.0)]
    [InlineData(1e10, 2, 2.0)]
    [InlineData(2.0, 5, 0.0)]
    [InlineData(0.1, 3, 3.0)]
    public void AJordanBlockCarriesTheDerivativesOfTheBase(double scalar, int order, double value)
    {
        Complex[,] f = MatrixFunction.ScalarPower(scalar, Widen(Block(order, value)), Power);

        double logarithm = Math.Log(scalar);
        double raised = Math.Pow(scalar, value);
        var expected = new Complex[order, order];
        double term = raised;
        for (int k = 0; k < order; k++)
        {
            // f⁽ᵏ⁾(λ) / k!, which is raised · (ln s)^k / k! built up one factor at a time.
            if (k > 0)
            {
                term = term * logarithm / k;
            }

            for (int i = 0; i + k < order; i++)
            {
                expected[i, i + k] = new Complex(term, 0.0);
            }
        }

        double scale = Math.Max(1.0, Distance(expected, new Complex[order, order]));
        Assert.True(Distance(f, expected) <= 1e-13 * scale, $"{Distance(f, expected)}");
    }

    [Theory]

    // s^A is expm(log(s) · A) for a positive base, and the matrix exponential here is an
    // independent road to it: a Padé approximant with scaling and squaring, which never
    // triangularizes and never asks what the eigenvalues are.
    [InlineData(2.0)]
    [InlineData(0.5)]
    [InlineData(7.0)]
    [InlineData(1.0)]
    public void APositiveBaseAgreesWithTheMatrixExponential(double scalar)
    {
        double[][,] matrices =
        [
            new double[,] { { 1, 2 }, { 3, 4 } },
            new double[,] { { 2, 1 }, { 0, 2 } },
            new double[,] { { 0, -1 }, { 1, 0 } },
            new double[,] { { 1, 1, 0 }, { 0, 1, 1 }, { 0, 0, 2 } },
            new double[,] { { 0.5, 0.25, 0 }, { 0.25, 0.5, 0.25 }, { 0, 0.25, 0.5 } },
            new double[,] { { 1, 1 }, { -1, 3 } },
        ];

        foreach (double[,] a in matrices)
        {
            int n = a.GetLength(0);
            var scaled = new double[n, n];
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    scaled[r, c] = Math.Log(scalar) * a[r, c];
                }
            }

            Complex[,] mine = MatrixFunction.ScalarPower(scalar, Widen(a), Power);
            Complex[,] theirs = Widen(MatrixFunctions.Exponential(scaled));
            double scale = Math.Max(1.0, Distance(theirs, new Complex[n, n]));
            Assert.True(Distance(mine, theirs) <= 1e-12 * scale, $"{Distance(mine, theirs)}");
        }
    }

    /// <summary>
    /// A positive base carries the reals to the reals, so a real exponent has an exactly real
    /// answer even when its eigenvalues are a conjugate pair and the whole evaluation ran in
    /// complex arithmetic.
    /// </summary>
    [Fact]
    public void APositiveBaseOverARealMatrixHasNoImaginaryPart()
    {
        Complex[,] f = MatrixFunction.ScalarPower(
            2.0, Widen(new double[,] { { 0, -1 }, { 1, 0 } }), Power);

        foreach (Complex value in f)
        {
            Assert.Equal(0.0, value.Imaginary);
        }
    }

    /// <summary>
    /// A negative base does not, and the imaginary part it leaves is the iπ in its principal
    /// logarithm times the derivative — so a diagonal exponent of whole numbers stays real and a
    /// defective one does not.
    /// </summary>
    [Fact]
    public void ANegativeBaseIsRealOnlyWhereThePowersAre()
    {
        Complex[,] diagonal = MatrixFunction.ScalarPower(
            -2.0, Widen(new double[,] { { 2, 0 }, { 0, 4 } }), Power);
        Assert.Equal(new Complex(4, 0), diagonal[0, 0]);
        Assert.Equal(new Complex(16, 0), diagonal[1, 1]);

        Complex[,] defective = MatrixFunction.ScalarPower(-2.0, Widen(Block(2, 2.0)), Power);
        Assert.Equal(new Complex(4, 0), defective[0, 0]);
        Assert.Equal(4 * Math.PI, defective[0, 1].Imaginary, 12);
        Assert.Equal(4 * Math.Log(2), defective[0, 1].Real, 12);
    }

    /// <summary>An exponent with no rows has no answer to give, and says so by staying empty.</summary>
    [Fact]
    public void AnEmptyExponentAnswersEmpty() =>
        Assert.Empty(MatrixFunction.ScalarPower(2.0, new Complex[0, 0], Power));

    /// <summary>A rectangular exponent is not a matrix function's argument and is refused.</summary>
    [Fact]
    public void ARectangularExponentIsRefused() => Assert.Throws<ArgumentException>(
        () => MatrixFunction.ScalarPower(2.0, new Complex[2, 3], Power));
}
