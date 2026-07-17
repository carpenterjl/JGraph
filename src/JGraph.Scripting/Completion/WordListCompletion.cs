using System.Reflection;
using JGraph.Api;

namespace JGraph.Scripting.Completion;

/// <summary>
/// Curated word-list completion for the hosted languages. C# and Python are external runtimes — real
/// semantic completion belongs to real IDEs — so the editor offers the words that matter when driving
/// JGraph: the language's keywords, the <see cref="JG"/> facade members (via reflection, so new API
/// surface appears automatically), and the script-globals helpers (<c>readcsv</c>, <c>print</c>, …).
/// </summary>
public static class WordListCompletion
{
    private static readonly string[] CSharpKeywords =
    {
        "abstract", "as", "base", "bool", "break", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit",
        "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in",
        "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object",
        "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref",
        "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct",
        "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
        "ushort", "using", "var", "virtual", "void", "volatile", "while",
    };

    private static readonly string[] PythonKeywords =
    {
        "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif",
        "else", "except", "False", "finally", "for", "from", "global", "if", "import", "in", "is",
        "lambda", "None", "nonlocal", "not", "or", "pass", "raise", "return", "True", "try", "while",
        "with", "yield",
    };

    /// <summary>The C# completion words: keywords, <c>JG</c> and its members, and the globals helpers.</summary>
    public static IReadOnlyList<CompletionItem> CSharp { get; } = Build(CSharpKeywords);

    /// <summary>The Python completion words: keywords, <c>JG</c> and its members, and the globals helpers.</summary>
    public static IReadOnlyList<CompletionItem> Python { get; } = Build(PythonKeywords);

    /// <summary>The word list for <paramref name="language"/> ("C#" or "Python"); null for other languages.</summary>
    public static IReadOnlyList<CompletionItem>? ForLanguage(string language) => language switch
    {
        "C#" => CSharp,
        "Python" => Python,
        _ => null,
    };

    private static IReadOnlyList<CompletionItem> Build(string[] keywords)
    {
        var byName = new Dictionary<string, CompletionItem>(StringComparer.Ordinal);

        foreach (string keyword in keywords)
        {
            byName.TryAdd(keyword, new CompletionItem(keyword, CompletionItemKind.Keyword));
        }

        // The facade itself, then every public static member of it — the API a script drives.
        byName.TryAdd("JG", new CompletionItem("JG", CompletionItemKind.Builtin, null, "The JGraph functional facade."));
        foreach (MemberInfo member in typeof(JG).GetMembers(BindingFlags.Public | BindingFlags.Static))
        {
            string name = member.Name;
            if (member is MethodInfo method)
            {
                if (method.IsSpecialName)
                {
                    continue; // property accessors
                }

                byName.TryAdd(name, new CompletionItem(name, CompletionItemKind.Builtin, null, $"JG.{name}"));
            }
            else if (member is PropertyInfo or FieldInfo)
            {
                byName.TryAdd(name, new CompletionItem(name, CompletionItemKind.Builtin, null, $"JG.{name}"));
            }
        }

        // The script-globals helpers available as bare names in C# scripts (and via the globals in Python).
        foreach (string helper in new[] { "print", "readcsv", "readxlsx", "readtable", "show" })
        {
            byName.TryAdd(helper, new CompletionItem(helper, CompletionItemKind.Builtin, null, "Script helper."));
        }

        var items = byName.Values.ToList();
        items.Sort(static (a, b) => string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase));
        return items;
    }
}
