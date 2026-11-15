namespace JGraph.Scripting.Jgs;

/// <summary>
/// MATLAB's <c>true</c> and <c>false</c> as names (open item 205, ADR 0363). The parser marks what
/// may answer each bare mention in place of the built-in (see <c>Parser.LiteralNames.cs</c>); while
/// nothing does — no workspace over the layer has bound either word, no file on the path is named
/// after one, the file defines no function of the name — a mention is the literal, for the price of
/// a field read and the file index's per-statement check every built-in call already pays.
/// </summary>
internal sealed partial class Interpreter
{
    /// <summary>A MATLAB literal's value: what answers its word here, when something does, else the literal.</summary>
    private JgsValue ShadowableLiteral(BoolLiteral literal, JgsEnvironment env)
    {
        LiteralShadow shadow = literal.Shadow;
        bool variables = (shadow & LiteralShadow.Variable) != 0 && env.LiteralNamesMayBeBound;
        bool files = (shadow & LiteralShadow.File) != 0 && _resolver.IsShadowed(literal.Word);
        if (!variables && (shadow & LiteralShadow.Function) == 0 && !files)
        {
            return JgsValue.Bool(literal.Value);
        }

        Resolution held = LiteralShadowing(literal.Word, env, variables || (shadow & LiteralShadow.Function) != 0, files);
        if (!held.Found)
        {
            return JgsValue.Bool(literal.Value);
        }

        if (!AutoCallsBare(held))
        {
            return held.Value;
        }

        JgsOutputDemand.Refuse(held.Value.AsCallable, 1, literal.Line, literal.Column);
        return held.Value.AsCallable.Call(System.Array.Empty<JgsValue>(), literal.Line, literal.Column);
    }

    /// <summary>
    /// What answers <paramref name="word"/> in <paramref name="env"/> ahead of the built-in, or
    /// <see cref="Resolution.None"/>: a variable (when <paramref name="workspace"/> asks for the
    /// workspace), a nested or local function of the running file, or a file the folders hold.
    /// </summary>
    /// <remarks>
    /// In a function's frame a variable counts only when the function's own text binds the word,
    /// as R2025b decides it: an <c>eval</c>, <c>load</c>, <c>assignin</c> or called script that
    /// makes one there leaves the word the built-in. In an anonymous function's body only what it
    /// captured counts: a variable its defining workspace binds after the handle was made is not the
    /// body's (R2025b: <c>h = @() true; true = 6;</c> then <c>h()</c> is logical 1).
    /// </remarks>
    private Resolution LiteralShadowing(string word, JgsEnvironment env, bool workspace, bool files = true)
    {
        if (workspace)
        {
            Resolution held = _resolver.Lookup(word, env);
            switch (held.Layer)
            {
                case ResolutionLayer.Bound when VariableAnswersLiteral(word, env):
                case ResolutionLayer.Nested or ResolutionLayer.Local:
                    return held;
            }
        }

        // A file named after the word takes it as it takes any built-in's name (ADR 0149): phase two
        // of a call with no arguments, from the built-in the walk would end on.
        if (files && _resolver.IsShadowed(word) && _resolver.BuiltinOf(word) is { } builtin)
        {
            Resolution file = _resolver.Invoke(word, new Resolution(ResolutionLayer.Builtin, builtin, null), System.Array.Empty<JgsValue>());
            if (file.Layer is not (ResolutionLayer.Builtin or ResolutionLayer.BuiltinMethod or ResolutionLayer.None))
            {
                return file;
            }
        }

        return Resolution.None;
    }

    /// <summary>
    /// <c>x = eval('true')</c> and <c>evalin(ws, 'true')</c> asked for a value: when the whole text is
    /// the bare word (space and trailing semicolons aside), a <c>true.m</c> on the path does not answer
    /// it, where a variable or a function of the file still does (R2025b, open item 329, measured:
    /// <c>'true'</c>, <c>' true '</c> and <c>'true;'</c> are the built-in, <c>'true,'</c>,
    /// <c>'(true)'</c>, <c>'~true'</c> and <c>'true()'</c> the file, and a <c>pi.m</c> answers
    /// <c>eval('pi')</c>).
    /// </summary>
    private void BareWordSkipsFile(string code, Expr expression)
    {
        if (Dialect.IsMatlab && expression is BoolLiteral literal
            && code.Trim().TrimEnd(TextEndings).Equals(literal.Word, StringComparison.Ordinal))
        {
            literal.Shadow &= ~LiteralShadow.File;
        }
    }

    /// <summary>
    /// Whether <paramref name="name"/> is a variable of the running workspace, as <c>which</c> reports
    /// it (open item 329): a bound value, and for <c>true</c> and <c>false</c> in a function's frame
    /// only one its text binds, the rule <see cref="LiteralShadowing"/> reads them by.
    /// </summary>
    internal bool IsWorkspaceVariable(string name) =>
        _resolver.Lookup(name, CurrentFrame).Layer == ResolutionLayer.Bound
        && (Parser.LiteralBits(name) == 0 || VariableAnswersLiteral(name, CurrentFrame));

    /// <summary>What may follow the bare word in <see cref="BareWordSkipsFile"/>'s text.</summary>
    private static readonly char[] TextEndings = [';', ' ', '\t', '\r', '\n'];

    /// <summary>
    /// <c>eval('true = 7;')</c> (or <c>evalc</c>'s) in the frame of a function whose text mentions the
    /// word and does not bind it: the binding is made and the function still reads the built-in, and
    /// R2025b says so with <c>MATLAB:lang:ExternalShadowedInEval</c>, each time (open item 329,
    /// measured). Not in a script, nor for text another function's <c>evalin('caller', ...)</c> runs
    /// here, nor through <c>assignin</c>. R2025b gives the warning for every name a function reads
    /// as a function (<c>pi</c>, <c>sin</c>); JGraph decides only these two words from a function's
    /// text, so it gives it for them alone (ADR 0368).
    /// </summary>
    private void WarnEvalBindsLiteralWord(IReadOnlyList<Stmt> program, JgsEnvironment env)
    {
        if (!Dialect.IsMatlab || _inScript || _currentFunction is not { } running || Host is not { } host
            || !ReferenceEquals(env.EnclosingFunction, running))
        {
            return;
        }

        int bound = 0;
        foreach (Stmt statement in program)
        {
            switch (statement)
            {
                case ExprStmt { Expression: AssignExpr assign }:
                    bound |= Parser.LiteralBits(TargetRootName(assign.Target));
                    break;
                case MultiAssignStmt multi:
                    foreach (Expr? target in multi.Targets)
                    {
                        bound |= target is null ? 0 : Parser.LiteralBits(TargetRootName(target));
                    }

                    break;
                case ForStmt loop:
                    bound |= Parser.LiteralBits(loop.Variable);
                    break;
            }
        }

        bound &= ~running.LiteralBindings;
        foreach (string word in (string[])["true", "false"])
        {
            int bit = Parser.LiteralBits(word);
            if ((bound & bit) != 0 && MentionsLiteralWord(running, word))
            {
                JgsBuiltins.Warn(host, "MATLAB:lang:ExternalShadowedInEval",
                    $"Identifier '{word}' in the evaluated statement does not refer to the external function. "
                    + "In a future release, using an identifier different to how it is used in the file will not be supported.");
            }
        }
    }

    /// <summary>Whether <paramref name="function"/>'s own text names <paramref name="word"/>: bare, or as a call or an index.</summary>
    private static bool MentionsLiteralWord(FnStmt function, string word) =>
        (function.LiteralMentions & Parser.LiteralBits(word)) != 0 || function.Mentions(word);

    /// <summary>The variable an assignment target is rooted at (<c>true</c> in <c>true(2).x = 1</c>), or null.</summary>
    private static string? TargetRootName(Expr target)
    {
        while (true)
        {
            switch (target)
            {
                case VariableExpr variable:
                    return variable.Name;
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
                    return null;
            }
        }
    }

    /// <summary>Whether the variable <paramref name="word"/> bound here is the word's answer; see <see cref="LiteralShadowing"/>.</summary>
    private static bool VariableAnswersLiteral(string word, JgsEnvironment env)
    {
        // A function whose text mentions the word and never binds it reads the built-in; one whose text
        // never mentions it decides nothing, and eval'd text there reads what an eval bound (open item
        // 329, measured: eval('false = 8;') then eval('false') is 8 in a function that names no false).
        if (env.EnclosingFunction is { } function && (function.LiteralBindings & Parser.LiteralBits(word)) == 0
            && MentionsLiteralWord(function, word))
        {
            return false;
        }

        if (env.IsGlobal(word))
        {
            return true;
        }

        bool inStatic = false;
        for (JgsEnvironment? scope = env; scope is not null && !scope.IsBuiltinLayer; scope = scope.Parent)
        {
            if (scope.IsStaticWorkspace)
            {
                inStatic = true;
            }
            else if (inStatic)
            {
                return false; // the defining workspace behind an anonymous function's snapshot
            }

            if (scope.Locals.ContainsKey(word) || scope.DeclaresPersistentLocally(word))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a compiled loop's MATLAB literals still mean the literal in <paramref name="env"/>:
    /// the entry check and the check after a walked statement both ask (ADR 0363).
    /// </summary>
    private bool LiteralsStillLiteral(RegisterProgram program, JgsEnvironment env)
    {
        if (!program.ReadsShadowableLiteral)
        {
            return true;
        }

        bool workspace = env.LiteralNamesMayBeBound;
        if (!workspace && !_resolver.IsShadowed("true") && !_resolver.IsShadowed("false"))
        {
            return true;
        }

        return !LiteralShadowing("true", env, workspace).Found && !LiteralShadowing("false", env, workspace).Found;
    }
}
