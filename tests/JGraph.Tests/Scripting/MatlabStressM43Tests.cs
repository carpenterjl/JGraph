using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M43: the data types and graphics verbs that finished the stress-test campaign — the
/// <c>table</c>/<c>timetable</c> constructors, <c>categorical</c>/<c>summary</c>, string and cell
/// conversions with <c>missing</c>, MATLAB's sprintf format cycling, element-wise <c>~</c>,
/// tiledlayout, and the grid-matrix surface forms.
/// </summary>
[Collection("JG facade")]
public class MatlabStressM43Tests : IDisposable
{
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabStressM43Tests() => JG.Reset();

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
    public void TableConstructorNamesAndBracesIn()
    {
        string output = RunAndRead("""
            T = table([1; 2], {'A'; 'B'}, 'VariableNames', {'ID', 'Code'});
            fprintf('%s %d\n', T.Code{2}, T.ID(1));
            """);

        Assert.Contains("B 1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TableDefaultNamesAreVarN()
    {
        string output = RunAndRead("""
            T = table((1:4)', (5:8)');
            fprintf('%d\n', sum(T.Var2));
            """);

        Assert.Contains("26", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TimetableCarriesItsRowTimes()
    {
        string output = RunAndRead("""
            tt = timetable(seconds(1:3)', [10; 20; 30]);
            fprintf('%d %d\n', tt.Time(2), tt.Var1(3));
            """);

        Assert.Contains("2 30", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CategoricalSummaryCountsTheCategories()
    {
        string output = RunAndRead("""
            c = categorical({'high', 'low', 'medium', 'high'});
            s = summary(c);
            fprintf('%d %d\n', s.high, s.low);
            """);

        Assert.Contains("2 1", output, StringComparison.Ordinal);
    }

    /// <summary>Splitting and rejoining text, in both containers MATLAB has one for.</summary>
    /// <remarks>
    /// The <c>joined{1}</c> here is not decoration. <c>split</c> of a <em>char row</em> answers a
    /// cell and <c>join</c> of a cell answers a 1-by-1 cell, so the text has to be taken out of the
    /// cell before <c>fprintf</c> can print it — which is what R2024a requires, measured rather than
    /// assumed. Until M121 this script read <c>joined</c> directly, because <c>split</c> gave back a
    /// bare array of strings whatever container it was handed.
    /// </remarks>
    [Fact]
    public void StringConversionsRoundTrip()
    {
        string output = RunAndRead("""
            parts = split('one two three');
            joined = join(parts');
            cs = cellstr(["a", "b"]);
            back = string(cs);
            fprintf('%d %s %s %s\n', numel(parts), joined{1}, cs{2}, back(1));
            """);

        Assert.Contains("3 one two three b a", output, StringComparison.Ordinal);
    }

    /// <summary>A one-to-many text verb answers in the container it was handed (M121).</summary>
    /// <remarks>
    /// Every expectation here is R2024a's own: a char row splits to a cell, a string to a string
    /// array, and a column of two strings to a 2-by-2 — the pieces along a new dimension, not a
    /// longer list. The last line is what a flattened answer would get wrong while keeping the
    /// count right, so it is the one that would catch a regression to the old behaviour.
    /// </remarks>
    [Fact]
    public void SplitAnswersInTheContainerItWasHanded()
    {
        string output = RunAndRead("""
            a = split('a,b', ',');
            b = split("a,b", ",");
            c = split(["a,b"; "c,d"], ",");
            fprintf('%s %s %s\n', class(a), class(b), class(c));
            fprintf('%d %d %d %d\n', size(c, 1), size(c, 2), numel(a), numel(b));
            fprintf('%s %s\n', c(2, 1), c(1, 2));
            """);

        Assert.Contains("cell string string", output, StringComparison.Ordinal);
        Assert.Contains("2 2 2 2", output, StringComparison.Ordinal);
        Assert.Contains("c b", output, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingIsAValueAndIsMissingFindsIt()
    {
        string output = RunAndRead("""
            arr = ["x", missing, "y"];
            hit = ismissing(arr(2));
            joined = join(arr); % must not error
            fprintf('%d\n', hit);
            """);

        Assert.Contains("1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeFormatsPerElement()
    {
        Assert.Contains("0.500", RunAndRead(
            "s = compose('%0.3f', [0.5; 0.25]);\nfprintf('%s\\n', s{1});"), StringComparison.Ordinal);
    }

    [Fact]
    public void SprintfCyclesTheFormatOverArrays()
    {
        string output = RunAndRead("fprintf('%d,', 1:5);\nfprintf('%d-%d;', [1 2 3 4]);");

        Assert.Contains("1,2,3,4,5,", output, StringComparison.Ordinal);
        Assert.Contains("1-2;3-4;", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TildeIsElementwiseOverArrays()
    {
        string output = RunAndRead("""
            M = magic(4);
            mask = mod(M, 2) == 0;
            fprintf('%d\n', numel(M(mask)) + numel(M(~mask)));
            """);

        Assert.Contains("16", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Num2strHonoursAFormatString()
    {
        Assert.Contains("0003.142", RunAndRead("disp(num2str(3.14159, '%08.3f'))"), StringComparison.Ordinal);
    }

    [Fact]
    public void TiledlayoutAndAcceptedVerbsRun()
    {
        ScriptRunResult result = RunMatlab("""
            figure;
            tiledlayout(2, 2);
            for k = 1:4
                nexttile;
                plot(1:10, (1:10) * k);
            end
            axis tight;
            rotate3d on;
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    [Fact]
    public void SurfAcceptsFullMeshgridMatrices()
    {
        ScriptRunResult result = RunMatlab("""
            [X, Y, Z] = peaks(20);
            surf(X, Y, Z);
            shading interp;
            contourf(X, Y, Z, 10);
            colormap turbo;
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    /// <summary>
    /// The braces around <c>cell(2, 2)</c> are MATLAB's own spelling and became load-bearing in M65:
    /// a bare cell argument to <c>struct</c> spreads across the elements of a struct array, so the
    /// unbraced form now builds a 2-by-2 struct array whose field is empty rather than one struct
    /// holding a 2-by-2 cell.
    /// </summary>
    [Fact]
    public void BraceAssignmentThroughADotChain()
    {
        string output = RunAndRead("""
            s = struct('inner', struct('cells', {cell(2, 2)}));
            s.inner.cells{2, 1} = 42;
            fprintf('%d\n', s.inner.cells{2, 1});
            """);

        Assert.Contains("42", output, StringComparison.Ordinal);
    }

    [Fact]
    public void MeshgridOneArgumentSquaresTheGrid()
    {
        Assert.Contains("1", RunAndRead("""
            [X, Y] = meshgrid(1:4);
            fprintf('%d\n', isequal(size(X), [4 4]) && X(2, 3) == 3 && Y(2, 3) == 2);
            """), StringComparison.Ordinal);
    }
}
