using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M79: the table a marker chart is drawn from, the patch a pie is drawn as, a reference line's
/// label font, and the two colours a box chart takes from its seat.
/// <para>
/// Every form here was run at the CLI before it was written down. The pixel proofs live in
/// stess_51.m; these tests pin what the properties mean. The refusals are pinned beside the
/// capabilities, because a ceiling nobody checks reads the same as an oversight.
/// </para>
/// </summary>
[Collection("JG facade")]
public class MatlabM79TablePiePropertyTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public MatlabM79TablePiePropertyTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(
            code, new ScriptContext(_output, static (_, _) => { }), default);

    private static double Number(ScriptRunResult result, string name) =>
        Assert.IsType<double>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    private static double[] Row(ScriptRunResult result, string name) =>
        Assert.IsType<double[]>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    private static string Text(ScriptRunResult result, string name) =>
        Assert.IsType<string>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    private static void Succeeded(ScriptRunResult result) =>
        Assert.True(result.Success, result.Message);

    private async Task Refuses(string code, string fragment)
    {
        ScriptRunResult result = await RunMatlab(code);
        Assert.False(result.Success, $"expected a refusal from: {code}");
        Assert.Contains(fragment, result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Several refusals in a row, one after another. Deliberately not <c>Task.WhenAll</c>: the facade
    /// these scripts run against is one static figure stack, so two scripts at once are two scripts
    /// editing the same figure — which passes alone and fails beside its neighbours.
    /// </summary>
    private async Task RefusesEach(params (string Code, string Fragment)[] cases)
    {
        foreach ((string code, string fragment) in cases)
        {
            await Refuses(code, fragment);
        }
    }

    /// <summary>Four columns, so a channel can be moved from one to another and be seen to move.</summary>
    private const string Sample = "t = table([1;2;3], [4;5;6], [10;20;30], [7;8;9]);\n";

    // --- The table a chart is drawn from ---------------------------------------------------------

    /// <summary>
    /// The point of the whole family: naming a different variable re-reads the table and redraws the
    /// chart. A name that answered without doing this would be a chart telling a script something it
    /// cannot act on, which is why M77 declined to answer it at all.
    /// </summary>
    [Fact]
    public async Task NamingADifferentVariableRedrawsTheChartFromTheTable()
    {
        ScriptRunResult result = await RunMatlab(Sample + """
            s = scatter(t, 'Var1', 'Var2');
            before = get(s, 'YData');
            xvar = get(s, 'XVariable');
            set(s, 'YVariable', 'Var3');
            after = get(s, 'YData');
            yvar = get(s, 'YVariable');
            """);

        Succeeded(result);
        Assert.Equal("Var1", Text(result, "xvar"));
        Assert.Equal("Var3", Text(result, "yvar"));
        Assert.Equal([4, 5, 6], Row(result, "before"));
        Assert.Equal([10, 20, 30], Row(result, "after"));
    }

    /// <summary>
    /// The optional channels are the ones a variable can be taken off as well as put on, and naming
    /// nothing is how a script takes one off — there is no other word for "stop reading this".
    /// </summary>
    [Fact]
    public async Task AnOptionalChannelIsFedByNamingOneAndClearedByNamingNothing()
    {
        ScriptRunResult result = await RunMatlab(Sample + """
            s = scatter(t, 'Var1', 'Var2');
            set(s, 'SizeVariable', 'Var3');
            sizes = get(s, 'SizeData');
            set(s, 'ColorVariable', 'Var4');
            colours = get(s, 'CData');
            set(s, 'ColorVariable', '');
            cleared = numel(get(s, 'CData'));
            """);

        Succeeded(result);
        Assert.Equal([10, 20, 30], Row(result, "sizes"));
        Assert.Equal([7, 8, 9], Row(result, "colours"));
        Assert.Equal(0, Number(result, "cleared"));
    }

    /// <summary>
    /// Replacing the table redraws from the new one. This is the write that has to go through the
    /// pair — both positions at once — because a shorter table changes the length of the series, and
    /// writing one coordinate at a time is refused exactly then.
    /// </summary>
    [Fact]
    public async Task ReplacingTheSourceTableRedrawsFromTheNewOne()
    {
        ScriptRunResult result = await RunMatlab(Sample + """
            s = scatter(t, 'Var1', 'Var2');
            u = table([1;2], [9;9]);
            set(s, 'SourceTable', u);
            xs = get(s, 'XData');
            ys = get(s, 'YData');
            rows = height(get(s, 'SourceTable'));
            """);

        Succeeded(result);
        Assert.Equal([1, 2], Row(result, "xs"));
        Assert.Equal([9, 9], Row(result, "ys"));
        Assert.Equal(2, Number(result, "rows"));
    }

    /// <summary>Every verb with a documented table form records where its numbers came from.</summary>
    [Theory]
    [InlineData("h = bubblechart(t, 'Var1', 'Var2', 'Var3');", "SizeVariable", "Var3")]
    [InlineData("h = bubblechart(t, 'Var1', 'Var2', 'Var3', 'Var4');", "ColorVariable", "Var4")]
    [InlineData("h = swarmchart(t, 'Var1', 'Var2');", "YVariable", "Var2")]
    [InlineData("h = scatter3(t, 'Var1', 'Var2', 'Var3');", "ZVariable", "Var3")]
    [InlineData("polaraxes; h = polarscatter(t, 'Var1', 'Var2');", "RVariable", "Var2")]
    [InlineData("polaraxes; h = polarbubblechart(t, 'Var1', 'Var2', 'Var3');", "ThetaVariable", "Var1")]
    public async Task EveryTableFormRecordsTheVariableBehindEachChannel(
        string draw, string property, string expected)
    {
        ScriptRunResult result = await RunMatlab(Sample + draw + $"\nname = get(h, '{property}');");

        Succeeded(result);
        Assert.Equal(expected, Text(result, "name"));
    }

    /// <summary>
    /// A circle's two channels are the same two, under the names it uses for them. On square paper
    /// the polar spelling is refused rather than aliased — the M77 rule for <c>ThetaData</c>, applied
    /// to the variable that feeds it.
    /// </summary>
    [Fact]
    public async Task ThePolarSpellingsNameTheSameChannelsOnACircle()
    {
        ScriptRunResult result = await RunMatlab(Sample + """
            polaraxes;
            p = polarscatter(t, 'Var1', 'Var2');
            theta = get(p, 'ThetaVariable');
            set(p, 'RVariable', 'Var3');
            r = get(p, 'RVariable');
            through = get(p, 'YVariable');
            radius = get(p, 'RData');
            """);

        Succeeded(result);
        Assert.Equal("Var1", Text(result, "theta"));
        Assert.Equal("Var3", Text(result, "r"));
        Assert.Equal("Var3", Text(result, "through"));
        Assert.Equal([10, 20, 30], Row(result, "radius"));
    }

    // --- The pie, answered as the patch MATLAB draws it with --------------------------------------

    /// <summary>
    /// A pie's mesh: one face per value, and a fan of vertices in each. Reading it is how a script
    /// checks the chart it asked for, and it is the half of the patch surface that is derived rather
    /// than chosen.
    /// </summary>
    [Fact]
    public async Task APieAnswersWithTheMeshItsWedgesAreMadeOf()
    {
        ScriptRunResult result = await RunMatlab("""
            p = pie([1 2 3]);
            faces = size(get(p, 'Faces'), 1);
            vertices = size(get(p, 'Vertices'), 2);
            coordinates = numel(get(p, 'XData'));
            offPlane = sum(abs(get(p, 'ZData')));
            """);

        Succeeded(result);
        Assert.Equal(3, Number(result, "faces"));
        Assert.Equal(3, Number(result, "vertices"));
        Assert.True(Number(result, "coordinates") > 3, "a wedge is a fan, not a triangle");
        Assert.Equal(0, Number(result, "offPlane"));
    }

    /// <summary>
    /// The wedges keep what a script chose about them when the values move: the geometry is rebuilt
    /// and nothing else is. A styling that had to be set again after every write would make the
    /// property surface a thing to work around rather than a thing to use.
    /// </summary>
    [Fact]
    public async Task ThePieKeepsItsStylingWhenItsValuesChange()
    {
        ScriptRunResult result = await RunMatlab("""
            p = pie([1 2 3]);
            set(p, 'LineStyle', ':');
            set(p, 'Marker', 'o');
            set(p, 'AmbientStrength', 0.8);
            set(p, 'Values', [1 1 1 1]);
            faces = size(get(p, 'Faces'), 1);
            dash = get(p, 'LineStyle');
            marker = get(p, 'Marker');
            ambient = get(p, 'AmbientStrength');
            """);

        Succeeded(result);
        Assert.Equal(4, Number(result, "faces"));
        Assert.Equal(":", Text(result, "dash"));
        Assert.Equal("o", Text(result, "marker"));
        Assert.Equal(0.8, Number(result, "ambient"));
    }

    /// <summary>
    /// A pie's faces take a colour each out of the colormap unless a script names one, and MATLAB's
    /// word for that is <c>'flat'</c> — the reading a surface's own <c>FaceColor</c> takes here.
    /// </summary>
    [Fact]
    public async Task APiesFaceColourIsFlatUntilOneIsChosen()
    {
        ScriptRunResult result = await RunMatlab("""
            p = pie([1 2 3]);
            before = get(p, 'FaceColor');
            set(p, 'FaceColor', [1 0 0]);
            chosen = get(p, 'FaceColor');
            set(p, 'FaceColor', 'flat');
            after = get(p, 'FaceColor');
            set(p, 'FaceColor', 'none');
            hidden = get(p, 'FaceColor');
            """);

        Succeeded(result);
        Assert.Equal("flat", Text(result, "before"));
        Assert.Equal([1, 0, 0], Row(result, "chosen"));
        Assert.Equal("flat", Text(result, "after"));
        Assert.Equal("none", Text(result, "hidden"));
    }

    /// <summary>
    /// The material block a pie is lit by, and the two normals it works out rather than keeps. The
    /// normals are the patch's own answer, so a pie has them for the same reason a patch does.
    /// </summary>
    [Fact]
    public async Task APieCarriesThePatchMaterialAndItsComputedNormals()
    {
        ScriptRunResult result = await RunMatlab("""
            p = pie([1 2 3]);
            lighting = get(p, 'FaceLighting');
            ambient = get(p, 'AmbientStrength');
            diffuse = get(p, 'DiffuseStrength');
            specular = get(p, 'SpecularStrength');
            exponent = get(p, 'SpecularExponent');
            reflect = get(p, 'SpecularColorReflectance');
            normals = size(get(p, 'FaceNormals'), 1);
            mode = get(p, 'FaceNormalsMode');
            """);

        Succeeded(result);
        Assert.Equal("flat", Text(result, "lighting"));
        Assert.Equal(0.3, Number(result, "ambient"));
        Assert.Equal(0.6, Number(result, "diffuse"));
        Assert.Equal(0.9, Number(result, "specular"));
        Assert.Equal(10, Number(result, "exponent"));
        Assert.Equal(1, Number(result, "reflect"));
        Assert.Equal(3, Number(result, "normals"));
        Assert.Equal("auto", Text(result, "mode"));
    }

    // --- A reference line's label, and a box chart's two colours ----------------------------------

    /// <summary>
    /// The label had no font of its own until now: it was drawn at ten point in the line's colour and
    /// nothing could say otherwise. The block reads what is drawn before it is set, which is what
    /// lets <c>FontSize</c> answer on a line nobody has styled.
    /// </summary>
    [Fact]
    public async Task AReferenceLinesLabelCarriesItsOwnFont()
    {
        ScriptRunResult result = await RunMatlab("""
            x = xline(1, '--r', 'limit');
            size0 = get(x, 'FontSize');
            weight0 = get(x, 'FontWeight');
            set(x, 'FontSize', 16);
            set(x, 'FontWeight', 'bold');
            set(x, 'FontAngle', 'italic');
            size1 = get(x, 'FontSize');
            weight1 = get(x, 'FontWeight');
            angle1 = get(x, 'FontAngle');
            interpreter = get(x, 'Interpreter');
            """);

        Succeeded(result);
        Assert.Equal(10, Number(result, "size0"));
        Assert.Equal("normal", Text(result, "weight0"));
        Assert.Equal(16, Number(result, "size1"));
        Assert.Equal("bold", Text(result, "weight1"));
        Assert.Equal("italic", Text(result, "angle1"));
        Assert.Equal("tex", Text(result, "interpreter"));
    }

    /// <summary>The same names in the call that draws the line, which is where a script writes them.</summary>
    [Fact]
    public async Task TheReferenceLineVerbsTakeTheFontNamesToo()
    {
        ScriptRunResult result = await RunMatlab("""
            z = xline(2, 'Label', 'hi', 'FontSize', 20, 'FontWeight', 'bold', ...
                'LabelOrientation', 'horizontal', 'Interpreter', 'none');
            size1 = get(z, 'FontSize');
            weight = get(z, 'FontWeight');
            orientation = get(z, 'LabelOrientation');
            interpreter = get(z, 'Interpreter');
            """);

        Succeeded(result);
        Assert.Equal(20, Number(result, "size1"));
        Assert.Equal("bold", Text(result, "weight"));
        Assert.Equal("horizontal", Text(result, "orientation"));
        Assert.Equal("none", Text(result, "interpreter"));
    }

    /// <summary>
    /// A box chart's two colours come from its seat until a script chooses them, and both answered
    /// <c>'none'</c> on a chart that plainly draws a fill. The mode is what makes the difference
    /// visible, and the colour beside it has to be the one being drawn for the pair to mean anything.
    /// </summary>
    [Fact]
    public async Task ABoxChartSaysWhetherItsColoursAreItsSeatsOrItsOwn()
    {
        ScriptRunResult result = await RunMatlab("""
            b = boxchart([1 2 3 4 10]);
            faceMode0 = get(b, 'BoxFaceColorMode');
            seat = get(b, 'BoxFaceColor');
            set(b, 'BoxFaceColor', [1 0 0]);
            faceMode1 = get(b, 'BoxFaceColorMode');
            chosen = get(b, 'BoxFaceColor');
            set(b, 'BoxFaceColorMode', 'auto');
            faceMode2 = get(b, 'BoxFaceColorMode');
            released = get(b, 'BoxFaceColor');
            markerMode = get(b, 'MarkerColorMode');
            """);

        Succeeded(result);
        Assert.Equal("auto", Text(result, "faceMode0"));
        Assert.Equal("manual", Text(result, "faceMode1"));
        Assert.Equal("auto", Text(result, "faceMode2"));
        Assert.Equal("auto", Text(result, "markerMode"));
        Assert.Equal([1, 0, 0], Row(result, "chosen"));
        Assert.Equal(Row(result, "seat"), Row(result, "released"));
        Assert.Equal(3, Row(result, "seat").Length);
    }

    // --- What is refused, and why ----------------------------------------------------------------

    /// <summary>
    /// The refusals that are decisions rather than typos. Each names what this build does not have,
    /// or what to set instead, because a property that refused silently would read as a bug.
    /// </summary>
    [Fact]
    public Task TheCeilingsRefuseByNameAndSayWhatIsMissing() =>
        RefusesEach(
            ("s = scatter([1 2], [1 2]); set(s, 'XVariable', 'a');", "given its numbers directly"),
            ("s = scatter([1 2], [1 2]); set(s, 'ThetaVariable', 'a');", "drawn round a circle"),
            ("s = scatter([1 2], [1 2]); set(s, 'ZVariable', 'a');", "draw it with scatter3"),
            ("s = scatter3([1 2], [1 2], [1 2]); set(s, 'AlphaVariable', 'a');",
                "no per-point transparency"),
            ("p = pie([1 2 3]); set(p, 'Faces', [1 2 3]);", "set Values, Explode"),
            ("p = pie([1 2 3]); set(p, 'XData', [1 2 3]);", "set Values, Explode"),
            ("p = pie([1 2 3]); set(p, 'AmbientStrength', 5);", "from 0 through 1"),
            ("xline(1, 'Label', 'x', 'LabelOrientation', 'sideways');",
                "is 'aligned' or 'horizontal'"));

    /// <summary>
    /// A misspelled variable lists the ones the table has. The whole reason to name a variable is
    /// that the name is the table's own, so a typo is easy and worth catching where it was written.
    /// </summary>
    [Fact]
    public Task NamingAVariableTheTableDoesNotHaveListsTheOnesItDoes() =>
        Refuses(Sample + "scatter(t, 'Nope', 'Var2');", "It has Var1, Var2");

    // --- The census -----------------------------------------------------------------------------

    /// <summary>
    /// What each kind answers, counted the way the coverage probe counts it. These are the numbers
    /// the milestone claims, pinned so that a later wave cannot quietly lose one.
    /// </summary>
    [Theory]
    [InlineData("h = pie([1 2 3]);", 75)]
    [InlineData("h = scatter([1 2 3], [1 2 3]);", 68)]
    [InlineData("h = bubblechart([1 2], [1 2], [10 20]);", 68)]
    [InlineData("h = xline(1);", 44)]
    [InlineData("h = boxchart([1 2 3 4]);", 40)]
    public async Task EachKindAnswersAtLeastTheNamesThisMilestoneClaims(string build, int wanted)
    {
        ScriptRunResult result = await RunMatlab(build + "\ncount = numel(fieldnames(get(h)));");

        Succeeded(result);
        Assert.True(
            Number(result, "count") >= wanted,
            $"{build} answered {Number(result, "count")} names, wanted at least {wanted}");
    }
}
