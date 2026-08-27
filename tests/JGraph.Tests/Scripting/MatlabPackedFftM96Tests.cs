using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M96a seen from a MATLAB script: the transform family takes the packed kernels, and nothing may
/// notice. Every test here runs its script twice — packing forced on, then forced off — and the
/// printed output must be byte-identical, at seventeen significant digits with the imaginary plane
/// printed beside the real one so that a dropped sign or a stray <c>-0</c> cannot hide. The scripts
/// sweep what the fast path claims: both directions, every dimension including one past the end,
/// lengths that pad and lengths that cut, real and complex subjects, the two- and N-dimensional
/// forms, the symmetry word — and the forms it must refuse, whose answers and error messages have
/// to read exactly as they did.
/// </summary>
[Collection("JG facade")]
public class MatlabPackedFftM96Tests : IDisposable
{
    public MatlabPackedFftM96Tests() => JG.Reset();

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

    /// <summary>Prints a spectrum bit-faithfully: its shape over three dimensions, whether anything
    /// imaginary survived, and both planes at full precision.</summary>
    private const string Show = """
        function show(v)
            fprintf('%dx%dx%d R%d|', size(v, 1), size(v, 2), size(v, 3), isreal(v));
            fprintf('%.17g ', real(v));
            fprintf('|');
            fprintf('%.17g ', imag(v));
            fprintf('\n');
        end
        """ + "\n";

    [Fact]
    public void EveryDimensionTransformsTheSameWhicheverWayTheSamplesAreStored()
    {
        AssertParity(Show + """
            v = [3 1 2 1 -4 9];
            show(fft(v)); show(fft(v')); show(ifft(fft(v)));
            A = [3 1; 1 4; 2 9];
            show(fft(A)); show(fft(A, [], 1)); show(fft(A, [], 2));
            show(fft(A, [], 3)); show(fft(A, [], 7));
            C = reshape((1:24) - 12.5, 2, 3, 4);
            show(fft(C, [], 1)); show(fft(C, [], 2)); show(fft(C, [], 3));
            show(ifft(fft(C, [], 3), [], 3));
            show(fft(5)); show(fft([2 2 2 2])); show(fft(0)); show(fft([0 0 0]));
            """);
    }

    [Fact]
    public void ALengthThatPadsOrCutsAnswersWhatItAnsweredBefore()
    {
        AssertParity(Show + """
            v = [3 1 2 1 -4 9];
            show(fft(v, 8)); show(fft(v, 4)); show(fft(v, 1)); show(fft(v, 16));
            show(ifft(v, 8)); show(ifft(v, 3));
            A = [3 1; 1 4; 2 9];
            show(fft(A, 5)); show(fft(A, 2)); show(fft(A, 5, 2)); show(fft(A, 1, 2));
            show(fft(A, 4, 3));
            """);
    }

    [Fact]
    public void AComplexSubjectTransformsWhereItLiesAndComesBackTheSame()
    {
        AssertParity(Show + """
            z = [1+2i, 3-1i, -2+0.5i, 4i, 0, -1-1i];
            show(fft(z)); show(ifft(z)); show(ifft(fft(z)));
            show(fft(z, 8)); show(fft(z, 3)); show(fft(z.', [], 1));
            M = [1+1i 2; 3 4-2i];
            show(fft(M)); show(fft(M, [], 2)); show(ifft(fft(M)));
            show(fft(complex([1 2 3 4], 0)));
            """);
    }

    [Fact]
    public void TheTwoAndManyDimensionalFormsAgreeWithTheirBoxedSelves()
    {
        AssertParity(Show + """
            A = [3 1 4 1; 5 9 2 6; 5 3 5 8; 9 7 9 3];
            show(fft2(A)); show(ifft2(fft2(A))); show(fft2(A, 8, 8)); show(fft2(A, 2, 2));
            show(fftn(A)); show(ifftn(fftn(A)));
            C = reshape((1:24) - 12.5, 2, 3, 4);
            show(fftn(C)); show(ifftn(fftn(C))); show(fftn(C, [4 3 2]));
            show(fftshift(fft(1:8))); show(ifftshift(fftshift(fft(1:8))));
            """);
    }

    [Fact]
    public void TheSymmetryWordStillPromisesARealAnswer()
    {
        AssertParity(Show + """
            x = [1 2 3 4 5 6 7 8];
            F = fft(x);
            show(ifft(F, 'symmetric')); show(ifft(F, 'nonsymmetric'));
            show(ifft(F, 8, 'symmetric')); show(ifft(F, 8, 2, 'symmetric'));
            y = [1 2 3 4 5];
            show(ifft(fft(y), 'symmetric'));
            z = [1+2i, 3-1i, -2+0.5i, 4i];
            show(ifft(z, 'symmetric'));
            A = [1 2; 3 4];
            show(ifft2(fft2(A), 'symmetric'));
            """);
    }

    [Fact]
    public void TheClassesAndKindsTheFastPathWillNotTakeAnswerExactlyAsBefore()
    {
        AssertParity(Show + """
            show(fft(single([1 2 3 4])));
            show(fft(int32([1 2 3 4])));
            show(fft(logical([1 0 1 1])));
            show(fft(true));
            c = {1, 2};
            show(fft([c{:}]));
            """);
    }

    /// <summary>
    /// An empty subject used to be refused — the length along its first non-singleton dimension is
    /// zero, and a zero length was read as a mistake. It is not: the transform of nothing is
    /// nothing, shaped by the same rule as every other length. Every expected shape below was read
    /// off MATLAB itself, including the one that surprises — padding an empty array out to a real
    /// length gives a real array of zeros, so <c>fft(zeros(1, 0), 4)</c> really is four of them.
    /// </summary>
    /// <remarks>
    /// The two rows that are not MATLAB's are not this family's doing: MATLAB's <c>[]</c> literal is
    /// 0-by-0 and this build's is 1-by-0, so the first non-singleton dimension of a bare <c>[]</c> is
    /// the second here and the first there. Handed the same shape by name — <c>zeros(0, 3)</c>,
    /// <c>zeros(1, 0)</c> — the two transforms agree exactly, which is how the literal was found to
    /// be the one carrying the difference.
    /// </remarks>
    [Fact]
    public void AnEmptySubjectAnswersTheEmptyArrayMatlabAnswers()
    {
        AssertParity(Show + """
            show(fft([])); show(fft([], 4)); show(fft(zeros(0, 3)));
            show(fft(zeros(3, 0), [], 2)); show(fft(zeros(0, 3), 2)); show(fft(zeros(2, 0)));
            show(ifft([])); show(fft2([])); show(fft([1 2 3], 0));
            show(filter([1 1], 1, []));
            """);

        (string[] output, bool ok, string? message) = RunWith(packed: true, """
            function p(v)
                fprintf('%dx%d n=%d\n', size(v, 1), size(v, 2), numel(v));
            end
            p(fft([])); p(fft([], 4)); p(fft(zeros(0, 3))); p(fft(zeros(3, 0), [], 2));
            p(fft(zeros(0, 3), 2)); p(fft(zeros(2, 0))); p(fft([1 2 3], 0));
            p(filter([1 1], 1, []));
            """);

        Assert.True(ok, message);
        Assert.Equal(
            [
                "1x0 n=0",  // MATLAB says 0x0, because its [] is 0x0 and this build's is 1x0
                "1x4 n=4",  // MATLAB says 4x0 for the same reason; for zeros(1, 0) it says 1x4 too
                "0x3 n=0", "3x0 n=0", "2x3 n=6", "2x0 n=0", "1x0 n=0",
                "1x0 n=0",  // filter of the same literal, so 1-by-0 again where MATLAB says 0-by-0
            ],
            Array.ConvertAll(output, static line => line.Trim()));

        // Named rather than written as a literal, the shapes MATLAB was asked for come back exactly.
        (string[] named, bool namedOk, string? namedMessage) = RunWith(packed: true, """
            function p(v)
                fprintf('%dx%d n=%d s=%g\n', size(v, 1), size(v, 2), numel(v), sum(v(:)));
            end
            p(fft(zeros(1, 0), 4)); p(fft(zeros(1, 0)));
            """);

        Assert.True(namedOk, namedMessage);
        Assert.Equal(["1x4 n=4 s=0", "1x0 n=0 s=0"], Array.ConvertAll(named, static l => l.Trim()));
    }

    [Fact]
    public void TheRefusalsKeepTheirWording()
    {
        AssertParity(Show + "fft([1 2 3], -2);", expectSuccess: false);
        AssertParity(Show + "fft([1 2 3], [], 0);", expectSuccess: false);
        AssertParity(Show + "fft([1 2 3], 'nonsense');", expectSuccess: false);
        AssertParity(Show + "ifft([1 2 3], 'sideways');", expectSuccess: false);
        AssertParity(Show + "fft2([1 2 3], 4);", expectSuccess: false);
        AssertParity(Show + "fft('hello');", expectSuccess: false);
    }

    [Fact]
    public void ALengthPastTheFactoringThresholdStillInvertsToWhatWentIn()
    {
        // 64K points is the shortest length the kernels factor rather than walk, so this is the
        // road whose rounding is new. What it owes is not the old bits but the old identity.
        AssertParity(Show + """
            n = 65536;
            x = mod((1:n) * 0.618033988749895, 1) - 0.5;
            F = fft(x);
            b = ifft(F);
            fprintf('%.10g %.10g %.3g\n', real(F(1)), abs(F(4097)), max(abs(b - x)));
            fprintf('%d %d\n', numel(F), isreal(b));
            """);
    }

    [Fact]
    public void ASpectrumOfARealSignalKeepsItsHermitianSymmetry()
    {
        (string[] output, bool ok, string? message) = RunWith(packed: true, """
            n = 4096;
            x = sin((1:n) * 0.1) + 0.25 * mod((1:n) * 0.618033988749895, 1);
            F = fft(x);
            k = 2:(n/2);
            fprintf('%.3g\n', max(abs(F(k) - conj(F(n + 2 - k)))));
            fprintf('%.3g\n', abs(imag(F(1))) + abs(imag(F(n/2 + 1))));
            fprintf('%.3g\n', abs(sum(x) - real(F(1))));
            """);

        Assert.True(ok, message);
        Assert.Equal(3, output.Length);

        // The spectrum of a real signal is conjugate-symmetric, and a transform of it is symmetric
        // to its own rounding rather than exactly: the two halves are summed in different orders.
        // What has to be exact is the pair of bins that have no partner — bin 0 and the Nyquist bin
        // — whose imaginary parts the arithmetic cancels rather than approximates.
        Assert.True(double.Parse(output[0].Trim()) < 1e-9, output[0]);
        Assert.Equal("0", output[1].Trim());
        Assert.True(double.Parse(output[2].Trim()) < 1e-9, output[2]);
    }
}
