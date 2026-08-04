using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M47: the three base-language gaps M46 wave L found and left. A number remembers the class it was
/// asked for, a <c>for</c> loop walks a cell array, and a table can say how tall and how wide it is.
/// </summary>
[Collection("JG facade")]
public sealed class MatlabNumericClassTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public MatlabNumericClassTests()
    {
        JG.Reset();
        _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
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
    }

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = Assert
            .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
            .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure)), _directory));
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    // --- The class tag ---------------------------------------------------------------------------

    [Fact]
    public async Task AConvertedValue_ReportsTheClassItWasAskedFor()
    {
        await RunAsserting("""
            assert(strcmp(class(uint8(7)), 'uint8'), 'a converted scalar forgot its class');
            assert(strcmp(class(int16([1 2 3])), 'int16'), 'a converted array forgot its class');
            assert(strcmp(class(single(1)), 'single'), 'single is not single');
            assert(strcmp(class(1), 'double'), 'a plain number stopped being double');
            assert(strcmp(class(double(uint8(7))), 'double'), 'double() did not clear the class');
            """);
    }

    [Fact]
    public async Task TheClassSurvivesAssignment_AndIndexing()
    {
        await RunAsserting("""
            g = uint8([10 20 30]);
            assert(strcmp(class(g), 'uint8'), 'binding a name dropped the class');
            one = g(2);
            assert(one == 20 && strcmp(class(one), 'uint8'), 'reading an element dropped the class');
            some = g(1:2);
            assert(strcmp(class(some), 'uint8'), 'a slice dropped the class');
            """);
    }

    [Fact]
    public async Task ThePredicatesAndWhos_ReadTheTag()
    {
        await RunAsserting("""
            assert(isinteger(uint8(7)), 'isinteger missed an integer');
            assert(~isfloat(uint8(7)), 'an integer claimed to be floating point');
            assert(isfloat(single(1)) && isfloat(1), 'single and double are both floating point');
            assert(~isinteger(1), 'a plain double claimed to be an integer');
            assert(isa(uint8(7), 'numeric') && isa(uint8(7), 'integer'), 'isa misread an integer');
            assert(~isa(uint8(7), 'float'), 'an integer claimed to be a float');
            assert(isa(single(1), 'float') && ~isa(single(1), 'integer'), 'isa misread a single');
            """);
    }

    [Fact]
    public async Task IntegerArithmetic_SaturatesInsideItsOwnClass()
    {
        await RunAsserting("""
            a = uint8(200) + uint8(100);
            assert(a == 255 && strcmp(class(a), 'uint8'), 'addition did not saturate at the top');
            b = uint8(10) - uint8(20);
            assert(b == 0 && strcmp(class(b), 'uint8'), 'subtraction did not saturate at zero');
            c = uint8([1 2 3]) * 2;
            assert(all(c == [2 4 6]) && strcmp(class(c), 'uint8'), 'a scalar multiply left the class');
            d = int8(7) / int8(2);
            assert(d == 4 && strcmp(class(d), 'int8'), 'integer division does not round half away from zero');
            e = -uint8(5);
            assert(e == 0 && strcmp(class(e), 'uint8'), 'negating an unsigned value did not saturate');
            """);
    }

    [Fact]
    public async Task MixingTwoIntegerClasses_IsRefused()
    {
        await RunAsserting("""
            failed = false;
            try
                z = uint8(1) + int8(1);
            catch err
                failed = true;
                assert(~isempty(strfind(err.message, 'uint8')), 'the message does not name the classes');
            end
            assert(failed, 'two different integer classes combined silently');

            failed = false;
            try
                z = uint8([1 2]) + [1 2];
            catch err
                failed = true;
            end
            assert(failed, 'an integer array combined with a double array');

            ok = uint8([1 2]) + 1;
            assert(all(ok == [2 3]) && strcmp(class(ok), 'uint8'), 'an integer array refused a scalar');
            """);
    }

    [Fact]
    public async Task Concatenation_TakesTheIntegerClassAndSaturates()
    {
        await RunAsserting("""
            f = [int8(1) 300];
            assert(f(1) == 1 && f(2) == 127, 'the double beside an int8 did not saturate');
            assert(strcmp(class(f), 'int8'), 'the literal did not take the integer class');
            assert(strcmp(class([1 2 3]), 'double'), 'a plain literal stopped being double');
            """);
    }

    [Fact]
    public async Task SingleRoundsToFloatPrecision_AndBeatsDouble()
    {
        await RunAsserting("""
            x = single(1) / 3;
            assert(strcmp(class(x), 'single'), 'single divided by a double did not stay single');
            assert(abs(double(x) - 1/3) > 1e-9, 'single kept full double precision');
            assert(abs(double(x) - 1/3) < 1e-7, 'single is not close to a third');
            """);
    }

    // --- for over a cell array -------------------------------------------------------------------

    [Fact]
    public async Task ForOverACell_BindsOneCellPerColumn()
    {
        await RunAsserting("""
            names = {'line', 'diamond', 'square'};
            lengths = [];
            passes = 0;
            for name = names
                assert(iscell(name) && numel(name) == 1, 'each pass should bind a 1-by-1 cell');
                passes = passes + 1;
                lengths(passes) = numel(name{1});
            end
            assert(passes == 3, 'the loop did not run once per entry');
            assert(all(lengths == [4 7 6]), 'the loop walked the entries out of order');
            """);
    }

    [Fact]
    public async Task ForOverATwoRowCell_BindsAWholeColumn()
    {
        await RunAsserting("""
            C = {1, 'two'; 3, 'four'};
            passes = 0;
            for k = C
                passes = passes + 1;
                assert(numel(k) == 2, 'a column of a 2-by-2 cell has two entries');
            end
            assert(passes == 2, 'a 2-by-2 cell has two columns');
            """);
    }

    [Fact]
    public async Task ACellLiteralWithRows_IsShapedAndColumnMajor()
    {
        await RunAsserting("""
            C = {1, 'two'; 3, 'four'};
            s = size(C);
            assert(s(1) == 2 && s(2) == 2, 'a rowed cell literal lost its shape');
            assert(C{2} == 3, 'linear order through a cell is not column-major');
            assert(C{2,1} == 3, 'two subscripts into a cell do not reach the second row');
            assert(strcmp(C{1,2}, 'two'), 'two subscripts into a cell do not reach the second column');
            """);
    }

    // --- height and width ------------------------------------------------------------------------

    [Fact]
    public async Task HeightAndWidth_MeasureATable()
    {
        await RunAsserting("""
            T = table([1;2;3], [4;5;6]);
            assert(height(T) == 3, 'height did not count the rows');
            assert(width(T) == 2, 'width did not count the variables');
            s = size(T);
            assert(s(1) == 3 && s(2) == 2, 'size did not learn tables');
            """);
    }

    [Fact]
    public async Task HeightAndWidth_MeasureAnOrdinaryArray()
    {
        await RunAsserting("""
            A = zeros(3, 5);
            assert(height(A) == 3 && width(A) == 5, 'height/width disagree with size on a matrix');
            V = zeros(2, 3, 4);
            assert(height(V) == 2 && width(V) == 3, 'height/width should read the first two dimensions');
            assert(height('abcd') == 1 && width('abcd') == 4, 'a char row is 1-by-4');
            assert(height(7) == 1 && width(7) == 1, 'a scalar is 1-by-1');
            """);
    }
}
