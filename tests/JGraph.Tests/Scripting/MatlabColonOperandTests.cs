using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// What a colon accepts at either end. MATLAB has no 1-by-1 array that is not a scalar, so
/// <c>1:N</c> reads N whether it was written <c>3</c> or <c>[3]</c> — and a user's script that wrote
/// the brackets stopped at <c>for lp = 1:N</c> with "the stop of a range must be a number, but got a
/// array". An empty operand makes an empty range, a logical counts as its number, a bracketed
/// integer keeps its class, and anything wider is refused in MATLAB's words.
/// </summary>
[Collection("JG facade")]
public class MatlabColonOperandTests : IDisposable
{
    public MatlabColonOperandTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private static (string[] Lines, ScriptRunResult Result) Run(string code)
    {
        var output = new RecordingScriptOutput();
        var context = new ScriptContext(output, static (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        return (output.NormalLines.ToArray(), result);
    }

    [Fact]
    public void ABracketedScalarIsAScalarOperand()
    {
        (string[] lines, ScriptRunResult result) = Run("""
            N = [3];
            s = 0;
            for lp = 1:N
                s = s + lp;
            end
            fprintf('%d\n', s);
            fprintf('%d\n', numel(1:N));
            fprintf('%d\n', numel([1]:[2]:[7]));
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(["6", "3", "4"], lines);
    }

    [Fact]
    public void AnEmptyOperandMakesAnEmptyRange()
    {
        (string[] lines, ScriptRunResult result) = Run("""
            r = 1:[];
            fprintf('%d %d\n', size(r));
            ran = 0;
            for i = 1:[]
                ran = ran + 1;
            end
            fprintf('%d\n', ran);
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(["1 0", "0"], lines);
    }

    [Fact]
    public void ALogicalCountsAsItsNumber()
    {
        (string[] lines, ScriptRunResult result) = Run("fprintf('%d\\n', numel(1:true));");

        Assert.True(result.Success, result.Message);
        Assert.Equal(["1"], lines);
    }

    [Fact]
    public void ABracketedIntegerKeepsItsClass()
    {
        (string[] lines, ScriptRunResult result) = Run("""
            k = int8([3]);
            r = 1:k;
            disp(class(r));
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(["int8"], lines);
    }

    [Fact]
    public void AWiderOperandIsRefusedInMatlabsWords()
    {
        (string[] lines, ScriptRunResult result) = Run("""
            try
                r = 1:[3 4];
            catch e
                disp(e.identifier);
                disp(e.message);
            end
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(["MATLAB:colon:operandsNotRealScalar", "Colon operands must be real scalars."], lines);
    }
}
