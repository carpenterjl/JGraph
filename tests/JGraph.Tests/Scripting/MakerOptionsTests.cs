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

    // --- open items 167, 168 and 198 -----------------------------------------------------------------

    [Fact]
    public void PcolorMakesAFlatSurfaceAndLaysTheAxesFlat()
    {
        // R2025b's pcolor.m (probe pc1): a surface at height zero, its CData the colours, the view
        // flat, the box on and both limits set when the axes is not held.
        Assert.Equal("surface flat 0.129412 0.5 on 1 4 manual 0 90 on 1 4 4 1\n", RunAndRead("""
            f = figure('Visible', 'off'); ax = axes(f);
            h = pcolor(ax, magic(4));
            e = h.EdgeColor; v = ax.View;
            fprintf('%s %s %g %g %s %g %g %s %g %g %s %g %g %d %d\n', h.Type, h.FaceColor, e(1), h.LineWidth, ...
                char(h.AlignVertexCenters), ax.XLim, ax.XLimMode, v, char(ax.Box), ax.YLim, size(h.YData));
            """));
    }

    [Fact]
    public void PcolorOnAHeldAxesLeavesTheAxesAlone()
    {
        Assert.Equal("-37.5 30 auto 2\n", RunAndRead("""
            f = figure('Visible', 'off'); ax = axes(f);
            plot(ax, [0 10], [0 10]); hold(ax, 'on'); view(ax, 3);
            pcolor(ax, magic(4));
            fprintf('%g %g %s %d\n', ax.View, ax.XLimMode, numel(ax.Children));
            """));
    }

    [Theory]
    [InlineData("pcolor(ax, [1 2 3])", "MATLAB:pcolor:NonMatrixColorInput")]
    [InlineData("pcolor(ax, 1:3, 1:2, magic(3))", "MATLAB:pcolor:InputSizeMismatch")]
    [InlineData("pcolor(ax, [1 2; 3 4i])", "MATLAB:pcolor:NonRealInputs")]
    [InlineData("pcolor(ax, magic(3), magic(3))", "MATLAB:badsubscript")]
    [InlineData("pcolor(ax, magic(3), 'Foo', 1)", "MATLAB:hg:InvalidProperty")]
    public void PcolorRefusesInItsOwnWords(string call, string identifier)
    {
        Assert.Equal($"{identifier}\n0\n", RunAndRead($$"""
            f = figure('Visible', 'off'); ax = axes(f);
            try, {{call}}; catch e, disp(e.identifier); end
            disp(numel(ax.Children));
            """));
    }

    [Fact]
    public void AJgsPcolorKeepsItsHeatmap()
    {
        ScriptRunResult result = Run("pcolor([0, 5], [0, 10], [[1, 2], [3, 4]])", new JgsScriptEngine());
        Assert.True(result.Success, Printed(result));
        Assert.IsType<JGraph.Objects.ImagePlot>(JG.Gca().Plots[0]);
    }

    [Theory]
    [InlineData("title(g, 'x')", "MATLAB:Chart:UnsupportedConvenienceFunction Using title with Group is not supported.")]
    [InlineData("xlabel(t, 'x')", "MATLAB:Chart:UnsupportedConvenienceFunction Using xlabel with Transform is not supported.")]
    [InlineData("hold(g, 'on')", "MATLAB:hold:UnsupportedCurrentAxes Using hold with hggroup is not supported.")]
    [InlineData("hold(t)", "MATLAB:hold:UnsupportedCurrentAxes Using hold with hgtransform is not supported.")]
    [InlineData("axis(g, 'tight')", "MATLAB:Chart:UnsupportedConvenienceFunction Using axis with hggroup is not supported.")]
    [InlineData("grid(g, 'on')", "MATLAB:grid:FirstArgAxes First argument must be an axes handle.")]
    [InlineData("box(g, 'on')", "MATLAB:hg:InvalidProperty Unrecognized property Box for class Group.")]
    [InlineData("box(t)", "MATLAB:hg:InvalidProperty Unrecognized property Box for class matlab.graphics.primitive.Transform.")]
    [InlineData("xlim(g, [0 1])", "MATLAB:rulerFunctions:InvalidObject Invalid or deleted graphics object.")]
    [InlineData("view(g, 3)", "MATLAB:view:InvalidInputs Invalid input arguments.")]
    [InlineData("colorbar(g)", "MATLAB:colorbar:InvalidInput Unrecognized input or invalid parameter/value pair arguments.")]
    [InlineData("daspect(g)", "MATLAB:hg:set_chck:Expected3ElementVector Error setting property 'DataAspectRatio' of class 'Axes': Value must be a 3 element numeric vector")]
    [InlineData("axes(g)", "MATLAB:handle_graphics:exceptions:HandleGraphicsException Axes cannot be a child of Group.")]
    public void AnAxesVerbRefusesAGroupInItsOwnWords(string call, string words)
    {
        Assert.Equal($"{words}\n2 1\n", RunAndRead($$"""
            f = figure('Visible', 'off'); ax = axes(f); plot(ax, 1:3); hold(ax, 'on');
            g = hggroup(ax); t = hgtransform(g);
            try, {{call}}; catch e, disp([e.identifier ' ' e.message]); end
            fprintf('%d %d\n', numel(ax.Children), numel(g.Children));
            """));
    }

    [Theory]
    [InlineData("plot(ax, x, y, 'Tag', 'a', 'r')", "MATLAB:hg:InvalidProperty Unrecognized property a for class Line.")]
    [InlineData("plot(ax, x, y, 'Tag', 'a', x, y)", "MATLAB:plot:InvalidFirstInput Invalid data argument.")]
    [InlineData("plot(ax, x, y, 'r', 'b')", "MATLAB:plot:InvalidFirstInput Invalid data argument.")]
    [InlineData("plot(ax, x, y, 'LineWidth', 2, 'r')", "MATLAB:plot:DataPairsMismatch Data must be a single input of y-values or one or more pairs of x- and y-values.")]
    [InlineData("plot(ax, x, y, x)", "MATLAB:plot:DataPairsMismatch Data must be a single input of y-values or one or more pairs of x- and y-values.")]
    [InlineData("plot(ax, x, y, 'Tag', 'Color', 'r')", "MATLAB:plot:UnrecognizedLineStyleParameter Invalid color, marker, or line style.")]
    [InlineData("plot(ax, x, y, 'r', 2)", "MATLAB:hg:InvalidProperty Unrecognized property r for class Line.")]
    [InlineData("plot(ax, x, y, 'Type', 'x')", "MATLAB:plot:InvalidPropertyValue Unable to set the 'Type' property of class ''Line'' because it is read-only.")]
    [InlineData("plot3(ax, x, y, y, 'Tag', 'a', 'r')", "MATLAB:hg:InvalidProperty Unrecognized property a for class Line.")]
    [InlineData("line(ax, x, y, 'Type', 'x')", "MATLAB:class:SetProhibited Unable to set the 'Type' property of class ''Line'' because it is read-only.")]
    public void PlotReadsAMalformedCallInItsOwnWords(string call, string words)
    {
        Assert.Equal($"{words}\n", RunAndRead($$"""
            f = figure('Visible', 'off'); ax = axes(f); hold(ax, 'on'); x = 1:3; y = [2 1 3];
            try, {{call}}; catch e, disp([e.identifier ' ' e.message]); end
            """));
    }

    [Theory]
    [InlineData("plot(ax, x, y, 'r', 'Tag', 'a')", "1 a")]
    [InlineData("plot(ax, x, y, 'Tag', 'r')", "1 r")]
    [InlineData("plot(ax, x, y, 'r', x, y, 'b', 'Tag', 'k')", "2 k")]
    [InlineData("plot(ax, x, y, x, y, 'Tag', 'k')", "2 k")]
    [InlineData("plot(ax, y, 'g', 'LineWidth', 2)", "1 ")]
    public void PlotStillTakesAWellFormedCall(string call, string made)
    {
        Assert.Equal($"{made}\n", RunAndRead($$"""
            f = figure('Visible', 'off'); ax = axes(f); hold(ax, 'on'); x = 1:3; y = [2 1 3];
            h = {{call}};
            fprintf('%d %s\n', numel(h), get(h(1), 'Tag'));
            """));
    }

    [Theory]
    [InlineData("[1 0 0]", "[1 0 0]")]
    [InlineData("uint8([255 0 0])", "[1 0 0]")]
    [InlineData("[1 2 3]", "MATLAB:hg:ColorBase:BadColorValue")]
    [InlineData("logical([1 0 0])", "MATLAB:fill:InvalidColorInput")]
    [InlineData("[1 2 3]'", "interp")]
    public void APatchsThreeElementRowIsOneColour(string colour, string answer)
    {
        Assert.Equal($"{answer}\n", RunAndRead($$"""
            f = figure('Visible', 'off'); ax = axes(f);
            try
                h = patch(ax, [0 1 0], [0 0 1], {{colour}});
                c = h.FaceColor; if ~ischar(c), c = mat2str(c); end
                disp(c);
            catch e
                disp(e.identifier);
            end
            """));
    }

    [Theory]
    [InlineData("line(g, x, y)", "MATLAB:hg:DeletedObject")]
    [InlineData("plot(g, x, y)", "MATLAB:hg:InvalidHandle")]
    [InlineData("bar(g, y)", "MATLAB:graphics:axescheck:DeletedObject")]
    [InlineData("fill(g, x, y, 'r')", "MATLAB:hg:InvalidHandle")]
    [InlineData("text(1, 1, 'a', 'Parent', g)", "MATLAB:hg:DeletedObject")]
    [InlineData("hggroup(g)", "MATLAB:hg:DeletedObject")]
    public void ADeletedParentIsRefusedNotReadAsData(string call, string identifier)
    {
        Assert.Equal($"{identifier}\n0\n", RunAndRead($$"""
            f = figure('Visible', 'off'); ax = axes(f); hold(ax, 'on'); x = 1:3; y = [2 1 3];
            g = hggroup(ax); delete(g);
            try, {{call}}; catch e, disp(e.identifier); end
            disp(numel(ax.Children));
            """));
    }

    [Fact]
    public void ScatterOfATableAfterAnAxesNamesItsVariables()
    {
        Assert.Equal("Var1 Var2 Var3 k\n", RunAndRead("""
            f = figure('Visible', 'off'); ax = axes(f); hold(ax, 'on');
            tbl = table([1; 2; 3], [4; 5; 6], [7; 8; 9], 'VariableNames', {'Var1', 'Var2', 'Var3'});
            s = scatter(ax, tbl, 'Var1', 'Var2', 'Tag', 'k');
            s3 = scatter3(ax, tbl, 'Var1', 'Var2', 'Var3');
            fprintf('%s %s %s %s\n', s.XVariable, s.YVariable, s3.ZVariable, s.Tag);
            """));
    }
}
