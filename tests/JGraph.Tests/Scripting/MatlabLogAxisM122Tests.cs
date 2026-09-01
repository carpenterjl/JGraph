using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// <c>semilogx</c>, <c>semilogy</c> and <c>loglog</c> after they were made <c>plot</c> with a ruler
/// changed afterwards (M122).
/// </summary>
/// <remarks>
/// These three used to read their own arguments and read only three of them, so the form the
/// documentation opens with — a line spec followed by a name/value pair — was refused for having
/// five. The tests below are mostly about what they now <em>accept</em>, because that is what
/// changed; the two that check the ruler are the guard that inheriting plot's grammar did not lose
/// the one thing these verbs are for.
/// </remarks>
[Collection("JG facade")]
public class MatlabLogAxisM122Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabLogAxisM122Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptRunResult Run(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        return JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
    }

    private string RunAsserting(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim();
    }

    private const string Readings = "xv = (0:0.1:1) + 1; yv = abs(sin(xv)) + 1;\n";

    [Theory]
    [InlineData("semilogy(xv, yv, '-o', 'LineWidth', 1.2)")]
    [InlineData("semilogx(xv, yv, '-o', 'LineWidth', 1.2)")]
    [InlineData("loglog(xv, yv, '-o', 'LineWidth', 1.2)")]
    [InlineData("semilogy(yv)")]
    [InlineData("semilogy(yv, 'r--')")]
    [InlineData("loglog(xv, yv, 'g:')")]
    [InlineData("loglog(xv, yv, xv, yv + 1)")]
    [InlineData("semilogy(xv, yv, 'Color', [1 0 0])")]
    [InlineData("semilogx(xv, yv, xv, yv + 1, '-o')")]
    public void EveryFormPlotTakesIsTakenHereToo(string call) =>
        Assert.True(Run(Readings + call + ";").Success, call);

    [Fact]
    public void TheRulerIsLogarithmicOnWhicheverAxisTheVerbIsNamedFor()
    {
        string scales = RunAsserting(Readings + """
            semilogy(xv, yv);
            a = get(gca, 'XScale'); b = get(gca, 'YScale');
            clf; semilogx(xv, yv);
            c = get(gca, 'XScale'); d = get(gca, 'YScale');
            clf; loglog(xv, yv);
            e = get(gca, 'XScale'); f = get(gca, 'YScale');
            fprintf('%s %s %s %s %s %s', a, b, c, d, e, f);
            """);

        Assert.Equal("linear log log linear log log", scales);
    }

    /// <summary>
    /// A call with several groups draws several lines and hands back a handle for each, which is
    /// <c>plot</c>'s behaviour and was not these verbs' before: they answered one handle whatever
    /// they were given, because they could only be given one line.
    /// </summary>
    [Fact]
    public void RepeatedGroupsDrawALinePerGroup()
    {
        string counts = RunAsserting(Readings + """
            h = loglog(xv, yv, xv, yv + 1, xv, yv + 2);
            fprintf('%d %d', numel(h), numel(get(gca, 'Children')));
            """);

        Assert.Equal("3 3", counts);
    }

    /// <summary>
    /// The ruler is set after the drawing rather than before, because a verb drawn with <c>hold</c>
    /// off clears the axes and would take the ruler with it.
    /// </summary>
    [Fact]
    public void TheRulerSurvivesTheClearingADrawWithoutHoldDoes()
    {
        string scale = RunAsserting(Readings + """
            plot(xv, yv);
            semilogy(xv, yv);
            fprintf('%s', get(gca, 'YScale'));
            """);

        Assert.Equal("log", scale);
    }

    /// <summary>
    /// The implicit x stays 1-based, which is what these three have always counted from. A
    /// logarithmic x axis has no room for a sample at zero, so <c>plot</c>'s JGS numbering would be
    /// the wrong inheritance to take.
    /// </summary>
    [Fact]
    public void ValuesAloneAreCountedFromOne()
    {
        JG.Reset();
        ScriptRunResult result = Run("yv = [2 4 8];\nsemilogy(yv);");
        Assert.True(result.Success, result.Message);

        AxesModel axes = JG.Gca();
        Assert.Single(axes.Plots);
        Assert.Equal(1, axes.Plots[0].GetXDataBounds().Min, 12);
        Assert.Equal(3, axes.Plots[0].GetXDataBounds().Max, 12);
    }
}
