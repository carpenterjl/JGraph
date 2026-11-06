using System.IO;
using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The parts of <c>help</c> that read the folders (the packages and class folders plan, stage P1,
/// ADR 0307): a package's listing, and a code file's help text — its first comment block, or a
/// sentence with the function's header when it has none — as R2025b prints them (measured in the
/// fixture <c>p1_packages</c>).
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// The help text for a name no catalog entry answers: a package member by its dotted name
    /// (<c>pkg.f</c>), a package or sub-package (<c>pkg</c>, <c>pkg.sub</c>), or a code file on the
    /// search path (<c>p1plain</c>). Null when the folders hold nothing of the name. A member file
    /// is asked before a sub-package of the same name, as the dotted name itself resolves.
    /// </summary>
    internal static string? UserHelp(JgsFunctionPath search, string name)
    {
        if (JgsCodeTree.DottedParts(name) is { } parts)
        {
            if (search.FindDotted(name, out _) is { } memberFile)
            {
                return FileHelp(memberFile, name);
            }

            return search.Tree.IsPackage(parts) ? PackageHelp(search, parts) : null;
        }

        if (JgsCodeTree.DottedParts("a." + name) is null)
        {
            return null; // not an identifier: a path or an expression, which help has no answer for
        }

        if (search.Find(name) is { } file)
        {
            return FileHelp(file, name);
        }

        return search.Tree.IsPackage([name]) ? PackageHelp(search, [name]) : null;
    }

    /// <summary>
    /// <c>help pkg</c>: "Contents of pkg namespace:", a blank line, then one line per member — the
    /// code files first, each "name - pkg.name is a function." (or a script, or a class), then the
    /// class folders as "+pkg\@D is a folder.", then the sub-packages as "pkg.sub is a namespace." —
    /// the names right-aligned to the longest, each group sorted ordinally, every folder holding
    /// the package merged (R2025b, measured).
    /// </summary>
    private static string PackageHelp(JgsFunctionPath search, IReadOnlyList<string> parts)
    {
        string qualified = string.Join('.', parts);
        (IReadOnlyList<string> members, IReadOnlyList<string> subPackages, IReadOnlyList<string> classFolders) = search.Tree.Contents(parts);
        var entries = new List<(string Name, string Description)>();
        foreach (string member in members)
        {
            string kind = search.FindDotted(qualified + "." + member, out _) is { } file ? KindOfFile(file) : "function";
            entries.Add((member, $"{qualified}.{member} is a {kind}."));
        }

        string folderPrefix = string.Join(Path.DirectorySeparatorChar, parts.Select(static p => "+" + p));
        foreach (string classFolder in classFolders)
        {
            entries.Add((classFolder, $"{folderPrefix}{Path.DirectorySeparatorChar}@{classFolder} is a folder."));
        }

        foreach (string sub in subPackages)
        {
            entries.Add((sub, $"{qualified}.{sub} is a namespace."));
        }

        int width = entries.Count == 0 ? 0 : entries.Max(static e => e.Name.Length);
        var text = new StringBuilder();
        text.Append($"Contents of {qualified} namespace:\n\n");
        foreach ((string entryName, string description) in entries)
        {
            text.Append(entryName.PadLeft(width)).Append(" - ").Append(description).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// A code file's help: its first comment block with each line's <c>%</c> read as a space (the
    /// block at the top of the file, or the one right under the function line), or, when it has
    /// none, "name is a function." with the header on the next line under four spaces, the name
    /// written as it was asked for (<c>r = pkg.f(x)</c>), or "name is a script." (R2025b, measured).
    /// </summary>
    private static string FileHelp(string file, string asked)
    {
        string[] lines;
        try
        {
            lines = JgsMlapp.ReadSource(file).ReplaceLineEndings("\n").Split('\n');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return $"{asked} is a function.\n";
        }

        int first = 0;
        while (first < lines.Length && lines[first].Trim().Length == 0)
        {
            first++;
        }

        if (first >= lines.Length)
        {
            return $"{asked} is a script.\n";
        }

        string head = lines[first].TrimStart();
        int blockAt = head.StartsWith('%') ? first : first + 1;
        var block = new StringBuilder();
        for (int i = blockAt; i < lines.Length && lines[i].TrimStart().StartsWith('%'); i++)
        {
            block.Append(' ').Append(lines[i].TrimStart()[1..]).Append('\n');
        }

        if (block.Length > 0)
        {
            return block.ToString();
        }

        if (StartsWithWord(head, "function"))
        {
            return $"{asked} is a function.\n    {QualifiedHeader(head, asked)}\n";
        }

        if (StartsWithWord(head, "classdef"))
        {
            return $"{asked} is a class.\n";
        }

        return $"{asked} is a script.\n";
    }

    /// <summary>The function header's call form with the name as it was asked for: <c>function r = f(x)</c> → <c>r = pkg.f(x)</c>.</summary>
    private static string QualifiedHeader(string functionLine, string asked)
    {
        string header = functionLine["function".Length..].Trim();
        int equals = header.IndexOf('=');
        string outputs = equals >= 0 ? header[..equals].Trim() + " = " : string.Empty;
        string call = equals >= 0 ? header[(equals + 1)..].Trim() : header;
        int paren = call.IndexOf('(');
        string rest = paren >= 0 ? call[paren..] : string.Empty;
        return outputs + asked + rest;
    }

    /// <summary>What a code file is, read from its first line of code: a class, a function, or a script.</summary>
    private static string KindOfFile(string file)
    {
        string[] lines;
        try
        {
            lines = JgsMlapp.ReadSource(file).ReplaceLineEndings("\n").Split('\n');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return "function"; // a P-file: its source is not there to read
        }

        foreach (string line in lines)
        {
            string code = line.TrimStart();
            if (code.Length == 0 || code.StartsWith('%'))
            {
                continue;
            }

            return StartsWithWord(code, "classdef") ? "class" : StartsWithWord(code, "function") ? "function" : "script";
        }

        return "script";
    }

    private static bool StartsWithWord(string text, string word) =>
        text.StartsWith(word, StringComparison.Ordinal)
        && (text.Length == word.Length || !(char.IsLetterOrDigit(text[word.Length]) || text[word.Length] == '_'));
}
