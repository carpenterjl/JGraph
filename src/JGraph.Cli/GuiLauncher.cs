using System.Diagnostics;

namespace JGraph.Cli;

/// <summary>
/// Starts the WPF application for the modes that need a window. The launcher never links against it —
/// that would pull WPF into a console executable that is meant to run without a display — so the two
/// meet as processes, over the same command line.
/// </summary>
internal static class GuiLauncher
{
    private const string ExecutableName = "JGraph.Application.exe";

    /// <summary>
    /// Runs the application to completion with its output piped to this console, and returns its exit
    /// code. Used by <c>-batch -showfigures</c>, where the caller is waiting for the run to finish.
    /// </summary>
    public static int RunAndWait(IReadOnlyList<string> args)
    {
        if (Locate() is not { } executable)
        {
            Console.Error.WriteLine(NotFoundMessage);
            return -1;
        }

        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        AddArguments(info, args);

        using Process process = Process.Start(info)
            ?? throw new InvalidOperationException($"Could not start '{executable}'.");
        process.OutputDataReceived += (_, e) => WriteIfNotNull(Console.Out, e.Data);
        process.ErrorDataReceived += (_, e) => WriteIfNotNull(Console.Error, e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        return process.ExitCode;
    }

    /// <summary>
    /// Starts the application and returns immediately, leaving the session to the user — the shell
    /// gets its prompt back, as it does for any GUI program.
    /// </summary>
    public static bool StartDetached(IReadOnlyList<string> args)
    {
        if (Locate() is not { } executable)
        {
            Console.Error.WriteLine(NotFoundMessage);
            return false;
        }

        var info = new ProcessStartInfo(executable) { UseShellExecute = true };
        AddArguments(info, args);
        using Process? process = Process.Start(info);
        return process is not null;
    }

    private static string NotFoundMessage =>
        $"jgraph: could not find {ExecutableName} next to this program. " +
        "Reinstall JGraph, or build the application project first.";

    /// <summary>
    /// Finds the application executable: beside this one in a deployed layout, then in the sibling
    /// project's build output, so the launcher also works from a development tree.
    /// </summary>
    private static string? Locate()
    {
        string baseDirectory = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string deployed = Path.Combine(baseDirectory, ExecutableName);
        if (File.Exists(deployed))
        {
            return deployed;
        }

        // …/src/JGraph.Cli/bin/<Config>/<tfm>  →  …/src/JGraph.Application/bin/<Config>/net8.0-windows
        var output = new DirectoryInfo(baseDirectory);
        if (output.Parent is { } configuration &&
            configuration.Parent is { Name: "bin" } bin &&
            bin.Parent?.Parent is { } sourceRoot)
        {
            string sibling = Path.Combine(
                sourceRoot.FullName, "JGraph.Application", "bin", configuration.Name,
                "net8.0-windows", ExecutableName);
            if (File.Exists(sibling))
            {
                return sibling;
            }
        }

        return null;
    }

    private static void AddArguments(ProcessStartInfo info, IReadOnlyList<string> args)
    {
        foreach (string arg in args)
        {
            info.ArgumentList.Add(arg);
        }
    }

    private static void WriteIfNotNull(TextWriter writer, string? line)
    {
        if (line is not null)
        {
            writer.WriteLine(line);
        }
    }
}
