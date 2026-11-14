using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open items 196, 309 and 310 (ADR 0353): a metaclass's member lists as R2025b's metadata objects, a
/// property's class converted before its size is fitted and its implicit default, validateattributes in
/// R2025b's catalog words, isvalid of a value object, help of several names and secretID's compact
/// display. Every expectation was measured in R2025b by probes p196, p196b, p309, p309b, p310 and p310b.
/// </summary>
[Collection("JG facade")]
public class MetaClassValidationTests : IDisposable
{
    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public MetaClassValidationTests()
    {
        JG.Reset();
        _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
        WriteClass("MkVal", """
            classdef MkVal
                % MKVAL a value class.
                properties
                    A = 1
                    B (1,1) double
                end
                properties (Constant)
                    K = 42
                end
                methods
                    function obj = MkVal(a)
                        if nargin > 0
                            obj.A = a;
                        end
                    end
                    function r = first(obj, x)
                        r = x;
                    end
                    function [p, q] = second(obj)
                        p = 1; q = 2;
                    end
                end
                methods (Static)
                    function r = make()
                        r = MkVal();
                    end
                end
            end
            """);
        WriteClass("MkHandle", """
            classdef MkHandle < handle
                events
                    Changed
                end
                methods
                    function delete(obj)
                    end
                    function bump(obj)
                    end
                end
            end
            """);
        WriteClass("MkTyped", """
            classdef MkTyped
                properties
                    S (1,1) string
                    N (1,1) double
                    V (1,3) double
                    T (1,:) char
                    C (:,1) cell
                end
            end
            """);
    }

    public void Dispose()
    {
        JG.Reset();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    private void WriteClass(string name, string body) =>
        File.WriteAllText(Path.Combine(_directory, name + ".m"), body);

    private ScriptRunResult Run(string code) =>
        _engine.RunAsync(
            code,
            new ScriptContext(_output, (_, _) => { }, _directory, resolvePath: null, figureFiles: new TestFigureFiles()),
            CancellationToken.None).GetAwaiter().GetResult();

    private string RunAndRead(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText;
    }

    [Fact]
    public void AMetaclassListsItsMethodsAsR2025bOrdersThem()
    {
        Assert.Equal(
            "matlab.metadata.Method\nsecond first MkVal make empty\nMkVal MkVal MkVal MkVal MkVal\n0 0 0 1 1 \nobj,x\n",
            RunAndRead("""
                mc = ?MkVal;
                disp(class(mc.MethodList))
                disp(strjoin({mc.MethodList.Name}, ' '))
                disp(strjoin(arrayfun(@(m) m.DefiningClass.Name, mc.MethodList', 'UniformOutput', false), ' '))
                fprintf('%d ', [mc.MethodList.Static]); fprintf('\n');
                disp(strjoin(mc.MethodList(2).InputNames', ','))
                """));
    }

    [Fact]
    public void AHandleClassListsHandlesMethodsLessThoseItOverrides()
    {
        string printed = RunAndRead("""
            mc = meta.class.fromName('MkHandle');
            fprintf('%d\n', numel(mc.MethodList))
            disp(strjoin({mc.MethodList(1:6).Name}, ' '))
            disp(mc.MethodList(5).DefiningClass.Name)
            fprintf('%d\n', sum(strcmp({mc.MethodList.Name}, 'delete')))
            disp(strjoin({mc.EventList.Name}, ' '))
            disp(mc.SuperclassList(1).Name)
            fprintf('%d\n', numel(meta.class.fromName('handle').MethodList))
            """);
        Assert.Equal("35\nbump delete MkHandle empty eq ne\nhandle\n1\nChanged ObjectBeingDestroyed\nhandle\n33\n", printed);
    }

    [Fact]
    public void AMetaclassPropertyCarriesItsAttributes()
    {
        Assert.Equal("A B K\npublic public none\n1 0 1 \n0 0 1 \n42\n", RunAndRead("""
            mc = ?MkVal;
            disp(strjoin({mc.PropertyList.Name}, ' '))
            disp(strjoin({mc.PropertyList.SetAccess}, ' '))
            fprintf('%d ', [mc.PropertyList.HasDefault]); fprintf('\n');
            fprintf('%d ', [mc.PropertyList.NonCopyable]); fprintf('\n');
            fprintf('%d\n', mc.PropertyList(3).DefaultValue)
            """));
    }

    [Fact]
    public void AMetadataObjectIsReadOnlyAndKeepsItsClassWhenIndexed()
    {
        Assert.Equal("matlab.metadata.Method\nMATLAB:class:SetProhibited|Unable to set the 'Name' property of class ''Method'' because it is read-only.\n",
            RunAndRead("""
                mc = ?MkVal;
                m = mc.MethodList(1);
                disp(class(m))
                try
                    m.Name = 'zz';
                catch e
                    disp([e.identifier '|' e.message])
                end
                """));
    }

    [Fact]
    public void APropertyConvertsItsClassBeforeFittingItsSize()
    {
        Assert.Equal("string abc\n5 5 5 \n97 98 99 \nab\n2 1 \nMATLAB:validation:IncompatibleSize|Error setting property 'N' of class 'MkTyped'. Value must be a scalar.\n",
            RunAndRead("""
                o = MkTyped();
                o.S = 'abc'; disp([class(o.S) ' ' char(o.S)])
                o.V = 5; fprintf('%g ', o.V); fprintf('\n');
                o.V = 'abc'; fprintf('%g ', o.V); fprintf('\n');
                o.T = ['a'; 'b']; disp(o.T)
                o.C = {1, 2}; fprintf('%d ', size(o.C)); fprintf('\n');
                try
                    o.N = [1 2];
                catch e
                    disp([e.identifier '|' e.message])
                end
                """));
    }

    [Fact]
    public void ATypedPropertyWithNoDefaultStartsAsItsClassesZeros()
    {
        WriteClass("MkPositive", """
            classdef MkPositive
                properties
                    X (1,1) double {mustBePositive}
                end
            end
            """);
        Assert.Equal("string 1 1\n0\n0 0 0 \n1 0\n0 1\nError using implicit default value of property 'X' of class 'MkPositive'. Value must be positive.\n",
            RunAndRead("""
                o = MkTyped();
                fprintf('%s %d %d\n', class(o.S), size(o.S));
                fprintf('%g\n', o.N);
                fprintf('%g ', o.V); fprintf('\n');
                fprintf('%d %d\n', size(o.T));
                fprintf('%d %d\n', size(o.C));
                try
                    MkPositive();
                catch e
                    disp(e.message)
                end
                """));
    }

    [Theory]
    [InlineData("validateattributes(5, {'char'}, {})", "MATLAB:invalidType|Expected input to be one of these types:\n\nchar\n\nInstead its type was double.")]
    [InlineData("validateattributes(5, [\"string\", \"char\"], {})", "MATLAB:invalidType|Expected input to be one of these types:\n\nstring, char\n\nInstead its type was double.")]
    [InlineData("validateattributes(['ab'; 'cd'], {'char'}, {'scalartext'})", "MATLAB:expectedScalartext|Expected input to be a non-missing string scalar or character vector.")]
    [InlineData("validateattributes(-1, {'numeric'}, {'positive'}, 'myfun', 'x', 2)", "MATLAB:myfun:expectedPositive|Expected input number 2, x, to be positive.")]
    [InlineData("validateattributes(3, {'numeric'}, {'>', 5})", "MATLAB:notGreater|Expected input to be an array with all of the values > 5.")]
    [InlineData("validateattributes(ones(2, 3), {'numeric'}, {'size', [NaN 2]})", "MATLAB:incorrectSize|Expected input to be of size Mx2, but it is of size 2x3.")]
    [InlineData("validateattributes([1 2], {'numeric'}, {'numel', 3})", "MATLAB:incorrectNumel|Expected input to be an array with number of elements equal to 3.")]
    [InlineData("validateattributes(1, 5, {})", "MATLAB:validateattributes:badClassList|Valid data types must be a string vector or cell array of character vectors.")]
    [InlineData("validateattributes(1, {'numeric'}, {}, 'f', 'x', 0)", "MATLAB:validateattributes:badArgPosition|The last argument must be a positive integer.")]
    [InlineData("validateattributes('a', {'numeric'}, {})", "MATLAB:invalidType|Expected input to be one of these types:\n\ndouble, single, uint8, uint16, uint32, uint64, int8, int16, int32, int64")]
    [InlineData("validateattributes(\"\", {'string'}, {'nonempty'}); error('x:y', 'passed')", "x:y|passed")]
    public void ValidateattributesRefusesInR2025bsWords(string call, string expected)
    {
        Assert.Equal(expected + "\n", RunAndRead($"""
            try
                {call};
            catch e
                disp([e.identifier '|' e.message])
            end
            """));
    }

    [Fact]
    public void IsvalidOfAValueObjectIsAnUndefinedFunction()
    {
        Assert.Equal("MATLAB:UndefinedFunction|Undefined function 'isvalid' for input arguments of type 'MkVal'.\n", RunAndRead("""
            try
                isvalid(MkVal());
            catch e
                disp([e.identifier '|' e.message])
            end
            """));
    }

    [Fact]
    public void HelpOfSeveralNamesIsEmptyAndPrintsTheJoinedTopicNotFound()
    {
        string printed = RunAndRead("""
            fprintf('%d %d\n', size(help('MkVal', 'MkVal')))
            help sin cos
            """);
        Assert.StartsWith("0 0\n", printed, StringComparison.Ordinal);
        Assert.Contains("sin cos not found.", printed, StringComparison.Ordinal);
    }

    [Fact]
    public void ASecretIdShowsItsCompactFormInAStructAndACell()
    {
        string printed = RunAndRead("""
            s = secretID('kname');
            t = secretID("other");
            disp(struct('x', s))
            disp({s})
            disp(struct('x', [s t]))
            disp({[s; t]})
            """);
        Assert.Contains("x: kname (secretID)", printed, StringComparison.Ordinal);
        Assert.Contains("[kname] (secretID)", printed, StringComparison.Ordinal);
        Assert.Contains("x: [kname    other] (secretID)", printed, StringComparison.Ordinal);
        Assert.Contains("2×1 secretID (secretID)", printed, StringComparison.Ordinal);
    }
}
