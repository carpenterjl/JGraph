using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Net;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Stage 2 of the .NET and shared-library interop plan (ADR 0175): members in full — the signature
/// model (inputs, optional inputs, outputs from <c>ref</c> and <c>out</c>), overloads chosen by output
/// count as well as by argument, writes and their refusals, <c>NET.setStaticProperty</c>,
/// <c>NET.explicitCast</c>, and the <c>methods</c> listing. The R2025b answers are the net_members and
/// net_display fixtures'; these tests pin the machinery under them.
/// </summary>
[Collection("JG facade")]
public class NetInteropM175Tests : IDisposable
{
    private static readonly string Assembly = Path.Combine(
        AppContext.BaseDirectory, "MatlabParity", "fixtures", "interop", "JGraph.Interop.TestAssembly.dll");

    private RecordingScriptOutput _output = new();

    public NetInteropM175Tests() => JG.Reset();

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

    private static Type Modifiers => new NetCatalog().AddFromPath(Assembly).GetType("JGTest.Modifiers")!;

    // --- the signature model -------------------------------------------------------------------

    [Fact]
    public void AnOutParameterIsAnOutputAndNotAnInput()
    {
        NetSignature refOut = Assert.Single(NetSignature.Group(Modifiers, "RefOutReturn", instance: false));
        Assert.Single(refOut.Inputs);               // ref double a
        Assert.Equal(3, refOut.OutputCount);        // RetVal, a, label
        Assert.True(refOut.Fits(1, 3));
        Assert.False(refOut.Fits(2, 1));

        NetSignature split = Assert.Single(NetSignature.Group(Modifiers, "Split", instance: false));
        Assert.Single(split.Inputs);
        Assert.Equal(2, split.OutputCount);
        Assert.False(split.Returns);
    }

    [Fact]
    public void TrailingOptionalParametersMayBeLeftOff()
    {
        NetSignature optional = Assert.Single(NetSignature.Group(Modifiers, "Optional", instance: false));
        Assert.Equal(1, optional.Required);
        Assert.True(optional.Fits(1, 1) && optional.Fits(3, 1));
        Assert.False(optional.Fits(0, 1) || optional.Fits(4, 1));
    }

    [Fact]
    public void AVoidMethodWithNoRefParameterFitsOnlyACallAskingForNothing()
    {
        NetSignature nothing = Assert.Single(NetSignature.Group(Modifiers, "Void", instance: false));
        Assert.Equal(0, nothing.OutputCount);
        Assert.True(nothing.Fits(0, 0));
        Assert.False(nothing.Fits(0, 1));
    }

    [Fact]
    public void AMethodGroupIsBuiltOncePerTypeAndName()
    {
        Assert.Same(NetSignature.Group(typeof(Math), "Max", instance: false), NetSignature.Group(typeof(Math), "Max", instance: false));
        Assert.Contains(NetSignature.Group(typeof(decimal), "op_Addition", instance: false), static s => s.IsStatic); // operators are callable
    }

    [Fact]
    public void FullTextFollowsRecordedNotation()
    {
        string Text(string name) => Assert.Single(NetSignature.Group(Modifiers, name, instance: false)).FullText(Modifiers);
        Assert.Equal("Static [double scalar RetVal, double scalar a, System.String label] RefOutReturn(double scalar a)", Text("RefOutReturn"));
        Assert.Equal("Static int32 scalar RetVal Optional(int32 scalar a, optional<int32 scalar> b, optional<int32 scalar> c)", Text("Optional"));
        Assert.Equal("Static System.String name MakeName", Text("MakeName"));
        Assert.Equal("Static Void", Text("Void"));
        Assert.Equal("Static System.Nullable<System*Int32> RetVal MaybeNull(logical scalar give)", Text("MaybeNull"));
    }

    // --- calls ---------------------------------------------------------------------------------

    [Fact]
    public void RefAndOutValuesComeBackAfterTheReturnValue()
    {
        Assert.Equal("20|2|a=2|4|2 0.75", Run(Load
            + "[r, a, l] = JGTest.Modifiers.RefOutReturn(1);\n"
            + "[w, f] = feval('JGTest.Modifiers.Split', 2.75);\n"
            + "fprintf('%d|%d|%s|%d|%g %g', r, a, char(l), JGTest.Modifiers.Double(2), w, f);"));
    }

    [Fact]
    public void AskingForMoreOutputsThanAnOverloadHasFindsNoOverload()
    {
        Assert.Equal("MATLAB:UndefinedFunction|MATLAB:class:UndefinedMethod|none", Run(Load
            + "try, x = JGTest.Modifiers.Void(); catch e, a = e.identifier; end\n"
            + "m = JGTest.Members(); try, x = m.Bump(); catch e, b = e.identifier; end\n"
            + "c = 'none'; try, m.Bump(); m.Bump; JGTest.Members.Reset; catch e, c = e.identifier; end\n"
            + "fprintf('%s|%s|%s', a, b, c);"));
    }

    [Fact]
    public void ABareVoidMemberRunsAsAStatementAndIsRefusedAnOutput()
    {
        Assert.Equal("3|MATLAB:class:UndefinedMethod|MATLAB:UndefinedFunction", Run(Load
            + "m = JGTest.Members(1); m.Bump; m.Bump\n"
            + "try, x = m.Bump; catch e, a = e.identifier; end\n"
            + "try, x = JGTest.Modifiers.Void; catch e, b = e.identifier; end\n"
            + "fprintf('%d|%s|%s', m.Value, a, b);"));
    }

    [Fact]
    public void ExplicitCastRefusesAsRecorded()
    {
        Assert.Equal("MATLAB:NET:interfaceView:RequireNetObject|MATLAB:NET:interfaceView:UnsupportedClassToClass|MATLAB:NET:interfaceView:InvalidCast|MATLAB:NET:InvalidClassName", Run(Load
            + "g = JGTest.Greeter(); m = JGTest.Members(); ids = {};\n"
            + "try, NET.explicitCast(3, 'JGTest.IGreeter'); catch e, ids{end+1} = e.identifier; end\n"
            + "try, NET.explicitCast(g, 'System.Object'); catch e, ids{end+1} = e.identifier; end\n"
            + "try, NET.explicitCast(m, 'JGTest.IGreeter'); catch e, ids{end+1} = e.identifier; end\n"
            + "try, NET.setStaticProperty('JGTest.NoSuchType.X', 1); catch e, ids{end+1} = e.identifier; end\n"
            + "fprintf('%s', strjoin(ids, '|'));"));
    }

    [Fact]
    public void MissingValueStandsForAnOptionalDefault()
    {
        Assert.Equal("157|153", Run(Load
            + "fprintf('%d|%d', JGTest.Modifiers.Optional(int32(1)), JGTest.Modifiers.Optional(int32(1), System.Reflection.Missing.Value, int32(3)));"));
    }

    // --- writes --------------------------------------------------------------------------------

    [Fact]
    public void APropertyWriteRefusesWhatItsTypeCannotTake()
    {
        Assert.Equal("MATLAB:class:RequireScalar|MATLAB:class:RequireNumeric|MATLAB:class:GetProhibited|MATLAB:index:assignmentToTemporary", Run(Load
            + "m = JGTest.Members(2, 'm'); ids = {};\n"
            + "try, m.Value = 'text'; catch e, ids{end+1} = e.identifier; end\n"
            + "try, m.Value = \"7\"; catch e, ids{end+1} = e.identifier; end\n"
            + "try, x = m.WriteOnly; catch e, ids{end+1} = e.identifier; end\n"
            + "try, m.Item(0) = 99; catch e, ids{end+1} = e.identifier; end\n"
            + "fprintf('%s', strjoin(ids, '|'));"));
    }

    [Fact]
    public void SetStaticPropertyWritesAStaticAndRefusesAReadOnlyOne()
    {
        Assert.Equal("8|MATLAB:NET:InvalidStaticPropertyAccess|MATLAB:NET:InvalidStaticPropName", Run(Load
            + "JGTest.Members.Reset(); NET.setStaticProperty('JGTest.Members.StaticField', 8);\n"
            + "try, NET.setStaticProperty('JGTest.Members.StaticReadOnly', 'x'); catch e, a = e.identifier; end\n"
            + "try, NET.setStaticProperty('JGTest.Members.NoSuch', 1); catch e, b = e.identifier; end\n"
            + "fprintf('%d|%s|%s', JGTest.Members.StaticField, a, b); JGTest.Members.Reset();"));
    }

    // --- views, loops, delete ------------------------------------------------------------------

    [Fact]
    public void AnInterfaceViewReachesAnExplicitImplementation()
    {
        Assert.Equal("NET.view.JGTest.IGreeter|hi from the interface|MATLAB:noSuchMethodOrField", Run(Load
            + "g = JGTest.Greeter(); v = NET.explicitCast(g, 'JGTest.IGreeter');\n"
            + "try, g.Greet(); catch e, id = e.identifier; end\n"
            + "fprintf('%s|%s|%s', class(v), char(v.Greet()), id);"));
    }

    [Fact]
    public void ALoopOverADotNetObjectIsRefusedAndDeleteDoesNotDispose()
    {
        Assert.Equal("MATLAB:class:parenReferenceScalar|0|1", Run(Load
            + "try, for x = JGTest.Sequence(int32(2)), end, catch e, id = e.identifier; end\n"
            + "JGTest.Resource.ResetCount(); r = JGTest.Resource(); delete(r);\n"
            + "fprintf('%s|%d|%d', id, JGTest.Resource.Disposed, exist('r', 'var'));"));
    }

    // --- methods -------------------------------------------------------------------------------

    [Fact]
    public void MethodsPrintsColumnsWhenItsAnswerIsNotAsked()
    {
        string listing = Run(Load + "methods('JGTest.Statics')");
        string[] lines = listing.Split('\n');
        Assert.Equal("Methods for class JGTest.Statics:", lines[0]);
        // Seven names in one row, each padded to ReferenceEquals' length plus two.
        Assert.Equal("end              isempty          isscalar         length           ndims            numel            size             ", lines[2]);
        Assert.Contains("Static methods:", lines);
        Assert.Equal("Methods of JGTest.Statics inherited from handle.", lines[^1]);
    }

    [Fact]
    public void MethodsFullAnswersTheSignaturesWhenAsked()
    {
        Assert.Equal("Static Nothing|1", Run(Load
            + "c = methods('JGTest.Statics', '-full'); fprintf('%s|%d', c{contains(c, 'Nothing')}, iscell(c));"));
    }

    [Fact]
    public void ANestedTypeKeepsItsPlus()
    {
        Assert.Equal("JGTest.Outer+Inner|inner", Run(Load
            + "i = JGTest.Outer.MakeInner(); fprintf('%s|%s', class(i), char(i.Where()));"));
    }
}
