using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.MatFile;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V6 (ADR 0167), thirteenth sub-stage: save and load fidelity (appendix A #110-#113, #146).
/// <c>save -struct</c>; an instance of a user class saved and loaded (a loaded handle is a new
/// instance, two names over one handle load as one, no constructor runs); a function handle saved
/// with what it captured; and <c>matfile</c>, whose variables are read from the file at every
/// mention and written back at every set.
/// </summary>
/// <remarks>
/// The parity fixture <c>save_load_roundtrip</c> holds R2025b's answers for the forms; these pin
/// the roads the sub-stage touched, one assertion a road, and the places this build has its own
/// answer: a v7.3 file is read and never written, and <c>-v7.3</c> stays refused.
/// </remarks>
[Collection("JG facade")]
public class SaveLoadM167Tests : IDisposable
{
    /// <summary>The parity fixtures' helpers folder: <c>vlog</c>, <c>HandleHolder</c>, <c>ValueBox</c>, <c>CtorLog</c>.</summary>
    private static readonly string Helpers = Path.Combine(AppContext.BaseDirectory, "MatlabParity", "fixtures", "helpers");

    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgraph-saveload-" + Guid.NewGuid().ToString("N"));

    public SaveLoadM167Tests()
    {
        JG.Reset();
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        JG.Reset();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private ScriptRunResult Run(string code)
    {
        var context = new ScriptContext(_output, (_, _) => { }, _folder, resolvePath: null, figureFiles: new TestFigureFiles());
        return JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab, searchFolders: [Helpers, _folder]);
    }

    private string RunAndRead(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim().Replace("\r\n", "\n");
    }

    private string RunExpectingError(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.False(result.Success);
        return result.Message ?? "";
    }

    /// <summary>A file name inside the test's own folder, as a MATLAB char literal.</summary>
    private string File(string name) => "'" + Path.Combine(_folder, name).Replace("'", "''") + "'";

    // --- save -struct (#110) ------------------------------------------------------------------------

    [Fact]
    public void SaveStructWritesTheFieldsAsTheVariables()
    {
        Assert.Equal("a,b [1 2] x", RunAndRead($$"""
            st.a = [1 2]; st.b = 'x';
            save({{File("s.mat")}}, '-struct', 'st');
            S = load({{File("s.mat")}});
            fprintf('%s %s %s\n', strjoin(fieldnames(S)', ','), mat2str(S.a), S.b);
            """));
    }

    [Fact]
    public void SaveStructTakesTheFieldsNamedAfterIt()
    {
        Assert.Equal("a,c", RunAndRead($$"""
            st.a = 1; st.b = 2; st.c = 3;
            save({{File("s.mat")}}, '-struct', 'st', 'a', 'c');
            S = load({{File("s.mat")}});
            disp(strjoin(fieldnames(S)', ','));
            """));
    }

    [Fact]
    public void SaveStructAppendsIntoAnExistingFile()
    {
        Assert.Equal("a,b,v", RunAndRead($$"""
            v = 5; save({{File("s.mat")}}, 'v');
            st.a = 1; st.b = 2;
            save({{File("s.mat")}}, '-struct', 'st', '-append');
            S = load({{File("s.mat")}});
            disp(strjoin(sort(fieldnames(S))', ','));
            """));
    }

    [Theory]
    [InlineData("st = 5;", "'-struct', 'st'", "The argument to -STRUCT must be the name of a scalar structure variable.")]
    [InlineData("st = struct('a', {1, 2});", "'-struct', 'st'", "The argument to -STRUCT must be the name of a scalar structure variable.")]
    [InlineData("st.a = 1;", "'-struct', 'nothere'", "The argument to -STRUCT must be the name of a scalar structure variable.")]
    [InlineData("st.a = 1;", "'-struct', 'st', 'zz'", "The variable 'st' does not contain a field named 'zz'.")]
    [InlineData("st.a = 1;", "'-struct'", "The -STRUCT option must be followed by the name of a scalar structure variable.")]
    [InlineData("st.a = 1;", "'st', '-struct'", "The -STRUCT option must be followed by the name of a scalar structure variable.")]
    public void SaveStructRefusesInMatlabsWords(string setup, string arguments, string expected)
    {
        Assert.Contains(expected, RunExpectingError($"{setup} save({File("s.mat")}, {arguments});"));
    }

    [Fact]
    public void SaveInsideAFunctionLeavesNarginAndNargoutOut()
    {
        Assert.Equal("fn,h,v", RunAndRead($$"""
            function go(fn)
                h = HandleHolder(); v = 1;
                save(fn);
                S = load(fn);
                disp(strjoin(sort(fieldnames(S))', ','));
            end
            go({{File("w.mat")}});
            """));
    }

    // --- objects (#111) ------------------------------------------------------------------------------

    [Fact]
    public void ALoadedHandleIsANewInstance()
    {
        Assert.Equal("[1 2] [7 2] 0", RunAndRead($$"""
            h = HandleHolder(); h.data = [1 2]; g = h;
            save({{File("h.mat")}}, 'h');
            S = load({{File("h.mat")}});
            S.h.data(1) = 7;
            fprintf('%s %s %d\n', mat2str(g.data), mat2str(S.h.data), S.h == g);
            """));
    }

    [Fact]
    public void TwoNamesOverOneHandleLoadAsOneInstance()
    {
        Assert.Equal("5 1", RunAndRead($$"""
            h = HandleHolder(); h.data = 1; g = h;
            save({{File("h.mat")}}, 'h', 'g');
            S = load({{File("h.mat")}});
            S.h.data = 5;
            fprintf('%g %d\n', S.g.data, S.g == S.h);
            """));
    }

    [Fact]
    public void AValueObjectRoundTripsAsItsClass()
    {
        Assert.Equal("ValueBox 3 4", RunAndRead($$"""
            o = ValueBox(); o.p = 3;
            save({{File("o.mat")}}, 'o');
            o.p = 4; q = o;
            S = load({{File("o.mat")}});
            fprintf('%s %g %g\n', class(S.o), S.o.p, q.p);
            """));
    }

    [Fact]
    public void AHandleInsideAStructAndAtTopLevelLoadAsOne()
    {
        Assert.Equal("1 9", RunAndRead($$"""
            h = HandleHolder(); h.data = 1; st.h = h;
            save({{File("h.mat")}}, 'h', 'st');
            S = load({{File("h.mat")}});
            S.st.h.data = 9;
            fprintf('%d %g\n', S.st.h == S.h, S.h.data);
            """));
    }

    [Fact]
    public void AHandleWhosePropertyHoldsItselfRoundTrips()
    {
        // The cycle: the instance is made before its properties are read, and the second mention
        // carries only the element id.
        Assert.Equal("1 1", RunAndRead($$"""
            h = HandleHolder(); h.data = h;
            save({{File("h.mat")}}, 'h');
            S = load({{File("h.mat")}});
            fprintf('%d %d\n', S.h.data == S.h, S.h.data.data == S.h);
            """));
    }

    [Fact]
    public void ADeletedHandleLoadsDeleted()
    {
        Assert.Equal("HandleHolder 0", RunAndRead($$"""
            h = HandleHolder(); delete(h);
            save({{File("h.mat")}}, 'h');
            S = load({{File("h.mat")}});
            fprintf('%s %d\n', class(S.h), isvalid(S.h));
            """));
    }

    [Fact]
    public void LoadRunsNoConstructor()
    {
        Assert.Equal("ctor;saved; 4", RunAndRead($$"""
            global vlog_text; vlog_text = '';
            c = CtorLog(); c.n = 4;
            save({{File("c.mat")}}, 'c');
            vlog('saved');
            S = load({{File("c.mat")}});
            fprintf('%s %g\n', vlog_text, S.c.n);
            """));
    }

    [Fact]
    public void ALoadedObjectAnswersItsClassAndItsMethods()
    {
        Assert.Equal("HandleHolder 1 1 1 [7 2]", RunAndRead($$"""
            h = HandleHolder(); h.data = [1 2];
            save({{File("h.mat")}}, 'h');
            S = load({{File("h.mat")}});
            S.h.bump();
            fprintf('%s %d %d %d %s\n', class(S.h), isa(S.h, 'handle'), isobject(S.h), isvalid(S.h), mat2str(S.h.data));
            """));
    }

    [Fact]
    public void ALoadedPropertyIsCheckedAgainstItsDeclaration()
    {
        Directory.CreateDirectory(_folder);
        System.IO.File.WriteAllText(Path.Combine(_folder, "Sized.m"), """
            classdef Sized
                properties
                    p (1, 1) double = 0
                end
            end
            """);
        Assert.Equal("2", RunAndRead($$"""
            o = Sized(); o.p = 2;
            save({{File("o.mat")}}, 'o');
            S = load({{File("o.mat")}});
            disp(S.o.p);
            """));
    }

    [Fact]
    public void AClassThatIsGoneWarnsAndLoadsAUint32()
    {
        System.IO.File.WriteAllText(Path.Combine(_folder, "GoneBox.m"), "classdef GoneBox\n properties\n q = 2\n end\nend\n");
        RunAndRead($"g = GoneBox(); g.q = 7; save({File("g.mat")}, 'g');");
        System.IO.File.Delete(Path.Combine(_folder, "GoneBox.m"));
        JG.Reset();
        Assert.Equal(
            "uint32 / Variable 'g' originally saved as a GoneBox cannot be instantiated as an object and will be read in as a uint32. / MATLAB:load:cannotInstantiateLoadedVariable",
            RunAndRead($$"""
                lastwarn('');
                S = load({{File("g.mat")}});
                [msg, id] = lastwarn();
                fprintf('%s / %s / %s\n', class(S.g), msg, id);
                """));
    }

    // --- function handles (#113) ---------------------------------------------------------------------

    [Fact]
    public void AnAnonymousHandleAnswersWhatItCaptured()
    {
        Assert.Equal("[1 2 3] function_handle", RunAndRead($$"""
            v = [1 2 3]; f = @() v;
            save({{File("f.mat")}}, 'f');
            v(1) = 7;
            S = load({{File("f.mat")}});
            fprintf('%s %s\n', mat2str(S.f()), class(S.f));
            """));
    }

    [Fact]
    public void ANamedHandleIsReMadeWhereItIsLoaded()
    {
        Assert.Equal("0 8", RunAndRead($$"""
            function z = twice(x)
                z = 2 * x;
            end
            f = @sin; g = @twice;
            save({{File("f.mat")}}, 'f', 'g');
            S = load({{File("f.mat")}});
            fprintf('%g %g\n', S.f(0), S.g(4));
            """));
    }

    [Fact]
    public void ACapturedHandleObjectLoadsAsAFreshInstance()
    {
        Assert.Equal("1 9", RunAndRead($$"""
            h = HandleHolder(); h.data = 1;
            f = @() h.data; f2 = f;
            save({{File("f.mat")}}, 'f');
            h.data = 9;
            S = load({{File("f.mat")}});
            fprintf('%g %g\n', S.f(), f2());
            """));
    }

    [Fact]
    public void AHandleThatCapturedAnotherHandleRoundTrips()
    {
        Assert.Equal("7 12", RunAndRead($$"""
            g = @(y) y * 2; f = @(x) g(x) + 1; c = {@(a, b) a * b};
            save({{File("f.mat")}}, 'f', 'c');
            S = load({{File("f.mat")}});
            fprintf('%g %g\n', S.f(3), S.c{1}(3, 4));
            """));
    }

    // --- matfile (#112) ------------------------------------------------------------------------------

    [Fact]
    public void AMatfileReadIsFreshEveryTime()
    {
        Assert.Equal("[1 2 3] [1 2 3] [9 9]", RunAndRead($$"""
            v = [1 2 3]; save({{File("m.mat")}}, 'v');
            m = matfile({{File("m.mat")}});
            x = m.v; x(1) = 7; a = m.v;
            v = [9 9]; save({{File("m.mat")}}, 'v');
            fprintf('%s %s %s\n', mat2str(x - [6 0 0]), mat2str(a), mat2str(m.v));
            """));
    }

    [Fact]
    public void AMatfileWriteGoesIntoTheFileIndexedWholeOrNew()
    {
        Assert.Equal("[1 8 3 0 1] [4 5 6] 5", RunAndRead($$"""
            v = [1 2 3]; save({{File("m.mat")}}, 'v');
            m = matfile({{File("m.mat")}}, 'Writable', true);
            m.v(1, 2) = 8; m.v(1, 5) = 1; a = m.v;
            m.v = [4 5 6]; m.w = 5;
            S = load({{File("m.mat")}});
            fprintf('%s %s %g\n', mat2str(a), mat2str(S.v), S.w);
            """));
    }

    [Fact]
    public void AReadOnlyMatfileRefusesAWriteBeforeReadingAnything()
    {
        Assert.Contains(
            "Cannot change 'v' because Properties.Writable is false.  To modify 'v', set Properties.Writable to true.",
            RunExpectingError($"v = 1; save({File("m.mat")}, 'v'); m = matfile({File("m.mat")}); m.v(1, 2) = 8;"));
    }

    [Fact]
    public void WritableIsSetThroughProperties()
    {
        Assert.Equal("[1 8 3] 1 matlab.io.matfile.Properties", RunAndRead($$"""
            v = [1 2 3]; save({{File("m.mat")}}, 'v');
            m = matfile({{File("m.mat")}});
            m.Properties.Writable = true;
            m.v(1, 2) = 8;
            S = load({{File("m.mat")}});
            fprintf('%s %d %s\n', mat2str(S.v), m.Properties.Writable, class(m.Properties));
            """));
    }

    [Fact]
    public void ABadWritableLeavesTheSettingsAsTheyWere()
    {
        Assert.Equal("Writable parameter must be a scalar logical. 0", RunAndRead($$"""
            v = 1; save({{File("m.mat")}}, 'v');
            m = matfile({{File("m.mat")}});
            try
                m.Properties.Writable = 'yes';
            catch err
                fprintf('%s %d\n', err.message, m.Properties.Writable);
            end
            """));
    }

    [Fact]
    public void AMatfileIsAHandleAndAnswersTheClassQuestions()
    {
        Assert.Equal("matlab.io.MatFile 1 1 0 0 1 1 0", RunAndRead($$"""
            v = 1; save({{File("m.mat")}}, 'v');
            m = matfile({{File("m.mat")}}); m2 = m; n = matfile({{File("m.mat")}});
            fprintf('%s %d %d %d %d %d %d %d\n', class(m), isobject(m), isa(m, 'handle'), isstruct(m), ishandle(m), isvalid(m), m == m2, m == n);
            """));
    }

    [Fact]
    public void WhoWhosSizeAndPropertiesReadTheHeadersAlone()
    {
        Assert.Equal("v,w | v double [2 3] | [2 3] 2 3 | Properties,v,w", RunAndRead($$"""
            v = [1 2 3; 4 5 6]; w = 'ab'; save({{File("m.mat")}}, 'v', 'w');
            m = matfile({{File("m.mat")}});
            names = who(m); d = whos(m); [r, c] = size(m, 'v');
            fprintf('%s | %s %s %s | %s %d %d | %s\n', strjoin(names', ','), d(1).name, d(1).class, mat2str(d(1).size), mat2str(size(m, 'v')), r, c, strjoin(properties(m)', ','));
            """));
    }

    [Fact]
    public void AMissingVariableAndAMissingFileAreRefusedInMatlabsWords()
    {
        string path = Path.Combine(_folder, "m.mat");
        Assert.Equal($"'zz' does not exist in '{path}'. | Cannot access 'v' because '{Path.Combine(_folder, "none.mat")}' does not exist. 0", RunAndRead($$"""
            v = 1; save({{File("m.mat")}}, 'v');
            m = matfile({{File("m.mat")}}); n = matfile({{File("none.mat")}});
            try, x = m.zz; catch err, a = err.message; end
            try, x = n.v; catch err, b = err.message; end
            fprintf('%s | %s %d\n', a, b, exist({{File("none.mat")}}, 'file'));
            """));
    }

    [Fact]
    public void AWriteThroughAMatfileCreatesTheFile()
    {
        Assert.Equal("2 x 1", RunAndRead($$"""
            m = matfile({{File("new.mat")}}, 'Writable', true);
            m.x = 1;
            S = load({{File("new.mat")}});
            fprintf('%d %s %g\n', exist({{File("new.mat")}}, 'file'), strjoin(fieldnames(S)', ','), S.x);
            """));
    }

    [Fact]
    public void AMatfileReadsASavedObjectAsANewInstance()
    {
        Assert.Equal("HandleHolder 0 [1 2]", RunAndRead($$"""
            h = HandleHolder(); h.data = [1 2]; save({{File("m.mat")}}, 'h');
            m = matfile({{File("m.mat")}});
            k = m.h;
            fprintf('%s %d %s\n', class(k), k == h, mat2str(k.data));
            """));
    }

    [Theory]
    [InlineData("matfile(5)", "Invalid input for argument 1 (rhs1): Value must be a character vector or a string scalar.")]
    [InlineData("matfile('a.mat', 'Writable', 'yes')", "Writable parameter must be a scalar logical.")]
    [InlineData("matfile('a.mat', 'Other', true)", "'Other' is not a matfile option; the one option is 'Writable'.")]
    public void MatfileRefusesBadArguments(string call, string expected)
    {
        Assert.Contains(expected, RunExpectingError($"m = {call};"));
    }

    [Fact]
    public void AVersion73FileIsReadAndNeverWritten()
    {
        string path = MatV73Fixture.Write(_folder, "v73_plain.mat");
        string literal = "'" + path.Replace("'", "''") + "'";
        Assert.Equal("double", RunAndRead($"m = matfile({literal}); d = whos(m); disp(d(1).class);"));
        Assert.Contains("is a version 7.3 MAT-file, which this build reads but does not write.",
            RunExpectingError($"m = matfile({literal}, 'Writable', true); m.x = 1;"));
        Assert.Contains("save writes version 5 MAT-files only",
            RunExpectingError($"v = 1; save({File("v.mat")}, 'v', '-v7.3');"));
    }

    // --- the format --------------------------------------------------------------------------------

    [Fact]
    public void TheReaderWithoutABinderRefusesObjectsAndHandlesByName()
    {
        RunAndRead($"h = HandleHolder(); f = @sin; save({File("o.mat")}, 'h'); save({File("f.mat")}, 'f');");
        Assert.Contains("holds a class object", Assert.Throws<InvalidDataException>(
            () => MatFileReader.Read(Path.Combine(_folder, "o.mat"))).Message);
        Assert.Contains("holds a function handle", Assert.Throws<InvalidDataException>(
            () => MatFileReader.Read(Path.Combine(_folder, "f.mat"))).Message);
    }

    [Fact]
    public void DescribeReadsNameShapeAndClassWithoutDecoding()
    {
        RunAndRead($"h = HandleHolder(); v = [1 2 3; 4 5 6]; t = 'ab'; f = @sin; b = true; save({File("d.mat")}, 'h', 'v', 't', 'f', 'b');");
        // In the order the names were given to save, which is the file's order.
        IReadOnlyList<(string Name, int[] Dims, string Class)> described = MatFileReader.Describe(Path.Combine(_folder, "d.mat"));
        Assert.Equal(["h", "v", "t", "f", "b"], described.Select(static d => d.Name));
        Assert.Equal(["HandleHolder", "double", "char", "function_handle", "logical"], described.Select(static d => d.Class));
        Assert.Equal([2, 3], described[1].Dims);
        Assert.Equal([1, 2], described[2].Dims);
    }
}
