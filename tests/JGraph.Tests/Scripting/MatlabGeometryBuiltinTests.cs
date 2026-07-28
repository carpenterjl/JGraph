using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The logical-array constructors, the two-dimensional transforms, and the convex hull (M38).
/// <c>true</c> and <c>false</c> are language keywords, so the tests pin both readings: the literal
/// on its own and the constructor when called.
/// </summary>
[Collection("JG facade")]
public class MatlabGeometryBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabGeometryBuiltinTests() => JG.Reset();

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
    public Task TrueAndFalse_AreStillLiteralsOnTheirOwn() => RunAsserting("""
        assert(true);
        assert(~false);
        assert(islogical(true));
        assert(double(true) == 1);
        assert(true + 1 == 2);

        % And they still work as ordinary values in every position.
        flag = true;
        if flag
            reached = 1;
        end
        assert(reached == 1);
        assert(isequal([true false true], logical([1 0 1])));
        """);

    [Fact]
    public Task TrueAndFalse_BuildLogicalArraysWhenCalled() => RunAsserting("""
        m = true(2, 3);
        assert(isequal(size(m), [2 3]));
        assert(all(all(m)));
        assert(islogical(m(1)(1)));

        z = false(3);
        assert(isequal(size(z), [3 3]));
        assert(~any(any(z)));

        v = true(1, 4);
        assert(numel(v) == 4);
        assert(sum(v) == 4);
        assert(sum(false(1, 4)) == 0);
        """);

    [Fact]
    public Task Fft2_TransformsBothDimensions() => RunAsserting("""
        % A constant matrix has all its energy at DC: the (1,1) bin holds the total, the rest is zero.
        A = ones(2, 2);
        F = fft2(A);
        assert(abs(F(1)(1) - 4) < 1e-12);
        assert(abs(F(1)(2)) < 1e-12);
        assert(abs(F(2)(1)) < 1e-12);

        % And the inverse puts it back.
        B = [1 2; 3 4];
        assert(norm(real(ifft2(fft2(B))) - B) < 1e-12);
        assert(norm(real(ifftn(fftn(B))) - B) < 1e-12);

        % A vector goes through the one-dimensional transform, which fft already does.
        assert(abs(fft2([1 1 1 1])(1) - 4) < 1e-12);
        """);

    [Fact]
    public Task Convhull_ReturnsTheClosedOutline() => RunAsserting("""
        % Four corners plus a point inside: the hull is the square, and the interior point is dropped.
        x = [0 1 1 0 0.5];
        y = [0 0 1 1 0.5];
        h = convhull(x, y);

        assert(h(1) == h(end));            % the outline closes on itself
        assert(numel(h) == 5);             % four corners, the first repeated
        assert(~any(h(1:end-1) == 5));     % the interior point is not on the hull
        """);
}
