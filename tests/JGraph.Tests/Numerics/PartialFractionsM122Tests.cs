using System.Numerics;
using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// The partial-fraction expansion behind <c>residue</c> (M122).
/// </summary>
/// <remarks>
/// The expansion is checked against the thing it means rather than against a table of digits: an
/// expansion is right when putting it back together gives the polynomial it came from, and that is a
/// check the code cannot pass by reproducing a mistake it also made on the way out. The reference
/// values that do appear are R2024a's own.
/// </remarks>
public class PartialFractionsM122Tests
{
    private static Complex[] Of(params double[] coefficients) =>
        Array.ConvertAll(coefficients, c => new Complex(c, 0));

    /// <summary>The value of a polynomial, highest power first.</summary>
    private static Complex At(Complex[] p, Complex s)
    {
        Complex value = Complex.Zero;
        foreach (Complex c in p)
        {
            value = (value * s) + c;
        }

        return value;
    }

    /// <summary>
    /// The expansion read back as a function, so it can be compared with the ratio it expands. This is
    /// the check that matters: it never looks at how the answer is laid out, only at what it means.
    /// </summary>
    private static Complex Sum(PartialFractions.Expansion e, Complex s)
    {
        Complex total = At(e.Direct, s);
        int at = 0;
        while (at < e.Poles.Length)
        {
            Complex pole = e.Poles[at];
            int power = 1;
            while (at > 0 && Complex.Abs(e.Poles[at - 1] - pole) < 1e-12 && power <= at)
            {
                // Count how many equal poles came before this one; that is this term's power.
                power = 1;
                for (int back = at - 1; back >= 0 && Complex.Abs(e.Poles[back] - pole) < 1e-12; back--)
                {
                    power++;
                }

                break;
            }

            total += e.Residues[at] / Complex.Pow(s - pole, power);
            at++;
        }

        return total;
    }

    [Fact]
    public void ASimpleExpansionMatchesMatlabsOwnAnswer()
    {
        PartialFractions.Expansion e = PartialFractions.Expand(Of(1, 1), Of(1, 3, 2));

        Assert.Equal(2, e.Poles.Length);
        Assert.Equal(-2, e.Poles[0].Real, 12);
        Assert.Equal(-1, e.Poles[1].Real, 12);
        Assert.Equal(1, e.Residues[0].Real, 12);
        Assert.Equal(0, e.Residues[1].Real, 12);
        Assert.Empty(e.Direct);
    }

    /// <summary>
    /// The pole order is <c>roots</c>'s, which is what makes the two functions agree about a
    /// polynomial with nothing repeated in it.
    /// </summary>
    [Fact]
    public void ThePolesComeBackInTheOrderRootsGivesThem()
    {
        Complex[] a = Of(1, 6, 11, 6);
        PartialFractions.Expansion e = PartialFractions.Expand(Of(2, 5, 3, 6), a);
        Complex[] roots = Polynomials.Roots(a);

        for (int i = 0; i < roots.Length; i++)
        {
            Assert.Equal(roots[i].Real, e.Poles[i].Real, 8);
        }

        Assert.Single(e.Direct);
        Assert.Equal(2, e.Direct[0].Real, 12);
    }

    /// <summary>
    /// A repeated pole reads in ascending power, which is the layout MATLAB documents:
    /// <c>r(j)/(s-p) + r(j+1)/(s-p)^2</c>. Getting this backwards is invisible in the round trip,
    /// because the round trip would put it back the same way it took it out.
    /// </summary>
    [Fact]
    public void ARepeatedPolesResiduesRunInAscendingPower()
    {
        // s^2 / ((s+3)(s+1)^2) — MATLAB answers r = [2.25; -1.25; 0.5], p = [-3; -1; -1].
        PartialFractions.Expansion e = PartialFractions.Expand(
            Of(1, 0, 0), Of(1, 5, 7, 3));

        Assert.Equal(3, e.Poles.Length);
        Assert.Equal(-3, e.Poles[0].Real, 6);
        Assert.Equal(-1, e.Poles[1].Real, 6);
        Assert.Equal(-1, e.Poles[2].Real, 6);
        Assert.Equal(2.25, e.Residues[0].Real, 6);
        Assert.Equal(-1.25, e.Residues[1].Real, 6);
        Assert.Equal(0.5, e.Residues[2].Real, 6);
    }

    /// <summary>
    /// The reason near-equal poles are moved together rather than read as two simple ones. A double
    /// root comes back from any eigenvalue solver as a conjugate pair about 1e-8 off the axis, and
    /// two simple poles that close have residues about 1e8 in size that cancel.
    /// </summary>
    [Fact]
    public void ADoubleRootIsOnePoleAndNotTwoNearlyEqualOnes()
    {
        PartialFractions.Expansion e = PartialFractions.Expand(Of(1), Of(1, 4, 5, 2));

        Assert.Equal(3, e.Poles.Length);
        Assert.Equal(e.Poles[1].Real, e.Poles[2].Real, 15);
        Assert.Equal(0, e.Poles[1].Imaginary, 15);
        foreach (Complex residue in e.Residues)
        {
            Assert.True(Complex.Abs(residue) < 10, $"a residue of {residue} is the unmerged-pole failure");
        }
    }

    [Theory]

    // b, then a, as flat coefficient runs. Every one is checked by evaluating the expansion.
    [InlineData(new double[] { 1, 1 }, new double[] { 1, 3, 2 })]
    [InlineData(new double[] { 2, 5, 3, 6 }, new double[] { 1, 6, 11, 6 })]
    [InlineData(new double[] { 1, 0, 0 }, new double[] { 1, 5, 7, 3 })]
    [InlineData(new double[] { 1 }, new double[] { 1, 0, 1 })]
    [InlineData(new double[] { 1, 0 }, new double[] { 1, 5, 1, 5 })]
    [InlineData(new double[] { 1, 0, 0, 0, 0 }, new double[] { 1, 3, 2 })]
    [InlineData(new double[] { 5 }, new double[] { 1, 1 })]
    [InlineData(new double[] { 1, 2 }, new double[] { 1, 3 })]
    [InlineData(new double[] { 3, 1, 4, 1, 5 }, new double[] { 1, 2, 3, 4 })]
    public void TheExpansionMeansTheRatioItExpands(double[] b, double[] a)
    {
        PartialFractions.Expansion e = PartialFractions.Expand(Of(b), Of(a));
        Complex[] top = Of(b);
        Complex[] bottom = Of(a);

        // Away from every pole, and off the real axis so a real pole cannot be landed on.
        foreach (Complex s in new[]
        {
            new Complex(0.37, 0.91), new Complex(-4.2, 2.3), new Complex(7.5, -1.1),
        })
        {
            Complex expected = At(top, s) / At(bottom, s);
            Complex actual = Sum(e, s);
            Assert.True(
                Complex.Abs(actual - expected) <= 1e-8 * Math.Max(1, Complex.Abs(expected)),
                $"at {s}: expansion {actual} against ratio {expected}");
        }
    }

    [Theory]
    [InlineData(new double[] { 1, 1 }, new double[] { 1, 3, 2 })]
    [InlineData(new double[] { 2, 5, 3, 6 }, new double[] { 1, 6, 11, 6 })]
    [InlineData(new double[] { 1, 0, 0 }, new double[] { 1, 5, 7, 3 })]
    [InlineData(new double[] { 1, 0 }, new double[] { 1, 5, 1, 5 })]
    public void PuttingTheExpansionBackTogetherGivesTheRatioItCameFrom(double[] b, double[] a)
    {
        PartialFractions.Expansion e = PartialFractions.Expand(Of(b), Of(a));
        (Complex[] numerator, Complex[] denominator) =
            PartialFractions.Combine(e.Residues, e.Poles, e.Direct);

        // The denominator comes back monic, so both sides are scaled by the original's lead before
        // they are compared — the ratio is what round-trips, not either half on its own.
        Complex lead = Of(a)[0];
        for (int i = 0; i < denominator.Length; i++)
        {
            Assert.Equal(Of(a)[i].Real, (denominator[i] * lead).Real, 6);
        }

        Complex[] expected = Of(b);
        int offset = numerator.Length - expected.Length;
        for (int i = 0; i < numerator.Length; i++)
        {
            double want = i >= offset && offset >= 0 ? expected[i - offset].Real / lead.Real : 0;
            Assert.Equal(want, numerator[i].Real, 6);
        }
    }

    /// <summary>
    /// A zero numerator has a residue of zero at every pole rather than no residue at all, which is
    /// what MATLAB answers for <c>residue([], [1 1])</c>.
    /// </summary>
    [Fact]
    public void AnEmptyNumeratorIsTheZeroPolynomial()
    {
        PartialFractions.Expansion e = PartialFractions.Expand([], Of(1, 1));

        Assert.Single(e.Residues);
        Assert.Equal(0, e.Residues[0].Real, 12);
        Assert.Equal(-1, e.Poles[0].Real, 12);
    }

    /// <summary>Leading zeros are not part of a polynomial, and MATLAB drops them before it looks.</summary>
    [Fact]
    public void LeadingZerosAreNotPartOfThePolynomial()
    {
        PartialFractions.Expansion e = PartialFractions.Expand(Of(1), Of(0, 1, 3, 2));

        Assert.Equal(2, e.Poles.Length);
        Assert.Equal(-2, e.Poles[0].Real, 12);
        Assert.Equal(-1, e.Residues[0].Real, 12);
        Assert.Equal(1, e.Residues[1].Real, 12);
    }

    /// <summary>A constant denominator is all polynomial part and no poles.</summary>
    [Fact]
    public void AConstantDenominatorIsAllDirectTerm()
    {
        PartialFractions.Expansion e = PartialFractions.Expand(Of(1, 2, 3), Of(2));

        Assert.Empty(e.Poles);
        Assert.Empty(e.Residues);
        Assert.Equal(3, e.Direct.Length);
        Assert.Equal(0.5, e.Direct[0].Real, 12);
        Assert.Equal(1.5, e.Direct[2].Real, 12);
    }

    [Fact]
    public void AZeroDenominatorIsRefused() =>
        Assert.Throws<ArgumentException>(() => PartialFractions.Expand(Of(1), Of(0, 0)));

    [Fact]
    public void ResiduesAndPolesMustPairUp() =>
        Assert.Throws<ArgumentException>(() => PartialFractions.Combine(Of(1, 2), Of(1), []));
}
