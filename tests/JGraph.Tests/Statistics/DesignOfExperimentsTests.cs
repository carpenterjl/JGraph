using JGraph.Statistics;
using Xunit;

namespace JGraph.Tests.Statistics;

/// <summary>
/// M53 wave J: the enumerable designs, and the two answers a measured process gives about itself.
/// </summary>
/// <remarks>
/// A design is checked by what it must be rather than by a printed copy of one: a full factorial holds
/// every combination exactly once, a two-level fraction has orthogonal columns, a Box-Behnken design
/// never puts more than its block's worth of factors away from centre at a time, and a rotatable
/// central composite has every star point at the same distance as its corners.
/// </remarks>
public class DesignOfExperimentsTests
{
    [Fact]
    public void AFullFactorialHoldsEveryCombinationOnce()
    {
        double[,] design = DesignOfExperiments.FullFactorial([2, 3, 2]);
        Assert.Equal(12, design.GetLength(0));
        Assert.Equal(3, design.GetLength(1));

        var seen = new HashSet<string>();
        for (int r = 0; r < 12; r++)
        {
            seen.Add($"{design[r, 0]},{design[r, 1]},{design[r, 2]}");
        }

        Assert.Equal(12, seen.Count);

        // The first factor varies fastest, which is the difference between this and ff2n.
        Assert.Equal(1, design[0, 0]);
        Assert.Equal(2, design[1, 0]);
        Assert.Equal(1, design[1, 1]);
    }

    [Fact]
    public void TheTwoLevelFactorialVariesItsLastFactorFastest()
    {
        double[,] design = DesignOfExperiments.TwoLevelFullFactorial(3);
        Assert.Equal(8, design.GetLength(0));
        Assert.Equal(0, design[1, 0]);
        Assert.Equal(0, design[1, 1]);
        Assert.Equal(1, design[1, 2]);
        Assert.Equal(1, design[7, 0]);
    }

    [Fact]
    public void AFractionsColumnsAreOrthogonal()
    {
        DesignOfExperiments.Generator[] generators =
        [
            new("a", [0]), new("b", [1]), new("c", [2]), new("abc", [0, 1, 2]),
        ];

        double[,] design = DesignOfExperiments.Fraction(generators);
        Assert.Equal(8, design.GetLength(0));
        Assert.Equal(4, design.GetLength(1));

        for (int i = 0; i < 4; i++)
        {
            double balance = 0;
            for (int r = 0; r < 8; r++)
            {
                Assert.True(design[r, i] is 1 or -1);
                balance += design[r, i];
            }

            Assert.Equal(0, balance, 12);

            for (int j = i + 1; j < 4; j++)
            {
                double inner = 0;
                for (int r = 0; r < 8; r++)
                {
                    inner += design[r, i] * design[r, j];
                }

                Assert.Equal(0, inner, 12);
            }
        }
    }

    [Fact]
    public void TheConfoundingGroupsAreTheEffectsThatShareAColumn()
    {
        DesignOfExperiments.Generator[] generators =
        [
            new("a", [0]), new("b", [1]), new("c", [2]), new("abc", [0, 1, 2]),
        ];

        double[,] design = DesignOfExperiments.Fraction(generators);
        IReadOnlyList<int[][]> groups = DesignOfExperiments.Confounding(design, 2);

        // Four factors and their six two-way interactions, in eight columns: three of the two-way
        // interactions must pair up with the other three, which is what resolution IV means.
        int paired = 0;
        foreach (int[][] group in groups)
        {
            if (group.Length > 1)
            {
                paired++;
            }
        }

        Assert.Equal(3, paired);

        // Every main effect stands alone at this order.
        foreach (int[][] group in groups)
        {
            foreach (int[] term in group)
            {
                if (term.Length == 1)
                {
                    Assert.Single(group);
                }
            }
        }
    }

    [Fact]
    public void TheGeneratorSearchFindsAResolutionFourDesignWhenOneExists()
    {
        IReadOnlyList<string> found = DesignOfExperiments.FractionGenerators(4, 3, 4);
        Assert.Equal(4, found.Count);
        Assert.Equal(["a", "b", "c"], found.Take(3));
        Assert.Equal("abc", found[3]);

        // Five factors in eight runs cannot reach resolution four, and the search says so by finding
        // nothing rather than by answering with something worse.
        Assert.Empty(DesignOfExperiments.FractionGenerators(5, 3, 4));
        Assert.Equal(5, DesignOfExperiments.FractionGenerators(5, 3, 3).Count);
    }

    [Theory]
    [InlineData(3, 15)]
    [InlineData(4, 27)]
    [InlineData(5, 46)]
    [InlineData(6, 54)]
    [InlineData(7, 62)]
    public void TheBoxBehnkenDesignsHaveTheirPublishedRunCounts(int factors, int runs)
    {
        double[,] design = DesignOfExperiments.BoxBehnken(
            factors, DesignOfExperiments.BehnkenCentrePoints(factors));
        Assert.Equal(runs, design.GetLength(0));
        Assert.Equal(factors, design.GetLength(1));

        // Every run sits on the cube's edge midpoints rather than its corners: at most three factors
        // are away from centre at once, and no factor ever leaves [-1, 1].
        for (int r = 0; r < design.GetLength(0); r++)
        {
            int away = 0;
            for (int c = 0; c < factors; c++)
            {
                Assert.InRange(design[r, c], -1, 1);
                if (design[r, c] != 0)
                {
                    away++;
                }
            }

            Assert.True(away <= 3);
        }
    }

    [Fact]
    public void ACircumscribedCompositeDesignIsRotatable()
    {
        double[,] design = DesignOfExperiments.CentralComposite(
            3, 0, DesignOfExperiments.CompositeKind.Circumscribed, 6);

        Assert.Equal(8 + 6 + 6, design.GetLength(0));

        // A corner of the cube and a star point are the same distance from the centre, which is the
        // whole content of the word rotatable.
        double corner = Math.Sqrt(3);
        double star = Math.Pow(8, 0.25);
        Assert.Equal(corner, Distance(design, 0), 10);
        Assert.Equal(star, Distance(design, 8), 10);
        Assert.Equal(0, Distance(design, design.GetLength(0) - 1), 12);
    }

    [Fact]
    public void AnInscribedDesignStaysInsideTheUnitCubeAndAFacedOneOnIt()
    {
        double[,] inscribed = DesignOfExperiments.CentralComposite(
            2, 0, DesignOfExperiments.CompositeKind.Inscribed, 5);
        for (int r = 0; r < inscribed.GetLength(0); r++)
        {
            for (int c = 0; c < 2; c++)
            {
                Assert.InRange(inscribed[r, c], -1.0000001, 1.0000001);
            }
        }

        double[,] faced = DesignOfExperiments.CentralComposite(
            2, 0, DesignOfExperiments.CompositeKind.Faced, 5);
        Assert.Equal(1, Distance(faced, 4), 10);
    }

    [Fact]
    public void HalvingTheCubeHalvesTheRuns()
    {
        double[,] whole = DesignOfExperiments.CentralComposite(
            4, 0, DesignOfExperiments.CompositeKind.Circumscribed, 0);
        double[,] half = DesignOfExperiments.CentralComposite(
            4, 1, DesignOfExperiments.CompositeKind.Circumscribed, 0);

        Assert.Equal(16 + 8, whole.GetLength(0));
        Assert.Equal(8 + 8, half.GetLength(0));
    }

    private static double Distance(double[,] design, int row)
    {
        double total = 0;
        for (int c = 0; c < design.GetLength(1); c++)
        {
            total += design[row, c] * design[row, c];
        }

        return Math.Sqrt(total);
    }

    // --- Capability -------------------------------------------------------------------------------

    [Fact]
    public void ACentredProcessHasTheSameIndexBothWays()
    {
        var values = new List<double>();
        for (int i = 0; i < 1001; i++)
        {
            // A symmetric sample about ten, so the mean is exactly the middle of the specification.
            values.Add(10 + ((i - 500) / 250.0));
        }

        ProcessCapability.Capability answer = ProcessCapability.Capable(values, 7, 13);
        Assert.Equal(10, answer.Mean, 10);
        Assert.Equal(answer.Cpl, answer.Cpu, 10);
        Assert.Equal(answer.Cp, answer.Cpk, 10);

        // On target, the index that charges for being off target is the one that does not.
        Assert.Equal(answer.Cp, answer.Cpm, 10);
    }

    [Fact]
    public void MovingTheProcessOffTargetLowersEveryOneSidedIndex()
    {
        double[] centred = [9, 9.5, 10, 10.5, 11];
        double[] shifted = Array.ConvertAll(centred, v => v + 1);

        ProcessCapability.Capability first = ProcessCapability.Capable(centred, 7, 13);
        ProcessCapability.Capability second = ProcessCapability.Capable(shifted, 7, 13);

        Assert.Equal(first.Cp, second.Cp, 10);
        Assert.True(second.Cpk < first.Cpk);
        Assert.True(second.Cpm < first.Cpm);
        Assert.True(second.AboveUpper > first.AboveUpper);
    }

    [Fact]
    public void AOneSidedSpecificationOnlyCountsTheSideItHas()
    {
        double[] values = [9, 9.5, 10, 10.5, 11];
        ProcessCapability.Capability answer =
            ProcessCapability.Capable(values, 7, double.PositiveInfinity);

        Assert.Equal(0, answer.AboveUpper, 12);
        Assert.Equal(answer.BelowLower, answer.Outside, 12);
        Assert.True(double.IsPositiveInfinity(answer.Cp));
        Assert.Equal(answer.Cpl, answer.Cpk, 12);
    }

    // --- Gage studies -----------------------------------------------------------------------------

    /// <summary>
    /// A study in which the parts differ and the measurements do not: every variance the measurement
    /// system is charged with must be zero, and the part must hold all of it.
    /// </summary>
    [Fact]
    public void APerfectGageChargesNothingToTheMeasuring()
    {
        var values = new List<double>();
        var parts = new List<int>();
        var operators = new List<int>();
        for (int part = 0; part < 5; part++)
        {
            for (int person = 0; person < 3; person++)
            {
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    values.Add(part * 10);
                    parts.Add(part);
                    operators.Add(person);
                }
            }
        }

        ProcessCapability.GageStudy study = ProcessCapability.Gage(
            values, parts, operators, ProcessCapability.GageModel.Interaction, 0, 5.15);

        Assert.Equal("Gage R&R", study.Rows[0].Source);
        Assert.Equal(0, study.Rows[0].Variance, 10);
        Assert.Equal(0, study.GageDeviation, 10);
        Assert.True(study.Rows[^2].Variance > 0);
        Assert.Equal(100, study.Rows[^2].PercentVariance, 8);
    }

    [Fact]
    public void RepeatabilityIsTheSpreadWithinACell()
    {
        // The same part measured twice by the same operator differs by exactly one unit each time, so
        // the residual variance is the variance of that pair and nothing else.
        var values = new List<double> { 0, 1, 10, 11, 20, 21 };
        var parts = new List<int> { 0, 0, 1, 1, 2, 2 };
        var operators = new List<int> { 0, 0, 0, 0, 0, 0 };

        ProcessCapability.GageStudy study = ProcessCapability.Gage(
            values, parts, operators, ProcessCapability.GageModel.Linear, 0, 5.15);

        Assert.Equal(0.5, study.Rows[1].Variance, 10);
        Assert.Equal(0, study.Rows[2].Variance, 10);
        Assert.True(study.DistinctCategories >= 1);
    }

    [Fact]
    public void ATolerancePutsEveryRowOnAPercentageOfIt()
    {
        var values = new List<double> { 1, 2, 5, 6, 9, 11 };
        var parts = new List<int> { 0, 0, 1, 1, 2, 2 };

        ProcessCapability.GageStudy plain = ProcessCapability.Gage(
            values, parts, [], ProcessCapability.GageModel.Linear, 0, 5.15);
        ProcessCapability.GageStudy against = ProcessCapability.Gage(
            values, parts, [], ProcessCapability.GageModel.Linear, 20, 5.15);

        Assert.True(double.IsNaN(plain.Rows[0].PercentTolerance));
        Assert.Equal(100 * plain.Rows[0].Sigma / 20, against.Rows[0].PercentTolerance, 10);
    }
}
