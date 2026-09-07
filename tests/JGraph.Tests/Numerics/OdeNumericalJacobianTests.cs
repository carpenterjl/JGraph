using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// The numerically differenced Jacobian the stiff solvers form (M126), against the values MATLAB's
/// <c>numjac</c> answers for the same sequence of calls.
/// </summary>
/// <remarks>
/// The Jacobian's <em>values</em> are not the interesting part — a forward difference is a forward
/// difference. What is pinned here is Salane's adaptive increment: the working storage carried from
/// call to call, which decides both how large each perturbation is and whether a column that came
/// out at the level of rounding is worth one more evaluation. Two implementations that difference
/// the same function with different increments answer Jacobians that agree to six figures and step
/// counts that do not agree at all.
/// </remarks>
public class OdeNumericalJacobianTests
{
    /// <summary>
    /// Robertson's chemical kinetics, the problem the reference values were taken from. The
    /// parenthesization is the reference script's, because a difference quotient of a squared
    /// quantity is decided in the last bits and <c>3e7·(y²)</c> is not <c>(3e7·y)·y</c>.
    /// </summary>
    private static double[] Robertson(double[] y) =>
    [
        (-0.04 * y[0]) + (1e4 * y[1] * y[2]),
        (0.04 * y[0]) - (1e4 * y[1] * y[2]) - (3e7 * (y[1] * y[1])),
        3e7 * (y[1] * y[1]),
    ];

    [Fact]
    public void TheIncrementsFollowMatlabsAcrossASequenceOfCalls()
    {
        var options = new OdeJacobianOptions { Threshold = [1e-8, 1e-14, 1e-6] };
        double[] y = [1, 0, 0];
        var lines = new List<string>();
        for (int step = 1; step <= 6; step++)
        {
            double[] f0 = Robertson(y);
            double[,] jacobian = OdeNumericalJacobian.Compute(Robertson, null, y, f0, options, out _);
            lines.Add(string.Join(' ', options.Increments!.Select(v => v.ToString("G17"))));
            lines.Add($"{jacobian[1, 1]:G17} {jacobian[2, 1]:G17}");
            y = [y[0] - (1e-6 * step), y[1] + (2e-7 * step), y[2] + (8e-7 * step)];
        }

        Assert.Equal(
            [
                "1.4901161193847656E-08 1.4901161193847656E-08 0.001220703125",
                "0 4.4703483581542967E-15",
                "1.4901161193847656E-08 1.4901161193847656E-07 0.001220703125",
                "-12.007076259558454 12.000000177635684",
                "1.4901161193847656E-08 1.4901161193847656E-07 0.001220703125",
                "-36.02410046325847 36.000002662166786",
                "1.4901161193847656E-08 1.4901161193847656E-07 0.001220703125",
                "-72.048006900980525 72.000005324333571",
                "1.4901161193847656E-08 1.4901161193847656E-07 0.001220703125",
                "-120.08000627073859 120.00000889031071",
                "1.4901161193847656E-08 1.4901161193847656E-07 0.001220703125",
                "-180.12002104764008 180.00001322177923",
            ],
            lines);
    }

    [Fact]
    public void ColumnsThatShareNoRowAreGroupedTheWayMatlabGroupsThem()
    {
        // A tridiagonal pattern: columns three apart share no row, so three evaluations suffice
        // however many equations there are.
        const int N = 12;
        var pattern = new bool[N, N];
        for (int i = 0; i < N; i++)
        {
            for (int j = System.Math.Max(0, i - 1); j <= System.Math.Min(N - 1, i + 1); j++)
            {
                pattern[i, j] = true;
            }
        }

        int[] groups = OdeNumericalJacobian.ColumnGroups(pattern);
        Assert.Equal(3, groups.Max());
        for (int j = 0; j < N; j++)
        {
            Assert.Equal((j % 3) + 1, groups[j]);
        }
    }

    [Fact]
    public void AColumnOfZerosIsNotGroupedAtAll()
    {
        var pattern = new bool[3, 3];
        pattern[0, 0] = true;
        pattern[2, 2] = true;
        int[] groups = OdeNumericalJacobian.ColumnGroups(pattern);
        Assert.Equal([1, 0, 1], groups);
    }
}
