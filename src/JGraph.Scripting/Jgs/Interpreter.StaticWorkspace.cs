namespace JGraph.Scripting.Jgs;

/// <summary>
/// R2025b's static workspaces (open item 350, ADR 0385, measured): a function that has nested functions, and each
/// of its nested functions, decides its variables when its file is read, so <c>eval</c>, <c>evalc</c>,
/// <c>evalin</c> and <c>assignin</c> cannot add a name to its workspace that its text never binds:
/// <c>MATLAB:err_static_workspace_violation</c>, "Attempt to add "pi" to a static workspace.". A variable it
/// already holds, or one any of the enclosing functions' text binds, may be written. An anonymous function's
/// workspace is static too.
/// </summary>
internal sealed partial class Interpreter
{
    /// <summary>The names an eval'd text binds at its top level: assignment roots, multiple-assignment targets, loop variables.</summary>
    private static List<string> EvalTextBinds(IReadOnlyList<Stmt> program)
    {
        var names = new List<string>();
        foreach (Stmt statement in program)
        {
            switch (statement)
            {
                case ExprStmt { Expression: AssignExpr assign } when TargetRootName(assign.Target) is { } root:
                    names.Add(root);
                    break;
                case MultiAssignStmt multi:
                    foreach (Expr? target in multi.Targets)
                    {
                        if (target is not null && TargetRootName(target) is { } each)
                        {
                            names.Add(each);
                        }
                    }

                    break;
                case ForStmt loop:
                    names.Add(loop.Variable);
                    break;
            }
        }

        return names;
    }

    /// <summary>Refuses eval'd text that binds a name the static workspace it runs in cannot take (MATLAB dialect only).</summary>
    private void RefuseStaticWorkspaceAdds(IReadOnlyList<Stmt> program, JgsEnvironment env, int line, int column)
    {
        if (!Dialect.IsMatlab)
        {
            return;
        }

        foreach (string name in EvalTextBinds(program))
        {
            if (AddsToStaticWorkspace(env, name))
            {
                throw StaticWorkspaceViolation(name, line, column);
            }
        }
    }

    /// <summary>R2025b's refusal of a name added to a static workspace.</summary>
    internal static JgsRuntimeException StaticWorkspaceViolation(string name, int line, int column) =>
        new(line, column, "MATLAB:err_static_workspace_violation", $"Attempt to add \"{name}\" to a static workspace.");

    /// <summary>
    /// Whether binding <paramref name="name"/> in <paramref name="env"/> would add it to a static workspace: an
    /// anonymous function's that did not capture it, or a function frame (the frames of a function with nested
    /// functions) that holds no such variable and whose functions' text never binds it.
    /// </summary>
    internal static bool AddsToStaticWorkspace(JgsEnvironment env, string name)
    {
        if (env.IsStaticWorkspace)
        {
            return !env.IsBoundWithinStaticWorkspace(name);
        }

        bool isStatic = false;
        for (JgsEnvironment? scope = env; scope is not null && !scope.IsBuiltinLayer; scope = scope.Parent)
        {
            if (scope.BindsLocally(name))
            {
                return false;
            }

            if (scope.Function is { } function)
            {
                if (function.NestedIn is not null || function.Body.Any(static s => s is FnStmt))
                {
                    isStatic = true;
                }

                if (function.Binds(name))
                {
                    return false;
                }
            }

            if (scope.IsCallBoundary)
            {
                break;
            }
        }

        return isStatic;
    }
}
