using JGraph.Scripting.Jgs;

namespace JGraph.Scripting.Startup;

/// <summary>
/// <c>jgraph mcc …</c> and <c>jgmcc …</c> (compiler and SDK plan, stage K7): JGraph's mcc from the
/// operating system's command line, as R2025b's <c>bin\mcc.bat</c> runs its mcc (k7_cmd). The build
/// runs in a fresh MATLAB-dialect session over the folder the command was given in; what mcc prints
/// goes to standard output, a warning there as <c>[Warning: …] </c>, and a refusal to standard error
/// as <c>{message}</c> with a closing line, exit code 1. An argument wrapped in single quotes loses
/// them, as cmd.exe passes them on and R2025b's mcc takes them off.
/// </summary>
public static class MccCommand
{
    /// <summary>Runs mcc over <paramref name="args"/> in <paramref name="directory"/>; answers the exit code.</summary>
    public static int Run(IReadOnlyList<string> args, string directory, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        string[] words = [.. args.Select(static a => a.Length >= 2 && a[0] == '\'' && a[^1] == '\'' ? a[1..^1] : a)];
        var sink = new Sink(output, error);
        var context = new ScriptContext(sink, static (_, _) => { }, directory);
        var session = new JgsReplSession(context, JgsDialect.Matlab, "MATLAB");
        try
        {
            var host = (JGraphScriptGlobals)session.Interpreter.Host!;
            ScriptThread.Run(() => JgsBuiltins.Mcc(session.Workspace, session.Interpreter, host, words, 0, 0)).GetAwaiter().GetResult();
            return StartupExitCodes.Success;
        }
        catch (JgsRuntimeException refused)
        {
            error.Write("{\b" + Trimmed(refused.Message) + "\n}\b \nERROR: JGraph error Exit Status: 0x00000001\n");
            return StartupExitCodes.ScriptError;
        }
        finally
        {
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>A message's lines without their trailing spaces, and no newline at its end, as mcc.bat prints one (k7_cmd).</summary>
    private static string Trimmed(string message) =>
        string.Join('\n', message.TrimEnd('\n').Split('\n').Select(static l => l.TrimEnd(' ')));

    /// <summary>
    /// mcc's output on standard output, its warnings there between R2025b's markers: <c>[</c> and
    /// <c>]</c> each followed by a backspace, which a console draws over (k7_cmd), as <c>{</c> and
    /// <c>}</c> mark a refusal on standard error.
    /// </summary>
    private sealed class Sink(TextWriter output, TextWriter error) : IScriptOutput
    {
        public void Write(string text) => output.Write(text);

        public void WriteLine(string text) => output.WriteLine(text);

        public void WriteError(string text)
        {
            if (text.StartsWith("Warning: ", StringComparison.Ordinal))
            {
                // A warning whose text ends a line closes on the next, as inside MATLAB (k7_switches).
                output.Write("[\b" + Trimmed(text) + (text.EndsWith('\n') ? "\n" : "") + "]\b \n");
            }
            else
            {
                error.WriteLine(text);
            }
        }
    }
}
