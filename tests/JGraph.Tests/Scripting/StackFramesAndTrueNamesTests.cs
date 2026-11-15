using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open items 329 and 334 (ADR 0368), measured in R2025b: <c>ME.stack</c> holds a frame per anonymous
/// body and per script the error left, <c>run</c> holds R2025b's <c>run.m</c> as a frame, and the
/// leftovers of ADR 0363's <c>true</c> and <c>false</c> as names - <c>eval('true')</c> asked for a
/// value answers the built-in past a <c>true.m</c>, <c>iskeyword</c>, <c>which</c> naming a variable,
/// the <c>MATLAB:lang:ExternalShadowedInEval</c> warning, and a nested function named <c>true</c>.
/// </summary>
[Collection("JG facade")]
public class StackFramesAndTrueNamesTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "jgraph-oi334-" + Guid.NewGuid().ToString("N"));

    public StackFramesAndTrueNamesTests()
    {
        JG.Reset();
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        JG.Reset();
        Directory.Delete(_folder, recursive: true);
    }

    private ScriptContext Context() => new(_output, static (_, _) => { }, _folder);

    private Task<ScriptRunResult> RunMatlab(string code) => new MatlabScriptEngine().RunAsync(code, Context(), default);

    private static object? Value(ScriptRunResult result, string name) =>
        Assert.Single(result.Variables, v => v.Name == name).RawValue;

    private void WriteFile(string name, string source) => File.WriteAllText(Path.Combine(_folder, name), source);

    /// <summary>
    /// A MATLAB script a JGS session names in a statement is a frame of <c>ME.stack</c>: an error it
    /// raises and catches at its own top level has the one frame naming it, at its line (R2025b:
    /// <c>matlab -batch "q2"</c> is <c>1 frames</c>, <c>q2</c> at line 3). It had none here: the
    /// script's frame was dbstack's alone, and the session has no main file to stand in for it.
    /// </summary>
    [Fact]
    public async Task AScriptAJgsSessionNames_IsTheFrameOfAnErrorItCatches()
    {
        WriteFile("q2.m", "x = 1;\ntry\n    error('JGraph:probe','raised here');\ncatch err\n    q2_frames = numel(err.stack);\n    q2_name = err.stack(1).name;\n    q2_line = err.stack(1).line;\n    q2_file = err.stack(1).file;\nend\n");

        ScriptRunResult result = await new JgsScriptEngine().RunAsync("q2", Context(), default);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(1.0, Value(result, "q2_frames"));
        Assert.Equal("q2", Value(result, "q2_name"));
        Assert.Equal(3.0, Value(result, "q2_line"));
        Assert.Equal(Path.Combine(_folder, "q2.m"), Assert.IsType<string>(Value(result, "q2_file")), ignoreCase: true);
    }

    [Fact]
    public async Task AnAnonymousBodyAndAScript_AreFramesOfTheErrorStack()
    {
        WriteFile("oi334s.m", "x = 1;\nerror('oi334:s', 'from script');\n");

        ScriptRunResult result = await RunMatlab("""
            try
                cellfun(@(x) error('oi334:a', 'boom'), {1});
            catch e
                anon = sprintf('%s@%d', e.stack(1).name, e.stack(1).line);
                report = getReport(e, 'basic');
            end
            try
                h = str2func('@() error(''oi334:e'', ''boom'')');
                h();
            catch e
                text_made = sprintf('%s@%d', e.stack(1).name, e.stack(1).line);
                text_report = getReport(e, 'basic');
            end
            try
                oi334s;
            catch e
                script = sprintf('%s@%d', e.stack(1).name, e.stack(1).line);
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("@(x)error('oi334:a','boom')@2", Value(result, "anon"));
        Assert.StartsWith("Error using ", Assert.IsType<string>(Value(result, "report")));
        Assert.Contains("@(x)error('oi334:a','boom') (line 2)", Assert.IsType<string>(Value(result, "report")));
        Assert.Equal("@()error('oi334:e','boom')@0", Value(result, "text_made"));
        Assert.DoesNotContain("(line", Assert.IsType<string>(Value(result, "text_report")));
        Assert.Equal("oi334s@2", Value(result, "script"));
    }

    [Fact]
    public async Task Run_HoldsRunAsAFrameAtItsLine()
    {
        WriteFile("oi334r.m", "oi334_rd = dbstack;\n");
        WriteFile("oi334e.m", "x = 2;\nerror('oi334:re', 'from run');\n");

        ScriptRunResult result = await RunMatlab("""
            run('oi334r.m');
            rd = strjoin(arrayfun(@(f) sprintf('%s@%d', f.name, f.line), oi334_rd', 'UniformOutput', false), ' ; ');
            try
                run('oi334e.m');
            catch e
                re = strjoin(arrayfun(@(f) sprintf('%s@%d', f.name, f.line), e.stack', 'UniformOutput', false), ' ; ');
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.StartsWith("oi334r@1 ; run@112", Assert.IsType<string>(Value(result, "rd")));
        Assert.StartsWith("oi334e@2 ; run@112", Assert.IsType<string>(Value(result, "re")));
    }

    [Fact]
    public async Task EvalOfTheBareWord_AnswersTheBuiltinPastATrueFile()
    {
        WriteFile("true.m", "function r = true(varargin)\nr = 'file';\nend\n");

        ScriptRunResult result = await RunMatlab("""
            a = class(eval('true'));
            b = class(eval('true;'));
            c = class(eval('true,'));
            d = class(eval('(true)'));
            e = class(evalin('base', 'true'));
            eval('f = true;');
            f = class(f);
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("logical", Value(result, "a"));
        Assert.Equal("logical", Value(result, "b"));
        Assert.Equal("char", Value(result, "c"));
        Assert.Equal("char", Value(result, "d"));
        Assert.Equal("logical", Value(result, "e"));
        Assert.Equal("char", Value(result, "f"));
    }

    [Fact]
    public async Task IsKeywordWhichAndTheEvalWarning()
    {
        ScriptRunResult result = await RunMatlab("""
            k = numel(iskeyword);
            yes = iskeyword('parfor');
            no = iskeyword('true');
            true = 3;
            w = which('true');
            printed = strtrim(evalc('which true'));
            clear true
            id = probe();
            function id = probe()
            lastwarn('');
            eval('true = 7;');
            [~, id] = lastwarn;
            id = [id '|' class(true)];
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(20.0, Value(result, "k"));
        Assert.Equal(true, Value(result, "yes"));
        Assert.Equal(false, Value(result, "no"));
        Assert.Equal("variable", Value(result, "w"));
        Assert.Equal("true is a variable.", Value(result, "printed"));
        Assert.Equal("MATLAB:lang:ExternalShadowedInEval|logical", Value(result, "id"));
    }

    [Fact]
    public async Task ANestedFunctionNamedTrue_AnswersOnlyTheFunctionItIsNestedIn()
    {
        WriteFile("oi329n.m", """
            function r = oi329n()
            r = {outer(), sib()};
            end
            function r = outer()
            r = true;
                function y = true()
                    y = 'nested';
                end
            end
            function r = sib()
            r = true;
            end
            """);

        ScriptRunResult result = await RunMatlab("""
            r = oi329n();
            a = r{1};
            b = class(r{2});
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("nested", Value(result, "a"));
        Assert.Equal("logical", Value(result, "b"));
    }
}
