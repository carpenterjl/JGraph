using System.IO.Compression;
using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>zip</c> and <c>unzip</c> (compiler and SDK plan, stage K3, because users package a program's
/// files with them), transcribed from R2025b's <c>toolbox/matlab/io/archive</c> (<c>zip.m</c>,
/// <c>unzip.m</c> and <c>matlab.io.internal.archive.*</c>) and measured by the fixture
/// <c>k3_zip_unzip</c>.
/// </summary>
/// <remarks>
/// Two divergences (ADR 0258): <c>unzip</c> refuses an entry that would land outside the output
/// folder (R2025b writes <c>../x</c> above it), and neither function encrypts or decrypts (R2025b's
/// <c>Password</c> and <c>EncryptionMethod</c>), so a password is refused by <c>zip</c> and an
/// encrypted entry by <c>unzip</c>. A URL is not downloaded.
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly char[] WindowsInvalidNameCharacters = [':', '*', '?', '"', '<', '>', '|'];

    /// <summary>Declares <c>zip</c> and <c>unzip</c>, which read and write relative to the session's current folder.</summary>
    internal static void RegisterArchiveBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        env.Builtins.Register("zip", JgsValue.Function(new BuiltinFunction("zip",
            (args, line, col) => Zip(host, args, 1, line, col)[0])
        {
            KeepsStringArguments = true,
            BindsAnsAsStatement = false,
            MultiOutput = (args, wanted, line, col) => Zip(host, args, wanted, line, col),
        }));

        env.Builtins.Register("unzip", JgsValue.Function(new BuiltinFunction("unzip",
            (args, line, col) => Unzip(host, args, 1, line, col)[0])
        {
            KeepsStringArguments = true,
            BindsAnsAsStatement = false,
            MultiOutput = (args, wanted, line, col) => Unzip(host, args, wanted, line, col),
        }));
    }

    /// <summary>
    /// <c>entries = zip(zipfile, files, rootfolder)</c>: the files, folders (with everything under
    /// them) and wildcard matches named, written to <c>zipfile</c> (<c>.zip</c> added unless it ends
    /// so), each entry named relative to <c>rootfolder</c> (the current folder when omitted), an
    /// absolute name by its file name; the entry names as a 1-by-n cell.
    /// </summary>
    private static JgsValue[] Zip(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", $"Invalid argument list. Function requires {2 - args.Count} more input(s).");
        }

        (int positional, IReadOnlyDictionary<string, JgsValue> options) = ArchiveOptions(args, 3, ["Password", "EncryptionMethod"], line, col);
        string zipName = ArchiveTextScalar(args[0], 1, line, col);
        string[] files = ArchiveTexts(args[1], 2, line, col);
        string rootDir = positional > 2 ? ArchiveRootFolder(args[2], line, col) : "";
        if (wanted > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:nargoutchk:tooManyOutputs", "Too many output arguments.");
        }

        if (options.Count > 0)
        {
            throw new JgsRuntimeException(line, col, "JGraph:zip:encryptionNotSupported",
                "JGraph's zip does not encrypt archives; Password and EncryptionMethod are not supported.");
        }

        string cwd = host.CurrentDirectory;
        if (rootDir.Length > 0 && !Directory.Exists(Path.Combine(cwd, rootDir)))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:io:archive:checkFilesDirInputs:nonExistDir",
                $"ROOTDIR directory \"{rootDir}\" does not exist.");
        }

        // parseArchiveInputs: .zip unless the name already ends with it, and never a folder.
        if (!Path.GetExtension(zipName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            zipName += ".zip";
        }

        // A name that is only an extension (zip('out/', ...) became out/.zip) names its folder.
        bool noName = MatlabFileParts(zipName).Name is var leaf && leaf.LastIndexOf('.') is 0;
        if (noName || Directory.Exists(Path.Combine(cwd, zipName)))
        {
            string shown = noName ? MatlabFileParts(zipName).Folder : zipName;
            throw new JgsRuntimeException(line, col, "MATLAB:zip:isDirectory", $"ZIP filename \"{shown}\" is a directory.");
        }

        List<(string File, string Entry)> entries = ArchiveEntries(files, rootDir, cwd, line, col);

        // checkDuplicateEntries: two files may not share an entry name.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((_, string entry) in entries)
        {
            if (!seen.Add(entry))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:zip:duplicateEntry", $"Function ZIP tried to add two files as \"{entry}\".");
            }
        }

        // filterOutArchiveFile: the archive is never one of its own entries.
        string zipPath = Path.GetFullPath(Path.Combine(cwd, zipName));
        if (File.Exists(zipPath))
        {
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (string.Equals(Path.GetFullPath(Path.Combine(cwd, entries[i].File)), zipPath, comparison))
                {
                    Warn(host, "MATLAB:zip:archiveName", $"ZIP cannot add the archive file: {zipPath} to the archive file itself.");
                    entries.RemoveAt(i);
                }
            }
        }

        if (entries.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:zip:noEntries", "Nothing to zip.");
        }

        string target = host.ResolveForWrite(Path.Combine(cwd, zipName));
        try
        {
            using FileStream stream = File.Create(target);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8);
            foreach ((string file, string entry) in entries)
            {
                string source = Path.Combine(cwd, file);
                if (Directory.Exists(source))
                {
                    archive.CreateEntry(entry + "/");
                    continue;
                }

                ZipArchiveEntry zipped = archive.CreateEntry(entry, CompressionLevel.Optimal);
                DateTime written = File.GetLastWriteTime(source);
                zipped.LastWriteTime = written.Year < 1980 ? new DateTime(1980, 1, 1) : written;
                using Stream into = zipped.Open();
                using FileStream from = File.OpenRead(source);
                from.CopyTo(into);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:zip:openError", $"Could not create \"{zipName}\": {ex.Message}");
        }

        return [ArchiveNames(entries.Select(e => e.Entry))];
    }

    /// <summary>
    /// R2025b's <c>getArchiveEntries</c>: a queue of the names given (repeats dropped, first kept),
    /// each a file, a folder (its contents queued behind everything already there, in ASCII order),
    /// an empty folder (an entry itself), or a wildcard in the last part of the name (its matches
    /// queued). Each file's entry is its name relative to the root folder with forward slashes and no
    /// leading <c>./</c>; an absolute name's is its file name.
    /// </summary>
    private static List<(string File, string Entry)> ArchiveEntries(string[] files, string rootDir, string cwd, int line, int col)
    {
        if (files.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:zip:noEntries", "Nothing to zip.");
        }

        var queue = new Queue<(string Path, string File, bool Original)>();
        foreach (string file in files.Distinct(StringComparer.Ordinal))
        {
            queue.Enqueue(IsArchiveAbsolute(file)
                ? (Path.GetDirectoryName(file) ?? "", Path.GetFileName(file), true)
                : (rootDir, file, true));
        }

        bool rootIsCurrent = rootDir.Length == 0 || rootDir == ".";
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<(string File, string Entry)>();
        while (queue.Count > 0)
        {
            (string pathstr, string file, bool original) = queue.Dequeue();
            if (pathstr.Contains('*', StringComparison.Ordinal))
            {
                throw WildcardInPath(line, col);
            }

            string fullFileName = MatlabFullFile(pathstr, file);
            bool hasStar = file.Contains('*', StringComparison.Ordinal);
            (bool isDirectory, List<string> contents) = ArchiveListing(Path.Combine(cwd, fullFileName), hasStar);
            if (hasStar && original)
            {
                string folderPart = MatlabFileParts(file).Folder;
                if (folderPart.Contains('*', StringComparison.Ordinal))
                {
                    throw WildcardInPath(line, col);
                }

                if (Path.GetFileName(file).Contains("**", StringComparison.Ordinal))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:io:archive:getArchiveEntries:FilenameHasRecursiveWildcard",
                        "Recursive wildcards are not supported in the filenames input.");
                }

                foreach (string item in contents)
                {
                    queue.Enqueue((pathstr, MatlabFullFile(folderPart, item), false));
                }
            }
            else if (isDirectory)
            {
                foreach (string item in contents)
                {
                    queue.Enqueue((pathstr, MatlabFullFile(file, item), false));
                }
            }
            else if (contents.Count != 1 && !File.Exists(Path.Combine(cwd, fullFileName)))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:zip:fileDoesNotExist", $"File \"{fullFileName}\" does not exist.");
            }
            else
            {
                string folder = pathstr.Length == 0 ? cwd.Replace('\\', '/') : ConvertArchiveSlash(pathstr);
                string candidate = rootIsCurrent && !IsArchiveAbsolute(fullFileName)
                    ? folder + "/" + ConvertArchiveSlash(fullFileName)
                    : ConvertArchiveSlash(fullFileName);
                if (candidates.Add(candidate))
                {
                    entries.Add((fullFileName, ConvertArchiveSlash(file)));
                }
            }
        }

        return entries;
    }

    private static JgsRuntimeException WildcardInPath(int line, int col) =>
        new(line, col, "MATLAB:io:archive:getArchiveEntries:FilepathHasWildcard",
            "Wildcards are not supported in the paths of the filenames input.");

    /// <summary>
    /// What <c>dir(name)</c> answers, as <c>getArchiveEntries</c>' <c>isDirectory</c> reads it: a
    /// folder's contents (sorted, <c>.</c> and <c>..</c> left out; an empty folder is not a folder
    /// but a one-item list of itself), a wildcard's matches, a file as one item, or nothing.
    /// </summary>
    private static (bool IsDirectory, List<string> Contents) ArchiveListing(string full, bool hasStar)
    {
        var options = new EnumerationOptions
        {
            AttributesToSkip = 0,
            IgnoreInaccessible = true,
            MatchType = MatchType.Simple,
            MatchCasing = OperatingSystem.IsWindows() ? MatchCasing.CaseInsensitive : MatchCasing.CaseSensitive,
        };
        if (hasStar)
        {
            string folder = Path.GetDirectoryName(full) is { Length: > 0 } f ? f : ".";
            List<string> matches = Directory.Exists(folder)
                ? [.. Directory.EnumerateFileSystemEntries(folder, Path.GetFileName(full), options).Select(Path.GetFileName).OfType<string>()]
                : [];

            // dir lists . and .. for a pattern that matches them, which makes the list "a folder" and sorted.
            matches.Sort(StringComparer.Ordinal);
            return (matches.Count > 1, matches);
        }

        if (Directory.Exists(full))
        {
            List<string> contents = [.. Directory.EnumerateFileSystemEntries(full, "*", options).Select(Path.GetFileName).OfType<string>()];
            contents.Sort(StringComparer.Ordinal);
            return contents.Count == 0 ? (false, [full]) : (true, contents);
        }

        return File.Exists(full) ? (false, [Path.GetFileName(full)]) : (false, []);
    }

    /// <summary>
    /// <c>files = unzip(zipfile, outputfolder)</c>: every entry written under <c>outputfolder</c> (the
    /// current folder when omitted or empty, made when missing), existing files replaced; the names
    /// written as a 1-by-n cell, a folder's ending in the separator, each under the folder as given.
    /// </summary>
    private static JgsValue[] Unzip(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Invalid argument list. Function requires 1 more input(s).");
        }

        (int positional, IReadOnlyDictionary<string, JgsValue> options) = ArchiveOptions(args, 2, ["Password"], line, col);
        string zipName = ArchiveTextScalar(args[0], 1, line, col);
        string outputDir = positional > 1 ? ArchiveTextScalar(args[1], 2, line, col, allowEmpty: true) : ".";
        if (options.TryGetValue("Password", out JgsValue? password))
        {
            ArchiveTextScalar(password, positional + 2, line, col);
        }

        if (wanted > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:nargoutchk:tooManyOutputs", "Too many output arguments.");
        }

        if (outputDir.Length == 0)
        {
            outputDir = ".";
        }

        string cwd = host.CurrentDirectory;
        string asGiven = Path.Combine(cwd, zipName);
        if (Directory.Exists(asGiven) && Directory.EnumerateFileSystemEntries(asGiven).Any())
        {
            throw new JgsRuntimeException(line, col, "MATLAB:unzip:isDirectory", $"ZIPFILENAME \"{zipName}\" is a directory.");
        }

        if (zipName.Contains("://", StringComparison.Ordinal))
        {
            throw new JgsRuntimeException(line, col, "JGraph:unzip:urlNotSupported",
                $"JGraph's unzip reads a file, not a URL; download \"{zipName}\" with websave first.");
        }

        // checkFilename: the name as given, else with .zip or .ZIP added.
        string found = File.Exists(asGiven) ? zipName
            : File.Exists(asGiven + ".zip") ? zipName + ".zip"
            : File.Exists(asGiven + ".ZIP") ? zipName + ".ZIP"
            : throw new JgsRuntimeException(line, col, "MATLAB:io:archive:checkfilename:invalidFilename",
                $"Function UNZIP was unable to find file ''{zipName}''.");

        string outputFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(cwd, outputDir)));
        ZipArchive archive;
        FileStream file = File.OpenRead(Path.Combine(cwd, found));
        try
        {
            archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false, Encoding.UTF8);
        }
        catch (InvalidDataException)
        {
            file.Dispose();
            throw new JgsRuntimeException(line, col, "MATLAB:io:archive:unzip:invalidZipFile", $"Invalid ZIP file \"{found}\".");
        }

        using (archive)
        {
            // Every entry is checked before anything is written, so a refused archive writes nothing.
            var plan = new List<(ZipArchiveEntry Entry, string Name, string Target, bool IsFolder)>();
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                bool isFolder = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
                string name = CleanArchiveName(entry.FullName.TrimEnd('/', '\\').Replace('/', Path.DirectorySeparatorChar));
                string target = Path.GetFullPath(Path.Combine(outputFull, name));
                if (name.Length == 0 || !target.StartsWith(outputFull + Path.DirectorySeparatorChar,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    throw new JgsRuntimeException(line, col, "JGraph:unzip:entryOutsideFolder",
                        $"The entry \"{entry.FullName}\" in \"{found}\" would be written outside \"{outputDir}\", so nothing was extracted.");
                }

                if (entry.IsEncrypted)
                {
                    throw new JgsRuntimeException(line, col, "JGraph:unzip:encryptionNotSupported",
                        $"The entry \"{entry.FullName}\" in \"{found}\" is encrypted; JGraph's unzip does not decrypt archives.");
                }

                plan.Add((entry, name, target, isFolder));
            }

            Directory.CreateDirectory(outputFull);
            var written = new List<string>();
            foreach ((ZipArchiveEntry entry, string name, string target, bool isFolder) in plan)
            {
                string shown = (outputDir == "." ? name : MatlabFullFile(outputDir, name)) + (isFolder ? Path.DirectorySeparatorChar : "");
                try
                {
                    if (isFolder)
                    {
                        Directory.CreateDirectory(target);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        using (FileStream into = File.Create(host.ResolveForWrite(target)))
                        using (Stream from = entry.Open())
                        {
                            from.CopyTo(into);
                        }

                        File.SetLastWriteTime(target, entry.LastWriteTime.DateTime);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:unzip:openError", $"Could not write \"{shown}\": {ex.Message}");
                }

                written.Add(shown);
            }

            return [ArchiveNames(written)];
        }
    }

    /// <summary>
    /// The arguments block's split of positional arguments from trailing name-value pairs: past the
    /// required ones, a text naming one of <paramref name="names"/> (any case) with an even count left
    /// starts the pairs; otherwise anything past <paramref name="most"/> positional ones is refused in
    /// R2025b's words.
    /// </summary>
    private static (int Positional, IReadOnlyDictionary<string, JgsValue> Options) ArchiveOptions(
        IReadOnlyList<JgsValue> args, int most, string[] names, int line, int col)
    {
        int required = most - 1;
        int start = args.Count;
        for (int at = required; at < args.Count; at++)
        {
            if ((args.Count - at) % 2 == 0 && IsTextScalar(args[at])
                && names.Any(n => n.Equals(TextOf(args[at]), StringComparison.OrdinalIgnoreCase)))
            {
                start = at;
                break;
            }
        }

        if (start > most)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs",
                $"Invalid argument at position {most + 1}. Function requires {required} to {most} positional input(s).");
        }

        var options = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        for (int at = start; at + 1 < args.Count; at += 2)
        {
            string given = IsTextScalar(args[at]) ? TextOf(args[at]) : "";
            string name = names.FirstOrDefault(n => n.Equals(given, StringComparison.OrdinalIgnoreCase))
                ?? throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs",
                    $"Invalid name-value argument '{given}'. Valid names are: {string.Join(", ", names)}.");
            options[name] = args[at + 1];
        }

        return (start, options);
    }

    /// <summary>A <c>mustBeTextScalar, mustBeNonzeroLengthText</c> argument.</summary>
    private static string ArchiveTextScalar(JgsValue value, int position, int line, int col, bool allowEmpty = false)
    {
        if (!IsTextScalar(value))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeTextScalar",
                $"Invalid argument at position {position}. Value must be a character vector or string scalar.");
        }

        string text = TextOf(value);
        if (text.Length == 0 && !allowEmpty)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeNonzeroLengthText",
                $"Invalid argument at position {position}. Value must be text with one or more characters.");
        }

        return text;
    }

    /// <summary>A <c>mustBeText, mustBeNonzeroLengthText</c> argument, as its elements.</summary>
    private static string[] ArchiveTexts(JgsValue value, int position, int line, int col)
    {
        string[]? texts = value.Type == JgsType.Cell && value.AsCell.Length == 0 ? [] : TextElementsOf(value);
        if (texts is null || value.Type == JgsType.Cell && !Array.TrueForAll(value.AsCell, e => e.Type == JgsType.String))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeText",
                $"Invalid argument at position {position}. Value must be a character vector, string array, or cell array of character vectors.");
        }

        if (Array.Exists(texts, t => t.Length == 0))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeNonzeroLengthText",
                $"Invalid argument at position {position}. Value must be text with one or more characters.");
        }

        return texts;
    }

    /// <summary>The root folder, converted to text as the arguments block's <c>(1,:) string</c> converts it.</summary>
    private static string ArchiveRootFolder(JgsValue value, int line, int col)
    {
        if (IsTextScalar(value))
        {
            return TextOf(value);
        }

        if (value.Type == JgsType.Number)
        {
            return value.AsNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        throw new JgsRuntimeException(line, col, "MATLAB:validation:UnableToConvert",
            "Invalid argument at position 3. Value must be of type string or be convertible to string.");
    }

    private static JgsValue ArchiveNames(IEnumerable<string> names)
    {
        JgsValue[] cells = names.Select(n => JgsValue.Str(n)).ToArray();
        JgsValue list = JgsValue.Cell(cells);
        list.Reshape(1, cells.Length);
        return list;
    }

    /// <summary>R2025b's <c>isAbsolute</c>: a drive (<c>C:\</c>, <c>C:/</c>) or a UNC start on Windows, a leading <c>/</c> elsewhere.</summary>
    private static bool IsArchiveAbsolute(string file) =>
        OperatingSystem.IsWindows()
            ? file.StartsWith(@"\\", StringComparison.Ordinal) || file.StartsWith("//", StringComparison.Ordinal)
                || System.Text.RegularExpressions.Regex.IsMatch(file, @"^[a-zA-Z]*:[\\/]")
            : file.StartsWith('/');

    /// <summary>R2025b's <c>convertSlash</c>: forward slashes, a leading <c>./</c> dropped.</summary>
    private static string ConvertArchiveSlash(string name)
    {
        string forward = name.Replace('\\', '/');
        return forward.StartsWith("./", StringComparison.Ordinal) ? forward[2..] : forward;
    }

    /// <summary>R2025b's <c>cleanupWindowsPathname</c>: <c>: * ? " &lt; &gt; |</c> after any drive made underscores.</summary>
    private static string CleanArchiveName(string name)
    {
        if (!OperatingSystem.IsWindows())
        {
            return name;
        }

        var cleaned = new StringBuilder(name);
        for (int i = 0; i < cleaned.Length; i++)
        {
            if (Array.IndexOf(WindowsInvalidNameCharacters, cleaned[i]) >= 0)
            {
                cleaned[i] = '_';
            }
        }

        return cleaned.ToString();
    }

    /// <summary><c>fullfile(a, b)</c> for two parts: the separator between them made the platform's, an empty part dropped.</summary>
    private static string MatlabFullFile(string first, string second)
    {
        if (first.Length == 0)
        {
            return second.Replace('/', Path.DirectorySeparatorChar);
        }

        string joined = first.TrimEnd('/', '\\') + Path.DirectorySeparatorChar + second.TrimStart('/', '\\');
        return joined.Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary><c>fileparts</c>' folder part.</summary>
    private static (string Folder, string Name) MatlabFileParts(string file)
    {
        int cut = file.LastIndexOfAny(['/', '\\']);
        return cut < 0 ? ("", file) : (file[..cut], file[(cut + 1)..]);
    }
}
