using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M84: the six names that stood on the graphics exclusion list as "app building".
/// <para>
/// The argument for taking them off is the one <c>docs/matlab-builtin-coverage.md</c> already makes
/// twice: an exclusion is a decision, and a decision whose grounds have gone is not a decision any
/// more. The grounds were that these describe an application rather than a figure — but M71 built
/// <c>uicontextmenu</c> and <c>uimenu</c>, M75 made every <c>Paper*</c> property real and said they
/// were waiting for something that printed, and M80 put a strip of buttons over an axes.
/// </para>
/// <para>
/// Five of the six want a window, and the fixture that matters most here is the one that pins their
/// answer without one: a refusal that names the non-interactive verb which does the job. A batch run
/// that opened a modal dialog would hang the stress gate, and nothing in it could say so.
/// </para>
/// </summary>
[Collection("JG facade")]
public class MatlabM84DialogTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public MatlabM84DialogTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(
            code, new ScriptContext(_output, static (_, _) => { }), default);

    private static object Scalar(ScriptRunResult result, string name)
    {
        object? raw = Assert.Single(result.Variables, v => v.Name == name).RawValue;
        return raw switch
        {
            double[] { Length: 1 } numbers => numbers[0],
            bool[] { Length: 1 } flags => flags[0],
            _ => raw!,
        };
    }

    private static double Number(ScriptRunResult result, string name) =>
        Assert.IsType<double>(Scalar(result, name));

    private static string Text(ScriptRunResult result, string name) =>
        Assert.IsType<string>(Scalar(result, name));

    private static double[] Row(ScriptRunResult result, string name) =>
        Assert.IsType<double[]>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    private static void Succeeded(ScriptRunResult result) =>
        Assert.True(result.Success, result.Message);

    private async Task RefusesEach(params (string Code, string Fragment)[] cases)
    {
        foreach ((string code, string fragment) in cases)
        {
            ScriptRunResult result = await RunMatlab(code);
            Assert.False(result.Success, $"expected a refusal from: {code}");
            Assert.Contains(fragment, result.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    // --- The five that want a window ---------------------------------------------------------------

    /// <summary>
    /// Each refuses by name and says which verb does the same job without a window — M60's fourth
    /// answer for a verb that wants one, and what keeps a batch run free of a modal dialog.
    /// </summary>
    [Fact]
    public async Task WithoutAWindowEachDialogNamesTheVerbThatNeedsNone() =>
        await RefusesEach(
            ("plot(1:3); printdlg(gcf);", "print(fig, file, '-dpng')"),
            ("plot(1:3); printpreview(gcf);", "exportgraphics or print"),
            ("plot(1:3); pagesetupdlg(gcf);", "PaperType"),
            ("plot(1:3); exportsetupdlg(gcf);", "Resolution"),
            ("plot(1:3); exportapp(gcf, 'x.png');", "exportgraphics writes the figure itself"));

    /// <summary>And each says it opens a window, rather than saying it is not implemented.</summary>
    [Fact]
    public async Task TheRefusalSaysWhatIsMissingRatherThanThatTheVerbIs() =>
        await RefusesEach(
            ("plot(1:3); printdlg(gcf);", "opens a window"),
            ("plot(1:3); printdlg('-setup', gcf);", "opens a window"),
            ("plot(1:3); printpreview;", "opens a window"));

    // --- uiaxes -------------------------------------------------------------------------------------

    /// <summary>
    /// A <c>uiaxes</c> is an axes with the app-building defaults. MATLAB documents it as its own class
    /// differing from Axes by exactly one property, and its <c>Type</c> is still <c>'axes'</c> — which
    /// is MATLAB's own answer and the reason this needed no new object.
    /// </summary>
    [Fact]
    public async Task UiaxesIsAnAxesWithTheAppBuildingDefaults()
    {
        ScriptRunResult result = await RunMatlab(
            "ax = uiaxes; a = get(ax, 'Type'); b = numel(fieldnames(get(ax))); "
            + "c = get(ax, 'BackgroundColor'); d = get(get(ax, 'Toolbar'), 'Visible');");
        Succeeded(result);
        Assert.Equal("axes", Text(result, "a"));

        // The documented UIAxes table is Axes' 147 plus BackgroundColor; this build answers a superset,
        // as it does for a plain axes, so the check is that it reaches at least the documented count.
        Assert.True(Number(result, "b") >= 148, "a uiaxes should answer at least its documented names");
        Assert.Equal([1.0, 1.0, 1.0], Row(result, "c"));
        Assert.Equal("on", Text(result, "d"));
    }

    /// <summary>
    /// <c>ax = uiaxes</c> with no parentheses is the form every app-building script writes, so the
    /// bare name has to make the axes rather than hand back the verb that would — the rule
    /// <c>bubblesize</c> wrote and <c>nexttile</c> paid for again in M80.
    /// </summary>
    [Fact]
    public async Task TheBareNameMakesTheAxesRatherThanBindingTheVerb()
    {
        ScriptRunResult result = await RunMatlab("ax = uiaxes; a = get(ax, 'Type');");
        Succeeded(result);
        Assert.Equal("axes", Text(result, "a"));
    }

    [Fact]
    public async Task BackgroundColorReadsAndWrites()
    {
        ScriptRunResult result = await RunMatlab(
            "ax = uiaxes; set(ax, 'BackgroundColor', [1 0 0]); a = get(ax, 'BackgroundColor'); "
            + "bx = uiaxes('BackgroundColor', [0 0 1]); b = get(bx, 'BackgroundColor');");
        Succeeded(result);
        Assert.Equal([1.0, 0.0, 0.0], Row(result, "a"));
        Assert.Equal([0.0, 0.0, 1.0], Row(result, "b"));
    }

    /// <summary>
    /// The fill is on every axes and unset by default, which is what makes a plain axes draw exactly
    /// as it always has. What <c>uiaxes</c> gives it is a default, not a property.
    /// </summary>
    [Fact]
    public async Task APlainAxesReadsTheNameAndLeavesTheFillUnset()
    {
        ScriptRunResult result = await RunMatlab(
            "plot(1:3); a = get(gca, 'BackgroundColor'); b = get(gca, 'Color');");
        Succeeded(result);
        Assert.Equal(Row(result, "b"), Row(result, "a"));
    }

    // --- The export preset ---------------------------------------------------------------------------

    /// <summary>
    /// The preset stands in only where the caller said nothing, and it lives on the figure. The pixel
    /// proof that an export actually reads it is in <c>stess_56.m</c>, because it needs a host that can
    /// write a file; what is checked here is that a fresh figure's preset has nothing to say, which is
    /// what makes every export written before M84 export exactly as it did.
    /// </summary>
    [Fact]
    public void AFreshFiguresExportPresetHasNothingToSay()
    {
        var figure = new JGraph.Core.Model.FigureModel();
        Assert.True(figure.ExportSetup.IsEmpty);
        Assert.Null(figure.ExportSetup.Resolution);
        Assert.Null(figure.ExportSetup.Size);
        Assert.Null(figure.ExportSetup.Background);
    }
}
