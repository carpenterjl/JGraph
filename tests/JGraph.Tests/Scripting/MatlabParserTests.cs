using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M28.S3: parsing MATLAB. These tests assert shapes, not results — the interpreter comes later — and
/// concentrate on the constructs JGS has no equivalent for: function declarations with output lists,
/// multiple-output calls, switch/try, cells and structs, function handles, command syntax, and the
/// space-separated matrix literal whose meaning depends on where the spaces are.
/// </summary>
public class MatlabParserTests
{
    private static IReadOnlyList<Stmt> Parse(string source) =>
        Parser.Parse(source, sourceId: "", JgsDialect.Matlab);

    private static Stmt ParseOne(string source) => Assert.Single(Parse(source));

    private static Expr Expression(string source) =>
        Assert.IsType<ExprStmt>(ParseOne(source)).Expression;

    // --- Matrix and cell literals ---------------------------------------------------------------

    [Fact]
    public void SpaceSeparatedElements_MakeAnArray()
    {
        var array = Assert.IsType<ArrayLiteral>(Expression("[1 2 3]"));
        Assert.Equal(3, array.Elements.Count);
    }

    [Fact]
    public void Spacing_DecidesBetweenASignAndASubtraction()
    {
        // '[1 -2]' is two elements; '[1 - 2]' is one subtraction; '[1-2]' likewise.
        Assert.Equal(2, Assert.IsType<ArrayLiteral>(Expression("[1 -2]")).Elements.Count);
        Assert.Single(Assert.IsType<ArrayLiteral>(Expression("[1 - 2]")).Elements);
        Assert.Single(Assert.IsType<ArrayLiteral>(Expression("[1-2]")).Elements);
    }

    [Fact]
    public void SemicolonsAndLineBreaks_BothMakeRows()
    {
        var bySemicolon = Assert.IsType<MatrixLiteral>(Expression("[1 2; 3 4]"));
        Assert.Equal(2, bySemicolon.Rows.Count);
        Assert.Equal(2, bySemicolon.Rows[0].Count);

        var byLineBreak = Assert.IsType<MatrixLiteral>(Expression("[1 2\n3 4]"));
        Assert.Equal(2, byLineBreak.Rows.Count);
        Assert.Equal(2, byLineBreak.Rows[1].Count);
    }

    [Fact]
    public void BracketsAfterAValue_Concatenate_TheyDoNotIndex()
    {
        // In MATLAB '[a [1 2]]' is concatenation. Reading the inner bracket as an index of 'a' would
        // silently change what the script computes.
        var outer = Assert.IsType<ArrayLiteral>(Expression("[a [1 2]]"));
        Assert.Equal(2, outer.Elements.Count);
        Assert.IsType<VariableExpr>(outer.Elements[0]);
        Assert.IsType<ArrayLiteral>(outer.Elements[1]);
    }

    [Fact]
    public void Braces_BuildACellLiteral_AndIndexOne()
    {
        var cell = Assert.IsType<CellLiteral>(Expression("{1, 'two', [3 4]}"));
        Assert.Single(cell.Rows);
        Assert.Equal(3, cell.Rows[0].Count);

        var brace = Assert.IsType<BraceIndexExpr>(Expression("c{2}"));
        Assert.IsType<VariableExpr>(brace.Target);
        Assert.Single(brace.Indices);
    }

    // --- Fields, transpose, handles -------------------------------------------------------------

    [Fact]
    public void FieldAccess_ParsesBothForms()
    {
        var literal = Assert.IsType<MemberExpr>(Expression("s.count"));
        Assert.Equal("count", literal.Field);

        var dynamic = Assert.IsType<MemberExpr>(Expression("s.('count')"));
        Assert.Null(dynamic.Field);
        Assert.IsType<StringLiteral>(dynamic.FieldName);

        // Chains work: s.inner.value, and a field of an indexed thing.
        Assert.IsType<MemberExpr>(Assert.IsType<MemberExpr>(Expression("s.inner.value")).Target);
        Assert.IsType<CallExpr>(Assert.IsType<MemberExpr>(Expression("f(1).value")).Target);
    }

    [Fact]
    public void Transpose_IsPostfix_AndDistinguishesTheTwoForms()
    {
        Assert.True(Assert.IsType<TransposeExpr>(Expression("a'")).Conjugate);
        Assert.False(Assert.IsType<TransposeExpr>(Expression("a.'")).Conjugate);

        // The column-vector idiom.
        var column = Assert.IsType<TransposeExpr>(Expression("(0:0.1:1)'"));
        Assert.IsType<RangeExpr>(column.Operand);
    }

    [Fact]
    public void FunctionHandles_AndAnonymousFunctions_Parse()
    {
        Assert.Equal("sin", Assert.IsType<FunctionHandleExpr>(Expression("@sin")).Name);

        var anonymous = Assert.IsType<AnonymousFnExpr>(Expression("@(x, y) x.^2 + y"));
        Assert.Equal(new[] { "x", "y" }, anonymous.Parameters);
        Assert.IsType<BinaryExpr>(anonymous.Body);
    }

    [Fact]
    public void ElementwiseLogicals_BindTighterThanTheShortCircuitingOnes()
    {
        // a && b | c parses as a && (b | c), matching MATLAB's precedence.
        var top = Assert.IsType<LogicalExpr>(Expression("a && b | c"));
        Assert.Equal(TokenType.AmpAmp, top.Op);
        Assert.Equal(TokenType.Pipe, Assert.IsType<BinaryExpr>(top.Right).Op);

        // ... and '&' binds tighter than '|'.
        var or = Assert.IsType<BinaryExpr>(Expression("a | b & c"));
        Assert.Equal(TokenType.Pipe, or.Op);
        Assert.Equal(TokenType.Amp, Assert.IsType<BinaryExpr>(or.Right).Op);
    }

    // --- Statements -----------------------------------------------------------------------------

    [Fact]
    public void FunctionDeclaration_ReadsOutputsAndParameters()
    {
        var fn = Assert.IsType<FnStmt>(ParseOne("""
            function [total, count] = tally(values, weight)
                total = sum(values) * weight;
                count = numel(values);
            end
            """));

        Assert.Equal("tally", fn.Name);
        Assert.Equal(new[] { "total", "count" }, fn.Outputs);
        Assert.Equal(new[] { "values", "weight" }, fn.Parameters);
        Assert.Equal(2, fn.Body.Count);
    }

    [Fact]
    public void FunctionDeclaration_AcceptsTheSingleOutputAndNoOutputForms()
    {
        var single = Assert.IsType<FnStmt>(ParseOne("function y = double(x)\n  y = 2*x;\nend"));
        Assert.Equal(new[] { "y" }, single.Outputs);
        Assert.Equal("double", single.Name);

        var none = Assert.IsType<FnStmt>(ParseOne("function report(x)\n  disp(x);\nend"));
        Assert.Empty(none.Outputs);
        Assert.Equal(new[] { "x" }, none.Parameters);
    }

    [Fact]
    public void FunctionFile_WithoutEnds_ParsesEachFunction()
    {
        // The classic style: a script's helpers written without a closing 'end'.
        IReadOnlyList<Stmt> program = Parse("""
            x = helper(2);

            function y = helper(v)
            y = v + 1;

            function z = other(v)
            z = v - 1;
            """);

        Assert.Equal(3, program.Count);
        Assert.Equal("helper", Assert.IsType<FnStmt>(program[1]).Name);
        var other = Assert.IsType<FnStmt>(program[2]);
        Assert.Equal("other", other.Name);
        Assert.Single(other.Body);
    }

    [Fact]
    public void MultipleOutputs_ParseAsAMultiAssign()
    {
        var multi = Assert.IsType<MultiAssignStmt>(ParseOne("[value, index] = max(x);"));
        Assert.Equal(2, multi.Targets.Count);
        Assert.IsType<CallExpr>(multi.Call);

        // '~' discards an output.
        var discarded = Assert.IsType<MultiAssignStmt>(ParseOne("[~, index] = max(x);"));
        Assert.Null(discarded.Targets[0]);
        Assert.NotNull(discarded.Targets[1]);
    }

    [Fact]
    public void ABracketedStatement_ThatIsNotAnAssignment_IsStillALiteral()
    {
        Assert.IsType<ExprStmt>(ParseOne("[1 2 3]"));
        Assert.IsType<ExprStmt>(ParseOne("[a == b]"));
    }

    [Fact]
    public void Switch_ReadsItsArms()
    {
        var block = Assert.IsType<SwitchStmt>(ParseOne("""
            switch mode
                case 'fast'
                    step = 1;
                case {'slow', 'careful'}
                    step = 10;
                otherwise
                    step = 5;
            end
            """));

        Assert.Equal(2, block.Cases.Count);
        Assert.IsType<StringLiteral>(block.Cases[0].Value);
        Assert.IsType<CellLiteral>(block.Cases[1].Value);
        Assert.NotNull(block.Otherwise);
    }

    [Fact]
    public void TryCatch_ReadsTheErrorVariable_WhenThereIsOne()
    {
        var named = Assert.IsType<TryStmt>(ParseOne("try\n  risky();\ncatch err\n  disp(err);\nend"));
        Assert.Equal("err", named.ErrorVariable);
        Assert.Single(named.Body);
        Assert.Single(named.Handler);

        var bare = Assert.IsType<TryStmt>(ParseOne("try\n  risky();\ncatch\n  disp('failed');\nend"));
        Assert.Null(bare.ErrorVariable);
        Assert.Single(bare.Handler);
    }

    [Fact]
    public void Global_ListsItsNames()
    {
        Assert.Equal(new[] { "a", "b" }, Assert.IsType<GlobalStmt>(ParseOne("global a b")).Names);
        Assert.Equal(new[] { "a", "b" }, Assert.IsType<GlobalStmt>(ParseOne("global a, b")).Names);
    }

    [Fact]
    public void CommandSyntax_BecomesACallWithStringArguments()
    {
        var call = Assert.IsType<CallExpr>(Assert.IsType<ExprStmt>(ParseOne("hold on")).Expression);
        Assert.Equal("hold", Assert.IsType<VariableExpr>(call.Callee).Name);
        Assert.Equal("on", Assert.IsType<StringLiteral>(Assert.Single(call.Arguments)).Value);

        var twoWords = Assert.IsType<CallExpr>(Assert.IsType<ExprStmt>(ParseOne("axis equal")).Expression);
        Assert.Single(twoWords.Arguments);
    }

    [Fact]
    public void AnExpressionStatement_IsNotMistakenForCommandSyntax()
    {
        // Anything an expression can continue with rules command syntax out.
        Assert.IsType<AssignExpr>(Assert.IsType<ExprStmt>(ParseOne("x = 1")).Expression);
        Assert.IsType<BinaryExpr>(Assert.IsType<ExprStmt>(ParseOne("a + b")).Expression);
        Assert.IsType<CallExpr>(Assert.IsType<ExprStmt>(ParseOne("disp(x)")).Expression);
        Assert.IsType<TransposeExpr>(Assert.IsType<ExprStmt>(ParseOne("a'")).Expression);
    }

    [Fact]
    public void ControlFlow_UsesMatlabBlocksWithoutLet()
    {
        var loop = Assert.IsType<ForStmt>(ParseOne("for k = 1:10\n  total = total + k;\nend"));
        Assert.Equal("k", loop.Variable);
        Assert.Single(loop.Body);

        var conditional = Assert.IsType<IfStmt>(ParseOne("if a > 1\n  b = 2;\nelseif a > 0\n  b = 1;\nelse\n  b = 0;\nend"));
        Assert.NotNull(conditional.Else);
    }

    [Fact]
    public void UnsupportedConstructs_AreNamedInTheError()
    {
        JgsSyntaxException error = Assert.Throws<JgsSyntaxException>(
            static () => Parse("classdef Widget\nend"));
        Assert.Contains("classdef", error.Message, StringComparison.Ordinal);
        Assert.Contains("not supported", error.Message, StringComparison.Ordinal);

        Assert.Contains("parfor", Assert.Throws<JgsSyntaxException>(
            static () => Parse("parfor k = 1:3\nend")).Message, StringComparison.Ordinal);
    }

    // --- JGS is untouched -----------------------------------------------------------------------

    [Fact]
    public void JgsParsing_IsUnchanged()
    {
        IReadOnlyList<Stmt> program = Parser.Parse("""
            let a = [1, 2, 3]
            let b = a[0]
            if a[0] > 0 { print(b) }
            fn double(x) { return x * 2 }
            """);

        Assert.Equal(4, program.Count);
        Assert.IsType<LetStmt>(program[0]);
        Assert.IsType<IndexExpr>(Assert.IsType<LetStmt>(program[1]).Value);
        Assert.IsType<IfStmt>(program[2]);
        Assert.Empty(Assert.IsType<FnStmt>(program[3]).Outputs);

        // A space-separated array literal is still a JGS syntax error, not two elements.
        Assert.Throws<JgsSyntaxException>(static () => Parser.Parse("let a = [1 2]"));
    }
}
