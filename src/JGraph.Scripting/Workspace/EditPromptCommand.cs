using System.Text.RegularExpressions;

namespace JGraph.Scripting.Workspace;

/// <summary>What <c>edit</c> or <c>open</c> at the prompt asks for.</summary>
public enum EditPromptVerb
{
    /// <summary><c>edit</c>: open a script in an editor tab (a new one when nothing is named).</summary>
    Edit,

    /// <summary><c>open</c>: open whatever the name is — a variable in the Data Viewer, a file in its pane.</summary>
    Open,
}

/// <summary>
/// MATLAB's <c>edit name</c> and <c>open name</c> as typed at the prompt, in command syntax
/// (<c>edit foo</c>, <c>edit foo.m</c>) or function syntax (<c>edit('foo')</c>). Host commands, like
/// the debugger words: each opens something in the window, which the interpreter cannot reach.
/// </summary>
/// <param name="Verb">Which of the two.</param>
/// <param name="Argument">The name or path given, without quotes, or null for a bare <c>edit</c>.</param>
public sealed partial record EditPromptCommand(EditPromptVerb Verb, string? Argument)
{
    [GeneratedRegex(@"^\s*(edit|open)(?:\s*\(\s*(?:'([^']*)'|""([^""]*)"")\s*\)|\s+(\S.*?)|)\s*;?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();

    /// <summary>
    /// Recognises an <c>edit</c>/<c>open</c> command. A bare <c>open</c> is not one — MATLAB's
    /// <c>open</c> needs a name, and the interpreter's own <c>open</c> can say so.
    /// </summary>
    /// <param name="input">The text typed at the prompt.</param>
    /// <param name="command">The parsed command, or null when <paramref name="input"/> is not one.</param>
    public static bool TryParse(string input, out EditPromptCommand? command)
    {
        ArgumentNullException.ThrowIfNull(input);
        command = null;
        Match match = Shape().Match(input);
        if (!match.Success)
        {
            return false;
        }

        EditPromptVerb verb = match.Groups[1].Value == "edit" ? EditPromptVerb.Edit : EditPromptVerb.Open;
        string? argument = match.Groups[2].Success ? match.Groups[2].Value
            : match.Groups[3].Success ? match.Groups[3].Value
            : match.Groups[4].Success ? match.Groups[4].Value.Trim().Trim('\'', '"')
            : null;
        if (argument is { Length: 0 })
        {
            argument = null;
        }

        if (verb == EditPromptVerb.Open && argument is null)
        {
            return false;
        }

        command = new EditPromptCommand(verb, argument);
        return true;
    }
}
