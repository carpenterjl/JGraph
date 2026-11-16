using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open item 351 and two parts of item 346 (ADR 0383): what the parity fixture
/// <c>oi_mixed_class_leftovers</c> cannot reach. The JGS dialect's <c>and</c>, <c>or</c> and
/// <c>xor</c> keep a matrix's shape (they answered a flat row), the JGS <c>idivide</c> saturates a
/// zero divisor as integer division does (it answered 0), and a MATLAB-dialect <c>&amp;&amp;</c> or
/// <c>||</c> refuses a non-scalar operand alike on the walk and inside a loop the loop compiler takes.
/// </summary>
[Collection("JG facade")]
public class MixedClassLeftovers8gTests : IDisposable
{
    public MixedClassLeftovers8gTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private static (ScriptRunResult Result, RecordingScriptOutput Output) Run(string code, JgsDialect dialect)
    {
        var output = new RecordingScriptOutput();
        var context = new ScriptContext(output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(code, context, default, sourceId: "", hook: null, dialect);
        return (result, output);
    }

    private static string Jgs(string code)
    {
        (ScriptRunResult result, RecordingScriptOutput output) = Run(code, JgsDialect.Jgs);
        Assert.True(result.Success, result.Message + output.ErrorText);
        return output.NormalText;
    }

    private static string Matlab(string code)
    {
        (ScriptRunResult result, RecordingScriptOutput output) = Run(code, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + output.ErrorText);
        return output.NormalText;
    }

    [Fact]
    public void TheJgsLogicalVerbsKeepAMatrixShape()
    {
        string text = Jgs("""
            let a = and([1, 0; 1, 1], [1, 1; 0, 1]);
            print(size(a));
            let o = or([1, 0; 0, 0], 1);
            print(size(o));
            let x = xor(0, [1, 0, 1; 0, 0, 1]);
            print(size(x));
            print(a);
            """);
        Assert.Equal("[2, 2]\n[2, 2]\n[2, 3]\n[true, false; false, true]\n", text);
    }

    [Fact]
    public void TheJgsIdivideSaturatesAZeroDivisor()
    {
        string text = Jgs("""
            print(idivide(int8(7), int8(0)));
            print(idivide(int8(-7), int8(0)));
            print(idivide(int32(0), int32(0)));
            print(idivide(uint8(7), uint8(0), 'floor'));
            print(idivide(int8(7), int8(2)));
            """);
        Assert.Equal("127\n-128\n0\n255\n3\n", text);
    }

    [Fact]
    public void ANonScalarShortCircuitOperandIsRefusedInACompiledLoopToo()
    {
        // The loop compiles in MATLAB code; the walk refuses the row the compiled road cannot hold.
        string text = Matlab("""
            r = [1 1];
            try
                s = 0;
                for k = 1:2000
                    if k > 1500 && r
                        s = s + k;
                    end
                end
                disp('answered');
            catch err
                disp(err.identifier);
            end
            q = [1 1];
            s = 0;
            for k = 1:2000
                if k > 1500 && q(1)
                    s = s + 1;
                end
            end
            disp(s);
            """);
        Assert.Equal("MATLAB:nonLogicalConditional\n500\n", text);
    }

    [Fact]
    public void ACharIsItsCodeUnderShortCircuit()
    {
        string text = Matlab("""
            fprintf('%d\n', char(0) && 1);
            fprintf('%d\n', 'a' && 1);
            fprintf('%d\n', 0 || char(1));
            """);
        Assert.Equal("0\n1\n1\n", text);
    }
}
