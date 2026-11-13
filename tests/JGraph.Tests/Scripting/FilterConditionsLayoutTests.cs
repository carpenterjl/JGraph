using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open item 279 (ADR 0345): <c>filter</c>'s final conditions are the delays first and the signal's
/// other dimensions after them, and its initial conditions are read in that layout or as one line
/// for every slice. Every script runs with packing on and off, and both lanes must agree with the
/// sizes R2025b answered and with each other.
/// </summary>
[Collection("JG facade")]
public class FilterConditionsLayoutTests : IDisposable
{
    public FilterConditionsLayoutTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private static (string[] Output, bool Success, string? Message) RunWith(bool packed, string code)
    {
        bool previous = JgsPacking.Enabled;
        JgsPacking.Enabled = packed;
        try
        {
            JG.Reset();
            var output = new RecordingScriptOutput();
            var figures = new List<FigureModel>();
            var context = new ScriptContext(output, (_, figure) => figures.Add(figure), null);
            ScriptRunResult result = JgsRunner.Run(
                code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
            return (output.Normal.ToArray(), result.Success, result.Message);
        }
        finally
        {
            JgsPacking.Enabled = previous;
        }
    }

    private static void AssertBothLanes(string code)
    {
        (string[] packedOut, bool packedOk, string? packedMessage) = RunWith(packed: true, code);
        (string[] boxedOut, bool boxedOk, string? boxedMessage) = RunWith(packed: false, code);
        Assert.True(packedOk, packedMessage);
        Assert.True(boxedOk, boxedMessage);
        Assert.Equal(boxedOut, packedOut);
    }

    [Fact]
    public void FinalConditionsPutTheDelaysFirst()
    {
        AssertBothLanes("""
            bi = [0.2 0.3]; ai = [1 -0.5 0.25];
            b3 = [1 -2 3 0.5] / 3; bo = [0.1 0.2 0.3 0.4]; ao = [2 -0.4 0.3];
            xr = [1 4 2 8 5 7 3 6];
            X = reshape(mod((1:20)*0.618, 1), 4, 5);
            C = reshape(mod((1:24)*0.414, 1), 2, 3, 4);
            [~, z] = filter(bi, ai, xr);          assert(isequal(size(z), [2 1]));
            [~, z] = filter(b3, 1, xr);           assert(isequal(size(z), [3 1]));
            [~, z] = filter(bo, ao, xr');         assert(isequal(size(z), [3 1]));
            [~, z] = filter(bi, ai, 5);           assert(isequal(size(z), [2 1]));
            [y, z] = filter(bi, ai, X, [], 1);    assert(isequal(size(z), [2 5]) && isequal(size(y), [4 5]));
            [~, z] = filter(bi, ai, X, [], 2);    assert(isequal(size(z), [2 4]));
            [~, z] = filter(bo, ao, X, [], 2);    assert(isequal(size(z), [3 4]));
            [~, z] = filter(bi, ai, X, [], 3);    assert(isequal(size(z), [2 4 5]));
            [~, z] = filter(bi, ai, C, [], 3);    assert(isequal(size(z), [2 2 3]));
            [~, z] = filter(bi, ai, C, [], 2);    assert(isequal(size(z), [2 2 4]));
            [~, z] = filter(b3, 1, C, [], 1);     assert(isequal(size(z), [3 3 4]));
            [~, z] = filter(bi, ai, xr, [], 1);   assert(isequal(size(z), [2 8]));
            [~, z] = filter(bi, ai, [1 2 3], [], 7); assert(isequal(size(z), [2 1 3]));
            [~, z] = filter(2, 4, X, [], 2);      assert(isequal(size(z), [0 4]));
            [y, z] = filter(bi, ai, zeros(3, 0)); assert(isequal(size(y), [3 0]) && isequal(size(z), [2 0]));
            [~, z] = filter(bi, ai, zeros(0, 3)); assert(isequal(size(z), [2 3]) && all(z(:) == 0));
            [~, z] = filter(bi, ai, single(xr));  assert(isa(z, 'single'));
            % each slice's line is the column at its place: filter row r alone and compare
            [~, z] = filter(bo, ao, X, [], 2);
            for r = 1:4
                [~, zr] = filter(bo, ao, X(r, :));
                assert(isequal(z(:, r), zr));
            end
            disp('ok');
            """);
    }

    [Fact]
    public void InitialConditionsAreOneLineOrOneLinePerSlice()
    {
        AssertBothLanes("""
            bi = [0.2 0.3]; ai = [1 -0.5 0.25];
            b3 = [1 -2 3 0.5] / 3;
            X = reshape(mod((1:20)*0.618, 1), 4, 5);
            C = reshape(mod((1:24)*0.414, 1), 2, 3, 4);
            % a matrix of lines, one per row of X, resumes each row as if filtered on its own
            Z = reshape(1:8, 2, 4) / 10;
            [Y, ZF] = filter(bi, ai, X, Z, 2);
            for r = 1:4
                [yr, zr] = filter(bi, ai, X(r, :), Z(:, r));
                assert(isequal(Y(r, :), yr) && isequal(ZF(:, r), zr));
            end
            % feed-forward too, on a strided slice
            Z3 = reshape(1:12, 3, 4) / 10;
            [Y, ZF] = filter(b3, 1, X, Z3, 2);
            for r = 1:4
                [yr, zr] = filter(b3, 1, X(r, :), Z3(:, r));
                assert(isequal(Y(r, :), yr) && isequal(ZF(:, r), zr));
            end
            % an N-D zi and its matrix spelling are the same lines
            [y1, z1] = filter(bi, ai, C, reshape(1:12, 2, 2, 3) / 10, 3);
            [y2, z2] = filter(bi, ai, C, reshape(1:12, 2, 6) / 10, 3);
            assert(isequal(y1, y2) && isequal(z1, z2));
            % one vector is every slice's line
            [y1, z1] = filter(bi, ai, X, [0.5 0.25], 2);
            [y2, z2] = filter(bi, ai, X, repmat([0.5; 0.25], 1, 4), 2);
            assert(isequal(y1, y2) && isequal(z1, z2));
            % a filter cut in two resumes from zf along any dimension
            [~, za] = filter(bi, ai, C(:, 1:2, :), [], 2);
            [yb, zb] = filter(bi, ai, C(:, 3, :), za, 2);
            [yw, zw] = filter(bi, ai, C, [], 2);
            assert(isequal(yb, yw(:, 3, :)) && isequal(zb, zw));
            disp('ok');
            """);
    }

    [Theory]
    [InlineData("filter([0.2 0.3], [1 -0.5 0.25], 1:8, [1 2 3])", "MATLAB:filter:invalidInitialConditions")]
    [InlineData("filter([0.2 0.3], [1 -0.5 0.25], 1:8, 0.5)", "MATLAB:filter:invalidInitialConditions")]
    [InlineData("filter([0.2 0.3], [1 -0.5 0.25], 1:8, zeros(0, 2))", "MATLAB:filter:invalidInitialConditions")]
    [InlineData("filter([0.2 0.3], [1 -0.5 0.25], magic(4), ones(4, 2))", "MATLAB:filter:invalidInitialConditions")]
    [InlineData("filter([0.2 0.3], [1 -0.5 0.25], magic(4), ones(2, 1, 4))", "MATLAB:filter:invalidInitialConditions")]
    [InlineData("filter([0.2 0.3], [1 -0.5 0.25], 1:8, reshape([1 2], 1, 1, 2))", "MATLAB:filter:invalidInitialConditions")]
    [InlineData("filter([0.2 0.3], [1 -0.5 0.25], 1:8, 'ab')", "MATLAB:filter:inputType")]
    [InlineData("filter([0.2 0.3], [1 -0.5 0.25], 1:8, '')", "MATLAB:filter:inputType")]
    [InlineData("filter([0.2 0.3], [1 -0.5 0.25], 1:8, [], 0)", "MATLAB:getdimarg:dimensionMustBePositiveInteger")]
    [InlineData("filter([0.2 0.3], [1 -0.5 0.25], 1:8, [], 1.5)", "MATLAB:getdimarg:dimensionMustBePositiveInteger")]
    public void RefusalsCarryR2025bsIdentifiers(string call, string identifier)
    {
        string code = "try\n    " + call + ";\n    disp('none');\ncatch e\n    disp(e.identifier);\nend\n";
        foreach (bool packed in new[] { true, false })
        {
            (string[] output, bool ok, string? message) = RunWith(packed, code);
            Assert.True(ok, message);
            Assert.Contains(identifier, string.Join("\n", output));
        }
    }
}
