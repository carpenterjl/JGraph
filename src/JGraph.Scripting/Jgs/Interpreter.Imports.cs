using JGraph.Scripting.Jgs.Net;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>import</c> for .NET names (interop plan, stage 3, ADR 0176) and for <c>+package</c> names
/// (packages plan, stage P3, ADR 0309): which scope an import belongs to, what an imported name
/// reaches, and the refusals R2025b makes when it parses a file.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope.</b> A function's imports are its own: collected from its body when it is entered, so
/// they apply wherever they are written, and seen by its nested functions, its anonymous functions
/// and its <c>eval</c> — each of which runs in a scope under the function's frame — and by nothing
/// it calls (the frame is a call boundary, and the walk stops there). The base workspace imports as
/// its statements run; a script's imports are a list of its own for its run, over its caller's,
/// and its caller's list never shows them (measured).
/// </para>
/// <para>
/// <b>Precedence</b> is the one the M145 plan's table gives: an explicit import (<c>import
/// System.Math.Max</c>, <c>import pkg.f</c>) answers after the variables and before nested and local
/// functions; a wildcard (<c>import System.*</c>, <c>import pkg.*</c>) after local functions and
/// before everything else — a written call's <c>private/</c> folder, the current folder, the path
/// and the built-ins included (measured). Among explicit imports of one name the first written
/// wins; among package wildcards the earlier written wins and any package wildcard beats any .NET
/// wildcard whichever was written first (p3_imports, measured both ways); among .NET wildcards the
/// later one wins (net_import). A wildcard over a namespace or a package reaches its types or
/// members, and its namespaces or sub-packages only in front of a dot; a wildcard over a type or a
/// class reaches its static methods alone (<c>import pkg.C.*; K</c> is undefined, measured).
/// </para>
/// <para>
/// <b>By text and by handle.</b> A name resolved from text — <c>feval</c>'s, <c>str2func</c>'s, a
/// handle's, eval'd code's — gives the running file's <c>private/</c> folder the first word over a
/// wildcard import where a written call gives it to the import (measured: <c>f(2)</c> under
/// <c>import pkg.*</c> beside <c>private/f.m</c> is <c>pkg.f</c>, <c>@f</c>, <c>feval('f', 2)</c> and
/// <c>eval('f(2)')</c> there are the private file; without the private file all are <c>pkg.f</c>,
/// and a plain <c>f.m</c> on the path loses to the import for every form). <c>which</c>,
/// <c>nargin</c>, <c>isa</c>, <c>methods</c> and <c>exist</c>'s class answers see an imported name;
/// <c>exist</c> of an imported function is 0 (measured).
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

    /// <summary>The workspace eval'd text is running in, while it runs (see <see cref="InEvalText"/>).</summary>
    private JgsEnvironment? _evalEnv;

    /// <summary>
    /// Whether <paramref name="env"/> is the workspace of eval'd text now running: its names resolve
    /// as text does — the file's <c>private/</c> folder before a wildcard import (P3, measured).
    /// </summary>
    internal bool InEvalText(JgsEnvironment env) => _evalEnv is not null && ReferenceEquals(_evalEnv, env);

    /// <summary>The import scopes <paramref name="env"/> sees, nearest first, up to its call boundary - a script's list before the workspace's it runs over.</summary>
    private static IEnumerable<JgsImports> ImportScopes(JgsEnvironment env)
    {
        for (JgsEnvironment? scope = env; scope is not null && !scope.IsBuiltinLayer; scope = scope.Parent)
        {
            for (JgsImports? list = scope.Imports; list is not null; list = list.Outer)
            {
                yield return list;
            }

            if (scope.IsCallBoundary)
            {
                yield break;
            }
        }
    }

    /// <summary>
    /// What <paramref name="name"/> reaches through the imports <paramref name="env"/> sees, or null:
    /// the first explicit import of the name anywhere in reach, nearest scope first; then the
    /// earliest package wildcard holding it; then the latest .NET wildcard. <paramref name="head"/>
    /// is true for the name in front of a dot, where a namespace or a sub-package answers too.
    /// </summary>
    internal JgsImported? ImportFor(string name, JgsEnvironment env, bool head)
    {
        if (!AnyImports)
        {
            return null;
        }

        foreach (JgsImports scope in ImportScopes(env))
        {
            foreach (string entry in scope.Resolving)
            {
                if (!JgsImports.IsWildcard(entry) && JgsImports.ShortName(entry) == name && ExplicitTarget(entry) is { } hit)
                {
                    return Noted(hit);
                }
            }
        }

        foreach (JgsImports scope in ImportScopes(env))
        {
            foreach (string entry in scope.Resolving)
            {
                if (JgsImports.IsWildcard(entry) && PackageWildcardTarget(entry[..^2], name, head) is { } reached)
                {
                    return reached;
                }
            }
        }

        foreach (JgsImports scope in ImportScopes(env))
        {
            foreach (string entry in scope.Resolving.Reverse())
            {
                if (JgsImports.IsWildcard(entry) && WildcardTarget(entry[..^2], name, head) is { } reached)
                {
                    return Noted(reached);
                }
            }
        }

        return null;
    }

    /// <summary>A .NET answer marks the session as using .NET; a package answer does not.</summary>
    private JgsImported Noted(JgsImported imported)
    {
        if (!imported.IsPackage)
        {
            AnyNet = true;
        }

        return imported;
    }

    /// <summary>The class an import gives <paramref name="name"/> — <c>C</c> under <c>import pkg.C</c> or <c>import pkg.*</c> — or null (P3).</summary>
    internal JgsClass? ImportedClass(string name, JgsEnvironment env) =>
        AnyImports && !name.Contains('.') && ImportFor(name, env, head: false) is { Class: { } imported } ? imported : null;

    /// <summary>The value an imported name stands for when it is called or mentioned: a package member, a class's constructor, a .NET constructor or a method group.</summary>
    internal JgsValue? ImportedValue(JgsImported imported) => imported switch
    {
        { Member: { } member } => member,
        { Class: { } definition } => definition.ConstructorValue,
        { Method: { } method, Type: { } type } => JgsValue.Function(new NetCallable(type, method, receiver: null, NetTypes)),
        { Type: { } type } => JgsValue.Function(NetCallable.Constructor(type, NetTypes)),
        _ => null,
    };

    /// <summary>An explicit import's target: a package's class, member or class static, or a .NET type named in full or a static method of one.</summary>
    private JgsImported? ExplicitTarget(string entry) => PackageTarget(entry, explicitly: true) ?? NetExplicitTarget(entry);

    /// <summary>
    /// The package world's reading of a dotted name: the class <c>pkg.C</c>, the member <c>pkg.f</c>
    /// (a function file or a script), or the static <c>pkg.C.stat</c>; null for a sub-package alone
    /// (<c>import pkg.sub</c> is refused, measured) and for anything no package holds.
    /// </summary>
    private JgsImported? PackageTarget(string dotted, bool explicitly)
    {
        if (FunctionPath is not { } path || !path.Index.AnyPackages || JgsCodeTree.DottedParts(dotted) is not { } parts)
        {
            return null;
        }

        if (path.Tree.IsPackage(parts[..^1]))
        {
            if (PackageClass(dotted) is { } definition)
            {
                return new JgsImported(explicitly, Class: definition, QualifiedName: dotted, File: FileOfClass(definition));
            }

            return path.TryResolveDotted(dotted, out JgsValue member, out string? file, out _) && member.Type == JgsType.Function
                ? new JgsImported(explicitly, Member: member, QualifiedName: dotted, File: file)
                : null;
        }

        if (parts.Length >= 3 && path.Tree.IsPackage(parts[..^2]) && PackageStatic(dotted) is { } held)
        {
            return new JgsImported(explicitly, Member: JgsValue.Function(held.Class.Callable(held.Method)),
                QualifiedName: dotted, File: FileOfClass(held.Class));
        }

        return null;
    }

    /// <summary>
    /// What <c>prefix.*</c> gives <paramref name="name"/> in the package world: a package's member
    /// or class, its sub-package in front of a dot, or a class's static method (<c>import
    /// pkg.C.*</c>); null when <paramref name="prefix"/> is no package or class in one.
    /// </summary>
    private JgsImported? PackageWildcardTarget(string prefix, string name, bool head)
    {
        if (FunctionPath is not { } path || !path.Index.AnyPackages
            || JgsCodeTree.DottedParts(prefix + "." + name) is not { } parts)
        {
            return null;
        }

        string[] package = parts[..^1];
        string dotted = prefix + "." + name;
        if (path.Tree.IsPackage(package))
        {
            if (path.Tree.MemberFolder(package, name) is not null)
            {
                return PackageTarget(dotted, explicitly: false);
            }

            return head && path.Tree.HasSubPackage(package, name) ? new JgsImported(false, Package: dotted) : null;
        }

        return package.Length >= 2 && path.Tree.IsPackage(package[..^1]) && PackageStatic(dotted) is { } held
            ? new JgsImported(false, Member: JgsValue.Function(held.Class.Callable(held.Method)), QualifiedName: dotted, File: FileOfClass(held.Class))
            : null;
    }

    /// <summary>Whether an entry's head names a package some search folder holds - the entry is the package world's, whatever else it says.</summary>
    private bool NamesPackage(string entry) =>
        FunctionPath is { } path && path.Index.AnyPackages && entry.IndexOf('.') is > 0 and var dot && path.Tree.IsPackage([entry[..dot]]);

    /// <summary>An explicit .NET import's target: a type named in full, or a static method of one.</summary>
    private JgsImported? NetExplicitTarget(string entry)
    {
        if (NetTypes.TypeNamed(entry) is { } type)
        {
            return new JgsImported(true, type, null, null);
        }

        int dot = entry.LastIndexOf('.');
        return dot > 0 && NetTypes.TypeNamed(entry[..dot]) is { } owner
            && NetInvoke.HasMethod(owner, entry[(dot + 1)..], instance: false)
            ? new JgsImported(true, owner, entry[(dot + 1)..], null)
            : null;
    }

    /// <summary>What <c>prefix.*</c> gives <paramref name="name"/> in .NET: a type's static method, or a namespace's type or namespace.</summary>
    private JgsImported? WildcardTarget(string prefix, string name, bool head)
    {
        if (NetTypes.TypeNamed(prefix) is { } owner)
        {
            return NetInvoke.HasMethod(owner, name, instance: false) ? new JgsImported(false, owner, name, null) : null;
        }

        string full = prefix + "." + name;
        if (NetTypes.TypeNamed(full) is { } type)
        {
            return new JgsImported(false, type, null, null);
        }

        return head && NetTypes.IsNamespace(full) ? new JgsImported(false, null, null, full) : null;
    }

    /// <summary>
    /// Enters a function's imports into its frame, refusing what R2025b refuses when it parses the
    /// file: an explicit import that names no type, static method, package member or class; one
    /// named like a nested function of the function; and a variable of the function named like an
    /// explicit import — each in R2025b's "Error: File: … Line: … Column: …" form, pointing at the
    /// name or at the assignment (measured).
    /// </summary>
    private void EnterImports(FnStmt declaration, JgsEnvironment frame)
    {
        ImportStmt[] imports = declaration.Imports ??= CollectImports(declaration.Body);
        if (imports.Length == 0 || !declaration.Dialect.IsMatlab)
        {
            return;
        }

        var scope = new JgsImports(dynamic: false);
        Dictionary<string, (int Line, int Column)>? assigned = null;
        HashSet<string>? nested = null;
        foreach (ImportStmt statement in imports)
        {
            for (int i = 0; i < statement.Names.Count; i++)
            {
                string name = statement.Names[i];
                int column = statement.ColumnOf(i);
                RefuseUnknownImport(name, statement, column);
                if (!JgsImports.IsWildcard(name))
                {
                    string bound = JgsImports.ShortName(name);
                    nested ??= NestedNames(declaration);
                    if (nested.Contains(bound))
                    {
                        throw ParseTimeRefusal(statement.SourceId, statement.Line, column,
                            "MATLAB:lang:ImportedFunctionAndNestedFunctionHaveSameName",
                            $"Importing a class or a function with the same name as that of a nested function \"{bound}\" is not supported.");
                    }

                    assigned ??= AssignedNames(declaration);
                    if (assigned.TryGetValue(bound, out (int Line, int Column) at))
                    {
                        throw ParseTimeRefusal(statement.SourceId, at.Line, at.Column,
                            "MATLAB:lang:ImportedFunctionAndVariableHaveSameName",
                            $"Declaring a variable with the same name as the imported function \"{bound}\" is not supported.");
                    }
                }

                scope.Write(name);
                if (!NamesPackage(name))
                {
                    AnyNet = true;
                }
            }
        }

        frame.Imports = scope;
        AnyImports = true;
    }

    /// <summary>
    /// Enters a script's imports into the list <paramref name="scope"/> runs with before its first
    /// statement: R2025b resolves a script file's imports when it parses it, as a function's, so
    /// <c>import</c> on a script's first line lists an import written on its last (p3_imports,
    /// measured), and an explicit import naming nothing refuses the whole file. The statements
    /// then write the same names again, which the list takes once.
    /// </summary>
    private void HoistImports(IReadOnlyList<Stmt> program, JgsEnvironment scope)
    {
        if (!Dialect.IsMatlab)
        {
            return;
        }

        ImportStmt[] imports = CollectImports(program);
        if (imports.Length == 0)
        {
            return;
        }

        JgsImports list = scope.Imports ??= new JgsImports(dynamic: true);
        foreach (ImportStmt statement in imports)
        {
            for (int i = 0; i < statement.Names.Count; i++)
            {
                string name = statement.Names[i];
                RefuseUnknownImport(name, statement, statement.ColumnOf(i));
                list.Write(name);
                if (!NamesPackage(name))
                {
                    AnyNet = true;
                }
            }
        }

        AnyImports = true;
    }

    /// <summary>
    /// Runs an <c>import</c> statement. In a function it was entered with the frame and does nothing
    /// now; anywhere else - the base workspace, a script's own list - it imports from here on.
    /// </summary>
    private void ExecuteImport(ImportStmt statement)
    {
        JgsEnvironment frame = CurrentFrame;
        if (frame.Function is { Imports: { } hoisted } && Array.IndexOf(hoisted, statement) >= 0)
        {
            return;
        }

        for (int i = 0; i < statement.Names.Count; i++)
        {
            RefuseUnknownImport(statement.Names[i], statement, statement.ColumnOf(i));
        }

        JgsImports scope = frame.Imports ??= new JgsImports(dynamic: true);
        foreach (string name in statement.Names)
        {
            scope.Write(name);
            if (!NamesPackage(name))
            {
                AnyNet = true;
            }
        }

        AnyImports = true;
    }

    /// <summary><c>import</c>: the list the running code sees, as a column of names (0-by-1 when empty).</summary>
    internal JgsValue ImportList()
    {
        var names = new List<string>();
        foreach (JgsImports scope in ImportScopes(CurrentFrame).Reverse())
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
            if (!JgsImports.IsWildcard(name) && ExplicitTarget(name) is null)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:import:NonFullyQualifiedImportArgument",
                    $"Unable to find or import '{name}'. Imported names must end with '.*' or be fully qualified.");
            }
        }

        JgsEnvironment frame = CurrentFrame;
        JgsImports scope = frame.Imports ??= new JgsImports(dynamic: frame.Function is null);
        foreach (string name in names)
        {
            scope.Add(name);
            if (!NamesPackage(name))
            {
                AnyNet = true;
            }
        }

        AnyImports = true;
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

    private void RefuseUnknownImport(string name, ImportStmt statement, int column)
    {
        if (!JgsImports.IsWildcard(name) && ExplicitTarget(name) is null)
        {
            throw ParseTimeRefusal(statement.SourceId, statement.Line, column, "MATLAB:mir_illegal_import_argument",
                $"Unable to find or import '{name}'. Imported names must end with '.*' or be fully qualified.");
        }
    }

    /// <summary>
    /// A refusal R2025b makes when it parses a file, in its form: the file's path, the line and the
    /// column ahead of the message (measured: <c>Error: File: …\imp_var.m Line: 3 Column: 1</c>).
    /// Code with no file - the prompt's - gets the message alone.
    /// </summary>
    private static JgsRuntimeException ParseTimeRefusal(string sourceId, int line, int column, string identifier, string message) =>
        new(line, column, identifier,
            sourceId.Length > 0 ? $"Error: File: {sourceId} Line: {line} Column: {column}\n{message}" : message);

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

    /// <summary>The names of a function's nested functions.</summary>
    private static HashSet<string> NestedNames(FnStmt declaration)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Stmt statement in declaration.Body)
        {
            if (statement is FnStmt nested)
            {
                names.Add(nested.Name);
            }
        }

        return names;
    }

    /// <summary>
    /// The names a function makes variables of — its parameters, its outputs, and every name it
    /// assigns — each with the position of its first assignment (the declaration's for a parameter
    /// or an output), where R2025b points the refusal.
    /// </summary>
    private static Dictionary<string, (int Line, int Column)> AssignedNames(FnStmt declaration)
    {
        var names = new Dictionary<string, (int Line, int Column)>(StringComparer.Ordinal);
        void Note(string name, int line, int column) => names.TryAdd(name, (line, column));
        foreach (string parameter in declaration.Parameters)
        {
            Note(parameter, declaration.Line, declaration.Column);
        }

        foreach (string output in declaration.Outputs)
        {
            Note(output, declaration.Line, declaration.Column);
        }

        void Walk(IReadOnlyList<Stmt> block)
        {
            foreach (Stmt statement in block)
            {
                switch (statement)
                {
                    case FnStmt:
                        continue;
                    case ExprStmt { Expression: AssignExpr assign } when RootName(assign.Target) is { } root:
                        Note(root, statement.Line, statement.Column);
                        break;
                    case MultiAssignStmt multi:
                        foreach (Expr? target in multi.Targets)
                        {
                            if (target is not null && RootName(target) is { } root)
                            {
                                Note(root, statement.Line, statement.Column);
                            }
                        }

                        break;
                    case ForStmt loop:
                        Note(loop.Variable, statement.Line, statement.Column);
                        break;
                    case TryStmt { ErrorVariable: { } caught }:
                        Note(caught, statement.Line, statement.Column);
                        break;
                    case GlobalStmt global:
                        foreach (string name in global.Names)
                        {
                            Note(name, statement.Line, statement.Column);
                        }

                        break;
                    case PersistentStmt persistent:
                        foreach (string name in persistent.Names)
                        {
                            Note(name, statement.Line, statement.Column);
                        }

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
