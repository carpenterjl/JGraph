using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V6 (ADR 0167), tenth sub-stage: the graphics verbs the probes needed. <c>guidata</c> stores one
/// value on the figure an object belongs to and hands a copy back (appendix A #102);
/// <c>axes(parent)</c> and <c>axes('Parent', f)</c> make an axes in the figure named (#103);
/// <c>isvalid</c> answers for a graphics handle and for a handle object, and <c>delete</c> on a handle
/// object ends it for every alias (#104).
/// </summary>
/// <remarks>
/// The parity fixture <c>handle_lifetime</c> holds R2025b's answers; these pin the roads the
/// sub-stage touched, one assertion a road, so a regression names itself.
/// </remarks>
[Collection("JG facade")]
public class GraphicsVerbsM167Tests : IDisposable
{
    /// <summary>The parity fixtures' helpers folder, for <c>HandleHolder</c>, <c>ValueBox</c> and <c>DeleteLogger</c>.</summary>
    private static readonly string Helpers = Path.Combine(AppContext.BaseDirectory, "MatlabParity", "fixtures", "helpers");

    private readonly RecordingScriptOutput _output = new();

    public GraphicsVerbsM167Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    /// <summary>Runs the script the way a fixture runs: the helpers folder on the function path.</summary>
    private ScriptRunResult Run(string code)
    {
        var context = new ScriptContext(_output, (_, _) => { }, Path.GetTempPath(), resolvePath: null, figureFiles: new TestFigureFiles());
        return JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab, searchFolders: [Helpers]);
    }

    private string RunAndRead(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim();
    }

    private string RunExpectingError(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.False(result.Success);
        return result.Message ?? "";
    }

    private const string Fig = "f = figure('Visible', 'off'); ";

    // --- guidata (#102) ---------------------------------------------------------------------------

    [Theory]
    [InlineData("g.a = 1; guidata(f, g); g.a = 2; t = guidata(f); disp(t.a);", "1")]
    [InlineData("g.a = 1; guidata(f, g); t = guidata(f); t.a = 3; u = guidata(f); fprintf('%d %d\\n', u.a, t.a);", "1 3")]
    [InlineData("p = plot(1:3); g.a = 5; guidata(p, g); t = guidata(f); disp(t.a);", "5")]
    [InlineData("ax = axes(f); g.a = 6; guidata(f, g); t = guidata(ax); disp(t.a);", "6")]
    [InlineData("t = guidata(f); fprintf('%s %s\\n', mat2str(t), class(t));", "[] double")]
    [InlineData("g.a = 1; guidata(f, g); h.b = 2; guidata(f, h); disp(strjoin(fieldnames(guidata(f))', ','));", "b")]
    [InlineData("v = [1 2 3]; guidata(f, v); v(1) = 9; disp(mat2str(guidata(f)));", "[1 2 3]")]
    [InlineData("o = HandleHolder; o.data = [1 2 3]; guidata(f, o); o.data(1) = 7; t = guidata(f); disp(mat2str(t.data));", "[7 2 3]")]
    public void GuidataStoresACopyOnTheFigure(string code, string expected)
    {
        Assert.Equal(expected, RunAndRead(Fig + code));
    }

    [Theory]
    [InlineData("g.a = 1; guidata(1234567.5, g);")]
    [InlineData("g.a = 1; guidata(f, g); close(f); t = guidata(f);")]
    public void GuidataRefusesWhatIsNotInAFigure(string code)
    {
        Assert.Contains("Object must be a figure or one of its child objects.", RunExpectingError(Fig + code));
    }

    // --- axes(parent) (#103) ----------------------------------------------------------------------

    [Theory]
    [InlineData("ax = axes(f); lim = [0 10]; ax.XLim = lim; lim(1) = -5; fprintf('%s %d\\n', mat2str(ax.XLim), ax.Parent == f);", "[0 10] 1")]
    [InlineData("ax = axes(f, 'XLim', [0 5], 'Tag', 'left'); fprintf('%s %s %d\\n', mat2str(ax.XLim), ax.Tag, ax.Parent == f);", "[0 5] left 1")]
    [InlineData("ax1 = axes(f); ax2 = axes(f); fprintf('%d %d %d\\n', numel(findobj(f, 'Type', 'axes')), ax1 == ax2, gca == ax2);", "2 0 1")]
    [InlineData("ax = axes('Parent', f, 'Tag', 'nv'); fprintf('%d %s\\n', ax.Parent == f, ax.Tag);", "1 nv")]
    [InlineData("ax1 = axes(f); ax2 = axes(f); axes(ax1); fprintf('%d %d\\n', gca == ax1, numel(findobj(f, 'Type', 'axes')));", "1 2")]
    public void AxesTakesTheFigureToMakeItIn(string code, string expected)
    {
        Assert.Equal(expected, RunAndRead(Fig + code));
    }

    [Fact]
    public void AxesInAnotherFigureLeavesTheCurrentFigureAlone()
    {
        string answer = RunAndRead("""
            f1 = figure('Visible', 'off'); f2 = figure('Visible', 'off'); figure(f1);
            ax = axes(f2);
            fprintf('%d %d %d\n', ax.Parent == f2, gcf == f2, gca == ax);
            figure(f2);
            fprintf('%d\n', gca == ax);
            """);
        Assert.Equal("1 0 0\n1", answer.Replace("\r\n", "\n"));
    }

    [Fact]
    public void AxesRefusesAParentThatIsNotAFigure()
    {
        Assert.Contains("Axes cannot be a child of Line.", RunExpectingError(Fig + "p = plot(1:3); ax = axes(p);"));
    }

    // --- isvalid (#104) ---------------------------------------------------------------------------

    [Theory]
    [InlineData("p = plot(1:2); fprintf('%d %d\\n', isvalid(p), isvalid(f));", "1 1")]
    [InlineData("q = plot(1:2); r = q; delete(q); fprintf('%d %d\\n', isvalid(r), isvalid(q));", "0 0")]
    [InlineData("p1 = plot(1:2); hold on; p2 = plot(3:4); h = [p1 p2]; delete(p2); fprintf('%s %s\\n', mat2str(isvalid(h)), class(isvalid(h)));", "[true false] logical")]
    [InlineData("p = plot(1:2); close(f); fprintf('%d %d\\n', isvalid(f), isvalid(p));", "0 0")]
    [InlineData("p = plot(1:2); ax = gca; clf(f); fprintf('%d %d %d\\n', isvalid(p), isvalid(ax), isvalid(f));", "0 0 1")]
    [InlineData("st.p = plot(1:2); delete(st.p); fprintf('%d\\n', isvalid(st.p));", "0")]
    [InlineData("r = isvalid(gobjects(0)); fprintf('%s %s\\n', mat2str(size(r)), class(r));", "[0 0] logical")]
    // A handle is a number here (ADR 0167's divergence): a number that names nothing is a dead handle, not a refusal.
    [InlineData("fprintf('%d\\n', isvalid(5));", "0")]
    public void IsvalidAnswersForAGraphicsHandle(string code, string expected)
    {
        Assert.Equal(expected, RunAndRead(Fig + code));
    }

    [Theory]
    [InlineData("o = HandleHolder; fprintf('%d %s\\n', isvalid(o), class(isvalid(o)));", "1 logical")]
    [InlineData("o = HandleHolder; delete(o); fprintf('%d\\n', isvalid(o));", "0")]
    [InlineData("o = HandleHolder; o2 = o; delete(o); fprintf('%d %d\\n', isvalid(o2), isvalid(o));", "0 0")]
    [InlineData("o = HandleHolder; delete(o); delete(o); fprintf('%d\\n', isvalid(o));", "0")]
    [InlineData("o = HandleHolder; o.delete(); fprintf('%d\\n', isvalid(o));", "0")]
    [InlineData("c = {HandleHolder}; delete(c{1}); fprintf('%d\\n', isvalid(c{1}));", "0")]
    public void IsvalidAnswersForAHandleObject(string code, string expected)
    {
        Assert.Equal(expected, RunAndRead(code));
    }

    [Fact]
    public void DeleteRunsTheDestructorOnceAndThenTheObjectIsGone()
    {
        Assert.Equal("D;after; 0", RunAndRead("""
            global vlog_text
            vlog_text = '';
            dl = DeleteLogger('D');
            delete(dl);
            delete(dl);
            vlog('after');
            fprintf('%s %d\n', vlog_text, isvalid(dl));
            """));
    }

    [Theory]
    [InlineData("o = HandleHolder; o.data = 1; delete(o); disp(o.data);")]
    [InlineData("o = HandleHolder; delete(o); o.data = 5;")]
    [InlineData("o = HandleHolder; delete(o); o.bump();")]
    [InlineData("o = HandleHolder; o2 = o; delete(o); o2.data = 5;")]
    [InlineData("s.o = HandleHolder; delete(s.o); s.o.data = 5;")]
    public void ADeletedObjectRefusesEveryDot(string code)
    {
        Assert.Contains("Invalid or deleted object.", RunExpectingError(code));
    }

    [Theory]
    [InlineData("v = ValueBox; isvalid(v);", "Undefined function 'isvalid' for input arguments of type 'ValueBox'.")]
    [InlineData("isvalid('h');", "Undefined function 'isvalid' for input arguments of type 'char'.")]
    [InlineData("isvalid({1});", "Undefined function 'isvalid' for input arguments of type 'cell'.")]
    [InlineData("v = ValueBox; delete(v);", "Undefined function 'delete' for input arguments of type 'ValueBox'.")]
    public void IsvalidAndDeleteAreForHandlesOnly(string code, string expected)
    {
        Assert.Contains(expected, RunExpectingError(code));
    }
}
