using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// U6 of the app-building plan (ADR 0203): a class that inherits, members with restricted access,
/// handles to methods, and the class, property, method and event attributes that go with them.
/// </summary>
/// <remarks>
/// What R2025b does is pinned by the parity fixtures <c>u6_inherit</c>, <c>u6_access</c>,
/// <c>u6_handles</c>, <c>u6_attrs</c> and <c>u6_more</c>. These tests hold what a recording
/// cannot: how the parser reads the new forms, the refusals that are this build's own words, and
/// the places where the implementation could fail without disagreeing with MATLAB.
/// </remarks>
[Collection("JG facade")]
public class ClassInheritanceU6Tests : IDisposable
{
    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public ClassInheritanceU6Tests()
    {
        JG.Reset();
        _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
        WriteClass("Animal", """
            classdef Animal < handle
                properties
                    Name = 'animal'
                end
                properties (Access = private)
                    Secret = 1
                end
                properties (SetAccess = protected)
                    Legs = 4
                end
                methods
                    function obj = Animal(name)
                        if nargin > 0
                            obj.Name = name;
                        end
                    end
                    function s = speak(obj)
                        s = [obj.Name ' says ' sound(obj)];
                    end
                    function s = sound(obj)
                        s = '...';
                    end
                    function h = whisperer(obj)
                        h = @whisper;
                    end
                end
                methods (Access = private)
                    function s = whisper(obj)
                        s = ['psst ' num2str(obj.Secret)];
                    end
                end
            end
            """);
        WriteClass("Dog", """
            classdef Dog < Animal
                methods
                    function obj = Dog()
                        obj@Animal('dog');
                    end
                    function s = sound(obj)
                        s = 'woof';
                    end
                    function hop(obj)
                        obj.Legs = 3;
                    end
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
            new ScriptContext(
                _output,
                (_, _) => { },
                _directory,
                resolvePath: null,
                figureFiles: new TestFigureFiles()),
            CancellationToken.None).GetAwaiter().GetResult();

    private string Printed(ScriptRunResult result) => result.Message + _output.ErrorText;

    private string RunAndRead(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.True(result.Success, Printed(result));
        return _output.NormalText;
    }

    private string Error(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.False(result.Success, "expected a refusal, got: " + _output.NormalText);
        return Printed(result);
    }

    private static ClassdefStmt Parse(string source) =>
        Assert.IsType<ClassdefStmt>(Parser.Parse(source, "test.m", JgsDialect.Matlab)[0]);

    // --- Parsing ------------------------------------------------------------------------------------

    [Fact]
    public void TheHeaderIsReadIntoItsAttributesAndSuperclasses()
    {
        ClassdefStmt declaration = Parse("""
            classdef (Sealed, Abstract = false, InferiorClasses = {?Low, ?pkg.Lower}, AllowedSubclasses = ?Only) Widget < Base & matlab.mixin.Copyable & handle
            end
            """);

        Assert.Equal("Widget", declaration.Name);
        Assert.True(declaration.Sealed);
        Assert.False(declaration.Abstract);
        Assert.Equal(["Low", "pkg.Lower"], declaration.InferiorClasses);
        Assert.Equal(["Only"], declaration.AllowedSubclasses);
        Assert.Equal(["Base", "matlab.mixin.Copyable", "handle"], declaration.Superclasses);
        Assert.True(declaration.IsHandle);
    }

    [Fact]
    public void ABlocksAttributesRideOnItsMembers()
    {
        ClassdefStmt declaration = Parse("""
            classdef Widget < handle
                properties (GetAccess = ?Friend, SetAccess = immutable, Hidden, Transient = true, NonCopyable, AbortSet)
                    P
                end
                properties (Access = 'protected', Abstract)
                    Q
                end
                methods (Access = {?A, ?B}, Sealed, Hidden)
                    function f(obj)
                    end
                end
                methods (Abstract, Static, Access = private)
                    [a, b] = g(x, ~)
                    h
                end
                events (ListenAccess = private, NotifyAccess = protected, Hidden)
                    Changed
                end
            end
            """);

        ClassProperty p = declaration.Properties[0];
        Assert.Equal(MemberAccessKind.List, p.GetAccess.Kind);
        Assert.Equal(["Friend"], p.GetAccess.Classes);
        Assert.Equal(MemberAccessKind.Immutable, p.SetAccess.Kind);
        Assert.True(p.Hidden && p.Transient && p.NonCopyable && p.AbortSet);
        ClassProperty q = declaration.Properties[1];
        Assert.Equal(MemberAccessKind.Protected, q.GetAccess.Kind);
        Assert.Equal(MemberAccessKind.Protected, q.SetAccess.Kind);
        Assert.True(q.Abstract);

        ClassMethod f = declaration.Methods[0];
        Assert.Equal(["A", "B"], f.Access.Classes);
        Assert.True(f.Sealed && f.Hidden && !f.Abstract && !f.Static);
        ClassMethod g = declaration.Methods[1];
        Assert.True(g.Abstract && g.Static);
        Assert.Equal(MemberAccessKind.Private, g.Access.Kind);
        Assert.Equal(["a", "b"], g.Function.Outputs);
        Assert.Equal(["x", "~"], g.Function.Parameters);
        Assert.Empty(g.Function.Body);
        Assert.Equal("h", declaration.Methods[2].Function.Name);

        ClassEvent changed = Assert.Single(declaration.EventSpecs);
        Assert.Equal(MemberAccessKind.Private, changed.ListenAccess.Kind);
        Assert.Equal(MemberAccessKind.Protected, changed.NotifyAccess.Kind);
        Assert.True(changed.Hidden);
    }

    [Fact]
    public void ASuperclassReferenceIsACallAndIsRememberedByItsMethod()
    {
        ClassdefStmt declaration = Parse("""
            classdef Sub < Base
                methods
                    function obj = Sub(x)
                        obj = obj@Base(x, 2);
                    end
                    function r = area(obj)
                        r = 2 * area@pkg.Base(obj);
                    end
                    function r = plain(obj)
                        r = [obj @sin];
                    end
                end
            end
            """);

        SuperRefExpr construct = Assert.Single(declaration.Methods[0].Function.SuperRefs!);
        Assert.Equal(("obj", "Base"), (construct.Name, construct.Superclass));
        SuperRefExpr method = Assert.Single(declaration.Methods[1].Function.SuperRefs!);
        Assert.Equal(("area", "pkg.Base"), (method.Name, method.Superclass));

        // A space before the @ is a function handle beside a value, as it always was.
        Assert.Null(declaration.Methods[2].Function.SuperRefs);
    }

    [Theory]
    [InlineData("classdef (Bogus) W\nend", "the class attribute 'Bogus' is not supported")]
    [InlineData("classdef W\nproperties (GetObservable)\nP\nend\nend", "the 'properties' attribute 'GetObservable' is not supported")]
    [InlineData("classdef W\nmethods (TestMethod)\nend\nend", "the 'methods' attribute 'TestMethod' is not supported")]
    [InlineData("classdef W < handle\nevents (Bogus)\nE\nend\nend", "the 'events' attribute 'Bogus' is not supported")]
    [InlineData("classdef W\nproperties (Access = immutable)\nP\nend\nend", "'public', 'protected', 'private' or a list of classes")]
    [InlineData("classdef W\nproperties (Hidden = 3)\nP\nend\nend", "is true or false")]
    [InlineData("classdef W < A && B\nend", "joined with '&'")]
    [InlineData("classdef W\nmethods\nr = f(obj)\nend\nend", "holds functions and nothing else")]
    public void WhatTheParserDoesNotKnowIsRefusedByName(string source, string expected)
    {
        JgsSyntaxException refusal = Assert.Throws<JgsSyntaxException>(() => Parser.Parse(source, "test.m", JgsDialect.Matlab));
        Assert.Contains(expected, refusal.Message, StringComparison.Ordinal);
    }

    // --- Inheriting ---------------------------------------------------------------------------------

    [Fact]
    public void ASuperclassMethodCallsTheSubclassOverride()
    {
        Assert.Contains("dog says woof", RunAndRead("d = Dog(); disp(speak(d)); disp(class(d)); disp(isa(d, 'Animal'))"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AClassThatReachesItselfThroughItsSuperclassesIsRefused()
    {
        WriteClass("LoopA", "classdef LoopA < LoopB\nend");
        WriteClass("LoopB", "classdef LoopB < LoopA\nend");

        Assert.Contains("inherits from itself", Error("x = LoopA;"), StringComparison.Ordinal);
    }

    [Fact]
    public void AMatlabClassThisBuildDoesNotSupplyIsSaidToBeThat()
    {
        WriteClass("Mixed", "classdef Mixed < matlab.mixin.Heterogeneous\nend");

        string refusal = Error("x = Mixed;");
        Assert.Contains("'matlab.mixin.Heterogeneous' is a MATLAB class this build does not supply", refusal, StringComparison.Ordinal);
        Assert.Contains("matlab.mixin.Copyable", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void ASuperclassReferenceMustNameASuperclass()
    {
        WriteClass("Stray", """
            classdef Stray < Animal
                methods
                    function s = sound(obj)
                        s = sound@Dog(obj);
                    end
                end
            end
            """);

        Assert.Contains("'Dog' is not a superclass of 'Stray'", Error("s = Stray; sound(s)"), StringComparison.Ordinal);
    }

    [Fact]
    public void APropertyOfASuperclassIsRedefinedOnlyWhenAbstract()
    {
        // R2025b lets a subclass reuse the name of a superclass's private property; this build
        // keeps one slot a name and refuses, in R2025b's words for a property that is not private.
        WriteClass("Reuse", "classdef Reuse < Animal\nproperties\nSecret = 2\nend\nend");

        Assert.Contains("Cannot define property 'Secret' in class 'Reuse'", Error("r = Reuse;"), StringComparison.Ordinal);
    }

    // --- Access -------------------------------------------------------------------------------------

    [Fact]
    public void AccessIsJudgedByTheClassTheCodeBelongsTo()
    {
        Assert.Contains("MATLAB:class:GetProhibited", RunAndRead(
            "a = Animal(); try, a.Secret, catch e, disp(e.identifier), end"), StringComparison.Ordinal);
        Assert.Contains("MATLAB:class:SetProhibited", RunAndRead(
            "a = Animal(); try, a.Legs = 2; catch e, disp(e.identifier), end"), StringComparison.Ordinal);
        Assert.Contains("MATLAB:class:MethodRestricted", RunAndRead(
            "a = Animal(); try, whisper(a), catch e, disp(e.identifier), end"), StringComparison.Ordinal);

        // A subclass writes what its superclass protected, and a handle made inside the class
        // still reaches the private method from a script.
        Assert.Contains("3", RunAndRead("d = Dog(); d.hop(); disp(d.Legs)"), StringComparison.Ordinal);
        Assert.Contains("psst 1", RunAndRead("a = Animal(); h = a.whisperer(); disp(h(a))"), StringComparison.Ordinal);
    }

    [Fact]
    public void AHandleMadeInAScriptDoesNotGainAccessInsideTheClass()
    {
        WriteClass("Caller", """
            classdef Caller < handle
                methods
                    function r = call(obj, h)
                        r = h(obj);
                    end
                end
                methods (Access = private)
                    function r = inner(obj)
                        r = 'inner';
                    end
                end
            end
            """);

        // @inner written in the script is the script's: handing it to a method of the class
        // does not make it the class's.
        Assert.Contains("MATLAB:class:MethodRestricted", RunAndRead(
            "c = Caller(); h = @inner; try, c.call(h), catch e, disp(e.identifier), end"), StringComparison.Ordinal);
    }

    [Fact]
    public void AWriteThroughAPropertyHoldingAHandleNeedsOnlyToReadIt()
    {
        WriteClass("Kennel", """
            classdef Kennel < handle
                properties (SetAccess = private)
                    Pet
                    Tags = {}
                end
                methods
                    function obj = Kennel()
                        obj.Pet = Dog();
                    end
                end
            end
            """);

        // k.Pet.Name = … writes the dog, not the kennel's property; k.Tags{1} = … writes the property.
        Assert.Contains("rex", RunAndRead("k = Kennel(); k.Pet.Name = 'rex'; disp(k.Pet.Name)"), StringComparison.Ordinal);
        Assert.Contains("MATLAB:class:SetProhibited", RunAndRead(
            "k = Kennel(); try, k.Tags{1} = 'x'; catch e, disp(e.identifier), end"), StringComparison.Ordinal);
    }

    // --- The rest -----------------------------------------------------------------------------------

    [Fact]
    public void IsequalOfHandlesThatHoldEachOtherEnds()
    {
        string printed = RunAndRead("""
            a = Animal('x'); b = Animal('x');
            a.Name = b; b.Name = a;
            disp(isequal(a, b))
            c = Animal('y'); c.Name = c;
            d = Animal('y'); d.Name = d;
            disp(isequal(c, d))
            """);

        Assert.Equal("true\ntrue\n", printed);
    }

    [Fact]
    public void ACopyableObjectIsCopiedAndAPlainHandleIsNot()
    {
        WriteClass("Sheet", """
            classdef Sheet < matlab.mixin.Copyable
                properties
                    Cells = [1 2 3]
                end
            end
            """);

        Assert.Contains("6 9", RunAndRead("s = Sheet; t = copy(s); t.Cells = 9; fprintf('%d %d', sum(s.Cells), t.Cells)"),
            StringComparison.Ordinal);
        Assert.Contains("Undefined function 'copy' for input arguments of type 'Animal'", Error("a = Animal(); copy(a)"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void SuperclassesNamesTheChainAndTakesAName()
    {
        string printed = RunAndRead("c = superclasses('Dog'); disp(strjoin(c', ',')); disp(numel(superclasses(5)))");

        Assert.Contains("Animal,handle", printed, StringComparison.Ordinal);
        Assert.Contains("0", printed, StringComparison.Ordinal);
    }
}
