using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Subscripting a table the way MATLAB does (M51): <c>T{rows, vars}</c> for the variables' contents and
/// <c>T(rows, vars)</c> for a smaller table. Before this, a table answered only to <c>T.Var</c> — braces
/// and parentheses both threw, so the ordinary way to pull a column out of an imported file did not work.
/// </summary>
[Collection("JG facade")]
public class MatlabTableIndexingTests : IDisposable
{
    private const string Csv = """
        SN,TEMP,VOLTS
        A1,100,3.3
        A2,110,3.4
        A1,120,3.5
        """;

    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"jgraph-table-{Guid.NewGuid():N}.csv");

    public MatlabTableIndexingTests()
    {
        JG.Reset();
        File.WriteAllText(_path, Csv);
    }

    public void Dispose()
    {
        JG.Reset();
        File.Delete(_path);
    }

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    /// <summary>Runs the code with <c>T</c> already bound to the fixture table.</summary>
    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = NewSession();
        string prologue = $"T = readtable('{_path.Replace(@"\", @"\\")}');\n";
        ScriptRunResult result = await session.ExecuteAsync(prologue + code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    [Fact]
    public Task Braces_ReadATextVariableAsACellOfChar() => RunAsserting("""
        sn = T{:,1};
        assert(iscell(sn));
        assert(isequal(size(sn), [3 1]));
        assert(strcmp(sn{1,1}, 'A1'));
        assert(strcmp(sn{3,1}, 'A1'));
        """);

    [Fact]
    public Task Braces_ReadANumericVariableAsAColumnVector() => RunAsserting("""
        t = T{:,2};
        assert(isequal(size(t), [3 1]));
        assert(isequal(t, [100; 110; 120]));
        """);

    [Fact]
    public Task BracesOnOneCell_GiveTheNumberItself() => RunAsserting("""
        v = T{2,3};
        assert(isnumeric(v));
        assert(isequal(size(v), [1 1]));
        assert(abs(v - 3.4) < 1e-12);
        """);

    [Fact]
    public Task BracesOverSeveralNumericVariables_LayThemSideBySide() => RunAsserting("""
        m = T{:, [2 3]};
        assert(isequal(size(m), [3 2]));
        assert(isequal(m(:,1), [100; 110; 120]));
        assert(abs(m(3,2) - 3.5) < 1e-12);
        """);

    [Fact]
    public Task SubscriptsTakeRangesEndAndMasks() => RunAsserting("""
        assert(isequal(T{1:2, 2}, [100; 110]));
        assert(isequal(T{end, 2}, 120));
        assert(isequal(T{:, end}, [3.3; 3.4; 3.5]));
        mask = [true; false; true];
        assert(isequal(T{mask, 2}, [100; 120]));
        """);

    [Fact]
    public Task BracesRefuseToMixTextWithNumbers() => RunAsserting("""
        ok = 0;
        try
            T{:, [1 2]};
        catch err
            ok = ok + ~isempty(strfind(err.message, 'cannot mix text with numbers'));
        end
        try
            T{1};
        catch err
            ok = ok + ~isempty(strfind(err.message, 'two subscripts'));
        end
        assert(ok == 2);
        """);

    [Fact]
    public Task ParenthesesGiveASmallerTable() => RunAsserting("""
        s = T(1:2, [1 3]);
        assert(strcmp(class(s), 'table'));
        assert(height(s) == 2);
        assert(width(s) == 2);
        assert(isequal(s{:,2}, [3.3; 3.4]));
        assert(strcmp(s{1,1}{1,1}, 'A1'));
        """);

    [Fact]
    public Task VariablesAnswerToTheirNames() => RunAsserting("""
        one = T(:, 'VOLTS');
        assert(width(one) == 1);
        assert(isequal(one{:,1}, [3.3; 3.4; 3.5]));
        two = T(:, {'SN','TEMP'});
        assert(width(two) == 2);
        assert(isequal(two{:,2}, [100; 110; 120]));
        assert(isequal(T{:, 'TEMP'}, [100; 110; 120]));
        ok = 0;
        try
            T(:, 'NOPE');
        catch err
            ok = ok + ~isempty(strfind(err.message, "no variable 'NOPE'"));
        end
        assert(ok == 1);
        """);

    [Fact]
    public Task UniqueSortsAndDedupesACellOfText() => RunAsserting("""
        u = unique(T{:,1});
        assert(iscell(u));
        assert(isequal(size(u), [2 1]));
        assert(strcmp(u{1,1}, 'A1'));
        assert(strcmp(u{2,1}, 'A2'));
        assert(isequal(unique([3 1 3 2]), [1 2 3]));
        """);

    [Fact]
    public Task TheWholeReadColumnsGroupLoop_Runs() => RunAsserting("""
        serials = T{:,1};
        temps = T{:,2};
        parts = unique(serials);
        totals = zeros(numel(parts), 1);
        for i = 1:numel(parts)
            rows = find(matches(serials, parts{i,1}));
            totals(i) = sum(temps(rows));
        end
        assert(isequal(totals, [220; 110]));
        """);
}
