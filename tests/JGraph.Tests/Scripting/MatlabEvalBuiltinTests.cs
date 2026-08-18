using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Evaluating text, asking about the workspace, and the file and machine builtins (M38). The file
/// tests run in a temporary folder of their own, so nothing here depends on where the suite was
/// started from or leaves anything behind.
/// </summary>
[Collection("JG facade")]
public class MatlabEvalBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _workingDirectory;

    public MatlabEvalBuiltinTests()
    {
        JG.Reset();
        _workingDirectory = Path.Combine(Path.GetTempPath(), "jgraph-env-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workingDirectory);
    }

    public void Dispose()
    {
        JG.Reset();
        try
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure)), _workingDirectory));

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    [Fact]
    public Task Eval_RunsTextInTheScopeThatCalledIt() => RunAsserting("""
        eval('x = 41 + 1;');
        assert(x == 42);
        assert(eval('x * 2') == 84);

        % The two-argument form is the pre-try/catch way of writing a recovery.
        eval('this is not code', 'y = 7;');
        assert(y == 7);
        """);

    [Fact]
    public Task Evalc_ReturnsWhatTheCodePrinted() => RunAsserting("""
        captured = evalc('disp(42)');
        assert(~isempty(strfind(captured, '42')));

        % Capturing must not swallow anything after it: this line still reaches the console.
        after = evalc('fprintf(''hi'')');
        assert(strcmp(after, 'hi'));
        """);

    [Fact]
    public Task EvalinAndAssignin_ReachTheBaseWorkspace() => RunAsserting("""
        assignin('base', 'planted', 99);
        assert(planted == 99);
        assert(evalin('base', 'planted + 1') == 100);
        """);

    [Fact]
    public Task Str2func_BuildsAHandleFromANameOrAnExpression() => RunAsserting("""
        f = str2func('@(x) x + 1');
        assert(f(1) == 2);

        s = str2func('sin');
        assert(abs(s(0)) < 1e-15);
        """);

    [Fact]
    public Task Exist_ReportsTheCategoryOfAName() => RunAsserting("""
        thing = 1;
        assert(exist('thing') == 1);
        assert(exist('thing', 'var') == 1);
        assert(exist('sin') == 5);
        assert(exist('no_such_name_anywhere') == 0);
        assert(exist('no_such_name_anywhere', 'var') == 0);
        """);

    [Fact]
    public Task Who_ListsTheVariablesAndNotTheBuiltins() => RunAsserting("""
        alpha = 1;
        beta = 2;
        names = who();
        assert(iscell(names));
        assert(any(strcmp(names, 'alpha')));
        assert(any(strcmp(names, 'beta')));
        assert(~any(strcmp(names, 'sin')));
        """);

    [Fact]
    public Task ArgumentChecks_OnlyMeanSomethingInsideAFunction() => RunAsserting("""
        threw = false;
        try
            takesTwo(1);
        catch
            threw = true;
        end
        assert(threw);

        takesTwo(1, 2);   % the right count passes silently

        function takesTwo(a, b)
            narginchk(2, 2);
        end
        """);

    [Fact]
    public Task ErrorHistory_RemembersWhatWasCaught() => RunAsserting("""
        try
            error('something went wrong');
        catch
        end
        assert(~isempty(strfind(lasterr(), 'something went wrong')));

        e = lasterror();
        assert(~isempty(strfind(e.message, 'something went wrong')));

        % rethrow raises the struct a catch handed over.
        again = false;
        try
            rethrow(e);
        catch
            again = true;
        end
        assert(again);
        """);

    [Fact]
    public Task PathHelpers_SplitAndJoinWithoutTouchingTheDisk() => RunAsserting("""
        joined = fullfile('one', 'two', 'three.txt');
        assert(~isempty(strfind(joined, 'three.txt')));

        parts = fileparts(joined);
        assert(strcmp(parts{2}, 'three'));
        assert(strcmp(parts{3}, '.txt'));
        assert(strlength(filesep()) == 1);
        assert(strcmp(filemarker(), '>'));
        """);

    [Fact]
    public Task FilesAndFolders_AreCreatedMovedAndRemoved() => RunAsserting("""
        assert(~isfolder('made'));
        mkdir('made');
        assert(isfolder('made'));
        assert(exist('made', 'dir') == 7);

        fid = fopen('note.txt', 'w');
        fprintf(fid, 'hello');
        fclose(fid);
        assert(isfile('note.txt'));

        copyfile('note.txt', 'copy.txt');
        assert(isfile('copy.txt'));
        movefile('copy.txt', 'moved.txt');
        assert(~isfile('copy.txt'));
        assert(isfile('moved.txt'));

        delete('moved.txt');
        assert(~isfile('moved.txt'));
        rmdir('made');
        assert(~isfolder('made'));
        """);

    [Fact]
    public Task Cd_MovesWhereRelativePathsResolve() => RunAsserting("""
        start = pwd();
        mkdir('subfolder');
        cd('subfolder');
        assert(~strcmp(pwd(), start));

        fid = fopen('inside.txt', 'w');
        fprintf(fid, 'x');
        fclose(fid);
        assert(isfile('inside.txt'));

        cd(start);
        assert(strcmp(pwd(), start));
        assert(~isfile('inside.txt'));
        assert(isfile(fullfile('subfolder', 'inside.txt')));
        """);

    [Fact]
    public Task StreamPositionHelpers_WalkAFileAndReportWhereTheyAre() => RunAsserting("""
        fid = fopen('lines.txt', 'w');
        fprintf(fid, 'first\nsecond\n');
        fclose(fid);

        fid = fopen('lines.txt', 'r');
        assert(ftell(fid) == 0);
        assert(~feof(fid));

        line = fgets(fid);
        assert(strcmp(line, sprintf('first\n')));   % fgets keeps the newline; fgetl drops it
        assert(ftell(fid) == 6);

        fseek(fid, 0, 'bof');
        assert(ftell(fid) == 0);
        assert(strcmp(fgetl(fid), 'first'));

        fseek(fid, 0, 'eof');
        assert(feof(fid));
        assert(isempty(ferror(fid)));
        fclose(fid);
        """);

    [Fact]
    public Task MachineQuestions_AnswerAboutThisComputer() => RunAsserting("""
        % Exactly one family is true, whichever machine this is.
        assert(ispc() + isunix() == 1);
        assert(namelengthmax() == 63);
        assert(cputime() >= 0);

        setenv('JGRAPH_TEST_VALUE', 'set');
        assert(strcmp(getenv('JGRAPH_TEST_VALUE'), 'set'));
        assert(isempty(getenv('JGRAPH_NO_SUCH_VARIABLE_ANYWHERE')));
        """);

    [Fact]
    public Task Json_RoundTripsThroughItsTextForm() => RunAsserting("""
        assert(strcmp(jsonencode(42), '42'));
        assert(strcmp(jsonencode('text'), '"text"'));
        assert(strcmp(jsonencode([1 2 3]), '[1,2,3]'));

        s.name = 'jgraph';
        s.count = 3;
        back = jsondecode(jsonencode(s));
        assert(strcmp(back.name, 'jgraph'));
        assert(back.count == 3);

        assert(isequal(jsondecode('[1,2,3]'), [1 2 3]));
        nested = jsondecode('{"a":{"b":7}}');
        assert(nested.a.b == 7);
        """);

    /// <summary>
    /// Both halves of this were missing until M69's syntax-form probe ran the documented forms
    /// rather than the documented name. The name form is the one MATLAB documents first, and the
    /// multi-output form used to answer one value in silence — a wrong answer, not an error.
    /// </summary>
    [Fact]
    public Task Feval_TakesAName_AndAnswersEveryOutputAskedFor() => RunAsserting("""
        assert(feval('sin', 0) == 0);
        assert(feval(@sin, 0) == 0);
        assert(feval('max', [3 9 4]) == 9);

        [rows, columns] = feval(@size, ones(2, 3));
        assert(rows == 2 && columns == 3);

        [value, where] = feval('max', [3 9 4]);
        assert(value == 9 && where == 2);

        % A name that is not a function, and a target that is not callable at all, each say which.
        caught = '';
        try
            feval('no_such_function_anywhere', 1);
        catch ME
            caught = ME.message;
        end
        assert(contains(caught, 'no_such_function_anywhere'));

        caught = '';
        try
            feval(42);
        catch ME
            caught = ME.message;
        end
        assert(contains(caught, 'function handle or a function name'));
        """);

    /// <summary>
    /// A <c>global</c> declaration belongs to the workspace that wrote it. Until M69 one run-wide set
    /// held every declared name, so the first function to say <c>global counter</c> rewired that name
    /// in every other scope for the rest of the session — silently, and with no test to notice.
    /// </summary>
    [Fact]
    public Task Global_BindsOnlyTheWorkspaceThatDeclaredIt() => RunAsserting("""
        counter = 1;
        bump();
        assert(counter == 1);      % the local is untouched by the helper's declaration
        assert(readGlobal() == 100);

        % The declaring script shares the same variable, and shares it both ways.
        global shared
        shared = 5;
        assert(readShared() == 5);
        writeShared(9);
        assert(shared == 9);

        % A global nobody has assigned reads as [], which is MATLAB's answer too.
        assert(isempty(readUnset()));

        function bump()
            global counter
            counter = 100;
        end

        function v = readGlobal()
            global counter
            v = counter;
        end

        function v = readShared()
            global shared
            v = shared;
        end

        function writeShared(v)
            global shared
            shared = v;
        end

        function v = readUnset()
            global never_assigned_anywhere
            v = never_assigned_anywhere;
        end
        """);

    /// <summary>
    /// <c>evalin</c> and <c>assignin</c> with <c>'caller'</c> reach the workspace that called the
    /// function asking, which needs one frame of call history the interpreter did not keep before M69.
    /// It used to resolve to the current frame, so a function read and wrote its own variables while
    /// appearing to read and write its caller's.
    /// </summary>
    [Fact]
    public Task EvalinAndAssignin_ReachTheCallersWorkspace() => RunAsserting("""
        secret = 42;
        helper();
        assert(planted == 7);

        % At the top level nothing called us, so 'caller' is the base workspace.
        assert(evalin('caller', 'secret') == 42);

        function helper()
            secret = 999;                              % the helper's own, deliberately different
            assert(evalin('caller', 'secret') == 42);
            assert(evalin('base', 'secret') == 42);
            assignin('caller', 'planted', 7);
            assert(secret == 999);                     % assignin did not touch this frame
        end
        """);
}
