using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The <c>voronoin</c> builtin (M57): the vertex table led by the point at infinity, one cell of
/// vertex numbers per point, and the refusals — collinear points have no diagram, and only the
/// plane is supported.
/// </summary>
[Collection("JG facade")]
public class MatlabVoronoinTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public MatlabVoronoinTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (_, _) => { }));

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    [Fact]
    public Task Square_HasOneFiniteVertexAndFourUnboundedCells() => RunAsserting("""
        % Four cocircular points share one circumcentre; the diagram is a single vertex with
        % four rays, so V is the Inf row plus that centre and every cell reaches infinity.
        [V, C] = voronoin([0 0; 1 0; 1 1; 0 1]);
        assert(isequal(size(V), [2 2]));
        assert(isinf(V(1, 1)) && isinf(V(1, 2)));
        assert(abs(V(2, 1) - 0.5) < 1e-12 && abs(V(2, 2) - 0.5) < 1e-12);
        assert(numel(C) == 4);
        for k = 1:4
            assert(any(C{k} == 1)); % every corner cell is unbounded
            assert(any(C{k} == 2)); % and touches the one finite vertex
        end
        """);

    [Fact]
    public Task InteriorPoint_GetsAClosedCellOfEquidistantVertices() => RunAsserting("""
        % Four compass points around the origin: the bisector against each is one side of the
        % square [-1,1]x[-1,1], so the middle cell is closed with corners at (+-1, +-1).
        [V, C] = voronoin([0 0; 2 0; 0 2; -2 0; 0 -2]);
        middle = C{1};
        assert(numel(middle) == 4);
        assert(~any(middle == 1)); % no point at infinity: the cell is closed
        for k = 1:4
            v = V(middle(k), :);
            assert(abs(abs(v(1)) - 1) < 1e-9 && abs(abs(v(2)) - 1) < 1e-9);
        end

        % The outer points sit on the hull, so their cells are unbounded.
        for p = 2:5
            assert(any(C{p} == 1));
        end
        """);

    [Fact]
    public Task SingleOutput_IsTheVertexTable() => RunAsserting("""
        X = [0 0; 1 0; 1 1; 0 1];
        [V, C] = voronoin(X);
        assert(isequal(voronoin(X), V));
        """);

    [Fact]
    public Task CollinearPoints_AreRefusedWithTheReason() => RunAsserting("""
        ok = false;
        try
            voronoin([0 0; 1 1; 2 2; 3 3]);
        catch e
            ok = contains(e.message, 'collinear');
        end
        assert(ok);
        """);

    [Fact]
    public Task ThreeDimensionalPoints_AreRefusedWithTheReason() => RunAsserting("""
        ok = false;
        try
            voronoin([0 0 0; 1 0 0; 0 1 0; 0 0 1]);
        catch e
            ok = contains(e.message, 'n-by-2');
        end
        assert(ok);
        """);
}
