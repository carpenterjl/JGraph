using System.Text;

namespace JGraph.Deploy;

/// <summary>
/// The log files a build writes beside its target, in the layout R2025b's mcc writes them (stage K0's
/// <c>recordings/build_side.out.txt</c>, stage K2's <c>recordings/k2_mcc.out.txt</c>): byte for byte
/// the same headers, separators and line ends, with JGraph's own runtime where R2025b lists its
/// runtime's add-ons.
/// </summary>
public static class BuildLogs
{
    /// <summary>The excluded-files log.</summary>
    public const string ExcludedFilesLog = "mccExcludedFiles.log";

    /// <summary>The unresolved-symbols list, deprecated in R2025b and still written.</summary>
    public const string UnresolvedSymbolsFile = "unresolvedSymbols.txt";

    /// <summary>The runtime products list, deprecated in R2025b and still written.</summary>
    public const string RequiredProductsFile = "requiredMCRProducts.txt";

    /// <summary>The support packages list; JGraph has no support packages, so it is always empty.</summary>
    public const string SupportPackagesFile = "includedSupportPackages.txt";

    /// <summary>The build result <c>compiler.build.Results</c> reads its <c>RuntimeDependencies</c> from.</summary>
    public const string BuildResultFile = "buildresult.json";

    /// <summary>
    /// The runtime product number a JGraph build needs: R2025b's MATLAB Base Runtime's, the one every
    /// build lists. JGraph's runtime is one install, so it is the only one listed.
    /// </summary>
    public const int RuntimeProductId = 35010;

    /// <summary>The name <see cref="BuildResultFile"/> gives JGraph's runtime.</summary>
    public const string RuntimeDisplayName = "JGraph Runtime";

    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>Writes all five files into <paramref name="folder"/>.</summary>
    /// <param name="folder">The build's output folder.</param>
    /// <param name="report">The analysis.</param>
    /// <param name="archivePath">Where a file sits inside the archive (<c>k2_main\k2_main.m</c>), for the unresolved list.</param>
    public static void WriteAll(string folder, DependencyReport report, Func<string, string> archivePath)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(archivePath);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ExcludedFilesLog), ExcludedFiles(report), Utf8);
        File.WriteAllText(Path.Combine(folder, UnresolvedSymbolsFile), UnresolvedSymbols(report, archivePath), Utf8);
        File.WriteAllText(Path.Combine(folder, RequiredProductsFile), RequiredProducts(), Utf8);
        File.WriteAllText(Path.Combine(folder, SupportPackagesFile), "", Utf8);
        File.WriteAllText(Path.Combine(folder, BuildResultFile), BuildResult(), Utf8);
    }

    /// <summary>
    /// <c>mccExcludedFiles.log</c>: two header lines, then one line per non-deployable function the
    /// code names, its exclusion rule column empty as R2025b leaves it; line feeds. JGraph names the
    /// function, where R2025b names its file under its own install.
    /// </summary>
    public static string ExcludedFiles(DependencyReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var text = new StringBuilder("Excluded Files\nExcluded Files\t Exclusion Message ID\t Reason For Exclusion\t Exclusion Rule\n");
        foreach (ExcludedDependency excluded in report.Excluded)
        {
            text.Append(excluded.Name).Append('\t').Append(excluded.MessageId).Append('\t').Append(excluded.Reason).Append("\t\n");
        }

        return text.ToString();
    }

    /// <summary><c>unresolvedSymbols.txt</c>: a header, then path, symbol and reason per name; CR LF.</summary>
    public static string UnresolvedSymbols(DependencyReport report, Func<string, string> archivePath)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(archivePath);
        var text = new StringBuilder("Path\tSymbol\tReason\r\n");
        foreach (UnresolvedSymbol symbol in report.Unresolved)
        {
            text.Append(archivePath(symbol.File)).Append('\t').Append(symbol.Symbol).Append('\t').Append(symbol.Reason).Append("\r\n");
        }

        return text.ToString();
    }

    /// <summary><c>requiredMCRProducts.txt</c>: the product numbers, each followed by a tab, no line end.</summary>
    public static string RequiredProducts() => RuntimeProductId + "\t";

    /// <summary><c>buildresult.json</c>, laid out as R2025b writes it: tabs and CR LF.</summary>
    public static string BuildResult() =>
        "{\r\n\t\"RuntimeDependencies\": [\r\n\t\t{\"ID\":" + RuntimeProductId + ", \"DisplayName\":\""
        + RuntimeDisplayName + "\", \"Optional\":\"no\"}\r\n\t]\r\n}\r\n";
}
