using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The M22 parity net: every corpus script runs twice — packed arrays forced ON and forced OFF —
/// and must produce byte-identical console output and variable snapshots. Packing is a pure
/// representation change; any observable divergence is a bug in the packed fast paths.
/// </summary>
[Collection("JG facade")]
public class JgsPackedParityTests : IDisposable
{
    public JgsPackedParityTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private static async Task<(string[] Output, string[] Variables, bool Success, string? Message)> RunWith(bool packed, string code)
    {
        bool previous = JgsPacking.Enabled;
        JgsPacking.Enabled = packed;
        try
        {
            JG.Reset();
            var engine = new JgsScriptEngine();
            var output = new RecordingScriptOutput();
            var figures = new List<FigureModel>();
            ScriptRunResult result = await engine.RunAsync(
                code, new ScriptContext(output, (_, figure) => figures.Add(figure), null), default);
            string[] variables = result.Variables
                .Select(v => $"{v.Name}:{v.Type}={v.DisplayValue}")
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();
            return (output.Normal.ToArray(), variables, result.Success, result.Message);
        }
        finally
        {
            JgsPacking.Enabled = previous;
        }
    }

    private static async Task AssertParity(string code, bool expectSuccess = true)
    {
        var packed = await RunWith(packed: true, code);
        var boxed = await RunWith(packed: false, code);

        Assert.Equal(boxed.Success, packed.Success);
        if (expectSuccess)
        {
            Assert.True(boxed.Success, boxed.Message);
        }
        else
        {
            Assert.False(boxed.Success);
            Assert.Equal(boxed.Message, packed.Message); // identical error text
        }

        Assert.Equal(boxed.Output, packed.Output);
        Assert.Equal(boxed.Variables, packed.Variables);
    }

    [Fact]
    public Task ElementwiseArithmetic_AllOperators_AllOperandShapes() => AssertParity("""
        let a = [1.5, -2, 3, 0.25];
        let b = [2, 4, -0.5, 8];
        print(a + b); print(a - b); print(a .* b); print(a ./ b); print(a % b); print(a .^ 2)
        print(a + 10); print(10 - a); print(a * 2); print(24 ./ b); print(2 .^ [1, 2, 3, 4])
        print(-a); print(abs(a)); print((-2) ^ 3)
        """);

    [Fact]
    public Task ComparisonsAndMasks_ProduceIdenticalLogic() => AssertParity("""
        let x = [3, 1, 4, 1, 5, 9, 2, 6];
        print(x > 3); print(x <= 2); print(3 < x); print(x == 1); print(x ~= 1)
        let mask = x > 3;
        print(x(mask)); print(sum(mask)); print(find(x > 4))
        print(x == "text"); print([true, false] == [true, true])
        """);

    [Fact]
    public Task SliceReadsAndWrites_IncludingEndAndColon() => AssertParity("""
        let x = 0:9;
        print(x(1)); print(x(end)); print(x(2:4)); print(x(end-2:end)); print(x(:))
        x(1) = 100; x(2:3) = 0; x(4:5) = [7, 8]; x(end) = -1;
        print(x)
        x(1:3) += 10; x(4) *= 2;
        print(x)
        let s = "hello";
        print(s(1)); print(s(end)); print(s(2:3))
        """);

    [Fact]
    public Task Ranges_FractionalAndDescending() => AssertParity("""
        let t = 0:0.001:3;
        print(length(t)); print(t(1)); print(t(end))
        print(5:-1:1); print(1:0.5:3); print(3:1)
        """);

    [Fact]
    public Task ConcatenationAndMatrixRows() => AssertParity("""
        let a = [1, 2, 3];
        let padded = [a; zeros(3, 1)];
        print(padded); print(length(padded))
        let m = [1, 2; 3, 4];
        print(m); print(m + 1); print(m + m); print(size(m))
        print(concat(a, [4, 5]))
        """);

    [Fact]
    public Task Demotion_WritingStringIntoNumericArray() => AssertParity("""
        let x = [1, 2, 3];
        x(2) = "two";
        print(x); print(x(2)); print(length(x))
        x(1) = 10;
        print(x)
        """);

    [Fact]
    public Task Aliasing_SharedArrays_SeeEachOthersWrites() => AssertParity("""
        let x1 = [1, 2, 3, 4];
        let x2 = x1;
        x2(0:1) = 0;
        print(x1); print(x2)
        x1(3) = 99;
        print(x2)
        let y = x1(:);
        y(0) = -1;
        print(x1(0)); print(y(0))
        """);

    [Fact]
    public Task StatsAndArrayBuiltins() => AssertParity("""
        let x = [4, 2, 7, 1, 9, 3];
        print(sum(x)); print(mean(x)); print(min(x)); print(max(x))
        print(sort(x)); print(cumsum(x)); print(diff(x)); print(reverse(x))
        print(sqrt([4, 9, 16])); print(floor([1.5, -1.5])); print(round([2.5, 3.5]))
        """);

    [Fact]
    public Task FftRoundTrip_ThroughTheDspBuiltins() => AssertParity("""
        let x = [1, 2, 3, 4, 5, 0, 0, 0];
        let restored = real(ifft(fft(x)));
        print(round(restored .* 1000) ./ 1000)
        print(round(abs(fft(x))(1)))
        """);

    [Fact]
    public Task ComplexSpectra_FftZeroingShiftAndProjections() => AssertParity("""
        let x = [1, 2, 3, 4, 5, 6, 7, 8];
        let X = fft(x);
        print(length(X)); print(round(abs(X(1))))
        print(real(X(2)) < 100); print(imag(X))
        let Xc = X(:);
        Xc(2:3) = 0; Xc(end) = 0;
        print(round(abs(Xc) .* 100) ./ 100)
        print(round(real(ifft(Xc)) .* 1000) ./ 1000)
        print(angle([1i, -1])); print(conj(X)(2) == X(2))
        print(fftshift([1, 2, 3, 4])); print(X(2:3))
        let g = X([1, 5]);
        print(round(abs(g)))
        """);

    [Fact]
    public Task ComplexArrays_ToDoublesErrors_AndEquality() => AssertParity("""
        let X = fft([1, 2, 3, 4]);
        print(sum(abs(X)) > 0)
        print(X == X); print(X ~= X)
        """);

    [Fact]
    public Task ComplexArrays_PlottingComplex_FailsWithGuidance() => AssertParity("""
        let X = fft([1, 2, 3, 4]);
        plot(X)
        """, expectSuccess: false);

    [Fact]
    public Task ControlFlow_LoopsAndTruthiness() => AssertParity("""
        let total = 0;
        for k = 1:100
            total += k;
        end
        print(total)
        let x = [1, 2, 3];
        if x > 0
            print("all positive")
        end
        let i = 0;
        while i < 3 { i++; }
        print(i)
        let [p, q] = [10, 20];
        print(p + q)
        """);

    [Fact]
    public Task EchoAndAns_MatchAcrossRepresentations() => AssertParity("""
        let x = [1, 2, 3]
        x + 1
        ans .* 2
        let y = 5;
        y
        """);

    [Fact]
    public Task LargeArrayEcho_UsesTheBoundedDisplay() => AssertParity("""
        let big = 1:2000;
        print(length(big))
        print(big)
        """);

    [Fact]
    public Task Errors_LengthMismatch_IdenticalMessages() => AssertParity("""
        let a = [1, 2, 3];
        print(a + [1, 2])
        """, expectSuccess: false);

    [Fact]
    public Task Errors_BadIndex_IdenticalMessages() => AssertParity("""
        let a = [1, 2, 3];
        print(a(4))
        """, expectSuccess: false);

    [Fact]
    public Task Errors_FractionalIndex_IdenticalMessages() => AssertParity("""
        let a = [1, 2, 3];
        print(a([1.5, 2]))
        """, expectSuccess: false);

    [Fact]
    public Task Errors_MaskLengthMismatch_IdenticalMessages() => AssertParity("""
        let a = [1, 2, 3];
        let mask = [true, false];
        print(a(mask))
        """, expectSuccess: false);

    [Fact]
    public async Task PackedMode_ActuallyPacks_SanityCheck()
    {
        // Guards against the parity suite silently comparing boxed-vs-boxed: with packing ON, a
        // range's snapshot raw value must come from the packed representation.
        bool previous = JgsPacking.Enabled;
        JgsPacking.Enabled = true;
        try
        {
            JG.Reset();
            var engine = new JgsScriptEngine();
            var output = new RecordingScriptOutput();
            ScriptRunResult result = await engine.RunAsync(
                "let t = 0:0.5:10;", new ScriptContext(output, (_, _) => { }, null), default);
            Assert.True(result.Success, result.Message);
            ScriptVariable variable = Assert.Single(result.Variables, v => v.Name == "t");
            double[] raw = Assert.IsType<double[]>(variable.RawValue);
            Assert.Equal(21, raw.Length);
            Assert.Equal(10, raw[^1]);
        }
        finally
        {
            JgsPacking.Enabled = previous;
        }
    }
}
