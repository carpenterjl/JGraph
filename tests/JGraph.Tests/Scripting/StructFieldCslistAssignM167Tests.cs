using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V6 (ADR 0167), eighth sub-stage: comma-list assignment to a struct array's field —
/// <c>[st.f] = deal(v)</c>, <c>[st.f] = C{:}</c>, <c>[st(2:3).f] = deal(a, b)</c> (appendix A #30) —
/// and to a graphics handle array's property, <c>[h.LineWidth] = deal(3)</c> (#134).
/// </summary>
/// <remarks>
/// The parity fixture <c>struct_field_cslist_assign</c> holds R2025b's answers; these pin the roads
/// the sub-stage touched, one assertion a road, so a regression names itself.
/// </remarks>
[Collection("JG facade")]
public class StructFieldCslistAssignM167Tests : IDisposable
{
    private const string Helpers = """

        function [a, b] = two_out(x)
        a = x; b = -x;
        end

        function varargout = count_out()
        varargout = num2cell(1:nargout);
        end

        function k = bump()
        global cslist_bumps
        cslist_bumps = cslist_bumps + 1;
        k = 2;
        end
        """;

    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();

    public StructFieldCslistAssignM167Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string RunAndRead(string code)
    {
        ScriptRunResult result = _engine.RunAsync(
            code + Helpers,
            new ScriptContext(_output, (_, _) => { }, Path.GetTempPath(), resolvePath: null, figureFiles: new TestFigureFiles()),
            CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim();
    }

    private string RunExpectingError(string code)
    {
        ScriptRunResult result = _engine.RunAsync(
            code + Helpers,
            new ScriptContext(_output, (_, _) => { }, Path.GetTempPath(), resolvePath: null, figureFiles: new TestFigureFiles()),
            CancellationToken.None).GetAwaiter().GetResult();
        Assert.False(result.Success);
        return result.Message ?? "";
    }

    [Theory]
    [InlineData("[st.f] = deal(5);", "5 5")]
    [InlineData("[st.f] = deal(1, 2);", "1 2")]
    [InlineData("C = {10, 20}; [st.f] = C{:};", "10 20")]
    [InlineData("C = {1, 2, 3}; [st.f] = C{:};", "1 2")]
    [InlineData("[~, st.f] = deal(1, 2, 3);", "2 3")]
    [InlineData("[st.f] = size(ones(2, 3));", "2 3")]
    [InlineData("[st.f] = two_out(5);", "5 -5")]
    [InlineData("name = 'f'; [st.(name)] = deal(8);", "8 8")]
    public void EachElementOfTheStructArrayIsOneTarget(string statement, string expected)
    {
        Assert.Equal(expected, RunAndRead($"st = struct('f', {{0, 0}}); {statement} fprintf('%d %d\\n', st(1).f, st(2).f);"));
    }

    [Fact]
    public void TheCallIsAskedForAsManyOutputsAsThereAreElements()
    {
        Assert.Equal("[1 2 3]", RunAndRead("st = struct('f', {0, 0, 0}); [st.f] = count_out(); disp(mat2str([st.f]));"));
    }

    [Fact]
    public void EveryElementTakesAShareAndDetachesOnItsOwnWrite()
    {
        string text = RunAndRead("""
            v = [1 2 3];
            st = struct('f', {0, 0});
            [st.f] = deal(v);
            v(1) = 7;
            st(1).f(2) = 9;
            fprintf('%s %s %s\n', mat2str(v), mat2str(st(1).f), mat2str(st(2).f));
            """);
        Assert.Equal("[7 2 3] [1 9 3] [1 2 3]", text);
    }

    [Theory]
    [InlineData("st = struct('f', {0, 0, 0}); [st(2:3).f] = deal(20, 30);", "[0 20 30]")]
    [InlineData("st = struct('f', {1, 2}); [st(3:4).f] = deal(3, 4);", "[1 2 3 4]")]
    [InlineData("st = struct('f', {0, 0, 0}); [st(end - 1:end).f] = deal(7, 8);", "[0 7 8]")]
    [InlineData("st = struct('f', {0, 0, 0}); [st([true false true]).f] = deal(1, 3);", "[1 0 3]")]
    [InlineData("[st(1:2).f] = deal(1, 2);", "[1 2]")]
    [InlineData("st = struct('f', {0 0; 0 0}); [st.f] = deal(1, 2, 3, 4);", "[1 3;2 4]")]
    public void ASubscriptPicksTheElementsAndAPositionPastTheEndGrowsTheArray(string statements, string expected)
    {
        Assert.Equal(expected, RunAndRead($"{statements} disp(mat2str(reshape([st.f], size(st))));"));
    }

    [Fact]
    public void TheSubscriptIsEvaluatedOnce()
    {
        string text = RunAndRead("""
            global cslist_bumps
            cslist_bumps = 0;
            st = struct('f', {0, 0, 0});
            [st(bump()).f] = deal(9);
            fprintf('%d %s\n', cslist_bumps, mat2str([st.f]));
            """);
        Assert.Equal("1 [0 9 0]", text);
    }

    [Theory]
    [InlineData("C = {1}; [st.f] = C{:};", "Insufficient number of outputs from right hand side of equal sign to satisfy assignment.")]
    [InlineData("[st.f] = deal(1, 2, 3);", "The number of outputs should match the number of inputs.")]
    [InlineData("st.f = 5;", "Scalar structure required for this assignment.")]
    public void ACountMismatchIsRefusedInMatlabsWords(string statement, string expected)
    {
        Assert.Contains(expected, RunExpectingError($"st = struct('f', {{0, 0}}); {statement}"));
    }

    [Theory]
    [InlineData("h.list = struct('f', {0, 0}); [h.list.f] = deal(1, 2); r = [h.list.f];", "[1 2]")]
    [InlineData("k = {struct('f', {0, 0})}; [k{1}.f] = deal(1, 2); r = [k{1}.f];", "[1 2]")]
    [InlineData("st = struct('f', 0); [st.f] = deal(3); r = [st.f size(st)];", "[3 1 1]")]
    [InlineData("st = struct('f', {}); [st.f] = deal(1); r = numel(st);", "0")]
    [InlineData("st = struct('f', {1, 2}); [st.g] = deal('x'); r = [st(1).g st(2).g];", "'xx'")]
    [InlineData("st = struct('f', {0, 0}); t = st; [st.f] = deal(9); r = [st.f t.f];", "[9 9 0 0]")]
    public void TheListReachesTheArrayWhereverItIsHeld(string statements, string expected)
    {
        Assert.Equal(expected, RunAndRead($"{statements} disp(mat2str(r));"));
    }

    [Fact]
    public void DealHandsAStringOnAsAString()
    {
        string text = RunAndRead("""
            st = struct('f', {0, 0});
            [st.f] = deal("a", "b");
            c = {["a" "b"]};
            [c{1}, b] = deal("z", c{1});
            fprintf('%s %s %s %s %d\n', class(st(1).f), st(2).f, class(c{1}), class(b), numel(b));
            """);
        Assert.Equal("string b string string 2", text);
    }

    [Fact]
    public void AHandleArraysPropertyIsOneTargetAHandleAndReadsBackAsAList()
    {
        string text = RunAndRead("""
            f = figure('Visible', 'off');
            p1 = plot([1 2 3]);
            hold on
            p2 = plot([4 5 6]);
            p3 = plot([7 8 9]);
            h = [p1 p2 p3];
            [h.LineWidth] = deal(1);
            [h(2:3).LineWidth] = deal(4);
            w = [h.LineWidth];
            [h.LineWidth] = deal(5, 6, 7);
            fprintf('%s %g %g %g\n', mat2str(w), p1.LineWidth, p2.LineWidth, p3.LineWidth);
            close(f);
            """);
        Assert.Equal("[1 4 4] 5 6 7", text);
    }
}
