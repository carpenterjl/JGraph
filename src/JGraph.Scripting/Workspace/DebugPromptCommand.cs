namespace JGraph.Scripting.Workspace;

/// <summary>What a debugger word typed at the <c>K&gt;&gt;</c> prompt asks for.</summary>
public enum DebugPromptVerb
{
    /// <summary><c>dbcont</c>: run to the next breakpoint or the end.</summary>
    Continue,

    /// <summary><c>dbstep</c> (optionally <c>dbstep N</c>): execute the next line(s), stepping over calls.</summary>
    Step,

    /// <summary><c>dbstep in</c>: execute the next line, entering a called function.</summary>
    StepIn,

    /// <summary><c>dbstep out</c>: run until the current function returns.</summary>
    StepOut,

    /// <summary><c>dbquit</c>: end the run where it is paused.</summary>
    Quit,

    /// <summary><c>dbstack</c>: print the call stack.</summary>
    Stack,

    /// <summary><c>dbup</c> (optionally <c>dbup N</c>): move the prompt's frame towards the caller.</summary>
    Up,

    /// <summary><c>dbdown</c> (optionally <c>dbdown N</c>): move the prompt's frame back towards the pause.</summary>
    Down,
}

/// <summary>
/// One of MATLAB's debugger commands as typed at the paused prompt — <c>dbcont</c>, <c>dbstep</c>,
/// <c>dbstep in</c>, <c>dbstep out</c>, <c>dbquit</c>, <c>dbstack</c>, <c>dbup</c>, <c>dbdown</c>.
/// They are host commands rather than builtins: each one drives the debugger the paused script is
/// under, which only the host holds. Anything that is not one of them is a statement for the
/// interpreter, so the parser answers false rather than guessing.
/// </summary>
/// <param name="Verb">What was asked.</param>
/// <param name="Count">How many times (<c>dbstep 3</c>, <c>dbup 2</c>); 1 when not given.</param>
public sealed record DebugPromptCommand(DebugPromptVerb Verb, int Count)
{
    /// <summary>
    /// Recognises a debugger command. Command syntax only (<c>dbstep in</c>, not <c>dbstep('in')</c>),
    /// case-sensitive like MATLAB's own names, with a trailing <c>;</c> or <c>,</c> tolerated because
    /// prompt habits carry it everywhere.
    /// </summary>
    /// <param name="input">The text typed at the prompt.</param>
    /// <param name="command">The parsed command, or null when <paramref name="input"/> is not one.</param>
    public static bool TryParse(string input, out DebugPromptCommand? command)
    {
        ArgumentNullException.ThrowIfNull(input);
        command = null;
        string trimmed = input.Trim().TrimEnd(';', ',').TrimEnd();
        string[] words = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || words.Length > 2)
        {
            return false;
        }

        string? argument = words.Length == 2 ? words[1] : null;
        switch (words[0])
        {
            case "dbcont" when argument is null:
                command = new DebugPromptCommand(DebugPromptVerb.Continue, 1);
                return true;

            case "dbquit" when argument is null:
                command = new DebugPromptCommand(DebugPromptVerb.Quit, 1);
                return true;

            case "dbstack" when argument is null:
                command = new DebugPromptCommand(DebugPromptVerb.Stack, 1);
                return true;

            case "dbstep" when argument is null:
                command = new DebugPromptCommand(DebugPromptVerb.Step, 1);
                return true;

            case "dbstep" when argument == "in":
                command = new DebugPromptCommand(DebugPromptVerb.StepIn, 1);
                return true;

            case "dbstep" when argument == "out":
                command = new DebugPromptCommand(DebugPromptVerb.StepOut, 1);
                return true;

            case "dbstep" when TryCount(argument, out int steps):
                command = new DebugPromptCommand(DebugPromptVerb.Step, steps);
                return true;

            case "dbup" when argument is null || TryCount(argument, out _):
                command = new DebugPromptCommand(DebugPromptVerb.Up, argument is null ? 1 : int.Parse(argument, System.Globalization.CultureInfo.InvariantCulture));
                return true;

            case "dbdown" when argument is null || TryCount(argument, out _):
                command = new DebugPromptCommand(DebugPromptVerb.Down, argument is null ? 1 : int.Parse(argument, System.Globalization.CultureInfo.InvariantCulture));
                return true;

            default:
                return false;
        }
    }

    private static bool TryCount(string? text, out int count) =>
        int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out count)
        && count > 0;
}
