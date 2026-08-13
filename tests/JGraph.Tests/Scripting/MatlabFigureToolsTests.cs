using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Objects.Annotations;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M60: the figure-tooling verbs — annotation, the figure file and export family, application data,
/// linkprop, the animation seam's batch behaviour, and the interaction verbs.
/// <para>
/// These verbs mostly work on what other verbs drew, so the claims here are about the objects the
/// script layer built and the values it answered with, not about pixels. The pixel claims — that a
/// saved figure reloads to the same picture — belong to <c>stess_32.m</c>, which can render.
/// </para>
/// </summary>
[Collection("JG facade")]
public class MatlabFigureToolsTests : IDisposable
{
    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public MatlabFigureToolsTests()
    {
        JG.Reset();
        _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        JG.Reset();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    private ScriptRunResult Run(string code) =>
        _engine.RunAsync(
            code,
            new ScriptContext(
                _output,
                (_, _) => { },
                _directory,
                resolvePath: null,
                figureFiles: new TestFigureFiles()),
            CancellationToken.None).GetAwaiter().GetResult();

    private string Printed(ScriptRunResult result) => result.Message + _output.ErrorText;

    private void Ok(ScriptRunResult result) => Assert.True(result.Success, Printed(result));

    private static AnnotationObject SoleAnnotation() => Assert.Single(JG.CurrentFigure.Annotations);

    // --- annotation -----------------------------------------------------------------------------

    [Theory]
    [InlineData("rectangle", typeof(RectangleAnnotation))]
    [InlineData("ellipse", typeof(EllipseAnnotation))]
    [InlineData("textbox", typeof(TextAnnotation))]
    [InlineData("line", typeof(ArrowAnnotation))]
    [InlineData("arrow", typeof(ArrowAnnotation))]
    [InlineData("doublearrow", typeof(ArrowAnnotation))]
    [InlineData("textarrow", typeof(ArrowAnnotation))]
    public void EachAnnotationKindMakesItsObject(string kind, Type expected)
    {
        Ok(Run($"figure(1); annotation('{kind}');"));
        Assert.IsType(expected, SoleAnnotation());
    }

    [Fact]
    public void AnAnnotationLivesInFigureSpace()
    {
        Ok(Run("figure(1); annotation('rectangle', [0.2 0.3 0.4 0.1]);"));
        Assert.Equal(AnnotationSpace.Figure, SoleAnnotation().Space);
    }

    /// <summary>
    /// The y flip is the one thing this verb owns: MATLAB measures up from the bottom of the figure
    /// and this model measures down from the top, so a box written at y = 0.3 with height 0.1 has
    /// its top edge at 1 - 0.4 in the model.
    /// </summary>
    [Fact]
    public void ABoxIsStoredWithItsYMeasuredFromTheTop()
    {
        Ok(Run("figure(1); annotation('rectangle', [0.2 0.3 0.4 0.1]);"));
        var shape = Assert.IsType<RectangleAnnotation>(SoleAnnotation());
        Assert.Equal(0.2, shape.Corner1.X, 12);
        Assert.Equal(0.6, shape.Corner2.X, 12);
        Assert.Equal(0.6, shape.Corner1.Y, 12);   // 1 - (0.3 + 0.1)
        Assert.Equal(0.7, shape.Corner2.Y, 12);   // 1 - 0.3
    }

    [Fact]
    public void APositionReadsBackAsItWasWritten()
    {
        Ok(Run(
            "figure(1); h = annotation('ellipse', [0.15 0.25 0.35 0.45]); "
            + "p = get(h, 'Position'); fprintf('%.4f %.4f %.4f %.4f\\n', p(1), p(2), p(3), p(4));"));
        Assert.Contains("0.1500 0.2500 0.3500 0.4500", _output.NormalText);
    }

    [Fact]
    public void AnArrowIsMeasuredByItsTwoEnds()
    {
        Ok(Run(
            "figure(1); h = annotation('arrow', [0.2 0.5], [0.3 0.7]); "
            + "x = get(h, 'X'); y = get(h, 'Y'); fprintf('%.2f %.2f %.2f %.2f\\n', x(1), x(2), y(1), y(2));"));
        Assert.Contains("0.20 0.50 0.30 0.70", _output.NormalText);
    }

    [Fact]
    public void TheFourArrowKindsAreOneObjectToldApartByItsProperties()
    {
        Ok(Run("figure(1); annotation('doublearrow', [0 1], [0 1]);"));
        var arrow = Assert.IsType<ArrowAnnotation>(SoleAnnotation());
        Assert.True(arrow.ShowTailHead);
        Assert.True(arrow.ShowHead);

        JG.Reset();
        Ok(Run("figure(1); annotation('line', [0 1], [0 1]);"));
        Assert.False(Assert.IsType<ArrowAnnotation>(SoleAnnotation()).ShowHead);
    }

    [Fact]
    public void ATextboxKeepsTheBoxItWasGiven()
    {
        Ok(Run("figure(1); annotation('textbox', [0.1 0.2 0.3 0.4], 'String', 'hi');"));
        var text = Assert.IsType<TextAnnotation>(SoleAnnotation());
        Assert.NotNull(text.Box);
        Assert.Equal(0.3, text.Box!.Value.Width, 12);
        Assert.Equal(0.4, text.Box!.Value.Height, 12);
        Assert.Equal("hi", text.Text);
    }

    [Theory]
    [InlineData("annotation('rectangle', [0 0 1 1])", "rectangle")]
    [InlineData("annotation('ellipse', [0 0 1 1])", "ellipse")]
    [InlineData("annotation('textbox', [0 0 1 1])", "textbox")]
    [InlineData("annotation('arrow', [0 1], [0 1])", "arrow")]
    [InlineData("annotation('doublearrow', [0 1], [0 1])", "doublearrow")]
    [InlineData("annotation('line', [0 1], [0 1])", "line")]
    [InlineData("annotation('textarrow', [0 1], [0 1], 'String', 'x')", "textarrow")]
    public void EachKindAnswersItsOwnTypeName(string call, string expected)
    {
        Ok(Run($"figure(1); h = {call}; disp(get(h, 'Type'));"));
        Assert.Contains(expected, _output.NormalText);
    }

    [Fact]
    public void AnUnknownKindNamesTheOnesThatExist()
    {
        ScriptRunResult result = Run("figure(1); annotation('squiggle', [0 0 1 1]);");
        Assert.False(result.Success);
        Assert.Contains("textarrow", Printed(result));
    }

    [Fact]
    public void ABoxNeedsFourNumbers()
    {
        ScriptRunResult result = Run("figure(1); annotation('rectangle', [0 0 1]);");
        Assert.False(result.Success);
        Assert.Contains("4 numbers", Printed(result));
    }

    // --- Figure files and pictures ----------------------------------------------------------------

    [Fact]
    public void SavefigAddsTheFigExtensionAndOpenfigReadsItBack()
    {
        Ok(Run("figure(1); plot(1:10); savefig('round'); h = openfig('round.fig'); disp(numel(get(h, 'Children')));"));
        Assert.True(File.Exists(Path.Combine(_directory, "round.fig")));
        Assert.Contains("1", _output.NormalText);
    }

    [Fact]
    public void HgsaveAndHgloadAreTheSameTwoVerbs()
    {
        Ok(Run("figure(1); plot(1:10); hgsave(1, 'old.fig'); h = hgload('old.fig'); disp(h > 0);"));
        Assert.True(File.Exists(Path.Combine(_directory, "old.fig")));
    }

    [Fact]
    public void ExportgraphicsWritesTheFormatItsExtensionNames()
    {
        Ok(Run(
            "figure(1); plot(1:10); exportgraphics(gcf, 'p.png'); "
            + "exportgraphics(gcf, 'p.pdf', 'ContentType', 'vector', 'Resolution', 200);"));
        Assert.True(File.Exists(Path.Combine(_directory, "p.png")));
        Assert.True(File.Exists(Path.Combine(_directory, "p.pdf")));
    }

    [Fact]
    public void AMisspeltExportOptionIsNamedRatherThanDropped()
    {
        ScriptRunResult result = Run("figure(1); plot(1:10); exportgraphics(gcf, 'p.png', 'Resolutn', 200);");
        Assert.False(result.Success);
        Assert.Contains("Resolution", Printed(result));
    }

    /// <summary>
    /// A frame is a plain <c>uint8</c> array rather than an image value, which is what lets a script
    /// difference two of them — the reason <c>getframe</c> is worth having headless at all.
    /// </summary>
    [Fact]
    public void GetframeAnswersAUint8HeightWidthThreeArray()
    {
        Ok(Run(
            "figure(1); plot(1:10); f = getframe; s = size(f.cdata); "
            + "fprintf('%d %s %d\\n', numel(s), class(f.cdata), s(3));"));
        Assert.Contains("3 uint8 3", _output.NormalText);
    }

    [Fact]
    public void AFrameCarriesNoColourTable()
    {
        Ok(Run("figure(1); plot(1:10); f = getframe; fprintf('%d\\n', isempty(f.colormap));"));
        Assert.Contains("1", _output.NormalText);
    }

    [Fact]
    public void AFramesNumbersCanBeUsedAsNumbers()
    {
        Ok(Run("figure(1); plot(1:10); f = getframe; d = double(f.cdata); fprintf('%d\\n', max(d(:)) <= 255);"));
        Assert.Contains("1", _output.NormalText);
    }

    [Fact]
    public void CopygraphicsIsAnAnswerWhereThereIsNoClipboard()
    {
        Ok(Run("figure(1); plot(1:10); copygraphics(gcf); copygraphics(gcf, 'Resolution', 300);"));
    }

    // --- Application data ---------------------------------------------------------------------------

    [Fact]
    public void AppdataStoresReadsAndRemoves()
    {
        Ok(Run(
            "figure(1); setappdata(gcf, 'n', 42); a = getappdata(gcf, 'n'); "
            + "b = isappdata(gcf, 'n'); rmappdata(gcf, 'n'); c = isappdata(gcf, 'n'); "
            + "fprintf('%d %d %d\\n', a, b, c);"));
        Assert.Contains("42 1 0", _output.NormalText);
    }

    [Fact]
    public void AppdataIsPerObject()
    {
        Ok(Run(
            "figure(1); plot(1:10); setappdata(gcf, 'w', 'fig'); setappdata(gca, 'w', 'ax'); "
            + "fprintf('%s %s\\n', getappdata(gcf, 'w'), getappdata(gca, 'w'));"));
        Assert.Contains("fig ax", _output.NormalText);
    }

    [Fact]
    public void AnUnstoredNameAnswersEmptyRatherThanErroring()
    {
        Ok(Run("figure(1); fprintf('%d\\n', isempty(getappdata(gcf, 'never')));"));
        Assert.Contains("1", _output.NormalText);
    }

    [Fact]
    public void GetappdataWithNoNameAnswersTheWholeLot()
    {
        Ok(Run(
            "figure(1); setappdata(gcf, 'a', 1); setappdata(gcf, 'b', 2); "
            + "disp(numel(fieldnames(getappdata(gcf))));"));
        Assert.Contains("2", _output.NormalText);
    }

    [Fact]
    public void RemovingSomethingThatIsNotThereSaysSo()
    {
        ScriptRunResult result = Run("figure(1); rmappdata(gcf, 'never');");
        Assert.False(result.Success);
        Assert.Contains("never", Printed(result));
    }

    // --- linkprop -------------------------------------------------------------------------------------

    /// <summary>
    /// The property this is usually asked for does not live on the object it is named on — an axes'
    /// <c>XLim</c> is a range on its x ruler — so the link has to watch the whole subtree. This test
    /// is the one that fails if it goes back to watching the object itself.
    /// </summary>
    [Fact]
    public void ALinkedPropertyThatLivesOnAChildStillMirrors()
    {
        Ok(Run(
            "figure(1); ax1 = gca; plot(1:5); figure(2); ax2 = gca; plot(1:5); "
            + "linkprop([ax1 ax2], 'XLim'); xlim(ax1, [1 3]); "
            + "l = get(ax2, 'XLim'); fprintf('%g %g\\n', l(1), l(2));"));
        Assert.Contains("1 3", _output.NormalText);
    }

    [Fact]
    public void ALinkMirrorsInBothDirections()
    {
        Ok(Run(
            "figure(1); ax1 = gca; plot(1:5); figure(2); ax2 = gca; plot(1:5); "
            + "linkprop([ax1 ax2], 'XLim'); xlim(ax2, [4 8]); "
            + "l = get(ax1, 'XLim'); fprintf('%g %g\\n', l(1), l(2));"));
        Assert.Contains("4 8", _output.NormalText);
    }

    [Fact]
    public void CreatingALinkBringsTheOthersIntoStepAtOnce()
    {
        Ok(Run(
            "figure(1); ax1 = gca; plot(1:5); xlim(ax1, [0 20]); "
            + "figure(2); ax2 = gca; plot(1:5); linkprop([ax1 ax2], 'XLim'); "
            + "l = get(ax2, 'XLim'); fprintf('%g %g\\n', l(1), l(2));"));
        Assert.Contains("0 20", _output.NormalText);
    }

    [Fact]
    public void ALinkTakesACellOfNames()
    {
        Ok(Run(
            "figure(1); p1 = plot(1:5); figure(2); p2 = plot(1:5); "
            + "linkprop([p1 p2], {'LineWidth', 'Visible'}); set(p1, 'LineWidth', 4); "
            + "disp(get(p2, 'LineWidth'));"));
        Assert.Contains("4", _output.NormalText);
    }

    // --- Transparency and the renderer -------------------------------------------------------------------

    [Fact]
    public void AlphaSetsWhatHasAFaceAndLeavesTheRestAlone()
    {
        Ok(Run("figure(1); surf(peaks(8)); hold on; plot(1:5); alpha(0.5);"));
    }

    [Fact]
    public void AlimAndAlphamapAnswerWhenNamedBare()
    {
        Ok(Run("figure(1); a = alim; m = alphamap; fprintf('%d %d %g\\n', numel(a), numel(m), a(2));"));
        Assert.Contains("2 64 1", _output.NormalText);
    }

    [Fact]
    public void RendererinfoNamesWhatDrew()
    {
        Ok(Run("figure(1); r = rendererinfo; disp(r.GraphicsRenderer);"));
        Assert.Contains("Skia", _output.NormalText);
    }

    // --- Motion ------------------------------------------------------------------------------------------

    /// <summary>
    /// With nothing to play the animation, the finished curve is the answer — which is what makes
    /// every verb here batch-safe by construction rather than by a flag.
    /// </summary>
    [Fact]
    public void CometLeavesTheWholeCurveBehind()
    {
        Ok(Run("figure(1); comet(1:20, (1:20).^2); h = get(gca, 'Children'); disp(numel(get(h, 'XData')));"));
        Assert.Contains("20", _output.NormalText);
    }

    [Fact]
    public void CometOverOneVectorCountsAlongTheWholeNumbers()
    {
        Ok(Run("figure(1); comet(1:15); h = get(gca, 'Children'); x = get(h, 'XData'); disp(x(15));"));
        Assert.Contains("15", _output.NormalText);
    }

    [Fact]
    public void Comet3KeepsItsThirdCoordinate()
    {
        Ok(Run("figure(1); comet3(1:12, 1:12, (1:12).^2); h = get(gca, 'Children'); disp(numel(get(h, 'ZData')));"));
        Assert.Contains("12", _output.NormalText);
    }

    [Fact]
    public void MovieReadsItsFramesEvenWithNothingToPlayThemOn()
    {
        Ok(Run("figure(1); plot(1:10); f = getframe; movie(f); movie(f, 2); movie(f, 2, 24);"));
    }

    [Fact]
    public void MovieRefusesSomethingThatIsNotAFrame()
    {
        ScriptRunResult result = Run("figure(1); movie(5);");
        Assert.False(result.Success);
        Assert.Contains("getframe", Printed(result));
    }

    [Fact]
    public void StreamparticlesDrawsTheSameMarkersTwice()
    {
        Ok(Run(
            "[x, y] = meshgrid(0:0.2:2, 0:0.2:2); v = stream2(x, y, -y, x, 1, 0.2); "
            + "figure(1); a = get(streamparticles(v, 0.5), 'XData'); "
            + "figure(2); b = get(streamparticles(v, 0.5), 'XData'); fprintf('%d\\n', isequal(a, b));"));
        Assert.Contains("1", _output.NormalText);
    }

    [Fact]
    public void InterpstreamspeedSpacesALineEvenlyAlongItself()
    {
        Ok(Run(
            "[x, y] = meshgrid(0:0.2:2, 0:0.2:2); v = stream2(x, y, -y, x, 1, 0.2); "
            + "r = interpstreamspeed(v, 2); s = sqrt(sum(diff(r).^2, 2)); "
            + "fprintf('%d\\n', max(s) - min(s) < 1e-6 * max(s));"));
        Assert.Contains("1", _output.NormalText);
    }

    [Fact]
    public void APlaneLineRespacesAsAPlaneLine()
    {
        Ok(Run(
            "[x, y] = meshgrid(0:0.2:2, 0:0.2:2); v = stream2(x, y, -y, x, 1, 0.2); "
            + "disp(size(interpstreamspeed(v, 1), 2));"));
        Assert.Contains("2", _output.NormalText);
    }

    // --- The verbs that would wait for a mouse -------------------------------------------------------------

    [Fact]
    public void RotateTurnsAPlotsOwnData()
    {
        Ok(Run(
            "figure(1); h = plot([0 1], [0 0]); rotate(h, [0 90], 90); "
            + "x = get(h, 'XData'); y = get(h, 'YData'); fprintf('%.6f %.6f\\n', x(2), y(2));"));
        Assert.Contains("0.000000 1.000000", _output.NormalText);
    }

    [Fact]
    public void RotatingBackReturnsTheLineToWhereItStarted()
    {
        Ok(Run(
            "figure(1); h = plot([0 1], [0 0]); rotate(h, [0 90], 90); rotate(h, [0 90], -90); "
            + "x = get(h, 'XData'); fprintf('%.6f\\n', x(2));"));
        Assert.Contains("1.000000", _output.NormalText);
    }

    [Fact]
    public void RotateRefusesADirectionThatPointsNowhere()
    {
        ScriptRunResult result = Run("figure(1); h = plot([0 1], [0 0]); rotate(h, [0 0 0], 45);");
        Assert.False(result.Success);
        Assert.Contains("zeros", Printed(result));
    }

    /// <summary>The write half of the pair that <c>rotate</c> needed and that M54 left unwritable.</summary>
    [Fact]
    public void ASeriesCanBeMovedByWritingItsData()
    {
        Ok(Run(
            "figure(1); h = plot(1:5, 1:5); set(h, 'YData', [5 4 3 2 1]); "
            + "y = get(h, 'YData'); x = get(h, 'XData'); fprintf('%g %g\\n', y(1), x(1));"));
        Assert.Contains("5 1", _output.NormalText);
    }

    [Fact]
    public void WritingASeriesOfTheWrongLengthSaysSo()
    {
        ScriptRunResult result = Run("figure(1); h = plot(1:5, 1:5); set(h, 'YData', [1 2]);");
        Assert.False(result.Success);
        Assert.Contains("written together", Printed(result));
    }

    [Fact]
    public void TheModeVerbsRememberWhatTheyWereTold()
    {
        Ok(Run("figure(1); pan on; a = pan(gcf); pan off; b = pan(gcf); fprintf('%s %s\\n', a, b);"));
        Assert.Contains("on off", _output.NormalText);
    }

    [Fact]
    public void GtextNamesTheTwoVerbsThatSayWhereInstead()
    {
        ScriptRunResult result = Run("figure(1); gtext('here');");
        Assert.False(result.Success);
        Assert.Contains("annotation", Printed(result));
    }

    [Fact]
    public void TheFiveInteractivityTogglesAreAccepted()
    {
        Ok(Run(
            "figure(1); plot(1:5); disableDefaultInteractivity(gca); enableDefaultInteractivity(gca); "
            + "enableLegacyExplorationModes(gcf); addToolbarExplorationButtons(gcf); "
            + "removeToolbarExplorationButtons(gcf);"));
    }
}


