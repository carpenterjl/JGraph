using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// What an array keeps when it is written through a subscript: its class, its shape, and the clamp
/// its class puts on the values it may hold.
/// </summary>
/// <remarks>
/// <para>
/// Three defects met here, all of them an array losing something it should have kept. An indexed
/// assignment stored the raw number, so <c>x = uint8([10 20 30]); x(2) = 300</c> left a 300 sitting
/// in a uint8 and <c>sum(x)</c> answered 340 where MATLAB answers 295 — the clamp was applied when
/// one element was read back and by arithmetic, and never on the way in, so every verb that read the
/// array wholesale saw a value the class cannot hold. Growing an array past its end dropped the
/// class outright wherever the packed buffer could not grow in place. And elementwise arithmetic on
/// the boxed road reshaped its answer to rows-by-columns, which is MATLAB's own two-dimensional view
/// of an N-D array, so a 2-by-3-by-4 came back a 2-by-12.
/// </para>
/// <para>
/// Every expectation below is a line real MATLAB R2024a printed for the same script, and every
/// script runs twice — packing forced on, then off — because two of the three defects were a
/// disagreement between the roads and a fix checked on one road is not a fix.
/// </para>
/// </remarks>
[Collection("JG facade")]
public class ChipIntegerStorageTests : IDisposable
{
    public ChipIntegerStorageTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    /// <summary>Class, then every sample at full precision, then the shape.</summary>
    private const string Show = """
        function show(x)
            fprintf('%s |', class(x));
            fprintf('%.17g ', double(x));
            fprintf('| %dx%d\n', size(x, 1), size(x, 2));
        end
        """ + "\n";

    private static string[] RunWith(bool packed, string code)
    {
        bool previous = JgsPacking.Enabled;
        JgsPacking.Enabled = packed;
        try
        {
            JG.Reset();
            var output = new RecordingScriptOutput();
            var figures = new List<FigureModel>();
            var context = new ScriptContext(output, (_, figure) => figures.Add(figure), null);
            ScriptRunResult result = JgsRunner.Run(
                code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
            Assert.True(result.Success, result.Message);

            // NormalLines, not Normal: fprintf writes a fragment per call, so one printed line
            // arrives here as three or four entries and only the reassembled text has lines in it.
            return output.NormalLines.Select(line => line.TrimEnd()).ToArray();
        }
        finally
        {
            JgsPacking.Enabled = previous;
        }
    }

    /// <summary>
    /// Runs the script on both roads, asserts they agree, and asserts both said what MATLAB said.
    /// </summary>
    private static void AssertMatlab(string code, params string[] expected)
    {
        string[] packed = RunWith(packed: true, code);
        string[] boxed = RunWith(packed: false, code);
        Assert.Equal(boxed, packed);
        Assert.Equal(expected, boxed);
    }

    [Fact]
    public void AnIndexedWriteStoresOnlyWhatTheClassCanHold()
    {
        AssertMatlab(
            Show + """
            x = uint8([10 20 30]); x(2) = 300; show(x);
            x = uint8([10 20 30]); x(2) = -7; show(x);
            x = uint8([10 20 30]); x(2) = 2.5; show(x);
            x = uint8([10 20 30]); x(2) = 3.5; show(x);
            x = uint8([10 20 30]); x(2) = Inf; show(x);
            x = uint8([10 20 30]); x(2) = -Inf; show(x);
            x = uint8([10 20 30]); x(2) = NaN; show(x);
            x = int8([1 2 3]); x(2) = 300; show(x);
            x = int8([1 2 3]); x(2) = -300; show(x);
            x = int8([1 2 3]); x(2) = -2.5; show(x);
            x = int32([1 2 3]); x(2) = 3e9; show(x);
            x = int32([1 2 3]); x(2) = -3e9; show(x);
            x = int64([1 2 3]); x(2) = 1e19; show(x);
            x = uint64([1 2 3]); x(2) = 2e19; show(x);
            x = uint8([10 20 30]); x(1:2) = [500 -3]; show(x);
            x = uint8([10 20 30]); x([true false true]) = [500 -3]; show(x);
            x = uint8([10 20 30]); x(:) = [500 -3 7]; show(x);
            x = uint8([1 2 3]); x(2) = true; show(x);
            x = uint8([1 2 3]); x(2) = uint8(200); show(x);
            x = single([1 2 3]); x(2) = 0.1; show(x);
            """,
            "uint8 |10 255 30 | 1x3",
            "uint8 |10 0 30 | 1x3",
            "uint8 |10 3 30 | 1x3",
            "uint8 |10 4 30 | 1x3",
            "uint8 |10 255 30 | 1x3",
            "uint8 |10 0 30 | 1x3",
            "uint8 |10 0 30 | 1x3",
            "int8 |1 127 3 | 1x3",
            "int8 |1 -128 3 | 1x3",
            "int8 |1 -3 3 | 1x3",
            "int32 |1 2147483647 3 | 1x3",
            "int32 |1 -2147483648 3 | 1x3",
            "int64 |1 9.2233720368547758e+18 3 | 1x3",
            "uint64 |1 1.8446744073709552e+19 3 | 1x3",
            "uint8 |255 0 30 | 1x3",
            "uint8 |255 20 0 | 1x3",
            "uint8 |255 0 7 | 1x3",
            "uint8 |1 1 3 | 1x3",
            "uint8 |1 200 3 | 1x3",
            "single |1 0.10000000149011612 3 | 1x3");
    }

    [Fact]
    public void GrowingKeepsTheClassAndClampsTheNewElement()
    {
        AssertMatlab(
            Show + """
            x = uint8([10 20 30]); x(5) = 300; show(x);
            x = uint8([10 20 30]); x(2) = 300; x(4) = -7; show(x);
            x = uint8([10 20 30]'); x(5) = 300; show(x);
            x = int8([1 2]); x(4) = 1000; show(x);
            x = single([1 2 3]); x(5) = 0.1; show(x);
            x = uint8([10 20; 30 40]); x(1,2) = 900; show(x);
            x = uint8([10 20; 30 40]); x(3,3) = 900; show(x);
            x = uint8([10 20; 30 40]); x(:,3) = [900; -5]; show(x);
            x = []; x(3) = 7; show(x);
            """,
            "uint8 |10 20 30 0 255 | 1x5",
            "uint8 |10 255 30 0 | 1x4",
            "uint8 |10 20 30 0 255 | 5x1",
            "int8 |1 2 0 127 | 1x4",
            "single |1 2 3 0 0.10000000149011612 | 1x5",
            "uint8 |10 30 255 40 | 2x2",
            "uint8 |10 30 0 20 40 0 0 0 255 | 3x3",
            "uint8 |10 30 20 40 255 0 | 2x3",
            "double |0 0 7 | 1x3");
    }

    [Fact]
    public void DeletingElementsKeepsTheClass()
    {
        AssertMatlab(
            Show + """
            x = uint8([1 2 3]); x(2) = []; show(x);
            x = uint8([1 2 3; 4 5 6]); x(1,:) = []; show(x);
            x = uint8([1 2 3; 4 5 6]); x(:,2) = []; show(x);
            x = single([1 2 3]); x(2) = []; show(x);
            x = uint8([1 2 3]); x(:) = []; fprintf('%s | %dx%d\n', class(x), size(x, 1), size(x, 2));
            """,
            "uint8 |1 3 | 1x2",
            "uint8 |4 5 6 | 1x3",
            "uint8 |1 4 3 6 | 2x2",
            "single |1 3 | 1x2",
            "uint8 | 0x0");
    }

    /// <summary>
    /// A write into a subscript of a three-dimensional array clamps like every other, and the array
    /// keeps all three dimensions while it happens.
    /// </summary>
    [Fact]
    public void AWriteIntoManyDimensionsClampsAndKeepsThemAll()
    {
        AssertMatlab("""
            x = int16(reshape(1:24, 2, 3, 4));
            x(1,2,3) = 1e6;
            fprintf('%s | %.17g | %dx%dx%d\n', class(x), sum(double(x(:))), size(x, 1), size(x, 2), size(x, 3));
            y = uint8(zeros(2, 2, 2));
            y(:,:,2) = 900;
            fprintf('%s | %.17g | %dx%dx%d\n', class(y), sum(double(y(:))), size(y, 1), size(y, 2), size(y, 3));
            """,
            "int16 | 33052 | 2x3x4",
            "uint8 | 1020 | 2x2x2");
    }

    /// <summary>
    /// The point of clamping into storage rather than on the way out: the verbs that read the whole
    /// array read what the class holds. Every one of these answered from a 300 before.
    /// </summary>
    [Fact]
    public void EveryReaderOfASaturatedArraySeesTheSaturatedValue()
    {
        AssertMatlab("""
            x = uint8([10 20 30]); x(2) = 300;
            fprintf('sum %.17g max %.17g min %.17g mean %.17g\n', ...
                double(sum(x)), double(max(x)), double(min(x)), double(mean(x)));
            fprintf('any>255 %d  nnz(x==255) %d  prod %.17g\n', any(x > 255), nnz(x == 255), double(prod(x)));
            fprintf('double |'); fprintf('%.17g ', double(x)); fprintf('|\n');
            fprintf('plus0  |'); fprintf('%.17g ', double(x + 0)); fprintf('|\n');
            fprintf('sorted |'); fprintf('%.17g ', double(sort(x, 'descend'))); fprintf('|\n');
            y = x; y(2) = y(2);
            fprintf('roundtrip %s %.17g\n', class(y), double(y(2)));
            """,
            "sum 295 max 255 min 10 mean 98.333333333333329",
            "any>255 0  nnz(x==255) 1  prod 76500",
            "double |10 255 30 |",
            "plus0  |10 255 30 |",
            "sorted |255 30 10 |",
            "roundtrip uint8 255");
    }

    /// <summary>
    /// Elementwise arithmetic keeps every dimension of its operand, not the two that MATLAB folds an
    /// N-D array into for a two-subscript reader. The last two lines are the case a fix that only
    /// looked at the row count would still get wrong: a 1-by-1-by-4 has one row, so it was not even
    /// recognised as having a shape worth keeping.
    /// </summary>
    [Fact]
    public void ArithmeticKeepsEveryDimensionOfItsOperand()
    {
        AssertMatlab("""
            function d(v)
                fprintf('%s %dx%dx%d %d %.17g\n', ...
                    class(v), size(v, 1), size(v, 2), size(v, 3), ndims(v), sum(double(v(:))));
            end
            A = reshape(1:24, 2, 3, 4);
            d(A * 1000); d(A + 1); d(A .* A); d(A ./ 2); d(-A); d(A > 5); d(A > 5 & A < 20);
            d(int16(A) * 1000); d(single(A) ./ 3); d(A == A); d(1000 * A); d(A .^ 2);
            B = reshape(1:4, 1, 1, 4);
            d(B * 2); d(B + B); d(B > 2);
            C = reshape(1:8, 2, 2, 2);
            d(C + reshape(1:2, 1, 1, 2));
            """,
            "double 2x3x4 3 300000",
            "double 2x3x4 3 324",
            "double 2x3x4 3 4900",
            "double 2x3x4 3 150",
            "double 2x3x4 3 -300",
            "logical 2x3x4 3 19",
            "logical 2x3x4 3 14",
            "int16 2x3x4 3 300000",
            "single 2x3x4 3 100.00000002980232",
            "logical 2x3x4 3 24",
            "double 2x3x4 3 300000",
            "double 2x3x4 3 4900",
            "double 1x1x4 3 20",
            "double 1x1x4 3 20",
            "logical 1x1x4 3 2",
            "double 2x2x2 3 48");
    }
}
