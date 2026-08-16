using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M68: user classes. A <c>classdef</c> parses, an instance holds checked properties, methods are
/// reachable both ways round, operators are what the class says they are, and a handle class means one
/// object where a value class means two.
/// </summary>
/// <remarks>
/// Classes are written into a temporary folder and reached through the search path, because that is
/// how a real project holds them — one class per file, named after the class. A few tests define the
/// class inline instead, which works for the same reason a class file does: a <c>classdef</c> is one
/// statement with one meaning.
/// </remarks>
[Collection("JG facade")]
public class MatlabClassdefTests : IDisposable
{
    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public MatlabClassdefTests()
    {
        JG.Reset();
        _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
        WriteClass("Circle", """
            classdef Circle
                properties
                    Radius (1,1) double {mustBeNonnegative} = 1
                    Label = 'unnamed'
                end
                properties (Constant)
                    Sides = Inf
                end
                methods
                    function obj = Circle(r, label)
                        if nargin > 0
                            obj.Radius = r;
                        end
                        if nargin > 1
                            obj.Label = label;
                        end
                    end
                    function a = area(obj)
                        a = pi * obj.Radius^2;
                    end
                    function [w, h] = extent(obj)
                        w = 2 * obj.Radius;
                        h = w;
                    end
                    function obj = grow(obj, by)
                        obj.Radius = obj.Radius + by;
                    end
                end
                methods (Static)
                    function c = unit()
                        c = Circle(1);
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

    // --- Parsing ------------------------------------------------------------------------------------

    [Fact]
    public void AClassFileParsesIntoItsPropertiesAndMethods()
    {
        var declaration = Assert.IsType<ClassdefStmt>(Assert.Single(Parser.Parse(
            """
            classdef Widget < handle
                properties
                    Size (1,1) double = 2
                end
                properties (Constant)
                    Kind = 'widget'
                end
                methods
                    function obj = Widget(s)
                        obj.Size = s;
                    end
                    function r = twice(obj)
                        r = 2 * obj.Size;
                    end
                end
                methods (Static)
                    function d = describe()
                        d = 'a widget';
                    end
                end
            end
            """,
            "Widget.m",
            JgsDialect.Matlab)));

        Assert.Equal("Widget", declaration.Name);
        Assert.True(declaration.IsHandle);
        Assert.Equal(["Size", "Kind"], declaration.Properties.Select(p => p.Spec.Name));
        Assert.True(declaration.Properties[1].Constant);
        Assert.Equal(["Widget", "twice", "describe"], declaration.Methods.Select(m => m.Function.Name));
        Assert.True(declaration.Methods[2].Static);
    }

    [Fact]
    public void PropertiesAndMethodsAreOnlyBlockWordsInsideAClass()
    {
        // The two words are the names of two builtins, so a classdef must not take them away.
        Assert.Equal("3\n2\n", RunAndRead("""
            properties = [1 2 3];
            methods = 2;
            disp(numel(properties));
            disp(methods);
            """));
    }

    [Fact]
    public void UnsupportedClassBlocksAndSuperclassesAreRefusedByName()
    {
        Assert.Contains("events", Assert.Throws<JgsSyntaxException>(
            static () => Parser.Parse("classdef A\n events\n Changed\n end\nend", "A.m", JgsDialect.Matlab)).Message,
            StringComparison.Ordinal);

        Assert.Contains("only inherit from 'handle'", Assert.Throws<JgsSyntaxException>(
            static () => Parser.Parse("classdef A < Base\nend", "A.m", JgsDialect.Matlab)).Message,
            StringComparison.Ordinal);

        Assert.Contains("Abstract", Assert.Throws<JgsSyntaxException>(
            static () => Parser.Parse("classdef A\n methods (Abstract)\n end\nend", "A.m", JgsDialect.Matlab)).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AClassFileMustBeNamedAfterItsClass()
    {
        WriteClass("Wrong", "classdef Different\nend\n");
        Assert.Contains("named after its class", Error("x = Wrong();"), StringComparison.Ordinal);
    }

    // --- Instances ----------------------------------------------------------------------------------

    [Fact]
    public void AConstructorFillsInWhatItWasGivenAndDefaultsTheRest()
    {
        Assert.Equal("Circle 3 big\n1 unnamed\n", RunAndRead("""
            c = Circle(3, 'big');
            fprintf('%s %g %s\n', class(c), c.Radius, c.Label);
            d = Circle;
            fprintf('%g %s\n', d.Radius, d.Label);
            """));
    }

    [Fact]
    public void APropertyIsCheckedAgainstItsDeclarationOnEveryWrite()
    {
        Assert.Contains("Circle.Radius", Error("c = Circle(1); c.Radius = -2;"), StringComparison.Ordinal);
        Assert.Contains("Circle.Radius", Error("c = Circle(-1);"), StringComparison.Ordinal);
        Assert.Contains("has no property 'Nope'", Error("c = Circle(1); c.Nope = 3;"), StringComparison.Ordinal);
    }

    [Fact]
    public void AConstantBelongsToTheClassAndCannotBeAssignedTo()
    {
        Assert.Equal("Inf Inf\n", RunAndRead("c = Circle(1); fprintf('%g %g\\n', Circle.Sides, c.Sides);"));
        Assert.Contains("Constant", Error("c = Circle(1); c.Sides = 3;"), StringComparison.Ordinal);
        Assert.Contains("Constant", Error("Circle.Sides = 3;"), StringComparison.Ordinal);
    }

    [Fact]
    public void AMethodIsReachableThroughTheDotAndThroughTheArgument()
    {
        Assert.Equal("28.2743 28.2743\n6 6 6 6\n", RunAndRead("""
            c = Circle(3);
            fprintf('%.4f %.4f\n', c.area, area(c));
            [w, h] = extent(c);
            [w2, h2] = c.extent();
            fprintf('%g %g %g %g\n', w, h, w2, h2);
            """));
    }

    [Fact]
    public void AStaticMethodIsCalledOnTheClass()
    {
        Assert.Equal("1\n", RunAndRead("fprintf('%g\\n', Circle.unit().Radius);"));
        Assert.Contains("static method", Error("c = Circle(1); c.unit();"), StringComparison.Ordinal);
    }

    [Fact]
    public void AValueClassCopiesAndAHandleClassDoesNot()
    {
        WriteClass("Counter", """
            classdef Counter < handle
                properties
                    N = 0
                end
                methods
                    function tick(obj)
                        obj.N = obj.N + 1;
                    end
                end
            end
            """);

        Assert.Equal("3 4\n2 2\n", RunAndRead("""
            c = Circle(3);
            d = c;
            d = d.grow(1);
            fprintf('%g %g\n', c.Radius, d.Radius);
            k = Counter;
            k2 = k;
            k2.tick();
            k2.tick();
            fprintf('%g %g\n', k.N, k2.N);
            """));
    }

    [Fact]
    public void AClassWithNoConstructorRefusesArguments()
    {
        WriteClass("Bare", "classdef Bare\n properties\n  A = 1\n end\nend\n");
        Assert.Equal("1\n", RunAndRead("b = Bare; disp(b.A);"));
        Assert.Contains("no constructor", Error("b = Bare(2);"), StringComparison.Ordinal);
    }

    // --- Operators and display ----------------------------------------------------------------------

    [Fact]
    public void AClassDecidesWhatItsOperatorsMean()
    {
        WriteMoney();
        Assert.Equal("7.5 -3 13\n1 1 0\n", RunAndRead("""
            a = Money(3);
            b = Money(4.5);
            fprintf('%g %g %g\n', (a + b).Amount, (-a).Amount, (a + 10).Amount);
            fprintf('%d %d %d\n', a == Money(3), a < b, a == b);
            """));
    }

    [Fact]
    public void AnOperatorTheClassHasNotDefinedIsRefusedByName()
    {
        WriteMoney();
        string message = Error("a = Money(3) * Money(2);");
        Assert.Contains("not defined for Money", message, StringComparison.Ordinal);
        Assert.Contains("mtimes", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AClassWithADispMethodShowsItselfThatWayBothTimes()
    {
        WriteMoney();
        // The explicit call and the echo of a bare name have to agree, which is why the echo asks
        // the class rather than formatting the properties itself.
        Assert.Equal("$3.00\nm =\n$3.00\n", RunAndRead("m = Money(3);\ndisp(m)\nm\n"));
    }

    [Fact]
    public void AClassWithoutADispMethodShowsItsPropertiesInOrder()
    {
        string shown = RunAndRead("c = Circle(2, 'ring');\ndisp(c)\n");
        Assert.Contains("Circle with properties:", shown, StringComparison.Ordinal);
        Assert.Contains("Radius: 2", shown, StringComparison.Ordinal);
        Assert.Contains("Label: ring", shown, StringComparison.Ordinal);
    }

    // --- Introspection ------------------------------------------------------------------------------

    [Fact]
    public void AnObjectAnswersWhatItIsAndWhatItHas()
    {
        Assert.Equal("1 0 0\nRadius,Label,Sides\nCircle,area,extent,grow,unit\n", RunAndRead("""
            c = Circle(1);
            fprintf('%d %d %d\n', isobject(c), isobject(3), isobject(struct('a', 1)));
            fprintf('%s\n', strjoin(properties(c)', ','));
            fprintf('%s\n', strjoin(methods(c)', ','));
            """));
    }

    [Fact]
    public void AClassCanBeAskedAboutByName()
    {
        Assert.Equal("3 5 Circle\n", RunAndRead("""
            m = metaclass(Circle(1));
            fprintf('%d %d %s\n', numel(properties('Circle')), numel(methods('Circle')), m.Name);
            """));
    }

    [Fact]
    public void AnExceptionIsAnObjectRatherThanAStruct()
    {
        // M62 gave MException a class name and M65 gave it a real struct-array stack; what it never
        // stopped answering was isstruct, and that was the whole of the gap (M68).
        Assert.Equal("0 1 MException\n1 0\n", RunAndRead("""
            ME = MException('a:b', 'first');
            fprintf('%d %d %s\n', isstruct(ME), isobject(ME), class(ME));
            fprintf('%d %d\n', isstruct(struct('a', 1)), isstruct(containers.Map));
            """));
    }

    [Fact]
    public void AnExceptionCanCarryTheCauseUnderneathIt()
    {
        Assert.Equal("1 c:d why\n", RunAndRead("""
            ME = addCause(MException('a:b', 'outer'), MException('c:d', 'why'));
            fprintf('%d %s %s\n', numel(ME.cause), ME.cause{1}.identifier, ME.cause{1}.message);
            """));
    }

    // --- Scope --------------------------------------------------------------------------------------

    [Fact]
    public void AMethodsLocalsStayInsideIt()
    {
        // A method's locals are ordinary short words, so this is where the old write-through would
        // have shown: `by` inside grow must not become the caller's `by` (M68 found it, ADR 0068).
        Assert.Equal("5 4\n", RunAndRead("""
            by = 5;
            c = Circle(3);
            c = c.grow(1);
            fprintf('%g %g\n', by, c.Radius);
            """));
    }

    [Fact]
    public void AFunctionsLocalsStayInsideItToo()
    {
        Assert.Equal("10\n", RunAndRead("""
            x = 10;
            bump();
            disp(x);
            function bump()
            x = 99;
            end
            """));
    }

    private void WriteMoney() => WriteClass("Money", """
        classdef Money
            properties
                Amount = 0
            end
            methods
                function obj = Money(a)
                    if nargin > 0
                        obj.Amount = a;
                    end
                end
                function r = plus(a, b)
                    r = Money(value(a) + value(b));
                end
                function r = uminus(a)
                    r = Money(-a.Amount);
                end
                function t = eq(a, b)
                    t = value(a) == value(b);
                end
                function t = lt(a, b)
                    t = value(a) < value(b);
                end
                function disp(obj)
                    fprintf('$%.2f\n', obj.Amount);
                end
                function v = value(x)
                    if isa(x, 'Money')
                        v = x.Amount;
                    else
                        v = x;
                    end
                end
            end
        end
        """);
}
