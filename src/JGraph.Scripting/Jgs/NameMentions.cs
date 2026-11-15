namespace JGraph.Scripting.Jgs;

/// <summary>
/// The variable names a function's own body mentions - reads, assignment targets, loop variables,
/// declared globals and persistents, its parameters and outputs, and what its anonymous functions
/// capture - and not what the functions nested inside it mention, which is theirs (V7, ADR 0168).
/// </summary>
/// <remarks>
/// MATLAB shares a variable between a nested function and its parent when both use the name, and
/// the variable belongs to the outermost function that mentions it; a name only the nested
/// function uses is its own and fresh each call. R2025b was measured before this was written: a
/// parent that reads a name only a nested function assigns is refused at parse time, so the rule
/// decides only where a nested function's write of an unbound name lands - in the outermost
/// enclosing frame whose function mentions it (a <c>clear v</c> in the nested function, then
/// <c>v = [7 8]</c>, leaves the parent's <c>v</c> at <c>[7 8]</c>), or in the nested frame itself.
/// A command-syntax word (<c>clear v</c>) is a string and not a mention, as it is in MATLAB.
/// Computed once per declaration and cached on it.
/// </remarks>
internal static class NameMentions
{
    /// <summary>Whether <paramref name="function"/>'s own body mentions <paramref name="name"/>.</summary>
    public static bool Mentions(this FnStmt function, string name) =>
        (function.MentionedNames ??= Collect(function)).Contains(name);

    /// <summary>The variable names <paramref name="statements"/> mention, in the order first met (eval'd text; open item 336).</summary>
    public static IEnumerable<string> MentionedIn(IReadOnlyList<Stmt> statements)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        Visit(statements, names);
        return names;
    }

    /// <summary>
    /// Whether <paramref name="function"/>'s own text binds <paramref name="name"/> as a variable: a parameter,
    /// an output, an assignment's or a loop's target, a declared global or persistent, a caught exception
    /// (open item 336, ADR 0378: a name it mentions and never binds is read as a function, whatever an
    /// <c>eval</c> puts there).
    /// </summary>
    public static bool Binds(this FnStmt function, string name)
    {
        if (function.BoundNames is null)
        {
            var bound = new HashSet<string>(function.Parameters, StringComparer.Ordinal);
            bound.UnionWith(function.Outputs);
            CollectBound(function.Body, bound);
            function.BoundNames = bound;
        }

        return function.BoundNames.Contains(name);
    }

    private static void CollectBound(IReadOnlyList<Stmt>? statements, HashSet<string> bound)
    {
        foreach (Stmt statement in statements ?? [])
        {
            switch (statement)
            {
                case ExprStmt { Expression: AssignExpr assign }:
                    AddRoot(assign.Target, bound);
                    break;
                case MultiAssignStmt multi:
                    foreach (Expr? target in multi.Targets)
                    {
                        AddRoot(target, bound);
                    }

                    break;
                case ForStmt loop:
                    bound.Add(loop.Variable);
                    CollectBound(loop.Body, bound);
                    break;
                case IfStmt branch:
                    CollectBound(branch.Then, bound);
                    CollectBound(branch.Else, bound);
                    break;
                case WhileStmt loop:
                    CollectBound(loop.Body, bound);
                    break;
                case SwitchStmt choice:
                    foreach (SwitchCase arm in choice.Cases)
                    {
                        CollectBound(arm.Body, bound);
                    }

                    CollectBound(choice.Otherwise, bound);
                    break;
                case TryStmt attempt:
                    CollectBound(attempt.Body, bound);
                    if (attempt.ErrorVariable is { } caught)
                    {
                        bound.Add(caught);
                    }

                    CollectBound(attempt.Handler, bound);
                    break;
                case GlobalStmt global:
                    bound.UnionWith(global.Names);
                    break;
                case PersistentStmt persistent:
                    bound.UnionWith(persistent.Names);
                    break;
            }
        }
    }

    private static void AddRoot(Expr? target, HashSet<string> bound)
    {
        while (true)
        {
            switch (target)
            {
                case VariableExpr variable:
                    bound.Add(variable.Name);
                    return;
                case CallExpr call:
                    target = call.Callee;
                    continue;
                case IndexExpr index:
                    target = index.Target;
                    continue;
                case BraceIndexExpr brace:
                    target = brace.Target;
                    continue;
                case MemberExpr member:
                    target = member.Target;
                    continue;
                default:
                    return;
            }
        }
    }

    private static HashSet<string> Collect(FnStmt function)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string parameter in function.Parameters)
        {
            names.Add(parameter);
        }

        foreach (string output in function.Outputs)
        {
            names.Add(output);
        }

        Visit(function.Body, names);
        return names;
    }

    private static void Visit(IReadOnlyList<Stmt> statements, HashSet<string> names)
    {
        foreach (Stmt statement in statements)
        {
            Visit(statement, names);
        }
    }

    private static void Visit(Stmt statement, HashSet<string> names)
    {
        switch (statement)
        {
            case FnStmt:
                break; // a nested function's mentions are its own
            case LetStmt let:
                names.Add(let.Name);
                Visit(let.Value, names);
                break;
            case DestructuringLetStmt destructuring:
                names.UnionWith(destructuring.Names);
                Visit(destructuring.Value, names);
                break;
            case ExprStmt expression:
                Visit(expression.Expression, names);
                break;
            case IfStmt ifStmt:
                Visit(ifStmt.Condition, names);
                Visit(ifStmt.Then, names);
                if (ifStmt.Else is not null) Visit(ifStmt.Else, names);
                break;
            case WhileStmt whileStmt:
                Visit(whileStmt.Condition, names);
                Visit(whileStmt.Body, names);
                break;
            case ForStmt forStmt:
                names.Add(forStmt.Variable);
                Visit(forStmt.Iterable, names);
                Visit(forStmt.Body, names);
                break;
            case MultiAssignStmt multi:
                foreach (Expr? target in multi.Targets)
                {
                    if (target is not null) Visit(target, names);
                }

                Visit(multi.Call, names);
                break;
            case SwitchStmt switchStmt:
                Visit(switchStmt.Subject, names);
                foreach (SwitchCase arm in switchStmt.Cases)
                {
                    Visit(arm.Value, names);
                    Visit(arm.Body, names);
                }

                if (switchStmt.Otherwise is not null) Visit(switchStmt.Otherwise, names);
                break;
            case TryStmt tryStmt:
                Visit(tryStmt.Body, names);
                if (tryStmt.ErrorVariable is { } errorVariable) names.Add(errorVariable);
                Visit(tryStmt.Handler, names);
                break;
            case GlobalStmt global:
                names.UnionWith(global.Names);
                break;
            case PersistentStmt persistent:
                names.UnionWith(persistent.Names);
                break;
            case ArgumentsStmt arguments:
                foreach (ArgumentSpec spec in arguments.Arguments)
                {
                    names.Add(spec.Variable);
                    if (spec.Default is not null) Visit(spec.Default, names);
                    foreach (Expr validator in spec.Validators) Visit(validator, names);
                }

                break;
            case ReturnStmt ret:
                if (ret.Value is not null) Visit(ret.Value, names);
                break;
            default:
                break; // break, continue, classdef: nothing a variable name rides on
        }
    }

    private static void Visit(Expr expression, HashSet<string> names)
    {
        switch (expression)
        {
            case VariableExpr variable:
                names.Add(variable.Name);
                break;
            case ArrayLiteral array:
                foreach (Expr element in array.Elements) Visit(element, names);
                break;
            case MatrixLiteral matrix:
                foreach (IReadOnlyList<Expr> row in matrix.Rows)
                {
                    foreach (Expr element in row) Visit(element, names);
                }

                break;
            case CellLiteral cell:
                foreach (IReadOnlyList<Expr> row in cell.Rows)
                {
                    foreach (Expr element in row) Visit(element, names);
                }

                break;
            case RangeExpr range:
                Visit(range.Start, names);
                if (range.Step is not null) Visit(range.Step, names);
                Visit(range.Stop, names);
                break;
            case UnaryExpr unary:
                Visit(unary.Operand, names);
                break;
            case BinaryExpr binary:
                Visit(binary.Left, names);
                Visit(binary.Right, names);
                break;
            case LogicalExpr logical:
                Visit(logical.Left, names);
                Visit(logical.Right, names);
                break;
            case CallExpr call:
                Visit(call.Callee, names);
                foreach (Expr argument in call.Arguments) Visit(argument, names);
                break;
            case IndexExpr index:
                Visit(index.Target, names);
                foreach (Expr subscript in index.Indices) Visit(subscript, names);
                break;
            case BraceIndexExpr brace:
                Visit(brace.Target, names);
                foreach (Expr subscript in brace.Indices) Visit(subscript, names);
                break;
            case AssignExpr assign:
                Visit(assign.Target, names);
                Visit(assign.Value, names);
                break;
            case TransposeExpr transpose:
                Visit(transpose.Operand, names);
                break;
            case MemberExpr member:
                Visit(member.Target, names);
                if (member.FieldName is not null) Visit(member.FieldName, names);
                break;
            case AnonymousFnExpr anonymous:
                // The body's free names are captured from the workspace, which is a use of them;
                // its own parameters are its own, and taking them out again would hide a parent
                // variable of the same name, so they stay in.
                Visit(anonymous.Body, names);
                break;
            case IncDecExpr incDec:
                Visit(incDec.Target, names);
                break;
            default:
                break; // literals, end, :, @name, a pre-evaluated value
        }
    }
}
