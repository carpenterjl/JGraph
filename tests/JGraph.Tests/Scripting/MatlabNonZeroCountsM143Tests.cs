using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Counting the elements that are not zero, when some of them are complex (M143). <c>nnz</c> and
/// <c>nonzeros</c> refused a complex argument outright; MATLAB asks each element the same question
/// it asks a real one and answers it a plane at a time, so <c>nnz([1+1i 0 2])</c> is 2.
/// </summary>
/// <remarks>
/// <para>
/// Every expectation here was printed by R2025b and pasted. The two storages a complex array can
/// arrive in are both exercised: a literal such as <c>[1+1i 0 2]</c> is boxed, and a transform's
/// output such as <c>fft([1 2 3 4])</c> is a packed pair of planes, which used to fail with an
/// internal message about boxed elements rather than with the type complaint.
/// </para>
/// <para>
/// <c>find</c>, <c>any</c> and <c>all</c> were measured against R2025b at the same time and already
/// agreed, because each of them asks <c>IsTruthy</c>, which has known both planes since complex
/// arrays arrived. They are pinned below so that the next change to the truthiness rule cannot move
/// them quietly.
/// </para>
/// </remarks>
[Collection("JG facade")]
public class MatlabNonZeroCountsM143Tests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabNonZeroCountsM143Tests() => JG.Reset();

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

    private async Task<string> Failure(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.False(result.Success);
        return result.Message + _output.ErrorText;
    }

    [Fact]
    public Task Nnz_CountsAnElementWithEitherPlaneNonZero() => RunAsserting("""
        assert(nnz([1+1i 0 2]) == 2);
        assert(nnz([0 0]) == 0);
        assert(nnz(1i) == 1);
        assert(nnz(1+1i) == 1);
        assert(nnz([1+1i 0; 0 2]) == 2);
        assert(nnz([0 2+1i; 3 0]) == 2);
        % An imaginary part alone is enough, and a zero one is not.
        assert(nnz([1i 0 2i]) == 2);
        assert(nnz(complex(0, 0)) == 0);
        assert(nnz(complex([1 0 2])) == 2);
        """);

    [Fact]
    public Task Nnz_ReadsAPackedTransformWithoutBoxingIt() => RunAsserting("""
        % fft returns planar storage, which used to fail with an internal message rather than
        % an answer: every one of these four entries is nonzero.
        assert(nnz(fft([1 2 3 4])) == 4);
        assert(nnz(fft([1 0 0 0])) == 4);
        assert(nnz(fft(zeros(1, 8))) == 0);
        assert(nnz(1i * zeros(1, 5)) == 0);
        """);

    [Fact]
    public Task Nnz_AsksWhetherEachPlaneIsZeroAndNotWhetherItIsFinite() => RunAsserting("""
        assert(nnz(-0) == 0);
        assert(nnz(complex(0, -0)) == 0);
        assert(nnz(NaN) == 1);
        assert(nnz(complex(NaN, 0)) == 1);
        assert(nnz(complex(0, NaN)) == 1);
        assert(nnz([Inf 0]) == 1);
        assert(nnz(complex(0, Inf)) == 1);
        """);

    [Fact]
    public Task Nonzeros_KeepsTheComplexElementsItKept() => RunAsserting("""
        v = nonzeros([1+1i 0 2]);
        assert(numel(v) == 2);
        assert(v(1) == 1+1i);
        assert(v(2) == 2);

        % Column-major order, which is the order MATLAB reads a matrix in: the 3 comes first.
        w = nonzeros([0 2+1i; 3 0]);
        assert(numel(w) == 2);
        assert(w(1) == 3);
        assert(w(2) == 2+1i);

        z = nonzeros(fft([1 2 3 4]));
        assert(numel(z) == 4);
        assert(abs(z(2) - (-2+2i)) < 1e-12);
        """);

    [Fact]
    public Task Nonzeros_DropsBackToRealStorageWhenNothingImaginarySurvives() => RunAsserting("""
        assert(isreal(nonzeros([1 0 2])));
        r = nonzeros([1 0 2]);
        assert(numel(r) == 2 && r(1) == 1 && r(2) == 2);
        assert(~isreal(nonzeros([1+1i 0 2])));
        % complex() marks an array complex with a zero imaginary plane, and R2025b lets nonzeros
        % narrow it back: isreal(nonzeros(complex([1 0 2]))) is 1 there too.
        assert(isreal(nonzeros(complex([1 0 2]))));
        assert(isreal(nonzeros([1+0i 0 2])));
        """);

    [Fact]
    public Task Nonzeros_AnswersARowWhereMatlabAnswersAColumn() => RunAsserting("""
        % A divergence that predates this milestone and is recorded in ADR 0147: R2025b returns
        % size [2 1] for both of these. Only the orientation differs; the values and their order
        % are MATLAB's.
        assert(isequal(size(nonzeros([1 0 2])), [1 2]));
        assert(isequal(size(nonzeros([1+1i 0 2])), [1 2]));
        """);

    [Fact]
    public Task FindAnyAndAll_AlreadyKnewBothPlanes() => RunAsserting("""
        assert(isequal(find([1+1i 0 2]), [1 3]));
        assert(isequal(find(fft([1 2 3 4])), [1 2 3 4]));
        assert(isempty(find(complex(0, 0))));

        assert(any([1i 0]));
        assert(~any([complex(0, 0) 0]));
        assert(all([1i 1]));
        assert(~all([complex(0, 0) 1]));

        [r, c, v] = find([1+1i 0; 0 2]);
        assert(isequal(r, [1; 2]));
        assert(isequal(c, [1; 2]));
        assert(v(1) == 1+1i);
        assert(v(2) == 2);
        """);

    [Fact]
    public async Task Nnz_StillRefusesWhatIsNotANumber()
    {
        string message = await Failure("nnz('hi');");
        Assert.Contains("nnz expects a number or numeric array", message);
        Assert.Contains("string", message);
    }
}
