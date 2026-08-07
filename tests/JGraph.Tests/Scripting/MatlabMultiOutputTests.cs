using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// MATLAB multiple-output calls against builtins: <c>[X, Y] = meshgrid(x, y)</c>,
/// <c>[r, c] = size(A)</c>, <c>[m, i] = max(x)</c>, <c>[s, i] = sort(x)</c>, <c>[r, c] = find(A)</c>.
/// User functions have had this since M28; these pin the builtin side, which silently produced one
/// value until M36. Assertions run inside the scripts (<c>assert(isequal(...))</c>) so the tests pin
/// MATLAB's answers, not JGraph's display formats.
/// </summary>
[Collection("JG facade")]
public class MatlabMultiOutputTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabMultiOutputTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private static Task<ScriptRunResult> Run(IScriptSession session, string code) =>
        session.ExecuteAsync(code, sourceId: "", CancellationToken.None);

    private void AssertRan(ScriptRunResult result) =>
        Assert.True(result.Success, result.Message + _output.ErrorText);

    [Fact]
    public async Task Meshgrid_TwoOutputs_ProducesBothCoordinateMatrices()
    {
        await using IScriptSession session = NewSession();

        // The exact form M35 had to leave broken: builtins could not produce two outputs.
        ScriptRunResult result = await Run(session, """
            [X, Y] = meshgrid(1:3, 1:2);
            assert(isequal(X, [1 2 3; 1 2 3]));
            assert(isequal(Y, [1 1 1; 2 2 2]));
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Meshgrid_OneOutput_IsXAlone()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            M = meshgrid(1:3, 1:2);
            assert(isequal(M, [1 2 3; 1 2 3]));
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Size_TwoOutputs_AreRowsAndColumns()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            [r, c] = size([1 2 3; 4 5 6]);
            assert(r == 2);
            assert(c == 3);
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Size_ExtraOutputs_PadWithOnes()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            [r, c, p] = size([1 2 3; 4 5 6]);
            assert(r == 2 && c == 3 && p == 1);
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task MaxAndMin_SecondOutput_IsTheFirstIndexOfTheExtreme()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            [m, i] = max([3 1 4 1 5]);
            assert(m == 5 && i == 5);
            [n, j] = min([3 1 4 1 5]);
            assert(n == 1 && j == 2);
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Sort_SecondOutput_IsThePermutation()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            [s, i] = sort([3 1 2]);
            assert(isequal(s, [1 2 3]));
            assert(isequal(i, [2 3 1]));
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Find_TwoOutputs_AreColumnMajorSubscripts()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            [r, c] = find([0 1; 1 0]);
            assert(isequal(r, [2; 1]));
            assert(isequal(c, [1; 2]));
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Find_ThirdOutput_IsTheValues()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            [r, c, v] = find([0 7; 0 0]);
            assert(isequal(r, [1]) && isequal(c, [2]) && isequal(v, [7]));
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Find_OnAVector_ReportsRowOneForEveryHit()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            [r, c] = find([0 5 0 6]);
            assert(isequal(r, [1 1]));
            assert(isequal(c, [2 4]));
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Ind2Sub_TwoOutputs_AreRowAndColumn()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            [r, c] = ind2sub([2 3], 4);
            assert(r == 2 && c == 2);
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task TildePlaceholder_SkipsAnOutputWithoutDefiningIt()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            [~, i] = max([3 1 4]);
            assert(i == 3);
            """);

        AssertRan(result);
        Assert.DoesNotContain(result.Variables, v => v.Name == "~");
    }

    [Fact]
    public async Task AskingASingleValueBuiltinForTwoOutputs_IsAnError()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, "[a, b] = sin(1);");

        Assert.False(result.Success);
        Assert.Contains("returns 1 value(s)", _output.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SingleOutputForms_AreUnchanged()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            s = size([1 2 3; 4 5 6]);
            assert(isequal(s, [2 3]));
            m = max([3 1 4]);
            assert(m == 4);
            o = sort([3 1 2]);
            assert(isequal(o, [1 2 3]));
            f = find([0 5 0 6]);
            assert(isequal(f, [2 4]));
            """);

        AssertRan(result);
    }
}
