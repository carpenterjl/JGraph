using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// ADR 0354 (open items 285, 286 and the engine half of 208): a write with two subscripts or more fits
/// its right side to the block by the extents that are not 1, a struct array is written through a
/// grid, and a subscript that is not a number is read as R2025b reads it. The parity fixture
/// <c>oi_index_assign_shapes</c> holds R2025b's answers row by row; these pin the roads and the JGS
/// dialect, which keeps its count rule.
/// </summary>
[Collection("JG facade")]
public class IndexAssignShapesTests : IDisposable
{
    public IndexAssignShapesTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private static (string[] Lines, ScriptRunResult Result) Run(string code, JgsDialect dialect)
    {
        var output = new RecordingScriptOutput();
        var context = new ScriptContext(output, static (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(code, context, default, sourceId: "", hook: null, dialect);
        return (output.NormalLines.ToArray(), result);
    }

    private static string[] Matlab(string code)
    {
        (string[] lines, ScriptRunResult result) = Run(code, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message);
        return lines;
    }

    [Fact]
    public void ABlockTakesTheRightSideByItsExtentsNotItsCount()
    {
        string[] lines = Matlab("""
            A = zeros(3, 4);
            try, A(2:3, 2:3) = 1:4; catch e, fprintf('%s|%s\n', e.identifier, e.message); end
            B = zeros(5, 1); B(:, 1) = reshape(1:5, 1, 1, 5); fprintf('%s\n', mat2str(B'));
            C = zeros(2, 3); C(:, :) = reshape(1:6, 1, 2, 3); fprintf('%s\n', mat2str(C));
            D = zeros(3, 4); D(2, 1:3) = [1; 2; 3]; fprintf('%s\n', mat2str(D(2, :)));
            E = zeros(2, 2, 2);
            try, E(1, :, :) = 1:4; catch e, fprintf('%s\n', e.message); end
            F = zeros(3, 4); F(1:4) = [1 2; 3 4]; fprintf('%s\n', mat2str(F(1, :)));
            """);

        Assert.Equal(
            [
                "MATLAB:subsassigndimmismatch|Unable to perform assignment because the size of the left side is 2-by-2 and the size of the right side is 1-by-4.",
                "[1 2 3 4 5]",
                "[1 3 5;2 4 6]",
                "[1 2 3 0]",
                "Unable to perform assignment because the size of the left side is 1-by-2-by-2 and the size of the right side is 1-by-4.",
                "[1 4 0 0]",
            ],
            lines);
    }

    [Fact]
    public void TheShapelessEmptyHasItsOwnRefusalAndAnEmptyBlockTakesAnyEmpty()
    {
        string[] lines = Matlab("""
            A = [];
            try, A(1:2, 1:3) = 1:6; catch e, fprintf('%s\n', e.message); end
            c = {};
            try, c(1:2, 1:2) = {1, 2, 3, 4}; catch e, fprintf('%s\n', e.message); end
            B = zeros(3, 4); B([], []) = zeros(0, 3); fprintf('%d %d\n', size(B));
            G = []; G(:, :) = ones(1, 1, 3); fprintf('%d %d\n', size(G));
            """);

        Assert.Equal(
            [
                "Unable to perform assignment because the indices on the left side are not compatible with the size of the right side.",
                "Unable to perform assignment because the indices on the left side are not compatible with the size of the right side.",
                "3 4",
                "3 1",
            ],
            lines);
    }

    [Fact]
    public void CellsCharsAndDeletionsMeetTheSameRule()
    {
        string[] lines = Matlab("""
            c = cell(2, 3);
            try, c(:, :) = num2cell(1:6); catch e, fprintf('%s\n', e.message); end
            x = 'abcd';
            try, x(1, 1:3) = 'uv'; catch e, fprintf('%s\n', e.message); end
            A = zeros(3, 4);
            try, A(1:2, 1:2) = []; catch e, fprintf('%s|%s\n', e.identifier, e.message); end
            try, A(5, :) = []; catch e, fprintf('%s|%s\n', e.identifier, e.message); end
            """);

        Assert.Equal(
            [
                "Unable to perform assignment because the size of the left side is 2-by-3 and the size of the right side is 1-by-6.",
                "Unable to perform assignment because the size of the left side is 1-by-3 and the size of the right side is 1-by-2.",
                "MATLAB:badnullassign|A null assignment can have only one non-colon index.",
                "MATLAB:subsdeldimmismatch|Matrix index is out of range for deletion.",
            ],
            lines);
    }

    [Fact]
    public void AStructArrayIsWrittenThroughAGrid()
    {
        string[] lines = Matlab("""
            s = struct('a', {}); s(:, 1) = struct('a', {1, 2});
            fprintf('%d %d %s\n', size(s), mat2str([s.a]));
            t = struct('a', {1, 2; 3, 4}); t(:, 3) = struct('a', {5, 6});
            fprintf('%d %d %s\n', size(t), mat2str([t.a]));
            u = struct('a', {1, 2; 3, 4}); u(:, 1, 2) = struct('a', {5, 6});
            fprintf('%s %d\n', mat2str(size(u)), isempty(u(2, 2, 2).a));
            v = struct('a', {1, 2; 3, 4}); v(1, :) = [];
            fprintf('%d %d %s\n', size(v), mat2str([v.a]));
            w = struct('a', {1, 2; 3, 4}); w(1:2, 1:2) = struct('a', 9);
            fprintf('%s\n', mat2str([w.a]));
            try, w(:, 1) = 5; catch e, fprintf('%s\n', e.identifier); end
            try, w(:, 1) = struct('b', {1, 2}); catch e, fprintf('%s\n', e.identifier); end
            try, w(:, :) = struct('a', {1, 2, 3, 4}); catch e, fprintf('%s\n', e.identifier); end
            k = struct('a', {1, 2; 3, 4}); m = k; m(2, 2) = struct('a', 9);
            fprintf('%d %d\n', k(2, 2).a, m(2, 2).a);
            r = reshape(struct('a', {1, 2, 3, 4}), 2, 2); q = r';
            fprintf('%s %s\n', mat2str(size(r)), mat2str([q.a]));
            """);

        Assert.Equal(
            [
                "2 1 [1 2]",
                "2 3 [1 3 2 4 5 6]",
                "[2 2 2] 1",
                "1 2 [3 4]",
                "[9 9 9 9]",
                "MATLAB:heterogeneousAssignment",
                "MATLAB:heterogeneousStrucAssignment",
                "MATLAB:subsassigndimmismatch",
                "4 9",
                "[2 2] [1 3 2 4]",
            ],
            lines);
    }

    [Fact]
    public void ASubscriptThatIsNotANumberIsReadAsR2025bReadsIt()
    {
        string[] lines = Matlab("""
            x = 1:100;
            fprintf('%s\n', mat2str(x('ab')));
            y = [1 2 3]; z = y(); fprintf('%s\n', mat2str(z));
            s = struct('a', 7); fprintf('%d\n', s().a);
            try, q = y("a"); catch e, fprintf('%s|%s\n', e.identifier, e.message); end
            try, q = y({1}); catch e, fprintf('%s\n', e.message); end
            c = {1, 2, 3};
            try, c{:} = 1; catch e, fprintf('%s|%s\n', e.identifier, e.message); end
            d = {1}; d{:} = 4; fprintf('%d\n', d{1});
            try, c(1) = 5; catch e, fprintf('%s\n', e.identifier); end
            s2 = struct('a', {1, 2});
            try, s2.a = 5; catch e, fprintf('%s\n', e.identifier); end
            try, eval('w() = 5;'); catch e, fprintf('%s\n', e.identifier); end
            try, eval('v = c{};'); catch e, fprintf('%s\n', e.identifier); end
            T = table([1; 2], [3; 4], 'VariableNames', {'p', 'q'});
            fprintf('%s\n', mat2str(T{:, 'q'}'));
            """);

        Assert.Equal(
            [
                "[97 98]",
                "[1 2 3]",
                "7",
                "MATLAB:matrix:unableToUseTypeAsIndex|Unable to use a value of type string as an index.",
                "Unable to use a value of type cell as an index.",
                "MATLAB:index:expected_one_output_for_assignment|Assigning to 3 elements using a simple assignment statement is not supported. Consider using comma-separated list assignment.",
                "4",
                "MATLAB:invalidConversion",
                "MATLAB:scalarStrucRequired",
                "MATLAB:m_empty_indices",
                "MATLAB:m_improper_grouping",
                "[3 4]",
            ],
            lines);
    }

    [Fact]
    public void TheJgsDialectKeepsItsCountRule()
    {
        (string[] lines, ScriptRunResult result) = Run("""
            let A = zeros(3, 4);
            A(1:2, 1:2) = [1, 2, 3, 4];
            print(A(1, 1))
            print(A(2, 2))
            """, JgsDialect.Jgs);

        Assert.True(result.Success, result.Message);
        Assert.Equal(["1", "4"], lines);
    }
}
