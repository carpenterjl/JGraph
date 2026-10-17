using JGraph.Core.Model;
using JGraph.Core.Primitives;
using Xunit;

namespace JGraph.Tests.Maths;

/// <summary>
/// Open item 119 (ADR 0238): R2025b's scored choice of a linear step through the bands a ruler's
/// length moves it through. Every expectation is a recorded R2025b answer (probe_119b, probe_119c
/// and the batch 18 sweeps in the open-items scratch); the pixels are R2025b's at 10 points, which
/// are this build's at its reference font of 11.
/// </summary>
public class TickRuleBandTests
{
    private static TickBudget Across(double pixels) => TickBudget.ForRuler(pixels, TickRule.ReferenceFontPoints, vertical: false, target: 5);

    private static TickBudget Up(double pixels) => TickBudget.ForRuler(pixels, TickRule.ReferenceFontPoints, vertical: true, target: 5);

    [Theory]
    [InlineData(60, false, 3, 0)]
    [InlineData(109, false, 3, 0)]
    [InlineData(110, false, 3.5, 0)]
    [InlineData(200, false, 4.5, 0)]
    [InlineData(250, false, 5.5, 0)]
    [InlineData(300, false, 0, 7)]
    [InlineData(340, false, 0, 8)]
    [InlineData(400, false, 0, 9)]
    [InlineData(434, false, 0, 10)]
    [InlineData(480, false, 0, 11)]
    [InlineData(600, false, 0, 11)]
    [InlineData(60, true, 3, 0)]
    [InlineData(70, true, 3.5, 0)]
    [InlineData(100, true, 4.5, 0)]
    [InlineData(120, true, 5.5, 0)]
    [InlineData(140, true, 7, 0)]
    [InlineData(180, true, 7.5, 0)]
    [InlineData(215, true, 9, 0)]
    [InlineData(240, true, 0, 9)]
    [InlineData(250, true, 0, 10)]
    [InlineData(270, true, 0, 11)]
    [InlineData(300, true, 0, 11)]
    public void TheBandsFollowThePixelsOverTheFont(double pixels, bool vertical, double target, int free)
    {
        TickScore score = TickRule.ScoreFor(pixels, TickRule.ReferenceFontPoints, vertical);
        Assert.Equal(target, score.Target);
        Assert.Equal(free, score.MaxFree);
    }

    [Theory]
    [InlineData(104, 8.8, false, 3)]
    [InlineData(105, 8.8, false, 3.5)]
    [InlineData(145, 8.8, false, 4.5)]
    [InlineData(174, 14.3, false, 3)]
    [InlineData(175, 14.3, false, 3.5)]
    [InlineData(107, 9.9, false, 3)]
    [InlineData(108, 9.9, false, 3.5)]
    [InlineData(175, 8.8, true, 9)]
    [InlineData(175, 11, true, 7.5)]
    [InlineData(180, 14.3, true, 5.5)]
    public void TheEdgesMoveWithTheFontAsMeasured(double pixels, double font, bool vertical, double target)
    {
        // JGraph's 8.8, 11 and 14.3 points are R2025b's 8, 10 and 13, the fonts probe_119b swept; 9.9
        // is R2025b's 9, between two measured fonts, where the first edge is interpolated to 107.5.
        Assert.Equal(target, TickRule.ScoreFor(pixels, font, vertical).Target);
    }

    [Fact]
    public void TheCappedBandsScaleWithTheDrawnFont()
    {
        // A colorbar reads its 434 pixels at nine tenths of the font: 482 at the table's 10 points,
        // past the last horizontal edge of 478 (oi_patch_rows_axes_font_short_rulers).
        Assert.Equal(11, TickRule.ScoreFor(434, 9.9, vertical: false).MaxFree);
        Assert.Equal(10, TickRule.ScoreFor(434, 11, vertical: false).MaxFree);

        // A square axes shrunk to 9.5 points keeps its scored bands at the nominal 10 (200 pixels
        // up stay in the 7.5 band) but reads the caps at the drawn size: 255 pixels are 268, the
        // top band, where 250 are not (probe_119d, the batch 19 fixture).
        Assert.Equal(7.5, TickRule.ScoreFor(200, 11, vertical: true, drawnFontPoints: 9.9).Target);
        Assert.Equal(11, TickRule.ScoreFor(255, 11, vertical: true, drawnFontPoints: 10.45).MaxFree);
        Assert.Equal(10, TickRule.ScoreFor(250, 11, vertical: true, drawnFontPoints: 10.45).MaxFree);
    }

    [Theory]
    [InlineData(60, 0, 5, 5)]
    [InlineData(60, 0, 3, 1)]
    [InlineData(60, 0, 6, 5)]
    [InlineData(60, 0, 17, 10)]
    [InlineData(60, 0, 25, 10)]
    [InlineData(60, 0, 0.3, 0.1)]
    [InlineData(60, 0, 13, 5)]
    [InlineData(140, 0, 5, 2)]
    [InlineData(140, 0, 6, 2)]
    [InlineData(140, 0, 9, 2)]
    [InlineData(140, 0, 25, 10)]
    [InlineData(140, 0, 50, 20)]
    [InlineData(140, 0.125, 5.125, 1)]
    [InlineData(200, 0, 5, 1)]
    [InlineData(200, 0, 25, 5)]
    [InlineData(200, 0, 20, 5)]
    [InlineData(250, 0, 55, 10)]
    [InlineData(434, 0, 15, 5)]
    [InlineData(434, 0, 10, 2)]
    public void AnXRulerTakesR2025bsStep(double pixels, double min, double max, double step)
    {
        Assert.Equal(step, TickRule.ChooseStep(min, max, Across(pixels)), 12);
    }

    [Theory]
    [InlineData(75, 0, 5, 2)]
    [InlineData(100, 0, 5, 1)]
    [InlineData(140, 0, 3, 0.5)]
    [InlineData(140, 0, 35, 5)]
    [InlineData(190, 0, 0.75, 0.1)]
    [InlineData(240, 0, 10, 2)]
    [InlineData(240, 0, 9, 1)]
    [InlineData(342, 0, 10, 1)]
    [InlineData(342, 0, 11, 1)]
    [InlineData(342, 0, 55, 5)]
    [InlineData(342, 0, 0.9, 0.1)]
    [InlineData(250, 0, 0.55, 0.1)]
    public void AYRulerTakesR2025bsStep(double pixels, double min, double max, double step)
    {
        Assert.Equal(step, TickRule.ChooseStep(min, max, Up(pixels)), 12);
    }

    [Theory]
    [InlineData(0, 1, 0.2)]
    [InlineData(0, 6, 2)]
    [InlineData(0, 12, 5)]
    [InlineData(0, 50, 10)]
    [InlineData(1, 10, 2)]
    public void ThePolarRadiusCountsTheSameAtEverySize(double min, double max, double step)
    {
        Assert.Equal(step, TickRule.ChooseStep(min, max, TickRule.PolarRadius(5)), 12);
    }

    [Fact]
    public void AlignedLimitsGiveTheStepUpWhenTheMovedLimitsPreferACoarserOne()
    {
        // R2025b ends [-1 1000] on the default x ruler in steps of 200 at [-200 1000] (probe_99b).
        DataRange aligned = TickRule.Align(new DataRange(-1, 1000), Across(434));
        Assert.Equal(-200, aligned.Min, 9);
        Assert.Equal(1000, aligned.Max, 9);
        Assert.Equal(200, TickRule.ChooseStep(aligned.Min, aligned.Max, Across(434)), 12);
    }

    [Fact]
    public void AlignedLimitsReachAQuarterOfTheRange()
    {
        // [-3 11] on a 120-pixel ruler becomes [-5 11]: the 15 is too far (probe_99c); on a 60-pixel
        // one at the same font the fives would hold four ticks, so the limits stay (oi_short_rulers).
        DataRange aligned = TickRule.Align(new DataRange(-3, 11), Across(120));
        Assert.Equal(-5, aligned.Min, 9);
        Assert.Equal(11, aligned.Max, 9);
        DataRange kept = TickRule.Align(new DataRange(-3, 11), Across(60));
        Assert.Equal(-3, kept.Min, 9);
        Assert.Equal(11, kept.Max, 9);
    }

    [Theory]
    [InlineData(60, 0, 0.3, 0.1, true)]
    [InlineData(60, 0, 0.15, 0.05, true)]
    [InlineData(120, 0, 0.3, 0.1, false)]
    [InlineData(250, 0, 0.3, 0.1, false)]
    [InlineData(250, 0, 0.6, 0.2, false)]
    public void AStepOverTheTargetIsCountedExactly(double pixels, double min, double max, double step, bool exactly)
    {
        // 3 × 0.1 overshoots 0.3 in doubles: a 60-pixel ruler at 10 points draws 0, 0.1, 0.2 and a
        // 250-pixel one draws to 0.3 (probe_119b, oi_short_rulers).
        Assert.Equal(step, TickRule.ChooseStep(min, max, Across(pixels), out bool counted), 12);
        Assert.Equal(exactly, counted);
    }

    [Fact]
    public void ABudgetWithoutAScoreKeepsTheClassicLadder()
    {
        Assert.False(TickBudget.Of(5).HasCaps);
        DataRange aligned = TickRule.Align(new DataRange(0.3, 9.7), TickBudget.Of(5));
        Assert.Equal(0, aligned.Min, 9);
        Assert.Equal(10, aligned.Max, 9);
    }
}
