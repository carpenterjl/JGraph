using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open items 317 and 319 (ADR 0358): a containers.Map joins by its class's own verbs and refuses a for
/// loop, cell2struct takes a dimension and a cell of any size, a metaproperty with no default raises on
/// DefaultValue, a metaclass's lists reach past two levels, an object or a map inside a struct or a cell
/// shows its size and class, and mustBeNonzeroLengthText is a builtin. Every expectation was measured in
/// R2025b (probes p317 to p317e, p319, p319b); the parity fixtures oi_map_concat_cell2struct and
/// oi_metadata_nested_display carry the rest.
/// </summary>
[Collection("JG facade")]
public class MapStructMetadataLeftoversTests : IDisposable
{
    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public MapStructMetadataLeftoversTests()
    {
        JG.Reset();
        _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "LoVal.m"), """
            classdef LoVal
                properties
                    A = 1
                    B
                end
                methods
                    function r = twice(obj)
                        r = 2 * obj.A;
                    end
                end
            end
            """);
    }

    public void Dispose()
    {
        JG.Reset();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    private ScriptRunResult Run(string code) =>
        _engine.RunAsync(
            code,
            new ScriptContext(_output, (_, _) => { }, _directory, resolvePath: null, figureFiles: new TestFigureFiles()),
            CancellationToken.None).GetAwaiter().GetResult();

    private string RunAndRead(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText;
    }

    [Fact]
    public void StackedMapsMergeIntoANewMapAndALaterKeyWins()
    {
        Assert.Equal(
            "a b c\n1 20 30\n2 2 4\n",
            RunAndRead("""
                m1 = containers.Map({'a', 'b'}, {1, 2});
                m2 = containers.Map({'b', 'c'}, {20, 30});
                m = [m1; m2];
                disp(strjoin(keys(m), ' '))
                v = values(m);
                fprintf('%d %d %d\n', v{:})
                m('d') = 4;
                fprintf('%d %d %d\n', m1.Count, m2.Count, m.Count)
                """));
    }

    [Fact]
    public void MapsSideBySideAndAForLoopOverAMapAreRefused()
    {
        Assert.Equal(
            "MATLAB:Containers:Map:HorzcatNotSupported\nMATLAB:Containers:Map:IncorrectIndexing\nMATLAB:Containers:TypeMismatch\nMATLAB:class:RequireClass\n",
            RunAndRead("""
                m1 = containers.Map({'a'}, {1});
                try, x = [m1 m1]; catch e, disp(e.identifier), end
                try, for k = m1, end, catch e, disp(e.identifier), end
                try, x = [m1; containers.Map({'z'}, {'text'})]; catch e, disp(e.identifier), end
                try, x = [m1; 5]; catch e, disp(e.identifier), end
                """));
    }

    [Fact]
    public void CellToStructTakesADimensionAndACellOfAnySize()
    {
        Assert.Equal(
            "3 4\n2 3\n20 22 24\n3 1\nMATLAB:Cell2Struct:NumberOfNamesMismatch\n",
            RunAndRead("""
                c3 = reshape(num2cell(1:24), 2, 3, 4);
                fprintf('%d %d\n', size(cell2struct(c3, {'a', 'b'}, 1)))
                fprintf('%d %d\n', size(cell2struct(c3, {'a', 'b', 'c', 'd'}, 3)))
                s = cell2struct(c3, {'a', 'b', 'c'}, 2);
                fprintf('%d %d %d\n', s(2, 4).a, s(2, 4).b, s(2, 4).c)
                fprintf('%d %d\n', size(cell2struct({1, 2, 3; 4, 5, 6}, {'a', 'b'}, 1)))
                try, cell2struct({1, 2}, {'a', 'b'}); catch e, disp(e.identifier), end
                """));
    }

    [Fact]
    public void AMetapropertyWithNoDefaultRaisesAndTheListsGoDeep()
    {
        Assert.Equal(
            "MATLAB:class:NoDefaultDefined\nNo default value has been defined for property B.\n1\nLoVal\n",
            RunAndRead("""
                mc = ?LoVal;
                p = mc.PropertyList(2);
                try, v = p.DefaultValue; catch e, disp(e.identifier), disp(e.message), end
                fprintf('%d\n', mc.PropertyList(1).DefaultValue)
                disp(mc.MethodList(1).DefiningClass.PropertyList(1).DefiningClass.MethodList(1).DefiningClass.Name)
                """));
    }

    [Fact]
    public void AnObjectOrAMapInsideAStructOrACellShowsItsSizeAndClass()
    {
        string printed = RunAndRead("""
            disp(struct('o', LoVal()))
            disp({LoVal()})
            disp(struct('m', containers.Map({'a', 'b'}, {1, 2})))
            disp({?LoVal})
            """);
        Assert.Contains("o: [1×1 LoVal]", printed, StringComparison.Ordinal);
        Assert.Contains("{1×1 LoVal}", printed, StringComparison.Ordinal);
        Assert.Contains("m: [2×1 containers.Map]", printed, StringComparison.Ordinal);
        Assert.Contains("{1×1 matlab.metadata.Class}", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("with properties", printed, StringComparison.Ordinal);
    }

    [Fact]
    public void MustBeNonzeroLengthTextChecksTextThenItsLength()
    {
        Assert.Equal(
            "ok\nValue must be text with one or more characters.\nValue must be a character vector, string array, or cell array of character vectors.\nValue must be text with one or more characters.\n",
            RunAndRead("""
                mustBeNonzeroLengthText({'a', 'b'}); disp('ok')
                try, mustBeNonzeroLengthText(["a" ""]); catch e, disp(e.message), end
                try, mustBeNonzeroLengthText(5); catch e, disp(e.message), end
                try, mustBeNonzeroLengthText(string(missing)); catch e, disp(e.message), end
                """));
    }
}
