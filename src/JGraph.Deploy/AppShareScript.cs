using System.Text;

namespace JGraph.Deploy;

/// <summary>
/// What App Details gives App Designer's Share windows (R2025b's <c>matlab.internal.deployment.getAppDetails</c>):
/// the name, version, author, summary and description the packaging options are set from.
/// </summary>
public sealed record AppShareDetails(string Name, string Version, string Author, string Summary, string Description);

/// <summary>
/// The scripts App Designer's Share windows run (App Designer window plan, stage W12; R2025b's
/// <c>matlab.internal.deployment.deployAppDesignerProject</c> and its builders
/// <c>AppDesignerDeployScriptBuilder</c>, <c>StandaloneAppDesignerScriptBuilder</c> and
/// <c>WebAppAppDesignerScriptBuilder</c>, read in place and measured by the W0 probe <c>w0_share</c>). The
/// window keeps no project: it writes a script over <c>compiler.build</c> and <c>compiler.package</c> and
/// runs it, the build into <c>&lt;app folder&gt;\release\build</c> and the installer into
/// <c>release\package</c>. The text is R2025b's to the character, but for one correction: R2025b pastes
/// App Details' text into double-quoted strings as it is, so a description with a line break makes the
/// script fail with <c>MATLAB:m_unterminated_string_literal</c> and one with a double quote with
/// <c>MATLAB:m_missing_operator</c>; here <see cref="Quoted"/> doubles the quotes and joins the lines with
/// <c>newline</c>.
/// </summary>
public static class AppShareScript
{
    /// <summary>Where an app's shared outputs go (<c>getAppOutputFolder</c>): <c>release</c> beside the app.</summary>
    public static string OutputFolder(string appPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(appPath);
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(appPath)) ?? string.Empty, "release");
    }

    /// <summary>Where a standalone app or web app is built: <c>release\build</c>.</summary>
    public static string BuildFolder(string appPath) => Path.Combine(OutputFolder(appPath), "build");

    /// <summary>Where a standalone app's installer goes: <c>release\package</c>.</summary>
    public static string PackageFolder(string appPath) => Path.Combine(OutputFolder(appPath), "package");

    /// <summary>
    /// The build half of Share &gt; Standalone Desktop App (<c>StandaloneAppDesignerScriptBuilder.getBuildScript</c>):
    /// the options over the app, the files required, the output folder, Verbose, the splash screen and icon when
    /// chosen, the version from App Details, and <c>compiler.build.standaloneWindowsApplication</c>.
    /// </summary>
    /// <param name="appPath">The app's full path.</param>
    /// <param name="requiredFiles">The files the window lists as required, custom ones included; none for an empty list.</param>
    /// <param name="splashScreen">The splash screen image's path, or null or empty for the default.</param>
    /// <param name="icon">The icon image's path, or null or empty for the default.</param>
    /// <param name="details">App Details.</param>
    public static string StandaloneBuild(string appPath, IReadOnlyList<string> requiredFiles, string? splashScreen, string? icon, AppShareDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        var text = new StringBuilder();
        text.Append("buildOpts=compiler.build.StandaloneApplicationOptions(").Append(Quoted(appPath)).Append(");\n");
        AppendCommonBuildLines(text, appPath, requiredFiles);
        if (!string.IsNullOrEmpty(splashScreen))
        {
            text.Append("buildOpts.ExecutableSplashScreen=").Append(Quoted(splashScreen)).Append(";\n");
        }

        if (!string.IsNullOrEmpty(icon))
        {
            text.Append("buildOpts.ExecutableIcon=").Append(Quoted(icon)).Append(";\n");
        }

        text.Append("buildOpts.ExecutableVersion = ").Append(Quoted(details.Version)).Append(";\n");
        text.Append("buildResult = compiler.build.standaloneWindowsApplication(buildOpts);\n");
        text.Append('\n');
        return text.ToString();
    }

    /// <summary>
    /// The installer half of Share &gt; Standalone Desktop App (<c>getPackageScript</c>): the options over the
    /// build's result, the package folder, Verbose, and App Details' name, version, author, summary and
    /// description; <paramref name="runtimeDelivery"/>, JGraph's own choice on the window, adds a line only when
    /// it is not R2025b's default (the runtime fetched while installing).
    /// </summary>
    public static string StandalonePackage(string appPath, AppShareDetails details, RuntimeDeliveryChoice runtimeDelivery = RuntimeDeliveryChoice.Web)
    {
        ArgumentNullException.ThrowIfNull(details);
        var text = new StringBuilder();
        text.Append("\npackageOpts = compiler.package.InstallerOptions(buildResult);\n");
        text.Append("packageOpts.OutputDir =").Append(Quoted(PackageFolder(appPath))).Append(";\n");
        text.Append("packageOpts.Verbose = true;\n");
        text.Append("packageOpts.ApplicationName = ").Append(Quoted(details.Name)).Append(";\n");
        text.Append("packageOpts.Version = ").Append(Quoted(details.Version)).Append(";\n");
        text.Append("packageOpts.AuthorName = ").Append(Quoted(details.Author)).Append(";\n");
        text.Append("packageOpts.Summary = ").Append(Quoted(details.Summary)).Append(";\n");
        text.Append("packageOpts.Description = ").Append(Quoted(details.Description)).Append(";\n");
        if (runtimeDelivery != RuntimeDeliveryChoice.Web)
        {
            text.Append("packageOpts.RuntimeDelivery = ").Append(Quoted(runtimeDelivery == RuntimeDeliveryChoice.Installer ? "installer" : "none")).Append(";\n");
        }

        text.Append("compiler.package.installer(buildResult, \"Options\", packageOpts);");
        return text.ToString();
    }

    /// <summary>The whole script Share &gt; Standalone Desktop App runs: the build, a line, the installer (<c>deployAppDesignerProject</c>'s standalone case).</summary>
    public static string Standalone(string appPath, IReadOnlyList<string> requiredFiles, string? splashScreen, string? icon, AppShareDetails details,
        RuntimeDeliveryChoice runtimeDelivery = RuntimeDeliveryChoice.Web) =>
        StandaloneBuild(appPath, requiredFiles, splashScreen, icon, details) + "\n" + StandalonePackage(appPath, details, runtimeDelivery);

    /// <summary>
    /// The script Share &gt; Web App would run (<c>WebAppAppDesignerScriptBuilder</c>): JGraph builds no web app
    /// archive (compiler plan, decision KD2), so the text is shown and never run.
    /// </summary>
    public static string WebApp(string appPath, IReadOnlyList<string> requiredFiles)
    {
        var text = new StringBuilder();
        text.Append("buildOpts=compiler.build.WebAppArchiveOptions(").Append(Quoted(appPath)).Append(");\n");
        AppendCommonBuildLines(text, appPath, requiredFiles);
        text.Append("buildResult = compiler.build.webAppArchive(buildOpts);\n");
        text.Append('\n');
        return text.ToString();
    }

    /// <summary>
    /// A double-quoted MATLAB string holding <paramref name="text"/>: a quote doubled, and the lines of a text with
    /// line breaks joined with <c>newline</c>, so that what App Details holds reaches the options as it is.
    /// </summary>
    public static string Quoted(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        return string.Join(" + newline + ", lines.Select(static line => "\"" + line.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""));
    }

    /// <summary><c>getCommonBuildLines</c>: the files required when any, the build folder, Verbose, a blank line.</summary>
    private static void AppendCommonBuildLines(StringBuilder text, string appPath, IReadOnlyList<string> requiredFiles)
    {
        ArgumentNullException.ThrowIfNull(requiredFiles);
        if (requiredFiles.Count > 0)
        {
            text.Append("buildOpts.AdditionalFiles = [").Append(string.Join(", ", requiredFiles.Select(Quoted))).Append("];\n");
        }

        text.Append("buildOpts.OutputDir = ").Append(Quoted(BuildFolder(appPath))).Append(";\n");
        text.Append("buildOpts.Verbose = true;\n");
        text.Append('\n');
    }
}
