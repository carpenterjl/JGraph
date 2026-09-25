using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V11 of the value-ownership plan (ADR 0172): code carries its dialect. A function, an anonymous
/// function, a script and a class method run in the dialect of the file they were parsed from,
/// whoever calls them, and a built-in reads that dialect at the call. The MATLAB half of every
/// expectation here is what <c>dialect_crossing_matlab.m</c> measured in R2025b; the JGS half is
/// JGS's own frozen meaning.
/// </summary>
[Collection("JG facade")]
public class DialectCrossingM172Tests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "jgraph-v11-crossing-" + Guid.NewGuid().ToString("N"));

    public DialectCrossingM172Tests()
    {
        JG.Reset();
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        JG.Reset();
        Directory.Delete(_folder, recursive: true);
    }

    private string WriteFile(string name, string source)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, source);
        return path;
    }

    private ScriptRunResult Run(string code, JgsDialect dialect)
    {
        string main = Path.Combine(_folder, dialect.IsMatlab ? "main.m" : "main.jgs");
        var context = new ScriptContext(_output, (_, _) => { }, _folder, resolvePath: null, figureFiles: new TestFigureFiles())
        {
            ScriptPath = main,
        };
        return JgsRunner.Run(code, context, default, sourceId: main, hook: null, dialect);
    }

    private string RunJgs(string code)
    {
        ScriptRunResult result = Run(code, JgsDialect.Jgs);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim().Replace("\r\n", "\n");
    }

    private string RunMatlab(string code)
    {
        ScriptRunResult result = Run(code, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim().Replace("\r\n", "\n");
    }

    // --- a MATLAB function file called from JGS (#131) ------------------------------------------

    [Fact]
    public void AMatlabFunctionFileCalledFromJgsIndexesOneBasedAndConcatenatesItsBrackets()
    {
        WriteFile("m_first.m", "function y = m_first(x)\ny = x(1);\nend\n");
        WriteFile("m_concat.m", "function y = m_concat(a, b)\ny = [a, b];\nend\n");
        Assert.Equal("1\n4", RunJgs("""
            print(m_first([1, 2, 3]))
            print(length(m_concat([1, 2], [3, 4])))
            """));
    }

    [Fact]
    public void AMatlabFunctionsParameterIsAValueWhenAJgsCallerHandsItsReferenceOver()
    {
        WriteFile("m_write_param.m", "function y = m_write_param(x)\nx(1) = 7;\ny = x;\nend\n");
        Assert.Equal("[7, 2, 3]\n[1, 2, 3]", RunJgs("""
            let a = [1, 2, 3];
            let r = m_write_param(a);
            print(r)
            print(a)
            """));
    }

    [Fact]
    public void AMatlabPersistentHandedToJgsKeepsItsSlotWhenTheCopyIsWritten()
    {
        WriteFile("m_persist.m", "function y = m_persist()\npersistent p\nif isempty(p)\n    p = [1 2 3];\nend\ny = p;\nend\n");
        Assert.Equal("[99, 2, 3]\n[1, 2, 3]", RunJgs("""
            let p = m_persist();
            p[0] = 99;
            print(p)
            print(m_persist())
            """));
    }

    [Fact]
    public void AJgsBuiltinIsNeverShadowedByAFileBesideTheScript()
    {
        // The folders come after the walk for JGS: a print.m beside the script is never reached for
        // a name the built-in layer answers - JGS's print stays the console verb - where MATLAB's
        // order asks the folders before the built-in layer.
        WriteFile("print.m", "function print(varargin)\ndisp('shadowed');\nend\n");
        Assert.Equal("3", RunJgs("print(length([1, 2, 3]))"));
    }

    [Fact]
    public void ANameNoFileAnswersIsStillJgssUndefinedRefusal()
    {
        ScriptRunResult result = Run("print(nosuch(1))", JgsDialect.Jgs);
        Assert.False(result.Success);
        Assert.Contains("'nosuch' is not defined.", result.Message);
    }

    // --- a MATLAB script run from JGS (#130, #142, #143) ---------------------------------------

    [Fact]
    public void AnAnonymousHandleMadeByAMatlabScriptKeepsMatlabsMeaningWhenJgsCallsIt()
    {
        WriteFile("m_make_handles.m", "first = @(x) x(1);\ntwice = @(a) [a, a];\n");
        Assert.Equal("1\n4", RunJgs("""
            run("m_make_handles.m")
            print(first([1, 2, 3]))
            print(length(twice([1, 2])))
            """));
    }

    [Fact]
    public void AMatlabScriptsBuiltinsMeanMatlabWhenRunFromJgs()
    {
        // find is 1-based and find(m, 1) a result limit; fprintf's \n is a newline, not two characters.
        WriteFile("m_find.m", "m = [0 1 1];\nk = find(m);\nk1 = find(m, 1);\nfprintf('k=%s k1=%s\\n', mat2str(k), mat2str(k1));\nfprintf('next\\n');\n");
        Assert.Equal("k=[2 3] k1=2\nnext", RunJgs("run(\"m_find.m\")"));
    }

    [Fact]
    public void JgsKeepsItsOwnMeaningAfterTheCrossing()
    {
        WriteFile("m_find.m", "k = find([0 1 1]);\n");
        WriteFile("m_lexical.m", "function s = m_lexical()\ns = sprintf('%d,', [1 2 3]);\nend\n");
        Assert.Equal("[2, 3]\n1,2,3,\n[1, 2]\n[2, 3]\n1-2", RunJgs("""
            run("m_find.m")
            print(k)
            print(m_lexical())
            print(find([0, 1, 1]))
            print(find([0, 1, 1], 1))
            print(sprintf("%d-%d", 1, 2))
            """));
    }

    [Fact]
    public void AMatlabFunctionDetachingAStructsChildLeavesTheJgsCallersStructAndItsAliasWhole()
    {
        WriteFile("m_make_struct_alias.m", "q = struct('f', [1 2 3]);\nw = q;\n");
        WriteFile("m_write_field.m", "function y = m_write_field(s)\ns.f(1) = 9;\ny = s;\nend\n");
        WriteFile("m_show.m", "fprintf('%s %s %s\\n', mat2str(r.f), mat2str(q.f), mat2str(w.f));\n");
        Assert.Equal("[9 2 3] [1 2 3] [1 2 3]", RunJgs("""
            run("m_make_struct_alias.m")
            let r = m_write_field(q);
            run("m_show.m")
            """));
    }

    // --- JGS code called from MATLAB code ------------------------------------------------------

    [Fact]
    public void AJgsFunctionCalledFromAMatlabScriptRunsAsJgs()
    {
        WriteFile("caller.m", "r = f([10 20 30]);\nfprintf('%g\\n', r);\n");
        Assert.Equal("10", RunJgs("""
            fn f(x) { return x[0] }
            run("caller.m")
            """));
    }

    [Fact]
    public void AnErrorInsideACrossingCallRestoresTheCallersDialect()
    {
        // The JGS callee's error unwinds its frame and the .m script is MATLAB again at the catch;
        // the script's end puts JGS back for the caller.
        WriteFile("caller.m", "try\n    fails(1);\ncatch e\n    fprintf('%s\\n', class(e));\nend\nfprintf('%s\\n', mat2str(find([0 1 1])));\n");
        Assert.Equal("MException\n[2 3]\n[1, 2]", RunJgs("""
            fn fails(x) { return no_such_name + x }
            run("caller.m")
            print(find([0, 1, 1]))
            """));
    }

    [Fact]
    public void AMatlabFunctionThatCatchesItsOwnErrorIsStillMatlabAfterwards()
    {
        WriteFile("m_recover.m", "function k = m_recover()\ntry\n    error('x:y', 'boom');\ncatch\nend\nk = find([0 1 1]);\nend\n");
        Assert.Equal("[2, 3]", RunJgs("print(m_recover())"));
    }

    [Fact]
    public void ACompiledMatlabLoopRunsInsideAFunctionCalledFromJgs()
    {
        WriteFile("m_loop.m", "function s = m_loop(n)\ns = 0;\nfor i = 1:n\n    s = s + i;\nend\nend\n");
        Assert.Equal("500500", RunJgs("print(m_loop(1000))"));
    }

    // --- the built-ins that dispatch on the calling code (registration choices, now runtime) ----

    [Fact]
    public void ANameTheDialectsGiveDifferentMeaningsDispatchesOnTheCallingCode()
    {
        // range, slice, seconds, print, zeros and sum keep JGS's meaning for JGS code in the same
        // session in which a .m function gets MATLAB's.
        WriteFile("m_forms.m", """
            function m_forms()
            fprintf('%g %s %s %s %s\n', range([4 9 1]), class(seconds(5)), mat2str(size(zeros(3))), mat2str(sum([1 2; 3 4], 2)), mat2str(max([1 5 2], [], 2)));
            end
            """);
        Assert.Equal("8 duration [3 3] [3;7] 5\n[0, 1, 2]\n[2, 3]\n5\n[0, 0, 0]\n10\n5",
            RunJgs("""
                m_forms()
                print(range(0, 3))
                print(slice([1, 2, 3, 4], 1, 3))
                print(seconds(5))
                print(zeros(3))
                print(sum([1, 2, 3, 4]))
                print(max([1, 5, 2]))
                """));
    }

    [Fact]
    public void SprintfCyclesAndDecodesForMatlabCodeAndStaysStrictForJgs()
    {
        WriteFile("m_fmt.m", "function s = m_fmt()\ns = sprintf('%d,', [1 2 3]);\nend\n");
        Assert.Equal("1,2,3,\n1-2", RunJgs("""
            print(m_fmt())
            print(sprintf("%d-%d", 1, 2))
            """));
    }

    // --- the slot itself ---------------------------------------------------------------------

    [Fact]
    public void TheSessionDialectIsTheHostsAndTheRunningDialectTheCodes()
    {
        JgsEnvironment env = JgsBuiltins.CreateGlobals(new JGraphScriptGlobals(new ScriptContext(_output, static (_, _) => { })), default, JgsDialect.Jgs);
        var interpreter = new Interpreter(env, default, dialect: JgsDialect.Jgs);
        Assert.Same(JgsDialect.Jgs, interpreter.SessionDialect);
        Assert.Same(JgsDialect.Jgs, interpreter.Dialect);

        // A MATLAB-parsed program run over a JGS workspace runs as MATLAB: find is 1-based inside
        // it, and the session's dialect is back when it ends.
        interpreter.Run(Parser.Parse("k = find([0 1 1]);", "x.m", JgsDialect.Matlab));
        JgsValue k = env.Locals["k"];
        Assert.Equal(2, k.ArrayLength);
        Assert.Equal(2.0, k.ElementAt(0).AsNumber);
        Assert.Equal(3.0, k.ElementAt(1).AsNumber);
        Assert.Same(JgsDialect.Jgs, interpreter.Dialect);
    }

    [Fact]
    public void AWorkspaceBuiltForOneDialectRefusesAnInterpreterOfTheOther()
    {
        JgsEnvironment env = JgsBuiltins.CreateGlobals(new JGraphScriptGlobals(new ScriptContext(_output, static (_, _) => { })), default, JgsDialect.Jgs);
        Assert.Throws<InvalidOperationException>(() => new Interpreter(env, default, dialect: JgsDialect.Matlab));
    }
}
