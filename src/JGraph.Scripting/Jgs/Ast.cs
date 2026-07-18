namespace JGraph.Scripting.Jgs;

/// <summary>Base of every JGS syntax-tree node; carries the 1-based source position for error reporting.</summary>
internal abstract class Node
{
    /// <summary>The 1-based line where this node begins.</summary>
    public int Line { get; init; }

    /// <summary>The 1-based column where this node begins.</summary>
    public int Column { get; init; }
}

/// <summary>Base of every expression node.</summary>
internal abstract class Expr : Node
{
}

/// <summary>Base of every statement node.</summary>
internal abstract class Stmt : Node
{
    /// <summary>
    /// Identifies the source (file path or "") the statement was parsed from, so the debugger can map
    /// breakpoints, the current-line marker, and stack frames onto the right document when a program
    /// spans multiple files via <c>run()</c>. One shared string reference per file, stamped by the
    /// parser — statement-level only; expressions never need file identity.
    /// </summary>
    public string SourceId { get; set; } = "";
}

// --- Expressions ----------------------------------------------------------------------------------

/// <summary>A numeric literal such as <c>3.14</c>.</summary>
internal sealed class NumberLiteral(double value) : Expr
{
    public double Value { get; } = value;
}

/// <summary>A string literal such as <c>"hello"</c>.</summary>
internal sealed class StringLiteral(string value) : Expr
{
    public string Value { get; } = value;
}

/// <summary>A boolean literal (<c>true</c>/<c>false</c>).</summary>
internal sealed class BoolLiteral(bool value) : Expr
{
    public bool Value { get; } = value;
}

/// <summary>An array literal such as <c>[1, 2, 3]</c>.</summary>
internal sealed class ArrayLiteral(IReadOnlyList<Expr> elements) : Expr
{
    public IReadOnlyList<Expr> Elements { get; } = elements;
}

/// <summary>A reference to a variable or built-in by name.</summary>
internal sealed class VariableExpr(string name) : Expr
{
    public string Name { get; } = name;
}

/// <summary>A unary operation (<c>-x</c>, <c>!flag</c>).</summary>
internal sealed class UnaryExpr(TokenType op, Expr operand) : Expr
{
    public TokenType Op { get; } = op;

    public Expr Operand { get; } = operand;
}

/// <summary>A binary arithmetic or comparison operation.</summary>
internal sealed class BinaryExpr(TokenType op, Expr left, Expr right) : Expr
{
    public TokenType Op { get; } = op;

    public Expr Left { get; } = left;

    public Expr Right { get; } = right;
}

/// <summary>A short-circuiting logical operation (<c>&amp;&amp;</c>, <c>||</c>).</summary>
internal sealed class LogicalExpr(TokenType op, Expr left, Expr right) : Expr
{
    public TokenType Op { get; } = op;

    public Expr Left { get; } = left;

    public Expr Right { get; } = right;
}

/// <summary>A function call <c>callee(arg, ...)</c>.</summary>
internal sealed class CallExpr(Expr callee, IReadOnlyList<Expr> arguments) : Expr
{
    public Expr Callee { get; } = callee;

    public IReadOnlyList<Expr> Arguments { get; } = arguments;
}

/// <summary>An element access <c>target[index]</c>.</summary>
internal sealed class IndexExpr(Expr target, Expr index) : Expr
{
    public Expr Target { get; } = target;

    public Expr Index { get; } = index;
}

/// <summary>
/// An assignment expression <c>target = value</c> or compound form (<c>+=</c>, <c>-=</c>, <c>*=</c>,
/// <c>/=</c>, <c>%=</c>). <see cref="Target"/> is a <see cref="VariableExpr"/> or <see cref="IndexExpr"/>
/// (the parser enforces this); the expression evaluates to the stored value.
/// </summary>
internal sealed class AssignExpr(Expr target, TokenType op, Expr value) : Expr
{
    public Expr Target { get; } = target;

    /// <summary>Assign for plain <c>=</c>, or PlusAssign/MinusAssign/StarAssign/SlashAssign/PercentAssign.</summary>
    public TokenType Op { get; } = op;

    public Expr Value { get; } = value;
}

/// <summary>
/// An increment or decrement, prefix (<c>++x</c>, evaluates to the new value) or postfix
/// (<c>x++</c>, evaluates to the old value). <see cref="Target"/> is a <see cref="VariableExpr"/>
/// or <see cref="IndexExpr"/>.
/// </summary>
internal sealed class IncDecExpr(Expr target, bool increment, bool prefix) : Expr
{
    public Expr Target { get; } = target;

    /// <summary>True for <c>++</c>, false for <c>--</c>.</summary>
    public bool Increment { get; } = increment;

    /// <summary>True for the prefix form.</summary>
    public bool Prefix { get; } = prefix;
}

// --- Statements -----------------------------------------------------------------------------------

/// <summary>A variable declaration <c>let name = value</c>.</summary>
internal sealed class LetStmt(string name, Expr value) : Stmt
{
    public string Name { get; } = name;

    public Expr Value { get; } = value;
}

/// <summary>
/// A destructuring declaration <c>let [a, b] = value</c>: the value must be an array whose length
/// matches the name count; each element is bound to the corresponding name.
/// </summary>
internal sealed class DestructuringLetStmt(IReadOnlyList<string> names, Expr value) : Stmt
{
    public IReadOnlyList<string> Names { get; } = names;

    public Expr Value { get; } = value;
}

/// <summary>An expression evaluated for its effect (e.g. a plotting call or an assignment).</summary>
internal sealed class ExprStmt(Expr expression) : Stmt
{
    public Expr Expression { get; } = expression;
}

/// <summary>An <c>if</c>/<c>else</c> statement; <see cref="Else"/> is null when there is no else branch.</summary>
internal sealed class IfStmt(Expr condition, IReadOnlyList<Stmt> then, IReadOnlyList<Stmt>? @else) : Stmt
{
    public Expr Condition { get; } = condition;

    public IReadOnlyList<Stmt> Then { get; } = then;

    public IReadOnlyList<Stmt>? Else { get; } = @else;
}

/// <summary>A <c>while</c> loop.</summary>
internal sealed class WhileStmt(Expr condition, IReadOnlyList<Stmt> body) : Stmt
{
    public Expr Condition { get; } = condition;

    public IReadOnlyList<Stmt> Body { get; } = body;
}

/// <summary>A <c>for variable in iterable { ... }</c> loop over an array.</summary>
internal sealed class ForStmt(string variable, Expr iterable, IReadOnlyList<Stmt> body) : Stmt
{
    public string Variable { get; } = variable;

    public Expr Iterable { get; } = iterable;

    public IReadOnlyList<Stmt> Body { get; } = body;
}

/// <summary>A function declaration <c>fn name(params) { ... }</c>.</summary>
internal sealed class FnStmt(string name, IReadOnlyList<string> parameters, IReadOnlyList<Stmt> body) : Stmt
{
    public string Name { get; } = name;

    public IReadOnlyList<string> Parameters { get; } = parameters;

    public IReadOnlyList<Stmt> Body { get; } = body;
}

/// <summary>A <c>return</c> statement; <see cref="Value"/> is null for a bare <c>return</c>.</summary>
internal sealed class ReturnStmt(Expr? value) : Stmt
{
    public Expr? Value { get; } = value;
}

/// <summary>A <c>break</c> statement.</summary>
internal sealed class BreakStmt : Stmt
{
}

/// <summary>A <c>continue</c> statement.</summary>
internal sealed class ContinueStmt : Stmt
{
}
