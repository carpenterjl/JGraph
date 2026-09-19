using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V6 (ADR 0167), second sub-stage: <c>T{rows, vars} = v</c> (appendix A #25), and a table
/// variable that holds the value it was given — an integer or logical array, a cell that is not
/// all text, a struct array — as it is (#119, #120).
/// </summary>
/// <remarks>
/// The parity fixture <c>table_brace_assign</c> holds R2025b's answers; these pin the roads the
/// sub-stage touched, one assertion a road, so a regression names itself.
/// </remarks>
[Collection("JG facade")]
public class TableBraceAssignM167Tests : IDisposable
{
    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();

    public TableBraceAssignM167Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string RunAndRead(string code)
    {
        ScriptRunResult result = _engine.RunAsync(
            code,
            new ScriptContext(_output, (_, _) => { }, Path.GetTempPath(), resolvePath: null, figureFiles: new TestFigureFiles()),
            CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim();
    }

    [Theory]
    [InlineData("U{1, 1} = 9;", "[9 2] [3 4]")]
    [InlineData("U{2, 'Var2'} = 7;", "[1 2] [3 7]")]
    [InlineData("U{2, {'Var2', 'Var1'}} = [7 8];", "[1 8] [3 7]")]
    [InlineData("U{1, :} = [8 9];", "[8 2] [9 4]")]
    [InlineData("U{:, 1} = [5; 6];", "[5 6] [3 4]")]
    [InlineData("U{:, 2} = 0;", "[1 2] [0 0]")]
    [InlineData("U{1:2, 1:2} = [10 20; 30 40];", "[10 30] [20 40]")]
    [InlineData("U{U.Var1 > 1, 1} = 0;", "[1 0] [3 4]")]
    [InlineData("U{1, logical([0 1])} = 9;", "[1 2] [9 4]")]
    [InlineData("U{end, end} = 100;", "[1 2] [3 100]")]
    [InlineData("U{:, 1} = U{:, 2};", "[3 4] [3 4]")]
    public void ABraceWriteLandsInTheCopyAlone(string write, string expected)
    {
        string text = RunAndRead($$"""
            T = table([1; 2], [3; 4]);
            U = T;
            {{write}}
            fprintf('%s %s\n', mat2str(U.Var1'), mat2str(U.Var2'));
            assert(isequal(T.Var1, [1; 2]) && isequal(T.Var2, [3; 4]));
            """);
        Assert.Equal(expected, text);
    }

    [Fact]
    public void AWritePastTheLastRowGrowsEveryVariableAndANewPositionOrNameAddsAVariable()
    {
        string text = RunAndRead("""
            T = table([1; 2], [3; 4], 'RowNames', {'a'; 'b'});
            T{4, 2} = 9;
            fprintf('%d %s %s %s\n', height(T), mat2str(T.Var1'), mat2str(T.Var2'), strjoin(T.Properties.RowNames', ','));
            U = table([1; 2]);
            U{1, 2} = 5;
            U{2, 'Extra'} = 6;
            fprintf('%s %s %s\n', strjoin(U.Properties.VariableNames, ','), mat2str(U.Var2'), mat2str(U.Extra'));
            """);
        Assert.Equal("4 [1 2 0 0] [3 4 0 9] a,b,Row3,Row4\nVar1,Var2,Extra [5 0] [0 6]", text.Replace("\r", ""));
    }

    [Fact]
    public void TheWriteConvertsIntoTheVariablesClass()
    {
        string text = RunAndRead("""
            T = table(int8([1; 2]), [1; 2]);
            T{1, 1} = 3.7;
            T{2, 1} = 300;
            T{1, 2} = true;
            fprintf('%s %s %s %s\n', class(T.Var1), mat2str(T.Var1'), class(T.Var2), mat2str(T.Var2'));
            """);
        Assert.Equal("int8 [4 127] double [1 2]", text);
    }

    [Fact]
    public void ATextVariableTakesACellAndRefusesAChar()
    {
        string text = RunAndRead("""
            T = table([1; 2], {'a'; 'b'});
            U = T;
            U{2, 2} = {'z'};
            try
                U{1, 2} = 'q';
            catch err
                disp(err.message);
            end
            fprintf('%s %s\n', strjoin(T.Var2', ','), strjoin(U.Var2', ','));
            """);
        Assert.Equal("Conversion to cell from char is not possible.\na,b a,z", text.Replace("\r", ""));
    }

    [Theory]
    [InlineData("T{1, :} = [1 2 3];", "wrong width")]
    [InlineData("T{1} = 5;", "one subscript")]
    [InlineData("T{0, 1} = 5;", "Index in position 1 is invalid")]
    [InlineData("T{1, :} = [];", "use () subscripting instead of {}")]
    public void ARefusedWriteSaysWhyAndLeavesTheTable(string write, string fragment)
    {
        string text = RunAndRead($$"""
            T = table([1; 2], [3; 4]);
            try
                {{write}}
                disp('no error');
            catch err
                fprintf('%d %s %s\n', contains(err.message, '{{fragment}}'), mat2str(T.Var1'), mat2str(T.Var2'));
            end
            """);
        Assert.Equal("1 [1 2] [3 4]", text);
    }

    [Fact]
    public void ABraceWriteThroughACellAFieldAndAnArgumentLandsInThatHolderAlone()
    {
        string text = RunAndRead("""
            T = table([1; 2], [3; 4]);
            c = {T}; d = c;
            d{1}{1, 1} = 9;
            s.t = T; r = s;
            r.t{2, 2} = 0;
            fprintf('%d %d %d %d\n', c{1}.Var1(1), d{1}.Var1(1), s.t.Var2(2), r.t.Var2(2));
            """);
        Assert.Equal("1 9 4 0", text);
    }

    [Fact]
    public void ATimetableAndItsPropertiesSurviveABraceWrite()
    {
        string text = RunAndRead("""
            TT = timetable(seconds([1; 2]), [1; 2]);
            TT.Properties.Description = 'about';
            TT{2, 1} = 9;
            fprintf('%d %s %s [%s]\n', istimetable(TT), mat2str(seconds(TT.Time)'), mat2str(TT.Var1'), TT.Properties.Description);
            """);
        Assert.Equal("1 [1 2] [1 9] [about]", text);
    }

    [Fact]
    public void AVariableHoldsACellOfValuesAndAStructArrayAsTheyAre()
    {
        string text = RunAndRead("""
            T = table({[1 2]; [3 4]});
            U = T;
            U.Var1{1}(1) = 9;
            S = table(struct('a', {1; 2}));
            V = S;
            V.Var1(1).a = 9;
            W = V(2, :);
            fprintf('%s %s %s %d %d %s %d\n', class(T.Var1), mat2str(T.Var1{1}), mat2str(U.Var1{1}), ...
                S.Var1(1).a, V.Var1(1).a, class(V.Var1), W.Var1.a);
            """);
        Assert.Equal("cell [1 2] [9 2] 1 9 struct 2", text);
    }

    [Fact]
    public void ATextVariableGrownByAnotherVariablesWriteFillsWithEmptyDoubles()
    {
        string text = RunAndRead("""
            T = table([1; 2], {'a'; 'b'});
            T{4, 1} = 9;
            fprintf('%d %s %s %s\n', height(T), class(T.Var2{3}), mat2str(size(T.Var2{3})), T.Var2{2});
            """);
        Assert.Equal("4 double [0 0] b", text);
    }
}
