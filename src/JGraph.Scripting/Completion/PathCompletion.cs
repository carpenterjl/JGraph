using System.IO;
using JGraph.Scripting.Workspace;

namespace JGraph.Scripting.Completion;

/// <summary>One workspace file or folder for path completion, as a workspace-relative path with
/// <c>/</c> separators (the form scripts write).</summary>
public sealed record WorkspaceFileEntry(string RelativePath, bool IsDirectory);

/// <summary>
/// Where the caret sits inside the path-string argument of a file builtin: which function, the folder
/// part already typed (<see cref="DirectoryPrefix"/>, e.g. <c>"lib/"</c>), the partial name being typed
/// after it, and the buffer offset where that partial name starts (the editor's replace span).
/// </summary>
public sealed record PathCompletionContext(
    string FunctionName,
    string DirectoryPrefix,
    string PartialName,
    int ReplaceStart);

/// <summary>
/// Workspace filename completion inside the string argument of the file-reading builtins
/// (<c>readcsv</c>/<c>readxlsx</c>/<c>readtable</c>, <c>audioread</c>, <c>imread</c>,
/// <c>sparameters</c>, <c>loadfigure</c>, and <c>run</c> in JGS). Engine-agnostic by design —
/// the same helpers exist in the C# and Python hosts — so detection is a single-line lexical scan over
/// both quote kinds, never a parse. Only workspace-relative paths complete: a rooted path (or one that
/// escapes via <c>..</c>) is outside the workspace's knowledge and offers nothing.
/// </summary>
public static class PathCompletion
{
    /// <summary>The extensions each file builtin accepts, matching the readers' extension routing.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> Functions =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["readcsv"] = new[] { ".csv", ".tsv", ".txt" },
            ["readxlsx"] = new[] { ".xlsx" },
            ["readtable"] = new[] { ".csv", ".tsv", ".txt", ".xlsx" },
            ["audioread"] = new[] { ".wav" },
            ["imread"] = new[] { ".png", ".jpg", ".jpeg", ".bmp" },
            ["sparameters"] = new[] { ".s1p", ".s2p", ".s3p", ".s4p" },
            ["loadfigure"] = new[] { ".graph" },
            ["run"] = new[] { ".jgs" }, // JGS-only; see Detect
        };

    /// <summary>
    /// Detects whether <paramref name="offset"/> sits inside the path-string argument of a file builtin.
    /// Null when it does not (not in a string, not the first argument of a known function, or the typed
    /// path is rooted / escapes the workspace).
    /// </summary>
    /// <param name="code">The buffer text (possibly syntactically broken).</param>
    /// <param name="offset">The cursor offset, clamped into the buffer.</param>
    /// <param name="language">The document language ("JGS", "C#", "Python") — <c>run</c> is JGS-only.</param>
    public static PathCompletionContext? Detect(string code, int offset, string language)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(language);
        offset = System.Math.Clamp(offset, 0, code.Length);

        int lineStart = offset;
        while (lineStart > 0 && code[lineStart - 1] != '\n')
        {
            lineStart--;
        }

        // Walk the line to the caret tracking string state ('"' or '\'') and line comments.
        char quote = '\0';
        int quoteIndex = -1;
        for (int i = lineStart; i < offset; i++)
        {
            char c = code[i];
            if (quote != '\0')
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                quoteIndex = i;
            }
            else if (c == '#' || (c == '/' && i + 1 < offset && code[i + 1] == '/'))
            {
                return null; // in a comment
            }
        }

        if (quote == '\0')
        {
            return null; // not inside a string
        }

        // The string must be argument 0 of a known file function: quote ← whitespace ← '(' ← identifier.
        int j = quoteIndex - 1;
        while (j >= 0 && (code[j] == ' ' || code[j] == '\t'))
        {
            j--;
        }

        if (j < 0 || code[j] != '(')
        {
            return null;
        }

        j--;
        while (j >= 0 && (code[j] == ' ' || code[j] == '\t'))
        {
            j--;
        }

        int nameEnd = j + 1;
        while (j >= 0 && (char.IsLetterOrDigit(code[j]) || code[j] == '_'))
        {
            j--;
        }

        string function = code[(j + 1)..nameEnd];
        if (!Functions.ContainsKey(function) || (function == "run" && language != "JGS"))
        {
            return null;
        }

        // The path typed so far; rooted or workspace-escaping paths offer nothing.
        string content = code[(quoteIndex + 1)..offset];
        if (content.Length > 0 && (Path.IsPathRooted(content) || content[0] == '~'))
        {
            return null;
        }

        string normalized = content.Replace('\\', '/');
        if (normalized.Split('/').Any(static segment => segment == ".."))
        {
            return null;
        }

        int lastSeparator = normalized.LastIndexOf('/');
        string directoryPrefix = lastSeparator < 0 ? string.Empty : normalized[..(lastSeparator + 1)];
        string partialName = lastSeparator < 0 ? normalized : normalized[(lastSeparator + 1)..];
        return new PathCompletionContext(function, directoryPrefix, partialName, offset - partialName.Length);
    }

    /// <summary>
    /// The entries to offer for <paramref name="context"/>: direct children of the typed folder, folders
    /// always (as <c>name/</c>, so paths compose), files filtered to the function's accepted extensions,
    /// prefix-matched against the partial name (ordinal, ignoring case). Folders first, then by name.
    /// </summary>
    public static IReadOnlyList<CompletionItem> GetCompletions(
        PathCompletionContext context, IReadOnlyList<WorkspaceFileEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entries);

        string[] extensions = Functions.TryGetValue(context.FunctionName, out string[]? accepted)
            ? accepted
            : Array.Empty<string>();
        var items = new List<CompletionItem>();

        foreach (WorkspaceFileEntry entry in entries)
        {
            // Direct children only: RelativePath == DirectoryPrefix + single segment.
            if (!entry.RelativePath.StartsWith(context.DirectoryPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string name = entry.RelativePath[context.DirectoryPrefix.Length..];
            if (name.Length == 0 || name.Contains('/'))
            {
                continue;
            }

            if (!name.StartsWith(context.PartialName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry.IsDirectory)
            {
                items.Add(new CompletionItem(name + "/", CompletionItemKind.Folder, Signature: null, "folder"));
            }
            else if (extensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            {
                items.Add(new CompletionItem(name, CompletionItemKind.File, Signature: null, "workspace file"));
            }
        }

        items.Sort(static (a, b) =>
        {
            int byKind = (a.Kind == CompletionItemKind.Folder ? 0 : 1) - (b.Kind == CompletionItemKind.Folder ? 0 : 1);
            return byKind != 0 ? byKind : string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase);
        });
        return items;
    }

    /// <summary>Flattens a workspace tree (from <see cref="ScriptWorkspace.EnumerateAll"/>) into the
    /// relative-path list completion matches against, normalizing separators to <c>/</c>.</summary>
    public static IReadOnlyList<WorkspaceFileEntry> Flatten(IReadOnlyList<WorkspaceEntry> tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var entries = new List<WorkspaceFileEntry>();
        void Walk(IReadOnlyList<WorkspaceEntry> nodes)
        {
            foreach (WorkspaceEntry node in nodes)
            {
                entries.Add(new WorkspaceFileEntry(node.RelativePath.Replace('\\', '/'), node.IsDirectory));
                if (node.IsDirectory)
                {
                    Walk(node.Children);
                }
            }
        }

        Walk(tree);
        return entries;
    }
}
