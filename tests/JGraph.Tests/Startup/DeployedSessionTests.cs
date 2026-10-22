using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Startup;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Startup;

/// <summary>
/// The deployed session (compiler and SDK plan, stage K1), run the way the hidden
/// <c>jgraph -batch main.m --deployed &lt;folder&gt; -- args</c> switch runs it, against what stage K0
/// recorded R2025b's compiled programs doing (<c>tools/matlab-checklist/compiler-probes/recordings</c>),
/// and the deployment builtins in an ordinary session against <c>probe_k1_session</c>.
/// </summary>
[Collection("JG facade")]
public sealed class DeployedSessionTests : IDisposable
{
    private static readonly IScriptEngine[] Engines = [new JgsScriptEngine(), new MatlabScriptEngine()];

    private readonly RecordingScriptOutput _output = new();
    private readonly string _root =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k1-" + Guid.NewGuid().ToString("N"))).FullName;

    public DeployedSessionTests() => JG.Reset();

    private string Ctf => Path.Combine(_root, "ctf");

    private string Component => Path.Combine(Ctf, "app");

    private string Assets => Path.Combine(Ctf, "assets");

    private string Run => Path.Combine(_root, "run");

    public void Dispose()
    {
        JG.Reset();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }

    /// <summary>Writes a file under the stand-in archive, the run folder, or anywhere under the root.</summary>
    private string Put(string folder, string name, string text)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, text.ReplaceLineEndings("\n"));
        return path;
    }

    /// <summary>Runs <paramref name="main"/> as a deployed program's main from the run folder.</summary>
    private async Task<int> Deployed(string main, string[]? arguments = null, bool numeric = false)
    {
        Directory.CreateDirectory(Run);
        var line = new List<string> { "-batch", main, "--deployed", Ctf, "-sd", Run };
        if (numeric)
        {
            line.Add("--deployed-numeric");
        }

        line.Add("--");
        line.AddRange(arguments ?? []);
        StartupOptions options = StartupCommandLine.Parse(line);
        Assert.False(options.HasUsageError, options.UsageError);
        return await BatchRunner.RunAsync(options, Engines, _output, static (_, _) => { });
    }

    private string Main(string body) => Put(Component, "app.m", "function app(varargin)\n" + body + "\nend\n");

    /// <summary>Runs MATLAB code in an ordinary session and fails with its errors if it fails.</summary>
    private async Task Ordinary(string code)
    {
        await using IScriptSession session = Assert.IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
            .CreateSession(new ScriptContext(_output, static (_, _) => { }));
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    // --- An ordinary session (probe_k1_session) ------------------------------------------------

    [Fact]
    public Task OrdinarySession_AnswersAsR2025bsMatlabDoes() => Ordinary("""
        assert(isequal(isdeployed, false) && islogical(isdeployed));
        assert(isequal(ismcc, false));
        assert(strcmp(ctfroot, matlabroot));
        v = mcrversion;
        assert(isequal(v, 25));
        [a, b] = mcrversion;
        assert(isequal([a b], [25 2]));
        [a, b, c, d, e] = mcrversion;
        assert(isequal([a b c d e], [25 2 0 0 0]));
        assert(contains(mcrcachedir, 'RuntimeCache'));
        none = getmcruserdata('k1nosuch');
        assert(isequal(size(none), [1 0]) && isa(none, 'double'));
        assert(isequal(size(getmcruserdata("k1nosuch")), [1 0]));
        setmcruserdata('k1key', 'k1value');
        assert(strcmp(getmcruserdata('k1key'), 'k1value'));
        setmcruserdata('k1num', [1 2 3]);
        assert(isequal(getmcruserdata('k1num'), [1 2 3]));
        setmcruserdata('k1key', 7);
        assert(isequal(getmcruserdata('k1key'), 7));
        assert(isempty(getmcruserdata('K1KEY')));
        setmcruserdata("k1str", 3);
        assert(isequal(getmcruserdata('k1str'), 3));
        """);

    [Fact]
    public Task OrdinarySession_RefusesInR2025bsWords() => Ordinary("""
        function check(f, id, message)
            try
                f();
                error('k1:none', 'no error');
            catch e
                assert(strcmp(e.identifier, id), e.identifier);
                assert(strcmp(e.message, message), e.message);
            end
        end

        check(@() isdeployed(1), 'MATLAB:maxrhs', 'Too many input arguments.');
        check(@() ismcc(1), 'MATLAB:maxrhs', 'Too many input arguments.');
        check(@() ctfroot(1), 'MATLAB:TooManyInputs', 'Too many input arguments.');
        check(@() mcrversion(1), 'MATLAB:TooManyInputs', 'Too many input arguments.');
        check(@() mcrcachedir(1), 'MATLAB:mcrcachedir:tooManyInputArgs', 'Too many input arguments.');
        check(@() getmcruserdata(), 'MATLAB:minrhs', 'Not enough input arguments.');
        check(@() getmcruserdata('a', 'b'), 'MATLAB:maxrhs', 'Too many input arguments.');
        check(@() getmcruserdata(5), 'MCR:mclinstancedata:MCLMCR_WRONG_USER_DATA_KEY_TYPE', ...
            sprintf('Wrong type of data used as MATLAB Runtime user data key. Keys must be scalar strings or character vectors.\n\nActual key type: ''double''.'));
        check(@() setmcruserdata('k1key'), 'MATLAB:minrhs', 'Not enough input arguments.');
        check(@() setmcruserdata('a', 1, 2), 'MATLAB:maxrhs', 'Too many input arguments.');
        check(@() waitforallfiguresclosed(), 'MATLAB:UndefinedFunction', 'Unrecognized function or variable ''waitforallfiguresclosed''.');
        """);

    [Fact]
    public async Task OrdinarySession_SetmcruserdataGivesNoOutput()
    {
        await using IScriptSession session = Assert.IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
            .CreateSession(new ScriptContext(_output, static (_, _) => { }));
        ScriptRunResult result = await session.ExecuteAsync(
            "try, x = setmcruserdata('k1o', 1); catch e, disp(e.identifier); end", sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Contains("MATLAB:maxlhs", _output.NormalText);
    }

    [Fact]
    public Task Beep_AnswersItsSwitchWithoutSounding() => Ordinary("""
        assert(strcmp(beep, 'on'));
        beep('off');
        s = beep;
        assert(strcmp(s, 'off'));
        """);

    [Fact]
    public Task UndefinedCall_WithNoArgumentsLeftAfterExpansion_IsUnrecognized() => Ordinary("""
        function call(varargin)
            k1_nosuch(varargin{:});
        end

        try
            call();
            error('k1:none', 'no error');
        catch e
            assert(strcmp(e.identifier, 'MATLAB:UndefinedFunction'), e.identifier);
            assert(strcmp(e.message, 'Unrecognized function or variable ''k1_nosuch''.'), e.message);
        end
        try
            feval('k1_nosuch', 2);
        catch e
            assert(strcmp(e.message, 'Undefined function ''k1_nosuch'' for input arguments of type ''double''.'), e.message);
        end
        try
            feval('k1_nosuch');
        catch e
            assert(strcmp(e.message, 'Unrecognized function or variable ''k1_nosuch''.'), e.message);
        end
        """);

    // --- The command line and the deployment ------------------------------------------------------

    [Fact]
    public void CommandLine_TakesTheHiddenSwitchesAndTheProgramsArguments()
    {
        StartupOptions options = StartupCommandLine.Parse(["-batch", "app.m", "--deployed", "ctf", "--deployed-numeric", "--", "1", "-x", "--"]);
        Assert.False(options.HasUsageError, options.UsageError);
        Assert.Equal("ctf", options.DeployedRoot);
        Assert.True(options.NumericArguments);
        Assert.Equal(["1", "-x", "--"], options.ProgramArguments);

        Assert.True(StartupCommandLine.Parse(["--deployed", "ctf"]).HasUsageError);
        Assert.True(StartupCommandLine.Parse(["-batch", "app.m", "--deployed-numeric"]).HasUsageError);
        Assert.True(StartupCommandLine.Parse(["-batch", "app.m", "--", "1"]).HasUsageError);
        Assert.DoesNotContain("deployed", StartupHelp.UsageText, StringComparison.Ordinal);
    }

    [Fact]
    public void Deployment_TakesUserDataOutOfTheCommandLine()
    {
        (IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> data) = ScriptDeployment.TakeUserData(
            ["-mcruserdata", "ParallelProfile:k0.mlsettings", "after", "-mcruserdata", "nocolon", "-mcruserdata", "Path:C:\\x"]);
        Assert.Equal(["after", "-mcruserdata", "nocolon"], arguments);
        Assert.Equal("k0.mlsettings", data["ParallelProfile"]);
        Assert.Equal("C:\\x", data["Path"]);
    }

    [Fact]
    public void Deployment_FromFolder_PutsTheMainsFolderFirstAndLeavesOutWhatIsNeverOnAPath()
    {
        string main = Main("disp(1);");
        Put(Assets, "data.txt", "x");
        Put(Path.Combine(Component, "private"), "p.m", "function p\nend\n");
        Put(Path.Combine(Ctf, "+pkg"), "f.m", "function f\nend\n");
        Put(Path.Combine(Ctf, ".matlab"), "prefs.xml", "<x/>");
        Directory.CreateDirectory(Path.Combine(Ctf, "empty"));

        ScriptDeployment deployment = ScriptDeployment.FromFolder(Ctf, main, ["a"], numericArguments: false);

        Assert.Equal([Component, Assets], deployment.FrozenPath);
        Assert.Equal("app", deployment.ComponentName);
        Assert.Equal(Ctf, deployment.CtfRoot);
        Assert.Equal(["a"], deployment.Arguments);
    }

    // --- The main function's arguments (K0: args_plain, argsn_ok, argsn_word) --------------------

    [Fact]
    public async Task Arguments_ArriveAsCharRowsJustAsTheyWereGiven()
    {
        string main = Main("""
            fprintf('nargin=%d\n', nargin);
            for k = 1:nargin
                fprintf('%d|%s|%s|%s\n', k, class(varargin{k}), mat2str(size(varargin{k})), varargin{k});
            end
            fprintf('deployed=%d\n', isdeployed);
            """);

        int code = await Deployed(main, ["1", "two", "three four", "a\"b", "c:\\x\\", "", "-x", "[1 2]"]);

        Assert.Equal(0, code);
        Assert.Equal(
            [
                "nargin=8", "1|char|[1 1]|1", "2|char|[1 3]|two", "3|char|[1 10]|three four", "4|char|[1 3]|a\"b",
                "5|char|[1 5]|c:\\x\\", "6|char|[0 0]|", "7|char|[1 2]|-x", "8|char|[1 5]|[1 2]", "deployed=1",
            ],
            _output.NormalLines);
    }

    [Fact]
    public async Task NumericArguments_AreEvaluatedWhenTheyLookNumeric()
    {
        string main = Main("""
            for k = 1:nargin
                fprintf('%s %s\n', class(varargin{k}), mat2str(varargin{k}));
            end
            """);

        int code = await Deployed(main, ["1", "2.5", "[1 2;3 4]", "-3", "1e3", "1+2i", "'txt'", "(1)", " 7"], numeric: true);

        Assert.Equal(0, code);
        Assert.Equal(
            ["double 1", "double 2.5", "double [1 2;3 4]", "double -3", "double 1000", "double 1+2i", "char 'txt'", "double 1", "double 7"],
            _output.NormalLines);
    }

    [Fact]
    public async Task NumericArguments_RefuseAWordBeforeTheMainRuns()
    {
        string main = Main("disp('main ran');");

        int code = await Deployed(main, ["hello"], numeric: true);

        Assert.Equal(ScriptDeployment.UncaughtErrorExitCode, code);
        Assert.DoesNotContain("main ran", _output.NormalText);
        // R2025b's message keeps a space before its first line break (K0, argsn_word).
        Assert.Equal(
            "Error using mainFuncForNumericStandaloneApplication>findArgType (line 21)\n"
            + "The input argument \n  'hello'\nis not recognized as a numeric type.\n\n"
            + "Error in mainFuncForNumericStandaloneApplication (line 11)\n\n"
            + "MCR:mainFuncForNumericStandaloneApplication:COM_ERROR_INPUT_ARGUMENT_TYPE",
            _output.ErrorText);
    }

    /// <summary>R2025b's mclisnumerictype over probe_k0_numeric's and probe_k1_numeric's strings.</summary>
    [Theory]
    [InlineData("1", true), InlineData("2.5", true), InlineData("-3", true), InlineData("+4", true), InlineData("1e3", true)]
    [InlineData("1E-3", true), InlineData(".5", true), InlineData("5.", true), InlineData("[1,2;3 4]", true), InlineData("[]", true)]
    [InlineData("3i", true), InlineData("0x1F", true), InlineData("0b101", true), InlineData("1_000", true), InlineData(" 7", true)]
    [InlineData("(1)", true), InlineData("'txt'", true), InlineData("", true), InlineData("--1", true), InlineData("1e", true)]
    [InlineData("3*2", true), InlineData("1i", true), InlineData(".e", true), InlineData("0xG", true), InlineData("\"txt\"", true)]
    [InlineData("'a'b", true), InlineData("@sin", true), InlineData("1ee", true), InlineData("1_", true), InlineData("_1", true)]
    [InlineData("1a", true), InlineData("12x", true), InlineData("1Inf", true), InlineData("1d3", true), InlineData("3e", true)]
    [InlineData("{1}", true), InlineData("1:3", true), InlineData("#", true), InlineData("1'", true), InlineData("[1 (2)]", true)]
    [InlineData("Inf", false), InlineData("-inf", false), InlineData("NaN", false), InlineData("pi", false), InlineData("true", false)]
    [InlineData("j", false), InlineData("int8(4)", false), InlineData("hello", false), InlineData("e3", false), InlineData("1+pi", false)]
    [InlineData("1+a", false), InlineData("[1 pi]", false), InlineData("i", false), InlineData("x1", false), InlineData("e", false)]
    [InlineData("[e]", false), InlineData("(e)", false), InlineData("-e", false), InlineData("+i", false), InlineData("[j]", false)]
    [InlineData("Inf1", false), InlineData("F", false), InlineData("true1", false), InlineData("i1", false), InlineData("x", false)]
    public void NumericArguments_FollowMclisnumerictype(string argument, bool evaluated) =>
        Assert.Equal(evaluated, JgsDeployedMain.LooksNumeric(argument));

    // --- How a program ends (K0: exit_*) ---------------------------------------------------------

    [Theory]
    [InlineData("disp('done');", 0)]
    [InlineData("exit(3);", 3)]
    [InlineData("quit(4);", 4)]
    [InlineData("quit(5, 'force');", 5)]
    [InlineData("warning('k1:careful', 'Careful with %s', 'that');", 0)]
    public async Task ExitCode_IsWhatTheProgramAskedFor(string body, int expected) =>
        Assert.Equal(expected, await Deployed(Main(body)));

    [Fact]
    public async Task UncaughtError_IsReportedAsR2025bsRuntimeReportsIt()
    {
        string main = Put(Component, "app.m", """
            function app(mode)
            switch mode
                case 'error'
                    error('k0:boom', 'Boom %d happened', 7);
                case 'nested'
                    inner(3);
                case 'cause'
                    ME = MException('k0:outer', 'Outer failure');
                    ME = addCause(ME, MException('k0:inner', 'Inner reason'));
                    throw(ME);
            end
            end

            function inner(n)
            if n > 0
                inner(n - 1);
            else
                error('k0:deep', 'Deep error at depth zero');
            end
            end
            """);

        Assert.Equal(249, await Deployed(main, ["error"]));
        Assert.Equal("Error using app (line 4)\nBoom 7 happened\n\nk0:boom", _output.ErrorText);

        _output.Errors.Clear();
        Assert.Equal(249, await Deployed(main, ["nested"]));
        Assert.Equal(
            "Error using app>inner (line 18)\nDeep error at depth zero\n\n"
            + "Error in app>inner (line 16)\n\nError in app>inner (line 16)\n\nError in app>inner (line 16)\n\n"
            + "Error in app (line 6)\n\nk0:deep",
            _output.ErrorText);

        _output.Errors.Clear();
        Assert.Equal(249, await Deployed(main, ["cause"]));
        Assert.Equal("Error using app (line 10)\nOuter failure\n\nCaused by:\n    Inner reason\n\nk0:outer", _output.ErrorText);
        Assert.DoesNotContain("jgraph:", _output.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Warnings_GoToStandardOutput()
    {
        int code = await Deployed(Main("warning('k1:careful', 'Careful with %s', 'that');"));

        Assert.Equal(0, code);
        Assert.Contains("Warning: Careful with that", _output.NormalText);
        Assert.Empty(_output.Errors);
    }

    // --- The frozen path and the current folder (K0: path) ---------------------------------------

    [Fact]
    public async Task Path_IsFrozenAndTheCurrentFolderHoldsDataNotCode()
    {
        Put(Run, "k1_outside.m", "function r = k1_outside()\nr = 'outside ran';\nend\n");
        Put(Run, "outside_script.m", "disp('outside script ran');\n");
        Put(Run, "outside.txt", "outside text\n");
        Put(Assets, "inside.txt", "inside text\n");
        Put(Component, "helper.m", "function r = helper()\nr = 'helper ran';\nend\n");
        string main = Main("""
            id = @(f) caught(f);
            fprintf('pwd=%d\n', strcmp(pwd, getenv('K1_RUN')));
            fprintf('exist.outside=%d which.outside=[%s] call=%s\n', exist('k1_outside'), which('k1_outside'), id(@() k1_outside()));
            fprintf('exist.data=%d which.data=[%s] read=%s\n', exist('outside.txt', 'file'), which('outside.txt'), strtrim(fileread('outside.txt')));
            fprintf('inside=%s which.inside=%d\n', strtrim(fileread('inside.txt')), endsWith(which('inside.txt'), fullfile('assets', 'inside.txt')));
            fprintf('helper=%s\n', helper());
            here = pwd;
            fprintf('addpath=%s\n', id(@() addpath(here)));
            fprintf('restoredefaultpath=%s\n', id(@() restoredefaultpath));
            p = path;
            parts = strsplit(p, pathsep);
            fprintf('rmpath.first=%s\n', id(@() rmpath(parts{1})));
            lastwarn('');
            rmpath(here);
            [msg, wid] = lastwarn;
            fprintf('rmpath.absent=%s|%s\n', wid, strrep(msg, here, '<RUN>'));
            path(p);
            fprintf('path.same=ok\n');
            fprintf('path.set=%s\n', id(@() path(p, tempdir)));
            fprintf('run=%s\n', id(@() run('outside_script.m')));
            cd(tempdir);
            t = tempdir;
            fprintf('cd=%d\n', strcmp(pwd, t(1:end-1)));
            cd(here);
            end

            function s = caught(f)
            try
                f();
                s = 'ok';
            catch e
                s = e.identifier;
            end
            """);
        Environment.SetEnvironmentVariable("K1_RUN", Run);
        try
        {
            int code = await Deployed(main);
            Assert.True(code == 0, _output.ErrorText);
        }
        finally
        {
            Environment.SetEnvironmentVariable("K1_RUN", null);
        }

        Assert.Equal(
            [
                "pwd=1",
                "exist.outside=0 which.outside=[] call=MATLAB:UndefinedFunction",
                "exist.data=2 which.data=[] read=outside text",
                "inside=inside text which.inside=1",
                "helper=helper ran",
                "addpath=MATLAB:mpath:PathAlterationNotSupported",
                "restoredefaultpath=MATLAB:mpath:PathAlterationNotSupported",
                "rmpath.first=MATLAB:mpath:PathAlterationNotSupported",
                "rmpath.absent=MATLAB:rmpath:DirNotFound|\"<RUN>\" not found in path.",
                "path.same=ok",
                "path.set=MATLAB:mpath:PathAlterationNotSupported",
                "run=MATLAB:run:FileNotFound",
                "cd=1",
            ],
            _output.NormalLines.Where(static line => !line.StartsWith("Warning:", StringComparison.Ordinal)));
    }

    // --- What the runtime leaves out or answers for itself (K0: env_plain, stubs) ----------------

    [Fact]
    public async Task Runtime_LeavesOutWhatR2025bsDoesNotShipAndRefusesTheDebugger()
    {
        string main = Main("""
            id = @(f) caught(f);
            fprintf('help=%s lookfor=%s\n', id(@() help('sin')), id(@() lookfor('sine')));
            fprintf('keyboard=%s dbstop=%s mex=%s\n', id(@() keyboard), id(@() dbstop('in', 'app')), id(@() mex('-help')));
            fprintf('clc=[%s] home=[%s]\n', evalc('clc'), evalc('home'));
            fprintf('license=%s test=%d\n', license, license('test', 'MATLAB'));
            fprintf('ctfroot=%d\n', strcmp(ctfroot, getenv('K1_CTF')));
            fprintf('deployed=%d mcc=%d\n', isdeployed, ismcc);
            f = figure('Visible', 'off');
            names = waitforallfiguresclosed();
            fprintf('wait=%s %d\n', class(names), numel(names));
            close(f);
            end

            function s = caught(f)
            try
                f();
                s = 'ok';
            catch e
                s = e.identifier;
            end
            """);
        Environment.SetEnvironmentVariable("K1_CTF", Ctf);
        try
        {
            Assert.Equal(0, await Deployed(main));
        }
        finally
        {
            Environment.SetEnvironmentVariable("K1_CTF", null);
        }

        Assert.Equal(
            [
                "help=MATLAB:UndefinedFunction lookfor=MATLAB:UndefinedFunction",
                "keyboard=MATLAB:services:MissingRequiredCapability dbstop=MATLAB:services:MissingRequiredCapability mex=MATLAB:mex:DEPLOYMENT_MODE_NOTSUPPORTED",
                "clc=[] home=[]",
                "license=unknown test=1",
                "ctfroot=1",
                "deployed=1 mcc=0",
                "wait=cell 1",
            ],
            _output.NormalLines);
    }

    [Fact]
    public async Task InputAndPause_ReadAClosedStandardInput()
    {
        string main = Main("""
            n = input('k1 number? ');
            fprintf('|%s %s\n', class(n), mat2str(size(n)));
            s = input('k1 text? ', 's');
            fprintf('|%s %s\n', class(s), mat2str(size(s)));
            pause;
            fprintf('|after pause\n');
            """);
        TextReader saved = Console.In;
        Console.SetIn(new StringReader(string.Empty));
        try
        {
            Assert.Equal(0, await Deployed(main));
        }
        finally
        {
            Console.SetIn(saved);
        }

        Assert.Equal("k1 number? |double [0 0]\nk1 text? |char [0 0]\nPaused: Press any key and return|after pause\n", _output.NormalText);
    }

    [Fact]
    public async Task UserData_ComesFromTheCommandLine()
    {
        string main = Main("""
            fprintf('nargin=%d %s\n', nargin, varargin{1});
            fprintf('profile=%s\n', getmcruserdata('ParallelProfile'));
            """);

        Assert.Equal(0, await Deployed(main, ["-mcruserdata", "ParallelProfile:k0.mlsettings", "after"]));
        Assert.Equal(["nargin=1 after", "profile=k0.mlsettings"], _output.NormalLines);
    }

    [Fact]
    public async Task Startup_ThatTheProgramWasBuiltWithRunsBeforeItsMain()
    {
        Put(Component, "startup.m", "setappdata(groot, 'k1_startup_ran', true);\nfprintf('startup ran (isdeployed=%d)\\n', isdeployed);\n");
        string main = Main("fprintf('main sees %d\\n', getappdata(groot, 'k1_startup_ran'));");

        Assert.Equal(0, await Deployed(main));
        Assert.Equal(["startup ran (isdeployed=1)", "main sees 1"], _output.NormalLines);
    }

    [Fact]
    public async Task OrdinaryBatchRun_IsNotDeployed()
    {
        string main = Main("fprintf('deployed=%d\\n', isdeployed);");
        StartupOptions options = StartupCommandLine.Parse(["-batch", main]);

        Assert.Equal(0, await BatchRunner.RunAsync(options, Engines, _output, static (_, _) => { }));
        Assert.Equal(["deployed=0"], _output.NormalLines);
    }
}
