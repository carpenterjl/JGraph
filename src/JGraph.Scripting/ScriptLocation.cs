using System.Globalization;
using System.Text.RegularExpressions;

namespace JGraph.Scripting;

/// <summary>
/// A place in a file that a line of console output names, in the layout errors are printed in:
/// <c>C:\work\sub.m(4,12): message</c> (<see cref="ScriptDiagnostic"/>) and a C# compiler's
/// <c>C:\src\Helper.cs(12,5): error CS1002: ; expected</c> (<c>jgraph.net.compile</c>, ADR 0179). A
/// location without a file (<c>(4,12): message</c>) is in the document that was run. The console
/// opens the place on a double-click (interop plan, stage 10, ADR 0183).
/// </summary>
/// <param name="Path">The absolute path, or null when the line names no file.</param>
/// <param name="Line">The 1-based line.</param>
/// <param name="Column">The 1-based column.</param>
public sealed partial record ScriptLocation(string? Path, int Line, int Column)
{
    /// <summary>The first location <paramref name="text"/> names, or null when it names none.</summary>
    public static ScriptLocation? Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Match match = Pattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        string path = match.Groups["path"].Value.Trim();
        return new ScriptLocation(
            path.Length == 0 ? null : path,
            int.Parse(match.Groups["line"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["col"].Value, CultureInfo.InvariantCulture));
    }

    // An absolute path (a drive or a UNC share; spaces allowed, the characters Windows refuses in a
    // path not), or nothing at the start of the text or after a space or a colon, then (line,col) and
    // the colon the message follows. A location glued to anything else names no file this can open:
    // C# compiled from text reports <string>(1,40), and f(2,3): is a call.
    [GeneratedRegex(@"(?:(?<path>(?:[A-Za-z]:[\\/]|\\\\)[^()<>|""*?\r\n]*?)|(?<=^|[\s:]))\((?<line>\d{1,7}),(?<col>\d{1,7})\):", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
