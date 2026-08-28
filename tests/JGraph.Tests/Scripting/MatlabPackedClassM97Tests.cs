using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Numerics;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M97 seen from a MATLAB script: an integer or single class is now applied by the kernel that
/// computed the element rather than by a sweep of its own, and nothing may notice. Every test here
/// runs its script twice — packing forced on, then forced off — and the printed output must be
/// byte-identical at seventeen significant digits, with reciprocals printed alongside so that a
/// saturated <c>-0</c> answered where a <c>+0</c> belongs cannot hide behind a zero that prints the
/// same either way.
/// </summary>
[Collection("JG facade")]
public class MatlabPackedClassM97Tests : IDisposable
{
    public MatlabPackedClassM97Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private static (string[] Output, bool Success, string? Message) RunWith(bool packed, string code)
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
            return (output.Normal.ToArray(), result.Success, result.Message);
        }
        finally
        {
            JgsPacking.Enabled = previous;
        }
    }

    private static void AssertParity(string code, bool expectSuccess = true)
    {
        (string[] packedOut, bool packedOk, string? packedMessage) = RunWith(packed: true, code);
        (string[] boxedOut, bool boxedOk, string? boxedMessage) = RunWith(packed: false, code);

        Assert.Equal(boxedOk, packedOk);
        if (expectSuccess)
        {
            Assert.True(boxedOk, boxedMessage);
        }
        else
        {
            Assert.False(boxedOk);
            Assert.Equal(boxedMessage, packedMessage); // the refusal must read the same too
        }

        Assert.Equal(boxedOut, packedOut);
    }

    /// <summary>Prints class, shape and both the samples and their reciprocals at full precision.</summary>
    private const string Show = """
        function show(v)
            fprintf('%s %dx%dx%d|', class(v), size(v, 1), size(v, 2), size(v, 3));
            fprintf('%.17g ', double(v));
            fprintf('|');
            fprintf('%.17g ', 1 ./ double(v));
            fprintf('\n');
        end
        """ + "\n";

    /// <summary>The values a conversion can go wrong on, as a MATLAB row.</summary>
    private const string Grid =
        "g = [0 -0 0.5 -0.5 1.5 -1.5 2.5 -2.5 0.49999999999999994 -0.49999999999999994 " +
        "0.4 -0.4 0.6 -0.6 127 128 255 256 -128 -129 32767 -32768 65535 " +
        "4503599627370495.5 -4503599627370495.5 4503599627370496 1e300 -1e300 " +
        "Inf -Inf NaN 1e-300 -1e-300];\n";

    [Fact]
    public void EveryClassConvertsTheEdgeGridTheSameWhicheverWayTheSamplesAreStored()
    {
        AssertParity(Show + Grid + """
            show(int8(g)); show(int16(g)); show(int32(g)); show(int64(g));
            show(uint8(g)); show(uint16(g)); show(uint32(g)); show(uint64(g));
            show(single(g)); show(double(g));
            show(int8(g')); show(uint8(reshape([g g], 2, [])));
            """);
    }

    [Fact]
    public void ArithmeticInsideAClassSaturatesWhereItAlwaysDid()
    {
        AssertParity(Show + """
            a = int8([100 -100 60 -60 0 127 -128]);
            b = int8([100 -100 -60 60 0 1 -1]);
            show(a + b); show(a - b); show(a .* b); show(a ./ b); show(-a);
            show(a + 1); show(1 + a); show(a - 1); show(a * 2); show(a / 2);
            show(a .^ 2); show(2 .^ int8([1 2 3 8]));
            u = uint8([0 1 200 255]);
            show(u + u); show(u - u); show(u - uint8(1)); show(uint8(0) - u);
            show(u .* 2); show(u ./ 0); show(u ./ 7);
            show(int32([2147483647 -2147483648]) + int32([1 -1]));
            show(int64([100 200]) .* int64([1000000000000 -1]));
            """);
    }

    [Fact]
    public void SingleKeepsFloatPrecisionThroughEveryOperation()
    {
        AssertParity(Show + """
            p = single([0.1 0.2 1/3 1e-40 1e40 -0 0]);
            show(p); show(p + p); show(p .* p); show(p ./ 3); show(p - single(0.1));
            show(p * 2); show(single(1) / single(3));
            show(single([1 2 3]) + 0.5); show(0.5 + single([1 2 3]));
            show(single(pi) == pi); show(double(single(0.1)) == 0.1);
            show(single([16777216 16777217 16777218]));
            """);
    }

    /// <summary>
    /// A shape, a dimension count and a compound assignment all have to survive the fused road, and
    /// an indexing read has to keep the class it read out of.
    /// </summary>
    [Fact]
    public void ShapesAndAssignmentsAndReadsKeepTheirClass()
    {
        AssertParity(Show + """
            A = int16(reshape(1:24, 2, 3, 4));
            show(A(:, :, 2)); show(A(1, 2, 3)); show(A(:)'); show(sum(size(A)));
            x = uint8([10 20 30]);
            x = x + 5; show(x);
            x(2) = 300; show(x);
            y = int32([1 2; 3 4]);
            y = y .* y; show(y);
            show([int8(1) 300]); show([uint8(200); 100]);
            show(cast(3.5, 'int8')); show(cast(-3.5, 'uint8')); show(cast(g(1:6), 'int16'));
            show(zeros(1, 3, 'int32')); show(ones(2, 2, 'uint8'));
            show(idivide(int32(7), int32(2))); show(idivide(int32([7 -7]), int32(2), 'floor'));
            show(uint8([])); show(int32(zeros(0, 3))); show(single([]));
            show(uint8([]) + uint8([])); show(int8(zeros(2, 0)) * 3);
            """.Replace("g(1:6)", "[0.5 -0.5 1.5 300 -300 NaN]"));
    }

    [Fact]
    public void TheRefusalsAndTheCombiningRuleKeepTheirWording()
    {
        AssertParity(Show + "int8([1 2]) + int16([1 2]);", expectSuccess: false);
        AssertParity(Show + "int8([1 2]) + [1 2];", expectSuccess: false);
        AssertParity(Show + "uint8([1 2]) .* int8([1 2]);", expectSuccess: false);
        AssertParity(Show + "[int8(1) int16(2)];", expectSuccess: false);

        // A char array is not a refusal: it converts through its codes, so int8('text') is the
        // int8 of [116 101 120 116]. What is refused is a value with no codes to convert.
        AssertParity(Show + "int8({1, 2});", expectSuccess: false);
    }

    /// <summary>
    /// N-D arithmetic keeps its dimensions on the packed road. The boxed road does not, and did not
    /// before M97 either — a scalar multiply of a 2x3x4 comes back 2x12 there, which is the same
    /// dropped-shape defect M94 recorded. This pins the answer the fast path gives rather than
    /// asserting the two roads agree, because on this one point they do not.
    /// </summary>
    [Fact]
    public void ArithmeticOverManyDimensionsKeepsThemOnThePackedRoad()
    {
        (string[] output, bool ok, string? message) = RunWith(packed: true, """
            A = int16(reshape(1:24, 2, 3, 4));
            B = A * 1000;
            disp(sprintf('%s %dx%dx%d %.17g', class(B), size(B, 1), size(B, 2), size(B, 3), sum(double(B(:)))));
            C = single(reshape(1:24, 2, 3, 4)) ./ 3;
            disp(sprintf('%s %dx%dx%d', class(C), size(C, 1), size(C, 2), size(C, 3)));
            """);

        Assert.True(ok, message);

        // 1000 .. 24000 saturate at int16's 32767, so the sum is the clipped one.
        double clipped = 0;
        for (int i = 1; i <= 24; i++)
        {
            clipped += Math.Min(i * 1000, short.MaxValue);
        }

        Assert.Equal($"int16 2x3x4 {clipped:R}", output[0].Trim());
        Assert.Equal("single 2x3x4", output[1].Trim());
    }

    /// <summary>
    /// Growing an integer array past its end keeps the class on the packed road, and the new element
    /// saturates into it like every other. The boxed road drops to double and stores the raw number
    /// instead, and did so before M97 as well — the same family of lost tag as the dropped N-D shape
    /// above — so this pins the packed answer rather than comparing the two.
    /// </summary>
    [Fact]
    public void GrowingAnIntegerArrayKeepsItsClassOnThePackedRoad()
    {
        (string[] output, bool ok, string? message) = RunWith(packed: true, """
            x = uint8([10 20 30]);
            x(2) = 300;
            x(end + 1) = -7;
            x(end + 1) = 3.5;
            disp(sprintf('%s %g %g %g %g %g', class(x), double(x(1)), double(x(2)), double(x(3)), double(x(4)), double(x(5))));
            s = int8([1 2]);
            s(4) = 1000;
            disp(sprintf('%s %g %g %g %g', class(s), double(s(1)), double(s(2)), double(s(3)), double(s(4))));
            """);

        Assert.True(ok, message);
        Assert.Equal("uint8 10 255 30 0 4", output[0].Trim());
        Assert.Equal("int8 1 2 0 127", output[1].Trim());
    }

    /// <summary>
    /// A char array converts through its codes, and a logical through its zeros and ones — both of
    /// which reach the same packed rounding as everything else.
    /// </summary>
    [Fact]
    public void CharsAndLogicalsConvertThroughTheSameKernel()
    {
        AssertParity(Show + """
            show(double('ABC')); show(uint8('ABC')); show(int8('ABC')); show(single('A'));
            L = logical([1 0 1 1 0]);
            show(double(L)); show(uint8(L)); show(int32(L)); show(single(L));
            show(uint8(L) + uint8(L));
            """);
    }

    /// <summary>
    /// Long enough to cross both the grain boundary threads are handed out on and the tile the fused
    /// kernel rounds in, so a seam in either would show.
    /// </summary>
    [Fact]
    public void ALongArrayConvertsToWhatTheShortRoadWouldHaveGiven()
    {
        (string[] packed, bool ok, string? message) = RunWith(packed: true, """
            n = 300000;
            x = (mod((1:n) * 0.618033988749895, 1) - 0.5) * 600;
            u = int8(x);
            v = int8(x) + int8(7);
            s = single(x);
            fprintf('%.17g %.17g %.17g\n', sum(double(u)), sum(double(v)), sum(double(s)));
            k = [1 2 8191 8192 8193 65535 65536 65537 n];
            e = 0;
            for i = 1:numel(k)
                j = k(i);
                w = max(-128, min(127, round(x(j))));
                e = e + abs(double(u(j)) - w);
            end
            fprintf('%.17g\n', e);
            """);

        Assert.True(ok, message);
        Assert.Equal("0", packed[1].Trim());

        (string[] boxed, bool boxedOk, string? boxedMessage) = RunWith(packed: false, """
            n = 300000;
            x = (mod((1:n) * 0.618033988749895, 1) - 0.5) * 600;
            u = int8(x);
            v = int8(x) + int8(7);
            s = single(x);
            fprintf('%.17g %.17g %.17g\n', sum(double(u)), sum(double(v)), sum(double(s)));
            """);

        Assert.True(boxedOk, boxedMessage);
        Assert.Equal(boxed[0], packed[0]);
    }

    /// <summary>
    /// The cross-layer claim M97 rests on: the kernel's rule and the interpreter's own conversion
    /// are two spellings of one thing. They are written in different projects out of different
    /// vocabularies, so this compares them element for element rather than assuming it.
    /// </summary>
    [Fact]
    public void TheKernelsRuleIsTheInterpretersOwnConversion()
    {
        double[] grid =
        [
            0.0, -0.0, 0.5, -0.5, 1.5, -1.5, 2.5, -2.5,
            0.49999999999999994, -0.49999999999999994, 0.4, -0.4, 0.6, -0.6,
            127, 128, 255, 256, -128, -129, 32767, -32768, 65535, 2147483647, -2147483648,
            4503599627370495.5, -4503599627370495.5, 4503599627370496.0, 9007199254740992.0,
            1e300, -1e300, double.MaxValue, -double.MaxValue,
            double.PositiveInfinity, double.NegativeInfinity, double.NaN,
            1e-300, -1e-300, double.Epsilon, -double.Epsilon,
        ];

        foreach (JgsNumericClass numericClass in Enum.GetValues<JgsNumericClass>())
        {
            PackedMath.Rounding rule = JgsNumericClasses.RoundingFor(numericClass);
            foreach (double x in grid)
            {
                double want = JgsNumericClasses.Convert(x, numericClass);
                double got = rule.Apply(x);
                Assert.True(
                    BitConverter.DoubleToInt64Bits(want) == BitConverter.DoubleToInt64Bits(got),
                    $"{numericClass} of {x:R}: kernel said {got:R}, conversion said {want:R}");
            }

            // And the same claim over a buffer, which is the road an array actually takes.
            using var buffer = new ManagedBuffer(grid.Length);
            grid.AsSpan().CopyTo(buffer.AsSpan());
            PackedMath.Round(buffer, buffer, rule);
            for (int i = 0; i < grid.Length; i++)
            {
                double want = JgsNumericClasses.Convert(grid[i], numericClass);
                Assert.True(
                    BitConverter.DoubleToInt64Bits(want) == BitConverter.DoubleToInt64Bits(buffer.AsSpan()[i]),
                    $"{numericClass} of {grid[i]:R} through the buffer");
            }
        }
    }
}
