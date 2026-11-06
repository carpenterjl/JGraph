using System.Text.RegularExpressions;
using JGraph.Scripting.Jgs;

namespace JGraph.Scripting.Analysis;

/// <summary>
/// The Code Analyzer's walk over a file's parse tree (App Designer window plan W10): one scope per script,
/// function, local, nested and method, with the variables it assigns, the parameters and outputs, the
/// globals and persistents, and every read and assignment in textual order; from those the unused family
/// (INUSD, MANU, NASGU, ASGLU, STRNU, STOUT, NUSED, PUSE, PSET, NODEF, DEFNU, REDEFGG), the statements
/// that cannot run or do nothing (UNRCH, VUNUS, NOEFF, EQEFF, NOPTS, NOPRT), the loop checks (AGROW,
/// LAXES) and, at each call of a function, the pattern checks of <see cref="PatternChecks"/>. The rules
/// are R2025b's as measured (<c>w10_analyzer</c>, <c>w10_rules</c>).
/// </summary>
internal sealed partial class ScopeAnalysis
{
    private readonly SourceSpans _spans;
    private readonly List<CodeMessage> _sink;
    private readonly List<CodeComplexity> _complexity;
    private readonly string _stem;
    private readonly List<Scope> _scopes = [];
    private readonly HashSet<string> _functionReferences = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FnStmt> _localFunctions = new(StringComparer.Ordinal);
    private readonly List<(FnStmt Function, Scope Scope)> _candidates = [];
    private bool _usesEval;

    public ScopeAnalysis(SourceSpans spans, List<CodeMessage> sink, List<CodeComplexity> complexity, string stem, string? fileName = null)
    {
        _spans = spans;
        _sink = sink;
        _complexity = complexity;
        _stem = stem;
        _fileName = fileName;
    }

    /// <summary>The file's name or path as the analysis was asked, for MCDIR's text and the <c>@</c> folder it reads.</summary>
    private readonly string? _fileName;

    private enum AssignKind
    {
        Simple,
        Multi,
        Index,
        Brace,
        Member,
        For,
        Catch,
        Load,
    }

    private enum VarState
    {
        Empty,
        Other,
    }

    private sealed record Event(int Index, string Name, bool IsAssign, AssignKind Kind, Span Span, int Block, int MultiGroup = 0, int MultiCount = 0, bool Implicit = false);

    private sealed class Loop
    {
        public int Start;
        public int End = int.MaxValue;
        public int Block;
        public Dictionary<string, VarState> EntryState = [];
    }

    private sealed class Decl
    {
        public Span First;
        public bool Persistent;
        public List<Span> All = [];
    }

    private sealed class Scope
    {
        public FnStmt? Function;
        public Scope? Parent;
        public bool IsScript;
        public bool IsMethod;
        public bool IsStatic;
        public bool IsMain;

        /// <summary>A class's constructor: its output is the object being built, never unset (R2025b raises no STOUT there; stage P5, measured).</summary>
        public bool IsConstructor;
        public readonly List<string> Parameters = [];
        public readonly List<string> Outputs = [];
        public readonly HashSet<string> Assigned = new(StringComparer.Ordinal);
        public readonly HashSet<string> Shared = new(StringComparer.Ordinal);
        public readonly Dictionary<string, Decl> Declared = new(StringComparer.Ordinal);
        public readonly List<Event> Events = [];
        public readonly List<Loop> Loops = [];
        public readonly List<int> BlockParents = [];
        public readonly Dictionary<string, VarState> State = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> AssignCount = new(StringComparer.Ordinal);
        public readonly Dictionary<string, string> LiteralValue = new(StringComparer.Ordinal);
        public readonly HashSet<string> MentionedByNested = new(StringComparer.Ordinal);
        public readonly HashSet<string> NestedNames = new(StringComparer.Ordinal);
        public readonly HashSet<string> OwnReferences = new(StringComparer.Ordinal);
        public readonly List<Span> AnsMentions = [];
        public readonly Stack<Loop> OpenLoops = new();
        public readonly Stack<HashSet<string>> AnonymousParameters = new();
        public int McCabe = 1;
        public int Modified = 1;
        public int Block;

        public bool IsVariable(string name)
        {
            if (Assigned.Contains(name) || Parameters.Contains(name) || Outputs.Contains(name) || Declared.ContainsKey(name) || Shared.Contains(name))
            {
                return true;
            }

            return name == "ans";
        }

        public bool InLoop => OpenLoops.Count > 0;

        /// <summary>The text a variable holds throughout the scope: assigned once, a literal (R2025b reads such a constant where it is used).</summary>
        public string? KnownLiteral(string name) =>
            AssignCount.TryGetValue(name, out int count) && count == 1 && LiteralValue.TryGetValue(name, out string? value) ? value : null;

        public void CountAssignment(string name) => AssignCount[name] = AssignCount.TryGetValue(name, out int count) ? count + 1 : 1;

        public int NewBlock(int parent)
        {
            BlockParents.Add(parent);
            return BlockParents.Count - 1;
        }

        public bool IsAncestorOrSame(int ancestor, int block)
        {
            while (block >= 0)
            {
                if (block == ancestor)
                {
                    return true;
                }

                block = BlockParents[block];
            }

            return false;
        }
    }

    // --- the file ---------------------------------------------------------------------------------------

    /// <summary>Walks the file: the script's statements or the functions, the class's methods, the nested functions.</summary>
    public void Run(IReadOnlyList<Stmt> program, bool isScript)
    {
        _usesEval = false;
        var topFunctions = program.OfType<FnStmt>().ToList();
        foreach (FnStmt function in topFunctions)
        {
            _localFunctions.TryAdd(function.Name, function);
        }

        if (isScript)
        {
            var script = new Scope { IsScript = true };
            PreScan(program.Where(static s => s is not FnStmt).ToList(), script);
            _scopes.Add(script);
            WalkScope(script, program.Where(static s => s is not FnStmt).ToList());
            foreach (FnStmt function in topFunctions)
            {
                AnalyzeFunction(function, parent: null, isMethod: false, isStatic: false, isMain: false, candidate: true);
            }
        }
        else
        {
            bool first = true;
            foreach (Stmt statement in program)
            {
                switch (statement)
                {
                    case FnStmt function:
                        if (first)
                        {
                            CheckMainName(function);
                        }

                        AnalyzeFunction(function, parent: null, isMethod: false, isStatic: false, isMain: first, candidate: !first);
                        first = false;
                        break;
                    case ClassdefStmt classdef:
                        CheckClassName(classdef);
                        foreach (ClassMethod method in classdef.Methods)
                        {
                            bool constructor = string.Equals(method.Function.Name, classdef.Name, StringComparison.Ordinal);

                            // A method with no input in a block that is not Static (or Abstract): R2025b's NOIN
                            // over a bodied method and a signature line alike, the constructor excepted (stage P5, measured).
                            if (!method.Static && !method.Abstract && !constructor && method.Function.Parameters.Count == 0)
                            {
                                Emit(CodeChecks.NOIN, _spans.SignatureName(method.Function), text: string.Format(CodeChecks.NOIN.Text, method.Function.Name));
                            }

                            // A signature line has no body to walk: its file is a function of its own (P4).
                            if (!method.Abstract && !method.Signature && method.Function.Body.Count + method.Function.Parameters.Count > 0)
                            {
                                AnalyzeFunction(method.Function, parent: null, isMethod: true, isStatic: method.Static, isMain: false, candidate: false, isConstructor: constructor);
                            }
                        }

                        first = false;
                        break;
                    default:
                        first = false;
                        break;
                }
            }
        }

        Resolve();
    }

    /// <summary>
    /// FNDEF and FNDOT over a file's main function (the packages and class folders plan, stage P5, ADR 0311):
    /// a name that is not the file's, case and all, is known by the file's name (a fix writes the file's);
    /// a dotted name is refused as well, at the same span. R2025b checks the first function alone, in a
    /// package, a class folder and a private folder as in a plain one (measured).
    /// </summary>
    private void CheckMainName(FnStmt function)
    {
        if (_stem.Length == 0)
        {
            return;
        }

        Span name = _spans.Signature(function).Name;
        if (!string.Equals(function.Name, _stem, StringComparison.Ordinal))
        {
            Emit(CodeChecks.FNDEF, name, CodeFix.Of(name.Line, name.Start, name.End, _stem), string.Format(CodeChecks.FNDEF.Text, function.Name, _stem));
        }

        if (function.Name.Contains('.', StringComparison.Ordinal))
        {
            Emit(CodeChecks.FNDOT, name);
        }
    }

    /// <summary>
    /// MCDIR and MCFIL over a class file (stage P5, ADR 0311): a class named otherwise than its <c>@</c>
    /// folder is refused with the file's path in the text and no fix; one named otherwise than its file
    /// is refused with a fix that renames the class and its constructor. Both sit on the class name.
    /// </summary>
    private void CheckClassName(ClassdefStmt classdef)
    {
        if (_stem.Length == 0 || string.Equals(classdef.Name, _stem, StringComparison.Ordinal))
        {
            return;
        }

        var name = new Span(classdef.Line, classdef.NameColumn, classdef.NameColumn + classdef.Name.Length - 1);
        if (_fileName is { } file && Path.GetFileName(Path.GetDirectoryName(file) ?? string.Empty) is { Length: > 1 } folder && folder[0] == '@')
        {
            Emit(CodeChecks.MCDIR, name, text: string.Format(CodeChecks.MCDIR.Text, classdef.Name, file));
        }

        var edits = new List<CodeEdit> { new(name.Line, name.Start, name.End, _stem) };
        foreach (ClassMethod method in classdef.Methods)
        {
            if (string.Equals(method.Function.Name, classdef.Name, StringComparison.Ordinal))
            {
                Span constructor = _spans.SignatureName(method.Function);
                edits.Add(new CodeEdit(constructor.Line, constructor.Start, constructor.End, _stem));
            }
        }

        Emit(CodeChecks.MCFIL, name, new CodeFix(edits), string.Format(CodeChecks.MCFIL.Text, classdef.Name, _stem));
    }

    private void AnalyzeFunction(FnStmt function, Scope? parent, bool isMethod, bool isStatic, bool isMain, bool candidate, bool isConstructor = false)
    {
        var scope = new Scope { Function = function, Parent = parent, IsMethod = isMethod, IsStatic = isStatic, IsMain = isMain, IsConstructor = isConstructor };
        scope.Parameters.AddRange(function.Parameters);
        scope.Outputs.AddRange(function.Outputs);
        PreScan(function.Body, scope);
        if (parent is not null)
        {
            // A nested function shares the variables its parents have (and it mentions).
            for (Scope? ancestor = parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                foreach (string name in Mentions(function))
                {
                    if (ancestor.IsVariable(name) && !scope.Parameters.Contains(name) && !scope.Outputs.Contains(name))
                    {
                        scope.Shared.Add(name);
                    }
                }
            }
        }

        _scopes.Add(scope);
        if (candidate)
        {
            _candidates.Add((function, scope));
        }

        WalkScope(scope, function.Body);
        foreach (FnStmt nested in function.Body.OfType<FnStmt>())
        {
            AnalyzeFunction(nested, scope, isMethod: false, isStatic: false, isMain: false, candidate: true);
        }
    }

    /// <summary>The names a nested function's body mentions, as variables or not, so the parent knows what it shares.</summary>
    private static HashSet<string> Mentions(FnStmt function)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        void Expr(Jgs.Expr? e)
        {
            switch (e)
            {
                case null:
                    break;
                case VariableExpr v:
                    names.Add(v.Name);
                    break;
                case CallExpr c:
                    Expr(c.Callee);
                    foreach (Jgs.Expr a in c.Arguments)
                    {
                        Expr(a);
                    }

                    break;
                case IndexExpr i:
                    Expr(i.Target);
                    foreach (Jgs.Expr a in i.Indices)
                    {
                        Expr(a);
                    }

                    break;
                case BraceIndexExpr b:
                    Expr(b.Target);
                    foreach (Jgs.Expr a in b.Indices)
                    {
                        Expr(a);
                    }

                    break;
                case MemberExpr m:
                    Expr(m.Target);
                    Expr(m.FieldName);
                    break;
                case BinaryExpr bi:
                    Expr(bi.Left);
                    Expr(bi.Right);
                    break;
                case LogicalExpr lo:
                    Expr(lo.Left);
                    Expr(lo.Right);
                    break;
                case UnaryExpr u:
                    Expr(u.Operand);
                    break;
                case TransposeExpr t:
                    Expr(t.Operand);
                    break;
                case RangeExpr r:
                    Expr(r.Start);
                    Expr(r.Step);
                    Expr(r.Stop);
                    break;
                case ArrayLiteral al:
                    foreach (Jgs.Expr a in al.Elements)
                    {
                        Expr(a);
                    }

                    break;
                case MatrixLiteral ml:
                    foreach (IReadOnlyList<Jgs.Expr> row in ml.Rows)
                    {
                        foreach (Jgs.Expr a in row)
                        {
                            Expr(a);
                        }
                    }

                    break;
                case CellLiteral cl:
                    foreach (IReadOnlyList<Jgs.Expr> row in cl.Rows)
                    {
                        foreach (Jgs.Expr a in row)
                        {
                            Expr(a);
                        }
                    }

                    break;
                case AnonymousFnExpr an:
                    Expr(an.Body);
                    break;
                case AssignExpr asg:
                    Expr(asg.Target);
                    Expr(asg.Value);
                    break;
            }
        }

        void Block(IReadOnlyList<Stmt> block)
        {
            foreach (Stmt s in block)
            {
                switch (s)
                {
                    case ExprStmt es:
                        Expr(es.Expression);
                        break;
                    case MultiAssignStmt ma:
                        foreach (Jgs.Expr? t in ma.Targets)
                        {
                            Expr(t);
                        }

                        Expr(ma.Call);
                        break;
                    case IfStmt ifs:
                        Expr(ifs.Condition);
                        Block(ifs.Then);
                        if (ifs.Else is not null)
                        {
                            Block(ifs.Else);
                        }

                        break;
                    case WhileStmt w:
                        Expr(w.Condition);
                        Block(w.Body);
                        break;
                    case ForStmt f:
                        names.Add(f.Variable);
                        Expr(f.Iterable);
                        Block(f.Body);
                        break;
                    case SwitchStmt sw:
                        Expr(sw.Subject);
                        foreach (SwitchCase c in sw.Cases)
                        {
                            Expr(c.Value);
                            Block(c.Body);
                        }

                        if (sw.Otherwise is not null)
                        {
                            Block(sw.Otherwise);
                        }

                        break;
                    case TryStmt t:
                        Block(t.Body);
                        Block(t.Handler);
                        break;
                    case ReturnStmt r:
                        Expr(r.Value);
                        break;
                    case FnStmt n:
                        Block(n.Body);
                        break;
                    case GlobalStmt g:
                        names.UnionWith(g.Names);
                        break;
                    case PersistentStmt p:
                        names.UnionWith(p.Names);
                        break;
                }
            }
        }

        Block(function.Body);
        return names;
    }

    /// <summary>What a scope assigns anywhere in its own statements (not its nested functions'): what makes a name a variable.</summary>
    private void PreScan(IReadOnlyList<Stmt> block, Scope scope)
    {
        foreach (Stmt statement in block)
        {
            switch (statement)
            {
                case ExprStmt { Expression: AssignExpr assign }:
                    if (RootName(assign.Target) is { } root)
                    {
                        scope.Assigned.Add(root);
                        scope.CountAssignment(root);
                        if (assign.Target is VariableExpr && assign.Value is StringLiteral literal)
                        {
                            scope.LiteralValue[root] = literal.Value;
                        }
                    }

                    PreScanExpr(assign.Value, scope);
                    break;
                case ExprStmt { Expression: CallExpr { Callee: VariableExpr { Name: "load" } } load }:
                    foreach (Expr argument in load.Arguments.Skip(1))
                    {
                        if (argument is StringLiteral { Value: var name } && Regex.IsMatch(name, "^[A-Za-z]\\w*$"))
                        {
                            scope.Assigned.Add(name);
                        }
                    }

                    break;
                case ExprStmt es:
                    PreScanExpr(es.Expression, scope);
                    break;
                case MultiAssignStmt multi:
                    foreach (Expr? target in multi.Targets)
                    {
                        if (target is not null && RootName(target) is { } name)
                        {
                            scope.Assigned.Add(name);
                            scope.CountAssignment(name);
                        }
                    }

                    break;
                case IfStmt ifs:
                    PreScan(ifs.Then, scope);
                    if (ifs.Else is not null)
                    {
                        PreScan(ifs.Else, scope);
                    }

                    break;
                case WhileStmt w:
                    PreScan(w.Body, scope);
                    break;
                case ForStmt f:
                    scope.Assigned.Add(f.Variable);
                    scope.CountAssignment(f.Variable);
                    PreScan(f.Body, scope);
                    break;
                case SwitchStmt sw:
                    foreach (SwitchCase c in sw.Cases)
                    {
                        PreScan(c.Body, scope);
                    }

                    if (sw.Otherwise is not null)
                    {
                        PreScan(sw.Otherwise, scope);
                    }

                    break;
                case TryStmt t:
                    PreScan(t.Body, scope);
                    if (t.ErrorVariable is not null)
                    {
                        scope.Assigned.Add(t.ErrorVariable);
                        scope.CountAssignment(t.ErrorVariable);
                    }

                    PreScan(t.Handler, scope);
                    break;
                case GlobalStmt g:
                    foreach (string name in g.Names)
                    {
                        scope.Declared.TryAdd(name, new Decl());
                    }

                    break;
                case PersistentStmt p:
                    foreach (string name in p.Names)
                    {
                        scope.Declared.TryAdd(name, new Decl { Persistent = true });
                    }

                    break;
                case FnStmt nested:
                    scope.NestedNames.Add(nested.Name);
                    scope.MentionedByNested.UnionWith(Mentions(nested));
                    break;
                case LetStmt let:
                    scope.Assigned.Add(let.Name);
                    break;
                case DestructuringLetStmt lets:
                    scope.Assigned.UnionWith(lets.Names);
                    break;
            }
        }

        // The declarations are variables too; a parent's variables a nested function mentions are shared.
        foreach (string name in scope.MentionedByNested)
        {
            if (scope.Assigned.Contains(name) || scope.Parameters.Contains(name) || scope.Outputs.Contains(name) || scope.Declared.ContainsKey(name))
            {
                scope.Shared.Add(name);
            }
        }
    }

    /// <summary>Assignments inside expressions (JGS's <c>a = b = 0</c>, or a condition's), rare in MATLAB.</summary>
    private static void PreScanExpr(Expr expression, Scope scope)
    {
        if (expression is AssignExpr assign && RootName(assign.Target) is { } root)
        {
            scope.Assigned.Add(root);
            scope.CountAssignment(root);
        }
    }

    /// <summary>The variable at the root of an assignment target: <c>x</c> of <c>x</c>, <c>x(1)</c>, <c>x.f(2).g</c>, <c>x{3}</c>.</summary>
    private static string? RootName(Expr target) => target switch
    {
        VariableExpr v => v.Name,
        CallExpr c => RootName(c.Callee),
        IndexExpr i => RootName(i.Target),
        BraceIndexExpr b => RootName(b.Target),
        MemberExpr m => RootName(m.Target),
        _ => null,
    };

    private static bool HasMember(Expr target) => target switch
    {
        MemberExpr => true,
        CallExpr c => HasMember(c.Callee),
        IndexExpr i => HasMember(i.Target),
        BraceIndexExpr b => HasMember(b.Target),
        _ => false,
    };

    // --- the walk ---------------------------------------------------------------------------------------

    private void WalkScope(Scope scope, IReadOnlyList<Stmt> body)
    {
        scope.Block = scope.NewBlock(-1);
        WalkBlock(scope, body);
        string name = scope.Function?.Name ?? _stem;
        Span nameSpan = scope.Function is { } function ? _spans.Signature(function).Name : new Span(1, 1, 1);
        _complexity.Add(new CodeComplexity(name, nameSpan.Line, nameSpan.Start, nameSpan.End, scope.McCabe, scope.Modified, scope.IsScript));
    }

    private void WalkBlock(Scope scope, IReadOnlyList<Stmt> block)
    {
        bool terminated = false;
        foreach (Stmt statement in block)
        {
            if (terminated)
            {
                Span span = _spans.StatementSpan(statement);
                Emit(CodeChecks.UNRCH, span);
                terminated = false; // once per block
            }

            WalkStatement(scope, statement);
            terminated = Terminates(statement);
        }
    }

    private static bool Terminates(Stmt statement) => statement switch
    {
        ReturnStmt or BreakStmt or ContinueStmt => true,
        ExprStmt { Expression: CallExpr { Callee: VariableExpr { Name: "error" or "throw" or "rethrow" } } } => true,
        WhileStmt { Condition: VariableExpr { Name: "true" } or BoolLiteral { Value: true } or NumberLiteral { Value: not 0 } } loop => !Breaks(loop.Body),
        _ => false,
    };

    private static bool Breaks(IReadOnlyList<Stmt> block)
    {
        foreach (Stmt statement in block)
        {
            switch (statement)
            {
                case BreakStmt:
                    return true;
                case IfStmt ifs when Breaks(ifs.Then) || (ifs.Else is not null && Breaks(ifs.Else)):
                    return true;
                case SwitchStmt sw when sw.Cases.Any(c => Breaks(c.Body)) || (sw.Otherwise is not null && Breaks(sw.Otherwise)):
                    return true;
                case TryStmt t when Breaks(t.Body) || Breaks(t.Handler):
                    return true;
            }
        }

        return false;
    }

    private void WalkStatement(Scope scope, Stmt statement)
    {
        switch (statement)
        {
            case ExprStmt { Expression: AssignExpr assign } es:
                WalkAssign(scope, assign, es);
                break;
            case ExprStmt es:
                WalkExpressionStatement(scope, es);
                break;
            case MultiAssignStmt multi:
                Visit(scope, multi.Call);
                int group = scope.Events.Count + 1;
                int count = multi.Targets.Count;
                foreach (Expr? target in multi.Targets)
                {
                    if (target is not null)
                    {
                        Assign(scope, target, AssignKind.Multi, group, count);
                    }
                }

                if (!multi.Suppressed)
                {
                    int start = _spans.IndexOf(multi);
                    int close = start >= 0 ? _spans.MatchingClose(start) : -1;
                    if (close >= 0 && close + 1 < _spans.Tokens.Count && _spans.Tokens[close + 1].Type == TokenType.Assign)
                    {
                        Span equals = _spans.TokenSpan(close + 1);
                        EmitNoSemicolon(scope, multi, equals);
                    }
                }

                break;
            case IfStmt ifs:
                scope.McCabe++;
                scope.Modified++;
                Visit(scope, ifs.Condition);
                WalkNested(scope, ifs.Then);
                if (ifs.Else is not null)
                {
                    WalkNested(scope, ifs.Else);
                }

                break;
            case WhileStmt w:
                scope.McCabe++;
                scope.Modified++;
                Visit(scope, w.Condition);
                WalkLoop(scope, w.Body);
                break;
            case ForStmt f:
                scope.McCabe++;
                scope.Modified++;
                Visit(scope, f.Iterable);
                int at = _spans.IndexOf(f);
                if (at >= 0 && at + 1 < _spans.Tokens.Count && _spans.Tokens[at + 1].Type == TokenType.LParen)
                {
                    Emit(CodeChecks.NO4LP, _spans.TokenSpan(at + 1));
                }

                scope.Events.Add(new Event(scope.Events.Count, f.Variable, true, AssignKind.For, new Span(f.Line, f.Column, f.Column), scope.Block));
                scope.State[f.Variable] = VarState.Other;
                WalkLoop(scope, f.Body);
                break;
            case SwitchStmt sw:
                scope.McCabe += sw.Cases.Count;
                scope.Modified += sw.Cases.Count > 0 ? 1 : 0;
                Visit(scope, sw.Subject);
                foreach (SwitchCase c in sw.Cases)
                {
                    Visit(scope, c.Value);
                    WalkNested(scope, c.Body);
                }

                if (sw.Otherwise is not null)
                {
                    WalkNested(scope, sw.Otherwise);
                }

                break;
            case TryStmt t:
                scope.McCabe++;
                scope.Modified++;
                WalkNested(scope, t.Body);
                if (t.ErrorVariable is { } error)
                {
                    scope.Events.Add(new Event(scope.Events.Count, error, true, AssignKind.Catch, new Span(t.Line, t.Column, t.Column), scope.Block));
                    scope.State[error] = VarState.Other;
                }

                WalkNested(scope, t.Handler);
                break;
            case ReturnStmt r:
                if (r.Value is not null)
                {
                    Visit(scope, r.Value);
                }

                break;
            case GlobalStmt g:
                Emit(CodeChecks.GVMIS, new Span(g.Line, g.Column, g.Column + 5));
                Declare(scope, g, g.Names, persistent: false);
                break;
            case PersistentStmt p:
                Declare(scope, p, p.Names, persistent: true);
                break;
            case FnStmt:
                // Nested: analysed as its own scope after this one.
                break;
            case LetStmt let:
                Visit(scope, let.Value);
                scope.Events.Add(new Event(scope.Events.Count, let.Name, true, AssignKind.Simple, new Span(let.Line, let.Column, let.Column), scope.Block));
                break;
            case ArgumentsStmt arguments:
                foreach (ArgumentSpec spec in arguments.Arguments)
                {
                    if (spec.Default is not null)
                    {
                        Visit(scope, spec.Default);
                    }
                }

                break;
        }
    }

    private void WalkNested(Scope scope, IReadOnlyList<Stmt> block)
    {
        int outer = scope.Block;
        scope.Block = scope.NewBlock(outer);
        WalkBlock(scope, block);
        scope.Block = outer;
    }

    private void WalkLoop(Scope scope, IReadOnlyList<Stmt> body)
    {
        var loop = new Loop { Start = scope.Events.Count };
        if (scope.OpenLoops.Count == 0)
        {
            loop.EntryState = new Dictionary<string, VarState>(scope.State, StringComparer.Ordinal);
        }
        else
        {
            loop.EntryState = scope.OpenLoops.Peek().EntryState;
        }

        int outer = scope.Block;
        scope.Block = scope.NewBlock(outer);
        loop.Block = scope.Block;
        scope.OpenLoops.Push(loop);
        WalkBlock(scope, body);
        scope.OpenLoops.Pop();
        loop.End = scope.Events.Count;
        scope.Loops.Add(loop);
        scope.Block = outer;
    }

    private void Declare(Scope scope, Stmt statement, IReadOnlyList<string> names, bool persistent)
    {
        List<Span> spans = _spans.NamesAfter(statement);
        for (int i = 0; i < names.Count; i++)
        {
            Span span = i < spans.Count ? spans[i] : new Span(statement.Line, statement.Column, statement.Column);
            if (!scope.Declared.TryGetValue(names[i], out Decl? decl))
            {
                decl = new Decl { Persistent = persistent };
                scope.Declared[names[i]] = decl;
            }

            if (decl.All.Count == 0)
            {
                decl.First = span;
            }
            else if (!persistent)
            {
                Emit(CodeChecks.REDEFGG, span);
            }

            decl.All.Add(span);
        }
    }

    private void WalkAssign(Scope scope, AssignExpr assign, ExprStmt statement)
    {
        Visit(scope, assign.Value);
        bool selfConcat = RootName(assign.Target) is { } name && assign.Target is VariableExpr && ConcatenatesItself(assign.Value, name);
        Assign(scope, assign.Target, AssignKind.Simple, 0, 0, assign.Value, selfConcat);
        if (!statement.Suppressed)
        {
            EmitNoSemicolon(scope, statement, new Span(assign.Line, assign.Column, assign.Column));
        }
    }

    private static bool ConcatenatesItself(Expr value, string name)
    {
        IEnumerable<Expr> elements = value switch
        {
            ArrayLiteral a => a.Elements,
            MatrixLiteral m => m.Rows.SelectMany(static r => r),
            CellLiteral c => c.Rows.SelectMany(static r => r),
            _ => [],
        };
        return elements.Any(e => e is VariableExpr v && v.Name == name);
    }

    private static bool IsEmptyLiteral(Expr value) => value switch
    {
        ArrayLiteral { Elements.Count: 0 } => true,
        MatrixLiteral m => m.Rows.Count == 0 || m.Rows.All(static r => r.Count == 0),
        CellLiteral c => c.Rows.Count == 0 || c.Rows.All(static r => r.Count == 0),
        StringLiteral { Value.Length: 0 } => true,
        _ => false,
    };

    /// <summary>Records an assignment to <paramref name="target"/>: an indexed or field target reads its variable first.</summary>
    private void Assign(Scope scope, Expr target, AssignKind kind, int group, int count, Expr? value = null, bool selfConcat = false)
    {
        string? name = RootName(target);
        if (name is null)
        {
            Visit(scope, target);
            return;
        }

        bool indexed = target is not VariableExpr;
        if (indexed)
        {
            // The variable is read, then its subscripts, then it is assigned.
            Span nameSpan = _spans.NameSpan(Root(target));
            if (scope.IsVariable(name))
            {
                scope.Events.Add(new Event(scope.Events.Count, name, false, AssignKind.Simple, nameSpan, scope.Block, Implicit: true));
            }

            bool endPlus = false;
            VisitSubscripts(scope, target, ref endPlus);
            kind = HasMember(target) ? AssignKind.Member : target is BraceIndexExpr ? AssignKind.Brace : AssignKind.Index;
            if (scope.InLoop && !scope.Shared.Contains(name))
            {
                Loop outer = scope.OpenLoops.Last();
                bool fresh = !outer.EntryState.TryGetValue(name, out VarState state)
                    ? !scope.Parameters.Contains(name) && !scope.Declared.ContainsKey(name)
                    : state == VarState.Empty;
                if (endPlus || fresh)
                {
                    Emit(CodeChecks.AGROW, nameSpan);
                }
            }

            scope.Events.Add(new Event(scope.Events.Count, name, true, kind, nameSpan, scope.Block, group, count));
            scope.State[name] = VarState.Other;
            return;
        }

        var variable = (VariableExpr)target;
        var span = new Span(variable.Line, variable.Column, variable.Column + name.Length - 1);
        if (name == "ans")
        {
            scope.AnsMentions.Add(span); // NOANS after the unused family, as R2025b lists them on one span
        }

        if (scope.InLoop && selfConcat && !scope.Shared.Contains(name))
        {
            Emit(CodeChecks.AGROW, span);
        }

        scope.Events.Add(new Event(scope.Events.Count, name, true, kind, span, scope.Block, group, count));
        scope.State[name] = value is not null && IsEmptyLiteral(value) ? VarState.Empty : VarState.Other;
    }

    private static Expr Root(Expr target) => target switch
    {
        CallExpr c => Root(c.Callee),
        IndexExpr i => Root(i.Target),
        BraceIndexExpr b => Root(b.Target),
        MemberExpr m => Root(m.Target),
        _ => target,
    };

    private void VisitSubscripts(Scope scope, Expr target, ref bool endPlus)
    {
        switch (target)
        {
            case CallExpr c:
                VisitSubscripts(scope, c.Callee, ref endPlus);
                foreach (Expr a in c.Arguments)
                {
                    endPlus |= IsEndPlus(a);
                    Visit(scope, a);
                }

                break;
            case IndexExpr i:
                VisitSubscripts(scope, i.Target, ref endPlus);
                foreach (Expr a in i.Indices)
                {
                    endPlus |= IsEndPlus(a);
                    Visit(scope, a);
                }

                break;
            case BraceIndexExpr b:
                VisitSubscripts(scope, b.Target, ref endPlus);
                foreach (Expr a in b.Indices)
                {
                    endPlus |= IsEndPlus(a);
                    Visit(scope, a);
                }

                break;
            case MemberExpr m:
                VisitSubscripts(scope, m.Target, ref endPlus);
                if (m.FieldName is not null)
                {
                    Visit(scope, m.FieldName);
                }

                break;
        }
    }

    private static bool IsEndPlus(Expr index) => index is BinaryExpr { Op: TokenType.Plus, Left: EndExpr };

    private void WalkExpressionStatement(Scope scope, ExprStmt statement)
    {
        Expr e = statement.Expression;
        bool function = !scope.IsScript;

        // The statement-level call checks: clear, load, subsasgn, disp(sprintf(...)); a bare name is a call without arguments.
        if (e is CallExpr { Callee: VariableExpr callee } call && !scope.IsVariable(callee.Name))
        {
            PatternChecks.StatementCall(this, scope, call, callee);
        }
        else if (e is VariableExpr bare && !scope.IsVariable(bare.Name))
        {
            PatternChecks.StatementCall(this, scope, new CallExpr(bare, []) { Line = bare.Line, Column = bare.Column }, bare);
        }

        Visit(scope, e);
        if (statement.Suppressed)
        {
            // A value computed and dropped.
            switch (e)
            {
                case BinaryExpr { Op: TokenType.EqualEqual } eq:
                    Emit(CodeChecks.EQEFF, new Span(eq.Line, eq.Column, eq.Column + 1), CodeFix.Of(eq.Line, eq.Column, eq.Column + 1, "="));
                    break;
                case BinaryExpr binary:
                    EmitValue(binary, SourceSpans.OperatorText(binary.Op), binary.Op is TokenType.DotStar or TokenType.DotSlash or TokenType.DotCaret or TokenType.DotBackslash or TokenType.EqualEqual or TokenType.BangEqual or TokenType.LessEqual or TokenType.GreaterEqual ? 2 : 1);
                    break;
                case LogicalExpr logical:
                    EmitValue(logical, SourceSpans.OperatorText(logical.Op), 2);
                    break;
                case UnaryExpr unary:
                    EmitValue(unary, SourceSpans.OperatorText(unary.Op), 1);
                    break;
                case TransposeExpr transpose:
                    Emit(CodeChecks.VUNUS, new Span(transpose.Line, transpose.Column, transpose.Column + (transpose.Conjugate ? 0 : 1)), text: "' produces a value that might be unused.");
                    break;
                case RangeExpr range:
                    EmitValue(range, ":", 1);
                    break;
                case ArrayLiteral or MatrixLiteral:
                    EmitValue(e, "[", 1);
                    break;
                case CellLiteral:
                    EmitValue(e, "{", 1);
                    break;
                case BraceIndexExpr:
                    EmitValue(e, "{", 1);
                    break;
                case VariableExpr v when function && scope.IsVariable(v.Name):
                    EmitValue(v, v.Name, v.Name.Length);
                    break;
                case CallExpr { Callee: VariableExpr v } when !function && scope.IsVariable(v.Name):
                {
                    int start = _spans.IndexOf(statement);
                    int end = start >= 0 ? _spans.StatementEnd(start) : -1;
                    if (end >= 0 && end + 1 < _spans.Tokens.Count && _spans.Tokens[end + 1].Type == TokenType.Semicolon && _spans.Tokens[end + 1].Line == statement.Line)
                    {
                        end++;
                    }

                    if (end >= 0 && _spans.Tokens[end].Line == statement.Line)
                    {
                        var span = new Span(statement.Line, statement.Column, _spans.EndColumn(end));
                        Emit(CodeChecks.NOEFF, span, text: string.Format(CodeChecks.NOEFF.Text, _spans.Text(span)));
                    }

                    break;
                }
            }
        }
        else if (Displays(scope, e))
        {
            EmitNoSemicolon(scope, statement, new Span(statement.Line, statement.Column, statement.Column));
        }
    }

    /// <summary>Whether a statement without a semicolon displays a value, which a call of a function (a command) is not taken to.</summary>
    private static bool Displays(Scope scope, Expr e) => e switch
    {
        CallExpr { Callee: VariableExpr v } => scope.IsVariable(v.Name),
        CallExpr => false,
        VariableExpr v => scope.IsVariable(v.Name),
        FunctionHandleExpr or AnonymousFnExpr => false,
        _ => true,
    };

    private void EmitValue(Expr e, string what, int width)
    {
        var span = new Span(e.Line, e.Column, e.Column + width - 1);
        Emit(CodeChecks.VUNUS, span, text: string.Format(CodeChecks.VUNUS.Text, what));
    }

    private void EmitNoSemicolon(Scope scope, Stmt statement, Span at)
    {
        int start = _spans.IndexOf(statement);
        CodeFix? fix = null;
        if (start >= 0)
        {
            int end = _spans.StatementEnd(start);
            int terminator = Math.Min(end + 1, _spans.Tokens.Count - 1);
            Token t = _spans.Tokens[terminator];
            if (t.Type == TokenType.Comma && t.Line == _spans.Tokens[end].Line)
            {
                fix = CodeFix.Of(t.Line, t.Column, t.Column, ";");
            }
            else
            {
                int column = _spans.EndColumn(end);
                fix = CodeFix.Of(_spans.Tokens[end].Line, column + 1, column, ";");
            }
        }

        Emit(scope.IsScript ? CodeChecks.NOPTS : CodeChecks.NOPRT, at, fix);
    }

    // --- expressions ------------------------------------------------------------------------------------

    private void Visit(Scope scope, Expr e)
    {
        switch (e)
        {
            case VariableExpr v:
                Mention(scope, v, new Span(v.Line, v.Column, v.Column + v.Name.Length - 1), isCall: false, arguments: null);
                break;
            case CallExpr { Callee: VariableExpr v } call:
                if (Mention(scope, v, new Span(v.Line, v.Column, v.Column + v.Name.Length - 1), isCall: true, arguments: call.Arguments))
                {
                    PatternChecks.Call(this, scope, call, v);
                }

                foreach (Expr a in call.Arguments)
                {
                    Visit(scope, a);
                }

                break;
            case CallExpr call:
                Visit(scope, call.Callee);
                foreach (Expr a in call.Arguments)
                {
                    Visit(scope, a);
                }

                break;
            case IndexExpr index:
                Visit(scope, index.Target);
                foreach (Expr a in index.Indices)
                {
                    Visit(scope, a);
                }

                break;
            case BraceIndexExpr brace:
                Visit(scope, brace.Target);
                foreach (Expr a in brace.Indices)
                {
                    Visit(scope, a);
                }

                break;
            case MemberExpr member:
                Visit(scope, member.Target);
                if (member.FieldName is not null)
                {
                    Visit(scope, member.FieldName);
                }

                break;
            case BinaryExpr binary:
                PatternChecks.Comparison(this, scope, binary);
                Visit(scope, binary.Left);
                Visit(scope, binary.Right);
                break;
            case LogicalExpr logical:
                if (logical.Op is TokenType.AmpAmp or TokenType.PipePipe)
                {
                    scope.McCabe++;
                    scope.Modified++;
                }

                Visit(scope, logical.Left);
                Visit(scope, logical.Right);
                break;
            case UnaryExpr unary:
                Visit(scope, unary.Operand);
                break;
            case TransposeExpr transpose:
                Visit(scope, transpose.Operand);
                break;
            case RangeExpr range:
                Visit(scope, range.Start);
                if (range.Step is not null)
                {
                    Visit(scope, range.Step);
                }

                Visit(scope, range.Stop);
                break;
            case ArrayLiteral array:
                PatternChecks.Brackets(this, scope, array, array.Elements);
                foreach (Expr a in array.Elements)
                {
                    Visit(scope, a);
                }

                break;
            case MatrixLiteral matrix:
                PatternChecks.Brackets(this, scope, matrix, matrix.Rows.Count == 1 ? matrix.Rows[0] : []);
                foreach (IReadOnlyList<Expr> row in matrix.Rows)
                {
                    foreach (Expr a in row)
                    {
                        Visit(scope, a);
                    }
                }

                break;
            case CellLiteral cell:
                foreach (IReadOnlyList<Expr> row in cell.Rows)
                {
                    foreach (Expr a in row)
                    {
                        Visit(scope, a);
                    }
                }

                break;
            case AnonymousFnExpr anonymous:
                scope.AnonymousParameters.Push(new HashSet<string>(anonymous.Parameters, StringComparer.Ordinal));
                Visit(scope, anonymous.Body);
                scope.AnonymousParameters.Pop();
                break;
            case FunctionHandleExpr handle:
            {
                _functionReferences.Add(handle.Name);
                scope.OwnReferences.Add(handle.Name);
                var span = new Span(handle.Line, handle.Column + 1, handle.Column + handle.Name.Length);
                if (CodeChecks.ByFunctionName.TryGetValue(handle.Name, out CodeChecks.Check? check))
                {
                    Emit(check, span);
                }

                break;
            }

            case AssignExpr assign:
                Visit(scope, assign.Value);
                Assign(scope, assign.Target, AssignKind.Simple, 0, 0, assign.Value);
                break;
            case IncDecExpr incDec:
                Visit(scope, incDec.Target);
                break;
        }
    }

    /// <summary>A name read: a variable's read is recorded; a function's is a reference. True when the name is a function.</summary>
    private bool Mention(Scope scope, VariableExpr v, Span span, bool isCall, IReadOnlyList<Expr>? arguments)
    {
        string name = v.Name;
        if (scope.AnonymousParameters.Any(p => p.Contains(name)))
        {
            return false;
        }

        if (name == "nargin" || name == "nargout")
        {
            return false;
        }

        if (scope.IsVariable(name))
        {
            if (name == "ans")
            {
                scope.AnsMentions.Add(span);
            }

            scope.Events.Add(new Event(scope.Events.Count, name, false, AssignKind.Simple, span, scope.Block));
            return false;
        }

        _functionReferences.Add(name);
        scope.OwnReferences.Add(name);
        if (name is "eval" or "feval" or "evalin" or "evalc" or "str2func")
        {
            _usesEval = true;
        }

        if (CodeChecks.ByFunctionName.TryGetValue(name, out CodeChecks.Check? check) && !(name == "str2num" && !isCall))
        {
            Emit(check, span);
        }

        if (isCall && arguments is not null && _localFunctions.TryGetValue(name, out FnStmt? local) && !ReferenceEquals(local, scope.Function))
        {
            CheckArity(local, arguments.Count, span);
        }

        return true;
    }

    /// <summary>LTARG: a local function called with fewer arguments than it reads, with no <c>nargin</c> to tell.</summary>
    private void CheckArity(FnStmt local, int given, Span span)
    {
        if (given >= local.Parameters.Count || local.Parameters.Contains("varargin") || local.Parameters.Contains("~"))
        {
            return;
        }

        HashSet<string> mentions = Mentions(local);
        if (mentions.Contains("nargin") || mentions.Contains("exist") || mentions.Contains("varargin"))
        {
            return;
        }

        for (int i = given; i < local.Parameters.Count; i++)
        {
            if (mentions.Contains(local.Parameters[i]))
            {
                Emit(CodeChecks.LTARG, span);
                return;
            }
        }
    }

    // --- what the events say -----------------------------------------------------------------------------

    private void Resolve()
    {
        foreach (Scope scope in _scopes)
        {
            ResolveScope(scope);
        }

        if (!_usesEval)
        {
            foreach ((FnStmt function, Scope scope) in _candidates)
            {
                bool referenced = _scopes.Any(s => !ReferenceEquals(s, scope) && s.OwnReferences.Contains(function.Name));
                if (!referenced)
                {
                    Emit(CodeChecks.DEFNU, _spans.Signature(function).Name);
                }
            }
        }
    }

    private void ResolveScope(Scope scope)
    {
        List<Event> events = scope.Events;
        var readsByName = new Dictionary<string, List<Event>>(StringComparer.Ordinal);
        var assignsByName = new Dictionary<string, List<Event>>(StringComparer.Ordinal);
        foreach (Event e in events)
        {
            Dictionary<string, List<Event>> table = e.IsAssign ? assignsByName : readsByName;
            if (!table.TryGetValue(e.Name, out List<Event>? list))
            {
                list = [];
                table[e.Name] = list;
            }

            list.Add(e);
        }

        // Parameters, outputs, declarations.
        if (scope.Function is { } function)
        {
            (List<Span> outputs, _, List<(string Text, Span Span)> parameters) = _spans.Signature(function);
            for (int i = 0; i < parameters.Count; i++)
            {
                string p = parameters[i].Text;
                if (p == "~" || p == "varargin" || scope.Shared.Contains(p))
                {
                    continue;
                }

                if (!readsByName.ContainsKey(p) && !scope.MentionedByNested.Contains(p))
                {
                    bool objectArgument = scope.IsMethod && !scope.IsStatic && i == 0;
                    Span span = parameters[i].Span;
                    Emit(objectArgument ? CodeChecks.MANU : CodeChecks.INUSD, span, objectArgument ? null : CodeFix.Of(span.Line, span.Start, span.End, "~"));
                }
            }

            for (int i = 0; i < function.Outputs.Count && i < outputs.Count; i++)
            {
                string o = function.Outputs[i];
                if (!assignsByName.ContainsKey(o) && !scope.Shared.Contains(o) && !scope.MentionedByNested.Contains(o) && !scope.IsConstructor)
                {
                    Emit(CodeChecks.STOUT, outputs[i]);
                }
            }
        }

        foreach ((string name, Decl decl) in scope.Declared)
        {
            if (decl.All.Count == 0)
            {
                continue;
            }

            bool read = readsByName.ContainsKey(name) || scope.MentionedByNested.Contains(name);
            bool assigned = assignsByName.ContainsKey(name);
            CodeFix? remove = RemoveDeclaration(decl.First, name);
            if (!read && !assigned)
            {
                Emit(CodeChecks.NUSED, decl.First, remove);
            }
            else if (decl.Persistent && !read)
            {
                Emit(CodeChecks.PUSE, decl.First, remove);
            }
            else if (decl.Persistent && !assigned)
            {
                Emit(CodeChecks.PSET, decl.First);
            }
        }

        // Assignments never read.
        foreach (Event a in events)
        {
            // ans assigned and never read is NASGU beside its NOANS (measured: the parity fixture w10_checkcode).
            if (!a.IsAssign || a.Kind is AssignKind.For or AssignKind.Catch || scope.Declared.ContainsKey(a.Name) || scope.Shared.Contains(a.Name) || scope.IsScript)
            {
                continue;
            }

            bool output = scope.Outputs.Contains(a.Name);
            bool used = ReadLater(scope, a, readsByName, assignsByName) || (output && !OverwrittenUnconditionally(scope, a, assignsByName));
            if (used)
            {
                continue;
            }

            // A method's object, changed in place: a handle's field set, which is the method's work.
            if (a.Kind is AssignKind.Member or AssignKind.Index or AssignKind.Brace && scope.IsMethod && !scope.IsStatic && scope.Parameters.Count > 0 && scope.Parameters[0] == a.Name)
            {
                continue;
            }

            if (a.Kind == AssignKind.Member)
            {
                Emit(CodeChecks.STRNU, a.Span);
            }
            else if (a.Kind == AssignKind.Multi && a.MultiCount >= 2)
            {
                Emit(CodeChecks.ASGLU, a.Span, CodeFix.Of(a.Span.Line, a.Span.Start, a.Span.End, "~"));
            }
            else
            {
                Emit(CodeChecks.NASGU, a.Span);
            }
        }

        foreach (Span span in scope.AnsMentions)
        {
            Emit(CodeChecks.NOANS, span);
        }

        // Reads before any assignment.
        if (!scope.IsScript)
        {
            foreach (Event r in events)
            {
                if (r.IsAssign || r.Implicit || scope.Parameters.Contains(r.Name) || scope.Outputs.Contains(r.Name) || scope.Declared.ContainsKey(r.Name) || scope.Shared.Contains(r.Name) || r.Name == "ans")
                {
                    continue;
                }

                if (!assignsByName.TryGetValue(r.Name, out List<Event>? assigns) || assigns.Any(a => a.Index < r.Index))
                {
                    continue;
                }

                Loop? loop = scope.Loops.FirstOrDefault(l => l.Start <= r.Index && r.Index < l.End);
                if (loop is not null && assigns.Any(a => a.Index >= loop.Start && a.Index < loop.End))
                {
                    continue;
                }

                Emit(CodeChecks.NODEF, r.Span);
            }
        }
    }

    private CodeFix? RemoveDeclaration(Span first, string name)
    {
        string line = _spans.LineText(first.Line);
        string rest = line.Length >= first.End ? line[first.End..] : string.Empty;
        string before = line[..Math.Max(0, first.Start - 1)];
        bool alone = Regex.IsMatch(before, "^\\s*(global|persistent)\\s+$") && Regex.IsMatch(rest, "^\\s*(;|%.*)?\\s*$");
        if (alone)
        {
            return CodeFix.DeleteLine(first.Line);
        }

        // Take the name and the space after it, or before it when it is last.
        int end = first.End;
        while (end < line.Length && line[end] == ' ')
        {
            end++;
        }

        if (end == first.End)
        {
            int start = first.Start;
            while (start > 1 && line[start - 2] == ' ')
            {
                start--;
            }

            return CodeFix.Of(first.Line, start, first.End, string.Empty);
        }

        return CodeFix.Of(first.Line, first.Start, end, string.Empty);
    }

    /// <summary>Whether a later read sees the assignment: a read after it not cut off by an unconditional reassignment, or a read earlier in the same loop.</summary>
    private static bool ReadLater(Scope scope, Event a, Dictionary<string, List<Event>> reads, Dictionary<string, List<Event>> assigns)
    {
        if (!reads.TryGetValue(a.Name, out List<Event>? readList))
        {
            return false;
        }

        List<Event> assignList = assigns[a.Name];
        foreach (Event r in readList)
        {
            if (r.Index > a.Index)
            {
                bool cut = assignList.Any(b => b.Index > a.Index && b.Index < r.Index && scope.IsAncestorOrSame(b.Block, a.Block));
                if (!cut)
                {
                    return true;
                }
            }
        }

        foreach (Loop loop in scope.Loops)
        {
            if (a.Index < loop.Start || a.Index >= loop.End)
            {
                continue;
            }

            foreach (Event r in readList)
            {
                if (r.Index >= loop.Start && r.Index < a.Index)
                {
                    bool cut = assignList.Any(b => scope.IsAncestorOrSame(b.Block, a.Block) && loop.Block >= 0 && scope.IsAncestorOrSame(loop.Block, b.Block)
                        && ((b.Index > a.Index && b.Index < loop.End) || (b.Index >= loop.Start && b.Index < r.Index)));
                    if (!cut)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool OverwrittenUnconditionally(Scope scope, Event a, Dictionary<string, List<Event>> assigns) =>
        assigns[a.Name].Any(b => b.Index > a.Index && b.Kind is not (AssignKind.Index or AssignKind.Brace or AssignKind.Member) && scope.IsAncestorOrSame(b.Block, a.Block));

    // --- emitting ---------------------------------------------------------------------------------------

    private void Emit(CodeChecks.Check check, Span span, CodeFix? fix = null, string? text = null) =>
        _sink.Add(new CodeMessage(check.Id, check.Severity, span.Line, span.Start, span.End, text ?? check.Text, fix));

    private void RecordLoadNames(Scope scope, CallExpr load)
    {
        foreach (Expr argument in load.Arguments.Skip(1))
        {
            if (argument is StringLiteral { Value: var name } literal && Regex.IsMatch(name, "^[A-Za-z]\\w*$"))
            {
                Span span = _spans.SpanOf(literal);
                scope.Events.Add(new Event(scope.Events.Count, name, true, AssignKind.Load, span, scope.Block));
                scope.State[name] = VarState.Other;
            }
        }
    }
}
