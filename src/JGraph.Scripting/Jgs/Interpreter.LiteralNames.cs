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
        if (!variables && (shadow & LiteralShadow.Function) == 0 && !_resolver.IsShadowed(literal.Word))
        {
            return JgsValue.Bool(literal.Value);
        }

        Resolution held = LiteralShadowing(literal.Word, env, variables || (shadow & LiteralShadow.Function) != 0);
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
    private Resolution LiteralShadowing(string word, JgsEnvironment env, bool workspace)
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
        if (_resolver.IsShadowed(word) && _resolver.BuiltinOf(word) is { } builtin)
        {
            Resolution file = _resolver.Invoke(word, new Resolution(ResolutionLayer.Builtin, builtin, null), System.Array.Empty<JgsValue>());
            if (file.Layer is not (ResolutionLayer.Builtin or ResolutionLayer.BuiltinMethod or ResolutionLayer.None))
            {
                return file;
            }
        }

        return Resolution.None;
    }

    /// <summary>Whether the variable <paramref name="word"/> bound here is the word's answer; see <see cref="LiteralShadowing"/>.</summary>
    private static bool VariableAnswersLiteral(string word, JgsEnvironment env)
    {
        if (env.EnclosingFunction is { } function && (function.LiteralBindings & Parser.LiteralBits(word)) == 0)
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
