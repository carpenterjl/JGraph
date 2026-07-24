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
    /// True when the statement ended with a ';' (MATLAB-style echo suppression): its result is not
    /// echoed to the console. Set by the parser after the statement is built.
    /// </summary>
    public bool Suppressed { get; set; }

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

/// <summary>
/// A semicolon-rowed array literal such as <c>[1, 2; 3, 4]</c> (MATLAB matrix rows) or
/// <c>[a; b]</c> (vertical concatenation when row elements are themselves arrays).
/// </summary>
internal sealed class MatrixLiteral(IReadOnlyList<IReadOnlyList<Expr>> rows) : Expr
{
    public IReadOnlyList<IReadOnlyList<Expr>> Rows { get; } = rows;
}

/// <summary>An imaginary literal such as <c>2i</c> or <c>1.5j</c>; <see cref="Imaginary"/> is the magnitude.</summary>
internal sealed class ComplexLiteral(double imaginary) : Expr
{
    public double Imaginary { get; } = imaginary;
}

/// <summary>
/// A MATLAB colon range <c>start:stop</c> or <c>start:step:stop</c>, evaluating to an inclusive
/// arithmetic sequence. <see cref="Step"/> is null for the two-part form (step 1).
/// </summary>
internal sealed class RangeExpr(Expr start, Expr? step, Expr stop) : Expr
{
    public Expr Start { get; } = start;

    public Expr? Step { get; } = step;

    public Expr Stop { get; } = stop;
}

/// <summary>
/// MATLAB <c>end</c> used inside an index expression: the length of the array being indexed. Valid
/// only while an index argument is being evaluated (enforced at runtime).
/// </summary>
internal sealed class EndExpr : Expr
{
}

/// <summary>A lone <c>:</c> filling a whole index argument (MATLAB "all elements").</summary>
internal sealed class AllExpr : Expr
{
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

/// <summary>
/// An element access <c>target[index]</c>. Brackets take the same subscript list as a paren index
/// (a scalar, a range, a mask, ':', 'end', or several subscripts for an image) and mean the same
/// thing — the two spellings differ only in that <c>f(x)</c> calls a function and <c>f[x]</c> does not.
/// </summary>
internal sealed class IndexExpr(Expr target, IReadOnlyList<Expr> indices) : Expr
{
    public Expr Target { get; } = target;

    public IReadOnlyList<Expr> Indices { get; } = indices;
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
/// A MATLAB transpose: <c>a'</c> (<see cref="Conjugate"/> true — conjugates complex elements) or
/// <c>a.'</c> (plain).
/// </summary>
internal sealed class TransposeExpr(Expr operand, bool conjugate) : Expr
{
    public Expr Operand { get; } = operand;

    /// <summary>True for <c>'</c>, which also conjugates; false for <c>.'</c>.</summary>
    public bool Conjugate { get; } = conjugate;
}

/// <summary>
/// A MATLAB cell literal such as <c>{1, 'two'}</c> or <c>{1, 2; 3, 4}</c>. Rows mirror
/// <see cref="MatrixLiteral"/>: a single row is the common case.
/// </summary>
internal sealed class CellLiteral(IReadOnlyList<IReadOnlyList<Expr>> rows) : Expr
{
    public IReadOnlyList<IReadOnlyList<Expr>> Rows { get; } = rows;
}

/// <summary>
/// A MATLAB brace index <c>c{i}</c>: the *contents* of a cell, where <c>c(i)</c> would give a
/// one-element cell back.
/// </summary>
internal sealed class BraceIndexExpr(Expr target, IReadOnlyList<Expr> indices) : Expr
{
    public Expr Target { get; } = target;

    public IReadOnlyList<Expr> Indices { get; } = indices;
}

/// <summary>
/// A MATLAB struct field access: <c>s.name</c> (<see cref="Field"/> set) or the dynamic form
/// <c>s.(expression)</c> (<see cref="FieldName"/> set), where the field is chosen at run time.
/// </summary>
internal sealed class MemberExpr(Expr target, string? field, Expr? fieldName) : Expr
{
    public Expr Target { get; } = target;

    /// <summary>The literal field name, or null for the dynamic form.</summary>
    public string? Field { get; } = field;

    /// <summary>The expression naming the field, or null for the literal form.</summary>
    public Expr? FieldName { get; } = fieldName;
}

/// <summary>
/// A MATLAB anonymous function <c>@(x, y) expr</c>. MATLAB captures the values of the free variables
/// when the handle is created, not when it is called, so the interpreter snapshots them here.
/// </summary>
internal sealed class AnonymousFnExpr(IReadOnlyList<string> parameters, Expr body) : Expr
{
    public IReadOnlyList<string> Parameters { get; } = parameters;

    public Expr Body { get; } = body;
}

/// <summary>A MATLAB function handle <c>@name</c>, naming a user function or a builtin.</summary>
internal sealed class FunctionHandleExpr(string name) : Expr
{
    public string Name { get; } = name;
}

/// <summary>
/// A value the interpreter has already computed, wrapped so it can be handed to the ordinary
/// assignment machinery. Never produced by the parser — it exists so a multiple-output call can reuse
/// one assignment path for every target shape instead of duplicating it.
/// </summary>
internal sealed class PreEvaluated(JgsValue value) : Expr
{
    public JgsValue Value { get; } = value;
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

/// <summary>
/// A function declaration: <c>fn name(params) { ... }</c> in JGS, or MATLAB's
/// <c>function [out1, out2] = name(params) ... end</c>. A JGS function hands back the value of its
/// <c>return</c>; a MATLAB one hands back the values its named <see cref="Outputs"/> hold when it ends.
/// </summary>
internal sealed class FnStmt(
    string name,
    IReadOnlyList<string> parameters,
    IReadOnlyList<Stmt> body,
    IReadOnlyList<string>? outputs = null) : Stmt
{
    private static readonly string[] NoOutputs = [];

    public string Name { get; } = name;

    public IReadOnlyList<string> Parameters { get; } = parameters;

    public IReadOnlyList<Stmt> Body { get; } = body;

    /// <summary>The MATLAB output variable names, in order; empty for a JGS <c>fn</c>.</summary>
    public IReadOnlyList<string> Outputs { get; } = outputs ?? NoOutputs;
}

/// <summary>
/// A MATLAB multiple-output call: <c>[a, b] = size(x)</c>. A null entry in <see cref="Targets"/> is
/// MATLAB's <c>~</c> placeholder — that output is computed and discarded.
/// </summary>
internal sealed class MultiAssignStmt(IReadOnlyList<Expr?> targets, Expr call) : Stmt
{
    public IReadOnlyList<Expr?> Targets { get; } = targets;

    public Expr Call { get; } = call;
}

/// <summary>One <c>case</c> arm of a <see cref="SwitchStmt"/>.</summary>
internal sealed class SwitchCase(Expr value, IReadOnlyList<Stmt> body)
{
    /// <summary>The value to compare against, or a <see cref="CellLiteral"/> of alternatives.</summary>
    public Expr Value { get; } = value;

    public IReadOnlyList<Stmt> Body { get; } = body;
}

/// <summary>
/// A MATLAB <c>switch</c>. Arms do not fall through, and <see cref="Otherwise"/> is the <c>otherwise</c>
/// block when there is one.
/// </summary>
internal sealed class SwitchStmt(Expr subject, IReadOnlyList<SwitchCase> cases, IReadOnlyList<Stmt>? otherwise) : Stmt
{
    public Expr Subject { get; } = subject;

    public IReadOnlyList<SwitchCase> Cases { get; } = cases;

    public IReadOnlyList<Stmt>? Otherwise { get; } = otherwise;
}

/// <summary>
/// A MATLAB <c>try</c>/<c>catch</c>. <see cref="ErrorVariable"/> is the name bound to the error struct
/// (<c>catch err</c>), or null for a bare <c>catch</c>.
/// </summary>
internal sealed class TryStmt(IReadOnlyList<Stmt> body, string? errorVariable, IReadOnlyList<Stmt> handler) : Stmt
{
    public IReadOnlyList<Stmt> Body { get; } = body;

    public string? ErrorVariable { get; } = errorVariable;

    public IReadOnlyList<Stmt> Handler { get; } = handler;
}

/// <summary>A MATLAB <c>global a b</c> declaration: the named variables refer to the global scope.</summary>
internal sealed class GlobalStmt(IReadOnlyList<string> names) : Stmt
{
    public IReadOnlyList<string> Names { get; } = names;
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
