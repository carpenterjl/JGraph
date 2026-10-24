namespace JGraph.Deploy;

/// <summary>
/// <c>extractCTF archive [folder]</c>, R2025b's utility (<c>bin\win64\extractCTF.exe</c>, measured in
/// stage K3): an archive, or a program carrying one, extracted into <c>&lt;name&gt;_mcr</c> in the
/// current folder, or straight into the folder given; arguments past the second ignored; what it did
/// on standard error, its usage on standard output; 0 on success and -1 otherwise.
/// </summary>
public static class ExtractCtfCommand
{
    /// <summary>Runs the command.</summary>
    /// <param name="args">The command's arguments.</param>
    /// <param name="currentFolder">The folder relative names are read against.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    /// <param name="programName">What the usage line calls the program.</param>
    /// <returns>The exit code.</returns>
    public static int Run(IReadOnlyList<string> args, string currentFolder, TextWriter output, TextWriter error, string programName)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (args.Count == 0)
        {
            output.WriteLine($"{programName} takes one mandatory input argument: the name of the CTF file to be extracted "
                + "and one optional input argument: parent directory for the extracted files.");
            error.WriteLine("No CTF file specified at the input. ");
            return -1;
        }

        string archive = args[0];
        string archivePath = Path.GetFullPath(Path.Combine(currentFolder, archive));
        string folder = args.Count > 1
            ? Path.GetFullPath(Path.Combine(currentFolder, args[1]))
            : Path.Combine(currentFolder, Path.GetFileNameWithoutExtension(archivePath) + "_mcr");
        try
        {
            using CtfArchiveReader reader = CtfArchiveReader.Open(archivePath);
            reader.ExtractTo(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            error.WriteLine($"Could not open the CTF file {archive}");
            if (ex is InvalidDataException && File.Exists(archivePath))
            {
                error.WriteLine(ex.Message);
            }

            return -1;
        }

        error.WriteLine(args.Count > 1
            ? $"{archive} CTF file extracted in {args[1]}"
            : $"{archive} CTF file extracted in the current working directory");
        return 0;
    }
}
