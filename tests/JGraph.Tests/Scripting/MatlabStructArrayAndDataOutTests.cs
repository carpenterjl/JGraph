using System.IO;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M65 waves A and B: struct arrays with storage of their own, and the writers that finally let a
/// script put an answer somewhere.
/// </summary>
/// <remarks>
/// The struct-array cases here are all versions of one question — whether every element has every
/// field — because that invariant is the reason the type stopped being a cell that happened to hold
/// structs. A cell can hold three structs with three different field sets; a struct array cannot,
/// and nothing about the cell representation could say so.
/// </remarks>
[Collection("JG facade")]
public class MatlabStructArrayAndDataOutTests : IDisposable
{
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder = Directory.CreateTempSubdirectory("jgraph-m65-").FullName;

    public MatlabStructArrayAndDataOutTests() => JG.Reset();

    public void Dispose()
    {
        JG.Reset();
        Directory.Delete(_folder, recursive: true);
    }

    private string RunAndRead(string code)
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText;
    }

    private string RunInFolder(string code)
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), _folder);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText;
    }

    private string Error(string code)
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), _folder);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.False(result.Success, "expected a refusal, got: " + _output.NormalText);
        return result.Message + _output.ErrorText;
    }

    // --- Wave A: the type ---------------------------------------------------------------------

    [Fact]
    public void AStructArrayIsAStructAndNotACell()
    {
        // The headline flip. Before M65 this printed 'cell 1', because a struct array was a cell and
        // numel counted the one thing the cell was.
        Assert.Equal("struct 3 0\n", RunAndRead("""
            S = struct('a', {1, 2, 3});
            fprintf('%s %d %d\n', class(S), numel(S), iscell(S));
            """));
    }

    [Fact]
    public void AScalarStructIsTheOneElementCase()
    {
        Assert.Equal("struct 1 1\n", RunAndRead("""
            s = struct('a', 1);
            fprintf('%s %d %d\n', class(s), numel(s), isscalar(s));
            """));
    }

    [Fact]
    public void ACellValueSpreadsAcrossElementsAndABracedOneDoesNot()
    {
        // The whole difference between struct('c', cell(2,2)) and struct('c', {cell(2,2)}), which is
        // the spelling a frozen stress script had to be corrected to.
        Assert.Equal("4 1 2 2\n", RunAndRead("""
            spread = struct('c', cell(2, 2));
            held = struct('c', {cell(2, 2)});
            fprintf('%d %d %d %d\n', numel(spread), numel(held), size(held.c, 1), size(held.c, 2));
            """));
    }

    [Fact]
    public void AnEmptyStructArrayRemembersItsFields()
    {
        // There is no element to read the names off, which is why the payload keeps them separately.
        Assert.Equal("0 1 1\n", RunAndRead("""
            E = struct('a', {});
            fprintf('%d %d %d\n', numel(E), isfield(E, 'a'), numel(fieldnames(E)));
            """));
    }

    [Fact]
    public void WritingOneElementsFieldGivesItToEveryElement()
    {
        Assert.Equal("3 1 1 1\n", RunAndRead("""
            S = struct('a', {1, 2, 3});
            S(2).b = 99;
            fprintf('%d %d %d %d\n', numel(S), isfield(S, 'b'), isempty(S(1).b), isempty(S(3).b));
            """));
    }

    [Fact]
    public void GrowingByEndPlusOneAppends()
    {
        // `end` inside the subscript counts what is already there, which is what used to be missing:
        // the accumulation idiom was refused because nothing told `end` what it was inside of.
        Assert.Equal("4 10 40\n", RunAndRead("""
            G = struct('n', {});
            for k = 1:4
                G(end+1).n = k * 10;
            end
            fprintf('%d %d %d\n', numel(G), G(1).n, G(4).n);
            """));
    }

    [Fact]
    public void GrowingPastTheEndFillsTheGapWithEveryField()
    {
        Assert.Equal("6 1 60\n", RunAndRead("""
            G = struct('n', {1});
            G(6).n = 60;
            fprintf('%d %d %d\n', numel(G), isempty(G(3).n), G(6).n);
            """));
    }

    [Fact]
    public void StackedRowsKeepTheirShape()
    {
        // [S; S] of two 1-by-3 arrays is 2-by-3. Appending the elements instead gives a column of
        // six, which is what this did until the stress script asked.
        Assert.Equal("2 3 3 1\n", RunAndRead("""
            S = struct('a', {1, 2, 3});
            M = [S; S];
            fprintf('%d %d %d %d\n', size(M, 1), size(M, 2), M(2,3).a, M(1,1).a);
            """));
    }

    [Fact]
    public void SideBySideJoinsRunAlong()
    {
        Assert.Equal("1 6 1\n", RunAndRead("""
            S = struct('a', {1, 2, 3});
            W = [S, S];
            fprintf('%d %d %d\n', size(W, 1), size(W, 2), W(4).a);
            """));
    }

    [Fact]
    public void ConcatenationUnionsTheFields()
    {
        Assert.Equal("2 1 1 1\n", RunAndRead("""
            U = [struct('a', 1) struct('b', 2)];
            fprintf('%d %d %d %d\n', numel(U), isfield(U, 'a'), isfield(U, 'b'), isempty(U(1).b));
            """));
    }

    [Fact]
    public void MismatchedRowsRefuseRatherThanFlatten()
    {
        Assert.Contains("same number of columns", Error("""
            a = struct('v', {1, 2, 3});
            b = struct('v', {1, 2});
            c = [a; b];
            """));
    }

    [Fact]
    public void AFieldAcrossTheElementsDistributesAcrossOutputs()
    {
        // The comma-separated list M61 built, reaching multi-assign over a slice rather than only
        // over a whole name.
        Assert.Equal("1 2\n", RunAndRead("""
            S = struct('a', {1, 2, 3});
            [first, second] = S(1:2).a;
            fprintf('%d %d\n', first, second);
            """));
    }

    [Fact]
    public void DeletingAnElementKeepsTheOrderOfTheSurvivors()
    {
        Assert.Equal("2 1 3\n", RunAndRead("""
            S = struct('a', {1, 2, 3});
            S(2) = [];
            fprintf('%d %d %d\n', numel(S), S(1).a, S(2).a);
            """));
    }

    [Fact]
    public void RmfieldWorksAcrossTheWholeArray()
    {
        Assert.Equal("3 0 1\n", RunAndRead("""
            S = struct('a', {1, 2, 3}, 'b', {'x', 'y', 'z'});
            R = rmfield(S, 'b');
            fprintf('%d %d %d\n', numel(R), isfield(R, 'b'), numel(fieldnames(R)));
            """));
    }

    [Fact]
    public void ForWalksTheElementsOneAtATime()
    {
        Assert.Equal("6 3\n", RunAndRead("""
            S = struct('a', {1, 2, 3});
            total = 0; scalars = 0;
            for e = S
                total = total + e.a;
                scalars = scalars + isscalar(e);
            end
            fprintf('%d %d\n', total, scalars);
            """));
    }

    [Fact]
    public void AFieldWriteMustNameAnElement()
    {
        // Refused rather than guessed at, because setting one field across many elements has no
        // meaning that keeps the invariant.
        Assert.Contains("name an element", Error("""
            S = struct('a', {1, 2, 3});
            S.a = 5;
            """));
    }

    [Fact]
    public void AStructArrayDisplaysItsShapeRatherThanItsContents()
    {
        Assert.Contains("1x3 struct array with fields: a, b", RunAndRead("""
            S = struct('a', {1, 2, 3}, 'b', {4, 5, 6});
            disp(S)
            """));
    }

    // --- Wave B: writing ----------------------------------------------------------------------

    [Fact]
    public void WritematrixAndReadmatrixRoundTrip()
    {
        Assert.Equal("2 3 1 6\n", RunInFolder("""
            A = [1 2 3; 4 5 6];
            writematrix(A, 'plain.csv');
            B = readmatrix('plain.csv');
            fprintf('%d %d %d %d\n', size(B, 1), size(B, 2), B(1,1), B(2,3));
            """));
    }

    [Fact]
    public void ADelimiterAndAnAppendModeAreHonoured()
    {
        Assert.Equal("6 3 3\n", RunInFolder("""
            A = [1 2 3; 4 5 6];
            writematrix(A, 'tabbed.txt', 'Delimiter', 'tab');
            B = readmatrix('tabbed.txt', 'Delimiter', 'tab');
            writematrix([7 8 9], 'tabbed.txt', 'Delimiter', 'tab', 'WriteMode', 'append');
            C = readmatrix('tabbed.txt', 'Delimiter', 'tab');
            fprintf('%d %d %d\n', B(2,3), size(C, 1), size(C, 2));
            """));
    }

    [Fact]
    public void ReadcellKeepsNumbersNumbersAndEverythingElseText()
    {
        Assert.Equal("1 1 two 1\n", RunInFolder("""
            writecell({1, 'two'; 3, 'four'}, 'mixed.csv');
            C = readcell('mixed.csv');
            fprintf('%d %d %s %d\n', C{1,1}, isnumeric(C{1,1}), C{1,2}, ischar(C{1,2}));
            """));
    }

    [Fact]
    public void WritetableWritesItsHeaderUnlessToldNotTo()
    {
        Assert.Equal("id,code 2\n", RunInFolder("""
            T = table([1; 2], {'a'; 'b'}, 'VariableNames', {'id', 'code'});
            writetable(T, 'headed.csv');
            writetable(T, 'bare.csv', 'WriteVariableNames', false);
            lines = readlines('headed.csv');
            fprintf('%s %d\n', lines(1), size(readmatrix('bare.csv'), 1));
            """));
    }

    [Fact]
    public void WritelinesAndReadlinesAreEachOthersInverse()
    {
        Assert.Equal("3 gamma 1\n", RunInFolder("""
            writelines(["alpha"; "beta"; "gamma"], 'notes.txt');
            back = readlines('notes.txt');
            fprintf('%d %s %d\n', numel(back), back(3), isstring(back));
            """));
    }

    [Fact]
    public void ASpreadsheetNameIsRefusedRatherThanWrittenAsText()
    {
        // The failure this prevents is not an error at all: a .xlsx holding comma-separated text
        // opens in a spreadsheet program and is wrong.
        Assert.Contains("not spreadsheets", Error("writematrix([1 2], 'out.xlsx');"));
    }

    [Fact]
    public void StructArraysAndTablesConvertBothWays()
    {
        Assert.Equal("table 3 2 struct 3 3\n", RunAndRead("""
            S = struct('a', {1, 2, 3}, 'b', {4, 5, 6});
            T = struct2table(S);
            B = table2struct(T);
            fprintf('%s %d %d %s %d %d\n', class(T), height(T), width(T), class(B), numel(B), B(3).a);
            """));
    }
}
