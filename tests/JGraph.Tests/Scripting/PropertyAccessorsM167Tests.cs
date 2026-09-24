using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V6 (ADR 0167), sixteenth sub-stage: property accessors and <c>Dependent</c> properties (appendix
/// A #27, #28, #147). A <c>get.p</c> method answers every read of the property and a <c>set.p</c>
/// method does every write, except inside the method's own body; a composite write through the
/// property is get, modify, set in R2025b's order; a <c>Dependent</c> property has no storage; and
/// the display, <c>properties</c>, <c>fieldnames</c>, <c>isprop</c> and <c>struct</c> of an object
/// with accessors read through them.
/// </summary>
/// <remarks>
/// The parity fixture <c>property_accessors</c> holds R2025b's answers for the forms; these pin the
/// roads the sub-stage touched, one assertion a road, and the places this build has its own answer:
/// a validator's message keeps M68's shape, and a getter that reads through a helper recurses to
/// this build's recursion limit rather than to MATLAB's out-of-memory error.
/// </remarks>
[Collection("JG facade")]
public class PropertyAccessorsM167Tests : IDisposable
{
    /// <summary>The parity fixtures' helpers folder: <c>vlog</c>, <c>AccBox</c>, <c>AccHBox</c> and the rest.</summary>
    private static readonly string Helpers = Path.Combine(AppContext.BaseDirectory, "MatlabParity", "fixtures", "helpers");

    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgraph-accessors-" + Guid.NewGuid().ToString("N"));

    public PropertyAccessorsM167Tests() => JG.Reset();

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
        var context = new ScriptContext(_output, (_, _) => { }, Path.GetTempPath(), resolvePath: null, figureFiles: new TestFigureFiles());
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

    /// <summary>Writes a class file into the test's own path folder.</summary>
    private void WriteClass(string name, string source)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, name + ".m"), source);
    }

    /// <summary>The fixtures' log: a global the helper <c>vlog</c> appends to.</summary>
    private const string Log = "global vlog_text; vlog_text = ''; ";

    /// <summary>A subscript and a right-hand side that log when they run, as the fixtures' local functions do.</summary>
    private const string Loggers = "\nfunction k = idx_l()\nvlog('idx'); k = 2;\nend\nfunction v = rhs_l()\nvlog('rhs'); v = 9;\nend\n";

    // --- when an accessor runs ----------------------------------------------------------------------

    [Theory]
    [InlineData("o = AccBox();", "|[1 2 3]")]
    [InlineData("o = AccBox(5);", "set;|5")]
    [InlineData("o = AccBox(); x = o.p;", "get;|[1 2 3]")]
    [InlineData("o = AccBox(); x = o.peek();", "get;|[1 2 3]")]
    [InlineData("o = AccBox(); o.p = 9;", "set;|9")]
    [InlineData("o = AccBox(); o.p(2) = 9;", "get;set;|[1 9 3]")]
    [InlineData("o = AccBox(); o.p(2) = [];", "get;set;|[1 3]")]
    [InlineData("o = AccBox(); o.p(end + 1) = 4;", "get;get;set;|[1 2 3 4]")]
    [InlineData("o = AccBox(); o.p = o.p + 1;", "get;set;|[2 3 4]")]
    public void AGetMethodAnswersAReadAndASetMethodDoesAWrite(string statements, string expected)
    {
        Assert.Equal(expected, RunAndRead(Log + statements + " fprintf('%s|%s\\n', vlog_text, mat2str(o.peek()));"));
    }

    [Fact]
    public void AWholeSetOfTheEmptyGoesThroughTheSetter()
    {
        Assert.Equal("set;|1 0 0", RunAndRead(Log + "o = AccBox(); o.p = []; fprintf('%s|%d %d %d\\n', vlog_text, isempty(o.peek()), size(o.peek()));"));
    }

    [Fact]
    public void ADefaultDoesNotRunTheSetMethodButTheConstructorDoes()
    {
        Assert.Equal("[] [set;]", RunAndRead(Log + "o = AccBox(); a = vlog_text; o = AccBox(2); fprintf('[%s] [%s]\\n', a, vlog_text);"));
    }

    // --- R2025b's order -----------------------------------------------------------------------------

    [Theory]
    [InlineData("o.p(idx_l()) = rhs_l();", "idx;rhs;get;set;", "[1 9 3]")]
    [InlineData("o.p(end) = rhs_l();", "get;rhs;get;set;", "[1 2 9]")]
    [InlineData("o.p(idx_l():end) = rhs_l();", "idx;get;rhs;get;set;", "[1 9 9]")]
    [InlineData("o.p = o.p + rhs_l();", "get;rhs;set;", "[10 11 12]")]
    [InlineData("o.s.f(2) = rhs_l();", "rhs;gets;sets;", "[1 2 3]")]
    public void ACompositeWriteIsSubscriptsThenRhsThenGetThenSet(string statement, string log, string value)
    {
        Assert.Equal(log + "|" + value, RunAndRead(Log + $$"""
            o = AccBox();
            {{statement}}
            fprintf('%s|%s\n', vlog_text, mat2str(o.peek()));
            """ + Loggers));
    }

    [Fact]
    public void AReadWithEndRunsTheGetterOnceForEndAndOnceForTheValue()
    {
        Assert.Equal("get;get;|3 get;|2", RunAndRead(Log + """
            o = AccBox(); x = o.p(end); a = vlog_text; vlog_text = '';
            y = o.p(2); fprintf('%s|%d %s|%d\n', a, x, vlog_text, y);
            """));
    }

    [Fact]
    public void AHandlesGetterCountsItsCalls()
    {
        Assert.Equal("get;get;|[1 2 3 1 2]", RunAndRead(Log + "h = AccHBox(); x = h.p; y = h.p(1); fprintf('%s|%s\\n', vlog_text, mat2str([x y h.hits]));"));
    }

    // --- the accessor's own body --------------------------------------------------------------------

    [Fact]
    public void InsideTheGetterTheReadIsTheStorageAndAWriteCallsTheSetter()
    {
        Assert.Equal("get;set;|[1 2 3]", RunAndRead(Log + "g = AccGetWrites(); x = g.p; fprintf('%s|%s\\n', vlog_text, mat2str(x));"));
    }

    [Fact]
    public void InsideTheSetterTheWriteIsTheStorageAndAReadCallsTheGetter()
    {
        Assert.Equal("set;get;old3;|5", RunAndRead(Log + "o = AccSetReads(); o.p = 5; a = vlog_text; fprintf('%s|%d\\n', a, o.p);"));
    }

    [Fact]
    public void ALocalFunctionAfterTheClassdefIsTheClasssOwn()
    {
        WriteClass("AccLocal", """
            classdef AccLocal
                properties
                    p = [1 2 3]
                end
                methods
                    function v = total(obj)
                        v = add_up(obj.p);
                    end
                end
            end

            function v = add_up(x)
            v = sum(x);
            end
            """);
        Assert.Equal("6", RunAndRead("o = AccLocal(); disp(o.total());"));
        Assert.Contains("not recognized", RunExpectingError("o = AccLocal(); disp(add_up([1 2]));"), StringComparison.Ordinal);
    }

    // --- Dependent ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("x = o.q;", "getq;get;", "[2 4 6]")]
    [InlineData("o.q = [8 8 8]; x = o.peek();", "setq;set;get;", "[4 4 4]")]
    [InlineData("o.q(2) = 8; x = o.peek();", "getq;get;setq;set;get;", "[1 4 3]")]
    [InlineData("h = AccHBox(); h.q(2) = 8; x = h.p;", "getq;get;setq;set;get;", "[1 4 3]")]
    public void ADependentPropertyReadsAndWritesThroughItsAccessors(string statement, string log, string value)
    {
        Assert.Equal(log + "|" + value, RunAndRead(Log + $"o = AccBox(); {statement} fprintf('%s|%s\\n', vlog_text, mat2str(x));"));
    }

    [Theory]
    [InlineData("d = AccDepNoGet(); x = d.r;", "In class 'AccDepNoGet', no get method is defined for dependent property 'r'. A dependent property needs a get method to access its value.")]
    [InlineData("d = AccDepNoGet(); d.r = 1;", "In class 'AccDepNoGet', no set method is defined for dependent property 'r'. A dependent property needs a set method to assign its value.")]
    [InlineData("b = DepBox(); b.q = 1;", "In class 'DepBox', no set method is defined for dependent property 'q'. A dependent property needs a set method to assign its value.")]
    [InlineData("b = DepBox(); b.q(2) = 8;", "In class 'DepBox', no set method is defined for dependent property 'q'. A dependent property needs a set method to assign its value.")]
    [InlineData("g = AccGetNoProp();", "Cannot specify a get function for property 'zz' in class 'AccGetNoProp', because that property is not defined by that class.")]
    [InlineData("b = AccBadSet(); b.p = 5;", "The set function for property 'p' must return an instance of class 'AccBadSet'.")]
    public void TheRefusalsAreInR2025bsWords(string statements, string expected)
    {
        Assert.Contains(expected, RunExpectingError(statements), StringComparison.Ordinal);
    }

    [Fact]
    public void ADependentPropertysDefaultIsIgnoredAndAHandleSetterMayReturnTheObject()
    {
        Assert.Equal("2 5 set;", RunAndRead(Log + "d = AccDepDefault(); b = AccHBadSet(); b.p = 5; fprintf('%d %d %s\\n', d.q, b.p, vlog_text);"));
    }

    [Fact]
    public void ADependentPropertyIsListedAndAskedAboutButNotStored()
    {
        Assert.Equal("p,q|1 1 0|p,s,n,q|", RunAndRead(Log + """
            b = DepBox(); o = AccBox();
            fprintf('%s|%d %d %d|%s|%s\n', strjoin(properties(b)', ','), isprop(b, 'q'), isprop(b, 'p'), isprop(b, 'zz'), strjoin(fieldnames(o)', ','), vlog_text);
            """));
    }

    [Fact]
    public void StructOfAnObjectReadsThroughTheGetters()
    {
        Assert.Equal("get;gets;getq;get;|p,s,n,q|[2 4 6]", RunAndRead(Log + "o = AccBox(); st = struct(o); fprintf('%s|%s|%s\\n', vlog_text, strjoin(fieldnames(st)', ','), mat2str(st.q));"));
    }

    [Fact]
    public void TheDisplayShowsADependentPropertyAndRunsTheGetters()
    {
        string shown = RunAndRead(Log + "o = AccBox(); disp(o); fprintf('%s\\n', vlog_text);");
        Assert.Contains("q: [2, 4, 6]", shown, StringComparison.Ordinal);
        Assert.EndsWith("get;gets;getq;get;", shown, StringComparison.Ordinal);
    }

    [Fact]
    public void MethodsDoesNotListTheAccessors()
    {
        Assert.Equal("AccBox,peek", RunAndRead("o = AccBox(); fprintf('%s\\n', strjoin(methods(o)', ','));"));
    }

    // --- validation and refusal ---------------------------------------------------------------------

    [Fact]
    public void TheDeclarationIsCheckedBeforeTheSetMethodRuns()
    {
        Assert.Equal("E;|1 setn;|97", RunAndRead(Log + """
            o = AccBox();
            try, o.n = -1; catch, vlog('E'); end
            a = sprintf('%s|%d', vlog_text, o.n); vlog_text = '';
            o.n = 'a';
            fprintf('%s %s|%d\n', a, vlog_text, o.n);
            """));
    }

    [Theory]
    [InlineData("o = AccRefuse();")]
    [InlineData("o = AccHRefuse();")]
    public void ASetMethodThatRefusesLeavesThePropertyAsItWas(string make)
    {
        Assert.Equal("negative refused|[1 2 3]", RunAndRead(make + " try, o.p(2) = -1; catch err, m = err.message; end; fprintf('%s|%s\\n', m, mat2str(o.p));"));
    }

    // --- values, handles and holders ----------------------------------------------------------------

    [Fact]
    public void ACopyOfAValueObjectIsWrittenAlone()
    {
        Assert.Equal("get;set;|[1 2 3]|[9 2 3]", RunAndRead(Log + "a = AccBox(); b = a; b.p(1) = 9; fprintf('%s|%s|%s\\n', vlog_text, mat2str(a.peek()), mat2str(b.peek()));"));
    }

    [Fact]
    public void TheSetMethodReceivesAShareOfTheValue()
    {
        Assert.Equal("1", RunAndRead("v = ones(1, 3); b = SetBox(); b.p = v; v(1) = 7; disp(b.p(1));"));
    }

    [Fact]
    public void AnAliasOfAHandleSeesTheWrite()
    {
        Assert.Equal("get;set;|[1 9 3]", RunAndRead(Log + "h = AccHBox(); g = h; h.p(2) = 9; a = vlog_text; fprintf('%s|%s\\n', a, mat2str(g.p));"));
    }

    [Theory]
    [InlineData("st.h = AccHBox(); st.h.p = 7; x = st.h.p;", "set;get;|7")]
    [InlineData("st.b = AccBox(); st.b.p(end) = rhs_l(); x = st.b.peek();", "get;rhs;get;set;get;|[1 2 9]")]
    [InlineData("c = {AccBox()}; c{1}.p(idx_l()) = rhs_l(); x = c{1}.peek();", "idx;rhs;get;set;get;|[1 9 3]")]
    [InlineData("c = {AccBox()}; c{1}.p = 4; x = c{1}.peek();", "set;get;|4")]
    [InlineData("c = {AccHBox()}; c{1}.q(1) = 20; x = c{1}.p;", "getq;get;setq;set;get;|[10 2 3]")]
    [InlineData("st.h = LogHBox(); st.h.p(end) = rhs_l(); x = st.h.p;", "get;rhs;get;set;get;|[1 2 9]")]
    public void AnObjectHeldInAStructOrACellIsWrittenThroughItsAccessors(string statements, string expected)
    {
        Assert.Equal(expected, RunAndRead(Log + statements + " fprintf('%s|%s\\n', vlog_text, mat2str(x));" + Loggers));
    }

    [Fact]
    public void TwoWritesInOneScriptAreTwoGetSetPairs()
    {
        Assert.Equal("get;set;get;set;|[7 2 8]", RunAndRead(Log + "o = AccBox(); o.p(1) = 7; o.p(3) = 8; fprintf('%s|%s\\n', vlog_text, mat2str(o.peek()));"));
    }

    [Fact]
    public void AWholeSetOnAHandleReadsNothing()
    {
        Assert.Equal("get;set;|1", RunAndRead(Log + "o = AccHBox(); o.p = o.p; fprintf('%s|%d\\n', vlog_text, o.hits);"));
    }

    // --- the parser ---------------------------------------------------------------------------------

    [Fact]
    public void ADotAfterAFunctionNameOutsideAMethodsBlockIsStillRefused()
    {
        Assert.Contains("Expected an expression", RunExpectingError("function v = get.p(x)\nv = x;\nend\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownPropertiesAttributeIsStillRefused()
    {
        WriteClass("AccHidden", """
            classdef AccHidden
                properties (Hidden)
                    p = 1
                end
            end
            """);
        Assert.Contains("'Hidden' is not supported", RunExpectingError("o = AccHidden();"), StringComparison.Ordinal);
    }
}
