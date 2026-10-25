using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// How a deployed session starts its program and how it reports the error that ends one
/// (compiler and SDK plan, stage K1), as R2025b's MATLAB Runtime was recorded doing in stage K0
/// (<c>tools/matlab-checklist/compiler-probes/recordings</c>).
/// </summary>
internal static class JgsDeployedMain
{
    /// <summary>The R2025b file that turns an <c>mcc -n</c> program's arguments into values, named in its errors.</summary>
    private const string NumericMainFile = "mainFuncForNumericStandaloneApplication.m";

    private const string NumericMainName = "mainFuncForNumericStandaloneApplication";

    private const string NotNumericId = "MCR:mainFuncForNumericStandaloneApplication:COM_ERROR_INPUT_ARGUMENT_TYPE";

    /// <summary>
    /// Runs the <c>startup.m</c> the program was built with, before its main: R2025b compiles in
    /// a startup file beside the main and runs it as a script in the base workspace (K0, startup).
    /// Only one on the frozen path counts; the user's own startup never runs in a deployed session.
    /// </summary>
    internal static void RunStartup(Interpreter interpreter, ScriptDeployment deployment)
    {
        if (interpreter.FunctionPath?.Find("startup") is not { } file
            || !deployment.FrozenPath.Contains(Path.GetDirectoryName(file) ?? string.Empty, JgsFunctionPath.PathComparer))
        {
            return;
        }

        interpreter.EvaluateSource("startup", interpreter.Globals, 0, 0, asStatement: true);
    }

    /// <summary>
    /// The values the main function is called with: each argument as the char row it arrived as,
    /// or for an <c>mcc -n</c> program the value of each one that looks numeric
    /// (<see cref="LooksNumeric"/>), evaluated, as R2025b's
    /// <c>mainFuncForNumericStandaloneApplication</c> does, and a refusal for any other.
    /// </summary>
    internal static IReadOnlyList<JgsValue> Arguments(Interpreter interpreter, ScriptDeployment deployment)
    {
        var values = new JgsValue[deployment.Arguments.Count];
        for (int i = 0; i < values.Length; i++)
        {
            string argument = deployment.Arguments[i];
            values[i] = deployment.NumericArguments ? Evaluated(interpreter, argument) : JgsValue.Str(argument);
        }

        return values;
    }

    /// <summary>One <c>mcc -n</c> argument: its value, or R2025b's error raised where R2025b raises it.</summary>
    private static JgsValue Evaluated(Interpreter interpreter, string argument)
    {
        if (!LooksNumeric(argument))
        {
            throw InNumericMain(new JgsRuntimeException(21, 0, NotNumericId,
                $"The input argument \n  '{argument}'\nis not recognized as a numeric type.") { Raised = true });
        }

        try
        {
            return interpreter.EvaluateSource(argument, new JgsEnvironment(interpreter.Globals) { IsCallBoundary = true }, 18, 0);
        }
        catch (JgsRuntimeException failure)
        {
            throw InNumericMain(new JgsRuntimeException(18, 0, failure.Identifier, failure.Message));
        }
    }

    /// <summary>Puts an error in the two frames R2025b's report names: findArgType at its line, then the loop at line 11.</summary>
    private static JgsRuntimeException InNumericMain(JgsRuntimeException error)
    {
        error.PushFrame("findArgType", NumericMainFile, 11);
        error.PushFrame(NumericMainName, NumericMainFile, 0);
        return error;
    }

    /// <summary>
    /// Whether <c>mcc -n</c> evaluates an argument: R2025b's builtin <c>mclisnumerictype</c>, as
    /// <c>probe_k0_numeric</c> and <c>probe_k1_numeric</c> measured it over 145 strings. A letter
    /// refuses the argument when it starts a word: at the start, or after anything but a digit, a
    /// letter, <c>_</c>, <c>.</c> or <c>@</c>. A quote of either kind accepts whatever follows it.
    /// So <c>1e3</c>, <c>3i</c>, <c>0x1F</c>, <c>1Inf</c>, <c>.e</c> and <c>'txt'</c> are evaluated,
    /// and <c>pi</c>, <c>Inf</c>, <c>-e</c>, <c>1+pi</c> and <c>int8(4)</c> refused. Whatever is
    /// evaluated and is not valid code (<c>1_000</c>, <c>1 2</c>) fails in the evaluation.
    /// </summary>
    internal static bool LooksNumeric(string argument)
    {
        for (int i = 0; i < argument.Length; i++)
        {
            char c = argument[i];
            if (c is '\'' or '"')
            {
                return true;
            }

            if (char.IsAsciiLetter(c) && (i == 0 || !(char.IsAsciiLetterOrDigit(argument[i - 1]) || argument[i - 1] is '_' or '.' or '@')))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The report R2025b's runtime writes to standard error for the error that ended a program (K0,
    /// exit_error, exit_nested, exit_builtin, exit_cause, argsn_word): an <c>Error using</c> line for
    /// the frame that raised it, the message, a blank line, its causes, an <c>Error in</c> line and a
    /// blank one for every frame it unwound through, and its identifier last.
    /// </summary>
    internal static string Report(JgsException error) => Report(error, handler: false);

    /// <summary>
    /// <see cref="Report(JgsException)"/>, or with <paramref name="handler"/> the report R2025b's
    /// runtime hands a C shared library's error handler (stage K10, k10_feval): the same lines
    /// without the identifier, ending in their blank line.
    /// </summary>
    internal static string Report(JgsException error, bool handler)
    {
        var report = new StringBuilder();
        var runtime = error as JgsRuntimeException;
        IReadOnlyList<(string Name, string File, int Line)> frames = runtime?.Frames ?? [];
        int unwound = 0;
        if (runtime is { Raised: true } && frames.Count > 0)
        {
            report.Append("Error using ").Append(Shown(frames[0])).Append(" (line ").Append(frames[0].Line).Append(")\n");
            unwound = 1;
        }
        else if (runtime?.UsingName is { } refused)
        {
            report.Append("Error using ").Append(refused).Append('\n');
        }

        report.Append(error.Message).Append("\n\n");
        string[] causes = Causes(runtime);
        if (causes.Length > 0)
        {
            report.Append("Caused by:\n");
            foreach (string cause in causes)
            {
                report.Append("    ").Append(cause.Replace("\n", "\n    ", StringComparison.Ordinal)).Append('\n');
            }

            report.Append('\n');
        }

        for (int i = unwound; i < frames.Count; i++)
        {
            report.Append("Error in ").Append(Shown(frames[i])).Append(" (line ").Append(frames[i].Line).Append(")\n\n");
        }

        if (handler)
        {
            return report.ToString();
        }

        if (runtime is { Identifier.Length: > 0 })
        {
            report.Append(runtime.Identifier).Append('\n');
        }

        return report.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// Writes <see cref="Report(JgsException)"/> to standard error, or hands it to a sink that keeps a log
    /// (<c>-logfile</c>, stage K4): R2025b's log takes the report without its identifier line.
    /// </summary>
    internal static void WriteReport(IScriptOutput output, JgsException error)
    {
        // A windowed program (mcc -e) has nowhere to write: R2025b shows the error in a dialog
        // instead, and exits when it is dismissed (stage K6, k6_windows: e_err).
        IScriptOutput shown = output is JGraph.Scripting.Startup.DeployedLogOutput log ? log.Console : output;
        if (shown is JGraph.Scripting.Startup.IDeployedErrorDialog dialog)
        {
            dialog.ShowError(DialogText(error));
        }

        string report = Report(error);
        if (output is JGraph.Scripting.Startup.IDeployedReportSink sink)
        {
            sink.WriteReport(report, (error as JgsRuntimeException)?.Identifier);
        }
        else
        {
            output.WriteError(report);
        }
    }

    /// <summary>
    /// The text of the dialog a windowed program's uncaught error shows (k6_windows, e_err): the
    /// message, then <c>Error in =&gt; k6_err.m at line 12</c> for the frame that raised it.
    /// </summary>
    internal static string DialogText(JgsException error)
    {
        IReadOnlyList<(string Name, string File, int Line)> frames = (error as JgsRuntimeException)?.Frames ?? [];
        return frames.Count > 0 && Path.GetFileName(frames[0].File) is { Length: > 0 } file
            ? $"{error.Message}\nError in => {file} at line {frames[0].Line}"
            : error.Message;
    }

    /// <summary>A frame as the report names it: a local function of a file as <c>file&gt;local</c>.</summary>
    internal static string Shown((string Name, string File, int Line) frame)
    {
        string stem = Path.GetFileNameWithoutExtension(frame.File);

        // A method of the file's class is Class/method, as R2025b's report names it (k9client: handle.increment.nargout0).
        if (stem.Length > 0 && frame.Name.StartsWith(stem + ".", StringComparison.Ordinal))
        {
            return stem + "/" + frame.Name[(stem.Length + 1)..];
        }

        return stem.Length > 0 && stem != frame.Name && !frame.Name.Contains('>', StringComparison.Ordinal)
            ? $"{stem}>{frame.Name}"
            : frame.Name;
    }

    /// <summary>The messages of the causes a thrown MException carried, or none.</summary>
    internal static string[] Causes(JgsRuntimeException? error)
    {
        if (error?.Carried is not JgsValue { Type: JgsType.Struct } thrown
            || !thrown.AsStruct.TryGetValue("cause", out JgsValue? held) || held.Type != JgsType.Cell)
        {
            return [];
        }

        return
        [
            .. held.AsCell
                .Where(static cause => cause.Type == JgsType.Struct && cause.AsStruct.TryGetValue("message", out _))
                .Select(static cause => cause.AsStruct["message"] is { Type: JgsType.String } message ? message.AsString : cause.AsStruct["message"].Display()),
        ];
    }
}
