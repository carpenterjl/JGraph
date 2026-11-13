using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open items 143, 144 and 166 (ADR 0000): a group is a child of its axes in the tree a script sees,
/// reached by findobj and findall and deleted by a replacing plot, cla and delete; a figure's
/// annotations answer R2025b's shape words and sit in a hidden pane (probes pa_groups, pa_pane).
/// </summary>
[Collection("JG facade")]
public class GroupTreeAnnotationPaneTests : IDisposable
{
    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public GroupTreeAnnotationPaneTests()
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

    private string RunAndRead(string code, IScriptEngine? engine = null)
    {
        ScriptRunResult result = Run(code, engine);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText;
    }

    // --- 144: the searches reach a group -----------------------------------------------------------

    [Fact]
    public void FindobjAndFindallReachAGroupAndATransform()
    {
        Assert.Equal("1 1 1 1 1\n", RunAndRead("""
            f = figure('Visible', 'off'); ax = axes(f);
            g = hggroup(ax, 'Tag', 'group');
            t = hgtransform(ax, 'Tag', 'xform');
            fprintf('%d %d %d %d %d\n', numel(findobj(f, 'Tag', 'group')), numel(findobj(f, 'Type', 'hggroup')), ...
                numel(findall(f, 'Tag', 'group')), numel(findobj(f, 'Type', 'hgtransform')), isequal(findobj(f, 'Tag', 'xform'), t));
            """));
    }

    [Fact]
    public void AnAxesListsItsGroupsAndAGroupItsMembers()
    {
        Assert.Equal("xform group\ninner member\n|xform|group|tmember|inner|member|deep\ninner\n", RunAndRead("""
            f = figure('Visible', 'off'); ax = axes(f);
            g = hggroup(ax, 'Tag', 'group');
            t = hgtransform(ax, 'Tag', 'xform');
            line(g, 1:3, 1:3, 'Tag', 'member');
            line(t, 1:3, 1:3, 'Tag', 'tmember');
            inner = hggroup(g, 'Tag', 'inner');
            c = line(inner, 1:3, 1:3, 'Tag', 'deep');
            disp(strjoin(get(ax.Children, 'Tag')', ' '));
            disp(strjoin(get(g.Children, 'Tag')', ' '));
            disp(strjoin(get(findobj(ax), 'Tag')', '|'));
            disp(get(ancestor(c, 'hggroup'), 'Tag'));
            """));
    }

    [Fact]
    public void AGroupStandsAmongTheDrawnObjectsByWhenItWasMade()
    {
        Assert.Equal("tx p2 gmid p1\n", RunAndRead("""
            f = figure('Visible', 'off'); ax = axes(f); hold(ax, 'on');
            plot(ax, 1:3, 'Tag', 'p1');
            g = hggroup(ax, 'Tag', 'gmid');
            plot(ax, 1:3, 'Tag', 'p2');
            text(ax, 1, 1, 'a', 'Tag', 'tx');
            line(g, 1:3, 1:3, 'Tag', 'gm');
            disp(strjoin(get(ax.Children, 'Tag')', ' '));
            """));
    }

    [Fact]
    public void AMemberTakenOutOfItsGroupComesToTheFront()
    {
        Assert.Equal("member held group\n", RunAndRead("""
            f = figure('Visible', 'off'); ax = axes(f); hold(ax, 'on');
            g = hggroup(ax, 'Tag', 'group');
            a = line(g, 1:3, 1:3, 'Tag', 'member');
            plot(ax, 1:3, 'Tag', 'held');
            set(a, 'Parent', ax);
            disp(strjoin(get(ax.Children, 'Tag')', ' '));
            """));
    }

    // --- 166: what deletes a group ------------------------------------------------------------------

    [Theory]
    [InlineData("plot(ax, 1:3);")]
    [InlineData("cla(ax);")]
    [InlineData("cla(ax, 'reset');")]
    [InlineData("delete(g);")]
    public void AReplacingPlotClaAndDeleteTakeAGroupWithWhatItHolds(string clear)
    {
        Assert.Equal("0 0 0 0 0\n", RunAndRead($$"""
            f = figure('Visible', 'off'); ax = axes(f);
            g = hggroup(ax);
            m = line(g, 1:3, 1:3);
            n = hggroup(g);
            k = line(n, 1:3, 1:3);
            {{clear}}
            fprintf('%d %d %d %d %d\n', isvalid(g), isvalid(m), isvalid(n), isvalid(k), numel(findobj(f, 'Type', 'hggroup')));
            """));
    }

    [Fact]
    public void HeldAxesAndAGroupMadeWithoutAParentKeepWhatIsThere()
    {
        Assert.Equal("1 g6 before\n", RunAndRead("""
            f = figure('Visible', 'off'); ax = axes(f);
            hold(ax, 'on');
            g = hggroup(ax);
            plot(ax, 1:3);
            kept = isvalid(g);
            hold(ax, 'off');
            cla(ax);
            plot(ax, 1:3, 'Tag', 'before');
            h = hggroup('Tag', 'g6');
            fprintf('%d %s\n', kept, strjoin(get(ax.Children, 'Tag')', ' '));
            """));
    }

    [Fact]
    public void DeletingAMemberLeavesItsGroup()
    {
        Assert.Equal("1 0 1\n", RunAndRead("""
            f = figure('Visible', 'off'); ax = axes(f);
            g = hggroup(ax);
            m = line(g, 1:3, 1:3);
            delete(m);
            fprintf('%d %d %d\n', isvalid(g), numel(g.Children), numel(ax.Children));
            """));
    }

    // --- 143: annotations ----------------------------------------------------------------------------

    [Fact]
    public void AnAnnotationAnswersItsShapeWordInTheMatlabDialect()
    {
        Assert.Equal("textboxshape rectangleshape ellipseshape lineshape arrowshape doubleendarrowshape textarrowshape\n", RunAndRead("""
            f = figure('Visible', 'off');
            h = {annotation(f, 'textbox'), annotation(f, 'rectangle'), annotation(f, 'ellipse'), annotation(f, 'line'), ...
                annotation(f, 'arrow'), annotation(f, 'doublearrow'), annotation(f, 'textarrow')};
            disp(strjoin(cellfun(@(a) get(a, 'Type'), h, 'UniformOutput', false), ' '));
            """));
    }

    [Fact]
    public void AnAnnotationKeepsItsKindAsItsTypeInJgs()
    {
        Assert.Equal("textbox 1\n", RunAndRead("""
            let f = figure('Visible', 'off');
            let a = annotation(f, 'textbox', [0.1, 0.1, 0.2, 0.2], 'Tag', 'tb');
            fprintf('%s %d\n', get(a, 'Type'), numel(findobj(f, 'Tag', 'tb')));
            """, new JgsScriptEngine()));
    }

    [Fact]
    public void AnAnnotationSitsInAHiddenPane()
    {
        Assert.Equal("0 1 0 1 annotationpane off scribeOverlay 1 axes 1 1\n", RunAndRead("""
            f = figure('Visible', 'off'); ax = axes(f);
            a = annotation(f, 'arrow', [0.1 0.2], [0.1 0.2], 'Tag', 'arr');
            p = get(a, 'Parent');
            fprintf('%d %d %d %d %s %s %s %d %s %d %d\n', numel(findobj(f, 'Tag', 'arr')), numel(findall(f, 'Tag', 'arr')), ...
                numel(findobj(f, 'Type', 'arrowshape')), numel(findall(f, 'Type', 'arrowshape')), ...
                get(p, 'Type'), get(p, 'HandleVisibility'), get(p, 'Tag'), isequal(get(p, 'Parent'), f), ...
                get(f.Children, 'Type'), isequal(ancestor(a, 'annotationpane'), p), isgraphics(a, 'arrowshape'));
            """));
    }

    [Fact]
    public void ClfKeepsThePaneAndClfResetOrDeleteTakesIt()
    {
        Assert.Equal("0 1 1\n0 0\n0 0 1\n", RunAndRead("""
            f = figure('Visible', 'off');
            c = annotation(f, 'line', [0.1 0.2], [0.1 0.2]);
            p = get(c, 'Parent');
            clf(f);
            fprintf('%d %d %d\n', isvalid(c), isvalid(p), numel(findall(f, 'Type', 'annotationpane')));
            c = annotation(f, 'line', [0.1 0.2], [0.1 0.2]);
            p2 = get(c, 'Parent');
            clf(f, 'reset');
            fprintf('%d %d\n', isvalid(c), isvalid(p2));
            c = annotation(f, 'textbox', [0.1 0.2 0.1 0.1]);
            p3 = get(c, 'Parent');
            delete(p3);
            c = annotation(f, 'textbox', [0.1 0.2 0.1 0.1]);
            fprintf('%d %d %d\n', isvalid(p3), isequal(get(c, 'Parent'), p3), numel(findall(f, 'Type', 'annotationpane')));
            """));
    }

    [Fact]
    public void ADrawnRectangleIsAPrimitiveRectangle()
    {
        Assert.Equal("1 0 1\n", RunAndRead("""
            f = figure('Visible', 'off'); ax = axes(f);
            r = rectangle(ax);
            a = annotation(f, 'rectangle');
            fprintf('%d %d %d\n', isa(r, 'matlab.graphics.primitive.Rectangle'), isa(r, 'matlab.graphics.shape.Rectangle'), ...
                isa(a, 'matlab.graphics.shape.Rectangle'));
            """));
    }

    [Fact]
    public void AFigureFileStillCarriesTheAnnotationsAndTheGroupsMembers()
    {
        Assert.Equal("1 2\n", RunAndRead("""
            f = figure('Visible', 'off'); ax = axes(f); hold(ax, 'on');
            plot(ax, 1:3, 'Tag', 'plain');
            g = hggroup(ax);
            line(g, [0 1], [0 1], 'Tag', 'inline');
            annotation(f, 'arrow', [0.1 0.2], [0.1 0.2], 'Tag', 'arr');
            savefig(f, 'kept.fig');
            close(f);
            h = openfig('kept.fig', 'invisible');
            fprintf('%d %d\n', numel(findall(h, 'Tag', 'arr')), numel(findobj(h, 'Type', 'line')));
            """));
    }
}
