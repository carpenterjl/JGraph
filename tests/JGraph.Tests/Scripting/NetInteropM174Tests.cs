using System.Reflection;
using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Net;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Stage 1 of the .NET and shared-library interop plan (ADR 0174): the external value kind, the
/// type catalog, dotted names that reach .NET, the converter's recorded rank order, members,
/// NET.NetException, and the general fixes the stage's fixtures surfaced. The R2025b answers are the
/// net_basics, net_conversions, net_exceptions and net_display fixtures'; these tests pin the
/// machinery under them — which type a name reaches, what a binding copies, which overload wins.
/// </summary>
/// <remarks>
/// A test that needs the interop test assembly loads it from where the test project copies it
/// (<c>MatlabParity/fixtures/interop</c>), as the fixtures do.
/// </remarks>
[Collection("JG facade")]
public class NetInteropM174Tests : IDisposable
{
    private static readonly string Assembly = Path.Combine(
        AppContext.BaseDirectory, "MatlabParity", "fixtures", "interop", "JGraph.Interop.TestAssembly.dll");

    private RecordingScriptOutput _output = new();

    public NetInteropM174Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string Run(string code, JgsDialect? dialect = null)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(code, context, default, sourceId: "", hook: null, dialect ?? JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim();
    }

    private string Failure(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.False(result.Success, "the statement was expected to be refused: " + _output.NormalText);
        return result.Message ?? _output.ErrorText;
    }

    private static string Load => $"NET.addAssembly('{Assembly}');\n";

    // --- names ---------------------------------------------------------------------------------

    [Fact]
    public void TheCatalogKnowsTheFrameworksNamespacesAndTypes()
    {
        var catalog = new NetCatalog();
        Assert.True(catalog.IsNamespace("System"));
        Assert.True(catalog.IsNamespace("System.Collections.Generic"));
        Assert.False(catalog.IsNamespace("System.String"));
        Assert.Same(typeof(string), catalog.TypeNamed("System.String"));
        Assert.Contains(typeof(List<>), catalog.GenericDefinitions("System.Collections.Generic.List"));
        Assert.Null(catalog.TypeNamed("JGTest.Members")); // not before the session adds it
    }

    [Fact]
    public void AnAddedAssemblyIsVisibleToTheSessionThatAddedItAlone()
    {
        var adding = new NetCatalog();
        Assembly loaded = adding.AddFromPath(Assembly);
        Assert.Same(loaded, adding.AddFromPath(Assembly)); // once per path per process
        Assert.NotNull(adding.TypeNamed("JGTest.Members"));
        Assert.True(adding.IsNamespace("JGTest"));
        Assert.Null(new NetCatalog().TypeNamed("JGTest.Members"));
    }

    [Fact]
    public void ATypeInFrontOfADotNamesItsStaticsWithoutBeingConstructed()
    {
        Assert.Equal("System.Version|8", Run(
            "v = System.Environment.Version; fprintf('%s|%d', class(v), v.Major);"));
        Assert.Equal("7|int32|double", Run(
            "fprintf('%d|%s|%s', System.Math.Max(int32(3), int32(7)), class(System.Math.Max(int32(3), int32(7))), class(System.Math.Max(int32(3), 7)));"));
    }

    [Fact]
    public void AnUnresolvedDottedNameIsMatlabsUndefinedVarOrClass()
    {
        Assert.Equal("MATLAB:undefinedVarOrClass|MATLAB:undefinedVarOrClass|MATLAB:UndefinedFunction", Run(
            "a = 'none'; b = a; c = a;\n"
            + "try, System.NoSuchType(); catch e, a = e.identifier; end\n"
            + "try, NoSuchRoot.Thing(); catch e, b = e.identifier; end\n"
            + "try, nosuchname; catch e, c = e.identifier; end\n"
            + "fprintf('%s|%s|%s', a, b, c);"));
    }

    [Fact]
    public void ADottedHandleAndFevalTextReachAStaticMethod()
    {
        Assert.Equal("4|4|System.Math.Max", Run(
            "h = @System.Math.Max; fprintf('%d|%d|%s', h(3, 4), feval('System.Math.Max', 3, 4), func2str(h));"));
    }

    [Fact]
    public void JgsReachesDotNetThroughFevalAndHoldsWhatComesBack()
    {
        Assert.Equal("4\nSystem.Text.StringBuilder", Run(
            "print(feval(\"System.Math.Max\", 3, 4));\nlet b = feval(\"System.Text.StringBuilder\", \"x\");\nprint(class(b));",
            JgsDialect.Jgs).ReplaceLineEndings("\n"));
    }

    // --- values --------------------------------------------------------------------------------

    [Fact]
    public void AReferenceTypeIsSharedByBindingAndAValueTypeIsCopied()
    {
        Assert.Equal("xy|3|99", Run(Load
            + "a = System.Text.StringBuilder('x'); b = a; b.Append('y');\n"
            + "p = JGTest.Point(3, 4); q = p; q.X = 99;\n"
            + "fprintf('%s|%d|%d', char(a.ToString()), p.X, q.X);"));
    }

    [Fact]
    public void AMethodOnAValueTypeChangesTheVariablesOwnCopy()
    {
        Assert.Equal("13|3", Run(Load
            + "p = JGTest.Point(3, 4); q = p; p.Move(10); fprintf('%d|%d', p.X, q.X);"));
    }

    [Fact]
    public void ReturnsKeepTheirClassAndAStringStaysDotNet()
    {
        Assert.Equal("uint8|int16|single|char|System.String|double|0 0", Run(Load
            + "fprintf('%s|%s|%s|%s|%s|%s|', class(JGTest.Returns.Byte()), class(JGTest.Returns.Int16()), "
            + "class(JGTest.Returns.Single()), class(JGTest.Returns.Char()), class(JGTest.Returns.String()), "
            + "class(JGTest.Returns.BoxedDouble()));\n"
            + "fprintf('%d %d', size(JGTest.Returns.NullString()));"));
    }

    [Fact]
    public void AnInt64PastTwoToTheFiftyThirdWarnsUnlessTheWarningIsOff()
    {
        Run(Load + "x = JGTest.Returns.Int64();");
        Assert.Contains("past 2^53", _output.ErrorText);

        Run(Load + "warning('off', 'JGraph:interop:int64Precision'); x = JGTest.Returns.Int64();");
        Assert.DoesNotContain("past 2^53", _output.ErrorText);
    }

    [Fact]
    public void ADotNetObjectJoinsNothingAndIsNoCondition()
    {
        Assert.Equal("MATLAB:class:concatenationScalar", Run(
            "m = System.Text.StringBuilder(); try, [m m]; catch e, disp(e.identifier), end"));
        Assert.Contains("Conversion to logical", Failure("m = System.Text.StringBuilder(); if m, end"));
    }

    [Fact]
    public void EqualityIsTheOperatorOverloadThenIdentity()
    {
        Assert.Equal("1|1|0|1", Run(Load
            + "s = System.String('Hello'); m = JGTest.Members(1, 'a');\n"
            + "fprintf('%d|%d|%d|%d', s == 'Hello', m == m, m == JGTest.Members(1, 'a'), isequal(m, JGTest.Members(1, 'a')));"));
    }

    // --- the converter -------------------------------------------------------------------------

    [Fact]
    public void RankFollowsTheRecordedOrderPerMatlabClass()
    {
        JgsValue three = JgsValue.Number(3);
        Assert.True(NetConvert.Rank(three, typeof(double)) < NetConvert.Rank(three, typeof(float)));
        Assert.True(NetConvert.Rank(three, typeof(long)) < NetConvert.Rank(three, typeof(int))); // recorded, not intuitive
        Assert.Null(NetConvert.Rank(three, typeof(string)));

        JgsValue flag = JgsValue.True;
        Assert.True(NetConvert.Rank(flag, typeof(bool)) < NetConvert.Rank(flag, typeof(byte)));

        JgsValue text = JgsValue.Str("abc");
        Assert.True(NetConvert.Rank(text, typeof(char[])) < NetConvert.Rank(text, typeof(string)));
        Assert.Null(NetConvert.Rank(text, typeof(char)));

        // Nullable<T> sits just after T.
        Assert.True(NetConvert.Rank(three, typeof(int?)) > NetConvert.Rank(three, typeof(int)));
        Assert.True(NetConvert.Rank(three, typeof(int?)) < NetConvert.Rank(three, typeof(uint)));

        Assert.Null(NetConvert.Rank(JgsValue.EmptyStruct(), typeof(object)));
    }

    [Fact]
    public void NanAndComplexRankAsARealDoubleAndFailAtConversion()
    {
        JgsValue nan = JgsValue.Number(double.NaN);
        Assert.Equal(NetConvert.Rank(JgsValue.Number(1), typeof(int)), NetConvert.Rank(nan, typeof(int)));
        Assert.Throws<JgsRuntimeException>(() => NetConvert.ToNet(nan, typeof(int), 1, "x", 0, 0));
        Assert.Equal(0, ((int[])NetConvert.ToNet(nan, typeof(int[]), 1, "x", 0, 0)!)[0]);

        JgsValue complex = JgsValue.ComplexNum(new System.Numerics.Complex(1.5, 2));
        Assert.Equal(1.5m, NetConvert.ToNet(complex, typeof(decimal), 1, "x", 0, 0));
        JgsRuntimeException refused = Assert.Throws<JgsRuntimeException>(() => NetConvert.ToNet(complex, typeof(double), 1, "x", 0, 0));
        Assert.Equal("Invalid input for argument 1 (x):\nValue must be real.", refused.Message);
    }

    // --- exceptions ----------------------------------------------------------------------------

    [Fact]
    public void ADotNetExceptionIsCaughtAsANetExceptionWithItsObject()
    {
        Assert.Equal("NET.NetException|MATLAB:NET:CLRException:MethodInvoke|System.NotSupportedException|1|static refused", Run(Load
            + "try, JGTest.Thrower.ThrowStatic(); catch e\n"
            + "fprintf('%s|%s|%s|%d|%s', class(e), e.identifier, class(e.ExceptionObject), isa(e, 'MException'), char(e.ExceptionObject.Message));\nend"));
    }

    [Fact]
    public void ANullReferenceInDotNetCodeIsTheScriptsErrorNotAnInternalFault()
    {
        Assert.Equal("MATLAB:NET:CLRException:MethodInvoke", Run(Load
            + "try, JGTest.Thrower.NullRef(); catch e, disp(e.identifier), end"));
    }

    // --- the general fixes the stage's fixtures surfaced ---------------------------------------

    [Fact]
    public void EvalcCapturesAStatementsEcho()
    {
        Assert.Equal("[x = 5]", Run("x = 5; t = evalc('x'); fprintf('[%s]', strtrim(t));"));
    }

    [Fact]
    public void AScalarLogicalIndexIsAOneElementMask()
    {
        Assert.Equal("7|0|0", Run("x = 7; s = \"a\"; fprintf('%d|%d|%d', x(true), numel(x(false)), numel(s(false)));"));
    }

    [Fact]
    public void StrjoinOfOneStringIsThatString()
    {
        Assert.Equal("abc|string", Run("fprintf('%s|%s', strjoin(\"abc\", \" / \"), class(strjoin(\"abc\", \",\")));"));
    }

    [Fact]
    public void IsmethodAndIsjavaAnswerForEveryValue()
    {
        Assert.Equal("1|1|0|0", Run(Load
            + "m = JGTest.Members(); fprintf('%d|%d|%d|%d', ismethod(m, 'Describe'), ismethod(m, 'Increment'), ismethod(m, 'Nope'), isjava(m));"));
    }
}
