using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The builtins that talk to the machine the script is running on: the working directory, file and
/// folder management, the rest of the <c>f*</c> stream family, environment variables, and JSON.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the file, folder, and environment builtins (M38).</summary>
    private static void RegisterEnvironmentBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // The folder commands hand back a path or a status flag that a bare statement should not
        // echo: `cd subfolder` and `mkdir data` print nothing in MATLAB, and binding ans would make
        // them print their result instead.
        void Command(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        // A question that takes no arguments has to answer when its bare name is mentioned, or
        // disp(pwd) hands disp the function rather than the folder. Callee position is exempted by
        // the interpreter, so a form that does take arguments still reaches the function.
        void Query(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { AutoCallsBare = true }));

        RegisterDirectoryBuiltins(Command, Query, host);
        RegisterFileVerbs(env, host);
        RegisterPathBuiltins(Define, Query, env, host);
        RegisterStreamBuiltins(Define, env, host);
        RegisterMachineBuiltins(Define, Query, host);
        RegisterJsonBuiltins((name, body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { KeepsStringArguments = true })));
    }

    // --- Directories ------------------------------------------------------------------------------

    private static void RegisterDirectoryBuiltins(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define,
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Query, JGraphScriptGlobals host)
    {
        Query("pwd", (args, line, col) =>
        {
            ArityRange("pwd", args, 0, 0, line, col);
            return JgsValue.Str(host.CurrentDirectory);
        });

        Define("cd", (args, line, col) =>
        {
            ArityRange("cd", args, 0, 1, line, col);
            if (args.Count == 0)
            {
                return JgsValue.Str(host.CurrentDirectory); // bare cd reports rather than moves
            }

            string left = host.CurrentDirectory;
            try
            {
                host.ChangeDirectory(Str("cd", args, 0, line, col));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                throw new JgsRuntimeException(line, col, $"cd: {ex.Message}");
            }

            // cd's output is the folder left, so `old = cd(d); cd(old)` puts it back (R2025b; open
            // item 244: it used to answer the folder entered, and an onCleanup restored nothing).
            return JgsValue.Str(left);
        });

        Define("rmdir", (args, line, col) =>
        {
            ArityRange("rmdir", args, 1, 2, line, col);
            string path = host.Resolve(Str("rmdir", args, 0, line, col));
            host.NoteFileChanging(path);

            // MATLAB refuses to remove a non-empty folder without the 's' switch, and so does this:
            // deleting a tree because the caller mistyped a name is not a recoverable mistake.
            bool recursive = args.Count == 2 && Str("rmdir", args, 1, line, col) == "s";
            return Attempt("rmdir", line, col, () => Directory.Delete(path, recursive));
        });

        Define("delete", (args, line, col) =>
        {
            if (TryDeviceBuiltin("delete", args, 0, line, col, out JgsValue gone))
            {
                return gone;
            }

            if (TryLibBuiltin("delete", host, args, line, col, out JgsValue deleted))
            {
                return deleted;
            }

            for (int i = 0; i < args.Count; i++)
            {
                // MATLAB spells two verbs with one name: deleting a file and deleting a figure
                // object. A handle is a number, a path is a string, so which one is meant is never
                // in doubt — and a number that names nothing still complains about not being a path.
                if (args[i].Type != JgsType.String && !args[i].IsObjectArray && TryDeleteGraphics(args[i], host))
                {
                    continue;
                }

                // A handle that named an object and names nothing now is deleted quietly, as R2025b
                // deletes a deleted handle; a number that never was one is its CannotDelete rather
                // than a file name (open item 83, measured).
                if (JgsRunningDialect.ThreadIsMatlab && IsHandleNumbers(args[i]))
                {
                    if (!TryDeleteLiveOrRetired(args[i], host))
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:hg:udd_interface:CannotDelete", "Invalid or deleted object.");
                    }

                    continue;
                }

                // Nothing to delete is nothing done: delete(findall(...)) of a search that found
                // nothing is the ordinary way to clear up (U4).
                if (args[i].Type == JgsType.Array && !args[i].IsStringArray && args[i].ArrayLength == 0)
                {
                    continue;
                }

                // A handle object with no delete method of its own comes here (one with a method
                // reached the method, which marks the object as it finishes — V6, #104).
                if (TryDeleteObject(args[i], line, col))
                {
                    continue;
                }

                // A .NET object: R2025b neither calls Dispose nor clears the variable (net_members,
                // ADR 0175), but the handle is ended and ObjectBeingDestroyed raised (ADR 0178).
                if (args[i].Type == JgsType.External && args[i].AsExternal is NetObject net)
                {
                    DeleteNetObject(net, args[i]);
                    continue;
                }

                // A timer is stopped if it was running, then ended for every alias (V6, #105).
                if (TryDeleteTimer(host, args[i], line, col))
                {
                    continue;
                }

                // A listener leaves its source and is ended for every alias (V6, #106).
                if (TryDeleteListener(args[i], line, col))
                {
                    continue;
                }

                if (JgsRunningDialect.ThreadIsMatlab)
                {
                    // R2025b's names: one text scalar each, a pattern among them (open item 300).
                    if (!IsTextScalar(args[i]))
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:string", "Name must be a text scalar.");
                    }

                    DeleteNamedFiles(host, TextOf(args[i]), line, col);
                    continue;
                }

                string path = host.Resolve(Str("delete", args, i, line, col));
                host.NoteFileChanging(path);
                File.Delete(path);
            }

            if (args.Count == 0 && JgsRunningDialect.ThreadIsMatlab)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
            }

            return JgsValue.Null;
        });

        Define("fileattrib", (args, line, col) =>
        {
            ArityRange("fileattrib", args, 1, 3, line, col);
            string path = host.Resolve(Str("fileattrib", args, 0, line, col));
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return JgsValue.Bool(false);
            }

            // fileattrib(name, '-w') and the like: +/- then the attribute letters, w (writable), h,
            // s and a on Windows; x and the users argument have nothing to change here. Open item 190's
            // fixture makes a read-only file this way for copyfile's 'f'.
            if (args.Count >= 2)
            {
                string mode = Str("fileattrib", args, 1, line, col);
                FileAttributes changed = File.GetAttributes(path);
                bool add = true;
                foreach (char c in mode)
                {
                    FileAttributes flag = c switch
                    {
                        '+' or '-' or ' ' or 'x' => 0,
                        'w' => FileAttributes.ReadOnly,
                        'h' => FileAttributes.Hidden,
                        's' => FileAttributes.System,
                        'a' => FileAttributes.Archive,
                        _ => throw new JgsRuntimeException(line, col, $"fileattrib: '{mode}' is not an attribute mode such as '+w' or '-h'."),
                    };
                    add = c switch { '+' => true, '-' => false, _ => add };

                    // Writable is the read-only flag's opposite.
                    bool set = flag == FileAttributes.ReadOnly ? !add : add;
                    changed = set ? changed | flag : changed & ~flag;
                }

                File.SetAttributes(path, changed);
                return JgsValue.Bool(true);
            }

            FileAttributes attributes = File.GetAttributes(path);
            return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["Name"] = JgsValue.Str(path),
                ["archive"] = JgsValue.Bool(attributes.HasFlag(FileAttributes.Archive)),
                ["system"] = JgsValue.Bool(attributes.HasFlag(FileAttributes.System)),
                ["hidden"] = JgsValue.Bool(attributes.HasFlag(FileAttributes.Hidden)),
                ["directory"] = JgsValue.Bool(attributes.HasFlag(FileAttributes.Directory)),
                ["UserRead"] = JgsValue.Bool(true),
                ["UserWrite"] = JgsValue.Bool(!attributes.HasFlag(FileAttributes.ReadOnly)),
            });
        });
    }

    /// <summary>
    /// Runs a file operation and reports success the way MATLAB does — a status flag rather than an
    /// error, so <c>[ok, msg] = mkdir(...)</c> style checks work without a try block.
    /// </summary>
    private static JgsValue Attempt(string name, int line, int col, Action operation)
    {
        try
        {
            operation();
            return JgsValue.Bool(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }
    }

    /// <summary>
    /// The MATLAB dialect's <c>delete(name)</c> of files, as R2025b deletes them (open item 300,
    /// ADR 0352, measured):
    /// <list type="bullet">
    /// <item>A name holding <c>*</c> or <c>?</c> anywhere is a pattern over its last part. Every
    /// file it matches in the current folder (or the folder it names) is deleted, read-only ones
    /// included. A folder it matches warns <c>MATLAB:DELETE:DirectoryDeletion</c> at its full path.
    /// A pattern that matches nothing, or whose folder part does not exist, is silent.</item>
    /// <item>A plain name that is a folder warns <c>DirectoryDeletion</c>, and one that is not
    /// there warns <c>MATLAB:DELETE:FileNotFound</c>, each with the name as given.</item>
    /// </list>
    /// The warnings go through <see cref="Warn(JGraphScriptGlobals, string, string)"/>, so <c>warning('error', id)</c> raises them.
    /// </summary>
    private static void DeleteNamedFiles(JGraphScriptGlobals host, string name, int line, int col)
    {
        if (name.IndexOfAny(['*', '?']) >= 0)
        {
            string part = Path.GetDirectoryName(name) ?? string.Empty;
            string folder = Path.IsPathRooted(part) ? part : Path.Combine(host.CurrentDirectory, part);
            string pattern = Path.GetFileName(name);
            if (part.IndexOfAny(['*', '?']) >= 0 || pattern.Length == 0 || !Directory.Exists(folder))
            {
                return;
            }

            foreach (string entry in Directory.EnumerateFileSystemEntries(folder, pattern, FileVerbMatching).Order(StringComparer.Ordinal))
            {
                if (Directory.Exists(entry))
                {
                    Warn(host, "MATLAB:DELETE:DirectoryDeletion",
                        $"'{Path.GetFullPath(entry)}' is a directory.  Use rmdir to delete directories.");
                    continue;
                }

                RemoveFile(host, entry, line, col);
            }

            return;
        }

        string path = name.Length == 0 ? string.Empty : Path.IsPathRooted(name) ? name : Path.Combine(host.CurrentDirectory, name);
        if (name.Length > 0 && !File.Exists(path) && !Directory.Exists(path)
            && host.Resolve(name) is var resolved && (File.Exists(resolved) || Directory.Exists(resolved)))
        {
            path = resolved;
        }

        if (name.Length > 0 && Directory.Exists(path))
        {
            Warn(host, "MATLAB:DELETE:DirectoryDeletion", $"'{name}' is a directory.  Use rmdir to delete directories.");
        }
        else if (name.Length == 0 || !File.Exists(path))
        {
            Warn(host, "MATLAB:DELETE:FileNotFound", $"File '{name}' not found.");
        }
        else
        {
            RemoveFile(host, path, line, col);
        }
    }

    /// <summary>Deletes one file, read-only or not, as R2025b's <c>delete</c> does (measured).</summary>
    private static void RemoveFile(JGraphScriptGlobals host, string path, int line, int col)
    {
        host.NoteFileChanging(path);
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }

            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new JgsRuntimeException(line, col, $"delete: {ex.Message}");
        }
    }

    // --- mkdir, copyfile and movefile (open items 190 and 204) -----------------------------------

    /// <summary>
    /// A file verb's failure in R2025b's words: a call with no outputs raises it, and a call with any
    /// answers status 0 with this message and identifier.
    /// </summary>
    private sealed class FileVerbRefusal(string identifier, string message) : Exception(message)
    {
        public string Identifier { get; } = identifier;
    }

    /// <summary>
    /// Declares <c>mkdir</c>, <c>copyfile</c> and <c>movefile</c> as R2025b answers them (measured,
    /// open items 190 and 204): <c>[status, msg, msgID] = verb(...)</c>. With no outputs a failure is
    /// an error; with any, it is status false and the error's words. A success with something to say
    /// (<c>mkdir</c> of a folder already there, or of no name) answers it as the message and
    /// identifier when two or more outputs are asked for, and warns it otherwise, one output included.
    /// </summary>
    private static void RegisterFileVerbs(JgsEnvironment env, JGraphScriptGlobals host)
    {
        void Verb(string name, Func<IReadOnlyList<JgsValue>, int, int, (string Identifier, string Message)> body)
        {
            JgsValue[] Run(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
            {
                if (wanted > 3)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:maxlhs", "Too many output arguments.");
                }

                bool ok = true;
                string identifier, message;
                try
                {
                    (identifier, message) = body(args, line, col);
                }
                catch (FileVerbRefusal refused)
                {
                    (ok, identifier, message) = (false, refused.Identifier, refused.Message);
                }

                if (!ok && wanted == 0)
                {
                    throw new JgsRuntimeException(line, col, identifier, message);
                }

                if (ok && message.Length > 0 && wanted < 2)
                {
                    Warn(host, identifier, message);
                }

                JgsValue[] answers = [JgsValue.Bool(ok), JgsValue.Str(message), JgsValue.Str(identifier)];
                return answers[..Math.Max(1, wanted)];
            }

            // A statement call asks for nothing, which is what makes a failure an error: `mkdir x`
            // and `copyfile(a, b)` on their own lines raise, and neither binds ans.
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) => Run(args, 1, line, col)[0])
            {
                BindsAnsAsStatement = false,
                KnowsWhenDiscarded = true,
                MultiOutput = Run,
            }));
        }

        Verb("mkdir", (args, line, col) =>
        {
            ArityRange("mkdir", args, 1, 2, line, col);

            // mkdir(parent, name) is the two-argument form; either way every missing folder is made.
            string folder = FileVerbText(args, 0, line, col);
            return MakeFolder(host, args.Count == 2 ? MatlabFullFile(folder, FileVerbText(args, 1, line, col)) : folder);
        });

        Verb("copyfile", (args, line, col) => TransferFiles(host, "COPYFILE", move: false, args, line, col));
        Verb("movefile", (args, line, col) => TransferFiles(host, "MOVEFILE", move: true, args, line, col));
    }

    /// <summary>A file verb's text argument: anything else is refused before the verb runs, outputs or not.</summary>
    private static string FileVerbText(IReadOnlyList<JgsValue> args, int index, int line, int col) =>
        IsTextScalar(args[index])
            ? TextOf(args[index])
            : throw new JgsRuntimeException(line, col, "MATLAB:string", "Argument must be a text scalar.");

    /// <summary>
    /// <c>mkdir</c>'s outcome: a folder already there is a success that says so, no name is a success
    /// that makes nothing, a file in the way is <c>ExistsAsFile</c>, and anything the system refuses
    /// is <c>OSError</c> with the system's own message, line end included, as R2025b reports it.
    /// </summary>
    private static (string, string) MakeFolder(JGraphScriptGlobals host, string given)
    {
        if (given.Length == 0)
        {
            return ("MATLAB:MKDIR:EmptyDirectoryName",
                "An empty directory name was given. No directory will be created.\nThis syntax may not be supported in future releases.");
        }

        string full = Path.GetFullPath(host.ResolveForWrite(given));
        if (Directory.Exists(full))
        {
            return ("MATLAB:MKDIR:DirectoryExists", "Directory already exists.");
        }

        if (File.Exists(full))
        {
            throw new FileVerbRefusal("MATLAB:MKDIR:ExistsAsFile", $"Unable to create directory. {given} already exists as a file.");
        }

        // A file where a folder is wanted further up is a path Windows cannot find (R2025b's code 3).
        for (string? up = Path.GetDirectoryName(full); up is not null; up = Path.GetDirectoryName(up))
        {
            if (File.Exists(up))
            {
                throw new FileVerbRefusal("MATLAB:MKDIR:OSError", SystemErrorText(3, terminated: true));
            }

            if (Directory.Exists(up))
            {
                break;
            }
        }

        try
        {
            Directory.CreateDirectory(full);
            return ("", "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new FileVerbRefusal("MATLAB:MKDIR:OSError", SystemErrorText(ex, terminated: true));
        }
    }

    /// <summary>
    /// The system's message for a Windows error code: R2025b's <c>mkdir</c> reports it with its line end
    /// (<paramref name="terminated"/>), <c>copyfile</c> and <c>movefile</c> without.
    /// </summary>
    private static string SystemErrorText(int code, bool terminated)
    {
        string text = new System.ComponentModel.Win32Exception(code).Message;
        return terminated && OperatingSystem.IsWindows() ? text + "\r\n" : text;
    }

    private static string SystemErrorText(Exception ex, bool terminated) => ex switch
    {
        UnauthorizedAccessException => SystemErrorText(5, terminated),
        IOException io when OperatingSystem.IsWindows() && (uint)io.HResult >> 16 == 0x8007 => SystemErrorText(io.HResult & 0xFFFF, terminated),
        ArgumentException or NotSupportedException when OperatingSystem.IsWindows() => SystemErrorText(123, terminated),
        _ => ex.Message,
    };

    /// <summary>
    /// <c>copyfile(source, destination, 'f')</c> and <c>movefile</c>: the destination is the current
    /// folder when left out; every argument from the third is a mode, and <c>'f'</c> (either case)
    /// the only one, which lets a read-only destination file be replaced.
    /// </summary>
    private static (string, string) TransferFiles(JGraphScriptGlobals host, string verb, bool move, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange(move ? "movefile" : "copyfile", args, 1, 4, line, col);
        string[] texts = [.. Enumerable.Range(0, args.Count).Select(i => FileVerbText(args, i, line, col))];
        bool force = false;
        for (int i = 2; i < texts.Length; i++)
        {
            if (!texts[i].Equals("f", StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col, $"MATLAB:{verb}:InvalidOption", $"'{texts[i]}' is an invalid option.");
            }

            force = true;
        }

        if (texts[0].Length == 0 || texts.Length > 1 && texts[1].Length == 0)
        {
            throw new FileVerbRefusal($"MATLAB:{verb}:IllegalName", "All arguments must be non-empty character vectors or string scalars.");
        }

        string source = FullSourcePath(host, texts[0]);
        string given = texts.Length > 1 ? texts[1] : host.CurrentDirectory;
        string destination = Path.GetFullPath(host.ResolveForWrite(given));
        if (move)
        {
            host.NoteFileChanging(source); // the destination reports itself through ResolveForWrite
        }

        try
        {
            TransferEntries(verb, move, force, source, destination, given.EndsWith('/') || given.EndsWith('\\'));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new FileVerbRefusal($"MATLAB:{verb}:OSError", SystemErrorText(ex, terminated: false));
        }

        return ("", "");
    }

    /// <summary>
    /// A source's full name: the current folder's entry, else what the read resolver finds (a script's
    /// own folder, a deployed program's frozen path), else the current folder's name for the refusal.
    /// </summary>
    private static string FullSourcePath(JGraphScriptGlobals host, string given)
    {
        string here = Path.GetFullPath(Path.Combine(host.CurrentDirectory, given));
        if (!File.Exists(here) && !Directory.Exists(here) && given.IndexOfAny(['*', '?']) < 0
            && host.Resolve(given) is var resolved && (File.Exists(resolved) || Directory.Exists(resolved)))
        {
            here = Path.GetFullPath(resolved);
        }

        return Path.TrimEndingDirectorySeparator(here);
    }

    private static readonly EnumerationOptions FileVerbMatching = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        MatchType = MatchType.Simple,
        MatchCasing = OperatingSystem.IsWindows() ? MatchCasing.CaseInsensitive : MatchCasing.CaseSensitive,
    };

    /// <summary>
    /// R2025b's three sources: a file (into a folder under its own name, else to the name given, whose
    /// folder must exist); a folder (copied: its contents merged into the destination, which is made;
    /// moved: renamed to a destination that is not there, else into it under its own name, merging with
    /// a folder of that name); a name with <c>*</c> or <c>?</c> in its last part (every match into the
    /// destination folder, which is made). A copy or move never takes its own destination, or a folder
    /// holding it, along (R2025b skips both).
    /// </summary>
    private static void TransferEntries(string verb, bool move, bool force, string source, string destination, bool namesFolder)
    {
        string leaf = Path.GetFileName(source);
        if (leaf.IndexOfAny(['*', '?']) >= 0)
        {
            string folder = Path.GetDirectoryName(source) ?? "";
            List<string> matches = folder.IndexOfAny(['*', '?']) < 0 && Directory.Exists(folder)
                ? [.. Directory.EnumerateFileSystemEntries(folder, leaf, FileVerbMatching)]
                : [];
            matches.Sort(StringComparer.Ordinal);
            if (matches.Count == 0)
            {
                throw FileVerbNotFound(verb, source);
            }

            if (File.Exists(destination))
            {
                throw new FileVerbRefusal($"MATLAB:{verb}:resourceNotFound",
                    $"The location '{Path.Combine(destination, Path.GetFileName(matches[0]))}' was not found.");
            }

            Directory.CreateDirectory(destination);
            foreach (string match in matches)
            {
                if (IsSameOrInside(destination, match))
                {
                    continue;
                }

                string target = Path.Combine(destination, Path.GetFileName(match));
                if (!Directory.Exists(match))
                {
                    TransferFile(verb, move, force, match, target);
                }
                else if (move)
                {
                    MoveFolder(verb, force, match, target);
                }
                else
                {
                    CopyFolderInto(verb, force, match, target, destination);
                }
            }

            return;
        }

        if (Directory.Exists(source))
        {
            if (SameFilePath(source, destination))
            {
                throw FileVerbSame(verb);
            }

            if (File.Exists(destination))
            {
                throw new FileVerbRefusal($"MATLAB:{verb}:OSError",
                    $"Destination {destination} is not a folder.\nWhen moving a source folder, the destination must also be a folder.");
            }

            if (!move)
            {
                CopyFolderInto(verb, force, source, destination, destination);
                return;
            }

            string target = Directory.Exists(destination) ? Path.Combine(destination, leaf) : destination;
            if (SameFilePath(source, target))
            {
                throw FileVerbSame(verb);
            }

            MoveFolder(verb, force, source, target);
            return;
        }

        if (!File.Exists(source))
        {
            throw FileVerbNotFound(verb, source);
        }

        // A destination written as a folder that is not there is a name Windows refuses (measured).
        if (namesFolder && !Directory.Exists(destination))
        {
            throw new FileVerbRefusal($"MATLAB:{verb}:OSError", "The filename, directory name, or volume label syntax is incorrect.");
        }

        TransferFile(verb, move, force, source, Directory.Exists(destination) ? Path.Combine(destination, leaf) : destination);
    }

    private static FileVerbRefusal FileVerbNotFound(string verb, string source) =>
        new($"MATLAB:{verb}:FileNotFound", $"No matching files named '{source}' were found.");

    private static FileVerbRefusal FileVerbSame(string verb) =>
        new($"MATLAB:{verb}:SourceAndDestinationSame", "Cannot copy or move a file or directory onto itself.");

    /// <summary>One file to its target: the target's folder must exist, and a read-only target is replaced only under 'f'.</summary>
    private static void TransferFile(string verb, bool move, bool force, string source, string target)
    {
        if (SameFilePath(source, target))
        {
            throw FileVerbSame(verb);
        }

        if (Path.GetDirectoryName(target) is not { } parent || !Directory.Exists(parent))
        {
            throw new FileVerbRefusal($"MATLAB:{verb}:resourceNotFound", $"The location '{target}' was not found.");
        }

        if (File.Exists(target) && File.GetAttributes(target) is var attributes && attributes.HasFlag(FileAttributes.ReadOnly))
        {
            if (!force)
            {
                throw new FileVerbRefusal($"MATLAB:{verb}:ReadOnly", $"Cannot write to destination: {target}.  Use the 'f' option to override.");
            }

            File.SetAttributes(target, attributes & ~FileAttributes.ReadOnly);
        }

        if (move)
        {
            File.Move(source, target, overwrite: true);
        }
        else
        {
            File.Copy(source, target, overwrite: true);
        }
    }

    /// <summary>A folder's contents merged into <paramref name="destination"/> (made), leaving out <paramref name="keepOut"/> and any folder holding it.</summary>
    private static void CopyFolderInto(string verb, bool force, string source, string destination, string keepOut)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
        {
            TransferFile(verb, move: false, force, file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (string folder in Directory.GetDirectories(source))
        {
            if (!IsSameOrInside(keepOut, folder))
            {
                CopyFolderInto(verb, force, folder, Path.Combine(destination, Path.GetFileName(folder)), keepOut);
            }
        }
    }

    /// <summary>A folder moved to <paramref name="target"/>: renamed when nothing is there (its parents made), merged into a folder that is.</summary>
    private static void MoveFolder(string verb, bool force, string source, string target)
    {
        if (!Directory.Exists(target))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(source, target);
                return;
            }

            CopyFolderInto(verb, force, source, target, target);
            Directory.Delete(source, recursive: true);
            return;
        }

        foreach (string file in Directory.GetFiles(source))
        {
            TransferFile(verb, move: true, force, file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (string folder in Directory.GetDirectories(source))
        {
            MoveFolder(verb, force, folder, Path.Combine(target, Path.GetFileName(folder)));
        }

        Directory.Delete(source);
    }

    private static bool SameFilePath(string first, string second) =>
        string.Equals(Path.TrimEndingDirectorySeparator(first), Path.TrimEndingDirectorySeparator(second),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> or lies inside it.</summary>
    private static bool IsSameOrInside(string path, string folder)
    {
        string inner = Path.TrimEndingDirectorySeparator(path);
        string outer = Path.TrimEndingDirectorySeparator(folder);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return inner.Equals(outer, comparison) || inner.StartsWith(outer + Path.DirectorySeparatorChar, comparison);
    }

    // --- Path names -------------------------------------------------------------------------------

    private static void RegisterPathBuiltins(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define,
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Query, JgsEnvironment env, JGraphScriptGlobals host)
    {
        Query("filesep", (args, line, col) =>
        {
            ArityRange("filesep", args, 0, 0, line, col);
            return JgsValue.Str(Path.DirectorySeparatorChar.ToString());
        });

        // The system's scratch folder, and a name in it nothing else is using. A script that wants to
        // write something it does not intend to keep has nowhere else to put it that is not the user's
        // own working folder.
        Query("tempdir", (args, line, col) =>
        {
            ArityRange("tempdir", args, 0, 0, line, col);
            return JgsValue.Str(Path.GetTempPath());
        });

        Query("tempname", (args, line, col) =>
        {
            ArityRange("tempname", args, 0, 0, line, col);
            return JgsValue.Str(Path.Combine(Path.GetTempPath(), "jg_" + Guid.NewGuid().ToString("N")));
        });

        Query("filemarker", (args, line, col) =>
        {
            ArityRange("filemarker", args, 0, 0, line, col);
            return JgsValue.Str(">"); // what separates a file from a local function inside it
        });

        Define("isfile", (args, line, col) =>
        {
            Arity("isfile", args, 1, line, col);
            return JgsValue.Bool(File.Exists(host.Resolve(Str("isfile", args, 0, line, col))));
        });

        Define("isfolder", (args, line, col) =>
        {
            Arity("isfolder", args, 1, line, col);
            return JgsValue.Bool(Directory.Exists(host.Resolve(Str("isfolder", args, 0, line, col))));
        });

        // fullfile is text, not Path.Combine (R2025b): the parts are joined with the platform's
        // separator, every slash becomes that separator on Windows, runs of it collapse to one except
        // a leading pair (a UNC share), empty parts are dropped, a trailing separator stays, and a
        // string among the parts makes the answer a string. A cellstr or string array among the
        // parts joins element by element: every such container must have the same shape or be a
        // scalar (which repeats), the answer has that shape, and it is a string array when any part
        // is a string and a cell otherwise. No arguments at all is MATLAB's narginchk error.
        env.Builtins.Register("fullfile", JgsValue.Function(new BuiltinFunction("fullfile", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
            }

            bool asString = false;
            bool anyContainer = false; // a cell or string array among the parts, scalar or not
            bool container = false; // a non-scalar one has fixed the answer's shape
            int rows = 1, cols = 1;
            var texts = new string[args.Count][];
            for (int i = 0; i < args.Count; i++)
            {
                JgsValue arg = args[i];
                asString |= arg.IsStringArray;
                if (arg.Type == JgsType.String)
                {
                    texts[i] = [arg.AsString];
                    continue;
                }

                if (arg.Type != JgsType.Cell && !arg.IsStringArray)
                {
                    throw new JgsRuntimeException(line, col,
                        $"fullfile expects argument {i + 1} to be a string, but got a {arg.TypeName}.");
                }

                anyContainer = true;
                JgsValue[] elements = arg.Type == JgsType.Cell ? arg.AsCell : arg.BoxedElements();
                texts[i] = new string[elements.Length];
                for (int k = 0; k < elements.Length; k++)
                {
                    if (!IsTextScalar(elements[k]))
                    {
                        throw new JgsRuntimeException(line, col,
                            $"fullfile expects every element of argument {i + 1} to be a string, but found a {elements[k].TypeName}.");
                    }

                    texts[i][k] = TextOf(elements[k]);
                }

                if (arg.Type == JgsType.Cell && elements.Length == 1)
                {
                    continue; // a one-cell repeats like a char row does (measured: fullfile({'a','b'}, {'x'}))
                }

                if (arg.IsStringArray && elements.Length == 1 && arg.Rows == 1 && arg.Cols == 1)
                {
                    continue;
                }

                if (container && (arg.Rows != rows || arg.Cols != cols))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:fullfile:CellstrSizeMismatch",
                        "All string and cell array inputs must be the same size or scalars.");
                }

                container = true;
                rows = arg.Rows;
                cols = arg.Cols;
            }

            if (!anyContainer)
            {
                string joined = JoinPath([.. texts.Select(static t => t[0]).Where(static p => p.Length > 0)]);
                return asString ? JgsValue.StringScalar(joined) : JgsValue.Str(joined);
            }

            int count = rows * cols;
            var answers = new JgsValue[count];
            for (int k = 0; k < count; k++)
            {
                var parts = new List<string>(args.Count);
                foreach (string[] t in texts)
                {
                    string part = t[t.Length == 1 ? 0 : k];
                    if (part.Length > 0)
                    {
                        parts.Add(part);
                    }
                }

                answers[k] = JgsValue.Str(JoinPath(parts));
            }

            if (asString)
            {
                return JgsValue.StringArray(answers, rows, cols);
            }

            JgsValue cell = JgsValue.Cell(answers);
            cell.Reshape(rows, cols);
            return cell;
        })
        { KeepsStringArguments = true }));

        DefineFileparts(env);
    }

    /// <summary>
    /// Declares <c>fileparts</c>: <c>[folder, name, ext] = fileparts(path)</c>, and one output is the
    /// folder alone, as in MATLAB. The split is textual and never looks at the disk (measured in
    /// R2025b): the folder is everything before the last separator, either slash counting on every
    /// platform, and keeps that separator only when it is a root (<c>C:\</c>, <c>/</c>); the extension
    /// starts at the last dot of what is left, so <c>.gitignore</c> is all extension and
    /// <c>c.tar.gz</c> is <c>c.tar</c> plus <c>.gz</c>; a trailing separator leaves the name and
    /// extension empty. A string scalar answers strings, a cellstr answers cells of the same length.
    /// </summary>
    private static void DefineFileparts(JgsEnvironment env)
    {
        static JgsValue[] Split(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
        {
            Arity("fileparts", args, 1, line, col);
            JgsValue input = args[0];
            if (input.Type == JgsType.String)
            {
                return SplitPath(input.AsString).Select(JgsValue.Str).ToArray();
            }

            if (IsStringScalar(input))
            {
                return SplitPath(TextOf(input)).Select(JgsValue.StringScalar).ToArray();
            }

            if (input.Type == JgsType.Cell && input.AsCell.All(static e => e.Type == JgsType.String))
            {
                string[][] parts = input.AsCell.Select(e => SplitPath(e.AsString)).ToArray();
                return Enumerable.Range(0, 3)
                    .Select(k => JgsValue.Cell(parts.Select(p => JgsValue.Str(p[k])).ToArray()))
                    .ToArray();
            }

            throw new JgsRuntimeException(line, col,
                "fileparts: Input must be a row vector of characters, or a string scalar, or a cellstr, or a string matrix.");
        }

        env.Builtins.Register("fileparts", JgsValue.Function(new BuiltinFunction("fileparts",
            (args, line, col) => Split(args, 1, line, col)[0])
        { MultiOutput = Split, KeepsStringArguments = true }));
    }

    /// <summary>The non-empty <paramref name="parts"/> joined under MATLAB's <c>fullfile</c> rule.</summary>
    private static string JoinPath(IReadOnlyList<string> parts)
    {
        char sep = Path.DirectorySeparatorChar;
        string joined = string.Join(sep, parts);
        if (OperatingSystem.IsWindows())
        {
            joined = joined.Replace('/', sep);
        }

        var result = new StringBuilder(joined.Length);
        for (int i = 0; i < joined.Length; i++)
        {
            bool repeated = joined[i] == sep && i > 0 && joined[i - 1] == sep && i != 1;
            if (!repeated)
            {
                result.Append(joined[i]);
            }
        }

        return result.ToString();
    }

    /// <summary>The folder, name, and extension of <paramref name="path"/> under MATLAB's textual rule.</summary>
    private static string[] SplitPath(string path)
    {
        int separator = path.LastIndexOfAny(['/', '\\']);
        string folder = string.Empty;
        string rest = path;
        if (separator >= 0)
        {
            string before = path[..separator];
            bool driveRoot = before.Length == 2 && before[1] == ':' && char.IsLetter(before[0]);
            folder = before.Length == 0
                ? Path.DirectorySeparatorChar.ToString() // a bare root answers the platform's separator (R2025b: the folder of '/y.m' is a lone backslash on Windows)
                : driveRoot ? path[..(separator + 1)] : before;
            rest = path[(separator + 1)..];
        }

        int dot = rest.LastIndexOf('.');
        return dot < 0 ? [folder, rest, string.Empty] : [folder, rest[..dot], rest[dot..]];
    }

    // --- Streams ----------------------------------------------------------------------------------

    private static void RegisterStreamBuiltins(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define,
        JgsEnvironment env, JGraphScriptGlobals host)
    {
        FileStream Stream(string name, IReadOnlyList<JgsValue> args, int line, int col)
        {
            int id = Count(name, args, 0, line, col);
            return host.FileFor(id) ?? throw new JgsRuntimeException(line, col, $"{name}: {id} is not an open file.");
        }

        Define("feof", (args, line, col) =>
        {
            Arity("feof", args, 1, line, col);
            FileStream stream = Stream("feof", args, line, col);
            return JgsValue.Bool(stream.Position >= stream.Length);
        });

        env.Builtins.Register("ferror", JgsValue.Function(new BuiltinFunction("ferror",
            (args, line, col) => Failure(host, args, 1, line, col)[0])
        { MultiOutput = (args, wanted, line, col) => Failure(host, args, wanted, line, col) }));

        Define("ftell", (args, line, col) =>
        {
            Arity("ftell", args, 1, line, col);
            return JgsValue.Number(Stream("ftell", args, line, col).Position);
        });

        Define("fseek", (args, line, col) =>
        {
            ArityRange("fseek", args, 2, 3, line, col);
            FileStream stream = Stream("fseek", args, line, col);
            long offset = (long)Num("fseek", args, 1, line, col);
            SeekOrigin origin = SeekOrigin.Begin;
            if (args.Count == 3)
            {
                origin = args[2].Type == JgsType.String
                    ? Str("fseek", args, 2, line, col) switch
                    {
                        "bof" => SeekOrigin.Begin,
                        "cof" => SeekOrigin.Current,
                        "eof" => SeekOrigin.End,
                        var other => throw new JgsRuntimeException(line, col, $"fseek: '{other}' is not 'bof', 'cof', or 'eof'."),
                    }
                    : (SeekOrigin)(int)(Num("fseek", args, 2, line, col) + 1);
            }

            try
            {
                stream.Seek(offset, origin);
                return JgsValue.Number(0); // MATLAB reports 0 for success and -1 for failure
            }
            catch (IOException)
            {
                return JgsValue.Number(-1);
            }
        });

        // fgets moved to the file wave in M76, beside fgetl: the two differ only in whether the
        // terminator comes back, and both now report how long it was.
        env.Builtins.Register("fscanf", JgsValue.Function(new BuiltinFunction("fscanf",
            (args, line, col) => ScanFile(host, args, 1, line, col)[0])
        { MultiOutput = (args, wanted, line, col) => ScanFile(host, args, wanted, line, col) }));

        env.Builtins.Register("textscan", JgsValue.Function(new BuiltinFunction("textscan",
            (args, line, col) => ScanColumns(host, args, 1, line, col)[0])
        { MultiOutput = (args, wanted, line, col) => ScanColumns(host, args, wanted, line, col) }));
    }

    /// <summary>
    /// Reads one line from a stream, byte by byte so the position stays exact, in the encoding the
    /// file was opened with and stopping after <paramref name="limit"/> characters.
    /// </summary>
    private static string? ReadLineFrom(FileStream stream, bool keepTerminator,
        Encoding? encoding = null, int limit = int.MaxValue)
    {
        if (stream.Position >= stream.Length || limit <= 0)
        {
            return null;
        }

        Encoding reader = encoding ?? Encoding.UTF8;
        var bytes = new List<byte>();
        int next;
        while ((next = stream.ReadByte()) >= 0)
        {
            bytes.Add((byte)next);
            if (next == '\n')
            {
                break;
            }

            // A character limit is counted in characters, so the bytes so far have to be decoded to
            // know how many there are — which is the only honest way to count them in an encoding
            // where a character is not a byte.
            if (limit != int.MaxValue && reader.GetString([.. bytes]).Length >= limit)
            {
                break;
            }
        }

        string text = reader.GetString([.. bytes]);
        if (limit != int.MaxValue && text.Length > limit)
        {
            text = text[..limit];
        }

        return keepTerminator ? text : text.TrimEnd('\n').TrimEnd('\r');
    }

    // --- The machine ------------------------------------------------------------------------------

    private static void RegisterMachineBuiltins(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define,
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Query, JGraphScriptGlobals host)
    {
        Define("getenv", (args, line, col) =>
        {
            Arity("getenv", args, 1, line, col);
            return JgsValue.Str(Environment.GetEnvironmentVariable(Str("getenv", args, 0, line, col)) ?? string.Empty);
        });

        Define("setenv", (args, line, col) =>
        {
            ArityRange("setenv", args, 1, 2, line, col);
            Environment.SetEnvironmentVariable(
                Str("setenv", args, 0, line, col),
                args.Count == 2 ? Str("setenv", args, 1, line, col) : string.Empty);
            Native.EnvironmentBlock.NoteChange(); // the native host is sent it before its next call (ADR 0180)
            return JgsValue.Null;
        });

        // These three answer about the machine, not about MATLAB, so they are honest questions with
        // honest answers even though JGraph is not MATLAB.
        Query("ispc", (args, line, col) => { ArityRange("ispc", args, 0, 0, line, col); return JgsValue.Bool(OperatingSystem.IsWindows()); });
        Query("isunix", (args, line, col) => { ArityRange("isunix", args, 0, 0, line, col); return JgsValue.Bool(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()); });
        Query("ismac", (args, line, col) => { ArityRange("ismac", args, 0, 0, line, col); return JgsValue.Bool(OperatingSystem.IsMacOS()); });

        Query("namelengthmax", (args, line, col) =>
        {
            ArityRange("namelengthmax", args, 0, 0, line, col);
            return JgsValue.Number(63); // MATLAB's limit, which JGraph matches so scripts agree
        });

        Query("cputime", (args, line, col) =>
        {
            ArityRange("cputime", args, 0, 0, line, col);
            return JgsValue.Number(Environment.TickCount64 / 1000.0);
        });

        Define("drawnow", (args, line, col) =>
        {
            ArityRange("drawnow", args, 0, 2, line, col);

            // MATLAB grew this vocabulary in two generations; the old words are spellings of the
            // new ones. 'update' is 'limitrate nocallbacks', 'expose' is 'nocallbacks'.
            bool limitRate = false;
            bool noCallbacks = false;
            for (int i = 0; i < args.Count; i++)
            {
                switch (Str("drawnow", args, i, line, col).ToLowerInvariant())
                {
                    case "limitrate": limitRate = true; break;
                    case "nocallbacks": noCallbacks = true; break;
                    case "update": limitRate = true; noCallbacks = true; break;
                    case "expose": noCallbacks = true; break;
                    default:
                        throw new JgsRuntimeException(line, col,
                            "drawnow takes 'limitrate', 'nocallbacks', 'update' or 'expose'.");
                }
            }

            // A figure touched mid-statement appears now rather than at the statement's end — this
            // is what makes an animation loop animate. Then queued callbacks get their turn (this
            // is one of MATLAB's interruption points), and finally the host's display catches up.
            host.ShowTouchedFigures();
            if (!noCallbacks)
            {
                PumpEvents(host);
            }

            if (!limitRate || Environment.TickCount64 - _lastRenderFlush >= RenderFlushInterval)
            {
                _lastRenderFlush = Environment.TickCount64;
                ScriptComponentFrames.Flush(force: true); // what the callbacks just changed, too (U1)
                ScriptRenderPump.Flush();
            }

            return JgsValue.Null;
        });
    }

    /// <summary>When <c>drawnow limitrate</c> last flushed, and how long it waits before flushing
    /// again — MATLAB caps the update rate at roughly 20 per second, and caps only the rendering:
    /// callbacks are still delivered on every call.</summary>
    private static long _lastRenderFlush;

    private const long RenderFlushInterval = 50;

    // --- JSON -------------------------------------------------------------------------------------

    private static void RegisterJsonBuiltins(Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define)
    {
        // R2025b's forms and refusals (probe u9b_json): the value, then name-value options whose
        // names match exactly; jsondecode takes the text alone.
        Define("jsonencode", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
            }

            JgsJson.Options options = JgsJson.Options.Default;
            for (int i = 1; i < args.Count; i += 2)
            {
                if (!IsTextScalar(args[i]))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:json:InvalidParamType",
                        "Name argument type must be string scalar or character vector.");
                }

                string name = TextOf(args[i]);
                if (name is not ("ConvertInfAndNaN" or "PrettyPrint"))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:json:UnmatchedParameter",
                        "Name argument must be 'ConvertInfAndNaN' or 'PrettyPrint'.");
                }

                if (i + 1 >= args.Count)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:json:ParamMissingValue", $"Name '{name}' requires a value.");
                }

                JgsValue setting = args[i + 1];
                if (setting.Type is not (JgsType.Bool or JgsType.Number) || IsTextScalar(setting))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:json:InvalidValueType",
                        $"Value type must be logical scalar for name '{name}'.");
                }

                options = name == "PrettyPrint"
                    ? options with { PrettyPrint = setting.AsNumber != 0 }
                    : options with { ConvertInfAndNaN = setting.AsNumber != 0 };
            }

            try
            {
                return JgsValue.Str(JgsJson.Encode(args[0], options));
            }
            catch (JgsJson.Unwritable refused)
            {
                throw new JgsRuntimeException(line, col, refused.Identifier, refused.Message);
            }
        });

        Define("jsondecode", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
            }

            if (args.Count > 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:maxrhs", "Too many input arguments.");
            }

            JgsValue text = args[0];
            bool missing = text.IsStringArray && text.ArrayLength == 1 && IsMissingText(text.ElementAt(0).AsString);
            if (!IsTextScalar(text) || missing)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:json:InvalidInput",
                    "JSON text must be a character vector or a scalar non-missing string.");
            }

            try
            {
                return JgsJson.Decode(TextOf(text));
            }
            catch (JgsJson.Malformed malformed)
            {
                throw new JgsRuntimeException(line, col, malformed.Identifier, malformed.Message);
            }
        });
    }
}
