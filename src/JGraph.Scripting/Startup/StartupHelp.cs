namespace JGraph.Scripting.Startup;

/// <summary>The process exit codes both executables use, so a shell script can branch on them.</summary>
public static class StartupExitCodes
{
    /// <summary>The script ran to completion (or there was nothing to run).</summary>
    public const int Success = 0;

    /// <summary>The script failed: a syntax error, a runtime error, or a missing engine.</summary>
    public const int ScriptError = 1;

    /// <summary>The command line itself was wrong; nothing ran.</summary>
    public const int UsageError = 2;
}

/// <summary>
/// The startup-option documentation, held in one place so the launcher's <c>-help</c> output and the
/// application's help dialog cannot drift from each other or from the parser.
/// </summary>
public static class StartupHelp
{
    /// <summary>The file name of the scripting guide, looked for in a <c>docs</c> folder beside the executable.</summary>
    public const string GuideFileName = "jgs-scripting-guide.html";

    /// <summary>The full flag reference, printed by <c>-h</c>/<c>-help</c>.</summary>
    public static string UsageText =>
        """
        JGraph — scientific graphing and scripting.

        Usage:
          jgraph                              Open the interactive application.
          jgraph -batch "statement"           Run non-interactively, log to stdout, then exit.
          jgraph -r "statement"               Run, then keep the session open until the script calls exit.
          jgraph -h | -help                   Show this text and open the scripting guide.

        Options:
          -batch "statement"    Executes the statement without a user interface and exits when it
                                finishes. Output goes to stdout, errors to stderr.
          -r "statement"        Executes the statement in the interactive application and leaves the
                                session open. The application closes when the script calls exit.
          -logfile "path"       Also writes all output, results and errors to a text file. The file is
                                appended to, and flushed after every line.
          -showfigures          With -batch only: display figures in standalone windows instead of
                                suppressing them. The process then exits once the last window closes.
          -sd "directory"       Use this directory instead of the current one for relative paths.
          -h, -help             Show this text.

        The statement:
          If it names a file that exists, that file is run, and its extension picks the language
          (.jgs, .csx/.cs, .py). Otherwise the statement is evaluated as JGS source.

        Exit codes:
          0  the script finished             2  the command line was invalid
          1  the script failed               n  whatever the script passed to exit(n)

        Examples:
          jgraph -batch "let x = 1:10; disp(sum(x))"
          jgraph -batch "measurements.jgs" -logfile run.log
          jgraph -batch "plot(1:10); exportfigure('trend.png')" -sd "C:\work"
        """;

    /// <summary>
    /// Finds the HTML scripting guide next to the executable, or null when it was not deployed.
    /// Probes the build output first, then the repository layout, so it works from both.
    /// </summary>
    public static string? FindGuide(string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(baseDirectory);
        string[] candidates =
        [
            Path.Combine(baseDirectory, "docs", GuideFileName),
            Path.Combine(baseDirectory, GuideFileName),
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }
}
