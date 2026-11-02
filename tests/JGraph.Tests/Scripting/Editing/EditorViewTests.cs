using JGraph.Scripting.Editing;
using Xunit;

namespace JGraph.Tests.Scripting.Editing;

/// <summary>The App Designer window plan's stage W9c: the folds' candidates and lineage, and the zoom's steps, as R2025b's FoldingModel and ZoomController have them.</summary>
public class EditorViewTests
{
    private static readonly string[] Program =
    [
        "function out = demo(x)",      // 1
        "% The first comment",         // 2
        "% and the second",            // 3
        "if x > 0",                    // 4
        "    for k = 1:x",             // 5
        "        out = k;",            // 6
        "    end",                     // 7
        "else",                        // 8
        "    out = 0;",                // 9
        "end",                         // 10
        "%% A section",                // 11
        "switch out",                  // 12
        "    case 1",                  // 13
        "        disp(1)",             // 14
        "    otherwise",               // 15
        "        disp(2)",             // 16
        "end",                         // 17
        "end",                         // 18
    ];

    [Fact]
    public void TheDefaultKindsFoldWhatR2025bFoldsByDefault()
    {
        IReadOnlyList<FoldCandidate> all = CodeFoldRules.AllBlocks(Program);
        Assert.Contains(new FoldCandidate(FoldKind.Function, 1, 18), all);
        Assert.Contains(new FoldCandidate(FoldKind.Comment, 2, 3), all);
        Assert.Contains(new FoldCandidate(FoldKind.If, 4, 10), all);
        Assert.Contains(new FoldCandidate(FoldKind.For, 5, 7), all);
        Assert.Contains(new FoldCandidate(FoldKind.Switch, 12, 17), all);
        Assert.Contains(new FoldCandidate(FoldKind.Case, 13, 14), all);
        Assert.Contains(new FoldCandidate(FoldKind.Case, 15, 16), all);
        Assert.Contains(new FoldCandidate(FoldKind.Section, 11, 18), all);

        // FoldIfElseBlocks, FoldSwitchCaseBlocks and FoldSections are off in R2025b's factory settings.
        IReadOnlyList<FoldCandidate> defaults = CodeFoldRules.Candidates(Program);
        Assert.DoesNotContain(defaults, static c => c.Kind is FoldKind.If or FoldKind.Switch or FoldKind.Case or FoldKind.Section);
        Assert.Contains(new FoldCandidate(FoldKind.For, 5, 7), defaults);
        Assert.Contains(new FoldCandidate(FoldKind.Comment, 2, 3), defaults);
        Assert.Contains(new FoldCandidate(FoldKind.Function, 1, 18), defaults);
    }

    [Fact]
    public void TheCandidateAtALineIsTheInnermostAndItsLineageTheBlocksAround()
    {
        IReadOnlyList<FoldCandidate> all = CodeFoldRules.AllBlocks(Program);
        Assert.Equal(new FoldCandidate(FoldKind.For, 5, 7), CodeFoldRules.At(all, 6));
        Assert.Equal(new FoldCandidate(FoldKind.If, 4, 10), CodeFoldRules.At(all, 9));
        Assert.Equal(new FoldCandidate(FoldKind.Function, 1, 18), CodeFoldRules.At(all, 1));
        IReadOnlyList<FoldCandidate> lineage = CodeFoldRules.Lineage(all, new FoldCandidate(FoldKind.For, 5, 7));
        Assert.Equal([FoldKind.Function, FoldKind.If, FoldKind.For], lineage.Select(static c => c.Kind).ToArray());
    }

    [Fact]
    public void ACommentRunNeedsTwoWholeLinesAndNoSectionTitle()
    {
        string[] lines = ["% one", "x = 1; % trailing", "% two", "% three", "%% title", "% four", "%{", "block", "%}", "% five"];
        IReadOnlyList<FoldCandidate> all = CodeFoldRules.AllBlocks(lines);
        Assert.Contains(new FoldCandidate(FoldKind.Comment, 3, 4), all);
        Assert.Contains(new FoldCandidate(FoldKind.BlockComment, 7, 9), all);
        Assert.DoesNotContain(all, static c => c.Kind == FoldKind.Comment && (c.First == 1 || c.First == 5 || c.First == 6 || c.First == 10));
    }

    [Theory]
    [InlineData(1.0, 1.1, 0.9)]
    [InlineData(0.25, 0.33, 0.25)]
    [InlineData(5.0, 5.0, 4.0)]
    [InlineData(1.75, 2.0, 1.5)]
    public void TheZoomStepsThroughR2025bsFactors(double factor, double next, double previous)
    {
        Assert.Equal(next, ZoomRules.Next(factor));
        Assert.Equal(previous, ZoomRules.Previous(factor));
        Assert.Equal(factor < 5, ZoomRules.CanZoomIn(factor));
        Assert.Equal(factor > 0.25, ZoomRules.CanZoomOut(factor));
        Assert.Equal(factor != 1, ZoomRules.CanReset(factor));
    }

    [Fact]
    public void TheWheelStepsOncePastTheThreshold()
    {
        // A browser's wheel delta is positive turning down, which zooms out; the accumulation starts again after each step.
        double turned = 0;
        turned += 40;
        Assert.Equal(0, ZoomRules.WheelStep(ref turned));
        turned += 40;
        Assert.Equal(-1, ZoomRules.WheelStep(ref turned));
        Assert.Equal(0, turned);
        Assert.Equal(0, ZoomRules.WheelStep(ref turned));
        turned -= 120;
        Assert.Equal(1, ZoomRules.WheelStep(ref turned));
    }
}
