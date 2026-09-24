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
                    names.Add(spec.Name);
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
