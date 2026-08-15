using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M62 — functions, files, and errors. Three things a ported MATLAB project needs before anything
/// else it contains can run: a helper in the next file over, an error that knows its own name, and a
/// function that says what its inputs must look like.
/// </summary>
[Collection("JG facade")]
public class MatlabFunctionsFilesAndErrorsTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "jgraph-m62-" + Guid.NewGuid().ToString("N"));

    private readonly string _library;

    public MatlabFunctionsFilesAndErrorsTests()
    {
        JG.Reset();
        _library = Path.Combine(_folder, "lib");
        Directory.CreateDirectory(_library);

        // Beside the script: the ordinary case, where a project's files simply sit together.
        File.WriteAllText(Path.Combine(_folder, "beside_me.m"), """
            function y = beside_me(x)
                y = x + 1;
            end
            """);

        // In a folder that only addpath can reach, with a local function that must stay local.
        File.WriteAllText(Path.Combine(_library, "far_away.m"), """
            function [s, d] = far_away(a, b)
                s = a + b;
                d = only_here(a, b);
            end

            function y = only_here(a, b)
                y = a - b;
            end
            """);

        File.WriteAllText(Path.Combine(_library, "a_script.m"), "made_by_the_script = 7;");
    }

    public void Dispose()
    {
        JG.Reset();
        Directory.Delete(_folder, recursive: true);
    }

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(
            code, new ScriptContext(_output, static (_, _) => { }, _folder), default);

    private static double Number(ScriptRunResult result, string name) =>
        Assert.IsType<double>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    private static string Text(ScriptRunResult result, string name) =>
        Assert.IsType<string>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    // --- Wave A: function files on a path -----------------------------------------------------

    [Fact]
    public async Task AFileBesideTheScript_AnswersItsOwnName()
    {
        ScriptRunResult result = await RunMatlab("""
            y = beside_me(4);
            where = which('beside_me');
            kind = exist('beside_me');
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(5.0, Number(result, "y"));
        Assert.EndsWith("beside_me.m", Text(result, "where"), StringComparison.Ordinal);
        Assert.Equal(2.0, Number(result, "kind"));
    }

    [Fact]
    public async Task AddpathMakesAFolderAnswer_AndRmpathTakesItBack()
    {
        ScriptRunResult before = await RunMatlab("far_away(1, 2);");
        Assert.False(before.Success);

        ScriptRunResult result = await RunMatlab($"""
            addpath('{_library.Replace("\\", "\\\\")}');
            [s, d] = far_away(5, 2);
            rmpath('{_library.Replace("\\", "\\\\")}');
            gone = 0;
            try
                far_away(1, 2);
            catch
                gone = 1;
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(7.0, Number(result, "s"));
        Assert.Equal(3.0, Number(result, "d"));
        Assert.Equal(1.0, Number(result, "gone"));
    }

    /// <summary>
    /// The point of loading a whole file rather than one function: its other functions exist for it
    /// and for nothing else. If this stops holding, two files with a helper of the same name start
    /// calling each other's.
    /// </summary>
    [Fact]
    public async Task AFilesOtherFunctionsAreLocalToIt()
    {
        ScriptRunResult result = await RunMatlab($"""
            addpath('{_library.Replace("\\", "\\\\")}');
            far_away(1, 2);
            hidden = 0;
            try
                only_here(9, 1);
            catch
                hidden = 1;
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(1.0, Number(result, "hidden"));
    }

    /// <summary>
    /// A script file on the path shares the caller's workspace, which is the whole difference between
    /// a script and a function and the reason a setup file is worth having.
    /// </summary>
    [Fact]
    public async Task AScriptOnThePath_RunsInTheCallersWorkspace()
    {
        ScriptRunResult result = await RunMatlab($"""
            addpath('{_library.Replace("\\", "\\\\")}');
            a_script;
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(7.0, Number(result, "made_by_the_script"));
    }

    [Fact]
    public async Task APathFunction_CanBeTakenAsAHandle()
    {
        ScriptRunResult result = await RunMatlab("""
            h = @beside_me;
            viaHandle = h(1);
            viaText = feval(str2func('beside_me'), 2);
            mapped = cellfun(@beside_me, {10, 20});
            first = mapped(1);
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(2.0, Number(result, "viaHandle"));
        Assert.Equal(3.0, Number(result, "viaText"));
        Assert.Equal(11.0, Number(result, "first"));
    }

    [Fact]
    public async Task AddpathRefusesAFolderThatIsNotThere()
    {
        ScriptRunResult result = await RunMatlab("addpath('definitely-not-a-folder')");

        Assert.False(result.Success);
        Assert.Contains("definitely-not-a-folder", result.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The recorded divergence: JGraph's built-ins win a name a path file also claims. MATLAB gives
    /// the file priority; here the 2,500 built-ins do, so a stray <c>max.m</c> cannot quietly replace
    /// the real one — see ADR 0062.
    /// </summary>
    [Fact]
    public async Task ABuiltinBeatsAFileOfTheSameName()
    {
        File.WriteAllText(Path.Combine(_folder, "max.m"), """
            function y = max(varargin)
                y = -999;
            end
            """);

        ScriptRunResult result = await RunMatlab("m = max([1 5 3]);");

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(5.0, Number(result, "m"));
    }

    // --- Wave B: errors that know their own name ----------------------------------------------

    [Fact]
    public async Task AnErrorCarriesItsIdentifierIntoTheCatch()
    {
        ScriptRunResult result = await RunMatlab("""
            id = '';
            msg = '';
            kind = '';
            try
                error('pkg:thing', 'value %d is wrong', 7);
            catch me
                id = me.identifier;
                msg = me.message;
                kind = class(me);
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("pkg:thing", Text(result, "id"));
        Assert.Equal("value 7 is wrong", Text(result, "msg"));
        Assert.Equal("MException", Text(result, "kind"));
    }

    /// <summary>
    /// The identifier is told apart from a message by MATLAB's own rule, and the interesting half is
    /// the negative one: a message that happens to contain a colon is still a message.
    /// </summary>
    [Fact]
    public async Task AMessageWithAColonIsStillAMessage()
    {
        ScriptRunResult result = await RunMatlab("""
            plainId = 'unset';
            plainMsg = '';
            try
                error('Value: %d out of range', 3);
            catch me
                plainId = me.identifier;
                plainMsg = me.message;
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(string.Empty, Text(result, "plainId"));
        Assert.Equal("Value: 3 out of range", Text(result, "plainMsg"));
    }

    [Fact]
    public async Task MExceptionCanBeBuilt_ThrownAndRethrown()
    {
        ScriptRunResult result = await RunMatlab("""
            built = MException('a:b', 'boom %d', 3);
            builtId = built.identifier;
            thrownId = '';
            try
                throw(built);
            catch first
                try
                    rethrow(first);
                catch again
                    thrownId = again.identifier;
                end
            end
            refused = 0;
            try
                MException('nocolon', 'x');
            catch
                refused = 1;
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("a:b", Text(result, "builtId"));
        Assert.Equal("a:b", Text(result, "thrownId"));
        Assert.Equal(1.0, Number(result, "refused"));
    }

    [Fact]
    public async Task ErrorAcceptsAnErrorStructAndReRaisesIt()
    {
        ScriptRunResult result = await RunMatlab("""
            id = '';
            try
                error(struct('message', 'from a struct', 'identifier', 'q:r'));
            catch me
                id = me.identifier;
                msg = me.message;
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("q:r", Text(result, "id"));
        Assert.Equal("from a struct", Text(result, "msg"));
    }

    /// <summary>
    /// The stack is built while the error unwinds, and each frame carries the line that was running
    /// in it — the innermost where it failed, every outer one at the call it was waiting on.
    /// </summary>
    [Fact]
    public async Task TheStackNamesEveryFunctionTheErrorUnwoundThrough()
    {
        ScriptRunResult result = await RunMatlab("""
            frames = 0;
            innermost = '';
            try
                outer();
            catch me
                frames = numel(me.stack);
                innermost = me.stack(1).name;
            end

            function outer()
                inner();
            end

            function inner()
                error('deep:down', 'from the bottom');
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(2.0, Number(result, "frames"));
        Assert.Equal("inner", Text(result, "innermost"));
    }

    // --- Wave C: the arguments block ----------------------------------------------------------

    [Fact]
    public async Task AnArgumentsBlockFillsInDefaultsAndChecksWhatArrived()
    {
        ScriptRunResult result = await RunMatlab("""
            withDefault = scaled(3);
            withBoth = scaled(3, 10);
            badValue = 0;
            try
                scaled(-1);
            catch
                badValue = 1;
            end
            badSize = 0;
            try
                scaled([1 2]);
            catch
                badSize = 1;
            end

            function y = scaled(x, factor)
                arguments
                    x (1,1) double {mustBePositive}
                    factor (1,1) double {mustBePositive} = 2
                end
                y = x * factor;
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(6.0, Number(result, "withDefault"));
        Assert.Equal(30.0, Number(result, "withBoth"));
        Assert.Equal(1.0, Number(result, "badValue"));
        Assert.Equal(1.0, Number(result, "badSize"));
    }

    /// <summary>
    /// A validator written as a call is evaluated exactly as written, in the function's own frame —
    /// which is what lets <c>mustBeMember(name, …)</c> name its own argument.
    /// </summary>
    [Fact]
    public async Task AValidatorCallReadsItsArgumentOutOfTheFrame()
    {
        ScriptRunResult result = await RunMatlab("""
            good = pick('red');
            refused = 0;
            try
                pick('mauve');
            catch
                refused = 1;
            end

            function c = pick(name)
                arguments
                    name (1,:) char {mustBeMember(name, {'red', 'green'})}
                end
                c = upper(name);
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("RED", Text(result, "good"));
        Assert.Equal(1.0, Number(result, "refused"));
    }

    /// <summary>
    /// The word only means the block where a block may appear, so no script loses the name — this is
    /// what makes the syntax purely additive.
    /// </summary>
    [Fact]
    public async Task TheWordArgumentsIsStillAnOrdinaryName()
    {
        ScriptRunResult result = await RunMatlab("""
            arguments = 5;
            doubled = arguments * 2;
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(10.0, Number(result, "doubled"));
    }

    [Fact]
    public async Task ValidateattributesChecksClassAndAttributes()
    {
        ScriptRunResult result = await RunMatlab("""
            validateattributes([1 2 3], {'numeric'}, {'vector', 'increasing', '>', 0});
            ok = 1;
            refused = 0;
            try
                validateattributes(-2, {'numeric'}, {'positive'}, 'reading');
            catch me
                refused = 1;
                msg = me.message;
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(1.0, Number(result, "ok"));
        Assert.Equal(1.0, Number(result, "refused"));
        Assert.Contains("reading", Text(result, "msg"), StringComparison.Ordinal);
    }

    /// <summary>A name-value declaration is refused by name rather than mis-parsed — the M62 deferral.</summary>
    [Fact]
    public async Task ANameValueArgumentIsRefusedWithItsOwnReason()
    {
        ScriptRunResult result = await RunMatlab("""
            function f(options)
                arguments
                    options.Width (1,1) double = 1
                end
            end
            """);

        Assert.False(result.Success);
        Assert.Contains("name-value", result.Message, StringComparison.Ordinal);
    }
}
