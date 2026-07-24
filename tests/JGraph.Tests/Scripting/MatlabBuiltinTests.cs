using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M28.S6: the builtins MATLAB scripts reach for — string handling, type predicates, errors, cells and
/// structs, applying function handles — and the two-output forms of <c>size</c>, <c>max</c> and
/// <c>sort</c>.
/// </summary>
[Collection("JG facade")]
public class MatlabBuiltinTests : IDisposable
{
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabBuiltinTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptRunResult RunMatlab(string code)
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        return JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
    }

    private string RunAndRead(string code)
    {
        ScriptRunResult result = RunMatlab(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText;
    }

    [Fact]
    public void RemAndModDifferOnNegatives()
    {
        // rem takes the dividend's sign, mod the divisor's — the classic MATLAB gotcha.
        Assert.Contains("-1", RunAndRead("disp(rem(-7, 3))"), StringComparison.Ordinal);
        _output.Normal.Clear();
        Assert.Contains("2", RunAndRead("disp(mod(-7, 3))"), StringComparison.Ordinal);
    }

    [Fact]
    public void FixRoundsTowardZero()
    {
        Assert.Contains("[2, -2]", RunAndRead("disp(fix([2.7 -2.7]))"), StringComparison.Ordinal);
    }

    [Fact]
    public void RepmatRepeats()
    {
        Assert.Contains("[1, 2, 1, 2]", RunAndRead("disp(repmat([1 2], 2))"), StringComparison.Ordinal);
    }

    [Fact]
    public void TypePredicatesAnswerForEachKind()
    {
        string output = RunAndRead("""
            fprintf('%d %d %d %d %d\n', ...
                isnumeric([1 2]), ischar('a'), islogical([1 2] > 1), iscell({1}), isstruct(struct('a', 1)));
            """);

        Assert.Contains("1 1 1 1 1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void StringHelpersBehaveLikeMatlab()
    {
        string output = RunAndRead("""
            disp(strcmp('abc', 'abc'))
            disp(strcmpi('ABC', 'abc'))
            disp(strrep('a-b-c', '-', '+'))
            disp(strtrim('  padded  '))
            disp(strjoin({'a', 'b'}, ', '))
            disp(num2str(3.5))
            disp(str2double('2.5'))
            """);

        Assert.Contains("true", output, StringComparison.Ordinal);
        Assert.Contains("a+b+c", output, StringComparison.Ordinal);
        Assert.Contains("padded", output, StringComparison.Ordinal);
        Assert.Contains("a, b", output, StringComparison.Ordinal);
        Assert.Contains("3.5", output, StringComparison.Ordinal);
        Assert.Contains("2.5", output, StringComparison.Ordinal);
    }

    [Fact]
    public void StrcmpOverACell_GivesOneAnswerPerElement()
    {
        Assert.Contains("[false, true]", RunAndRead("disp(strcmp({'a', 'b'}, 'b'))"), StringComparison.Ordinal);
    }

    [Fact]
    public void StrsplitProducesACell()
    {
        Assert.Contains("b", RunAndRead("parts = strsplit('a,b,c', ',');\ndisp(parts{2})"), StringComparison.Ordinal);
    }

    [Fact]
    public void Str2doubleAnswersNaNForNonNumbers()
    {
        Assert.Contains("NaN", RunAndRead("disp(str2double('not a number'))"), StringComparison.Ordinal);
    }

    [Fact]
    public void FormatStringsDecodeEscapes_EvenThoughQuotesDoNot()
    {
        // MATLAB's single quotes take '\n' literally, but fprintf/sprintf decode it. Both halves of
        // that rule have to hold or every formatted line runs together.
        string output = RunAndRead("fprintf('a\\nb\\n');\ndisp(length('a\\nb'))");

        Assert.Contains("a\nb", output, StringComparison.Ordinal);
        Assert.Contains("4", output, StringComparison.Ordinal); // the literal is a, \, n, b
    }

    [Fact]
    public void ErrorStopsTheScriptWithItsMessage()
    {
        ScriptRunResult result = RunMatlab("error('the value %d is out of range', 42)");

        Assert.False(result.Success);
        Assert.Contains("the value 42 is out of range", result.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorWithAnIdentifier_ReportsOnlyTheMessage()
    {
        ScriptRunResult result = RunMatlab("error('jgraph:badInput', 'bad input')");

        Assert.False(result.Success);
        Assert.Contains("bad input", result.Message!, StringComparison.Ordinal);
        Assert.DoesNotContain("jgraph:badInput", result.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorCanBeCaught()
    {
        Assert.Contains("boom", RunAndRead("try\n  error('boom')\ncatch e\n  disp(e.message)\nend"), StringComparison.Ordinal);
    }

    [Fact]
    public void WarningWritesToTheErrorStream_WithoutStopping()
    {
        ScriptRunResult result = RunMatlab("warning('careful');\ndisp('still running')");

        Assert.True(result.Success, result.Message);
        Assert.Contains("careful", _output.ErrorText, StringComparison.Ordinal);
        Assert.Contains("still running", _output.NormalText, StringComparison.Ordinal);
    }

    [Fact]
    public void AssertStopsOnlyWhenTheConditionIsFalse()
    {
        Assert.True(RunMatlab("assert(1 == 1)").Success);

        ScriptRunResult failed = RunMatlab("assert(1 == 2, 'values differ')");
        Assert.False(failed.Success);
        Assert.Contains("values differ", failed.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void CellAndStructBuilders()
    {
        string output = RunAndRead("""
            c = cell(3);
            disp(numel(c))
            s = struct('width', 2, 'name', 'trace');
            disp(s.width)
            disp(fieldnames(s){1})
            disp(isfield(s, 'name'))
            t = rmfield(s, 'name');
            disp(isfield(t, 'name'))
            """);

        Assert.Contains("3", output, StringComparison.Ordinal);
        Assert.Contains("2", output, StringComparison.Ordinal);
        Assert.Contains("width", output, StringComparison.Ordinal);
        Assert.Contains("true", output, StringComparison.Ordinal);
        Assert.Contains("false", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Num2cellAndCell2matRoundTrip()
    {
        Assert.Contains("[1, 2, 3]", RunAndRead("disp(cell2mat(num2cell([1 2 3])))"), StringComparison.Ordinal);
    }

    [Fact]
    public void FevalAndCellfunApplyAHandle()
    {
        Assert.Contains("9", RunAndRead("disp(feval(@(x) x^2, 3))"), StringComparison.Ordinal);

        _output.Normal.Clear();
        Assert.Contains("[1, 4, 9]", RunAndRead("disp(cellfun(@(x) x^2, {1, 2, 3}))"), StringComparison.Ordinal);
    }

    [Fact]
    public void CellfunNeedsUniformOutputFalse_ForNonScalarResults()
    {
        ScriptRunResult result = RunMatlab("disp(cellfun(@(x) [x x], {1, 2}))");

        Assert.False(result.Success);
        Assert.Contains("UniformOutput", result.Message!, StringComparison.Ordinal);

        _output.Normal.Clear();
        Assert.Contains("[2, 2]", RunAndRead("c = cellfun(@(x) [x x], {1, 2}, 'UniformOutput', false);\ndisp(c{2})"), StringComparison.Ordinal);
    }

    [Fact]
    public void Sub2indAndInd2subUseTheDialectsBase()
    {
        // Column-major, 1-based: row 2, column 1 of a 3x3 is the second element.
        Assert.Contains("2", RunAndRead("disp(sub2ind([3 3], 2, 1))"), StringComparison.Ordinal);
        _output.Normal.Clear();
        Assert.Contains("[2, 1]", RunAndRead("disp(ind2sub([3 3], 2))"), StringComparison.Ordinal);
    }

    // --- Multiple outputs -----------------------------------------------------------------------

    [Fact]
    public void SizeCanReportRowsAndColumnsSeparately()
    {
        Assert.Contains("2 3", RunAndRead("A = [1 2 3; 4 5 6];\n[r, c] = size(A);\nfprintf('%d %d\\n', r, c);"), StringComparison.Ordinal);
    }

    [Fact]
    public void MaxAndMinCanReportWhereTheyFoundIt()
    {
        Assert.Contains("9 2", RunAndRead("[v, i] = max([3 9 5]);\nfprintf('%d %d\\n', v, i);"), StringComparison.Ordinal);
        _output.Normal.Clear();
        Assert.Contains("3 1", RunAndRead("[v, i] = min([3 9 5]);\nfprintf('%d %d\\n', v, i);"), StringComparison.Ordinal);
    }

    [Fact]
    public void SortCanReportThePermutation()
    {
        string output = RunAndRead("[s, i] = sort([30 10 20]);\ndisp(s)\ndisp(i)");

        Assert.Contains("[10, 20, 30]", output, StringComparison.Ordinal);
        Assert.Contains("[2, 3, 1]", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSingleOutputFormsAreUnchanged()
    {
        string output = RunAndRead("disp(max([3 9 5]))\ndisp(size([1 2 3]))\ndisp(sort([3 1 2]))");

        Assert.Contains("9", output, StringComparison.Ordinal);
        Assert.Contains("[1, 3]", output, StringComparison.Ordinal);
        Assert.Contains("[1, 2, 3]", output, StringComparison.Ordinal);
    }

    // --- Unsupported functions ------------------------------------------------------------------

    [Fact]
    public void AnUnsupportedMatlabFunction_IsNamedInTheError()
    {
        ScriptRunResult result = RunMatlab("y = ode45(@(t, x) -x, [0 1], 1);");

        Assert.False(result.Success);
        Assert.Contains("'ode45' is not supported in JGraph", result.Message!, StringComparison.Ordinal);
        Assert.Contains("differential-equation solvers", result.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownName_SaysSoInMatlabsWords()
    {
        ScriptRunResult result = RunMatlab("disp(nosuchthing)");

        Assert.False(result.Success);
        Assert.Contains("not recognized as a variable or a function", result.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void Jgs_StillUsesItsOwnWording()
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        ScriptRunResult result = JgsRunner.Run("disp(nosuchthing)", context, default);

        Assert.False(result.Success);
        Assert.Contains("'nosuchthing' is not defined", result.Message!, StringComparison.Ordinal);
    }
}
