using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>zip</c> and <c>unzip</c> (compiler and SDK plan, stage K3, because users package a program's
/// files with them), and <c>gzip</c>, <c>gunzip</c>, <c>tar</c> and <c>untar</c> (open item 171),
/// transcribed from R2025b's <c>toolbox/matlab/io/archive</c> (<c>zip.m</c>, <c>unzip.m</c>,
/// <c>gzip.m</c>, <c>gunzip.m</c>, <c>tar.m</c>, <c>untar.m</c> and
/// <c>matlab.io.internal.archive.*</c>) and measured by the fixtures <c>k3_zip_unzip</c> and
/// <c>oi171_gzip_tar</c>.
/// </summary>
/// <remarks>
/// Divergences (ADR 0258 and the open-items ADR that added the four): <c>unzip</c> and <c>untar</c>
/// refuse an entry that would land outside the output folder (R2025b writes <c>../x</c> above it), and
/// neither <c>zip</c> nor <c>unzip</c> encrypts or decrypts (R2025b's <c>Password</c> and
/// <c>EncryptionMethod</c>): the refusals R2025b gives itself are given in its words, and what it would
/// encrypt or decrypt is refused in JGraph's. A URL is not downloaded.
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

        // gzip, gunzip, tar and untar (open item 171), from the same folder of R2025b's sources.
        void Archiver(string name, Func<JGraphScriptGlobals, IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name,
                (args, line, col) => body(host, args, 1, line, col)[0])
            {
                KeepsStringArguments = true,
                BindsAnsAsStatement = false,
                MultiOutput = (args, wanted, line, col) => body(host, args, wanted, line, col),
            }));

        Archiver("gzip", Gzip);
        Archiver("gunzip", Gunzip);
        Archiver("tar", Tar);
        Archiver("untar", Untar);
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

        (int positional, List<(string Name, JgsValue Value)> options) = ArchiveOptions(args, 3, ["Password", "EncryptionMethod"], line, col);
        string zipName = ArchiveTextScalar(args[0], 1, line, col);
        string[] files = ArchiveTexts(args[1], 2, line, col);
        string rootDir = positional > 2 ? ArchiveRootFolder(args[2], line, col) : "";
        string? password = null, method = null;
        foreach ((string name, JgsValue value) in options)
        {
            string text = ArchiveOptionValue(name, value, line, col);
            if (name == "Password")
            {
                password = text;
            }
            else
            {
                method = text;
            }
        }

        if (wanted > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:nargoutchk:tooManyOutputs", "Too many output arguments.");
        }

        // R2025b's checks on the two names, in its order and words (open item 172, measured): the
        // method is one of the five (ZipCrypto alone off Windows and macOS), and each needs the other.
        if (method is not null)
        {
            string[] methods = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? ["zipcrypto", "aes-128", "aes128", "aes-256", "aes256"]
                : ["zipcrypto"];
            if (!methods.Contains(method.ToLowerInvariant()))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeMember",
                    "Value must be a member of this set:\n" + string.Concat(methods.Select(m => $"    '{m}'\n")));
            }
        }

        if (method is not null && password is null)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:zip:missingPassword",
                "The 'Password' name-value argument must be specified when using the 'EncryptionMethod' name-value argument.");
        }

        if (password is not null && method is null)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:zip:missingEncryptionMethod",
                "The 'EncryptionMethod' name-value argument must be specified when using the 'Password' name-value argument.");
        }

        // Both given is where R2025b encrypts and .NET's ZIP classes cannot (divergence, ADR 0258).
        if (password is not null)
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
        CheckArchiveDuplicates(entries, "zip", line, col);
        FilterOutArchiveSelf(host, entries, zipName, "zip");
        if (entries.Count == 0)
        {
            throw ArchiveNoEntries("zip", line, col);
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
    private static List<(string File, string Entry)> ArchiveEntries(string[] files, string rootDir, string cwd, int line, int col,
        string fcnName = "zip", bool includeEmptyDirs = true)
    {
        if (files.Length == 0)
        {
            throw ArchiveNoEntries(fcnName, line, col);
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
            (bool isDirectory, List<string> contents) = fullFileName.Length == 0
                ? (false, []) // dir('') lists nothing, so an empty name is a file that does not exist
                : ArchiveListing(Path.Combine(cwd, fullFileName), hasStar, includeEmptyDirs);
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
                throw new JgsRuntimeException(line, col, $"MATLAB:{fcnName}:fileDoesNotExist", $"File \"{fullFileName}\" does not exist.");
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
    private static (bool IsDirectory, List<string> Contents) ArchiveListing(string full, bool hasStar, bool includeEmptyDirs = true)
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
            return contents.Count == 0 && includeEmptyDirs ? (false, [full]) : (true, contents);
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

        (int positional, List<(string Name, JgsValue Value)> options) = ArchiveOptions(args, 2, ["Password"], line, col);
        string zipName = ArchiveTextScalar(args[0], 1, line, col);
        string outputDir = positional > 1 ? ArchiveTextScalar(args[1], 2, line, col, allowEmpty: true) : ".";
        bool hasPassword = false;
        foreach ((string name, JgsValue value) in options)
        {
            ArchiveOptionValue(name, value, line, col);
            hasPassword = true;
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

                // Without a password R2025b refuses as well, in these words; with one it decrypts, and
                // .NET's ZIP classes cannot (divergence, ADR 0258).
                if (entry.IsEncrypted && !hasPassword)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:io:archive:extractArchive:unableToWrite",
                        $"Unable to extract file \"{Path.GetFullPath(Path.Combine(cwd, found))}\".\nThe file is password-protected. Use the \"Password\" Name-Value pair to extract password-protected zip files.");
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
    /// The arguments block's split of positional arguments from trailing name-value pairs (measured,
    /// open item 172): past the required ones, a text that starts one of <paramref name="names"/> (any
    /// case, so <c>'Pass'</c> is <c>Password</c>) with an even count left starts the pairs, and so does
    /// whatever follows <paramref name="most"/> positional ones. An odd count left is a name without
    /// its value when it starts with a name, and one positional too many when it does not; a pair
    /// whose name is none of them is refused by name.
    /// </summary>
    private static (int Positional, List<(string Name, JgsValue Value)> Options) ArchiveOptions(
        IReadOnlyList<JgsValue> args, int most, string[] names, int line, int col)
    {
        int required = most - 1;
        int start = Math.Min(args.Count, most);
        for (int at = required; at < start; at++)
        {
            if ((args.Count - at) % 2 == 0 && ArchiveOptionName(args[at], names) is not null)
            {
                start = at;
                break;
            }
        }

        if ((args.Count - start) % 2 == 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", ArchiveOptionName(args[start], names) is null
                ? $"Invalid argument at position {most + 1}. Function requires {required} to {most} positional input(s)."
                : "Invalid argument list. Make sure name-value arguments include both the name and the value.");
        }

        var options = new List<(string Name, JgsValue Value)>();
        for (int at = start; at + 1 < args.Count; at += 2)
        {
            string name = ArchiveOptionName(args[at], names)
                ?? throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs",
                    $"Invalid argument name '{(IsTextScalar(args[at]) ? TextOf(args[at]) : "")}'. " + (names.Length == 1
                        ? $"Use '{names[0]}'."
                        : $"Name must be {string.Join(", ", names[..^1].Select(n => $"'{n}'"))} or '{names[^1]}'."));
            options.Add((name, args[at + 1]));
        }

        return (start, options);
    }

    /// <summary>The name an argument starts, any case, or null.</summary>
    private static string? ArchiveOptionName(JgsValue value, string[] names) =>
        IsTextScalar(value) && TextOf(value) is { Length: > 0 } given
            ? names.FirstOrDefault(n => n.StartsWith(given, StringComparison.OrdinalIgnoreCase))
            : null;

    /// <summary>A name-value argument's <c>mustBeTextScalar, mustBeNonzeroLengthText</c>, in the words R2025b uses for a named one.</summary>
    private static string ArchiveOptionValue(string name, JgsValue value, int line, int col)
    {
        if (!IsTextScalar(value))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeTextScalar",
                $"Invalid value for '{name}' argument. Value must be a character vector or string scalar.");
        }

        return TextOf(value) is { Length: > 0 } text
            ? text
            : throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeNonzeroLengthText",
                $"Invalid value for '{name}' argument. Value must be text with one or more characters.");
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

    // --- gzip, gunzip, tar and untar (open item 171) ----------------------------------------------

    private static JgsRuntimeException ArchiveNoEntries(string fcnName, int line, int col) =>
        new(line, col, $"MATLAB:{fcnName}:noEntries", $"Nothing to {fcnName}.");

    /// <summary>R2025b's <c>checkDuplicateEntries</c>: two files may not share an entry name.</summary>
    private static void CheckArchiveDuplicates(List<(string File, string Entry)> entries, string fcnName, int line, int col)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((_, string entry) in entries)
        {
            if (!seen.Add(entry))
            {
                throw new JgsRuntimeException(line, col, $"MATLAB:{fcnName}:duplicateEntry",
                    $"Function {fcnName.ToUpperInvariant()} tried to add two files as \"{entry}\".");
            }
        }
    }

    /// <summary>R2025b's <c>filterOutArchiveFile</c>: the archive is never one of its own entries, and leaving it out warns.</summary>
    private static void FilterOutArchiveSelf(JGraphScriptGlobals host, List<(string File, string Entry)> entries, string archiveName, string fcnName)
    {
        string cwd = host.CurrentDirectory;
        string archivePath = Path.GetFullPath(Path.Combine(cwd, archiveName));
        if (!File.Exists(archivePath))
        {
            return;
        }

        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            if (string.Equals(Path.GetFullPath(Path.Combine(cwd, entries[i].File)), archivePath, comparison))
            {
                Warn(host, $"MATLAB:{fcnName}:archiveName",
                    $"{fcnName.ToUpperInvariant()} cannot add the archive file: {archivePath} to the archive file itself.");
                entries.RemoveAt(i);
            }
        }
    }

    private static void ArchiveNarginchk(IReadOnlyList<JgsValue> args, int least, int most, int line, int col)
    {
        if (args.Count < least)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > most)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }
    }

    private static void ArchiveNargoutchk(int wanted, int line, int col)
    {
        if (wanted > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:nargoutchk:tooManyOutputs", "Too many output arguments.");
        }
    }

    /// <summary>R2025b's <c>checkFilesDirInputs</c> for its files: a char, a string array or a cell of chars, as names.</summary>
    private static string[] ArchiveFileList(JgsValue value, int line, int col)
    {
        string[]? texts = value.Type == JgsType.Cell && value.AsCell.Length == 0 ? [] : TextElementsOf(value);
        if (texts is null || value.Type == JgsType.Cell && !Array.TrueForAll(value.AsCell, e => e.Type == JgsType.String))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:io:archive:checkFilesDirInputs:isNumeric",
                "FILES must be a character vector, cell array of character vectors, or a string array.");
        }

        return texts;
    }

    /// <summary>What <c>isempty</c> says of an argument once <c>convertStringsToChars</c> has made a string scalar a char.</summary>
    private static bool IsEmptyArchiveArgument(JgsValue value) =>
        value.Type == JgsType.String ? value.AsString.Length == 0
        : value.Type == JgsType.Cell ? value.AsCell.Length == 0
        : IsTextScalar(value) ? TextOf(value).Length == 0
        : value.Type == JgsType.Array && value.ArrayLength == 0;

    /// <summary>A number as <c>num2str</c> shows it, for a refusal that quotes a folder given as a number.</summary>
    private static string ArchiveNumberText(JgsValue value) =>
        IsTextScalar(value) ? TextOf(value)
        : value.Type == JgsType.Number ? value.AsNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "";

    /// <summary>
    /// <c>checkFilesDirInputs</c>' output folder: none when left out or empty, refused when it is not
    /// text or is a file, made with every missing parent when it is not there. The topmost folder made
    /// comes back, so a failure can take it away again as R2025b's <c>dirCreated</c> does.
    /// </summary>
    private static (string OutputDir, string? Created) ArchiveOutputFolder(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int at, int line, int col)
    {
        if (args.Count <= at || IsEmptyArchiveArgument(args[at]))
        {
            return ("", null);
        }

        JgsValue value = args[at];
        if (!IsTextScalar(value))
        {
            // The message formats its argument as {0,number,integer}: rounded half to even, with grouping.
            string shown = value.Type == JgsType.Number
                ? Math.Round(value.AsNumber, MidpointRounding.ToEven).ToString("#,0", System.Globalization.CultureInfo.InvariantCulture)
                : "";
            throw new JgsRuntimeException(line, col, "MATLAB:io:archive:checkFilesDirInputs:invalidDirname",
                $"OUTPUTDIR directory argument \"{shown}\" is not type char or string scalar.");
        }

        string folder = TextOf(value);
        string full = Path.GetFullPath(Path.Combine(host.CurrentDirectory, folder));
        if (Directory.Exists(full))
        {
            return (folder, null);
        }

        if (File.Exists(full))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:io:archive:checkFilesDirInputs:invalidDir",
                $"OUTPUTDIR directory argument \"{folder}\" is the name of a file.  OUTPUTDIR must refer to a directory.");
        }

        string created = full;
        for (string? up = Path.GetDirectoryName(created); up is not null && !Directory.Exists(up); up = Path.GetDirectoryName(up))
        {
            created = up;
        }

        try
        {
            Directory.CreateDirectory(host.ResolveForWrite(full));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:MKDIR:OSError", SystemErrorText(ex, terminated: true));
        }

        return (folder, created);
    }

    private static void RemoveArchiveFolder(string? created)
    {
        try
        {
            if (created is not null && Directory.Exists(created))
            {
                Directory.Delete(created, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // R2025b's rmdir would complain; what failed first is the error worth reporting.
        }
    }

    /// <summary>
    /// R2025b's <c>checkFilename</c>: an empty name is refused, a URL is not downloaded here (JGraph's
    /// refusal), and a local name is found as given or with each extension added (lower case, then
    /// upper); the full name of the file found, as <c>fopen</c> reports it.
    /// </summary>
    private static string ArchiveCheckFilename(JGraphScriptGlobals host, string name, string[] extensions, string fcnName, string argName, int line, int col)
    {
        string shout = fcnName.ToUpperInvariant();
        if (name.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:io:archive:checkfilename:expectedNonEmpty",
                $"Function {shout} expected {argName} to be nonempty.");
        }

        if (name.Contains("://", StringComparison.Ordinal))
        {
            throw new JgsRuntimeException(line, col, $"JGraph:{fcnName}:urlNotSupported",
                $"JGraph's {fcnName} reads a file, not a URL; download \"{name}\" with websave first.");
        }

        string asGiven = Path.Combine(host.CurrentDirectory, name);
        IEnumerable<string> candidates = extensions.Select(e => "." + e.ToLowerInvariant())
            .Concat(extensions.Select(e => "." + e.ToUpperInvariant()))
            .Select(e => asGiven + e)
            .Prepend(asGiven);
        return candidates.FirstOrDefault(File.Exists) is { } found
            ? Path.GetFullPath(found)
            : throw new JgsRuntimeException(line, col, "MATLAB:io:archive:checkfilename:invalidFilename",
                $"Function {shout} was unable to find file ''{name}''.");
    }

    /// <summary>Whether a file starts with gzip's two magic bytes.</summary>
    private static bool IsGzipFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return stream.ReadByte() == 0x1F && stream.ReadByte() == 0x8B;
    }

    /// <summary>
    /// <c>names = gzip(files, outputfolder)</c> (R2025b's <c>gzip.m</c>): every file named, found under
    /// a folder named or matched by a wildcard, compressed to its name and <c>.gz</c>, beside itself or
    /// in <c>outputfolder</c> (made when missing, and taken away again when anything fails); the names
    /// written as a 1-by-n cell. An empty folder adds nothing.
    /// </summary>
    private static JgsValue[] Gzip(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArchiveNarginchk(args, 1, 2, line, col);
        ArchiveNargoutchk(wanted, line, col);
        string[] files = ArchiveFileList(args[0], line, col);
        (string outputDir, string? created) = ArchiveOutputFolder(host, args, 1, line, col);
        string cwd = host.CurrentDirectory;
        try
        {
            List<(string File, string Entry)> entries = ArchiveEntries(files, "", cwd, line, col, "gzip", includeEmptyDirs: false);
            if (entries.Count == 0)
            {
                throw ArchiveNoEntries("gzip", line, col);
            }

            var names = new List<string>(entries.Count);
            foreach ((string file, _) in entries)
            {
                (string folder, string leaf) = MatlabFileParts(file);
                string name = MatlabFullFile(outputDir.Length == 0 ? folder : outputDir, leaf + ".gz");
                try
                {
                    using FileStream from = File.OpenRead(Path.Combine(cwd, file));
                    using FileStream into = File.Create(host.ResolveForWrite(Path.GetFullPath(Path.Combine(cwd, name))));
                    using var packed = new GZipStream(into, CompressionLevel.Optimal);
                    from.CopyTo(packed);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new JgsRuntimeException(line, col, "JGraph:gzip:writeError", $"Could not write \"{name}\": {ex.Message}");
                }

                names.Add(name);
            }

            return [ArchiveNames(names)];
        }
        catch (JgsRuntimeException) when (created is not null)
        {
            RemoveArchiveFolder(created);
            throw;
        }
    }

    /// <summary>
    /// <c>names = gunzip(files, outputfolder)</c> (R2025b's <c>gunzip.m</c>): every gzip file named,
    /// found under a folder named or matched by a wildcard, uncompressed to its name less its extension,
    /// beside itself or in <c>outputfolder</c> (made when missing, and taken away again when anything
    /// fails). A file that would be written over itself is refused, as R2025b refuses it.
    /// </summary>
    private static JgsValue[] Gunzip(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        ArchiveNargoutchk(wanted, line, col);
        string[] files = ArchiveFileList(args[0], line, col);
        (string outputDir, string? created) = ArchiveOutputFolder(host, args, 1, line, col);
        string cwd = host.CurrentDirectory;
        try
        {
            // checkFilesURLInput: one name that is neither a pattern nor a folder must name a file,
            // or a file with .gz added; the entries are still taken from the name as given.
            if (files.Length == 1 && !files[0].Contains('*', StringComparison.Ordinal)
                && (files[0].Length == 0 || !Directory.Exists(Path.Combine(cwd, files[0]))))
            {
                ArchiveCheckFilename(host, files[0], ["gz"], "gunzip", "FILES", line, col);
            }

            List<(string File, string Entry)> entries = ArchiveEntries(files, "", cwd, line, col, "gunzip", includeEmptyDirs: false);
            if (created is null)
            {
                var given = new HashSet<string>(files.Select(f => MatlabFullFile(outputDir, f)), StringComparer.Ordinal);
                string[] collided = [.. entries
                    .Where(e => given.Contains(MatlabFullFile("", e.File)) && !(OperatingSystem.IsWindows()
                        ? System.Text.RegularExpressions.Regex.IsMatch(e.Entry, @"\.[^.]")
                        : e.Entry.Contains('.', StringComparison.Ordinal)))
                    .Select(e => e.File)];
                if (collided.Length > 0)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:io:archive:gunzip:destinationSameAsSource",
                        "Name of unzipped file must differ from name of zipped file if extracted to the same location. Specify a different destination or rename the zipped file(s): "
                        + string.Join(", ", collided));
                }
            }

            var names = new List<string>(entries.Count);
            foreach ((string file, _) in entries)
            {
                (string folder, string leaf) = MatlabFileParts(file);
                int dot = leaf.LastIndexOf('.');
                string name = MatlabFullFile(outputDir.Length == 0 ? folder : outputDir, dot < 0 ? leaf : leaf[..dot]);
                string source = Path.GetFullPath(Path.Combine(cwd, file));
                string target = Path.GetFullPath(Path.Combine(cwd, name));
                JgsRuntimeException notGzip = new(line, col, "MATLAB:io:archive:gunzip:notGzipFormat", $"File \"{file}\" is not in GZIP format.");
                if (!IsGzipFile(source))
                {
                    throw notGzip;
                }

                if (SameFilePath(source, target))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:io:archive:extractArchive:OverwriteOpenFile",
                        $"Cannot overwrite file \"{name}\". The file is already open.");
                }

                try
                {
                    using FileStream from = File.OpenRead(source);
                    using var unpacked = new GZipStream(from, CompressionMode.Decompress);
                    using FileStream into = File.Create(host.ResolveForWrite(target));
                    unpacked.CopyTo(into);
                }
                catch (InvalidDataException)
                {
                    File.Delete(target);
                    throw notGzip;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new JgsRuntimeException(line, col, "JGraph:gunzip:writeError", $"Could not write \"{name}\": {ex.Message}");
                }

                names.Add(name);
            }

            return [ArchiveNames(names)];
        }
        catch (JgsRuntimeException) when (created is not null)
        {
            RemoveArchiveFolder(created);
            throw;
        }
    }

    /// <summary>
    /// <c>entries = tar(tarfile, files, rootfolder)</c> (R2025b's <c>tar.m</c> and
    /// <c>parseArchiveInputs</c>): zip's entries, written as a POSIX tar; a name ending <c>.tgz</c> or
    /// <c>.gz</c> (any case) is gzip-compressed and kept, any other gets <c>.tar</c> unless it ends so.
    /// </summary>
    private static JgsValue[] Tar(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArchiveNarginchk(args, 2, 3, line, col);
        ArchiveNargoutchk(wanted, line, col);
        string[] files = ArchiveFileList(args[1], line, col);
        string cwd = host.CurrentDirectory;
        string rootDir = "";
        if (args.Count > 2 && !IsEmptyArchiveArgument(args[2]))
        {
            if (!IsTextScalar(args[2]) || !Directory.Exists(Path.Combine(cwd, TextOf(args[2]))))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:io:archive:checkFilesDirInputs:nonExistDir",
                    $"ROOTDIR directory \"{ArchiveNumberText(args[2])}\" does not exist.");
            }

            rootDir = TextOf(args[2]);
        }

        if (!IsTextScalar(args[0]))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:tar:invalidFileString", "File name TAR must be a character vector or string scalar.");
        }

        string tarName = TextOf(args[0]);
        string extension = Path.GetExtension(MatlabFileParts(tarName).Name);
        bool compress = extension.Equals(".tgz", StringComparison.OrdinalIgnoreCase) || extension.Equals(".gz", StringComparison.OrdinalIgnoreCase);
        if (!compress && !extension.Equals(".tar", StringComparison.OrdinalIgnoreCase))
        {
            tarName += ".tar";
        }

        (string folder, string leaf) = MatlabFileParts(tarName);
        bool noName = leaf.LastIndexOf('.') == 0;
        if (noName || Directory.Exists(Path.Combine(cwd, tarName)))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:tar:isDirectory", $"TAR filename \"{(noName ? folder : tarName)}\" is a directory.");
        }

        List<(string File, string Entry)> entries = ArchiveEntries(files, rootDir, cwd, line, col, "tar");
        CheckArchiveDuplicates(entries, "tar", line, col);
        FilterOutArchiveSelf(host, entries, tarName, "tar");
        if (entries.Count == 0)
        {
            throw ArchiveNoEntries("tar", line, col);
        }

        string target = host.ResolveForWrite(Path.Combine(cwd, tarName));
        try
        {
            using FileStream stream = File.Create(target);
            using Stream body = compress ? new GZipStream(stream, CompressionLevel.Optimal) : stream;
            using var writer = new TarWriter(body, TarEntryFormat.Pax, leaveOpen: true);
            foreach ((string file, string entry) in entries)
            {
                string source = Path.Combine(cwd, file);
                if (Directory.Exists(source))
                {
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, entry + "/") { ModificationTime = Directory.GetLastWriteTimeUtc(source) });
                }
                else
                {
                    writer.WriteEntry(source, entry);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:tar:openError", $"Could not create \"{tarName}\": {ex.Message}");
        }

        return [ArchiveNames(entries.Select(e => e.Entry))];
    }

    /// <summary>
    /// <c>files = untar(tarfile, outputfolder)</c> (R2025b's <c>untar.m</c> and
    /// <c>parseUnArchiveInputs</c>): the archive found as given or with <c>.tgz</c>, <c>.tar.gz</c> or
    /// <c>.tar</c> added, gzip-compressed or not whatever its name, every file and folder in it written
    /// under <c>outputfolder</c> (the current folder when omitted or empty, made when missing); the names
    /// written as a 1-by-n cell, as unzip names them. An entry that would land outside the folder is
    /// refused before anything is written (divergence, as unzip's in ADR 0258).
    /// </summary>
    private static JgsValue[] Untar(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArchiveNarginchk(args, 1, 2, line, col);
        ArchiveNargoutchk(wanted, line, col);
        string outputDir = ".";
        if (args.Count > 1 && !IsEmptyArchiveArgument(args[1]))
        {
            if (!IsTextScalar(args[1]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:untar:invalidDir", "Output directory must be a character vector or a string scalar.");
            }

            outputDir = TextOf(args[1]);
        }

        if (!IsTextScalar(args[0]))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:exist:firstInputString", "The first input to exist must be a string scalar or character vector.");
        }

        string tarName = TextOf(args[0]);
        string cwd = host.CurrentDirectory;
        if (tarName.Length > 0 && Directory.Exists(Path.Combine(cwd, tarName)))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:untar:isDirectory", $"TARFILENAME \"{tarName}\" is a directory.");
        }

        string found = ArchiveCheckFilename(host, tarName, ["tgz", "tar.gz", "tar"], "untar", "TARFILENAME", line, col);
        string outputFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(cwd, outputDir)));
        if (!Directory.Exists(outputFull))
        {
            try
            {
                Directory.CreateDirectory(host.ResolveForWrite(outputFull));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:io:archive:parseUnArchiveInputs:createPermissionDenied",
                    $"Insufficient permissions to write or create files in the directory \"{MatlabFileParts(found).Folder}\".");
            }
        }

        JgsRuntimeException invalid = new(line, col, "MATLAB:io:archive:untar:invalidTarFile", $"Invalid TAR file {found}.");

        // Every entry is checked before anything is written, so a refused archive writes nothing.
        var plan = new List<(string Name, string Target, bool IsFolder)>();
        try
        {
            using Stream body = OpenTarBody(found);
            var header = new byte[512];
            if (body.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length || !IsTarHeader(header))
            {
                throw invalid;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            throw invalid;
        }

        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        try
        {
            using Stream body = OpenTarBody(found);
            using var reader = new TarReader(body);
            while (reader.GetNextEntry(copyData: false) is { } entry)
            {
                bool isFolder = entry.EntryType == TarEntryType.Directory;
                if (!isFolder && entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile))
                {
                    continue; // links and devices have nothing to write
                }

                string name = CleanArchiveName(entry.Name.TrimEnd('/', '\\').Replace('/', Path.DirectorySeparatorChar));
                string target = Path.GetFullPath(Path.Combine(outputFull, name));
                if (name.Length == 0 || !target.StartsWith(outputFull + Path.DirectorySeparatorChar, comparison))
                {
                    throw new JgsRuntimeException(line, col, "JGraph:untar:entryOutsideFolder",
                        $"The entry \"{entry.Name}\" in \"{found}\" would be written outside \"{outputDir}\", so nothing was extracted.");
                }

                plan.Add((name, target, isFolder));
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or FormatException)
        {
            throw invalid;
        }

        var written = new List<string>(plan.Count);
        using (Stream body = OpenTarBody(found))
        using (var reader = new TarReader(body))
        {
            int at = 0;
            while (reader.GetNextEntry(copyData: false) is { } entry)
            {
                bool isFolder = entry.EntryType == TarEntryType.Directory;
                if (!isFolder && entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile))
                {
                    continue;
                }

                (string name, string target, _) = plan[at++];
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
                        {
                            entry.DataStream?.CopyTo(into);
                        }

                        File.SetLastWriteTimeUtc(target, entry.ModificationTime.UtcDateTime);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:untar:openError", $"Could not write \"{shown}\": {ex.Message}");
                }

                written.Add(shown);
            }
        }

        return [ArchiveNames(written)];
    }

    /// <summary>A tar archive's bytes: uncompressed through gzip when it starts with gzip's magic, whatever its name.</summary>
    private static Stream OpenTarBody(string path)
    {
        bool packed = IsGzipFile(path);
        FileStream file = File.OpenRead(path);
        return packed ? new GZipStream(file, CompressionMode.Decompress) : file;
    }

    /// <summary>Whether a 512-byte block is a tar header: its checksum field holds the sum of its bytes, the field itself read as spaces.</summary>
    private static bool IsTarHeader(byte[] block)
    {
        string field = Encoding.ASCII.GetString(block, 148, 8).Trim('\0', ' ');
        if (field.Length == 0 || field.Any(c => c is < '0' or > '7'))
        {
            return false;
        }

        long sum = 0;
        for (int i = 0; i < 512; i++)
        {
            sum += i is >= 148 and < 156 ? 32 : block[i];
        }

        return sum == Convert.ToInt64(field, 8);
    }
}
