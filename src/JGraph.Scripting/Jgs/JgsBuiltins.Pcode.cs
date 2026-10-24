namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>pcode</c> (the compiler and SDK plan's stage K3b, ADR 0259), transcribed from R2025b's
/// <c>toolbox/matlab/general/pcode.m</c>: its flags, how it turns each argument into the
/// <c>.m</c> files it names, its warnings, and where the P-file goes - the current folder, under
/// the source's <c>private</c>, <c>+package</c> and <c>@class</c> folders, or beside the source
/// with <c>-inplace</c>. What it writes is <see cref="JgsPcode"/>'s format, not MathWorks'.
/// </summary>
/// <remarks>
/// <c>-R2022a</c>, <c>-R2007b</c> and <c>-nodebug</c> are checked as R2025b checks them and
/// otherwise change nothing: JGraph has one P-file format. <c>-nocheck</c> is accepted and still
/// parses the file, since a P-file is the parsed tokens; R2025b refuses a file that does not
/// parse under <c>-nocheck</c> as well (<c>MATLAB:screen</c>), and writes nothing either way.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Declares <c>pcode</c>, which writes relative to the session's current folder.</summary>
    internal static void RegisterPcodeBuiltins(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        env.Builtins.Register("pcode", JgsValue.Function(new BuiltinFunction("pcode",
            (args, line, col) => Pcode(interpreter, host, args, 0, line, col))
        {
            KeepsStringArguments = true,
            BindsAnsAsStatement = false,
            MultiOutput = (args, wanted, line, col) => [Pcode(interpreter, host, args, wanted, line, col)],
        }));
    }

    private static JgsValue Pcode(Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (wanted > 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyOutputs", "Too many output arguments.");
        }

        bool r2022a = false, r2007b = false, inplace = false, nodebug = false;
        var inputs = new List<string>();
        foreach (JgsValue arg in args)
        {
            // convertStringsToChars, then a nonempty char row or the refusal.
            string? text = IsTextScalar(arg) ? TextOf(arg) : null;
            if (string.IsNullOrEmpty(text))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:pcode:ArgMustBeString",
                    "Input argument must be a nonempty string or character vector.");
            }

            inputs.Add(text);
        }

        // The flags are read from the last argument back, so the last unknown one is the one named.
        for (int i = inputs.Count - 1; i >= 0; i--)
        {
            string arg = inputs[i];
            if (!arg.StartsWith('-'))
            {
                continue;
            }

            if (arg.Equals("-R2022a", StringComparison.OrdinalIgnoreCase)) r2022a = true;
            else if (arg.Equals("-R2007b", StringComparison.OrdinalIgnoreCase)) r2007b = true;
            else if (arg.Equals("-inplace", StringComparison.OrdinalIgnoreCase)) inplace = true;
            else if (arg.Equals("-nocheck", StringComparison.OrdinalIgnoreCase)) { }
            else if (arg.Equals("-nodebug", StringComparison.OrdinalIgnoreCase)) nodebug = true;
            else throw new JgsRuntimeException(line, col, "MATLAB:pcode:UnknownFlag", $"Unknown flag '{arg}'.");
            inputs.RemoveAt(i);
        }

        if (r2022a && r2007b)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:pcode:IncompatibleFlags",
                "The -R2007b option may not be used in combination with the -R2022a option.");
        }

        if (nodebug && !r2022a)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:pcode:R2022aRequired",
                "The -nodebug option must be used in combination with the -R2022a option.");
        }

        string pwd = host.CurrentDirectory;
        bool IsFolder(string p) => Directory.Exists(Path.Combine(pwd, p));
        bool IsFile(string p) => File.Exists(Path.Combine(pwd, p));

        // which(file) for a file name: a path that names a file is that file in full; a bare name
        // is looked for where a call would look.
        string Which(string p)
        {
            if (p.IndexOfAny(['*', '?']) >= 0)
            {
                return string.Empty;
            }

            if (Path.GetDirectoryName(p) is { Length: > 0 })
            {
                return IsFile(p) ? Path.GetFullPath(Path.Combine(pwd, p)) : string.Empty;
            }

            if (IsFile(p))
            {
                return Path.GetFullPath(Path.Combine(pwd, p));
            }

            return interpreter.FunctionPath?.FindFile(p) ?? string.Empty;
        }

        void Warn(string identifier, string text) => JgsBuiltins.Warn(host, identifier, text);

        foreach (string given in inputs)
        {
            string arg = given;
            if (arg.Contains("..", StringComparison.Ordinal) || arg.Contains('?', StringComparison.Ordinal))
            {
                Warn("MATLAB:pcode:BadPath", $"Unable to resolve argument '{given}'. Arguments containing '?' and '..' are not supported.");
                continue;
            }

            if (IsFolder(arg))
            {
                if (IsFile(arg + ".m") || IsFile(arg + ".M"))
                {
                    Warn("MATLAB:pcode:Ambiguous", $"'{given}' names both a MATLAB code file and directory.");
                }

                arg = FullFile(arg, "*.?");
            }

            (string pn, string fn, string en) = FileParts(arg);
            if (fn.Length > 0)
            {
                if (en.Length == 0 || en.Equals(".p", StringComparison.OrdinalIgnoreCase))
                {
                    string origName = Which(FullFile(pn, fn + ".m"));
                    arg = origName.Length > 0 ? origName : FullFile(pn, fn + ".?");
                }
                else if (Which(arg) is { Length: > 0 } fullArg)
                {
                    arg = fullArg;
                }
            }

            if (FileParts(arg).Folder.Length == 0)
            {
                arg = FullFile(pwd, arg);
            }

            (pn, fn, en) = FileParts(arg);
            if (!IsFolder(pn))
            {
                Warn("MATLAB:pcode:NotPath", $"Unable to resolve argument '{given}'. Directory does not exist.");
                continue;
            }

            if (Which(arg).Length == 0)
            {
                string ap = FullFile(pwd, pn);
                if (!Path.IsPathRooted(pn) && IsFolder(ap))
                {
                    arg = FullFile(ap, fn + en);
                }

                (pn, fn, en) = FileParts(arg);
                if (fn.Length == 0)
                {
                    Warn("MATLAB:pcode:NotFileName", $"Unable to generate P-code file for argument '{given}'. File must have a '.m' extension.");
                    continue;
                }
            }

            if (en.Equals(".p", StringComparison.OrdinalIgnoreCase) || en == ".*")
            {
                en = ".?";
            }
            else if (!en.Equals(".m", StringComparison.OrdinalIgnoreCase) && en != ".?")
            {
                Warn("MATLAB:pcode:FileUnsupported", $"Unable to generate P-code file for argument '{given}'. File must have a '.m' extension.");
                continue;
            }

            string folder = Path.GetFullPath(Path.Combine(pwd, pn));
            string pattern = en == ".?" ? fn + ".m" : fn + en;
            string[] files = Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, pattern)
                    .Select(Path.GetFileName)
                    .OfType<string>()
                    .Where(name => Path.GetExtension(name).Equals(".m", StringComparison.OrdinalIgnoreCase))
                    .Order(StringComparer.Ordinal)
                    .ToArray()
                : [];
            if (files.Length == 0)
            {
                Warn("MATLAB:pcode:FileNotFound", $"Unable to find file with '.m' extension for argument '{given}'. Check that a matching file exists and that you have permission to read it.");
                continue;
            }

            foreach (string file in files)
            {
                PcodeFile(host, folder, file, inplace, pwd);
            }
        }

        host.NoteFileChanging(null); // rehash
        return JgsValue.Null;
    }

    /// <summary>
    /// One file's P-code: written beside it with <c>-inplace</c>, otherwise into the current folder
    /// under the source's own <c>private</c>, <c>+package</c> and <c>@class</c> folders.
    /// </summary>
    private static void PcodeFile(JGraphScriptGlobals host, string folder, string file, bool inplace, string pwd)
    {
        string origName = Path.Combine(folder, file);
        string stem = Path.GetFileNameWithoutExtension(file);
        string target;
        if (inplace)
        {
            target = Path.Combine(folder, stem + JgsPcode.Extension);
        }
        else
        {
            string destination = SplitImplicitFolders(Path.GetFullPath(pwd)).Plain;
            string implicitPart = SplitImplicitFolders(folder).Implicit;
            if (implicitPart.Length > 0)
            {
                destination = Path.Combine(destination, implicitPart);
                Directory.CreateDirectory(destination);
            }

            target = Path.Combine(destination, stem + JgsPcode.Extension);
        }

        byte[] pcode;
        try
        {
            pcode = JgsPcode.Compile(File.ReadAllText(origName), origName);
        }
        catch (JgsSyntaxException error)
        {
            throw new JgsRuntimeException(0, 0, $"Error: File: {origName} Line: {error.Line} Column: {error.Column}\n{error.Message}");
        }

        File.WriteAllBytes(host.ResolveForWrite(target), pcode);
    }

    /// <summary>
    /// A folder split where its implicit folders start: the trailing <c>private</c>, <c>+package</c>
    /// and <c>@class</c> names, which are on the path through the folder above them.
    /// </summary>
    private static (string Plain, string Implicit) SplitImplicitFolders(string folder)
    {
        string trimmed = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var implicitNames = new Stack<string>();
        while (Path.GetFileName(trimmed) is { Length: > 0 } name
            && (name.Equals("private", StringComparison.Ordinal) || name[0] is '+' or '@'))
        {
            implicitNames.Push(name);
            trimmed = Path.GetDirectoryName(trimmed) ?? string.Empty;
        }

        return (trimmed, string.Join(Path.DirectorySeparatorChar, implicitNames));
    }

    /// <summary>MATLAB's <c>fileparts</c> for the three parts <c>pcode.m</c> reads.</summary>
    private static (string Folder, string Name, string Extension) FileParts(string path)
    {
        int slash = path.LastIndexOfAny(['/', '\\']);
        string folder = slash < 0 ? string.Empty : slash == 0 ? path[..1] : path[..slash];
        if (slash == 2 && path.Length > 1 && path[1] == ':')
        {
            folder = path[..3]; // C:\ keeps its separator
        }

        string rest = path[(slash + 1)..];
        int dot = rest.LastIndexOf('.');
        return dot < 0 ? (folder, rest, string.Empty) : (folder, rest[..dot], rest[dot..]);
    }

    /// <summary>MATLAB's <c>fullfile</c> of two parts.</summary>
    private static string FullFile(string folder, string name) =>
        folder.Length == 0 ? name
        : name.Length == 0 ? folder
        : folder.EndsWith('\\') || folder.EndsWith('/') ? folder + name
        : folder + Path.DirectorySeparatorChar + name;
}
