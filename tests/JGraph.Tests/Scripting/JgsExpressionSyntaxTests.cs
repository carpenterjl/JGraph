using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M20a: C-style expression syntax — compound assignment (+=, -=, *=, /=, %=), increment/decrement
/// (prefix and postfix), assignment as an expression, newline leniency, and destructuring let.
/// </summary>
[Collection("JG facade")]
public class JgsExpressionSyntaxTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public JgsExpressionSyntaxTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> Run(string code) =>
        _engine.RunAsync(code, new ScriptContext(_output, (_, figure) => _figures.Add(figure), null), default);

    // --- Compound assignment --------------------------------------------------------------------

    [Fact]
    public async Task CompoundAssignment_AllFiveOperators_OnVariables()
    {
        ScriptRunResult result = await Run("""
            let x = 10
            x += 5
            print(x)
            x -= 3
            print(x)
            x *= 2
            print(x)
            x /= 4
            print(x)
            x %= 4
            print(x)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("15", _output.NormalText);
        Assert.Contains("12", _output.NormalText);
        Assert.Contains("24", _output.NormalText);
        Assert.Contains("6", _output.NormalText);
        Assert.Contains("2", _output.NormalText);
    }

    [Fact]
    public async Task CompoundAssignment_OnArrayElement()
    {
        ScriptRunResult result = await Run("""
            let a = [1, 2, 3]
            a[1] += 10
            a[2] *= 3
            print(a)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("[1, 12, 9]", _output.NormalText);
    }

    [Fact]
    public async Task CompoundAssignment_BroadcastsElementwise_OverArrays()
    {
        ScriptRunResult result = await Run("""
            let xs = [1, 2, 3]
            xs += 1
            print(xs)
            xs *= [2, 3, 4]
            print(xs)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("[2, 3, 4]", _output.NormalText);
        Assert.Contains("[4, 9, 16]", _output.NormalText);
    }

    [Fact]
    public async Task PlusAssign_ConcatenatesStrings_LikePlus()
    {
        ScriptRunResult result = await Run("""
            let s = "abc"
            s += "def"
            print(s)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("abcdef", _output.NormalText);
    }

    [Fact]
    public async Task Assignment_IsAnExpression_YieldingTheStoredValue()
    {
        ScriptRunResult result = await Run("""
            let x = 1
            let y = (x += 2)
            print(x, y)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("3 3", _output.NormalText);
    }

    [Fact]
    public async Task ChainedAssignment_IsRightAssociative()
    {
        ScriptRunResult result = await Run("""
            let a = 1
            let b = 2
            a = b = 7
            print(a, b)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("7 7", _output.NormalText);
    }

    [Fact]
    public async Task AssignmentInCondition_IsAllowed()
    {
        ScriptRunResult result = await Run("""
            let x = 0
            if x = 5 {
                print("truthy:", x)
            }
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("truthy: 5", _output.NormalText);
    }

    [Fact]
    public async Task CompoundAssignment_IndexTarget_EvaluatesIndexOnce()
    {
        ScriptRunResult result = await Run("""
            let calls = [0]
            fn f() {
                calls[0] += 1
                return 1
            }
            let a = [10, 20, 30]
            a[f()] += 5
            print(a[1], calls[0])
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("25 1", _output.NormalText);
    }

    [Fact]
    public async Task Assignment_ToUndeclaredVariable_StillErrors()
    {
        ScriptRunResult result = await Run("nope += 1");

        Assert.False(result.Success);
        Assert.Contains("not defined", result.Message);
    }

    [Fact]
    public async Task Assignment_ToNonAssignableTarget_IsASyntaxError()
    {
        ScriptRunResult result = await Run("5 += 1");

        Assert.False(result.Success);
        Assert.Contains("left-hand side", result.Message);
    }

    // --- Increment / decrement ------------------------------------------------------------------

    [Fact]
    public async Task PostfixIncrement_YieldsOldValue_AndStoresNew()
    {
        ScriptRunResult result = await Run("""
            let x = 5
            let old = x++
            print(old, x)
            let y = 5
            let old2 = y--
            print(old2, y)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("5 6", _output.NormalText);
        Assert.Contains("5 4", _output.NormalText);
    }

    [Fact]
    public async Task PrefixIncrement_YieldsNewValue()
    {
        ScriptRunResult result = await Run("""
            let x = 5
            print(++x, x)
            let y = 5
            print(--y, y)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("6 6", _output.NormalText);
        Assert.Contains("4 4", _output.NormalText);
    }

    [Fact]
    public async Task Increment_OnArrayElement_Works()
    {
        ScriptRunResult result = await Run("""
            let a = [1, 2, 3]
            a[0]++
            ++a[2]
            print(a)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("[2, 2, 4]", _output.NormalText);
    }

    [Fact]
    public async Task Increment_InLoopCounter_Works()
    {
        ScriptRunResult result = await Run("""
            let i = 0
            let total = 0
            while i < 5 {
                total += i
                i++
            }
            print(total)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("10", _output.NormalText);
    }

    [Fact]
    public async Task Increment_OnAString_IsARuntimeError()
    {
        ScriptRunResult result = await Run("""
            let s = "abc"
            s++
            """);

        Assert.False(result.Success);
        Assert.Contains("'++' needs a number", result.Message);
    }

    [Fact]
    public async Task Increment_OnALiteral_IsASyntaxError()
    {
        ScriptRunResult result = await Run("5++");

        Assert.False(result.Success);
        Assert.Contains("left-hand side", result.Message);
    }

    [Fact]
    public async Task MinusMinus_BetweenNumbers_NoLongerParses()
    {
        // C-style lexing: 5--3 is 5 (--3), and --3 is not assignable. Write 5 - -3 instead.
        ScriptRunResult broken = await Run("print(5--3)");
        Assert.False(broken.Success);

        ScriptRunResult spaced = await Run("print(5 - -3)");
        Assert.True(spaced.Success, spaced.Message);
        Assert.Contains("8", _output.NormalText);
    }

    // --- Newline leniency -----------------------------------------------------------------------

    [Fact]
    public async Task FunctionDefinition_AllowsNewlines_AroundParensAndBrace()
    {
        ScriptRunResult result = await Run("""
            fn add
            (a, b)
            {
                return a + b
            }
            print(add(2, 3))
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("5", _output.NormalText);
    }

    [Fact]
    public async Task ControlFlow_AllowsBraceOnItsOwnLine()
    {
        ScriptRunResult result = await Run("""
            let x = 1
            if x > 0
            {
                print("if")
            }
            else
            {
                print("else")
            }
            while x < 2
            {
                x++
            }
            for v in [1]
            {
                print("for", v)
            }
            print(x)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("if", _output.NormalText);
        Assert.Contains("for 1", _output.NormalText);
        Assert.Contains("2", _output.NormalText);
    }

    [Fact]
    public async Task ElseIf_OnItsOwnLine_Chains()
    {
        ScriptRunResult result = await Run("""
            let x = 2
            if x == 1 {
                print("one")
            }
            else
            if x == 2 {
                print("two")
            }
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("two", _output.NormalText);
    }

    [Fact]
    public async Task TwoStatements_OnSeparateLines_StayTwoStatements()
    {
        // A newline at top level still separates statements: `print(1)` then `(1 + 2)`
        // must not merge into a call `print(1)(1 + 2)`.
        ScriptRunResult result = await Run("""
            print(1)
            (1 + 2)
            """);

        Assert.True(result.Success, result.Message);
    }

    // --- Destructuring let ----------------------------------------------------------------------

    [Fact]
    public async Task DestructuringLet_BindsEachElement()
    {
        ScriptRunResult result = await Run("""
            fn pair() {
                return [10, 20]
            }
            let [a, b] = pair()
            print(a, b)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("10 20", _output.NormalText);
    }

    [Fact]
    public async Task DestructuringLet_LengthMismatch_Errors()
    {
        ScriptRunResult result = await Run("let [a, b, c] = [1, 2]");

        Assert.False(result.Success);
        Assert.Contains("3 variables", result.Message);
        Assert.Contains("2 elements", result.Message);
    }

    [Fact]
    public async Task DestructuringLet_NonArray_Errors()
    {
        ScriptRunResult result = await Run("let [a, b] = 5");

        Assert.False(result.Success);
        Assert.Contains("needs an array", result.Message);
    }
}
