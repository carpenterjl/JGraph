using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Stage 4 of the .NET and shared-library interop plan (ADR 0177): .NET arrays under parentheses,
/// the conversions out of them, enum members in MATLAB's verbs, generic types and methods, and a
/// .NET dictionary converted to a MATLAB one. The R2025b answers are the net_arrays, net_generics and
/// net_enums fixtures' and probe4's; these tests pin the machinery under them.
/// </summary>
[Collection("JG facade")]
public class NetInteropM177Tests : IDisposable
{
    private static readonly string Assembly = Path.Combine(
        AppContext.BaseDirectory, "MatlabParity", "fixtures", "interop", "JGraph.Interop.TestAssembly.dll");

    private RecordingScriptOutput _output = new();

    public NetInteropM177Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string Run(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(Load + code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim().ReplaceLineEndings("\n");
    }

    private static string Load => $"NET.addAssembly('{Assembly}');\n";

    private static string Id(string expression) =>
        $"try, {expression}; fprintf('none|'); catch e, fprintf('%s|', e.identifier); end\n";

    // --- arrays ----------------------------------------------------------------------------------

    [Fact]
    public void AnArrayElementWrittenThroughOneNameIsSeenThroughEveryOther()
    {
        Assert.Equal("9|9|7", Run(
            "r = JGTest.ArrayMaker.Ramp(int32(3)); q = r; q(1) = 9; bump(r);\n"
            + "fprintf('%d|%d|%d', r(1), q(1), r(3));\n"
            + "function bump(a)\na(3) = 7;\nend\n"));
    }

    [Fact]
    public void EndIsRefusedWhereverItStandsInASubscript()
    {
        Assert.Equal("MATLAB:NET:UnsupportedEndIndexingArray|MATLAB:NET:UnsupportedEndIndexingArray|MATLAB:NET:UnsupportedEndIndexingArray|",
            Run("r = JGTest.ArrayMaker.Ramp(int32(3));\n" + Id("r(end - 1)") + Id("r(min(end, 2))") + Id("r(end) = 1")));
    }

    [Fact]
    public void ASubscriptMustBeAPositiveWholeNumberAndOnePerDimension()
    {
        Assert.Equal(
            "MATLAB:NET:InvalidArrayIndex|MATLAB:NET:InvalidArrayIndex|MATLAB:NET:InvalidArrayIndex|MATLAB:class:UndefinedMethod|MATLAB:class:UndefinedMethod|2",
            Run("r = JGTest.ArrayMaker.Ramp(int32(3)); g = JGTest.ArrayMaker.Grid(int32(2), int32(3));\n"
                + Id("r(:)") + Id("r([1 2])") + Id("r(uint8(0))") + Id("g(2)") + Id("r()")
                + "fprintf('%d', g(uint8(1), int16(3)));"));
    }

    [Fact]
    public void AnIndexPastTheBoundsIsTheArraysOwnException()
    {
        Assert.Equal("MATLAB:NET:CLRException:MethodInvoke|System.IndexOutOfRangeException|1", Run(
            "r = JGTest.ArrayMaker.Ramp(int32(3));\n"
            + "try, r(4) = 1; catch e, fprintf('%s|%s|%d', e.identifier, class(e.ExceptionObject), isa(e, 'NET.NetException')); end"));
    }

    [Fact]
    public void AValueTheElementTypeRefusesIsNoSetMethodAndLeavesTheArray()
    {
        Assert.Equal("MATLAB:class:UndefinedMethod|MATLAB:class:UndefinedMethod|1|3", Run(
            "r = JGTest.ArrayMaker.Ramp(int32(3)); w = JGTest.ArrayMaker.Words();\n"
            + Id("r(1) = 'x'") + Id("w(1) = 5") + "r(3) = true; fprintf('%d|%d', r(3), w.Length);"));
    }

    [Fact]
    public void ARectangularJaggedArrayIsAMatrixAndARaggedOneIsRefused()
    {
        Assert.Equal("2x3 uint8 6|MATLAB:NET:NetConversion:NonRectJaggedArray|", Run(
            "j = NET.createArray('System.Byte[]', 2); j(1) = NET.convertArray(uint8([1 2 3])); j(2) = NET.convertArray(uint8([4 5 6]));\n"
            + "m = uint8(j); fprintf('%dx%d %s %d|', size(m, 1), size(m, 2), class(m), m(2, 3));\n"
            + Id("double(JGTest.ArrayMaker.Jagged())")));
    }

    [Fact]
    public void ACellOfAnArrayConvertsEachElementAsItsElementTypeAnswers()
    {
        Assert.Equal("char|System.String|double|MATLAB:invalidConversion|", Run(
            "w = cell(JGTest.ArrayMaker.Words()); m = cell(JGTest.ArrayMaker.Mixed()); j = cell(JGTest.ArrayMaker.Jagged());\n"
            + "fprintf('%s|%s|%s|', class(w{1}), class(m{2}), class(j{3}));\n" + Id("cell(JGTest.ArrayMaker.Bools())")));
    }

    [Fact]
    public void AnArrayTypeNamedWithItsBracketsAndAGenericElementCreate()
    {
        Assert.Equal("System.Int32[,][]|2|System.Collections.Generic.List<System*Double>[]|MATLAB:NET:CLRException:TypeError|", Run(
            "a = NET.createArray('System.Int32[,]', 2); g = NET.GenericClass('System.Collections.Generic.List', 'System.Double');\n"
            + "fprintf('%s|%d|%s|', class(a), a.Length, class(NET.createArray(g, 1)));\n" + Id("NET.createArray('No.Such', 1)")));
    }

    [Fact]
    public void ArithmeticOnATypeWithoutOperatorsIsNotNumericAndAMissingOverloadIsUndefined()
    {
        Assert.Equal("MATLAB:math:mustBeNumericCharOrLogical|MATLAB:math:mustBeNumericCharOrLogical|MATLAB:UndefinedFunction|MATLAB:sum:wrongInput|MATLAB:max:wrongInput|", Run(
            "r = JGTest.ArrayMaker.Ramp(int32(3)); v = JGTest.Vector2(1, 2);\n"
            + Id("r .* 2") + Id("System.Text.StringBuilder('a') | 1") + Id("2 * v") + Id("mean(r)") + Id("max(r)")));
    }

    // --- enums -----------------------------------------------------------------------------------

    [Fact]
    public void AnEnumConvertsToDoubleAndItsOwnIntegerClassOnly()
    {
        Assert.Equal("250|uint8|MATLAB:invalidConversion|-5|MATLAB:invalidConversion|MATLAB:invalidConversion|", Run(
            "s = JGTest.Small.High; b = JGTest.Big.Negative;\n"
            + "fprintf('%d|%s|', double(s), class(uint8(s)));\n" + Id("uint16(s)")
            + "fprintf('%d|', int64(b));\n" + Id("int32(b)") + Id("logical(JGTest.Color.Red)")));
    }

    [Fact]
    public void FlagsCombineInTheirOwnTypeAndEveryOtherOperandIsRefused()
    {
        Assert.Equal("JGTest.Access|All|Read|MATLAB:Bitor:operandsNotNumeric|MATLAB:bitxor:operandsNotNumeric|MATLAB:UndefinedFunction|MATLAB:UndefinedFunction|", Run(
            "a = bitor(bitor(JGTest.Access.Read, JGTest.Access.Write), JGTest.Access.Execute);\n"
            + "fprintf('%s|%s|%s|', class(a), char(a), char(bitand(a, JGTest.Access.Read)));\n"
            + Id("bitor(JGTest.Color.Red, JGTest.Color.Blue)") + Id("bitxor(1, JGTest.Access.Read)")
            + Id("bitand(JGTest.Access.Read, JGTest.Color.Green)") + Id("bitor(JGTest.Access.Read, JGTest.Access.Read, 'uint8')")));
    }

    [Fact]
    public void EnumMembersCompareByValueAndAMemberMatchesItsNameOnlyInTheTextVerbs()
    {
        Assert.Equal("1|1|0|0|1|1|1|0|hit|miss", Run(
            "c = JGTest.Color.Blue;\n"
            + "fprintf('%d|%d|%d|%d|%d|%d|%d|%d|', c > JGTest.Color.Red, JGTest.Color.Green == JGTest.Access.Read, c == 2, c == 'Blue',\n"
            + "    strcmp(c, 'Blue'), isequal(c, 'Blue'), ismember(c, {'Red', 'Blue'}), isequal(c, 2));\n"
            + "switch c\n  case JGTest.Color.Blue\n    fprintf('hit|');\n  otherwise\n    fprintf('miss|');\nend\n"
            + "switch c\n  case 'Blue'\n    fprintf('hit');\n  otherwise\n    fprintf('miss');\nend\n"));
    }

    [Fact]
    public void EnumerationPrintsTheMembersInValueOrderAndAnswersOnlyOne()
    {
        Assert.Equal(
            "[\nEnumeration members for class 'JGTest.Access':\n\n    None\n    Read\n    Write\n    Execute\n    All\n\n"
            + "|No class 'No.Such'.\n|MATLAB:class:concatenationScalar|",
            Run("t = evalc('enumeration JGTest.Access'); u = evalc('enumeration(''No.Such'')');\n"
                + "fprintf('[%s|%s|', t, u);\n" + Id("x = enumeration(JGTest.Color.Red)")).Replace("\r", ""));
    }

    // --- generics --------------------------------------------------------------------------------

    [Fact]
    public void AGenericClassValueClosesANestedTypeArgumentAndShowsNoProperties()
    {
        Assert.Equal("NET.GenericClass|0|JGTest.Box<System*Collections*Generic*List<System*Int32>>|MATLAB:NET:CLRException:BuildGenericType|MATLAB:minrhs|", Run(
            "g = NET.GenericClass('System.Collections.Generic.List', 'System.Int32');\n"
            + "fprintf('%s|%d|%s|', class(g), numel(properties(g)), class(NET.createGeneric('JGTest.Box', {g})));\n"
            + Id("NET.GenericClass('No.Such', 'System.Double')") + Id("NET.GenericClass('System.Collections.Generic.List')")));
    }

    [Fact]
    public void AGenericMethodIsCalledOnATypeOrAnObjectWithItsTypeArguments()
    {
        Assert.Equal("int32|System.String|MATLAB:NET:NoGenericMethod|MATLAB:NET:NoMatchingGenericMethod|MATLAB:NET:InvalidGenericParameterType|MATLAB:NET:CLRException:InvokeGenericMethod|", Run(
            "x = NET.invokeGenericMethod('JGTest.GenericTools', 'Echo', {'System.Int32'}, int32(5));\n"
            + "y = NET.invokeGenericMethod('JGTest.GenericTools', 'TypeOf', {'System.String'});\n"
            + "fprintf('%s|%s|', class(x), char(y));\n"
            + Id("NET.invokeGenericMethod(JGTest.GenericTools.Range(int32(2)), 'Count', {'System.Int32'})")
            + Id("NET.invokeGenericMethod('JGTest.GenericTools', 'Echo', {'System.Int32', 'System.Int32'}, 1)")
            + Id("NET.invokeGenericMethod('JGTest.GenericTools', 'Echo', 'System.Int32', 1)")
            + Id("NET.invokeGenericMethod('JGTest.GenericTools', 'Echo', {'No.Such'}, 1)")));
    }

    [Fact]
    public void AGenericNestedInAGenericIsNamedWithAStar()
    {
        Assert.Equal("System.Collections.Generic.Dictionary*ValueCollection<System*String,System*Double>",
            Run("t = JGTest.GenericTools.Table(); fprintf('%s', class(t.Values));"));
    }

    // --- dictionaries ----------------------------------------------------------------------------

    [Fact]
    public void ADotNetDictionaryConvertsToACopyWithStringKeys()
    {
        Assert.Equal("one,two|string|2|2|3|int32|three", Run(
            "t = JGTest.GenericTools.Table(); d = dictionary(t); t.Add('three', 3);\n"
            + "di = NET.createGeneric('System.Collections.Generic.Dictionary', {'System.Int32', 'System.String'}); di.Add(int32(3), 'three');\n"
            + "e = dictionary(di);\n"
            + "fprintf('%s|%s|%d|%d|%d|%s|%s', strjoin(sort(keys(d))', ','), class(keys(d)), numEntries(d), d(\"two\"), keys(e), class(keys(e)), char(values(e)));"));
    }

    [Fact]
    public void ANonGenericDictionaryConvertsTooAndADictionaryIsNoObjectArgument()
    {
        // R2025b crashes on dictionary(System.Collections.Hashtable()) (probe4): JGraph converts it.
        Assert.Equal("k|1|MATLAB:NET:NetConversion:ObjectConversion|", Run(
            "h = System.Collections.Hashtable(); h.Add('k', 1); d = dictionary(h);\n"
            + "fprintf('%s|%d|', keys(d), d('k'));\n"
            + Id("JGTest.Overloads.Only_Object(dictionary([\"a\" \"b\"], [1 2]))")));
    }
}
