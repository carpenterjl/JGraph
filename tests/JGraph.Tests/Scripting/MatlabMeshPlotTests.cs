using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M57 wave B: <c>voronoi</c>, <c>triplot</c> and <c>tetramesh</c> — the verbs that draw a
/// triangulation or its dual, each of which answers with the geometry instead when a script asks for
/// two outputs rather than one.
/// </summary>
[Collection("JG facade")]
public class MatlabMeshPlotTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabMeshPlotTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    private async Task<string> RunExpectingFailure(string code)
    {
        int before = _output.Errors.Count;
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.False(result.Success);
        return string.Concat(_output.Errors.Skip(before));
    }

    // --- voronoi --------------------------------------------------------------------------------

    [Fact]
    public async Task AVoronoiIsThePointsAndTheBoundariesBetweenThem()
    {
        await RunAsserting("""
            figure(1);

            % Four points on a square are cocircular, so the whole diagram is one vertex in the middle
            % with four rays leaving it — there is no finite edge to draw at all.
            h = voronoi([0 1 1 0], [0 0 1 1]);
            disp(size(h));
            disp(get(h(1), 'Type'));
            disp(get(h(1), 'XData'));
            disp(get(h(1), 'Marker'));
            disp(get(h(2), 'Name'));

            % The edges are one series with a gap between each segment: three samples per segment,
            % the third being the gap, and every segment starting at the middle.
            xd = get(h(2), 'XData');
            disp(numel(xd));
            disp(sum(isnan(xd)));
            disp(unique(xd(1:3:end)));
            """);

        Assert.Equal(
            new[] { "[2, 1]", "line", "[0, 1, 1, 0]", ".", "Voronoi", "12", "4", "[0.5]" },
            _output.NormalLines);
    }

    [Fact]
    public async Task AskingForTheEdgesAnswersWithThemAndDrawsNothing()
    {
        await RunAsserting("""
            figure(1);
            [vx, vy] = voronoi([0 1 1 0], [0 0 1 1]);

            % One column per segment, the first row where it starts and the second where it ends,
            % which is exactly what plot(vx, vy) wants.
            disp(size(vx));
            disp(vx(1, :));
            disp(vy(1, :));

            % Asking for the numbers is asking to draw them yourself.
            disp(numel(findobj(gcf, 'Type', 'line')));
            """);

        Assert.Equal(
            new[] { "[2, 4]", "[0.5, 0.5, 0.5, 0.5]", "[0.5, 0.5, 0.5, 0.5]", "0" },
            _output.NormalLines);
    }

    [Fact]
    public async Task ATriangulationGivenByHandDrivesTheDiagramItIsDualTo()
    {
        await RunAsserting("""
            x = [0 2 1 1];
            y = [0 0 2 0.5];

            % voronoi(x, y, TRI) is the dual of that triangulation; handed the one voronoi would have
            % computed for itself, it must answer with the same diagram.
            [ax, ay] = voronoi(x, y);
            [bx, by] = voronoi(x, y, delaunay(x, y));
            disp(isequal(ax, bx) && isequal(ay, by));
            """);

        Assert.Equal(new[] { "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task CollinearPointsAreRefusedByName()
    {
        string error = await RunExpectingFailure("voronoi([0 1 2], [0 1 2]);");
        Assert.Contains("voronoi:", error);
        Assert.Contains("collinear", error);
    }

    // --- triplot --------------------------------------------------------------------------------

    [Fact]
    public async Task TriplotClosesEveryTriangleAndLiftsThePenBetweenThem()
    {
        await RunAsserting("""
            figure(1);
            x = [0 1 0 1];
            y = [0 0 1 1];
            tri = [1 2 3; 2 3 4];

            % Five samples per triangle: round the three corners, back to the first, then a gap. The
            % whole mesh is one series, so it is one handle, as MATLAB's single line object is.
            h = triplot(tri, x, y);
            xd = get(h, 'XData');
            disp(numel(xd));
            disp(sum(isnan(xd)));
            disp(xd(1:5));

            % The two-output form hands the same path back as columns instead of drawing it.
            [qx, qy] = triplot(tri, x, y);
            disp(size(qx));
            disp(size(qy));
            """);

        Assert.Equal(
            new[] { "10", "2", "[0, 1, 0, 0, NaN]", "[10, 1]", "[10, 1]" },
            _output.NormalLines);
    }

    [Fact]
    public async Task TriplotTakesALineSpecAndThenNameValueOptions()
    {
        await RunAsserting("""
            figure(1);
            h = triplot([1 2 3], [0 1 0], [0 0 1], 'r--', 'LineWidth', 3);
            disp(get(h, 'LineWidth'));
            disp(get(h, 'Color'));
            disp(get(h, 'LineStyle'));
            """);

        Assert.Equal(new[] { "3", "[1, 0, 0]", "--" }, _output.NormalLines);
    }

    [Fact]
    public async Task ATriangulationTableThatIsNotThreeWideIsRefusedByName()
    {
        string error = await RunExpectingFailure("triplot([1 2 3 4], [0 1 0 1], [0 0 1 1]);");
        Assert.Contains("triplot:", error);
        Assert.Contains("3 columns", error);
    }

    // --- tetramesh ------------------------------------------------------------------------------

    [Fact]
    public async Task TetrameshDrawsTheFourFacesOfEveryTetrahedronInSpace()
    {
        await RunAsserting("""
            figure(1);
            X = [0 0 0; 1 0 0; 0 1 0; 0 0 1; 1 1 1];
            T = [1 2 3 4; 2 3 4 5];

            % One patch for the whole mesh — the deliberate divergence from MATLAB's patch per
            % tetrahedron — coloured by tetrahedron number, and the axes turned to 3-D.
            h = tetramesh(T, X);
            disp(get(h, 'Type'));
            disp(get(h, 'ColorRange'));
            disp(get(gca, 'View'));

            % A colour per tetrahedron replaces the numbering, and the patch options come through.
            h2 = tetramesh(T, X, [5 9], 'FaceAlpha', 0.25);
            disp(get(h2, 'ColorRange'));
            disp(get(h2, 'Opacity'));
            """);

        Assert.Equal(
            new[] { "patch", "[1, 2]", "[-37.5, 30]", "[5, 9]", "0.25" },
            _output.NormalLines);
    }

    [Fact]
    public async Task AVertexNumberOutsideThePointsIsRefusedByName()
    {
        string error = await RunExpectingFailure(
            "tetramesh([1 2 3 9], [0 0 0; 1 0 0; 0 1 0; 0 0 1]);");
        Assert.Contains("tetramesh:", error);
        Assert.Contains("outside the 4 points", error);
    }

    [Fact]
    public async Task VerticesThatAreNotPointsInSpaceAreRefusedByName()
    {
        string error = await RunExpectingFailure("tetramesh([1 2 3 4], [0 0; 1 0; 0 1; 1 1]);");
        Assert.Contains("tetramesh:", error);
        Assert.Contains("m-by-3", error);
    }

    [Fact]
    public async Task AColourPerTetrahedronMustCountTheTetrahedra()
    {
        string error = await RunExpectingFailure("""
            tetramesh([1 2 3 4; 2 3 4 5], [0 0 0; 1 0 0; 0 1 0; 0 0 1; 1 1 1], [1 2 3]);
            """);
        Assert.Contains("tetramesh:", error);
        Assert.Contains("2 values, not 3", error);
    }
}
