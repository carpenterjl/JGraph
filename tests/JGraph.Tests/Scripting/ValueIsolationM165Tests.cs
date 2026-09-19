using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V4 (ADR 0165): a dotted write finds its object wherever the frame's name for it lives — a
/// <c>global</c> declaration redirects the write the way it redirects every other one — and a
/// handle is its own share, so a value that holds one never clones it.
/// </summary>
/// <remarks>
/// The parity fixture <c>value_isolation_objscope</c> holds R2025b's answers for the whole matrix;
/// these pin the roads the fix touched, one assertion a road, so a regression names itself.
/// </remarks>
[Collection("JG facade")]
public class ValueIsolationM165Tests : IDisposable
{
    private const string Writers =
        """

        function write_value(v)
        global gV
        gV.p = v;
        end
        function write_handle(v)
        global gH
        gH.data = v;
        end
        function write_line(v)
        global gL
        gL.YData = v;
        end
        """;

    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public ValueIsolationM165Tests()
    {
        JG.Reset();
        _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "ValueBox.m"), """
            classdef ValueBox
                properties
                    p = 1
                end
            end
            """);
        File.WriteAllText(Path.Combine(_directory, "HandleHolder.m"), """
            classdef HandleHolder < handle
                properties
                    data
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

    private string RunAndRead(string code)
    {
        ScriptRunResult result = _engine.RunAsync(
            code + Writers,
            new ScriptContext(_output, (_, _) => { }, _directory, resolvePath: null, figureFiles: new TestFigureFiles()),
            CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim();
    }

    [Fact]
    public void AValueObjectInAGlobal_TakesAPropertyWriteFromAnotherFrame()
    {
        string printed = RunAndRead("""
            global gV
            gV = ValueBox();
            write_value(7);
            fprintf('%d', gV.p);
            """);

        Assert.Equal("7", printed);
    }

    [Fact]
    public void AValueObjectInAGlobal_LeavesACopyTakenBeforeTheWriteAlone()
    {
        string printed = RunAndRead("""
            global gV
            gV = ValueBox();
            w = gV;
            write_value(7);
            fprintf('%d %d', w.p, gV.p);
            """);

        Assert.Equal("1 7", printed);
    }

    [Theory]
    [InlineData("gV.p = ones(1, 3); gV.p(2) = 7;", "[1 7 1]")]
    [InlineData("gV.p = ones(1, 3); gV.p(5) = 9;", "[1 1 1 0 9]")]
    [InlineData("gV.p = [1 2 3 4]; gV.p(2) = [];", "[1 3 4]")]
    [InlineData("gV.p = {1, 2}; gV.p{2} = [7 8];", "[7 8]")]
    [InlineData("gV.('p') = [7 8];", "[7 8]")]
    public void AValueObjectInAGlobal_TakesEveryShapeOfWriteThroughItsProperty(string write, string expected)
    {
        string printed = RunAndRead($$"""
            probe();
            function probe()
            global gV
            gV = ValueBox();
            {{write}}
            held = gV.p;
            if iscell(held), held = held{2}; end
            fprintf('%s', mat2str(held));
            end
            """);

        Assert.Equal(expected, printed);
    }

    [Fact]
    public void AnObjectHeldByAnObjectInAGlobal_IsWrittenWhereItLives()
    {
        string printed = RunAndRead("""
            probe();
            function probe()
            global gV
            gV = ValueBox();
            gV.p = ValueBox();
            w = gV;
            gV.p.p = 7;
            fprintf('%d %d', w.p.p, gV.p.p);
            end
            """);

        Assert.Equal("1 7", printed);
    }

    [Fact]
    public void ARefusedPropertyWriteThroughAGlobal_LeavesTheObjectAsItWas()
    {
        string printed = RunAndRead("""
            probe();
            function probe()
            global gV
            gV = ValueBox();
            try
                gV.nosuch = 7;
                fprintf('assigned');
            catch
                fprintf('refused %d', gV.p);
            end
            end
            """);

        Assert.Equal("refused 1", printed);
    }

    [Fact]
    public void AHandleObjectInAGlobal_IsOneObjectUnderEveryName()
    {
        string printed = RunAndRead("""
            global gH
            gH = HandleHolder();
            w = gH;
            write_handle(7);
            fprintf('%d %d', w.data, gH.data);
            """);

        Assert.Equal("7 7", printed);
    }

    [Fact]
    public void AGraphicsHandleInAGlobal_TakesAPropertyWriteFromAnotherFrame()
    {
        string printed = RunAndRead("""
            global gL
            f = figure('Visible', 'off');
            gL = plot([1 2 3]);
            write_line([4 5 6]);
            fprintf('%s', mat2str(gL.YData));
            close(f);
            """);

        Assert.Equal("[4 5 6]", printed);
    }

    [Fact]
    public void AnArrayOfGraphicsHandlesInAGlobal_TakesAWriteOnOneElement()
    {
        string printed = RunAndRead("""
            probe();
            function probe()
            global gL
            f = figure('Visible', 'off');
            gL = plot([1 2 3; 4 5 6]);
            gL(1).LineWidth = 2;
            gL(2).LineWidth = 3;
            fprintf('%g %g', gL(1).LineWidth, gL(2).LineWidth);
            close(f);
            end
            """);

        Assert.Equal("2 3", printed);
    }

    [Theory]
    [InlineData("o.p.data = 7;")]
    [InlineData("w.p.data = 7;")]
    public void AHandleHeldByAValueObject_IsSharedByTheObjectsCopies(string write)
    {
        // The copy shares its fields (M2), and the handle among them is a reference: the write goes
        // to the one object both copies hold, not to a clone made by a detach.
        string printed = RunAndRead($$"""
            o = ValueBox();
            o.p = HandleHolder();
            w = o;
            {{write}}
            fprintf('%d %d', w.p.data, o.p.data);
            """);

        Assert.Equal("7 7", printed);
    }

    [Fact]
    public void AHandleInACell_IsSharedByTheCellsCopies()
    {
        string printed = RunAndRead("""
            h = HandleHolder();
            c = {h, 1};
            d = c;
            d{2} = 5;
            h.data = 7;
            fprintf('%d %d', c{1}.data, d{1}.data);
            """);

        Assert.Equal("7 7", printed);
    }
}
