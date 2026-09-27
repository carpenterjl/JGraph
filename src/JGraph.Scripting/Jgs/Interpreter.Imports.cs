using JGraph.Scripting.Jgs.Net;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>import</c> for .NET names (interop plan, stage 3, ADR 0176): which scope an import belongs to,
/// what an imported name reaches, and the refusals R2025b makes when it parses a file.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope.</b> A function's imports are its own: collected from its body when it is entered, so
/// they apply wherever they are written, and seen by its nested functions, its anonymous functions
/// and its <c>eval</c> — each of which runs in a scope under the function's frame — and by nothing
/// it calls (the frame is a call boundary, and the walk stops there). The base workspace and a
/// script import as their statements run.
/// </para>
/// <para>
/// <b>Precedence</b> is the one the M145 plan's table gives: an explicit import (<c>import
/// System.Math.Max</c>) answers after the variables and before nested and local functions; a
/// wildcard (<c>import System.*</c>) after local functions and before everything else, which is
/// what makes <c>plot</c> under <c>import JGTest.*</c> construct <c>JGTest.plot</c> (net_import).
/// Between two wildcards the later one wins. A wildcard over a namespace reaches its types, and its
/// namespaces only in front of a dot — <c>max.Thing()</c> is the namespace <c>JGTest.max</c> and
/// <c>max([1 3])</c> is still MATLAB's <c>max</c>; a wildcard over a type reaches its static
/// methods alone.
/// </para>
/// </remarks>
internal sealed partial class Interpreter
{
    /// <summary>Whether any scope of this session has imported a name, so a lookup that finds nothing else asks.</summary>
    internal bool AnyImports { get; private set; }

    /// <summary>
    /// Whether the code running was typed at the prompt, the one place <c>clear import</c> is allowed
    /// (R2025b refuses it in a function, in a script and through <c>evalin('base', …)</c>; probe3).
    /// </summary>
    internal bool AtPrompt { get; set; }

    /// <summary>The import scopes <paramref name="env"/> sees, nearest first, up to its call boundary.</summary>
    private static IEnumerable<NetImports> ImportScopes(JgsEnvironment env)
    {
        for (JgsEnvironment? scope = env; scope is not null && !scope.IsBuiltinLayer; scope = scope.Parent)
        {
            if (scope.Imports is { } imports)
            {
                yield return imports;
            }

            if (scope.IsCallBoundary)
            {
                yield break;
            }
        }
    }

    /// <summary>
    /// What <paramref name="name"/> reaches through the imports <paramref name="env"/> sees, or null.
    /// An explicit import anywhere in reach wins over any wildcard; among wildcards, the nearest
    /// scope's latest. <paramref name="head"/> is true for the name in front of a dot, where a
    /// namespace answers too.
    /// </summary>
    internal NetImported? ImportFor(string name, JgsEnvironment env, bool head)
    {
        if (!AnyImports)
        {
            return null;
        }

        NetImported? wildcard = null;
        foreach (NetImports scope in ImportScopes(env))
        {
            foreach (string entry in scope.Resolving.Reverse())
            {
                if (!NetImports.IsWildcard(entry))
                {
                    if (NetImports.ShortName(entry) == name && ExplicitTarget(entry) is { } hit)
                    {
                        AnyNet = true;
                        return hit;
                    }
                }
                else if (wildcard is null && WildcardTarget(entry[..^2], name, head) is { } reached)
                {
                    wildcard = reached;
                }
            }
        }

        if (wildcard is not null)
        {
            AnyNet = true;
        }

        return wildcard;
    }

    /// <summary>The value an imported name stands for when it is called or mentioned: a constructor or a method group.</summary>
    internal JgsValue? ImportedValue(NetImported imported) => imported switch
    {
        { Method: { } method, Type: { } type } => JgsValue.Function(new NetCallable(type, method, receiver: null, NetTypes)),
        { Type: { } type } => JgsValue.Function(NetCallable.Constructor(type, NetTypes)),
        _ => null,
    };

    /// <summary>An explicit import's target: a type named in full, or a static method of one.</summary>
    private NetImported? ExplicitTarget(string entry)
    {
        if (NetTypes.TypeNamed(entry) is { } type)
        {
            return new NetImported(true, type, null, null);
        }

        int dot = entry.LastIndexOf('.');
        return dot > 0 && NetTypes.TypeNamed(entry[..dot]) is { } owner
            && NetInvoke.HasMethod(owner, entry[(dot + 1)..], instance: false)
            ? new NetImported(true, owner, entry[(dot + 1)..], null)
            : null;
    }

    /// <summary>What <c>prefix.*</c> gives <paramref name="name"/>: a type's static method, or a namespace's type or namespace.</summary>
    private NetImported? WildcardTarget(string prefix, string name, bool head)
    {
        if (NetTypes.TypeNamed(prefix) is { } owner)
        {
            return NetInvoke.HasMethod(owner, name, instance: false) ? new NetImported(false, owner, name, null) : null;
        }

        string full = prefix + "." + name;
        if (NetTypes.TypeNamed(full) is { } type)
        {
            return new NetImported(false, type, null, null);
        }

        return head && NetTypes.IsNamespace(full) ? new NetImported(false, null, null, full) : null;
    }

    /// <summary>
    /// Enters a function's imports into its frame, refusing what R2025b refuses when it parses the
    /// file: an explicit import that names no type or static method, and a variable of the function
    /// named like an explicit import.
    /// </summary>
    private void EnterImports(FnStmt declaration, JgsEnvironment frame)
    {
        ImportStmt[] imports = declaration.Imports ??= CollectImports(declaration.Body);
        if (imports.Length == 0 || !declaration.Dialect.IsMatlab)
        {
            return;
        }

        var scope = new NetImports(dynamic: false);
        HashSet<string>? assigned = null;
        foreach (ImportStmt statement in imports)
        {
            foreach (string name in statement.Names)
            {
                RefuseUnknownImport(name, statement);
                if (!NetImports.IsWildcard(name))
                {
                    assigned ??= AssignedNames(declaration);
                    string bound = NetImports.ShortName(name);
                    if (assigned.Contains(bound))
                    {
                        throw new JgsRuntimeException(statement.Line, statement.Column,
                            "MATLAB:lang:ImportedFunctionAndVariableHaveSameName",
                            $"Declaring a variable with the same name as the imported function \"{bound}\" is not supported.");
                    }
                }

                scope.Write(name);
            }
        }

        frame.Imports = scope;
        AnyImports = true;
        AnyNet = true;
    }

    /// <summary>
    /// Runs an <c>import</c> statement. In a function it was entered with the frame and does nothing
    /// now; anywhere else it imports from here on.
    /// </summary>
    private void ExecuteImport(ImportStmt statement)
    {
        JgsEnvironment frame = CurrentFrame;
        if (frame.Function is { Imports: { } hoisted } && Array.IndexOf(hoisted, statement) >= 0)
        {
            return;
        }

        foreach (string name in statement.Names)
        {
            RefuseUnknownImport(name, statement);
        }

        NetImports scope = frame.Imports ??= new NetImports(dynamic: true);
        foreach (string name in statement.Names)
        {
            scope.Write(name);
        }

        AnyImports = true;
        AnyNet = true;
    }

    /// <summary><c>import</c>: the list the running code sees, as a column of names (0-by-1 when empty).</summary>
    internal JgsValue ImportList()
    {
        var names = new List<string>();
        foreach (NetImports scope in ImportScopes(CurrentFrame).Reverse())
        {
            foreach (string name in scope.Listed)
            {
                if (!names.Contains(name, StringComparer.Ordinal))
                {
                    names.Add(name);
                }
            }
        }

        JgsValue column = JgsValue.Cell([.. names.Select(static n => JgsValue.Str(n))]);
        column.Reshape(names.Count, 1);
        return column;
    }

    /// <summary>
    /// <c>import('System.Math', …)</c>: adds to the running scope's list and answers the list. In a
    /// function the names are listed but do not resolve — R2025b resolved the function's names when it
    /// parsed it (net_import, <c>fncall</c>).
    /// </summary>
    internal JgsValue ImportByCall(IReadOnlyList<JgsValue> args, int line, int col)
    {
        var names = new List<string>();
        foreach (JgsValue argument in args)
        {
            if (JgsBuiltins.TextElementsOf(argument) is { } texts)
            {
                names.AddRange(texts);
            }
            else
            {
                throw new JgsRuntimeException(line, col, "MATLAB:import:InvalidInputDataType",
                    "Inputs must be strings or arrays of character vectors.");
            }
        }

        foreach (string name in names)
        {
            if (!NetImports.IsWildcard(name) && ExplicitTarget(name) is null)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:import:NonFullyQualifiedImportArgument",
                    $"Unable to find or import '{name}'. Imported names must end with '.*' or be fully qualified.");
            }
        }

        JgsEnvironment frame = CurrentFrame;
        NetImports scope = frame.Imports ??= new NetImports(dynamic: frame.Function is null);
        foreach (string name in names)
        {
            scope.Add(name);
        }

        AnyImports = true;
        AnyNet = true;
        return ImportList();
    }

    /// <summary><c>clear import</c>: allowed at the prompt alone, where it forgets the base workspace's imports.</summary>
    internal void ClearImports(int line, int col)
    {
        if (!AtPrompt || _inScript || CurrentFrame.Function is not null)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:cannotClear",
                "Invalid use of CLEAR IMPORT. Call CLEAR IMPORT only from the command prompt.");
        }

        CurrentFrame.Imports?.Clear();
    }

    private void RefuseUnknownImport(string name, ImportStmt statement)
    {
        if (!NetImports.IsWildcard(name) && ExplicitTarget(name) is null)
        {
            throw new JgsRuntimeException(statement.Line, statement.Column, "MATLAB:mir_illegal_import_argument",
                $"Unable to find or import '{name}'. Imported names must end with '.*' or be fully qualified.");
        }
    }

    /// <summary>The <c>import</c> statements of a body, its blocks included and its nested functions not.</summary>
    private static ImportStmt[] CollectImports(IReadOnlyList<Stmt> body)
    {
        List<ImportStmt>? found = null;
        void Walk(IReadOnlyList<Stmt> block)
        {
            foreach (Stmt statement in block)
            {
                if (statement is ImportStmt import)
                {
                    (found ??= []).Add(import);
                }
                else if (statement is not FnStmt)
                {
                    for (int slot = 0; slot < AstChildren.SlotCount(statement); slot++)
                    {
                        if (AstChildren.Slot(statement, slot) is { } inner)
                        {
                            Walk(inner);
                        }
                    }
                }
            }
        }

        Walk(body);
        return found is null ? [] : [.. found];
    }

    /// <summary>The names a function makes variables of: its parameters, its outputs, and every name it assigns.</summary>
    private static HashSet<string> AssignedNames(FnStmt declaration)
    {
        var names = new HashSet<string>(declaration.Parameters, StringComparer.Ordinal);
        names.UnionWith(declaration.Outputs);
        void Walk(IReadOnlyList<Stmt> block)
        {
            foreach (Stmt statement in block)
            {
                switch (statement)
                {
                    case FnStmt:
                        continue;
                    case ExprStmt { Expression: AssignExpr assign } when RootName(assign.Target) is { } root:
                        names.Add(root);
                        break;
                    case MultiAssignStmt multi:
                        foreach (Expr? target in multi.Targets)
                        {
                            if (target is not null && RootName(target) is { } root)
                            {
                                names.Add(root);
                            }
                        }

                        break;
                    case ForStmt loop:
                        names.Add(loop.Variable);
                        break;
                    case TryStmt { ErrorVariable: { } caught }:
                        names.Add(caught);
                        break;
                    case GlobalStmt global:
                        names.UnionWith(global.Names);
                        break;
                    case PersistentStmt persistent:
                        names.UnionWith(persistent.Names);
                        break;
                }

                for (int slot = 0; slot < AstChildren.SlotCount(statement); slot++)
                {
                    if (AstChildren.Slot(statement, slot) is { } inner)
                    {
                        Walk(inner);
                    }
                }
            }
        }

        Walk(declaration.Body);
        return names;

        static string? RootName(Expr target) => target switch
        {
            VariableExpr variable => variable.Name,
            CallExpr call => RootName(call.Callee),
            IndexExpr index => RootName(index.Target),
            BraceIndexExpr brace => RootName(brace.Target),
            MemberExpr member => RootName(member.Target),
            _ => null,
        };
    }
}
