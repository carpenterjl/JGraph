using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open items 136 and 137 (ADR 0255): every graphics maker takes any property of what it makes as a
/// trailing option, and a group or a transform in place of the axes, as R2025b's do (probes
/// oi136_options, oi136_order, oi136_unheld, oi136_create).
/// </summary>
[Collection("JG facade")]
public class MakerOptionsTests : IDisposable
{
    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public MakerOptionsTests()
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

    private ScriptRunResult Run(string code, IScriptEngine? engine = null) =>
        (engine ?? _engine).RunAsync(
            code,
            new ScriptContext(
                _output,
                (_, _) => { },
                _directory,
                resolvePath: null,
                figureFiles: new TestFigureFiles()),
            CancellationToken.None).GetAwaiter().GetResult();

    private string Printed(ScriptRunResult result) => result.Message + _output.ErrorText;

    private string RunAndRead(string code, IScriptEngine? engine = null)
    {
        ScriptRunResult result = Run(code, engine);
        Assert.True(result.Success, Printed(result));
        return _output.NormalText;
    }

    private string Error(string code, IScriptEngine? engine = null)
    {
        ScriptRunResult result = Run(code, engine);
        Assert.False(result.Success, "expected a refusal, got: " + _output.NormalText);
        return Printed(result);
    }

    // --- 136: any property of what is made ------------------------------------------------------------

    [Theory]
    [InlineData("plot(ax, x, y, 'r-o', o{:})")]
    [InlineData("plot(ax, y, o{:})")]
    [InlineData("plot(ax, x, y, 'r', x, y, 'b', o{:})")]
    [InlineData("plot3(ax, x, y, y, 'k:', o{:})")]
    [InlineData("semilogy(ax, x, y, o{:})")]
    [InlineData("stairs(ax, x, y, 'r', o{:})")]
    [InlineData("stem(ax, x, y, 'filled', o{:})")]
    [InlineData("stem3(ax, x, y, y, o{:})")]
    [InlineData("scatter(ax, x, y, 20, 'r', o{:})")]
    [InlineData("scatter3(ax, x, y, y, o{:})")]
    [InlineData("bar(ax, [y; y], 'stacked', o{:})")]
    [InlineData("barh(ax, y, o{:})")]
    [InlineData("area(ax, x, y, o{:})")]
    [InlineData("errorbar(ax, x, y, y, o{:})")]
    [InlineData("histogram(ax, [1 2 2 3], o{:})")]
    [InlineData("fill(ax, x, y, 'r', o{:})")]
    [InlineData("patch(ax, x, y, 'r', o{:})")]
    [InlineData("rectangle(ax, 'Position', [0 0 1 1], o{:})")]
    [InlineData("light(ax, o{:})")]
    [InlineData("quiver(ax, x, y, y, y, o{:})")]
    [InlineData("pcolor(ax, magic(3), o{:})")]
    [InlineData("hggroup(ax, o{:})")]
    public void EveryMakerTakesAnyPropertyOfWhatItMakes(string call)
    {
        Assert.Equal("t 7 t\n", RunAndRead($$"""
            f = figure('Visible', 'off'); ax = axes(f); hold(ax, 'on');
            x = 1:3; y = [2 1 3]; o = {'Tag', 't', 'UserData', 7};
            h = {{call}};
            fprintf('%s %d %s\n', get(h(1), 'Tag'), get(h(1), 'UserData'), get(h(end), 'Tag'));
            """));
    }

    [Fact]
    public void TheValuesArriveAndTheMakersOwnStayItsOwn()
    {
        Assert.Equal("off 0.3 4 w pt 3\n", RunAndRead("""
            f = figure('Visible', 'off'); ax = axes(f); hold(ax, 'on');
            a = plot(ax, 1:3, 'Visible', 'off');
            b = bar(ax, 1:3, 'BarWidth', 0.2, 'Tag', 'w', 'BarWidth', 0.3);
            c = plot(ax, 1:3, 'LineWidth', 3, 'Tag', 'w', 'LineWidth', 4);
            d = plot(ax, 1:3, 'Ta', 'pt', 'UserD', 3);
            fprintf('%s %g %g %s %s %d\n', char(a.Visible), b.BarWidth, c.LineWidth, c.Tag, d.Tag, d.UserData);
            """));
    }

    [Theory]
    [InlineData("plot(ax, 1:3, 'Foo', 1)", "Unrecognized property Foo for class Line.")]
    [InlineData("plot(ax, 1:3, 'Mark', 1)", "Unrecognized property Mark for class Line.")]
    [InlineData("line(ax, 1:3, 1:3, 'Foo', 1)", "Unrecognized property Foo for class Line.")]
    [InlineData("bar(ax, 1:3, 'Foo', 1)", "Unrecognized property Foo for class Bar.")]
    [InlineData("scatter(ax, 1:3, 1:3, 'Tag', 'ok', 'Foo', 1)", "Unrecognized property Foo for class Scatter.")]
    [InlineData("rectangle(ax, 'Foo', 1)", "Unrecognized property Foo for class Rectangle.")]
    [InlineData("light(ax, 'Foo', 1)", "Unrecognized property Foo for class Light.")]
    public void AnUnknownNameIsRefusedInR2025bsWordsAndLeavesNothingMade(string call, string words)
    {
        Assert.Equal($"{words}\n1\n", RunAndRead($$"""
            f = figure('Visible', 'off'); ax = axes(f); hold(ax, 'on');
            plot(ax, 1:3);
            try, {{call}}; catch e, disp(e.message); end
            disp(numel(ax.Children));
            """));
    }

    [Fact]
    public void AWordALineSpecCannotBeIsRefused()
    {
        Assert.Contains("Invalid color, marker, or line style.", Error("plot(1:3, 1:3, 'Tag');"));
    }

    [Fact]
    public void CreateFcnRunsAfterEveryOtherOption()
    {
        Assert.Equal("after 5\nbefore\nlineafter\n", RunAndRead("""
            f = figure('Visible', 'off'); ax = axes(f); hold(ax, 'on');
            plot(ax, 1:3, 'CreateFcn', @(h, e) fprintf('%s %d\n', h.Tag, h.UserData), 'Tag', 'after', 'UserData', 5);
            bar(ax, 1:3, 'Tag', 'before', 'CreateFcn', @(h, e) disp(h.Tag));
            line(ax, 1:3, 1:3, 'CreateFcn', @(h, e) disp(h.Tag), 'Tag', 'lineafter');
            """));
    }

    [Fact]
    public void AJgsScriptTakesWholePropertyNamesAndKeepsItsOwnRefusal()
    {
        var jgs = new JgsScriptEngine();
        Assert.Equal("t\n", RunAndRead("""
            let h = bar([1, 2, 3], 'Tag', 't');
            disp(get(h, 'Tag'));
            """, jgs));
        Assert.Contains("bar has no option 'Foo'", Error("bar([1, 2, 3], 'Foo', 1);", jgs));
    }

    // --- 137: a group or a transform in place of the axes ---------------------------------------------

    [Theory]
    [InlineData("line(g, [0 1], [1 0])")]
    [InlineData("line(g, 'XData', [0 1], 'YData', [1 0])")]
    [InlineData("text(g, 1, 2, 'hi')")]
    [InlineData("patch(g, 1:3, [2 1 3], 'r')")]
    [InlineData("surface(g, [1 2; 3 4])")]
    [InlineData("rectangle(g, 'Position', [0 0 1 1])")]
    [InlineData("image(g, magic(3))")]
    [InlineData("light(g)")]
    [InlineData("plot(g, 1:3)")]
    [InlineData("bar(g, 1:3)")]
    [InlineData("scatter(g, 1:3, 1:3)")]
    [InlineData("histogram(g, [1 2 2])")]
    [InlineData("hggroup(g)")]
    public void AGroupFirstHoldsWhatIsMade(string call)
    {
        Assert.Equal("hggroup hgtransform 1 1\n", RunAndRead($$"""
            f = figure('Visible', 'off'); ax = axes(f);
            g = hggroup(ax);
            h = {{call}};
            first = get(get(h, 'Parent'), 'Type');
            g = hgtransform(ax);
            h = {{call}};
            fprintf('%s %s %d %d\n', first, get(get(h, 'Parent'), 'Type'), numel(g.Children), isequal(get(h, 'Parent'), g));
            """));
    }

    [Fact]
    public void AGroupFirstDrawsAsThoughTheAxesWereHeld()
    {
        Assert.Equal("1 1 replace\n", RunAndRead("""
            f = figure('Visible', 'off'); ax = axes(f);
            plot(ax, 1:3, 'Tag', 'before');
            g = hggroup(ax);
            plot(g, 3:-1:1);
            fprintf('%d %d %s\n', numel(findobj(ax, 'Tag', 'before')), isvalid(g), char(ax.NextPlot));
            """));
    }

    [Fact]
    public void ParentAmongTheOptionsPlacesWhatIsMade()
    {
        Assert.Equal("hggroup hggroup axes 1 1\n", RunAndRead("""
            f = figure('Visible', 'off'); ax = axes(f);
            plot(ax, 1:3, 'Tag', 'before');
            g = hggroup(ax);
            a = plot(1:3, 'Parent', g);
            b = bar(1:3, 'Parent', g);
            f2 = figure('Visible', 'off'); ax2 = axes(f2);
            c = plot(ax, 1:3, 'Parent', ax2);
            fprintf('%s %s %s %d %d\n', a.Parent.Type, b.Parent.Type, c.Parent.Type, isequal(c.Parent, ax2), ...
                numel(findobj(ax, 'Tag', 'before')));
            """));
    }

    [Fact]
    public void AGroupsChildrenAreWhatWasMadeInItNewestFirst()
    {
        Assert.Equal("fourth third second first\nfourth third first axes\nfourth third|first|third\n", RunAndRead("""
            f = figure('Visible', 'off'); ax = axes(f); hold(ax, 'on');
            g = hggroup(ax);
            a = line(g, 1:3, 1:3, 'Tag', 'first');
            b = text(g, 1, 1, 'b', 'Tag', 'second');
            c = hggroup(g, 'Tag', 'third');
            light(g, 'Tag', 'fourth');
            disp(strjoin(get(g.Children, 'Tag')', ' '));
            set(b, 'Parent', ax);
            fprintf('%s %s\n', strjoin(get(g.Children, 'Tag')', ' '), b.Parent.Type);
            set(a, 'Parent', c);
            fprintf('%s|%s|%s\n', strjoin(get(g.Children, 'Tag')', ' '), get(c.Children, 'Tag'), a.Parent.Tag);
            """));
    }

    [Fact]
    public void ATextInATransformMovesWithIt()
    {
        // The members' own coordinates move, as for every member here (the group's recorded limit).
        Assert.Equal("11 2\n", RunAndRead("""
            f = figure('Visible', 'off'); ax = axes(f);
            t = hgtransform(ax);
            h = text(t, 1, 2, 'hi');
            set(t, 'Matrix', makehgtform('translate', [10 0 0]));
            p = get(h, 'Position');
            fprintf('%g %g\n', p(1), p(2));
            """));
    }
}
