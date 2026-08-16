using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M66 wave B: the five leftover numeric names, the class a constructor can be asked for, the
/// dimensions <c>size</c> can be asked about, and the ordering of complex numbers.
/// </summary>
/// <remarks>
/// The complex cases are the interesting ones. MATLAB orders complex numbers by their real parts and
/// throws the imaginary parts away, so <c>1+9i</c> and <c>1-9i</c> compare equal under <c>&lt;</c>
/// while <c>sort</c> — which goes by magnitude — puts them in a definite order. Both rules are
/// asserted here together, because the surprise is that one name disagrees with the other and that
/// the disagreement is correct.
/// </remarks>
[Collection("JG facade")]
public class MatlabNumericExtrasTests : IDisposable
{
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabNumericExtrasTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string RunAndRead(string code)
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText;
    }

    private string Error(string code)
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.False(result.Success, "expected a refusal, got: " + _output.NormalText);
        return result.Message + _output.ErrorText;
    }

    // --- kron, perms, factor, idivide -----------------------------------------------------------

    [Fact]
    public void KronLaysEveryElementOutAsABlock()
    {
        Assert.Equal("4 4 1 2 3\n", RunAndRead("""
            K = kron([1 2; 3 4], eye(2));
            fprintf('%d %d %g %g %g\n', size(K, 1), size(K, 2), K(1,1), K(1,3), K(3,1));
            """));
    }

    [Fact]
    public void KronOfVectorsIsAnOuterProductLaidFlat()
    {
        Assert.Equal("1 6 2 4 6 4 8 12\n", RunAndRead("""
            K = kron([1 2], [2 4 6]);
            fprintf('%d %d %g %g %g %g %g %g\n', size(K, 1), size(K, 2), K);
            """));
    }

    [Fact]
    public void PermsStartsAtTheLastArrangementAndEndsAtTheFirst()
    {
        // Reverse lexicographic, which is MATLAB's order and not the obvious one.
        Assert.Equal("6 3 321 123\n", RunAndRead("""
            P = perms([1 2 3]);
            fprintf('%d %d %d%d%d %d%d%d\n', size(P, 1), size(P, 2), P(1,:), P(6,:));
            """));
    }

    [Fact]
    public void PermsRefusesTheSizeItCannotHold()
    {
        Assert.Contains("goes up to 10", Error("perms(1:11);"));
    }

    [Fact]
    public void FactorRepeatsAPrimeAsOftenAsItDivides()
    {
        Assert.Equal("2 2 3 5 97 1\n", RunAndRead("""
            fprintf('%g %g %g %g %g %d\n', factor(60), factor(97), isempty(factor(1)));
            """));
    }

    [Fact]
    public void FactorTakesWholeNumbersOnly()
    {
        Assert.Contains("positive whole number", Error("factor(2.5);"));
    }

    [Fact]
    public void IdivideRoundsTheWayItWasTold()
    {
        // 'fix' towards zero is the default, and it is the only one that differs from ./ for an
        // integer class — which is the whole reason idivide is a separate name.
        Assert.Equal("3 4 4 -3 -4 int32\n", RunAndRead("""
            a = int32(7);
            b = int32(2);
            fprintf('%g %g %g %g %g %s\n', idivide(a, b), idivide(a, b, 'ceil'), idivide(a, b, 'round'), ...
                idivide(int32(-7), b), idivide(int32(-7), b, 'floor'), class(idivide(a, b)));
            """));
    }

    // --- interp2 --------------------------------------------------------------------------------

    [Fact]
    public void Interp2ReadsBetweenTheSamples()
    {
        Assert.Equal("2.5 1 4\n", RunAndRead("""
            V = [1 2; 3 4];
            fprintf('%g %g %g\n', interp2(V, 1.5, 1.5), interp2(V, 1, 1), interp2(V, 1.6, 1.6, 'nearest'));
            """));
    }

    [Fact]
    public void AGridCanBeGivenAsMeshgridMatricesOrAsVectors()
    {
        Assert.Equal("2.5 2.5\n", RunAndRead("""
            V = [1 2; 3 4];
            [X, Y] = meshgrid(1:2, 1:2);
            fprintf('%g %g\n', interp2(X, Y, V, 1.5, 1.5), interp2(1:2, 1:2, V, 1.5, 1.5));
            """));
    }

    [Fact]
    public void ARowOfXAgainstAColumnOfYNamesAGridOfQueries()
    {
        Assert.Equal("2 2 4\n", RunAndRead("""
            V = [1 2; 3 4];
            Q = interp2(V, [1 2], [1; 2]);
            fprintf('%d %d %g\n', size(Q, 1), size(Q, 2), Q(2,2));
            """));
    }

    [Fact]
    public void APointOutsideTheGridHasNoSampleToRead()
    {
        Assert.Equal("1\n", RunAndRead("""
            fprintf('%d\n', isnan(interp2([1 2; 3 4], 5, 5)));
            """));
    }

    [Fact]
    public void FittingASurfaceThroughTheNeighboursIsRefusedByName()
    {
        Assert.Contains("'linear' and 'nearest'", Error("interp2([1 2; 3 4], 1.5, 1.5, 'cubic');"));
    }

    // --- constructor classes --------------------------------------------------------------------

    [Fact]
    public void AConstructorCanBeAskedForItsClass()
    {
        Assert.Equal("uint8 2 2 single double\n", RunAndRead("""
            z = zeros(2, 'uint8');
            o = ones(2, 3, 'single');
            fprintf('%s %d %d %s %s\n', class(z), size(z, 1), size(z, 2), class(o), class(zeros(2)));
            """));
    }

    [Fact]
    public void LikeCopiesTheClassFromAValueRatherThanAWord()
    {
        // The point of 'like': a function can build an array of the same kind it was handed without
        // ever naming that kind.
        Assert.Equal("int16 double\n", RunAndRead("""
            fprintf('%s %s\n', class(zeros(2, 'like', int16(1))), class(zeros(2, 'like', 1)));
            """));
    }

    [Fact]
    public void AClassAConstructorDoesNotHaveIsNamedAsSuch()
    {
        Assert.Contains("no 'uint9' class", Error("zeros(2, 'uint9');"));
    }

    // --- size with dimensions -------------------------------------------------------------------

    [Fact]
    public void SizeAnswersTheDimensionsItWasAskedAbout()
    {
        Assert.Equal("2 4 4 4 1\n", RunAndRead("""
            s = size(magic(4), [1 2]);
            fprintf('%d %d %d %d %d\n', numel(s), s(1), s(2), size(magic(4), 2), size(magic(4), 5));
            """));
    }

    [Fact]
    public void SeveralDimensionsCanBeNamedOneByOneToo()
    {
        Assert.Equal("4 4\n", RunAndRead("""
            fprintf('%d %d\n', size(magic(4), 1, 2));
            """));
    }

    [Fact]
    public void NamedDimensionsAreNotFoldedIntoTheLastOutput()
    {
        // Without a dimension, [r, c] = size(V) folds every trailing dimension into c. With one, the
        // call already said what it wanted, and folding would answer a different question.
        Assert.Equal("2 3 2 12\n", RunAndRead("""
            V = zeros(2, 3, 4);
            [a, b] = size(V, [1 2]);
            [r, c] = size(V(:,:,1));
            fprintf('%d %d %d %d\n', a, b, r, c * 4);
            """));
    }

    [Fact]
    public void ADimensionPastTheRankIsOne()
    {
        // Refusing would have been the MATLAB answer for a zero dimension, but size's leniency at
        // both ends predates this milestone and JGS scripts rely on it, so it is left alone.
        Assert.Equal("1 1\n", RunAndRead("""
            fprintf('%d %d\n', size(magic(4), 7), size(magic(4), 0));
            """));
    }

    // --- complex ordering -----------------------------------------------------------------------

    [Fact]
    public void RelationalOperatorsOrderComplexNumbersByTheirRealParts()
    {
        Assert.Equal("1 1 0 0\n", RunAndRead("""
            fprintf('%d %d %d %d\n', (1+2i) < (3+0i), (5+2i) > 3, (1+9i) < (1-9i), (1+9i) > (1-9i));
            """));
    }

    [Fact]
    public void AComplexElementInsideAnArrayComparesLikeAnyOther()
    {
        Assert.Equal("011\n", RunAndRead("""
            fprintf('%d%d%d\n', [1+2i, 5, 2-1i] > 1.5);
            """));
    }

    [Fact]
    public void SortStillGoesByMagnitudeWhereTheOperatorsGoByRealPart()
    {
        // The two rules disagree on purpose: 1+1i sorts before 2 because its magnitude is smaller,
        // and compares below it because its real part is smaller — the same answer here for two
        // different reasons, which is why the magnitudes are what the test reads.
        Assert.Equal("1.41421 2 3\n", RunAndRead("""
            fprintf('%g %g %g\n', abs(sort([3, 1+1i, 2])));
            """));
    }
}
