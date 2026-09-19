using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V6 (ADR 0167), first sub-stage: a table write is a rebuild, and a rebuild keeps what the table
/// is — row names, row times, dimension names, units, descriptions, description, user data — and a
/// write through a table's variable or its <c>Properties</c> is get, modify, set, stored back
/// where the table was read from.
/// </summary>
/// <remarks>
/// The parity fixture <c>table_rebuild_metadata</c> holds R2025b's answers; these pin the roads
/// the sub-stage touched, one assertion a road, so a regression names itself.
/// </remarks>
[Collection("JG facade")]
public class TableRebuildM167Tests : IDisposable
{
    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();

    public TableRebuildM167Tests() => JG.Reset();

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
    [InlineData("U.Var1(1) = 9;", "a,b 9")]
    [InlineData("U.Var1 = [5; 6];", "a,b 5")]
    [InlineData("U.New = [7; 8];", "a,b 1")]
    [InlineData("U.Var1(2) = 0; U.Var1(1) = 4;", "a,b 4")]
    public void ADotWriteKeepsTheRowNames(string write, string expected)
    {
        string text = RunAndRead($$"""
            T = table([1; 2], 'RowNames', {'a'; 'b'});
            U = T;
            {{write}}
            fprintf('%s %d\n', strjoin(U.Properties.RowNames', ','), U.Var1(1));
            assert(isequal(T.Var1, [1; 2]));
            assert(isequal(T.Properties.RowNames, {'a'; 'b'}));
            """);
        Assert.Equal(expected, text);
    }

    [Fact]
    public void ATimetableStaysOneThroughAWriteAndItsRowTimesStayADuration()
    {
        string text = RunAndRead("""
            TT = timetable(seconds([1; 2]), [1; 2]);
            U = TT;
            U.Var1(1) = 9;
            U.New = [3; 4];
            fprintf('%d %s %s %d %d\n', istimetable(U), class(U.Time), class(U.Properties.RowTimes), ...
                isequal(U.Properties.RowTimes, seconds([1; 2])), TT.Var1(1));
            """);
        Assert.Equal("1 duration duration 1 1", text);
    }

    [Fact]
    public void DatetimeRowTimesComeBackAsTheDatetimesThatWentIn()
    {
        string text = RunAndRead("""
            d = datetime(2020, 1, [1; 2]);
            TT = timetable(d, [1; 2]);
            TT.Var1(2) = 7;
            fprintf('%s %d\n', class(TT.Properties.RowTimes), isequal(TT.Properties.RowTimes, d));
            """);
        Assert.Equal("datetime 1", text);
    }

    [Fact]
    public void AVariableGrownPastTheHeightGrowsTheTable()
    {
        string text = RunAndRead("""
            T = table([1; 2], {'x'; 'y'}, 'RowNames', {'a'; 'b'});
            T.Var1(4) = 5;
            fprintf('%d %s %s %d\n', height(T), mat2str(T.Var1'), strjoin(T.Properties.RowNames', ','), isempty(T.Var2{4}));
            TT = timetable(seconds([1; 2]), [1; 2]);
            TT.Var1(3) = 5;
            fprintf('%d %s\n', height(TT), mat2str(seconds(TT.Time)'));
            """);
        Assert.Equal("4 [1 2 0 5] a,b,Row3,Row4 1\n3 [1 2 NaN]", text.Replace("\r", ""));
    }

    [Fact]
    public void SettingAVariableToEmptyRemovesItAndItsUnits()
    {
        string text = RunAndRead("""
            T = table([1; 2], [3; 4], [5; 6]);
            T.Properties.VariableUnits = {'m', 's', 'kg'};
            T.Var2 = [];
            fprintf('%s %s\n', strjoin(T.Properties.VariableNames, ','), strjoin(T.Properties.VariableUnits, ','));
            """);
        Assert.Equal("Var1,Var3 m,kg", text);
    }

    [Fact]
    public void EveryPropertySurvivesAWriteANewVariableAndARowSelection()
    {
        string text = RunAndRead("""
            T = table([1; 2; 3], [4; 5; 6], 'RowNames', {'a'; 'b'; 'c'});
            T.Properties.Description = 'about';
            T.Properties.UserData = [1 2 3];
            T.Properties.VariableUnits = {'m', 's'};
            T.Properties.VariableDescriptions = {'first', 'second'};
            T.Properties.DimensionNames = {'Id', 'Data'};
            T.Var1(1) = 9;
            T.New = [7; 8; 9];
            U = T([3 1], :);
            p = U.Properties;
            u = p.VariableUnits;
            fprintf('%s|%s|%s,%s,[%s]|%s|%s|%s\n', p.Description, mat2str(p.UserData), u{1}, u{2}, u{3}, ...
                strjoin(p.VariableDescriptions(1:2), ','), strjoin(p.DimensionNames, ','), strjoin(p.RowNames', ','));
            """);
        Assert.Equal("about|[1 2 3]|m,s,[]|first,second|Id,Data|c,a", text);
    }

    [Fact]
    public void APropertyWriteOnACopyLeavesTheOriginalAndUserDataIsAValue()
    {
        string text = RunAndRead("""
            T = table([1; 2]);
            v = [1 2 3];
            T.Properties.UserData = v;
            v(1) = 7;
            U = T;
            U.Properties.Description = 'u';
            U.Properties.VariableNames{1} = 'a';
            w = U.Properties.UserData;
            w(2) = 8;
            fprintf('[%s] [%s] %s %s %s\n', T.Properties.Description, U.Properties.Description, ...
                T.Properties.VariableNames{1}, U.Properties.VariableNames{1}, mat2str(T.Properties.UserData));
            """);
        Assert.Equal("[] [u] Var1 a [1 2 3]", text);
    }

    [Fact]
    public void AWriteThroughATableHeldInACellOrAFieldLandsInThatHolderAlone()
    {
        string text = RunAndRead("""
            T = table([1; 2], 'RowNames', {'a'; 'b'});
            c = {T};
            d = c;
            d{1}.Var1(1) = 9;
            s.t = T;
            r = s;
            r.t.Var1(2) = 5;
            r.t.Properties.Description = 'r';
            fprintf('%d %d %d %d [%s] [%s] %s\n', c{1}.Var1(1), d{1}.Var1(1), s.t.Var1(2), r.t.Var1(2), ...
                s.t.Properties.Description, r.t.Properties.Description, strjoin(d{1}.Properties.RowNames', ','));
            """);
        Assert.Equal("1 9 2 5 [] [r] a,b", text);
    }

    [Fact]
    public void ATextVariableTakesABraceWriteAndARowReadsByItsName()
    {
        string text = RunAndRead("""
            T = table([1; 2], {'x'; 'y'}, 'RowNames', {'a'; 'b'});
            U = T;
            U.Var2{2} = 'z';
            R = U('b', :);
            fprintf('%s %s %d %s\n', strjoin(U.Var2', ','), strjoin(T.Var2', ','), R.Var1, R.Properties.RowNames{1});
            """);
        Assert.Equal("x,z x,y 2 b", text);
    }

    [Theory]
    [InlineData("T.Properties.VariableNames = {'a', 'b'};", "one name for each variable")]
    [InlineData("T.Properties.VariableUnits = {'m', 's'};", "one element for each variable")]
    [InlineData("T.Properties.Nonsense = 1;", "Nonsense")]
    public void ABadPropertyIsRefusedAndTheTableIsAsItWas(string write, string fragment)
    {
        string text = RunAndRead($$"""
            T = table([1; 2]);
            try
                {{write}}
                disp('no error');
            catch err
                fprintf('%d %s\n', contains(err.message, '{{fragment}}'), T.Properties.VariableNames{1});
            end
            """);
        Assert.Equal("1 Var1", text);
    }
}
