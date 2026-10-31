namespace JGraph.Scripting.Designer;

/// <summary>
/// What the Code View holds closed and lists (App Designer window plan W8d): the blocks <c>DefaultFoldsPlugin</c> folds as an app opens - the component properties, <c>createComponents</c>, the constructor, <c>delete</c> and a
/// read-only callback - and the class's functions (<c>CodeModel.getClassFunctions</c>) for the Go To list.
/// </summary>
public static class DesignCodeFolds
{
    /// <summary>The one-based first and last lines of each block closed by default in the class <paramref name="text"/>, in order.</summary>
    public static IReadOnlyList<(int First, int Last)> ClosedByDefault(string text, DesignCode? code)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] lines = text.Split('\n');
        var folds = new List<(int First, int Last)>();
        string? className = code?.ClassName;
        HashSet<string> generated = code is null ? [] : [.. code.Callbacks.Where(static c => c.IsGenerated).Select(static c => c.Name)];
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            string trimmed = line.TrimStart();
            bool folded = false;
            if (trimmed.StartsWith("properties", StringComparison.Ordinal))
            {
                folded = i > 0 && lines[i - 1].Trim() == DesignCodeReader.ComponentPropertiesComment;
            }
            else if (trimmed.StartsWith("function ", StringComparison.Ordinal))
            {
                string name = FunctionName(trimmed);
                folded = name == "createComponents" || name == "delete" || name == className || generated.Contains(name);
            }

            if (folded && BlockEnd(lines, i, line.Length - trimmed.Length) is int end and > 0)
            {
                folds.Add((i + 1, end + 1));
            }

            // A plain-text app's appendix: each section's root (XMLRootBuilder, isFoldedByDefault).
            if (i > 0 && lines[i - 1].TrimStart().StartsWith("<?xml", StringComparison.Ordinal) && RootName(trimmed) is { } root)
            {
                for (int k = i + 1; k < lines.Length; k++)
                {
                    if (lines[k].TrimEnd('\r').Trim() == "</" + root + ">")
                    {
                        folds.Add((i + 1, k + 1));
                        break;
                    }
                }
            }
        }

        return folds;
    }

    /// <summary>The functions the Go To list holds, code-unit sorted (<c>EditorTab._generateFunctionList</c>): the callbacks, the user's functions, the constructor, <c>createComponents</c> and <c>delete</c>.</summary>
    public static IReadOnlyList<string> GoToFunctions(DesignCode? code, IEnumerable<string> userFunctions)
    {
        ArgumentNullException.ThrowIfNull(userFunctions);
        if (code is null)
        {
            return [];
        }

        return [.. code.Callbacks.Select(static c => c.Name).Concat(userFunctions).Concat([code.ClassName, "createComponents", "delete"]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    private static string? RootName(string line)
    {
        if (!line.StartsWith('<') || line.StartsWith("<!", StringComparison.Ordinal) || line.StartsWith("<?", StringComparison.Ordinal) || line.EndsWith("/>", StringComparison.Ordinal))
        {
            return null;
        }

        int stop = line.IndexOfAny([' ', '>']);
        return stop <= 1 ? null : line[1..stop];
    }

    private static string FunctionName(string signature)
    {
        string rest = signature["function ".Length..];
        int equals = rest.IndexOf('=', StringComparison.Ordinal);
        if (equals >= 0)
        {
            rest = rest[(equals + 1)..];
        }

        rest = rest.TrimStart();
        int stop = rest.IndexOfAny(['(', ' ', '%', ';']);
        return stop < 0 ? rest.TrimEnd() : rest[..stop];
    }

    /// <summary>The line of the <c>end</c> at the block's own indent.</summary>
    private static int? BlockEnd(string[] lines, int start, int indent)
    {
        for (int i = start + 1; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            string trimmed = line.TrimStart();
            if (line.Length - trimmed.Length == indent && (trimmed == "end" || trimmed.StartsWith("end ", StringComparison.Ordinal) || trimmed.StartsWith("end%", StringComparison.Ordinal)))
            {
                return i;
            }
        }

        return null;
    }
}
