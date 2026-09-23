using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V6 (ADR 0167), table forms (appendix A #117, #121, #122): <c>T(rows, vars) = rhs</c> with a
/// table or a cell on the right, row and variable deletion, <c>[T; U]</c> and <c>[T U]</c>,
/// <c>table2array</c>, <c>addvars</c>, <c>varfun</c>, <c>rowfun</c>, and a string-array
/// variable that stays one.
/// </summary>
/// <remarks>
/// The parity fixture <c>table_forms</c> holds R2025b's answers for every form; these pin the
/// roads the sub-stage touched, one assertion a road, so a regression names itself.
/// </remarks>
[Collection("JG facade")]
public class TableFormsM167Tests : IDisposable
{
    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();

    public TableFormsM167Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string RunAndRead(string code)
    {
        ScriptRunResult result = _engine.RunAsync(
            code,
            new ScriptContext(_output, (_, _) => { }, Path.GetTempPath(), resolvePath: null, figureFiles: new TestFigureFiles()),
            CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim().Replace("\r", "");
    }

    private string RunForError(string code)
    {
        ScriptRunResult result = _engine.RunAsync(
            code,
            new ScriptContext(_output, (_, _) => { }, Path.GetTempPath(), resolvePath: null, figureFiles: new TestFigureFiles()),
            CancellationToken.None).GetAwaiter().GetResult();
        Assert.False(result.Success);
        return result.Message ?? string.Empty;
    }

    private const string Two = "T = table([1; 2], {'a'; 'b'});";

    private const string Show = "fprintf('%d %s %s\\n', height(T), mat2str(T.Var1'), strjoin(T.Var2', ','));";

    // ---- T(rows, vars) = rhs ----------------------------------------------------------------------

    [Theory]
    [InlineData("T(1, :) = table(9, {'z'});", "2 [9 2] z,b")]
    [InlineData("T(1, :) = {9, 'z'};", "2 [9 2] z,b")]
    [InlineData("T(end + 1, :) = {3, 'c'};", "3 [1 2 3] a,b,c")]
    [InlineData("T(1:2, 1) = {7};", "2 [7 7] a,b")]
    [InlineData("T(:, 'Var1') = {9; 8};", "2 [9 8] a,b")]
    [InlineData("T(logical([0 1]), :) = {5, 'q'};", "2 [1 5] a,q")]
    [InlineData("T(1, :) = table(9, {'z'}, 'VariableNames', {'p', 'q'});", "2 [9 2] z,b")]
    [InlineData("T(1, :) = T(2, :);", "2 [2 2] b,b")]
    public void AParenRowWriteTakesATableOrACellByPosition(string write, string expected)
    {
        string text = RunAndRead($"{Two}\n{write}\n{Show}");
        Assert.Equal(expected, text);
    }

    [Fact]
    public void AOneVariableTableOrAOneElementCellExpandsOverTheSelection()
    {
        string text = RunAndRead("""
            T = table([1; 2], [3; 4]);
            T(1, :) = table(9);
            U = table([1; 2], [3; 4]);
            U(:, :) = {0};
            fprintf('%s %s | %s %s\n', mat2str(T.Var1'), mat2str(T.Var2'), mat2str(U.Var1'), mat2str(U.Var2'));
            """);
        Assert.Equal("[9 2] [9 4] | [0 0] [0 0]", text);
    }

    [Fact]
    public void AWritePastTheEndGrowsEveryVariableAndTheRowNames()
    {
        string text = RunAndRead("""
            T = table(int8([1; 2]), [true; false], 'RowNames', {'a'; 'b'});
            T(4, :) = {4, true};
            fprintf('%s %s %s %s\n', class(T.Var1), mat2str(T.Var1'), mat2str(T.Var2'), strjoin(T.Properties.RowNames', ','));
            U = table([1; 2], 'RowNames', {'a'; 'b'});
            U('c', :) = {3};
            fprintf('%s %s\n', mat2str(U.Var1'), strjoin(U.Properties.RowNames', ','));
            """);
        Assert.Equal("int8 [1 2 0 4] [true false false true] a,b,Row3,Row4\n[1 2 3] a,b,c", text);
    }

    [Fact]
    public void ANewVariableIsMadeByNameOrPosition()
    {
        string text = RunAndRead($$"""
            {{Two}}
            T(1, 'New') = {7};
            T(:, 4) = {5; 6};
            fprintf('%s %s %s\n', strjoin(T.Properties.VariableNames, ','), mat2str(T.New'), mat2str(T.Var4'));
            """);
        Assert.Equal("Var1,Var2,New,Var4 [7 0] [5 6]", text);
    }

    [Fact]
    public void ACellRightHandSideIsReadAsATableFirst()
    {
        // A column of chars is a text variable; a number into a text variable is refused as MATLAB refuses it.
        string text = RunAndRead($$"""
            {{Two}}
            T(1, :) = {5, 'zz'};
            fprintf('%s %s\n', mat2str(T.Var1'), strjoin(T.Var2', ','));
            U = table(int8([1; 2]));
            U(1, 1) = {3.7};
            fprintf('%s %s\n', class(U.Var1), mat2str(U.Var1'));
            """);
        Assert.Equal("[5 2] zz,b\nint8 [4 2]", text);
        Assert.Contains("Conversion to cell from double is not possible.", RunForError($"{Two}\nT(1, :) = {{5}};"));
        Assert.Contains("value of type 'string' is not convertible to 'cell'", RunForError($"{Two}\nT(1, :) = {{5, \"s\"}};"));
    }

    [Theory]
    [InlineData("T(1, :) = 5;", "Right hand side of an assignment into a table must be another table or a cell array.")]
    [InlineData("T(1, :) = {1, 2, 3};", "The number of table variables in an assignment must match.")]
    [InlineData("T(1:2, :) = table(9, {'z'});", "To assign to or create a variable in a table, the number of rows must match the height of the table.")]
    [InlineData("T(1, 1) = [];", "At least one subscript must be ':' when you delete rows or variables by assigning [].")]
    [InlineData("T(0, :) = [];", "Array indices must be positive integers or logical values.")]
    [InlineData("T(3) = [];", "Subscripting into a table using one subscript (as in t(i)) is not supported.")]
    public void TheRefusalsAreMatlabs(string write, string expected)
    {
        Assert.Contains(expected, RunForError($"{Two}\n{write}"));
    }

    [Fact]
    public void ARefusedWriteLeavesTheTableAsItWas()
    {
        string text = RunAndRead($$"""
            {{Two}}
            try
                T(1, :) = {5, 7};
            catch
            end
            {{Show}}
            """);
        Assert.Equal("2 [1 2] a,b", text);
    }

    // ---- deletion ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("U(2, :) = [];", "1 1 a | 2")]
    [InlineData("U(U.Var1 > 1, :) = [];", "1 1 a | 2")]
    [InlineData("U(:, :) = [];", "0 zeros(1,0)  | 2")]
    [InlineData("U(:, 'Var2') = [];", "2 double Var1 | 2")]
    [InlineData("U(:, [true false]) = [];", "2 cell Var2 | 2")]
    public void DeletionRemovesRowsOrVariablesAndLeavesTheAlias(string write, string expected)
    {
        string text = RunAndRead($$"""
            {{Two}}
            U = T;
            {{write}}
            if width(U) == 2
                fprintf('%d %s %s | %d\n', height(U), mat2str(U.Var1'), strjoin(U.Var2', ','), height(T));
            else
                fprintf('%d %s %s | %d\n', height(U), class(U{:, 1}), U.Properties.VariableNames{1}, height(T));
            end
            """);
        Assert.Equal(expected, text);
    }

    [Fact]
    public void DeletionKeepsRowNamesRowTimesAndTheOtherProperties()
    {
        string text = RunAndRead("""
            T = table([1; 2; 3], 'RowNames', {'a'; 'b'; 'c'});
            T.Properties.Description = 'd';
            T({'a', 'c'}, :) = [];
            fprintf('%s %s %s\n', mat2str(T.Var1'), strjoin(T.Properties.RowNames', ','), T.Properties.Description);
            TT = timetable(seconds([1; 2; 3]), [1; 2; 3]);
            TT(2, :) = [];
            TT(end + 1, :) = {5};
            fprintf('%s %s %s\n', class(TT), mat2str(TT.Var1'), mat2str(seconds(TT.Time)'));
            """);
        Assert.Equal("2 b d\ntimetable [1 3 5] [1 3 NaN]", text);
    }

    [Fact]
    public void AParenWriteThroughAFieldOrACellStoresBack()
    {
        string text = RunAndRead($$"""
            st.T = table([1; 2]);
            st.T(1, :) = {9};
            c = {table([1; 2; 3])};
            c{1}(2, :) = [];
            fprintf('%s %s\n', mat2str(st.T.Var1'), mat2str(c{1}.Var1'));
            """);
        Assert.Equal("[9 2] [1 3]", text);
    }

    // ---- [T; U] and [T U] -------------------------------------------------------------------------

    [Fact]
    public void StackingMatchesVariablesByNameInTheFirstTablesOrder()
    {
        string text = RunAndRead("""
            A = table([1; 2], {'a'; 'b'}, 'VariableNames', {'n', 't'});
            B = table({'c'}, 3, 'VariableNames', {'t', 'n'});
            C = [A; B];
            C.n(1) = 9;
            fprintf('%s %s %s | %s\n', strjoin(C.Properties.VariableNames, ','), mat2str(C.n'), strjoin(C.t', ','), mat2str(A.n'));
            D = [A; {4, 'd'}; []];
            fprintf('%d %s\n', height(D), strjoin(D.t', ','));
            """);
        Assert.Equal("n,t [9 2 3] a,b,c | [1 2]\n3 a,b,d", text);
    }

    [Fact]
    public void StackingJoinsEachVariableByTheBracketsRules()
    {
        string text = RunAndRead("""
            A = table(int8([1; 2])); B = table([300; 4]);
            C = [A; B];
            S = [table(["a"; "b"]); table(["c"; "d"])];
            TT = timetable(seconds([1; 2]), [1; 2]);
            U = [TT; table([3; 4])];
            fprintf('%s %s | %s %d | %s %s\n', class(C.Var1), mat2str(C.Var1'), class(S.Var1), height(S), class(U), mat2str(seconds(U.Time)'));
            """);
        Assert.Equal("int8 [1 2 127 4] | string 4 | timetable [1 2 NaN NaN]", text);
    }

    [Fact]
    public void StackingKeepsRowNamesFromEitherSideAndTheFirstTablesMetadata()
    {
        string text = RunAndRead("""
            A = table([1; 2], 'RowNames', {'a'; 'b'}); A.Properties.Description = 'd';
            B = table([3; 4]); B.Properties.Description = 'e';
            C = [B; A];
            fprintf('%s [%s]\n', strjoin(C.Properties.RowNames', ','), C.Properties.Description);
            """);
        Assert.Equal("Row1,Row2,a,b [e]", text);
    }

    [Theory]
    [InlineData("[table([1; 2]); table([3; 4], 'VariableNames', {'x'})]", "All tables being vertically concatenated must have the same variable names.")]
    [InlineData("[table([1; 2]); table([3; 4], [5; 6])]", "All tables being vertically concatenated must have the same number of variables.")]
    [InlineData("[table([1; 2]); table({'a'; 'b'})]", "Unable to concatenate the table variable 'Var1' because it is a cell array in one table and not a cell array in another.")]
    [InlineData("[table([1; 2], 'RowNames', {'a'; 'b'}); table([3; 4], 'RowNames', {'a'; 'x'})]", "Duplicate table row name: 'a'.")]
    [InlineData("[table([1; 2]); 5]", "All input arguments must be tables or cell arrays.")]
    [InlineData("[table([1; 2]) table([3; 4])]", "Duplicate table variable name: 'Var1'.")]
    [InlineData("[table([1; 2]) table(3, 'VariableNames', {'t'})]", "All tables being horizontally concatenated must have the same number of rows.")]
    [InlineData("[table([1; 2], 'RowNames', {'a'; 'b'}) table([3; 4], 'VariableNames', {'y'}, 'RowNames', {'c'; 'd'})]", "All tables being horizontally concatenated must have the same row names.")]
    public void ConcatenationRefusesInMatlabsWords(string bracket, string expected)
    {
        Assert.Contains(expected, RunForError($"x = {bracket};"));
    }

    [Fact]
    public void JoiningAcrossPutsVariablesSideBySideAndTakesACellAsVariables()
    {
        string text = RunAndRead("""
            A = table([1; 2], 'RowNames', {'a'; 'b'});
            B = table({'a'; 'b'}, 'VariableNames', {'t'});
            C = [A B {3; 4}];
            C.t{1} = 'z';
            TT = [timetable(seconds([1; 2]), [1; 2]) B];
            fprintf('%s %s %s | %s | %s %s\n', strjoin(C.Properties.VariableNames, ','), strjoin(C.Properties.RowNames', ','), mat2str(C.Var3'), strjoin(B.t', ','), class(TT), strjoin(horzcat(A, B).Properties.VariableNames, ','));
            """);
        Assert.Equal("Var1,t,Var3 a,b [3 4] | a,b | timetable Var1,t", text);
    }

    // ---- the verbs --------------------------------------------------------------------------------

    [Fact]
    public void Table2ArrayJoinsTheVariablesAndRefusesAMix()
    {
        string text = RunAndRead("""
            fprintf('%s %s %s %s\n', mat2str(table2array(table([1 2; 3 4], [5; 6]))), class(table2array(table(int8([1; 2]), [300; 4]))), class(table2array(table({'a'; 'b'}))), class(table2array(table(["a"; "b"]))));
            T = table([1; 2]); A = table2array(T); A(1) = 9;
            fprintf('%s\n', mat2str(T.Var1'));
            """);
        Assert.Equal("[1 2 5;3 4 6] int8 cell string\n[1 2]", text);
        Assert.Contains("Unable to concatenate the table variables 'Var1' and 'Var2', because their types are double and cell.",
            RunForError("table2array(table([1; 2], {'a'; 'b'}));"));
    }

    [Fact]
    public void AddVarsNamesAVariableAfterItsExpressionOrItsPositionMadeUnique()
    {
        string text = RunAndRead("""
            T = table([1; 2], [3; 4]); x = [5; 6];
            fprintf('%s | %s | %s | %s | %s\n', ...
                strjoin(addvars(T, x).Properties.VariableNames, ','), ...
                strjoin(addvars(T, x * 2, 'NewVariableNames', 'w').Properties.VariableNames, ','), ...
                strjoin(addvars(T, [5; 6], 'Before', 'Var2').Properties.VariableNames, ','), ...
                strjoin(addvars(T, [5; 6], 'Before', 1).Properties.VariableNames, ','), ...
                strjoin(addvars(timetable(seconds([1; 2]), [1; 2]), x).Properties.VariableNames, ','));
            U = addvars(T, x); U.x(1) = 9;
            fprintf('%d %s\n', width(T), mat2str(x'));
            """);
        Assert.Equal("Var1,Var2,x | Var1,Var2,w | Var1,Var2_1,Var2 | Var1_1,Var1,Var2 | Var1,x\n2 [5 6]", text);
        Assert.Contains("Duplicate table variable name: 'Var1'.", RunForError("T = table([1; 2]); addvars(T, [5; 6], 'NewVariableNames', 'Var1');"));
        Assert.Contains("the number of rows must match the height of the table", RunForError("T = table([1; 2]); addvars(T, [5; 6; 7]);"));
    }

    [Fact]
    public void VarFunAppliesToEachVariableWhole()
    {
        string text = RunAndRead("""
            T = table([1; 2], [3; 4], 'RowNames', {'a'; 'b'}); T.Properties.Description = 'd';
            V = varfun(@(x) x * 2, T);
            W = varfun(@mean, T, 'InputVariables', {'Var2', 'Var1'});
            TT = varfun(@mean, timetable(seconds([1; 2]), [1; 2]));
            fprintf('%s %s %d [%s] | %s %s | %s %s | %s | %s\n', strjoin(V.Properties.VariableNames, ','), mat2str(V.Fun_Var1'), isempty(V.Properties.RowNames), V.Properties.Description, ...
                strjoin(W.Properties.VariableNames, ','), mat2str([W.mean_Var2 W.mean_Var1]), ...
                class(TT), mat2str(seconds(TT.Time)'), mat2str(varfun(@mean, T, 'OutputFormat', 'uniform')), class(varfun(@mean, T, 'OutputFormat', 'cell')));
            """);
        Assert.Equal("Fun_Var1,Fun_Var2 [2 4] 1 [d] | mean_Var2,mean_Var1 [3.5 1.5] | timetable 1 | [1.5 3.5] | cell", text);
        Assert.Contains("returned a non-scalar value when applied to the variable 'Var1'", RunForError("varfun(@(x) x * 2, table([1; 2]), 'OutputFormat', 'uniform');"));
    }

    [Fact]
    public void RowFunCallsOncePerRowWithEachVariablesRow()
    {
        string text = RunAndRead("""
            T = table([1; 2], {'a'; 'bb'}, 'RowNames', {'r'; 's'});
            R = rowfun(@(a, t) a + numel(t{1}), T);
            C = rowfun(@(a, t) sprintf('%s:%d', class(t), numel(t)), T, 'OutputFormat', 'cell', 'ExtractCellContents', true);
            U = rowfun(@(a, b) deal(a + b, a * b), table([1; 2], [3; 4]), 'NumOutputs', 2, 'OutputVariableNames', {'p', 'q'});
            S = rowfun(@(r) sum(r), table([1; 2], [3; 4]), 'SeparateInputs', false, 'OutputFormat', 'uniform');
            M = rowfun(@(m, v) sum(m) + v, table([1 2; 3 4], [5; 6]), 'OutputFormat', 'uniform');
            TT = rowfun(@(a) a * 2, timetable(seconds([1; 2]), [1; 2]));
            fprintf('%s %s %s | %s | %s %s %s | %s | %s | %s %s\n', strjoin(R.Properties.VariableNames, ','), mat2str(R.Var1'), strjoin(R.Properties.RowNames', ','), strjoin(C', ','), ...
                strjoin(U.Properties.VariableNames, ','), mat2str(U.p'), mat2str(U.q'), mat2str(S'), mat2str(M'), class(TT), mat2str(seconds(TT.Time)'));
            """);
        Assert.Equal("Var1 [2 4] r,s | char:1,char:2 | p,q [4 6] [3 8] | [4 6] | [8 13] | timetable [1 2]", text);
        Assert.Contains("returned a non-scalar value when applied to the 1st row", RunForError("rowfun(@(a, b) [a b], table([1; 2], [3; 4]), 'OutputFormat', 'uniform');"));
        Assert.Contains("'GroupingVariables' is not supported here", RunForError("rowfun(@(a) a, table([1; 2]), 'GroupingVariables', 'Var1');"));
    }

    [Fact]
    public void RowFunRunsScriptCodeInsideAHeldOperand()
    {
        // M5: rowfun runs the handle, so `g + f()` holds g as it was when the read began (#122).
        string text = RunAndRead("""
            global tf_g
            tf_g = [1 2];
            r = tf_g + rowfun_zero();
            fprintf('%s %s\n', mat2str(r), mat2str(tf_g));
            function y = bump(x)
            global tf_g
            tf_g(1) = 7;
            y = x;
            end
            function z = rowfun_zero()
            out = rowfun(@bump, table(1), 'OutputFormat', 'uniform');
            z = out * 0;
            end
            """);
        Assert.Equal("[1 2] [7 2]", text);
        Assert.Contains("rowfun", JgsBuiltins.ScriptRunningBuiltins);
        Assert.Contains("varfun", JgsBuiltins.ScriptRunningBuiltins);
    }

    // ---- a string-array variable -------------------------------------------------------------------

    [Fact]
    public void AStringArrayVariableStaysAStringArray()
    {
        string text = RunAndRead("""
            T = table(["a"; "b"]);
            U = T; U.Var1(1) = "z";
            T(3, :) = {"c"};
            fprintf('%s %s %s %s %s | %d %s\n', class(T.Var1), class(T{1, 1}), class(U.Var1), U.Var1(1), T.Var1(3), istable(T), class(timetable(seconds(1), 1)));
            """);
        Assert.Equal("string string string z c | 1 timetable", text);
    }
}
