using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M145, step 6 — the flip: a name resolves in the order R2025b showed. A bound name; a nested
/// function; a local function of the running file; a built-in <em>class method</em> when the
/// measured dispatch table says the built-in answers the arguments' classes; the file's
/// <c>private/</c> folder; a user class method on the dominant object; the current folder; the
/// <c>addpath</c> folders; and the built-in layer last, so a user file takes a built-in's name as it
/// does in MATLAB. One test per row of the plan's probe tables (<c>prec/</c>, <c>step0/</c>,
/// <c>prec6/probe6.out</c>), every expectation a line MATLAB printed. Step 7 adds the tools over
/// the same layers (<c>prec7/probe7.out</c>): <c>builtin</c> as a forwarder over the built-in layer,
/// <c>which -all</c>, <c>exist</c> by the layers, and the shadowing warning. Step 8 (the stress
/// scripts) adds the anonymous-body row (<c>prec8/probe8.out</c>).
/// </summary>
[Collection("JG facade")]
public class JgsPrecedenceTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "jgraph-m145-flip-" + Guid.NewGuid().ToString("N"));

    private readonly string _library;

    public JgsPrecedenceTests()
    {
        JG.Reset();
        _library = Path.Combine(_folder, "lib");
        Directory.CreateDirectory(_library);
    }

    public void Dispose()
    {
        JG.Reset();
        Directory.Delete(_folder, recursive: true);
    }

    private ScriptContext Context() => new(_output, static (_, _) => { }, _folder);

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(code, Context(), default);

    /// <summary>
    /// Runs <paramref name="code"/> as the file <paramref name="name"/> in the test folder — so the
    /// code has a file behind it, which is what makes <c>private/</c> visible to it.
    /// </summary>
    private async Task<ScriptRunResult> RunAsFile(string name, string code)
    {
        string path = WriteFile(name, code);
        var session = Assert.IsType<JgsReplSession>(
            Assert.IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine()).CreateSession(Context()));
        await using (session)
        {
            return await session.ExecuteFileAsync(code, path, CancellationToken.None);
        }
    }

    private static object? Value(ScriptRunResult result, string name) =>
        Assert.Single(result.Variables, v => v.Name == name).RawValue;

    private static double Number(ScriptRunResult result, string name) => Assert.IsType<double>(Value(result, name));

    private static string Text(ScriptRunResult result, string name) => Assert.IsType<string>(Value(result, name));

    private string WriteFile(string name, string source)
    {
        string path = Path.Combine(_folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
        return path;
    }

    private string Escaped(string path) => path.Replace("\\", "\\\\");

    private static string Shadow(string name, double sentinel) =>
        $"function y = {name}(varargin)\ny = {sentinel};\nend\n";

    private void Ok(ScriptRunResult result) => Assert.True(result.Success, result.Message + _output.ErrorText);

    // --- Which layer answers ------------------------------------------------------------------

    /// <summary>
    /// The first row: with <c>max.m</c> beside the script, <c>max([1 5 3])</c> is still 5 because
    /// <c>@double/max</c> is a built-in method, and every argument list with no applicable method
    /// reaches the file. Among numeric, logical and char arguments the leftmost decides and a second
    /// such argument never blocks.
    /// </summary>
    [Fact]
    public async Task ABuiltinMethodOfTheArgumentsClass_OutranksAFile_AndOnlyThen()
    {
        WriteFile("max.m", Shadow("max", -999));
        WriteFile("sum.m", Shadow("sum", -555));

        ScriptRunResult result = await RunMatlab("""
            a = max([1 5 3]);
            b = [max({1, 2}), max('a', 1), max(2, {1}), max(1, @sin), max("a"), max([1 2], [], "all"), max(int8(3), {1})];
            c = [max(int8(3), 2.5), max(1, single(2)), max(true, false), sum([1 2], 'all')];
            try; d = max(1, 'a'); d = isequal(d, -999); catch; d = false; end
            """);

        Ok(result);
        Assert.Equal(5.0, Number(result, "a"));
        Assert.Equal(new double[] { -999, -999, -999, -999, -999, -999, -999 }, (double[])Value(result, "b")!);
        Assert.Equal(new double[] { 3, 2, 1, 3 }, (double[])Value(result, "c")!);
        Assert.False(Assert.IsType<bool>(Value(result, "d"))); // the built-in was reached (JGraph's max refuses a char; MATLAB answers 97)
    }

    /// <summary>
    /// Per name: a string in second position blocks the built-in for <c>numel</c> and not for
    /// <c>plus</c>. <c>plus(1, "x")</c> is the built-in's (MATLAB: the string <c>"1x"</c>),
    /// <c>numel("x", 1)</c> the built-in's (MATLAB: too many arguments), <c>numel(1, "x")</c> the file's.
    /// </summary>
    [Fact]
    public async Task AStringSecond_BlocksNumel_ButNotPlus()
    {
        WriteFile("numel.m", Shadow("numel", -333));
        WriteFile("plus.m", Shadow("plus", -444));

        ScriptRunResult result = await RunMatlab("""
            try; p = plus(1, "x"); p = isequal(p, -444); catch; p = false; end
            try; n1 = numel("x", 1); n1 = isequal(n1, -333); catch; n1 = false; end
            n2 = numel(1, "x");
            """);

        Ok(result);
        Assert.False(Assert.IsType<bool>(Value(result, "p")));
        Assert.False(Assert.IsType<bool>(Value(result, "n1")));
        Assert.Equal(-333.0, Number(result, "n2"));
    }

    /// <summary>A library <c>@tabular</c> method loses to a current-folder file: <c>height(t)</c> and <c>height(1, t)</c> both reach <c>height.m</c>.</summary>
    [Fact]
    public async Task ATabularMethod_LosesToAFile()
    {
        WriteFile("height.m", Shadow("height", -222));

        ScriptRunResult result = await RunMatlab("""
            t = table(1);
            h = [height(t), height(1, t)];
            """);

        Ok(result);
        Assert.Equal(new double[] { -222, -222 }, (double[])Value(result, "h")!);
    }

    /// <summary>A library <c>@datetime</c> method wins over the same file, in every argument position.</summary>
    [Fact]
    public async Task ADatetimeMethod_WinsOverAFile()
    {
        WriteFile("minus.m", Shadow("minus", -111));

        ScriptRunResult result = await RunMatlab("""
            d = datetime(2024, 1, 15);
            try; a = minus(d, d); a = isequal(a, -111); catch; a = false; end
            try; b = minus(1, d); b = isequal(b, -111); catch; b = false; end
            try; c = minus(d, 1); c = isequal(c, -111); catch; c = false; end
            """);

        Ok(result);
        Assert.False(Assert.IsType<bool>(Value(result, "a")));
        Assert.False(Assert.IsType<bool>(Value(result, "b")));
        Assert.False(Assert.IsType<bool>(Value(result, "c")));
    }

    /// <summary>The map's method wins only with the map first: <c>keys(m)</c> is the method's, <c>keys(1, m)</c> the file's.</summary>
    [Fact]
    public async Task TheMapsMethod_WinsOnlyWithTheMapFirst()
    {
        WriteFile("keys.m", Shadow("keys", -777));

        ScriptRunResult result = await RunMatlab("""
            m = containers.Map({'k'}, {42});
            a = keys(m);
            a = a{1};
            b = keys(1, m);
            """);

        Ok(result);
        Assert.Equal("k", Text(result, "a"));
        Assert.Equal(-777.0, Number(result, "b"));
    }

    /// <summary><c>mean</c> and <c>linspace</c> are plain <c>.m</c> files in MATLAB, not methods: the current folder beats them outright.</summary>
    [Fact]
    public async Task APlainToolboxFunction_LosesToTheCurrentFolder()
    {
        WriteFile("mean.m", Shadow("mean", -222));
        WriteFile("linspace.m", Shadow("linspace", -223));

        ScriptRunResult result = await RunMatlab("""
            a = mean([1 2 3]);
            b = linspace(0, 1, 3);
            """);

        Ok(result);
        Assert.Equal(-222.0, Number(result, "a"));
        Assert.Equal(-223.0, Number(result, "b"));
    }

    /// <summary>A script's local function beats a built-in method (and a user method); a nested function beats a local one.</summary>
    [Fact]
    public async Task ALocalFunction_BeatsABuiltinMethod_AndANestedOneBeatsALocal()
    {
        WriteFile("Meth.m", """
            classdef Meth
                methods
                    function y = foo(obj)
                        y = 'method foo';
                    end
                end
            end
            """);
        WriteFile("outer.m", """
            function y = outer()
                y = inner();
                function z = inner()
                    z = 'nested';
                end
            end
            function z = inner()
                z = 'local';
            end
            """);

        ScriptRunResult result = await RunMatlab("""
            n = numel([1 2 3]);
            o = outer();
            f = foo(Meth());
            function y = numel(x)
                y = -444;
            end
            function y = foo(x)
                y = 'local foo';
            end
            """);

        Ok(result);
        Assert.Equal(-444.0, Number(result, "n"));
        Assert.Equal("nested", Text(result, "o"));
        Assert.Equal("local foo", Text(result, "f"));
    }

    /// <summary>A built-in class method beats a private function — the reverse of the documentation's steps 6 and 7 — and a private function answers where no method applies.</summary>
    [Fact]
    public async Task ABuiltinMethod_BeatsAPrivateFunction()
    {
        WriteFile(Path.Combine("private", "max.m"), Shadow("max", -998));

        ScriptRunResult result = await RunAsFile("main.m", """
            a = max([1 5 3]);
            b = max({1});
            """);

        Ok(result);
        Assert.Equal(5.0, Number(result, "a"));
        Assert.Equal(-998.0, Number(result, "b"));
    }

    /// <summary>A private function beats a user class method (R2025b: <c>bar(o)</c> is <c>private/bar.m</c>'s) and a current-folder file of the same name; private functions see their siblings.</summary>
    [Fact]
    public async Task APrivateFunction_BeatsAUserMethod_AndACurrentFolderFile_AndSeesItsSiblings()
    {
        WriteFile("Meth.m", """
            classdef Meth
                methods
                    function y = bar(obj)
                        y = 'method bar';
                    end
                end
            end
            """);
        WriteFile("dup.m", "function y = dup()\ny = 'cur dup';\nend\n");
        WriteFile(Path.Combine("private", "dup.m"), "function y = dup()\ny = 'private dup';\nend\n");
        WriteFile(Path.Combine("private", "bar.m"), "function y = bar(x)\ny = 'private bar';\nend\n");
        WriteFile(Path.Combine("private", "secret.m"), "function y = secret()\ny = 'private secret';\nend\n");
        WriteFile(Path.Combine("private", "secret2.m"), "function y = secret2()\ny = ['secret2->' secret() '|' dup()];\nend\n");

        ScriptRunResult result = await RunAsFile("main.m", """
            o = Meth();
            a = bar(o);
            b = dup();
            c = secret2();
            """);

        Ok(result);
        Assert.Equal("private bar", Text(result, "a"));
        Assert.Equal("private dup", Text(result, "b"));
        Assert.Equal("secret2->private secret|private dup", Text(result, "c"));
    }

    /// <summary>
    /// A user object dominates every built-in class: with no method of the name, resolution
    /// continues to the file rather than to the built-in, in either position; with a method, the
    /// method answers in either position.
    /// </summary>
    [Fact]
    public async Task AUserObject_DominatesEveryBuiltinClass()
    {
        WriteFile("sum.m", Shadow("sum", -555));
        WriteFile("NoSum.m", "classdef NoSum\nend\n");
        WriteFile("HasSum.m", """
            classdef HasSum
                methods
                    function y = sum(varargin)
                        y = -666;
                    end
                end
            end
            """);

        ScriptRunResult result = await RunMatlab("""
            on = NoSum(); o = HasSum();
            a = [sum(on, 2), sum(2, on), sum(o, 2), sum(2, o), sum([1 2])];
            """);

        Ok(result);
        Assert.Equal(new double[] { -555, -555, -666, -666, 3 }, (double[])Value(result, "a")!);
    }

    /// <summary>A user method beats a current-folder file, and dispatch is per call: <c>foo(o)</c> is the method's, <c>foo(1)</c> the file's.</summary>
    [Fact]
    public async Task AUserMethod_BeatsAFile_PerCall()
    {
        WriteFile("foo.m", "function y = foo(x)\ny = 'file foo';\nend\n");
        WriteFile("Meth.m", """
            classdef Meth
                methods
                    function y = foo(obj)
                        y = 'method foo';
                    end
                end
            end
            """);

        ScriptRunResult result = await RunMatlab("""
            o = Meth();
            a = foo(o);
            b = foo(1);
            """);

        Ok(result);
        Assert.Equal("method foo", Text(result, "a"));
        Assert.Equal("file foo", Text(result, "b"));
    }

    /// <summary>A bound name beats a user method: a variable holding a handle named like the method is what <c>foo(o)</c> calls.</summary>
    [Fact]
    public async Task ABoundHandle_BeatsAUserMethod()
    {
        WriteFile("Meth.m", """
            classdef Meth
                methods
                    function y = foo(obj)
                        y = 'method foo';
                    end
                end
            end
            """);

        ScriptRunResult result = await RunMatlab("""
            o = Meth();
            foo = @(x) 'bound';
            a = foo(o);
            """);

        Ok(result);
        Assert.Equal("bound", Text(result, "a"));
    }

    /// <summary>With no argument there is no class, so a bare <c>max</c> or <c>eps</c> reaches the file; <c>eps(1)</c> keeps the built-in and <c>eps("double")</c> reaches the file.</summary>
    [Fact]
    public async Task ABareName_HasNoClass_SoTheFileAnswers()
    {
        WriteFile("max.m", Shadow("max", -999));
        WriteFile("eps.m", Shadow("eps", -777));

        ScriptRunResult result = await RunMatlab("""
            a = max;
            b = eps;
            c = eps(1);
            d = eps("double");
            """);

        Ok(result);
        Assert.Equal(-999.0, Number(result, "a"));
        Assert.Equal(-777.0, Number(result, "b"));
        Assert.Equal(Math.Pow(2, -52), Number(result, "c"));
        Assert.Equal(-777.0, Number(result, "d"));
    }

    /// <summary>A file's local functions beat everything but variables inside that file only: <c>sum</c> local to <c>localshadow.m</c> answers -5 there and 3 in the script.</summary>
    [Fact]
    public async Task AFilesLocalFunctions_AreLocalToIt()
    {
        WriteFile("localshadow.m", """
            function y = localshadow()
                y = sum([1 2]);
            end
            function y = sum(x)
                y = -5;
            end
            """);

        ScriptRunResult result = await RunMatlab("""
            a = localshadow();
            b = sum([1 2]);
            """);

        Ok(result);
        Assert.Equal(-5.0, Number(result, "a"));
        Assert.Equal(3.0, Number(result, "b"));
    }

    /// <summary>The current folder beats every path folder whatever <c>addpath</c> did; and folder order decides between a script and a function of one name.</summary>
    [Fact]
    public async Task TheCurrentFolder_BeatsEveryPathFolder_AndFolderOrderDecidesNotKind()
    {
        WriteFile("helper.m", "function y = helper()\ny = 'cur';\nend\n");
        WriteFile(Path.Combine("lib", "helper.m"), "function y = helper()\ny = 'lib';\nend\n");
        WriteFile("ascript.m", "ran = 'the script';\n");
        WriteFile(Path.Combine("lib", "ascript.m"), "function ascript()\nerror('the function ran');\nend\n");

        ScriptRunResult result = await RunMatlab($"""
            addpath('{Escaped(_library)}');
            h = helper();
            ascript
            """);

        Ok(result);
        Assert.Equal("cur", Text(result, "h"));
        Assert.Equal("the script", Text(result, "ran"));
    }

    /// <summary>Private functions are visible only to files in the folder above <c>private/</c>: not to a path function elsewhere, and not to code with no file behind it.</summary>
    [Fact]
    public async Task PrivateFunctions_AreVisibleOnlyFromTheFolderAbove()
    {
        WriteFile(Path.Combine("private", "secret.m"), "function y = secret()\ny = 'private secret';\nend\n");
        WriteFile(Path.Combine("lib", "callsecret.m"), "function y = callsecret()\ny = secret();\nend\n");

        ScriptRunResult fromFile = await RunAsFile("main.m", $"""
            addpath('{Escaped(_library)}');
            a = secret();
            try
                b = callsecret();
            catch e
                b = e.message;
            end
            """);
        Ok(fromFile);
        Assert.Equal("private secret", Text(fromFile, "a"));
        Assert.Equal("'secret' is not recognized as a variable or a function.", Text(fromFile, "b"));

        ScriptRunResult fromNoFile = await RunMatlab("a = secret();");
        Assert.False(fromNoFile.Success);
        Assert.Contains("'secret' is not recognized", fromNoFile.Message, StringComparison.Ordinal);
    }

    /// <summary>A variable shadows everything and the call becomes an index; clearing it uncovers the function.</summary>
    [Fact]
    public async Task AVariable_ShadowsEverything_AndClearUncoversTheFunction()
    {
        WriteFile("max.m", Shadow("max", -999));

        ScriptRunResult result = await RunMatlab("""
            max = 7;
            try
                a = max([1 5 3]);
            catch
                a = 'index';
            end
            clear max
            b = max([1 5 3]);
            c = max({1});
            """);

        Ok(result);
        Assert.Equal("index", Text(result, "a"));
        Assert.Equal(5.0, Number(result, "b"));
        Assert.Equal(-999.0, Number(result, "c"));
    }

    /// <summary>JGraph's own library is C# and immune to a user's shadowing file, where MATLAB's <c>.m</c> toolbox code breaks under a <c>numel.m</c> (the recorded divergence in JGraph's favour).</summary>
    [Fact]
    public async Task JGraphsLibrary_IsImmuneToShadowing()
    {
        WriteFile("numel.m", Shadow("numel", -333));

        ScriptRunResult result = await RunMatlab("""
            t = table([1; 2; 3]);
            n = height(t);
            s = num2str(numel({1, 2}));
            """);

        Ok(result);
        Assert.Equal(3.0, Number(result, "n"));
        Assert.Equal("-333", Text(result, "s"));
    }

    /// <summary><c>clear;</c> as a bare statement with <c>clear.m</c> present runs the file: the variables survive and the file's side effect lands.</summary>
    [Fact]
    public async Task ClearAsABareStatement_ReachesClearM()
    {
        WriteFile("clear.m", "function clear(varargin)\nassignin('caller', 'CLEAR_SEEN', numel(varargin));\nend\n");

        ScriptRunResult result = await RunMatlab("""
            z = 5;
            clear;
            a = [exist('z'), exist('CLEAR_SEEN'), CLEAR_SEEN];
            clear z
            b = [exist('z'), CLEAR_SEEN];
            """);

        Ok(result);
        Assert.Equal(new double[] { 1, 1, 0 }, (double[])Value(result, "a")!);
        Assert.Equal(new double[] { 1, 1 }, (double[])Value(result, "b")!);
    }

    /// <summary>A discarded call dispatches like any other: <c>max([1 5 3]);</c> binds <c>ans</c> to the built-in's 5, <c>max({1});</c> to the file's -999.</summary>
    [Fact]
    public async Task ADiscardedCall_DispatchesLikeAnyOther()
    {
        WriteFile("max.m", Shadow("max", -999));

        ScriptRunResult result = await RunMatlab("""
            max([1 5 3]);
            a = ans;
            max({1});
            b = ans;
            """);

        Ok(result);
        Assert.Equal(5.0, Number(result, "a"));
        Assert.Equal(-999.0, Number(result, "b"));
    }

    /// <summary>
    /// The step-5 case that waited for the private layer: <c>x = a() + b()</c> where each path
    /// function's folder holds its own <c>private/helper.m</c>, and each gets its own.
    /// </summary>
    [Fact]
    public async Task TwoPathFunctions_EachGetTheirOwnPrivateHelper()
    {
        string lib1 = Path.Combine(_folder, "lib1");
        string lib2 = Path.Combine(_folder, "lib2");
        WriteFile(Path.Combine("lib1", "a.m"), "function y = a()\ny = helper();\nend\n");
        WriteFile(Path.Combine("lib1", "private", "helper.m"), "function y = helper()\ny = 100;\nend\n");
        WriteFile(Path.Combine("lib2", "b.m"), "function y = b()\ny = helper();\nend\n");
        WriteFile(Path.Combine("lib2", "private", "helper.m"), "function y = helper()\ny = 1;\nend\n");

        ScriptRunResult result = await RunMatlab($"""
            addpath('{Escaped(lib1)}');
            addpath('{Escaped(lib2)}');
            x = a() + b();
            try
                h = helper();
            catch e
                h = e.message;
            end
            """);

        Ok(result);
        Assert.Equal(101.0, Number(result, "x"));
        Assert.Equal("'helper' is not recognized as a variable or a function.", Text(result, "h"));
    }

    // --- Handles and name-based calls ---------------------------------------------------------

    /// <summary>
    /// A handle to a name captures the file that answered it where the handle was made, and keeps
    /// it after <c>cd</c>, while the written call no longer finds the file. (The file sits in a
    /// subfolder <c>cd</c> leaves, because the workspace root is always on JGraph's implicit path —
    /// the recorded convenience MATLAB does not have.)
    /// </summary>
    [Fact]
    public async Task AHandle_KeepsTheFileItCaptured_AfterCd()
    {
        string cur = Path.Combine(_folder, "cur");
        WriteFile(Path.Combine("cur", "mean.m"), Shadow("mean", -222));

        ScriptRunResult result = await RunMatlab($"""
            cd('{Escaped(cur)}');
            h = @mean;
            cd('{Escaped(_library)}');
            a = h([1 2 3]);
            b = mean([1 2 3]);
            """);

        Ok(result);
        Assert.Equal(-222.0, Number(result, "a"));
        Assert.Equal(2.0, Number(result, "b"));
    }

    /// <summary>
    /// The captured file and the built-in method coexist inside one handle: each invocation
    /// dispatches on its own arguments, direct, through <c>cellfun</c> (which never looks a name up)
    /// and through <c>feval</c>.
    /// </summary>
    [Fact]
    public async Task ACapturedFile_AndTheBuiltinMethod_CoexistInsideOneHandle()
    {
        WriteFile("max.m", Shadow("max", -999));

        ScriptRunResult result = await RunMatlab($$$"""
            hm = @max;
            cd('{{{Escaped(_library)}}}');
            a = hm({1});
            b = hm([1 5 3]);
            c = cellfun(hm, {{1}, [1 5 3]});
            d = feval(hm, {1});
            """);

        Ok(result);
        Assert.Equal(-999.0, Number(result, "a"));
        Assert.Equal(5.0, Number(result, "b"));
        Assert.Equal(new double[] { -999, 5 }, (double[])Value(result, "c")!);
        Assert.Equal(-999.0, Number(result, "d"));
    }

    /// <summary>
    /// A handle to <c>max</c> taken <em>inside</em> <c>max.m</c> is the same value as one taken
    /// outside: the file's main function is not a local function of its own file, so the built-in
    /// method still answers a double through it (R2025b: 5, -999, 'max').
    /// </summary>
    [Fact]
    public async Task AHandleTakenInsideTheFile_DispatchesLikeOneTakenOutside()
    {
        WriteFile("max.m", """
            function y = max(varargin)
                if nargin == 1 && ischar(varargin{1}) && strcmp(varargin{1}, 'handle')
                    y = @max;
                else
                    y = -999;
                end
            end
            """);

        ScriptRunResult result = await RunMatlab("""
            h = max('handle');
            a = h([1 5 3]);
            b = h({1});
            n = strcmp(func2str(h), func2str(@max));
            same = isequal(h, @max);
            """);

        Ok(result);
        Assert.Equal(5.0, Number(result, "a"));
        Assert.Equal(-999.0, Number(result, "b"));
        Assert.True(Assert.IsType<bool>(Value(result, "n")));
        Assert.True(Assert.IsType<bool>(Value(result, "same")));
    }

    /// <summary>Name-based calls follow the same order as a written call: <c>feval</c>, <c>str2func</c>, a handle applied, and <c>nargin</c> of the name reads the file.</summary>
    [Fact]
    public async Task NameBasedCalls_FollowTheWrittenOrder()
    {
        WriteFile("mean.m", Shadow("mean", -222));
        WriteFile("max.m", Shadow("max", -999));

        ScriptRunResult result = await RunMatlab("""
            a = feval('mean', [1 2 3]);
            f = str2func('mean');
            b = f([1 2 3]);
            g = @max;
            c = g({1});
            d = feval('max', [1 5 3]);
            e = feval('max', {1});
            n = nargin('max');
            """);

        Ok(result);
        Assert.Equal(-222.0, Number(result, "a"));
        Assert.Equal(-222.0, Number(result, "b"));
        Assert.Equal(-999.0, Number(result, "c"));
        Assert.Equal(5.0, Number(result, "d"));
        Assert.Equal(-999.0, Number(result, "e"));
        Assert.Equal(-1.0, Number(result, "n"));
    }

    /// <summary>
    /// The step-0 handle rows: <c>@sum</c> on a user object with and without the method, in both
    /// positions, direct, through the handle, <c>cellfun</c> and <c>feval</c> — all agree with the
    /// written call; after <c>cd</c> the handle keeps the file and the written call reaches the
    /// built-in, which refuses the object.
    /// </summary>
    [Fact]
    public async Task AHandleOnAUserObject_AgreesWithTheWrittenCall_EverywhereItIsCalled()
    {
        string cur = Path.Combine(_folder, "cur");
        WriteFile(Path.Combine("cur", "sum.m"), Shadow("sum", -555));
        WriteFile("NoSum.m", "classdef NoSum\nend\n");
        WriteFile("HasSum.m", """
            classdef HasSum
                methods
                    function y = sum(varargin)
                        y = -666;
                    end
                end
            end
            """);

        ScriptRunResult result = await RunMatlab($$"""
            cd('{{Escaped(cur)}}');
            o = HasSum(); on = NoSum();
            written = [sum(o), sum(1, o), sum(on), sum(1, on), sum([1 2])];
            hs = @sum;
            handled = [hs(o), hs(1, o), hs(on), hs(1, on), hs([1 2])];
            each = cellfun(hs, {o, on, [1 2]}, 'UniformOutput', false);
            each = [each{:}];
            evaled = [feval('sum', o), feval('sum', on), feval(hs, on)];
            cd('{{Escaped(_library)}}');
            kept = hs(on);
            try
                direct = sum(on);
            catch
                direct = 'builtin';
            end
            """);

        Ok(result);
        Assert.Equal(new double[] { -666, -666, -555, -555, 3 }, (double[])Value(result, "written")!);
        Assert.Equal(new double[] { -666, -666, -555, -555, 3 }, (double[])Value(result, "handled")!);
        Assert.Equal(new double[] { -666, -555, 3 }, (double[])Value(result, "each")!);
        Assert.Equal(new double[] { -666, -555, -555 }, (double[])Value(result, "evaled")!);
        Assert.Equal(-555.0, Number(result, "kept"));
        Assert.Equal("builtin", Text(result, "direct"));
    }

    /// <summary>A handle to a private function keeps it after <c>cd</c>, as a handle to any file does.</summary>
    [Fact]
    public async Task AHandleToAPrivateFunction_SurvivesCd()
    {
        WriteFile(Path.Combine("private", "secret.m"), "function y = secret()\ny = 'private secret';\nend\n");

        ScriptRunResult result = await RunAsFile("main.m", $"""
            hs = @secret;
            cd('{Escaped(_library)}');
            a = hs();
            """);

        Ok(result);
        Assert.Equal("private secret", Text(result, "a"));
    }

    // --- Files that appear and disappear, through a call ------------------------------------------

    /// <summary>
    /// Step 3's every-writer test, asserted through a call: <c>mean.m</c> written by each writer in
    /// one loop pass answers <c>mean([1 2 3])</c> in that same pass, and the built-in answers again
    /// once <c>delete</c> has removed it (a deleted file falls through, the recorded divergence).
    /// </summary>
    [Fact]
    public async Task EveryWriter_IsSeen_ThroughACall_InTheStatementThatWrote()
    {
        File.WriteAllText(Path.Combine(_folder, "seed.txt"), "function y = mean(x)\ny = -1;\nend\n");

        ScriptRunResult result = await RunMatlab("""
            after = zeros(1, 7); gone = zeros(1, 7);
            for w = 1:7
                switch w
                    case 1
                        fid = fopen('mean.m', 'w'); fprintf(fid, 'function y = mean(x)\ny = -1;\nend\n'); fclose(fid);
                    case 2
                        writelines(["function y = mean(x)", "y = -1;", "end"], 'mean.m');
                    case 3
                        copyfile('seed.txt', 'mean.m');
                    case 4
                        copyfile('seed.txt', 'moving.txt');
                        movefile('moving.txt', 'mean.m');
                    case 5
                        copyfile('seed.txt', 'mean.m');
                    case 6
                        writelines(["function y = mean(x)", "y = -1;", "end"], 'mean.m');
                    case 7
                        copyfile('seed.txt', 'mean.m');
                end
                after(w) = mean([1 2 3]);
                delete('mean.m');
                gone(w) = mean([1 2 3]);
            end
            """);

        Ok(result);
        Assert.Equal(new double[] { -1, -1, -1, -1, -1, -1, -1 }, (double[])Value(result, "after")!);
        Assert.Equal(new double[] { 2, 2, 2, 2, 2, 2, 2 }, (double[])Value(result, "gone")!);
    }

    // --- The loop JIT -------------------------------------------------------------------------------

    /// <summary>
    /// Both JIT settings answer byte for byte: with <c>eps.m</c> present a bare <c>eps</c> in a loop
    /// reaches the file (the constant is not folded) while <c>eps(1)</c> keeps the built-in; with
    /// <c>abs.m</c> and <c>atan2.m</c> present a logical argument keeps the built-in for <c>abs</c>
    /// and reaches the file for <c>atan2</c>, as the table measured, so the compiled loop binds the
    /// one and refuses the other.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheLoopJit_AnswersWhatTheWalkAnswers_WithShadowingFiles(bool jit)
    {
        WriteFile("eps.m", Shadow("eps", -777));
        WriteFile("abs.m", Shadow("abs", -666));
        WriteFile("atan2.m", Shadow("atan2", -321));

        bool previous = JgsLoopJit.Enabled;
        JgsLoopJit.Enabled = jit;
        try
        {
            ScriptRunResult result = await RunMatlab("""
                x = 0; y = 0; s = 0; t = 0; k = 0;
                while k < 4
                    k = k + 1;
                    x = x + eps;
                    y = y + eps(1);
                    s = s + abs(k > 2);
                    t = t + atan2(k > 2, 1);
                end
                """);

            Ok(result);
            Assert.Equal(-777.0 * 4, Number(result, "x"));
            Assert.Equal(4 * Math.Pow(2, -52), Number(result, "y"));
            Assert.Equal(2.0, Number(result, "s"));
            Assert.Equal(-321.0 * 4, Number(result, "t"));
        }
        finally
        {
            JgsLoopJit.Enabled = previous;
        }
    }

    // --- Step 7: builtin(), which -all, exist, the shadowing warning -----------------------------

    /// <summary>
    /// <c>builtin</c> hands the arguments over as written, so the target's own string policy
    /// applies: <c>builtin('class', "abc")</c> is <c>string</c> like <c>class("abc")</c>. A name the
    /// layer holds as a value is the zero-argument function MATLAB has under it.
    /// </summary>
    [Fact]
    public async Task Builtin_ForwardsTheArgumentsAsWritten()
    {
        ScriptRunResult result = await RunMatlab("""
            a = builtin('class', "abc");
            b = builtin('class', 'abc');
            c = class("abc");
            d = builtin("class", 1);
            p = builtin('pi');
            """);

        Ok(result);
        Assert.Equal("string", Text(result, "a"));
        Assert.Equal("char", Text(result, "b"));
        Assert.Equal("string", Text(result, "c"));
        Assert.Equal("double", Text(result, "d"));
        Assert.Equal(Math.PI, Number(result, "p"));
    }

    /// <summary>The output count is forwarded: <c>[m, n] = builtin('size', A)</c> reaches <c>size</c>'s multi-output body.</summary>
    [Fact]
    public async Task Builtin_ForwardsTheOutputCount()
    {
        ScriptRunResult result = await RunMatlab("""
            A = [1 2; 3 4; 5 6];
            [m, n] = builtin('size', A);
            s = builtin('size', A);
            """);

        Ok(result);
        Assert.Equal(3.0, Number(result, "m"));
        Assert.Equal(2.0, Number(result, "n"));
        Assert.Equal(new double[] { 3, 2 }, (double[])Value(result, "s")!);
    }

    /// <summary>
    /// A statement forwards its statement-ness: <c>builtin('ecdf', x);</c> draws because <c>ecdf</c>
    /// is told nobody wanted the numbers, <c>builtin('size', A);</c> binds <c>ans</c> because
    /// <c>size</c> would have, and <c>builtin('disp', x);</c> leaves no <c>ans</c> behind.
    /// </summary>
    [Fact]
    public async Task Builtin_AsAStatement_RunsTheTargetsStatementForm()
    {
        ScriptRunResult result = await RunMatlab("""
            builtin('size', [1 2 3]);
            s = ans;
            clear ans
            builtin('disp', 'DISPLINE');
            gone = exist('ans');
            builtin('ecdf', [1 2 3 4]);
            """);

        Ok(result);
        Assert.Equal(new double[] { 1, 3 }, (double[])Value(result, "s")!);
        Assert.Equal(0.0, Number(result, "gone"));
        Assert.Contains("DISPLINE", _output.NormalText, StringComparison.Ordinal);
        Assert.Single(JG.Gca().Plots);
    }

    /// <summary>
    /// The call site is forwarded without the selector, so the <c>table</c> wrapper names the
    /// variables from <c>builtin('table', A, B)</c> exactly as it does from <c>table(A, B)</c>.
    /// </summary>
    [Fact]
    public async Task Builtin_ForwardsTheCallSite_SoTableNamesItsVariables()
    {
        ScriptRunResult result = await RunMatlab("""
            A = [1; 2]; B = [3; 4];
            t = builtin('table', A, B);
            ok = isequal(t.Properties.VariableNames, {'A', 'B'});
            u = table(A, B);
            same = isequal(u.Properties.VariableNames, t.Properties.VariableNames);
            """);

        Ok(result);
        Assert.True(Assert.IsType<bool>(Value(result, "ok")));
        Assert.True(Assert.IsType<bool>(Value(result, "same")));
    }

    /// <summary>
    /// A name the layer does not hold errors with MATLAB's words and identifier, a local function
    /// of the script included: <c>builtin</c> reaches the layer and nothing above it. The
    /// argument errors carry MATLAB's identifiers too.
    /// </summary>
    [Fact]
    public async Task Builtin_ErrorsWithMatlabsWords_ForANameTheLayerLacks()
    {
        ScriptRunResult result = await RunMatlab("""
            try; builtin('helper'); id1 = ''; m1 = ''; catch e; id1 = e.identifier; m1 = e.message; end
            try; builtin('loc'); m2 = ''; catch e; m2 = e.message; end
            try; builtin(); id3 = ''; catch e; id3 = e.identifier; end
            try; builtin(1); id4 = ''; m4 = ''; catch e; id4 = e.identifier; m4 = e.message; end
            y = loc();
            function y = loc()
            y = 1;
            end
            """);

        Ok(result);
        Assert.Equal("MATLAB:dispatcher:CannotFindBuiltinFunction", Text(result, "id1"));
        Assert.Equal("Cannot find built-in function 'helper'", Text(result, "m1"));
        Assert.Equal("Cannot find built-in function 'loc'", Text(result, "m2"));
        Assert.Equal("MATLAB:minrhs", Text(result, "id3"));
        Assert.Equal("MATLAB:string:MustBeStringScalarOrCharacterVector", Text(result, "id4"));
        Assert.Equal("Argument must be a text scalar.", Text(result, "m4"));
        Assert.Equal(1.0, Number(result, "y"));
    }

    /// <summary>
    /// With <c>max.m</c> taking every written call a cell reaches, <c>builtin('max', …)</c> reaches
    /// the built-in for a double and for a cell alike — the file is never consulted.
    /// </summary>
    [Fact]
    public async Task Builtin_ReachesTheBuiltinPastAShadowingFile()
    {
        WriteFile("max.m", Shadow("max", -999));

        ScriptRunResult result = await RunMatlab("""
            a = builtin('max', [1 5 3]);
            b = max({1});
            try; c = builtin('max', {1}); catch; c = NaN; end
            fileTookIt = isequal(c, -999);
            """);

        Ok(result);
        Assert.Equal(5.0, Number(result, "a"));
        Assert.Equal(-999.0, Number(result, "b"));
        Assert.False(Assert.IsType<bool>(Value(result, "fileTookIt")));
    }

    /// <summary>
    /// <c>builtin</c> is a name like any other: <c>builtin.m</c> beside the script takes the written
    /// call, <c>feval('builtin', …)</c> and <c>@builtin</c> — R2025b's three answers — while
    /// <c>exist</c> still says 5 and <c>which</c> names the file.
    /// </summary>
    [Fact]
    public async Task BuiltinDotM_ShadowsBuiltinItself()
    {
        WriteFile("builtin.m", Shadow("builtin", -1));

        ScriptRunResult result = await RunMatlab("""
            a = builtin('class', 1);
            b = feval('builtin', 'class', 1);
            h = @builtin;
            c = h('class', 1);
            k = exist('builtin');
            w = which('builtin');
            """);

        Ok(result);
        Assert.Equal(-1.0, Number(result, "a"));
        Assert.Equal(-1.0, Number(result, "b"));
        Assert.Equal(-1.0, Number(result, "c"));
        Assert.Equal(5.0, Number(result, "k"));
        Assert.EndsWith("builtin.m", Text(result, "w"), StringComparison.Ordinal);
    }

    /// <summary>Unshadowed, the same three roads reach the forwarder.</summary>
    [Fact]
    public async Task Builtin_IsReachedByFevalAndByAHandle()
    {
        ScriptRunResult result = await RunMatlab("""
            a = feval('builtin', 'class', 1);
            h = @builtin;
            b = h('class', "s");
            n = nargin('builtin');
            """);

        Ok(result);
        Assert.Equal("double", Text(result, "a"));
        Assert.Equal("string", Text(result, "b"));
        Assert.Equal(1.0, Number(result, "n"));
    }

    /// <summary>
    /// <c>exist</c> by the rules R2025b answered: 5 for a built-in whether or not a file shadows it,
    /// 2 for a file the resolver would run — a private function and a local function of the running
    /// file included — 7 for a folder, 0 for <c>exist('sin', 'file')</c>, and a named kind asks
    /// about that kind alone.
    /// </summary>
    [Fact]
    public async Task Exist_AnswersByTheLayers()
    {
        WriteFile("max.m", Shadow("max", -999));
        WriteFile("plain.m", "function y = plain()\ny = 1;\nend\n");
        WriteFile(Path.Combine("private", "secret.m"), "function y = secret()\ny = 7;\nend\n");
        Directory.CreateDirectory(Path.Combine(_folder, "fixdir"));

        ScriptRunResult result = await RunAsFile("main.m", """
            e = [exist('max'), exist('max', 'builtin'), exist('max', 'file'), ...
                 exist('secret'), exist('secret', 'file'), exist('loc'), exist('loc', 'builtin'), ...
                 exist('plain'), exist('nosuch'), exist('fixdir'), exist('fixdir', 'dir'), ...
                 exist('fixdir', 'file'), exist('sin'), exist('sin', 'file'), exist('sin', 'builtin')];
            function y = loc()
            y = 1;
            end
            """);

        Ok(result);
        Assert.Equal(new double[] { 5, 5, 2, 2, 2, 2, 0, 2, 0, 7, 7, 7, 5, 0, 5 }, (double[])Value(result, "e")!);
    }

    /// <summary>
    /// <c>which(name)</c> is the layer that would answer; <c>which(name, '-all')</c> — either
    /// argument order — is every layer holding it as a cell column, files first, and 0-by-0 when
    /// none does. A private file and a local function name their file.
    /// </summary>
    [Fact]
    public async Task Which_NamesTheLayerThatAnswers_AndAllOfThem()
    {
        WriteFile("max.m", Shadow("max", -999));
        WriteFile("plain.m", "function y = plain()\ny = 1;\nend\n");
        WriteFile(Path.Combine("private", "secret.m"), "function y = secret()\ny = 7;\nend\n");

        ScriptRunResult result = await RunAsFile("main.m", """
            a = which('max'); b = which('sin'); c = which('secret'); d = which('nosuch');
            e = which('plain'); f = which('loc');
            g = which('max', '-all'); h = which('-all', 'max'); k = which('nosuch', '-all');
            g1 = g{1}; g2 = g{2}; gs = size(g); hs = size(h); ks = size(k);
            same = isequal(g, h);
            function y = loc()
            y = 1;
            end
            """);

        Ok(result);
        Assert.Equal(Path.Combine(_folder, "max.m"), Text(result, "a"));
        Assert.Equal("sin is a built-in function.", Text(result, "b"));
        Assert.Equal(Path.Combine(_folder, "private", "secret.m"), Text(result, "c"));
        Assert.Equal("", Text(result, "d"));
        Assert.Equal(Path.Combine(_folder, "plain.m"), Text(result, "e"));
        Assert.Equal(Path.Combine(_folder, "main.m"), Text(result, "f"));
        Assert.Equal(Path.Combine(_folder, "max.m"), Text(result, "g1"));
        Assert.Equal("max is a built-in function.", Text(result, "g2"));
        Assert.Equal(new double[] { 2, 1 }, (double[])Value(result, "gs")!);
        Assert.Equal(new double[] { 2, 1 }, (double[])Value(result, "hs")!);
        Assert.Equal(new double[] { 0, 0 }, (double[])Value(result, "ks")!);
        Assert.True(Assert.IsType<bool>(Value(result, "same")));
    }

    /// <summary>
    /// A file that takes a built-in's name is warned about with MATLAB's own words, once per name,
    /// through the script's <c>warning</c> — so <c>lastwarn</c> keeps it — at the <c>addpath</c>
    /// that brought the folder in, and the file keeps the name.
    /// </summary>
    [Fact]
    public async Task AShadowingFile_IsWarnedAboutOnce_ThroughWarning()
    {
        File.WriteAllText(Path.Combine(_library, "abs.m"), Shadow("abs", -777));

        ScriptRunResult result = await RunMatlab($$"""
            lastwarn('');
            addpath('{{Escaped(_library)}}');
            w = lastwarn;
            a = abs({-3});
            b = abs(-3);
            addpath('{{Escaped(_library)}}');
            c = abs({-3});
            """);

        Ok(result);
        const string expected = "Function abs has the same name as a MATLAB built-in. "
            + "We suggest you rename the function to avoid a potential name conflict.";
        Assert.Equal(expected, Text(result, "w"));
        Assert.Equal(-777.0, Number(result, "a"));
        Assert.Equal(3.0, Number(result, "b"));
        Assert.Equal(-777.0, Number(result, "c"));
        Assert.Equal(1, _output.ErrorText.Split("Function abs has the same name").Length - 1);
    }

    /// <summary>The same warning for a file in the current folder, raised when the index is first built and not again.</summary>
    [Fact]
    public async Task AShadowingFileInTheCurrentFolder_IsWarnedAboutOnce()
    {
        WriteFile("max.m", Shadow("max", -999));

        ScriptRunResult result = await RunMatlab("""
            a = max({1});
            b = max({2});
            w = lastwarn;
            """);

        Ok(result);
        Assert.Equal(-999.0, Number(result, "a"));
        Assert.Equal(-999.0, Number(result, "b"));
        Assert.StartsWith("Function max has the same name as a MATLAB built-in.", Text(result, "w"), StringComparison.Ordinal);
        Assert.Equal(1, _output.ErrorText.Split("Function max has the same name").Length - 1);
    }

    /// <summary>
    /// R2025b, measured in step 8 (<c>prec8/probe8.out</c>): an anonymous body asks the current
    /// folder when it is <em>called</em>, not when the handle is made. Beside <c>max.m</c>,
    /// <c>@() max({1})</c> is the file's -999 direct, with a parameter, through <c>cellfun</c> and
    /// from a local function; after <c>cd</c> to a folder without the file the same handle reaches
    /// the built-in, and a handle made there answers the file once back beside it. (Before the
    /// fix the handle captured the built-in as a local function of its body and never asked the
    /// folders.)
    /// </summary>
    [Fact]
    public async Task AnAnonymousBody_AsksTheFolders_WhenItIsCalled()
    {
        string cur = Path.Combine(_folder, "cur");
        WriteFile(Path.Combine("cur", "max.m"), Shadow("max", -999));

        ScriptRunResult result = await RunMatlab($$$"""
            cd('{{{Escaped(cur)}}}');
            f = @() max({1});
            g = @(c) max(c);
            a = f();
            b = g([1 5 3]);
            c = g({1});
            d = cellfun(@(c) max(c), {{1}});
            e = local_anon();
            cd('{{{Escaped(_library)}}}');
            try
                p = f();
            catch err
                p = 'built-in';
            end
            q = g([1 5 3]);
            h = @() max({1});
            cd('{{{Escaped(cur)}}}');
            r = h();
            function k = local_anon()
                hh = @() max({1});
                k = hh();
            end
            """);

        Ok(result);
        Assert.Equal(-999.0, Number(result, "a"));
        Assert.Equal(5.0, Number(result, "b"));
        Assert.Equal(-999.0, Number(result, "c"));
        Assert.Equal(new double[] { -999 }, (double[])Value(result, "d")!);
        Assert.Equal(-999.0, Number(result, "e"));
        Assert.Equal("built-in", Text(result, "p"));
        Assert.Equal(5.0, Number(result, "q"));
        Assert.Equal(-999.0, Number(result, "r"));
    }
}
