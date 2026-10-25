using JGraph.Deploy;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// What a build takes along (compiler and SDK plan, stage K2): every user file the roots reach by
/// calling, naming a handle, inheriting or a <c>%#function</c> pragma, the <c>.fig</c> beside each
/// code file, and the data files a literal names in a call to one of the functions that read one
/// (<see cref="DeploymentRules.DataReaders"/>). Built-ins are left out, since the runtime ships
/// them, and give the products the files need. The rules are R2025b's, as probe_k2_depend and
/// probe_k2_rules measured <c>matlab.codetools.requiredFilesAndProducts</c> and <c>mcc</c> answering.
/// </summary>
/// <remarks>
/// <para>
/// A name is resolved statically, in the order a call would be, without argument classes: a local
/// or nested function of the file (walked with the file); a variable, which is any name the function
/// assigns anywhere in its body, a parameter, an output, a loop variable, a global or a persistent
/// (<c>fn(1); fn = 3;</c> names no file); the file's <c>private/</c> folder; then a file in the
/// current folder, the <c>-I</c> folders and the search path (a root's own folder only when
/// <see cref="DependencyOptions.SearchRootFolders"/> says so), an <c>.mlapp</c> of a folder before
/// its <c>.m</c>, matched case-sensitively; and last the built-ins. A handle
/// <c>@f</c> is never a variable. A call through a dot on a variable (<c>o.go()</c>) names nothing;
/// a name only in a string (<c>feval('f')</c>, <c>eval</c>, <c>str2func</c>) is not seen, which is
/// what <c>%#function</c> is for.
/// </para>
/// <para>
/// A data file is taken for a literal argument, or for a variable the literal was last assigned to
/// before the call, when the literal is a bare file name (no folder, absolute or relative), not a
/// code file's, found where <c>which</c> would find it, the folders above; <c>load</c>
/// of a name with no extension takes its <c>.mat</c>.
/// </para>
/// <para>
/// Packages, <c>@Class</c> folders and imports are not followed: JGraph does not have them
/// (ADR 0149), and a program that uses them fails to build here as it fails to run.
/// </para>
/// </remarks>
internal sealed class JgsDependencyAnalyzer
{
    private static readonly StringComparer PathComparer = JgsFunctionPath.PathComparer;

    private readonly DependencyOptions _options;
    private readonly Func<string, bool> _isBuiltin;
    private readonly List<string> _folders = [];
    private readonly Dictionary<string, Dictionary<string, string>> _listings = new(PathComparer);
    private readonly Dictionary<string, DependencyFile> _taken = new(PathComparer);
    private readonly HashSet<string> _forced = new(PathComparer);
    private readonly HashSet<string> _analysed = new(PathComparer);
    private readonly HashSet<string> _pragmaExcluded = new(StringComparer.Ordinal);
    private readonly List<ExcludedDependency> _excluded = [];
    private readonly HashSet<string> _excludedNames = new(StringComparer.Ordinal);
    private readonly List<UnresolvedSymbol> _unresolved = [];
    private readonly List<int> _products = [DeploymentRules.MatlabProduct];
    private readonly Queue<string> _pending = new();
    private readonly HashSet<string> _restored;

    /// <summary>Creates an analysis over <paramref name="options"/>' folders.</summary>
    /// <param name="options">Where names are looked for, and the mcc flags.</param>
    /// <param name="isBuiltin">Whether the session answers a name with a built-in.</param>
    public JgsDependencyAnalyzer(DependencyOptions options, Func<string, bool> isBuiltin)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(isBuiltin);
        _options = options;
        _isBuiltin = isBuiltin;
        _restored = new HashSet<string>(options.ToolboxFolders, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Analyses <paramref name="roots"/>: full paths of files, each taken whatever a pragma says and
    /// analysed when it is code, or of folders, every file under which is a root. A <c>.fig</c> root
    /// brings the code file of its name beside it.
    /// </summary>
    public DependencyReport Analyze(IReadOnlyList<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        AddFolder(_options.CurrentFolder);
        foreach (string root in _options.SearchRootFolders ? roots : [])
        {
            AddFolder(Directory.Exists(root) ? root : Path.GetDirectoryName(root));
        }

        foreach (string folder in _options.IncludeFolders.Concat(_options.SearchPath))
        {
            AddFolder(folder);
        }

        foreach (string root in roots)
        {
            foreach (string file in Expand(root))
            {
                Force(file, DependencyReason.Root);
                if (Path.GetExtension(file).Equals(".fig", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string extension in JgsFunctionPath.CodeExtensions)
                    {
                        string code = Path.ChangeExtension(file, extension);
                        if (File.Exists(code))
                        {
                            Force(code, DependencyReason.Root);
                            break;
                        }
                    }
                }
            }
        }

        foreach (string added in _options.AdditionalFiles)
        {
            foreach (string file in ExpandAdditional(added))
            {
                Force(file, DependencyReason.Additional);
            }
        }

        while (_pending.TryDequeue(out string? file))
        {
            Analyse(file);
        }

        DependencyFile[] files = _taken.Values.OrderBy(f => f.Path, StringComparer.Ordinal).ToArray();
        RuntimeProduct[] products = _products.Select(DeploymentRules.Product).OfType<RuntimeProduct>().ToArray();
        return new DependencyReport(files, products, _excluded.ToArray(), _unresolved.ToArray());
    }

    // --- Files ----------------------------------------------------------------------------------

    private void AddFolder(string? folder)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        string full = Path.GetFullPath(folder);
        if (!_folders.Contains(full, PathComparer))
        {
            _folders.Add(full);
        }
    }

    private static IEnumerable<string> Expand(string root)
    {
        if (Directory.Exists(root))
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(Path.GetFullPath)
                .OrderBy(f => f, StringComparer.Ordinal);
        }

        return [Path.GetFullPath(root)];
    }

    /// <summary>An <c>-a</c> entry: a file, a folder whole, or a wildcard over one folder's files.</summary>
    private IEnumerable<string> ExpandAdditional(string added)
    {
        string full = Path.GetFullPath(Path.IsPathRooted(added) ? added : Path.Combine(_options.CurrentFolder, added));
        if (Path.GetFileName(full).IndexOfAny(['*', '?']) >= 0)
        {
            string folder = Path.GetDirectoryName(full)!;
            return Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, Path.GetFileName(full)).Select(Path.GetFullPath).OrderBy(f => f, StringComparer.Ordinal)
                : [];
        }

        return Expand(full);
    }

    /// <summary>Takes a root or an added file whatever a pragma says, analysing it when it is code.</summary>
    private void Force(string file, DependencyReason reason)
    {
        _forced.Add(file);
        Take(file, reason, null);
    }

    /// <summary>
    /// Takes <paramref name="file"/> into the build unless it is already in or a <c>%#exclude</c>
    /// names it; a code file is queued for analysis and brings its <c>.fig</c>.
    /// </summary>
    private void Take(string file, DependencyReason reason, string? namedBy)
    {
        if (_taken.ContainsKey(file))
        {
            return;
        }

        bool code = IsCode(file);
        if (!_forced.Contains(file)
            && _pragmaExcluded.Contains(code ? Path.GetFileNameWithoutExtension(file) : Path.GetFileName(file)))
        {
            return;
        }

        _taken[file] = new DependencyFile(file, reason, namedBy);
        if (!code)
        {
            return;
        }

        if (!_options.TopOnly || reason is DependencyReason.Root or DependencyReason.Additional)
        {
            _pending.Enqueue(file);
        }

        // mcr.rdl's Insert rule: the .fig of an .m (or .mlx) beside it, whatever took the .m - when
        // the .m's folder is one names are looked for in (a root off the path brings none, measured).
        if (Path.GetExtension(file).Equals(".m", StringComparison.OrdinalIgnoreCase)
            && _folders.Contains(Path.GetDirectoryName(file)!, PathComparer))
        {
            string figure = Path.ChangeExtension(file, ".fig");
            if (Exists(figure) is { } found)
            {
                Take(found, DependencyReason.Figure, file);
            }
        }
    }

    private static bool IsCode(string file) =>
        JgsFunctionPath.CodeExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);

    /// <summary>The file at <paramref name="path"/> when its folder holds that name exactly, case and all.</summary>
    private string? Exists(string path)
    {
        string? folder = Path.GetDirectoryName(path);
        return folder is not null && Listing(folder).TryGetValue(Path.GetFileName(path), out string? found) ? found : null;
    }

    private Dictionary<string, string> Listing(string folder)
    {
        if (!_listings.TryGetValue(folder, out Dictionary<string, string>? listing))
        {
            listing = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Directory.Exists(folder))
            {
                foreach (string file in Directory.EnumerateFiles(folder))
                {
                    listing[Path.GetFileName(file)] = Path.GetFullPath(file);
                }
            }

            _listings[folder] = listing;
        }

        return listing;
    }

    /// <summary>The code file <paramref name="name"/> resolves to from <paramref name="from"/>, or null.</summary>
    private string? FindCode(string name, string from)
    {
        string folder = Path.GetDirectoryName(from)!;
        string privateFolder = Path.GetFileName(folder).Equals("private", StringComparison.OrdinalIgnoreCase)
            ? folder
            : Path.Combine(folder, "private");
        foreach (string candidate in new[] { privateFolder }.Concat(_folders))
        {
            foreach (string extension in JgsFunctionPath.CodeExtensions)
            {
                if (Listing(candidate).TryGetValue(name + extension, out string? found))
                {
                    return found;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The data file a literal names, or null: a bare name, not a code file's, where <c>which</c>
    /// would find it - the current folder, then the path.
    /// </summary>
    private string? FindData(string literal, bool addMat)
    {
        if (literal.Length == 0 || literal.IndexOfAny(['/', '\\', ':']) >= 0
            || DeploymentRules.CodeExtensions.Contains(Path.GetExtension(literal)))
        {
            return null;
        }

        foreach (string candidate in _folders)
        {
            if (Listing(candidate).TryGetValue(literal, out string? found))
            {
                return found;
            }
        }

        return addMat && Path.GetExtension(literal).Length == 0 ? FindData(literal + ".mat", false) : null;
    }

    // --- One file -------------------------------------------------------------------------------

    private void Analyse(string file)
    {
        if (!_analysed.Add(file))
        {
            return;
        }

        IReadOnlyList<Stmt> program;
        var pragmas = new List<SourcePragma>();
        try
        {
            // A P-file is walked as the tokens it holds (stage K3b; it has no pragmas left).
            program = JgsPcode.IsPcode(file)
                ? JgsPcode.Load(file)
                : Parser.Parse(JgsMlapp.ReadSource(file), file, JgsDialect.Matlab, pragmas);
        }
        catch (Exception ex) when (ex is JgsSyntaxException or JgsRuntimeException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return; // a file that does not read or parse is taken as it is; running it reports why
        }

        var walk = new FileWalk(this, file, program);
        foreach (SourcePragma pragma in pragmas)
        {
            if (pragma.Word == "exclude")
            {
                // A name with a code file's extension excludes nothing (%#exclude fg.m, measured).
                foreach (string name in PragmaNames(pragma.Text))
                {
                    if (!DeploymentRules.CodeExtensions.Contains(Path.GetExtension(name)))
                    {
                        _pragmaExcluded.Add(name);
                    }
                }
            }
        }

        foreach (SourcePragma pragma in pragmas)
        {
            if (pragma.Word == "function")
            {
                foreach (string name in PragmaNames(pragma.Text))
                {
                    walk.Pragma(name);
                }
            }
        }

        walk.Run();
    }

    /// <summary>The names a pragma lists, separated by blanks or commas.</summary>
    private static IEnumerable<string> PragmaNames(string text) =>
        text.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>A name the code calls or mentions, after the file's own functions and variables are ruled out.</summary>
    private void Reference(string name, string from)
    {
        if (!IsIdentifier(name))
        {
            return;
        }

        if (FindCode(name, from) is { } code)
        {
            Take(code, DependencyReason.Code, from);
            return;
        }

        if (DeploymentRules.NonDeployableFile(name) is { } excluded)
        {
            if (_excludedNames.Add(name))
            {
                _excluded.Add(new ExcludedDependency(name, excluded, DeploymentRules.ExcludedById,
                    DeploymentRules.NonDeployableReason, from));
            }

            return;
        }

        if (!_isBuiltin(name))
        {
            return; // nothing answers it: R2025b lists nothing either
        }

        if (_options.ClearToolboxPath && DeploymentRules.ToolboxOf(name) is { } toolbox
            && !toolbox.Equals("matlab", StringComparison.OrdinalIgnoreCase) && !_restored.Contains(toolbox))
        {
            if (!_unresolved.Any(u => u.Symbol == name && PathComparer.Equals(u.File, from)))
            {
                _unresolved.Add(new UnresolvedSymbol(from, name, "mcc_flag"));
            }

            return;
        }

        foreach (int product in DeploymentRules.ProductsOf(name))
        {
            if (!_products.Contains(product))
            {
                _products.Add(product);
            }
        }
    }

    private static bool IsIdentifier(string name) =>
        name.Length > 0 && char.IsLetter(name[0]) && name.All(c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>
    /// One file's walk: its own functions, each function's variables and the literals its
    /// variables last held, and every name and data literal it mentions.
    /// </summary>
    private sealed class FileWalk
    {
        private readonly JgsDependencyAnalyzer _owner;
        private readonly string _file;
        private readonly IReadOnlyList<Stmt> _program;
        private readonly HashSet<string> _ownFunctions = new(StringComparer.Ordinal);

        public FileWalk(JgsDependencyAnalyzer owner, string file, IReadOnlyList<Stmt> program)
        {
            _owner = owner;
            _file = file;
            _program = program;
            foreach (Stmt statement in program)
            {
                CollectFunctions(statement);
            }
        }

        private void CollectFunctions(Stmt statement)
        {
            switch (statement)
            {
                case FnStmt function:
                    _ownFunctions.Add(function.Name);
                    foreach (Stmt inner in function.Body)
                    {
                        CollectFunctions(inner);
                    }

                    break;
                case ClassdefStmt classdef:
                    foreach (ClassMethod method in classdef.Methods)
                    {
                        CollectFunctions(method.Function);
                    }

                    break;
            }
        }

        /// <summary>A <c>%#function</c> name: a function, or a data file with its extension.</summary>
        public void Pragma(string name)
        {
            string extension = Path.GetExtension(name);
            if (extension.Length == 0)
            {
                if (_owner.FindCode(name, _file) is { } code)
                {
                    _owner.Take(code, DependencyReason.Pragma, _file);
                }
            }
            else if (_owner.FindData(name, addMat: false) is { } data)
            {
                _owner.Take(data, DependencyReason.Pragma, _file);
            }
        }

        public void Run()
        {
            var script = new List<Stmt>();
            foreach (Stmt statement in _program)
            {
                switch (statement)
                {
                    case FnStmt function:
                        Function(function, null);
                        break;
                    case ClassdefStmt classdef:
                        Class(classdef);
                        break;
                    default:
                        script.Add(statement);
                        break;
                }
            }

            if (script.Count > 0)
            {
                var scope = new Scope(null, Assigned(script));
                Statements(script, scope);
            }
        }

        private void Class(ClassdefStmt classdef)
        {
            foreach (string superclass in classdef.Superclasses)
            {
                if (!superclass.Contains('.', StringComparison.Ordinal) && superclass != "handle")
                {
                    Name(superclass, null);
                }
            }

            var none = new Scope(null, new HashSet<string>(StringComparer.Ordinal));
            foreach (ClassProperty property in classdef.Properties)
            {
                Spec(property.Spec, none);
            }

            foreach (ClassMethod method in classdef.Methods)
            {
                Function(method.Function, null);
            }
        }

        private void Spec(ArgumentSpec spec, Scope scope)
        {
            if (spec.ClassName is { } className && !className.Contains('.', StringComparison.Ordinal))
            {
                Name(className, null);
            }

            if (spec.Default is not null)
            {
                Expression(spec.Default, scope);
            }

            foreach (Expr validator in spec.Validators)
            {
                Expression(validator, scope);
            }

            if (spec.Dims is not null)
            {
                foreach (Expr? dim in spec.Dims)
                {
                    if (dim is not null) Expression(dim, scope);
                }
            }
        }

        private void Function(FnStmt function, Scope? parent)
        {
            HashSet<string> variables = Assigned(function.Body);
            variables.UnionWith(function.Parameters);
            variables.UnionWith(function.Outputs);
            var scope = new Scope(parent, variables);
            Statements(function.Body, scope);
        }

        /// <summary>A name in code: a reference unless the file or a variable answers it.</summary>
        private void Name(string name, Scope? scope)
        {
            if (_ownFunctions.Contains(name) || (scope is not null && scope.IsVariable(name)))
            {
                return;
            }

            _owner.Reference(name, _file);
        }

        private void Statements(IReadOnlyList<Stmt> statements, Scope scope)
        {
            foreach (Stmt statement in statements)
            {
                Statement(statement, scope);
            }
        }

        private void Statement(Stmt statement, Scope scope)
        {
            switch (statement)
            {
                case FnStmt nested:
                    Function(nested, scope);
                    break;
                case LetStmt let:
                    Expression(let.Value, scope);
                    scope.Assign(let.Name, let.Value);
                    break;
                case DestructuringLetStmt destructuring:
                    Expression(destructuring.Value, scope);
                    foreach (string name in destructuring.Names) scope.Assign(name, null);
                    break;
                case ExprStmt expression:
                    Expression(expression.Expression, scope);
                    break;
                case IfStmt ifStmt:
                    Expression(ifStmt.Condition, scope);
                    Statements(ifStmt.Then, scope);
                    if (ifStmt.Else is not null) Statements(ifStmt.Else, scope);
                    break;
                case WhileStmt whileStmt:
                    Expression(whileStmt.Condition, scope);
                    Statements(whileStmt.Body, scope);
                    break;
                case ForStmt forStmt:
                    Expression(forStmt.Iterable, scope);
                    scope.Assign(forStmt.Variable, null);
                    Statements(forStmt.Body, scope);
                    break;
                case MultiAssignStmt multi:
                    Expression(multi.Call, scope);
                    foreach (Expr? target in multi.Targets)
                    {
                        if (target is null) continue;
                        Expression(target, scope);
                        if (RootName(target) is { } name) scope.Assign(name, null);
                    }

                    break;
                case SwitchStmt switchStmt:
                    Expression(switchStmt.Subject, scope);
                    foreach (SwitchCase arm in switchStmt.Cases)
                    {
                        Expression(arm.Value, scope);
                        Statements(arm.Body, scope);
                    }

                    if (switchStmt.Otherwise is not null) Statements(switchStmt.Otherwise, scope);
                    break;
                case TryStmt tryStmt:
                    Statements(tryStmt.Body, scope);
                    Statements(tryStmt.Handler, scope);
                    break;
                case ArgumentsStmt arguments:
                    foreach (ArgumentSpec spec in arguments.Arguments) Spec(spec, scope);
                    break;
                case ReturnStmt { Value: { } value }:
                    Expression(value, scope);
                    break;
                case ClassdefStmt classdef:
                    Class(classdef);
                    break;
            }
        }

        private void Expression(Expr expression, Scope scope)
        {
            switch (expression)
            {
                case VariableExpr variable:
                    Name(variable.Name, scope);
                    break;
                case CallExpr call:
                    if (call.Callee is VariableExpr callee)
                    {
                        Name(callee.Name, scope);
                        if (!_ownFunctions.Contains(callee.Name) && !scope.IsVariable(callee.Name))
                        {
                            DataArguments(callee.Name, call.Arguments, scope);
                        }
                    }
                    else
                    {
                        Expression(call.Callee, scope);
                    }

                    Expressions(call.Arguments, scope);
                    break;
                case FunctionHandleExpr handle:
                    // A handle is never a variable: max = 7; @max is the function.
                    if (!handle.Name.Contains('.', StringComparison.Ordinal) && !_ownFunctions.Contains(handle.Name))
                    {
                        _owner.Reference(handle.Name, _file);
                    }

                    break;
                case AnonymousFnExpr anonymous:
                    Expression(anonymous.Body, new Scope(scope, new HashSet<string>(anonymous.Parameters, StringComparer.Ordinal)));
                    break;
                case MetaClassExpr meta:
                    if (!meta.Name.Contains('.', StringComparison.Ordinal)) Name(meta.Name, scope);
                    break;
                case AssignExpr assign:
                    Expression(assign.Value, scope);
                    Expression(assign.Target, scope);
                    if (assign.Target is VariableExpr target) scope.Assign(target.Name, assign.Op == TokenType.Assign ? assign.Value : null);
                    break;
                case MemberExpr member:
                    Expression(member.Target, scope);
                    if (member.FieldName is not null) Expression(member.FieldName, scope);
                    break;
                case IndexExpr index:
                    Expression(index.Target, scope);
                    Expressions(index.Indices, scope);
                    break;
                case BraceIndexExpr brace:
                    Expression(brace.Target, scope);
                    Expressions(brace.Indices, scope);
                    break;
                case ArrayLiteral array:
                    Expressions(array.Elements, scope);
                    break;
                case MatrixLiteral matrix:
                    foreach (IReadOnlyList<Expr> row in matrix.Rows) Expressions(row, scope);
                    break;
                case CellLiteral cell:
                    foreach (IReadOnlyList<Expr> row in cell.Rows) Expressions(row, scope);
                    break;
                case RangeExpr range:
                    Expression(range.Start, scope);
                    if (range.Step is not null) Expression(range.Step, scope);
                    Expression(range.Stop, scope);
                    break;
                case UnaryExpr unary:
                    Expression(unary.Operand, scope);
                    break;
                case BinaryExpr binary:
                    Expression(binary.Left, scope);
                    Expression(binary.Right, scope);
                    break;
                case LogicalExpr logical:
                    Expression(logical.Left, scope);
                    Expression(logical.Right, scope);
                    break;
                case TransposeExpr transpose:
                    Expression(transpose.Operand, scope);
                    break;
                case IncDecExpr incDec:
                    Expression(incDec.Target, scope);
                    break;
            }
        }

        private void Expressions(IReadOnlyList<Expr> expressions, Scope scope)
        {
            foreach (Expr expression in expressions)
            {
                Expression(expression, scope);
            }
        }

        /// <summary>The data files a call to a reader names by a literal or a variable holding one.</summary>
        private void DataArguments(string function, IReadOnlyList<Expr> arguments, Scope scope)
        {
            if (!_owner._options.DetectDataFiles || !DeploymentRules.DataReaders.Contains(function))
            {
                return;
            }

            foreach (Expr argument in arguments)
            {
                string? literal = argument switch
                {
                    StringLiteral text => text.Value,
                    VariableExpr variable => scope.LiteralOf(variable.Name),
                    _ => null,
                };
                if (literal is not null && _owner.FindData(literal, addMat: function == "load") is { } data)
                {
                    _owner.Take(data, DependencyReason.Data, _file);
                }
            }
        }

        private static string? RootName(Expr target) => target switch
        {
            VariableExpr variable => variable.Name,
            IndexExpr index => RootName(index.Target),
            BraceIndexExpr brace => RootName(brace.Target),
            MemberExpr member => RootName(member.Target),
            _ => null,
        };

        /// <summary>
        /// Every name a body assigns, outside its nested functions: what makes a mention a variable
        /// throughout the function, wherever the assignment stands.
        /// </summary>
        private static HashSet<string> Assigned(IReadOnlyList<Stmt> statements)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            void Visit(IReadOnlyList<Stmt> body)
            {
                foreach (Stmt statement in body)
                {
                    switch (statement)
                    {
                        case LetStmt let: names.Add(let.Name); break;
                        case DestructuringLetStmt destructuring: names.UnionWith(destructuring.Names); break;
                        case ForStmt forStmt: names.Add(forStmt.Variable); Visit(forStmt.Body); break;
                        case MultiAssignStmt multi:
                            foreach (Expr? target in multi.Targets)
                            {
                                if (target is not null && RootName(target) is { } name) names.Add(name);
                            }

                            break;
                        case ExprStmt { Expression: AssignExpr assign } when RootName(assign.Target) is { } assigned:
                            names.Add(assigned);
                            break;
                        case ExprStmt { Expression: IncDecExpr incDec } when RootName(incDec.Target) is { } stepped:
                            names.Add(stepped);
                            break;
                        case IfStmt ifStmt: Visit(ifStmt.Then); if (ifStmt.Else is not null) Visit(ifStmt.Else); break;
                        case WhileStmt whileStmt: Visit(whileStmt.Body); break;
                        case SwitchStmt switchStmt:
                            foreach (SwitchCase arm in switchStmt.Cases) Visit(arm.Body);
                            if (switchStmt.Otherwise is not null) Visit(switchStmt.Otherwise);
                            break;
                        case TryStmt tryStmt:
                            Visit(tryStmt.Body);
                            if (tryStmt.ErrorVariable is { } error) names.Add(error);
                            Visit(tryStmt.Handler);
                            break;
                        case GlobalStmt global: names.UnionWith(global.Names); break;
                        case PersistentStmt persistent: names.UnionWith(persistent.Names); break;
                        case ArgumentsStmt arguments: names.UnionWith(arguments.Arguments.Select(a => a.Variable)); break;
                    }
                }
            }

            Visit(statements);
            return names;
        }
    }

    /// <summary>
    /// A function's variables (its own and those of the functions it is nested in, which it shares)
    /// and the literal each held when last assigned.
    /// </summary>
    private sealed class Scope(Scope? parent, HashSet<string> variables)
    {
        private readonly Dictionary<string, string> _literals = new(StringComparer.Ordinal);

        public bool IsVariable(string name) => variables.Contains(name) || (parent?.IsVariable(name) ?? false);

        public void Assign(string name, Expr? value)
        {
            if (value is StringLiteral text)
            {
                _literals[name] = text.Value;
            }
            else
            {
                _literals.Remove(name);
            }
        }

        public string? LiteralOf(string name) =>
            _literals.TryGetValue(name, out string? literal) ? literal : parent?.LiteralOf(name);
    }
}
