using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Serialization;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M86: the two marker colours, called what MATLAB calls them.
/// <para>
/// The gaps page recorded this as a naming pass, and most of it was: the model spelled them
/// <c>MarkerFill</c> and <c>MarkerEdge</c> while the property table added the MATLAB names on top.
/// What the rename uncovered is that the table is built by <em>reflection</em> over the model's own
/// property names — so the two JGraph spellings were themselves being served as properties, and a
/// script that misspelled one was told <em>"Did you mean … MarkerFill?"</em>, which recommended a
/// name MATLAB has never had.
/// </para>
/// <para>
/// And on the charts in space the names were missing outright. <c>plot3</c> makes a <c>Line</c> and
/// <c>stem3</c> makes a <c>Stem</c> — the same MATLAB classes <c>plot</c> and <c>stem</c> make — but
/// neither answered either colour, because the property census asks the <c>line</c> kind through
/// <c>plot</c> and has never asked <c>plot3</c> anything.
/// </para>
/// </summary>
[Collection("JG facade")]
public class MatlabM86MarkerNameTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public MatlabM86MarkerNameTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(
            code, new ScriptContext(_output, static (_, _) => { }), default);

    private static void Succeeded(ScriptRunResult result) =>
        Assert.True(result.Success, result.Message);

    /// <summary>
    /// A one-element answer, however it was packed. A comparison answers a logical rather than a
    /// number, and a logical arrives bare where a number arrives in an array of one.
    /// </summary>
    private static bool Truth(ScriptRunResult result, string name) =>
        Assert.Single(result.Variables, v => v.Name == name).RawValue switch
        {
            bool flag => flag,
            bool[] { Length: 1 } packed => packed[0],
            double[] { Length: 1 } numbers => numbers[0] != 0,
            double one => one != 0,
            { } other => throw new InvalidOperationException($"{name} is a {other.GetType()}."),
            null => throw new InvalidOperationException($"{name} carries no value."),
        };

    // --- The names a script can reach ---------------------------------------------------------------

    /// <summary>
    /// The JGraph spellings are gone from the property surface. They were never MATLAB names, and a
    /// reflection-built table served them only because they were what the CLR properties were called.
    /// </summary>
    [Theory]
    [InlineData("plot(1:3)")]
    [InlineData("plot3(1:3, 1:3, 1:3)")]
    [InlineData("stem(1:3)")]
    [InlineData("stem3([1 2], [1 2], [1 2])")]
    [InlineData("errorbar(1:3, 1:3, [.1 .1 .1])")]
    [InlineData("surf(peaks(8))")]
    public async Task NoKindAnswersTheJGraphSpellings(string call)
    {
        ScriptRunResult result = await RunMatlab(
            $"h = {call};\nnames = fieldnames(get(h(1)));\n"
            + "a = any(strcmp(names, 'MarkerFill')); b = any(strcmp(names, 'MarkerEdge'));");
        Succeeded(result);
        Assert.False(Truth(result, "a"), $"{call} still answers MarkerFill.");
        Assert.False(Truth(result, "b"), $"{call} still answers MarkerEdge.");
    }

    /// <summary>
    /// And every kind that draws a marker answers both MATLAB names — including the two in space,
    /// which answered neither before M86.
    /// </summary>
    [Theory]
    [InlineData("plot(1:3)")]
    [InlineData("plot3(1:3, 1:3, 1:3)")]
    [InlineData("stem(1:3)")]
    [InlineData("stem3([1 2], [1 2], [1 2])")]
    [InlineData("errorbar(1:3, 1:3, [.1 .1 .1])")]
    [InlineData("scatter(1:3, 1:3)")]
    public async Task EveryMarkerChartAnswersBothMatlabNames(string call)
    {
        ScriptRunResult result = await RunMatlab(
            $"h = {call};\nnames = fieldnames(get(h(1)));\n"
            + "a = any(strcmp(names, 'MarkerFaceColor')); "
            + "b = any(strcmp(names, 'MarkerEdgeColor'));");
        Succeeded(result);
        Assert.True(Truth(result, "a"), $"{call} does not answer MarkerFaceColor.");
        Assert.True(Truth(result, "b"), $"{call} does not answer MarkerEdgeColor.");
    }

    /// <summary>
    /// A chart in space reads and writes both colours the way its flat counterpart does, <c>'none'</c>
    /// included — which is the whole reason these keep a curated entry rather than being left to
    /// reflection, since no reflected colour property knows the word.
    /// </summary>
    [Theory]
    [InlineData("plot3(1:3, 1:3, 1:3)")]
    [InlineData("stem3([1 2], [1 2], [1 2])")]
    public async Task TheSpatialChartsTakeAColourAndTakeNone(string call)
    {
        ScriptRunResult result = await RunMatlab(
            $"h = {call};\nset(h, 'MarkerFaceColor', [1 0 0], 'MarkerEdgeColor', [0 1 0]);\n"
            + "f = get(h, 'MarkerFaceColor'); e = get(h, 'MarkerEdgeColor');\n"
            + "set(h, 'MarkerFaceColor', 'none'); n = get(h, 'MarkerFaceColor');");
        Succeeded(result);

        Assert.Equal([1, 0, 0], Assert.IsType<double[]>(
            Assert.Single(result.Variables, v => v.Name == "f").RawValue));
        Assert.Equal([0, 1, 0], Assert.IsType<double[]>(
            Assert.Single(result.Variables, v => v.Name == "e").RawValue));
        Assert.Equal("none", Assert.Single(result.Variables, v => v.Name == "n").RawValue);
    }

    /// <summary>The verb forms too, which were refused outright by verbs that draw markers.</summary>
    [Fact]
    public async Task TheVerbsTakeBothColoursAsOptions()
    {
        Succeeded(await RunMatlab(
            "h = plot3(1:3, 1:3, 1:3, 'Marker', 'o', "
            + "'MarkerFaceColor', 'r', 'MarkerEdgeColor', 'g');"));

        Line3DPlot drawn = Assert.Single(JG.Gca().Plots.OfType<Line3DPlot>());
        Assert.Equal(Colors.Red, drawn.MarkerFaceColor);
        Assert.Equal(Colors.Green, drawn.MarkerEdgeColor);
    }

    /// <summary>
    /// <c>stem3</c>'s <c>MarkerEdgeColor</c> outlines the markers rather than repainting the stems.
    /// It set the whole series' <c>Color</c> until M86, because the marker had no edge of its own to
    /// put a colour on — so asking to outline the heads moved the stalks.
    /// </summary>
    [Fact]
    public async Task OutliningAStem3MarkerDoesNotRepaintItsStems()
    {
        Succeeded(await RunMatlab(
            "h = stem3([1 2], [1 2], [1 2], 'MarkerEdgeColor', 'g');"));

        Stem3DPlot drawn = Assert.Single(JG.Gca().Plots.OfType<Stem3DPlot>());
        Assert.Equal(Colors.Green, drawn.MarkerEdgeColor);
        Assert.Null(drawn.Color);
    }

    // --- The wire ------------------------------------------------------------------------------------

    /// <summary>
    /// The saved keys are still <c>markerFill</c> and <c>markerEdge</c>.
    /// <para>
    /// A saved figure is a file somebody already has. Renaming a CLR property must not turn a
    /// document written yesterday into one that loads with its markers blank, and the format version
    /// is not bumped because nothing about the format changed. This pin is the point: a later tidy-up
    /// that "corrected" these keys would lose data with no error anywhere to notice it by.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSavedKeysAreStillTheOldSpellings()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        LinePlot flat = axes.AddLine([1, 2, 3], [1, 2, 3]);
        flat.MarkerFaceColor = Colors.Red;
        flat.MarkerEdgeColor = Colors.Green;

        string json = GraphFormat.Serialize(figure);
        Assert.Contains("\"markerFill\"", json, StringComparison.Ordinal);
        Assert.Contains("\"markerEdge\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("markerFaceColor", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("markerEdgeColor", json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Both colours survive a save and a reload, on the flat kinds and the spatial ones.</summary>
    [Fact]
    public void BothColoursSurviveARoundTripOnEveryKindThatCarriesThem()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();

        LinePlot flat = axes.AddLine([1, 2, 3], [1, 2, 3]);
        flat.MarkerFaceColor = Colors.Red;
        flat.MarkerEdgeColor = Colors.Green;

        Line3DPlot space = axes.AddLine3D([1, 2], [1, 2], [1, 2]);
        space.MarkerFaceColor = Colors.Blue;
        space.MarkerEdgeColor = Colors.Yellow;

        Stem3DPlot stems = axes.AddStem3D([1, 2], [1, 2], [1, 2]);
        stems.MarkerFaceColor = Colors.Yellow;
        stems.MarkerEdgeColor = Colors.Blue;

        AxesModel back = GraphFormat.Deserialize(GraphFormat.Serialize(figure)).Axes[0];

        LinePlot flatBack = Assert.Single(back.Plots.OfType<LinePlot>());
        Assert.Equal(Colors.Red, flatBack.MarkerFaceColor);
        Assert.Equal(Colors.Green, flatBack.MarkerEdgeColor);

        Line3DPlot spaceBack = Assert.Single(back.Plots.OfType<Line3DPlot>());
        Assert.Equal(Colors.Blue, spaceBack.MarkerFaceColor);
        Assert.Equal(Colors.Yellow, spaceBack.MarkerEdgeColor);

        Stem3DPlot stemsBack = Assert.Single(back.Plots.OfType<Stem3DPlot>());
        Assert.Equal(Colors.Yellow, stemsBack.MarkerFaceColor);
        Assert.Equal(Colors.Blue, stemsBack.MarkerEdgeColor);
    }

    /// <summary>
    /// A document written before M86 loads with its markers intact. The keys never changed, so this
    /// holds by construction — and it is asserted anyway, because "by construction" is exactly the
    /// kind of claim that stops being true without anybody editing the sentence.
    /// </summary>
    [Fact]
    public void ADocumentWrittenBeforeTheRenameStillLoadsItsMarkerColours()
    {
        const string before = """
            {
              "format": "jgraph",
              "formatVersion": 6,
              "figure": {
                "axes": [
                  {
                    "plots": [
                      {
                        "type": "line",
                        "series": { "xs": [1, 2, 3], "ys": [1, 2, 3], "count": 0 },
                        "marker": "Circle",
                        "markerFill": "#FF0000",
                        "markerEdge": "#00FF00"
                      }
                    ]
                  }
                ]
              }
            }
            """;

        LinePlot loaded = Assert.Single(
            GraphFormat.Deserialize(before).Axes[0].Plots.OfType<LinePlot>());
        Assert.Equal(Color.FromRgb(0xFF, 0x00, 0x00), loaded.MarkerFaceColor);
        Assert.Equal(Color.FromRgb(0x00, 0xFF, 0x00), loaded.MarkerEdgeColor);
    }
}
