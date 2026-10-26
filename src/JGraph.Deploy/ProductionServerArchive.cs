using System.Text;

namespace JGraph.Deploy;

/// <summary>A Production Server archive build's files: what <c>compiler.build.Results.Files</c> lists, and the archive's manifest.</summary>
/// <param name="Files">The archive, as R2025b's results list it (k14_build: main.Files).</param>
/// <param name="Manifest">The archive's manifest.</param>
public sealed record ProductionServerArchiveBuild(IReadOnlyList<string> Files, CtfManifest Manifest);

/// <summary>
/// A Production Server archive (compiler and SDK plan, stage K14), as R2025b's mcc <c>-W CTF:&lt;name&gt; -U</c>
/// and <c>compiler.build.productionServerArchive</c> make one (<c>record-k14.ps1</c>, <c>recordings\k14_build</c>):
/// <c>&lt;name&gt;.ctf</c> with the archive's functions public, its discovery signatures in the manifest
/// and its routes file at <see cref="RoutesFile.ArchivePath"/>, and a readme, in JGraph's words, saying
/// how a JGraph Production Server deploys it. The server (<c>jgraph-mps</c>) deploys what its
/// <c>auto_deploy</c> folder holds and answers <c>POST /&lt;archive&gt;/&lt;function&gt;</c>.
/// </summary>
public static class ProductionServerArchive
{
    /// <summary>
    /// Writes the archive and the readme into <paramref name="outputFolder"/>; answers the archive's
    /// path (the results' one file) and its manifest.
    /// </summary>
    /// <param name="build">The archive's contents, its discovery signatures applied.</param>
    /// <param name="outputFolder">The folder the build writes into.</param>
    /// <param name="archiveName">The archive's name, which the server routes by.</param>
    /// <param name="routes">The routes file to carry, or null.</param>
    /// <param name="written">Each file written, for <c>-v</c>'s lines.</param>
    public static ProductionServerArchiveBuild Write(CtfBuild build, string outputFolder, string archiveName, RoutesFile? routes, Action<string>? written = null)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentException.ThrowIfNullOrEmpty(archiveName);
        string folder = Path.GetFullPath(outputFolder);
        Directory.CreateDirectory(folder);
        var utf8 = new UTF8Encoding(false);

        CtfBuild archive = build with
        {
            TargetType = CtfTargetType.ProductionServerArchive,
            MainFunction = null,
            MadeFiles = routes is null ? build.MadeFiles : [.. build.MadeFiles, (utf8.GetBytes(routes.Text), RoutesFile.ArchivePath)],
        };
        string ctf = Path.Combine(folder, archiveName + ".ctf");
        CtfManifest manifest = CtfArchiveWriter.Write(ctf, archive);

        // R2025b writes the readme after the archive, and names it under -v (k14_build: main.printed).
        string readme = Path.Combine(folder, "readme.txt");
        File.WriteAllText(readme, Readme(archiveName).Replace("\n", "\r\n", StringComparison.Ordinal), utf8);
        written?.Invoke(readme);
        return new ProductionServerArchiveBuild([ctf], manifest);
    }

    /// <summary>The readme beside the archive, in JGraph's words (R2025b's names MATLAB Production Server's clients and documentation).</summary>
    public static string Readme(string archiveName) =>
        $$$"""
        {{{archiveName}}} for the JGraph Production Server

        {{{archiveName}}}.ctf is a deployable archive for the JGraph Production Server, the server that
        runs the functions of such archives on the JGraph Runtime and answers HTTP requests for them.

        1. Deploy {{{archiveName}}}.ctf

        A server instance is a folder made by `jgraph-mps new <folder>`: its config\main_config names
        the port (--http), the worker count (--num-workers) and the rest, and `jgraph-mps run <folder>`
        runs it. To deploy {{{archiveName}}}.ctf, copy it into the instance's `auto_deploy` folder: the
        server deploys it while it runs and when it starts, and retires it when the file goes.

        2. Call its functions

        A client sends POST /{{{archiveName}}}/<function> with a JSON body of MATLAB Production Server's
        form, {"rhs":[...],"nargout":n,"outputFormat":{"mode":"small"|"large","nanType":"string"|"object"}},
        and reads {"lhs":[...]} back, or {"error":{...}} for an error the function raised. mps.json.encoderequest
        and mps.json.decoderesponse write and read those bodies in MATLAB code; MATLAB Production
        Server's client libraries and its documentation of the RESTful API describe the same requests.

        """;
}
