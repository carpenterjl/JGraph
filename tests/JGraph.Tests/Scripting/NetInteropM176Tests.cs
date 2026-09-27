using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Stage 3 of the .NET and shared-library interop plan (ADR 0176): assemblies and <c>import</c> —
/// the statement and where it applies, the precedence of an imported name, the function form and
/// <c>clear import</c>, one <c>NET.Assembly</c> per assembly, the runtime's status, and the process's
/// working folder following <c>cd</c>. The R2025b answers are the net_import and net_assembly
/// fixtures' and probe3's; these tests pin the machinery under them.
/// </summary>
[Collection("JG facade")]
public class NetInteropM176Tests : IDisposable
{
    private static readonly string Assembly = Path.Combine(
        AppContext.BaseDirectory, "MatlabParity", "fixtures", "interop", "JGraph.Interop.TestAssembly.dll");

    private RecordingScriptOutput _output = new();

    public NetInteropM176Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string Run(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim().ReplaceLineEndings("\n");
    }

    private static string Load => $"NET.addAssembly('{Assembly}');\n";

    // --- the statement -------------------------------------------------------------------------

    [Fact]
    public void AnImportStatementTakesSeveralNamesAndAWildcard()
    {
        IReadOnlyList<Stmt> program = Parser.Parse("import System.IO.* System.Math.Max\nx = 2 .* 3;", "", JgsDialect.Matlab);
        ImportStmt import = Assert.IsType<ImportStmt>(program[0]);
        Assert.Equal(["System.IO.*", "System.Math.Max"], import.Names);
        Assert.IsNotType<ImportStmt>(program[1]); // the elementwise product is untouched
    }

    [Fact]
    public void AFunctionsImportsHoldBeforeTheirLineAndInABranchNeverTaken()
    {
        Assert.Equal("5|5", Run(
            "fprintf('%d|%d', early(), branch(false));\n"
            + "function v = early()\nv = Max(4, 5);\nimport System.Math.*\nend\n"
            + "function v = branch(flag)\nif flag\nimport System.Math.*\nend\nv = Max(4, 5);\nend\n"));
    }

    [Fact]
    public void AnImportReachesNestedAndAnonymousFunctionsAndEvalButNotACallee()
    {
        Assert.Equal("5|5|2|MATLAB:UndefinedFunction", Run(
            "[a, b, c, d] = outer(); fprintf('%d|%d|%d|%s', a, b, c, d);\n"
            + "function [a, b, c, d] = outer()\nimport System.Math.*\na = inner();\nf = @() Max(4, 5); b = f();\n"
            + "c = eval('Max(1, 2)');\ntry, callee(); d = 'none'; catch e, d = e.identifier; end\n"
            + "    function v = inner()\n    v = Max(4, 5);\n    end\nend\n"
            + "function v = callee()\nv = Max(4, 5);\nend\n"));
    }

    [Fact]
    public void AnExplicitImportBeatsALocalFunctionAndAWildcardDoesNot()
    {
        Assert.Equal("5|-1", Run(
            "fprintf('%d|%d', viaExplicit(), viaWildcard());\n"
            + "function v = viaExplicit()\nimport System.Math.Max\nv = Max(4, 5);\nend\n"
            + "function v = viaWildcard()\nimport System.Math.*\nv = Max(4, 5);\nend\n"
            + "function v = Max(a, b)\nv = -1;\nend\n"));
    }

    [Fact]
    public void AWildcardReachesTypesAndNamespacesButANamespaceIsNoFunction()
    {
        Assert.Equal("System.Text.StringBuilder|System.String|3", Run(
            "[a, b, c] = f(); fprintf('%s|%s|%d', a, b, c);\n"
            + "function [a, b, c] = f()\nimport System.*\na = class(Text.StringBuilder('x'));\n"
            + "b = class(IO.Path.GetTempPath());\nc = max([1 3]);\nend\n"));
    }

    [Fact]
    public void BetweenTwoWildcardsTheLaterWins()
    {
        Assert.Equal("MATLAB:dispatcher:noMatchingConstructor|System.Timers.*,System.Threading.*", Run(
            "[a, b] = f(); fprintf('%s|%s', a, strjoin(b', ','));\n"
            + "function [a, b] = f()\nimport System.Timers.*\nimport System.Threading.*\n"
            + "try, t = Timer(100); a = 'none'; catch e, a = e.identifier; end\nb = import;\nend\n"));
    }

    [Fact]
    public void TheFunctionFormListsButDoesNotResolveInAFunction()
    {
        Assert.Equal("System.Math,System.IO.*|MATLAB:undefinedVarOrClass|2", Run(
            "[L, id] = f(); import('System.Math.*'); fprintf('%s|%s|%d', strjoin(L', ','), id, Max(1, 2));\n"
            + "function [L, id] = f()\nimport System.IO.*\nL = import('System.Math');\n"
            + "try, Math.Max(1, 2); id = 'none'; catch e, id = e.identifier; end\nend\n"));
    }

    [Fact]
    public void AnImportIsRefusedWhenItsFunctionIsEntered()
    {
        Assert.Equal("MATLAB:mir_illegal_import_argument|MATLAB:mir_illegal_import_argument|MATLAB:lang:ImportedFunctionAndVariableHaveSameName|MATLAB:cannotClear|MATLAB:import:NonFullyQualifiedImportArgument|MATLAB:import:InvalidInputDataType", Run(
            "ids = {};\n"
            + "try, badType(); catch e, ids{end+1} = e.identifier; end\n"
            + "try, aProperty(); catch e, ids{end+1} = e.identifier; end\n"
            + "try, clash(3); catch e, ids{end+1} = e.identifier; end\n"
            + "try, clearing(); catch e, ids{end+1} = e.identifier; end\n"
            + "try, import('System.NoSuch'); catch e, ids{end+1} = e.identifier; end\n"
            + "try, import(5); catch e, ids{end+1} = e.identifier; end\n"
            + "fprintf('%s', strjoin(ids, '|'));\n"
            + "function v = badType()\nimport System.NoSuchType\nv = 1;\nend\n"
            + "function v = aProperty()\nimport System.Math.PI\nv = 1;\nend\n"
            + "function v = clash(Math)\nimport System.Math\nv = Math;\nend\n"
            + "function v = clearing()\nimport System.Math.*\nclear import\nv = 1;\nend\n"));
    }

    [Fact]
    public void AnImportedTypeBeatsABuiltinOfItsName()
    {
        Assert.Equal("JGTest.plot|3|JGTest.max.Thing", Run(Load
            + "[a, b, c] = f(); fprintf('%s|%d|%s', a, b, c);\n"
            + "function [a, b, c] = f()\nimport JGTest.*\na = class(plot);\nb = sum([1 2]);\nc = char(max.Thing().Where());\nend\n"));
    }

    [Fact]
    public void WhichNamesAnImportedMethod()
    {
        Assert.Equal("Max is a built-in method|0", Run(
            "[w, x] = f(); fprintf('%s|%d', w, x);\n"
            + "function [w, x] = f()\nimport System.Math.*\nw = which('Max');\nx = exist('Max');\nend\n"));
    }

    // --- assemblies and the runtime ------------------------------------------------------------

    [Fact]
    public void AddingAnAssemblyAgainAnswersTheSameHandle()
    {
        Assert.Equal("1|1|1|NET.Assembly", Run(Load
            + $"a = NET.addAssembly('{Assembly}'); b = NET.addAssembly(string('{Assembly}'));\n"
            + "x = NET.addAssembly('System.Xml'); y = NET.addAssembly(System.Reflection.AssemblyName('System.Xml'));\n"
            + "z = feval('NET.addAssembly', 'System.Xml');\n"
            + "fprintf('%d|%d|%d|%s', a == b, x == y, x == z, class(y));"));
    }

    [Fact]
    public void AnAssemblyListsItsNestedClassesLast()
    {
        Assert.Equal("24|JGTest.ArrayMaker|JGTest.Outer+Inner", Run(
            $"a = NET.addAssembly('{Assembly}'); c = a.Classes; fprintf('%d|%s|%s', numel(c), c{{1}}, c{{end}});"));
    }

    [Fact]
    public void AddAssemblyRefusesAsRecorded()
    {
        Assert.Equal("MATLAB:NET:AddAssembly:EmptyAssemblyName|MATLAB:UndefinedFunction|No method 'NET.addAssembly' with matching signature found.|MATLAB:badargs|MATLAB:class:RequireClass", Run(Load
            + "ids = {};\n"
            + "try, NET.addAssembly(''); catch e, ids{end+1} = e.identifier; end\n"
            + "try, NET.addAssembly('a', 1); catch e, ids{end+1} = e.identifier; ids{end+1} = e.message; end\n"
            + "try, NET.disableAutoRelease(JGTest.Members()); catch e, ids{end+1} = e.identifier; end\n"
            + "try, NET.enableAutoRelease(5); catch e, ids{end+1} = e.identifier; end\n"
            + "fprintf('%s', strjoin(ids, '|'));"));
    }

    [Fact]
    public void TheRuntimeIsNotLoadedUntilDotNetIsReached()
    {
        Assert.Equal("notloaded||notloaded|loaded", Run(
            "a = char(dotnetenv().Status); v = char(dotnetenv().Version); dotnetenv('core', Version='8');\n"
            + "b = char(dotnetenv().Status); NET.isNETSupported; c = char(dotnetenv().Status);\n"
            + "fprintf('%s|%s|%s|%s', a, v, b, c);"));
    }

    [Fact]
    public void WhichAndExistKnowTheNetPackage()
    {
        Assert.Equal("addAssembly is a built-in method|Max is a built-in method|8|8|0", Run(
            "fprintf('%s|%s|%d|%d|%d', which('NET.addAssembly'), which('System.Math.Max'), "
            + "exist('NET.NetException', 'class'), exist('NET.Assembly'), exist('NET.addAssembly'));"));
    }

    [Fact]
    public void ADotNetCallSeesTheFolderCdMovedTo()
    {
        string before = Environment.CurrentDirectory;
        try
        {
            Assert.Equal("1|1", Run(
                "here = pwd; cd(tempdir); a = strcmp(char(System.Environment.CurrentDirectory), pwd);\n"
                + "p = pwd; b = p(end) ~= filesep; cd(here); System.Environment.CurrentDirectory; fprintf('%d|%d', a, b);"));
        }
        finally
        {
            Environment.CurrentDirectory = before;
        }
    }
}
