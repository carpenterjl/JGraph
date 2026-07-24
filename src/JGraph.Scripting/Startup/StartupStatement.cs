using JGraph.Scripting.Workspace;

namespace JGraph.Scripting.Startup;

/// <summary>
/// What a <c>-batch</c>/<c>-r</c> argument turned out to be: script source, the language to run it
/// with, and where it came from.
/// </summary>
/// <param name="Code">The script source to execute.</param>
/// <param name="Language">The engine's language name ("JGS", "C#", "Python").</param>
/// <param name="SourcePath">The file the source was read from, or null for an inline statement.</param>
/// <param name="Error">Why the argument could not be turned into a runnable script, or null.</param>
public sealed record ResolvedStatement(string Code, string Language, string? SourcePath, string? Error = null)
{
    /// <summary>A rejected statement, carrying the reason.</summary>
    public static ResolvedStatement Invalid(string error) =>
        new(string.Empty, "JGS", SourcePath: null, error);

    /// <summary>The directory the script file lives in, or null for an inline statement.</summary>
    public string? SourceDirectory =>
        SourcePath is null ? null : Path.GetDirectoryName(Path.GetFullPath(SourcePath));
}

/// <summary>
/// Turns the string after <c>-batch</c>/<c>-r</c> into runnable source. It is JGS source unless it
/// names a file that exists, in which case the file is read and its extension picks the engine —
/// so <c>-batch "analysis.jgs"</c> and <c>-batch "disp(1)"</c> both do the obvious thing.
/// </summary>
public static class StartupStatement
{
    /// <summary>Resolves <paramref name="statement"/> against <paramref name="workingDirectory"/>.</summary>
    public static ResolvedStatement Resolve(string? statement, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(workingDirectory);
        if (string.IsNullOrWhiteSpace(statement))
        {
            return ResolvedStatement.Invalid("No statement was given.");
        }

        if (TryFindFile(statement, workingDirectory) is not { } path)
        {
            return new ResolvedStatement(statement, "JGS", SourcePath: null);
        }

        string language = ScriptDocumentModel.LanguageForFile(path);
        if (language == "Text")
        {
            // The argument unambiguously names a file, so evaluating it as JGS source would be a
            // confusing second guess: say what is wrong with the file instead.
            return ResolvedStatement.Invalid(
                $"'{path}' is not a runnable script (expected .jgs, .csx, .cs or .py).");
        }

        try
        {
            return new ResolvedStatement(File.ReadAllText(path), language, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ResolvedStatement.Invalid($"Cannot read '{path}': {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the file the statement names, or null when it names none. A statement is ordinary
    /// source far more often than it is a path, and source is full of characters no path may contain,
    /// so every probe failure simply means "not a file".
    /// </summary>
    private static string? TryFindFile(string statement, string workingDirectory)
    {
        try
        {
            if (Path.IsPathRooted(statement))
            {
                return File.Exists(statement) ? statement : null;
            }

            string candidate = Path.Combine(workingDirectory, statement);
            return File.Exists(candidate) ? candidate : File.Exists(statement) ? statement : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
