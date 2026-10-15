using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Serialization;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open item 126 (ADR 0234), where the parity fixture cannot reach: the arithmetic of R2025b's
/// automatic tick label angle on its own (probes m126_rot3, m126_rot4 and m126_rot5), the angle the
/// renderer draws for a crowded categorical ruler, and what a saved figure keeps of a ruler's angle.
/// What a script reads is R2025b's (fixture oi_crowded_ruler_mixed_axis).
/// </summary>
[Collection("JG facade")]
public class OpenItemsBatch21Tests : IAsyncLifetime
{
    private readonly RecordingScriptOutput _output = new();
    private JgsReplSession _session = null!;

    public Task InitializeAsync()
    {
        JG.Reset();
        _session = Assert.IsType<JgsReplSession>(((IScriptRepl)new MatlabScriptEngine()).CreateSession(
            new ScriptContext(_output, (_, _) => { })));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _session.DisposeAsync();
        JgsHandleRegistry.Clear();
        JG.Reset();
    }

    private async Task Exec(string code)
    {
        ScriptRunResult result = await _session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    // --- the rule ---------------------------------------------------------------------------------

    [Fact]
    public void LabelsTurnAtR2025bsMeasuredThresholds()
    {
        // Four digits at 10 points: 29.66 pixels wide, 15 high; the level gap is 9/16 of 13.33 pixels.
        const double wide = 29.66, high = 15, font = 40.0 / 3;
        Assert.Equal(0, TickLabelTurn.Angle(38, wide, high, font));    // ceil(29.66) + ceil(7.5) = 38
        Assert.Equal(30, TickLabelTurn.Angle(37.9, wide, high, font));
        Assert.Equal(30, TickLabelTurn.Angle(30, wide, high, font));   // two heights
        Assert.Equal(45, TickLabelTurn.Angle(29.9, wide, high, font));
        Assert.Equal(45, TickLabelTurn.Angle(15, wide, high, font));   // one height
        Assert.Equal(90, TickLabelTurn.Angle(14.9, wide, high, font));

        // A categorical ruler of 8 to 31 categories takes the larger angle exactly at a threshold.
        Assert.Equal(30, TickLabelTurn.Angle(38, wide, high, font, tiesTurn: true));
        Assert.Equal(45, TickLabelTurn.Angle(30, wide, high, font, tiesTurn: true));
        Assert.Equal(90, TickLabelTurn.Angle(15, wide, high, font, tiesTurn: true));

        // One tick or none never crowds.
        Assert.Equal(0, TickLabelTurn.Angle(double.PositiveInfinity, wide, high, font));
    }

    [Fact]
    public void TheRoomIsTheRulersWholePixelsSharedAmongItsTicks()
    {
        // A default axes in a figure 491 pixels wide: each end snapped, 444 - 64 = 380 pixels.
        double[] ten = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        Assert.Equal(38, TickLabelTurn.Spacing(ten, new DataRange(0.5, 10.5), 0.13 * 491, 0.775 * 491, logarithmic: false), 9);

        // Ticks 1..9 under limits [0 10] share the ruler among nine, not the distance between ticks.
        double[] nine = [1, 2, 3, 4, 5, 6, 7, 8, 9];
        Assert.Equal(299.0 / 9, TickLabelTurn.Spacing(nine, new DataRange(0, 10), 20, 299, logarithmic: false), 9);

        // Ticks outside the limits do not count; one tick left has all the room there is.
        Assert.Equal(double.PositiveInfinity, TickLabelTurn.Spacing([1, 20], new DataRange(0, 10), 0, 300, logarithmic: false));
    }

    // --- the renderer and the file ------------------------------------------------------------------

    [Fact]
    public async Task ACrowdedCategoricalRulerIsDrawnTurned_AndALevelOneLevel()
    {
        await Exec(
            "f = figure('Visible', 'off', 'Position', [100 100 560 420]); ax = axes(f);"
            + " names = arrayfun(@(k) sprintf('c%02d', k), 1:30, 'UniformOutput', false);"
            + " bar(ax, categorical(names, names), 1:30);");
        AxesModel crowded = Assert.Single(JG.Gcf().Axes);
        new FigureRenderer().Render(JG.Gcf(), new RecordingRenderContext(new Size2D(560, 420)), Theme.Light);
        Assert.True(crowded.PrimaryXAxis.TickLabelAngleAuto);
        Assert.Equal(90, crowded.PrimaryXAxis.DrawnTickLabelAngle); // 434 pixels over 30 ticks is under one height

        JG.Reset();
        await Exec(
            "f = figure('Visible', 'off', 'Position', [100 100 560 420]); ax = axes(f);"
            + " bar(ax, categorical({'a', 'b', 'c'}), 1:3);");
        AxesModel roomy = Assert.Single(JG.Gcf().Axes);
        new FigureRenderer().Render(JG.Gcf(), new RecordingRenderContext(new Size2D(560, 420)), Theme.Light);
        Assert.Equal(0, roomy.PrimaryXAxis.DrawnTickLabelAngle);
    }

    [Fact]
    public async Task ASavedFigureKeepsWhetherARulerTurnsItsOwnLabels()
    {
        await Exec(
            "f = figure('Visible', 'off'); ax = axes(f); plot(ax, 1:3);"
            + " ax2 = axes(figure('Visible', 'off')); plot(ax2, 1:3); ax2.XTickLabelRotation = 20;");
        FigureModel[] figures = [.. JG.FigureNumbers.Select(static n => JG.TryGetFigure(n, out FigureModel f) ? f : throw new InvalidOperationException())];
        Assert.Equal(2, figures.Length);

        AxisModel automatic = GraphFormat.Deserialize(GraphFormat.Serialize(figures[0])).Axes[0].PrimaryXAxis;
        Assert.True(automatic.TickLabelAngleAuto);

        AxisModel manual = GraphFormat.Deserialize(GraphFormat.Serialize(figures[1])).Axes[0].PrimaryXAxis;
        Assert.False(manual.TickLabelAngleAuto);
        Assert.Equal(20, manual.TickLabelAngle);
        Assert.Equal(20, manual.DrawnTickLabelAngle);
    }
}
