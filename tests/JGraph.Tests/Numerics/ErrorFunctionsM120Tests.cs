using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// The error functions after M120 moved them off the continued fraction and onto a rational.
/// </summary>
/// <remarks>
/// Every expected value here was read out of MATLAB R2024a rather than out of the implementation
/// this one replaced, which is the point: a fast road tested against the walk it replaced can only
/// ever prove the two agree, and says nothing about whether either is right. The tolerances are
/// relative and stated in units of the last place, because a solver's contract is relative and
/// xunit's decimal-place overload is not.
/// </remarks>
public class ErrorFunctionsM120Tests
{
    /// <summary>One unit in the last place of a double.</summary>
    private const double Ulp = 2.220446049250313e-16;

    private static void CloseTo(double expected, double actual, double ulps, string what)
    {
        double error = Math.Abs(actual - expected) / Math.Abs(expected);
        Assert.True(
            error <= ulps * Ulp,
            $"{what}: expected {expected:R}, got {actual:R} — {error / Ulp:F2} ulp, allowed {ulps}");
    }

    [Theory]
    // Cody's three intervals meet at 0.46875 and at 4, so each boundary is visited from both sides.
    [InlineData(0.1, 0.1124629160182849, 0.88753708398171516)]
    [InlineData(0.46875, 0.49261347321793797, 0.50738652678206198)]
    [InlineData(0.5, 0.52049987781304652, 0.47950012218695348)]
    [InlineData(1.0, 0.84270079294971489, 0.15729920705028513)]
    [InlineData(2.0, 0.99532226501895271, 0.0046777349810472662)]
    [InlineData(3.9999, 0.9999999845700388, 1.5429961215558176e-08)]
    [InlineData(4.0, 0.99999998458274209, 1.541725790028002e-08)]
    [InlineData(4.0001, 0.99999998459543527, 1.5404564743590145e-08)]
    [InlineData(10.0, 1.0, 2.0884875837625449e-45)]
    [InlineData(26.0, 1.0, 5.6631924088561432e-296)]
    public void TheForwardFunctionsAnswerWhatMatlabAnswers(double x, double erf, double erfc)
    {
        CloseTo(erf, ErrorFunctions.Erf(x), 4, $"erf({x})");
        CloseTo(erfc, ErrorFunctions.Erfc(x), 4, $"erfc({x})");
        CloseTo(-erf, ErrorFunctions.Erf(-x), 4, $"erf({-x})");
        CloseTo(2.0 - erfc, ErrorFunctions.Erfc(-x), 4, $"erfc({-x})");
    }

    [Theory]
    [InlineData(0.1, 0.89645697996912654)]
    [InlineData(0.46875, 0.63206968924955598)]
    [InlineData(1.0, 0.427583576155807)]
    [InlineData(4.0, 0.13699945762506138)]
    [InlineData(10.0, 0.056140992743822594)]
    [InlineData(27.0, 0.02088160799042094)]
    public void TheScaledComplementIsRightWhereTheUnscaledOneHasNothingLeft(double x, double expected)
    {
        // erfcx exists for arguments where erfc itself has run out of exponent: at 27 the plain
        // complement is a subnormal carrying a handful of bits and this still carries all of them.
        CloseTo(expected, ErrorFunctions.ErfcScaled(x), 6, $"erfcx({x})");
    }

    [Fact]
    public void ErfcReachesTheSubnormalsRatherThanStoppingAtTheNormals()
    {
        // Cody's own limit stops at 26.543, where erfc leaves the normal doubles. That was right on
        // a machine that flushed the rest to zero, and MATLAB on this one answers erfc(27).
        CloseTo(5.2370464393526292e-319, ErrorFunctions.Erfc(27.0), 8, "erfc(27)");
        Assert.True(ErrorFunctions.Erfc(27.2) > 0, "erfc(27.2) should still be a subnormal, not zero");
        Assert.Equal(0.0, ErrorFunctions.Erfc(30.0));
        Assert.Equal(0.0, ErrorFunctions.Erfc(double.MaxValue));
        Assert.Equal(2.0, ErrorFunctions.Erfc(-30.0));
    }

    [Theory]
    [InlineData(0.1, 0.088855990494257686)]
    [InlineData(0.5, 0.47693627620446988)]
    // The two arms meet at 0.9, so it is asked for from both sides of the join.
    [InlineData(0.9, 1.1630871536766743)]
    [InlineData(0.9000001, 1.1630874964810918)]
    [InlineData(0.99, 1.8213863677184494)]
    [InlineData(0.999999, 3.4589107372754988)]
    public void ErfInverseAnswersWhatMatlabAnswers(double p, double expected)
    {
        CloseTo(expected, ErrorFunctions.ErfInverse(p), 8, $"erfinv({p})");
        CloseTo(-expected, ErrorFunctions.ErfInverse(-p), 8, $"erfinv({-p})");
    }

    [Fact]
    public void TheLargestArgumentShortOfOneIsStillAnswered()
    {
        // 1 - eps/2 is the last double below 1, and 1 - it is exact, so the tail arm has a real
        // number to invert rather than the zero a naive subtraction would hand it.
        CloseTo(5.8635847487551676, ErrorFunctions.ErfInverse(1.0 - (Ulp / 2)), 8, "erfinv(1-eps/2)");
        Assert.Equal(double.PositiveInfinity, ErrorFunctions.ErfInverse(1.0));
        Assert.Equal(double.NegativeInfinity, ErrorFunctions.ErfInverse(-1.0));
        Assert.Equal(0.0, ErrorFunctions.ErfInverse(0.0));
        Assert.True(double.IsNaN(ErrorFunctions.ErfInverse(1.5)));
    }

    [Theory]
    [InlineData(1e-300, 26.209469960516124)]
    [InlineData(1e-100, 15.065574702592645)]
    [InlineData(1e-16, 5.8723700904539635)]
    [InlineData(0.001, 2.3267537655135246)]
    [InlineData(0.0999999, 1.163087496481092)]
    [InlineData(0.1, 1.163087153676674)]
    [InlineData(0.1000001, 1.1630868108725296)]
    [InlineData(1.9, -1.1630871536766738)]
    public void ErfcInverseAnswersWhatMatlabAnswers(double q, double expected)
    {
        // 1e-300 is the whole point of refining on the logarithm: erfc(26.2) cannot be formed at
        // all as a plain double, and the residual -y² + ln erfcx(y) - ln q never tries to.
        CloseTo(expected, ErrorFunctions.ErfcInverse(q), 8, $"erfcinv({q})");
    }

    [Fact]
    public void ErfcInverseIsOddAboutTheMiddleOfItsRange()
    {
        Assert.Equal(0.0, ErrorFunctions.ErfcInverse(1.0));

        // The upper argument is the one iterated over, because 2 - u for u in (1, 2) is exact —
        // both operands lie within a factor of two, so the subtraction cannot round. Walking the
        // lower half instead and reflecting it up would compare two different questions.
        for (double upper = 1.05; upper < 2.0; upper += 0.05)
        {
            double lower = 2.0 - upper;
            CloseTo(
                -ErrorFunctions.ErfcInverse(lower), ErrorFunctions.ErfcInverse(upper), 8,
                $"erfcinv({upper}) against erfcinv({lower})");
        }

        Assert.Equal(double.PositiveInfinity, ErrorFunctions.ErfcInverse(0.0));
        Assert.Equal(double.NegativeInfinity, ErrorFunctions.ErfcInverse(2.0));
        Assert.True(double.IsNaN(ErrorFunctions.ErfcInverse(2.5)));
    }

    [Fact]
    public void TheInverseIsTheInverseOfThisLibrarysOwnErf()
    {
        // The refinement calls Erf, so this is not a coincidence of two tables agreeing — it is the
        // property the design has, and the one a change to either would break first.
        double worst = 0;
        double at = 0;
        for (int i = 1; i < 20000; i++)
        {
            double p = -0.9999 + (1.9998 * i / 20000);
            double back = ErrorFunctions.Erf(ErrorFunctions.ErfInverse(p));
            double error = Math.Abs(back - p) / Math.Abs(p);
            if (error > worst)
            {
                (worst, at) = (error, p);
            }
        }

        Assert.True(worst <= 8 * Ulp, $"erf(erfinv(p)) drifted {worst / Ulp:F2} ulp at p = {at:R}");
    }

    [Fact]
    public void TheComplementaryInverseUndoesTheComplementDownTheWholeTail()
    {
        for (int power = 1; power <= 300; power++)
        {
            double q = Math.Pow(10, -power);
            double y = ErrorFunctions.ErfcInverse(q);

            // How far a round trip may drift is not a free choice: erfc's own relative sensitivity
            // to y is 2y/(√π·erfcx(y)), which is about 2y² down the tail. At q = 1e-300 that is
            // thirteen hundred, so demanding a few ulp back would be demanding that y be right to
            // a thousandth of one — a test of arithmetic nobody has.
            double condition = (2 * y / (Math.Sqrt(Math.PI) * ErrorFunctions.ErfcScaled(y))) + 4;

            // Below 1e-308 erfc(y) is a subnormal or zero and cannot say whether y is right; the
            // scaled complement can, and it is the same y either way.
            double back = ErrorFunctions.ErfcScaled(y) * Math.Exp(-y * y);
            if (back == 0 || double.IsSubnormal(back))
            {
                double target = q * Math.Exp(y * y);
                if (double.IsInfinity(target))
                {
                    continue; // nothing representable is left to compare
                }

                CloseTo(target, ErrorFunctions.ErfcScaled(y), condition, $"erfcx at erfcinv(1e-{power})");
                continue;
            }

            CloseTo(q, back, condition, $"erfc(erfcinv(1e-{power}))");
        }
    }

    [Fact]
    public void BothFunctionsClimbWithoutADipWhereTheApproximationsChangeOver()
    {
        // A rational fitted per interval can meet its neighbour a fraction of an ulp out of step,
        // and a monotone function that steps backwards is how that shows up in a caller.
        static void Climbs(Func<double, double> f, double from, double to, string what)
        {
            double previous = f(from);
            for (int i = 1; i <= 40000; i++)
            {
                double x = from + ((to - from) * i / 40000);
                double now = f(x);
                Assert.True(now >= previous, $"{what} stepped back at x = {x:R}: {previous:R} then {now:R}");
                previous = now;
            }
        }

        Climbs(ErrorFunctions.Erf, -6, 6, "erf");
        Climbs(x => -ErrorFunctions.Erfc(x), 0, 8, "erfc");
        Climbs(ErrorFunctions.ErfInverse, -0.99999, 0.99999, "erfinv");
        Climbs(x => -ErrorFunctions.ErfcInverse(x), 1e-6, 1.999, "erfcinv");
    }

    [Fact]
    public void NotANumberStaysNotANumber()
    {
        Assert.True(double.IsNaN(ErrorFunctions.Erf(double.NaN)));
        Assert.True(double.IsNaN(ErrorFunctions.Erfc(double.NaN)));
        Assert.True(double.IsNaN(ErrorFunctions.ErfcScaled(double.NaN)));
        Assert.True(double.IsNaN(ErrorFunctions.ErfInverse(double.NaN)));
        Assert.True(double.IsNaN(ErrorFunctions.ErfcInverse(double.NaN)));

        Assert.Equal(1.0, ErrorFunctions.Erf(double.PositiveInfinity));
        Assert.Equal(-1.0, ErrorFunctions.Erf(double.NegativeInfinity));
        Assert.Equal(0.0, ErrorFunctions.ErfcScaled(double.PositiveInfinity));
        Assert.Equal(double.PositiveInfinity, ErrorFunctions.ErfcScaled(double.NegativeInfinity));
    }
}
