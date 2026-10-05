using System.Text.RegularExpressions;
using JGraph.Scripting.Completion;

namespace JGraph.Scripting.Jgs.Completion;

/// <summary>The completions to offer at a cursor position: the identifier span being completed starts at
/// <see cref="ReplaceStart"/> (equal to the cursor offset when there is no prefix).</summary>
public sealed record JgsCompletionResult(int ReplaceStart, IReadOnlyList<CompletionItem> Items);

/// <summary>Signature help for the innermost call at the cursor: the full signature, its parameter labels
/// (as displayed, e.g. <c>spec?</c>), and which parameter the cursor's argument maps to.</summary>
public sealed record JgsSignatureHelp(
    string Name,
    string Signature,
    IReadOnlyList<string> ParameterLabels,
    int ActiveParameter,
    string? Summary);

/// <summary>
/// Context-aware completion and signature help for JGS, entirely UI-free. Everything works on the raw
/// buffer via the tolerant lexer — never the parser, which throws on the first error and a buffer
/// mid-keystroke is routinely broken. Suggestions combine the language keywords, the builtin catalog,
/// symbols harvested from the buffer (<c>let</c> bindings and loop variables before the cursor, <c>fn</c>
/// definitions anywhere — they hoist), and optional workspace symbols the host harvested from other files.
/// </summary>
public static class JgsCompletionEngine
{
    /// <summary>Computes the completions to offer at <paramref name="offset"/> in <paramref name="code"/>.
    /// Inside a string or comment the result is empty. Items are prefix-filtered (ordinal, ignoring case)
    /// against the identifier being typed and sorted by name.</summary>
    /// <param name="code">The buffer text (possibly syntactically broken).</param>
    /// <param name="offset">The cursor offset, clamped into the buffer.</param>
    /// <param name="workspaceSymbols">Extra symbols from other workspace scripts (see <see cref="HarvestFunctions"/>).</param>
    /// <param name="matlab">True for a MATLAB buffer, where '%' starts a comment.</param>
    /// <param name="live">In a MATLAB buffer, the names a session knows (ADR 0183): what follows a dotted
    /// .NET name, and the libraries and functions inside <c>calllib('…'</c>. Null asks
    /// <see cref="InteropCompletion.Framework"/>, which knows the .NET framework and no library.</param>
    public static JgsCompletionResult GetCompletions(
        string code, int offset, IReadOnlyList<CompletionItem>? workspaceSymbols = null, bool matlab = false,
        IScriptCompletionSource? live = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        offset = System.Math.Clamp(offset, 0, code.Length);

        int replaceStart = offset;
        while (replaceStart > 0 && IsIdentifierChar(code[replaceStart - 1]))
        {
            replaceStart--;
        }

        if (matlab)
        {
            IScriptCompletionSource names = live ?? InteropCompletion.Framework;
            if (LibraryArgument(code, offset, names) is { } library)
            {
                return library;
            }

            // serialport("…: the serial ports (ADR 0197).
            if (DeviceCompletion.NameArgument(code, offset, names) is { } device)
            {
                return device;
            }

            if (!IsInStringOrComment(code, replaceStart, matlab) && DottedQualifier(code, replaceStart) is { } qualifier)
            {
                string typed = code[replaceStart..offset];
                // app. in a class file is the class's own members (U7). jgraph and its packages are
                // JGraph's own (ADR 0197); every other dotted name is .NET's.
                IEnumerable<CompletionItem> offered = ClassCompletion.Members(code, qualifier) is { } ofClass
                    ? ofClass
                    : DeviceCompletion.PackageMembers(qualifier) is { } package
                        ? package.Concat(names.Members(qualifier))
                        : names.Members(qualifier);
                var members = offered
                    .Where(i => i.Text.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                    .DistinctBy(static i => i.Text)
                    .OrderBy(static i => i.Text, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return new JgsCompletionResult(replaceStart, members);
            }

            // After any other dot — a number's (1.), a field's (s.x.) — the builtins are no answer.
            if (replaceStart > 0 && code[replaceStart - 1] == '.')
            {
                return new JgsCompletionResult(offset, Array.Empty<CompletionItem>());
            }
        }

        // Inside a number ("12|"), a string, or a comment, completion stays quiet.
        if ((replaceStart < offset && char.IsDigit(code[replaceStart])) || IsInStringOrComment(code, replaceStart, matlab))
        {
            return new JgsCompletionResult(offset, Array.Empty<CompletionItem>());
        }

        string prefix = code[replaceStart..offset];
        var byName = new Dictionary<string, CompletionItem>(StringComparer.Ordinal);

        void Offer(CompletionItem item)
        {
            if (item.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                byName.TryAdd(item.Text, item);
            }
        }

        foreach (string keyword in JgsBuiltinCatalog.Keywords)
        {
            Offer(new CompletionItem(keyword, CompletionItemKind.Keyword));
        }

        foreach (JgsBuiltinInfo builtin in JgsBuiltinCatalog.All)
        {
            // A package root is completed to its name, for the dot that follows, not to a call.
            Offer(PackageRoots.Contains(builtin.Name)
                ? new CompletionItem(builtin.Name, CompletionItemKind.Namespace, Signature: null, builtin.Summary)
                : new CompletionItem(builtin.Name, CompletionItemKind.Builtin, builtin.Signature, builtin.Summary));
        }

        foreach (HarvestedSymbol symbol in Harvest(code))
        {
            // The identifier being typed must not offer itself ("let fo|" completing to "fo").
            if (symbol.Offset == replaceStart)
            {
                continue;
            }

            // Functions hoist; let/loop bindings only exist below their declaration.
            if (symbol.Parameters is null && symbol.Offset > offset)
            {
                continue;
            }

            Offer(ToItem(symbol, "defined in this file"));
        }

        if (workspaceSymbols is not null)
        {
            foreach (CompletionItem item in workspaceSymbols)
            {
                Offer(item);
            }
        }

        var items = byName.Values.ToList();
        items.Sort(static (a, b) => string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase));
        return new JgsCompletionResult(replaceStart, items);
    }

    /// <summary>
    /// Signature help for the innermost open call containing <paramref name="offset"/>: which function, and
    /// which parameter the cursor's argument corresponds to (clamped to the last for variadic builtins).
    /// Null when the cursor is not inside a call or the callee is unknown. The callee is looked up in the
    /// builtin catalog, then among <c>fn</c>s in the buffer, then in <paramref name="workspaceSymbols"/>.
    /// </summary>
    /// <param name="code">The buffer text.</param>
    /// <param name="offset">The cursor offset.</param>
    /// <param name="workspaceSymbols">Extra symbols from other workspace scripts.</param>
    /// <param name="matlab">True for a MATLAB buffer, which is lexed by MATLAB's rules.</param>
    public static JgsSignatureHelp? GetSignatureHelp(
        string code, int offset, IReadOnlyList<CompletionItem>? workspaceSymbols = null, bool matlab = false)
    {
        ArgumentNullException.ThrowIfNull(code);
        offset = System.Math.Clamp(offset, 0, code.Length);

        IReadOnlyList<Token> tokens = Lexer.Tokenize(code, tolerant: true, Dialect(matlab));
        int[] lineStarts = LineStarts(code);

        // Walk the tokens before the cursor keeping a stack of open brackets; call frames (an identifier
        // followed by '(') count their top-level commas so the active argument falls out of the scan.
        var stack = new List<(string? Callee, int ArgIndex)>();
        Token prev1 = default; // the token before the current one
        Token prev2 = default; // the token before that ('fn name(' is a declaration, not a call)

        for (int t = 0; t < tokens.Count; t++)
        {
            Token token = tokens[t];
            if (token.Type == TokenType.Eof || Offset(token, lineStarts) >= offset)
            {
                break;
            }

            switch (token.Type)
            {
                case TokenType.LParen:
                    bool isCall = prev1.Type == TokenType.Identifier && prev2.Type != TokenType.Fn;
                    stack.Add((isCall ? DottedCallee(tokens, t - 1) : null, 0));
                    break;
                case TokenType.LBracket:
                    stack.Add((null, 0)); // array literal / indexing — anonymous
                    break;
                case TokenType.RParen or TokenType.RBracket when stack.Count > 0:
                    stack.RemoveAt(stack.Count - 1);
                    break;
                case TokenType.Comma when stack.Count > 0:
                    stack[^1] = (stack[^1].Callee, stack[^1].ArgIndex + 1);
                    break;
            }

            prev2 = prev1;
            prev1 = token;
        }

        // The innermost frame that is an actual call (grouping parens and array brackets are anonymous).
        for (int i = stack.Count - 1; i >= 0; i--)
        {
            if (stack[i].Callee is not string name)
            {
                continue;
            }

            JgsSignatureHelp? help = Resolve(name, code, workspaceSymbols);
            if (help is null)
            {
                return null;
            }

            int active = help.ParameterLabels.Count == 0
                ? 0
                : System.Math.Min(stack[i].ArgIndex, help.ParameterLabels.Count - 1);
            return help with { ActiveParameter = active };
        }

        return null;
    }

    /// <summary>
    /// Harvests the <c>fn</c> definitions of <paramref name="code"/> as completion items — the shape hosts
    /// feed back as workspace symbols for other files. Tolerant: works on broken buffers.
    /// </summary>
    /// <param name="code">The script text to harvest (possibly syntactically broken).</param>
    /// <param name="origin">A short origin note shown in the completion list (e.g. the file name).</param>
    public static IReadOnlyList<CompletionItem> HarvestFunctions(string code, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        var items = new List<CompletionItem>();
        foreach (HarvestedSymbol symbol in Harvest(code))
        {
            if (symbol.Parameters is not null)
            {
                items.Add(ToItem(symbol, origin is null ? "fn" : $"fn — {origin}"));
            }
        }

        return items;
    }

    // --- Harvesting ------------------------------------------------------------------------------

    /// <summary>A symbol found in the buffer: a fn (with parameters) or a let/loop binding (null parameters).
    /// <see cref="Offset"/> is where its name token starts.</summary>
    private sealed record HarvestedSymbol(string Name, IReadOnlyList<string>? Parameters, int Offset);

    private static List<HarvestedSymbol> Harvest(string code)
    {
        IReadOnlyList<Token> tokens = Lexer.Tokenize(code, tolerant: true);
        int[] lineStarts = LineStarts(code);
        var symbols = new List<HarvestedSymbol>();

        for (int i = 0; i < tokens.Count - 1; i++)
        {
            Token token = tokens[i];
            Token name = tokens[i + 1];
            if (name.Type != TokenType.Identifier)
            {
                continue;
            }

            switch (token.Type)
            {
                case TokenType.Let:
                case TokenType.For:
                    symbols.Add(new HarvestedSymbol(name.Text, null, Offset(name, lineStarts)));
                    break;
                case TokenType.Fn:
                    var parameters = new List<string>();
                    if (i + 2 < tokens.Count && tokens[i + 2].Type == TokenType.LParen)
                    {
                        for (int j = i + 3; j < tokens.Count && tokens[j].Type != TokenType.RParen; j++)
                        {
                            if (tokens[j].Type == TokenType.Identifier)
                            {
                                parameters.Add(tokens[j].Text);
                            }
                            else if (tokens[j].Type is not TokenType.Comma)
                            {
                                break; // Broken parameter list; keep what parsed cleanly.
                            }
                        }
                    }

                    symbols.Add(new HarvestedSymbol(name.Text, parameters, Offset(name, lineStarts)));
                    break;
            }
        }

        return symbols;
    }

    private static CompletionItem ToItem(HarvestedSymbol symbol, string description) => symbol.Parameters is null
        ? new CompletionItem(symbol.Name, CompletionItemKind.Variable, Signature: null, description)
        : new CompletionItem(symbol.Name, CompletionItemKind.Function, $"{symbol.Name}({string.Join(", ", symbol.Parameters)})", description);

    /// <summary>
    /// The name a call is made by: the identifier at <paramref name="index"/> with the dotted names
    /// before it (<c>jgraph.usb.hid</c>), so a package function is found by its whole name.
    /// </summary>
    private static string DottedCallee(IReadOnlyList<Token> tokens, int index)
    {
        string name = tokens[index].Text;
        while (index >= 2 && tokens[index - 1].Type == TokenType.Dot && tokens[index - 2].Type == TokenType.Identifier)
        {
            index -= 2;
            name = tokens[index].Text + "." + name;
        }

        return name;
    }

    /// <summary>The names that are packages, not functions: completed bare, for the dot that follows.</summary>
    private static readonly HashSet<string> PackageRoots = new(StringComparer.Ordinal) { "jgraph", "NET" };

    private static JgsSignatureHelp? Resolve(string name, string code, IReadOnlyList<CompletionItem>? workspaceSymbols)
    {
        if (name.Contains('.', StringComparison.Ordinal))
        {
            // A package function (ADR 0197), or a method called with a dot, whose last name is the callee.
            if (DeviceCompletion.Find(name) is { Signature: { } packaged } item)
            {
                return new JgsSignatureHelp(name, packaged, ParameterLabels(packaged), 0, item.Description);
            }

            name = name[(name.LastIndexOf('.') + 1)..];
        }

        if (JgsBuiltinCatalog.Find(name) is JgsBuiltinInfo builtin)
        {
            return new JgsSignatureHelp(
                builtin.Name,
                builtin.Signature,
                builtin.Parameters.Select(static p => p.Display).ToArray(),
                ActiveParameter: 0,
                builtin.Summary);
        }

        foreach (HarvestedSymbol symbol in Harvest(code))
        {
            if (symbol.Parameters is not null && symbol.Name == name)
            {
                return new JgsSignatureHelp(
                    symbol.Name, $"{symbol.Name}({string.Join(", ", symbol.Parameters)})", symbol.Parameters, 0, null);
            }
        }

        if (workspaceSymbols is not null)
        {
            foreach (CompletionItem item in workspaceSymbols)
            {
                if (item.Text == name && item.Signature is string signature)
                {
                    return new JgsSignatureHelp(name, signature, ParameterLabels(signature), 0, item.Description);
                }
            }
        }

        return null;
    }

    /// <summary>Splits the parameter labels back out of a rendered signature like <c>plot(x, y, spec?)</c>
    /// (a label ending in <c>?</c> is an optional parameter). Editors use this for placeholder insertion.</summary>
    public static IReadOnlyList<string> ParameterLabels(string signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        int open = signature.IndexOf('(');
        int close = signature.LastIndexOf(')');
        if (open < 0 || close <= open + 1)
        {
            return Array.Empty<string>();
        }

        return signature[(open + 1)..close].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    // --- Names a session knows (ADR 0183) ------------------------------------------------------------

    /// <summary>The names that take a library as their first argument, whose string gets the libraries offered.</summary>
    private static readonly Regex LibraryFirst = new(
        @"\b(?:calllib|libfunctions|libfunctionsview|unloadlibrary|libisloaded)\s*\(\s*['""](\w*)$",
        RegexOptions.CultureInvariant);

    /// <summary><c>calllib('lib', '</c>: the second argument names a function of the first.</summary>
    private static readonly Regex LibraryFunction = new(
        @"\bcalllib\s*\(\s*(['""])(\w+)\1\s*,\s*['""](\w*)$",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Inside the first string argument of <c>calllib</c> and its family, the loaded libraries; inside
    /// <c>calllib</c>'s second, the named library's functions. Null anywhere else.
    /// </summary>
    private static JgsCompletionResult? LibraryArgument(string code, int offset, IScriptCompletionSource names)
    {
        int lineStart = offset;
        while (lineStart > 0 && code[lineStart - 1] != '\n')
        {
            lineStart--;
        }

        string line = code[lineStart..offset];
        if (LibraryFunction.Match(line) is { Success: true } call)
        {
            string typed = call.Groups[3].Value;
            return new JgsCompletionResult(offset - typed.Length,
            [
                .. names.LibraryFunctions(call.Groups[2].Value)
                    .Where(i => i.Text.StartsWith(typed, StringComparison.OrdinalIgnoreCase)),
            ]);
        }

        if (LibraryFirst.Match(line) is { Success: true } first)
        {
            string typed = first.Groups[1].Value;
            return new JgsCompletionResult(offset - typed.Length,
            [
                .. names.Libraries()
                    .Where(n => n.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                    .Select(static n => new CompletionItem(n, CompletionItemKind.Library, Description: "loaded library")),
            ]);
        }

        return null;
    }

    /// <summary>
    /// The dotted name before a dot that ends right before <paramref name="replaceStart"/>
    /// (<c>System.IO</c> in <c>System.IO.Fi|</c>), or null when there is no dot there or what precedes
    /// it is not a plain dotted name that starts with a letter.
    /// </summary>
    private static string? DottedQualifier(string code, int replaceStart)
    {
        if (replaceStart < 2 || code[replaceStart - 1] != '.')
        {
            return null;
        }

        int start = replaceStart - 1;
        while (start > 0 && (IsIdentifierChar(code[start - 1])
            || (code[start - 1] == '.' && start >= 2 && IsIdentifierChar(code[start - 2]))))
        {
            start--;
        }

        string qualifier = code[start..(replaceStart - 1)];
        return qualifier.Length > 0 && char.IsLetter(qualifier[0]) ? qualifier : null;
    }

    // --- Text helpers ------------------------------------------------------------------------------

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static int[] LineStarts(string code)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < code.Length; i++)
        {
            if (code[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return starts.ToArray();
    }

    private static int Offset(Token token, int[] lineStarts) =>
        lineStarts[System.Math.Min(token.Line, lineStarts.Length) - 1] + token.Column - 1;

    /// <summary>The dialect a completion request is lexed in.</summary>
    private static JgsDialect Dialect(bool matlab) => matlab ? JgsDialect.Matlab : JgsDialect.Jgs;

    /// <summary>Whether <paramref name="offset"/> sits inside a string literal or a line comment — the two
    /// places completion must stay quiet. Both end at the line break, so scanning the current line suffices.
    /// In MATLAB a '%' opens a comment where JGS would read modulo.</summary>

    private static bool IsInStringOrComment(string code, int offset, bool matlab)
    {
        int lineStart = offset;
        while (lineStart > 0 && code[lineStart - 1] != '\n')
        {
            lineStart--;
        }

        char stringQuote = '\0'; // '\0' = outside any string; else the quote that opened it
        for (int i = lineStart; i < offset; i++)
        {
            char c = code[i];
            if (stringQuote != '\0')
            {
                if (c == '\\')
                {
                    i++; // skip the escaped character
                }
                else if (c == stringQuote)
                {
                    stringQuote = '\0';
                }
            }
            else if (c is '"' or '\'')
            {
                stringQuote = c;
            }
            else if (c == '#' || (matlab && c == '%') || (c == '/' && i + 1 < offset && code[i + 1] == '/'))
            {
                return true;
            }
        }

        return stringQuote != '\0';
    }
}
