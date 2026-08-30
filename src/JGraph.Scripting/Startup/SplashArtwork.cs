using System.Collections.Generic;
using System.IO;

namespace JGraph.Scripting.Startup;

/// <summary>
/// Finds the artwork the startup splash should show. It is replaceable without a rebuild: drop a
/// <c>splash.apng</c> (an animation) or a <c>splash.png</c> (or .jpg/.jpeg/.bmp) into
/// <c>%AppData%\JGraph</c> to override it, or ship one beside the executable to brand a deployment.
/// With neither, the splash draws its built-in design.
///
/// An animation and a still are looked for separately and the animation wins, because the two are
/// not the same job: the animation is the splash's background and the wordmark stays over it, while
/// a still replaces the wordmark outright.
/// </summary>
public static class SplashArtwork
{
    /// <summary>The base file name, without extension, that every candidate uses.</summary>
    public const string BaseName = "splash";

    /// <summary>The still-image extensions probed, in preference order.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".png", ".jpg", ".jpeg", ".bmp"];

    /// <summary>
    /// The animation extensions probed. Only one, and it is the only video container here that
    /// carries an alpha channel — a splash with a background painted into it is the thing this
    /// replaces (see <c>AnimatedPngReader</c>).
    /// </summary>
    public static IReadOnlyList<string> AnimationExtensions { get; } = [".apng"];

    /// <summary>
    /// Returns the first artwork file that exists, or null for the built-in design. The user's own
    /// folder wins over the deployment's, so a personal choice is never overwritten by an update.
    /// </summary>
    /// <param name="appDataDirectory">The per-user JGraph folder, or null to skip it.</param>
    /// <param name="executableDirectory">The folder the application runs from, or null to skip it.</param>
    /// <param name="fileExists">Reports whether a candidate path exists.</param>
    public static string? Find(string? appDataDirectory, string? executableDirectory, Func<string, bool> fileExists) =>
        Probe(appDataDirectory, executableDirectory, Extensions, fileExists);

    /// <summary>
    /// Returns the first animated artwork that exists, or null for none. Searched in the same two
    /// places and the same order as <see cref="Find"/>.
    /// </summary>
    /// <param name="appDataDirectory">The per-user JGraph folder, or null to skip it.</param>
    /// <param name="executableDirectory">The folder the application runs from, or null to skip it.</param>
    /// <param name="fileExists">Reports whether a candidate path exists.</param>
    public static string? FindAnimation(string? appDataDirectory, string? executableDirectory, Func<string, bool> fileExists) =>
        Probe(appDataDirectory, executableDirectory, AnimationExtensions, fileExists);

    private static string? Probe(
        string? appDataDirectory,
        string? executableDirectory,
        IReadOnlyList<string> extensions,
        Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);

        foreach (string? directory in new[] { appDataDirectory, executableDirectory })
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            foreach (string extension in extensions)
            {
                string candidate = Path.Combine(directory, BaseName + extension);
                if (fileExists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
