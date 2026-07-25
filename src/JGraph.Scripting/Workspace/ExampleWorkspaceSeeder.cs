using System.Collections.Generic;
using System.IO;

namespace JGraph.Scripting.Workspace;

/// <summary>
/// Works out which shipped example files a first-run workspace still needs. The copy itself is the
/// host's job; deciding what to copy is testable policy, so it lives here.
/// </summary>
/// <remarks>
/// The examples cannot simply be opened where they ship: an installed <c>examples/</c> folder sits
/// beside the executable and may be read-only, so a workspace rooted there could not be saved into.
/// They are copied into the user's documents instead, and only the files that are not already there —
/// re-running never overwrites work the user has done to a copy.
/// </remarks>
public static class ExampleWorkspaceSeeder
{
    /// <summary>One file to copy, as absolute source and target paths.</summary>
    public readonly record struct SeedFile(string Source, string Target);

    /// <summary>
    /// Pairs each source file with where it belongs under <paramref name="targetRoot"/>, preserving
    /// the folder structure below <paramref name="sourceRoot"/>, and drops the ones already present.
    /// </summary>
    /// <param name="sourceFiles">Absolute paths of the shipped example files.</param>
    /// <param name="sourceRoot">The folder <paramref name="sourceFiles"/> are relative to.</param>
    /// <param name="targetRoot">The workspace folder to seed.</param>
    /// <param name="targetExists">Reports whether a target path is already present.</param>
    public static IReadOnlyList<SeedFile> Plan(
        IEnumerable<string> sourceFiles,
        string sourceRoot,
        string targetRoot,
        Func<string, bool> targetExists)
    {
        ArgumentNullException.ThrowIfNull(sourceFiles);
        ArgumentException.ThrowIfNullOrEmpty(sourceRoot);
        ArgumentException.ThrowIfNullOrEmpty(targetRoot);
        ArgumentNullException.ThrowIfNull(targetExists);

        var plan = new List<SeedFile>();
        foreach (string source in sourceFiles)
        {
            string relative = Path.GetRelativePath(sourceRoot, source);
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                continue; // outside the source root — not ours to copy
            }

            string target = Path.Combine(targetRoot, relative);
            if (!targetExists(target))
            {
                plan.Add(new SeedFile(source, target));
            }
        }

        return plan;
    }
}
