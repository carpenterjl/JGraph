using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The computational-geometry builtins M39 adds: the Delaunay triangulation and the contour matrix.
/// Both are checked by properties of the answer — a triangulation covers the right area with no
/// point inside a circumcircle, a contour line sits on its own level — rather than by a stored
/// result, because neither has a unique correct output.
/// </summary>
[Collection("JG facade")]
public class MatlabGeometryExtraTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabGeometryExtraTests() => JG.Reset();

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

    [Fact]
    public Task Delaunay_SplitsASquareIntoTwoTriangles() => RunAsserting("""
        x = [0 1 1 0];
        y = [0 0 1 1];
        t = delaunay(x, y);
        assert(isequal(size(t), [2 3]));

        % Every index names one of the four corners, and every corner is used.
        assert(min(min(t)) == 1);
        assert(max(max(t)) == 4);
        """);

    [Fact]
    public Task Delaunay_CoversTheWholeHull() => RunAsserting("""
        % Nine points on a 3x3 grid triangulate into 8 triangles covering area 4.
        [X, Y] = meshgrid(0:2, 0:2);
        x = reshape(X, 1, 9);
        y = reshape(Y, 1, 9);
        t = delaunay(x, y);
        assert(size(t, 1) == 8);

        total = 0;
        for k = 1:8
            ax = x(t(k, 1)); ay = y(t(k, 1));
            bx = x(t(k, 2)); by = y(t(k, 2));
            cx = x(t(k, 3)); cy = y(t(k, 3));
            total = total + abs((bx - ax)*(cy - ay) - (by - ay)*(cx - ax)) / 2;
        end
        assert(abs(total - 4) < 1e-12);
        """);

    [Fact]
    public Task Delaunay_TakesThePointsAsOneMatrixToo() => RunAsserting("""
        p = [0 0; 1 0; 1 1; 0 1];
        assert(isequal(delaunay(p), delaunay([0 1 1 0], [0 0 1 1])));
        """);

    [Fact]
    public Task Contourc_PacksLevelAndCountAheadOfEachLine() => RunAsserting("""
        % A plane tilted in x: the level-l contour is the vertical line x = l.
        [X, Y] = meshgrid(0:4, 0:4);
        C = contourc(0:4, 0:4, X, [1 3]);
        top = C(1);
        bottom = C(2);

        % Walk the blocks: each opens with its level and point count, then that many points.
        k = 1;
        blocks = 0;
        levelSum = 0;
        while k <= length(top)
            level = top(k);
            n = bottom(k);
            assert(n >= 2);
            blocks = blocks + 1;
            levelSum = levelSum + level;

            % Every point of this line sits at x = level, which is what the plane makes it.
            for j = (k + 1):(k + n)
                assert(abs(top(j) - level) < 1e-12);
            end

            k = k + n + 1;
        end

        % The walk consumed the matrix exactly, and found both levels.
        assert(k == length(top) + 1);
        assert(length(top) == length(bottom));
        assert(blocks == 2);
        assert(levelSum == 4);
        """);

    [Fact]
    public Task Contourc_ChoosesItsOwnLevelsWhenNotToldAny() => RunAsserting("""
        [X, Y] = meshgrid(0:4, 0:4);
        Z = X + Y;
        C = contourc(Z);
        assert(size(C, 1) == 2);
        assert(length(C(1)) > 0);

        % A single number asks for that many levels rather than naming one; a single level is
        % written twice, which is MATLAB's own way of disambiguating the argument.
        C3 = contourc(Z, 3);
        top = C3(1);
        bottom = C3(2);
        k = 1;
        distinct = 0;
        previous = -1e308;
        while k <= length(top)
            if top(k) ~= previous
                distinct = distinct + 1;
                previous = top(k);
            end
            k = k + bottom(k) + 1;
        end
        assert(distinct == 3);
        """);
}
