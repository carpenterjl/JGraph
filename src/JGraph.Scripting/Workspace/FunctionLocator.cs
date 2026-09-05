using System.IO;
using System.Text.RegularExpressions;

namespace JGraph.Scripting.Workspace;

/// <summary>
/// Finds where a script defines a function — the answer behind the editor's <em>Open name</em>
/// (MATLAB's Ctrl+D) and the prompt's <c>edit name</c>. Each language is read by its own definition
/// shape rather than parsed: a definition is a line, and reading lines is what keeps this cheap
/// enough to run over every script in a workspace on a keystroke, and what lets it read a file with
/// a syntax error further down.
/// </summary>
public static class FunctionLocator
{
    /// <summary>
    /// The 1-based line on which <paramref name="code"/>, in <paramref name="language"/>, defines
    /// <paramref name="name"/> — a <c>fn</c>, a <c>function</c>, a <c>def</c> or <c>class</c>, a C#
    /// method or type — or null when it does not.
    /// </summary>
    public static int? FindDefinition(string code, string language, string name)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(name);
        if (!IsIdentifier(name) || DefinitionPattern(language, name) is not Regex pattern)
        {
            return null;
        }

        int line = 1;
        foreach (string text in code.Split('\n'))
        {
            if (pattern.IsMatch(text.TrimEnd('\r')))
            {
                return line;
            }

            line++;
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="path"/> is the script file MATLAB's rule names for
    /// <paramref name="name"/>: a script or function file called <c>name</c> with a script extension.
    /// The file itself is the definition, whatever it contains.
    /// </summary>
    public static bool FileNameMatches(string path, string name)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(name);
        return ScriptDocumentModel.LanguageForFile(path) != "Text"
            && string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether <paramref name="text"/> is a plain identifier — what a name lookup can be asked about.</summary>
    public static bool IsIdentifier(string text)
    {
        if (string.IsNullOrEmpty(text) || !(char.IsLetter(text[0]) || text[0] == '_'))
        {
            return false;
        }

        foreach (char ch in text)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static Regex? DefinitionPattern(string language, string name)
    {
        string n = Regex.Escape(name);
        string pattern = language switch
        {
            "JGS" => $@"^\s*fn\s+{n}\s*\(",

            // function name, function out = name(…), function [a, b] = name …, and a bare
            // 'function name' with no argument list.
            "MATLAB" => $@"^\s*function\s+(?:\[[^\]]*\]\s*=\s*|[A-Za-z_]\w*\s*=\s*)?{n}\s*(?:\(|%|$)",

            "Python" => $@"^\s*(?:async\s+)?def\s+{n}\s*\(|^\s*class\s+{n}\s*[(:]",

            // A method: at least one word (a modifier or the return type) before the name and its
            // parameter list, which is what tells a declaration from the call 'Foo(1);' — with the
            // statement keywords that would otherwise pass as a return type kept out. And a type.
            "C#" => $@"^\s*(?!(?:return|new|throw|await|else|case|yield|using)\b)(?:[\w<>\[\],.?]+\s+)+{n}\s*(?:<[^>]*>)?\s*\("
                    + $@"|^\s*(?:\w+\s+)*(?:class|struct|record|enum|interface|delegate)\s+{n}\b",

            _ => "",
        };

        return pattern.Length == 0 ? null : new Regex(pattern, RegexOptions.CultureInvariant);
    }
}
