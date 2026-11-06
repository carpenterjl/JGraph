using System.IO;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// An <c>@Class</c> folder as a class is built from it (the packages and class folders plan, stage
/// P4, ADR 0310): the class file, the method files beside it - each a method named by its file,
/// declared in the classdef by a signature line or not declared at all - and the folder's
/// <c>private/</c> functions, which are the class's private methods. Read once when the class is
/// loaded, through the file index's entries; <see cref="Changed"/> says, once per statement,
/// whether a file was written, added or removed since, so the class is loaded again at its next
/// mention as a changed class file is (W11, ADR 0303), where R2025b needs <c>clear classes</c> for
/// a new method file (measured; a recorded divergence in JGraph's favour).
/// </summary>
internal sealed class JgsClassFolder
{
    private readonly Dictionary<string, DateTime> _written = new(JgsFunctionPath.PathComparer);
    private readonly DateTime _folderWritten;
    private readonly DateTime _privateWritten;
    private int _checkedEpoch = -1;
    private bool _changed;

    private JgsClassFolder(string name, string folder, string classFile, Dictionary<string, string> methodFiles, Dictionary<string, string> privateFiles)
    {
        Name = name;
        Folder = folder;
        ClassFile = classFile;
        MethodFiles = methodFiles;
        PrivateFiles = privateFiles;
        _folderWritten = WriteTime(folder);
        _privateWritten = WriteTime(Path.Combine(folder, "private"));
        foreach (string file in methodFiles.Values.Concat(privateFiles.Values))
        {
            _written[file] = WriteTime(file);
        }
    }

    /// <summary>The class's name: the folder's without its <c>@</c>.</summary>
    public string Name { get; }

    /// <summary>The <c>@Class</c> folder's full path.</summary>
    public string Folder { get; }

    /// <summary>The class file inside it (<c>@Thing/Thing.m</c>).</summary>
    public string ClassFile { get; }

    /// <summary>The method files beside the class file, by stem, the class file left out.</summary>
    public IReadOnlyDictionary<string, string> MethodFiles { get; }

    /// <summary>The functions of the folder's <c>private/</c>, by stem.</summary>
    public IReadOnlyDictionary<string, string> PrivateFiles { get; }

    /// <summary>
    /// Reads the folder <paramref name="classFile"/> sits in through <paramref name="index"/>'s
    /// entries: every code file beside the class file is a method file, every one under
    /// <c>private/</c> a private method. A P-file is taken before the .m beside it, as the path
    /// takes it.
    /// </summary>
    public static JgsClassFolder Read(string classFile, string name, JgsFileIndex index)
    {
        string folder = Path.GetDirectoryName(classFile) ?? string.Empty;
        var methodFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        JgsFileIndex.Folder entry = index.Track(folder);
        foreach (string stem in index.StemsOf(entry).Order(StringComparer.Ordinal))
        {
            if (!string.Equals(stem, name, StringComparison.Ordinal))
            {
                methodFiles[stem] = CodeFile(folder, stem, entry);
            }
        }

        var privateFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        string privateFolder = Path.Combine(folder, "private");
        JgsFileIndex.Folder privateEntry = index.Track(privateFolder);
        foreach (string stem in index.StemsOf(privateEntry).Order(StringComparer.Ordinal))
        {
            privateFiles[stem] = CodeFile(privateFolder, stem, privateEntry);
        }

        return new JgsClassFolder(name, folder, classFile, methodFiles, privateFiles);
    }

    /// <summary>The code file <paramref name="stem"/> names in <paramref name="folder"/>: the P-file where the index saw one, else the .m.</summary>
    private static string CodeFile(string folder, string stem, JgsFileIndex.Folder entry) =>
        Path.Combine(folder, stem + (entry.PcodeStems.Contains(stem) ? JgsPcode.Extension : ".m"));

    /// <summary>
    /// Whether the folder has changed since it was read - a method or private file written, added
    /// or removed - checked once per <paramref name="statementEpoch"/>, so a loop over the class
    /// pays the file times once per statement.
    /// </summary>
    public bool Changed(int statementEpoch)
    {
        if (_checkedEpoch == statementEpoch)
        {
            return _changed;
        }

        _checkedEpoch = statementEpoch;
        _changed = WriteTime(Folder) != _folderWritten || WriteTime(Path.Combine(Folder, "private")) != _privateWritten;
        if (!_changed)
        {
            foreach ((string file, DateTime written) in _written)
            {
                if (WriteTime(file) != written)
                {
                    _changed = true;
                    break;
                }
            }
        }

        return _changed;
    }

    /// <summary>
    /// The method a file defines, as the class's method named <paramref name="stem"/> (R2025b:
    /// the file name is the method's name, measured - <c>@Thing/misnamed.m</c> holding
    /// <c>function r = othername(o)</c> answers to <c>misnamed</c>): the file's first function,
    /// its local functions hoisted into the file's own storage so the method sees them and nothing
    /// outside the file does. A file that does not parse is a method whose call raises the load
    /// error (R2025b's "Error: File: … Line: … Column: …" form), so one bad file does not take
    /// the class down with it.
    /// </summary>
    public (FnStmt Function, IJgsCallable? Refusal) LoadMethod(string file, string stem, Interpreter interpreter)
    {
        string source;
        IReadOnlyList<Stmt> program;
        try
        {
            if (JgsPcode.IsPcode(file))
            {
                program = JgsPcode.Load(file);
            }
            else
            {
                source = JgsMlapp.ReadSource(file);
                program = Parser.ParseMethodFile(source, file);
            }
        }
        catch (JgsSyntaxException error)
        {
            var load = new JgsRuntimeException(error.Line, error.Column,
                $"Error: File: {file} Line: {error.Line} Column: {error.Column}\n{error.Message}");
            load.AttributeTo(file);
            return (Stub(stem, file), new BuiltinFunction(stem, (_, _, _) => throw load));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            var load = new JgsRuntimeException(0, 0, $"'{stem}': cannot read '{file}': {ex.Message}");
            return (Stub(stem, file), new BuiltinFunction(stem, (_, _, _) => throw load));
        }

        if (program.Count == 0 || program[0] is not FnStmt main)
        {
            var notAFunction = new JgsRuntimeException(0, 0,
                $"'{Path.GetFileName(file)}': a method file of class '{Name}' holds a function, and this one does not.");
            return (Stub(stem, file), new BuiltinFunction(stem, (_, _, _) => throw notAFunction));
        }

        // The file's functions live with the file, as a function file's do: the method sees its
        // local functions through the file's storage, and nothing outside the file sees them.
        FunctionFile storage = interpreter.ReplaceFile(file);
        foreach (Stmt statement in program)
        {
            if (statement is not FnStmt function)
            {
                var notAFunction = new JgsRuntimeException(statement.Line, statement.Column,
                    $"'{Path.GetFileName(file)}': a method file of class '{Name}' holds functions and nothing else.");
                return (Stub(stem, file), new BuiltinFunction(stem, (_, _, _) => throw notAFunction));
            }

            interpreter.Hoist(function, file);
        }

        storage.MainName = main.Name;
        if (string.Equals(main.Name, stem, StringComparison.Ordinal))
        {
            return (main, null);
        }

        // The method answers to the file's name, whatever the function line says.
        return (new FnStmt(stem, main.Parameters, main.Body, main.Outputs)
        {
            Line = main.Line,
            Column = main.Column,
            SourceId = main.SourceId,
            Dialect = main.Dialect,
            Suppressed = main.Suppressed,
            SuperRefs = main.SuperRefs,
        }, null);
    }

    /// <summary>The signature R2025b shows for a method it has not read: <c>varargout name(varargin)</c>.</summary>
    private static FnStmt Stub(string stem, string file) =>
        new(stem, ["varargin"], [], ["varargout"]) { Line = 1, Column = 1, SourceId = file, Dialect = JgsDialect.Matlab };

    private static DateTime WriteTime(string path)
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path)
                : Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(path)
                : default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return default;
        }
    }
}
