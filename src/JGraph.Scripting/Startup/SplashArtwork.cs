using System.Collections.Generic;
using System.IO;

namespace JGraph.Scripting.Startup;

/// <summary>
/// Finds the image the startup splash should show. The artwork is replaceable without a rebuild:
/// drop a <c>splash.png</c> (or .jpg/.jpeg/.bmp) into <c>%AppData%\JGraph</c> to override it, or ship
/// one beside the executable to brand a deployment. With neither, the splash draws its built-in
/// design.
/// </summary>
public static class SplashArtwork
{
    /// <summary>The base file name, without extension, that every candidate uses.</summary>
    public const string BaseName = "splash";

    /// <summary>The image extensions probed, in preference order.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".png", ".jpg", ".jpeg", ".bmp"];

    /// <summary>
    /// Returns the first artwork file that exists, or null for the built-in design. The user's own
    /// folder wins over the deployment's, so a personal choice is never overwritten by an update.
    /// </summary>
    /// <param name="appDataDirectory">The per-user JGraph folder, or null to skip it.</param>
    /// <param name="executableDirectory">The folder the application runs from, or null to skip it.</param>
    /// <param name="fileExists">Reports whether a candidate path exists.</param>
    public static string? Find(string? appDataDirectory, string? executableDirectory, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);

        foreach (string? directory in new[] { appDataDirectory, executableDirectory })
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            foreach (string extension in Extensions)
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
