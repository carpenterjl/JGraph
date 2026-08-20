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
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // The folder commands hand back a path or a status flag that a bare statement should not
        // echo: `cd subfolder` and `mkdir data` print nothing in MATLAB, and binding ans would make
        // them print their result instead.
        void Command(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        // A question that takes no arguments has to answer when its bare name is mentioned, or
        // disp(pwd) hands disp the function rather than the folder. Callee position is exempted by
        // the interpreter, so a form that does take arguments still reaches the function.
        void Query(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { AutoCallsBare = true }));

        RegisterDirectoryBuiltins(Command, Query, host);
        RegisterPathBuiltins(Define, Query, host);
        RegisterStreamBuiltins(Define, host);
        RegisterMachineBuiltins(Define, Query, host);
        RegisterJsonBuiltins(Define);
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

            try
            {
                host.ChangeDirectory(Str("cd", args, 0, line, col));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                throw new JgsRuntimeException(line, col, $"cd: {ex.Message}");
            }

            return JgsValue.Str(host.CurrentDirectory);
        });

        Define("mkdir", (args, line, col) =>
        {
            ArityRange("mkdir", args, 1, 2, line, col);

            // mkdir(parent, name) is the two-argument form; either way the whole path is created.
            string path = args.Count == 2
                ? Path.Combine(Str("mkdir", args, 0, line, col), Str("mkdir", args, 1, line, col))
                : Str("mkdir", args, 0, line, col);
            return Attempt("mkdir", line, col, () => Directory.CreateDirectory(host.ResolveForWrite(path)));
        });

        Define("rmdir", (args, line, col) =>
        {
            ArityRange("rmdir", args, 1, 2, line, col);
            string path = host.Resolve(Str("rmdir", args, 0, line, col));

            // MATLAB refuses to remove a non-empty folder without the 's' switch, and so does this:
            // deleting a tree because the caller mistyped a name is not a recoverable mistake.
            bool recursive = args.Count == 2 && Str("rmdir", args, 1, line, col) == "s";
            return Attempt("rmdir", line, col, () => Directory.Delete(path, recursive));
        });

        Define("copyfile", (args, line, col) =>
        {
            ArityRange("copyfile", args, 2, 3, line, col);
            string source = host.Resolve(Str("copyfile", args, 0, line, col));
            string destination = host.ResolveForWrite(Str("copyfile", args, 1, line, col));
            return Attempt("copyfile", line, col, () => File.Copy(source, DestinationFor(source, destination), overwrite: true));
        });

        Define("movefile", (args, line, col) =>
        {
            ArityRange("movefile", args, 2, 3, line, col);
            string source = host.Resolve(Str("movefile", args, 0, line, col));
            string destination = host.ResolveForWrite(Str("movefile", args, 1, line, col));
            return Attempt("movefile", line, col, () => File.Move(source, DestinationFor(source, destination), overwrite: true));
        });

        Define("delete", (args, line, col) =>
        {
            for (int i = 0; i < args.Count; i++)
            {
                // MATLAB spells two verbs with one name: deleting a file and deleting a figure
                // object. A handle is a number, a path is a string, so which one is meant is never
                // in doubt — and a number that names nothing still complains about not being a path.
                if (args[i].Type != JgsType.String && TryDeleteGraphics(args[i], host))
                {
                    continue;
                }

                File.Delete(host.Resolve(Str("delete", args, i, line, col)));
            }

            return JgsValue.Null;
        });

        Define("fileattrib", (args, line, col) =>
        {
            Arity("fileattrib", args, 1, line, col);
            string path = host.Resolve(Str("fileattrib", args, 0, line, col));
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return JgsValue.Bool(false);
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

    /// <summary>Copying onto a folder keeps the source's file name, as every shell does.</summary>
    private static string DestinationFor(string source, string destination) =>
        Directory.Exists(destination) ? Path.Combine(destination, Path.GetFileName(source)) : destination;

    // --- Path names -------------------------------------------------------------------------------

    private static void RegisterPathBuiltins(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define,
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Query, JGraphScriptGlobals host)
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

        Define("fullfile", (args, line, col) =>
        {
            var parts = new string[args.Count];
            for (int i = 0; i < args.Count; i++)
            {
                parts[i] = Str("fullfile", args, i, line, col);
            }

            return JgsValue.Str(parts.Length == 0 ? string.Empty : Path.Combine(parts));
        });

        DefineFileparts(Define);
    }

    /// <summary>Declares <c>fileparts</c>, which splits a path into folder, name, and extension.</summary>
    private static void DefineFileparts(Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define) =>
        Define("fileparts", (args, line, col) =>
        {
            Arity("fileparts", args, 1, line, col);
            string path = Str("fileparts", args, 0, line, col);

            // One output is the folder; the name and extension come back as a cell so a script can
            // take all three without multiple-output plumbing for a builtin that rarely needs it.
            return JgsValue.Cell([
                JgsValue.Str(Path.GetDirectoryName(path) ?? string.Empty),
                JgsValue.Str(Path.GetFileNameWithoutExtension(path)),
                JgsValue.Str(Path.GetExtension(path)),
            ]);
        });

    // --- Streams ----------------------------------------------------------------------------------

    private static void RegisterStreamBuiltins(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define, JGraphScriptGlobals host)
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

        Define("ferror", (args, line, col) =>
        {
            ArityRange("ferror", args, 1, 2, line, col);
            _ = Stream("ferror", args, line, col);

            // Every failure here is already an exception, so a stream that is still open has, by
            // construction, nothing to report.
            return JgsValue.Str(string.Empty);
        });

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

        Define("fgets", (args, line, col) =>
        {
            Arity("fgets", args, 1, line, col);

            // fgets keeps the line terminator where fgetl strips it; that is the only difference.
            string? read = ReadLineFrom(Stream("fgets", args, line, col), keepTerminator: true);
            return read is null ? JgsValue.Number(-1) : JgsValue.Str(read);
        });

        Define("fscanf", (args, line, col) =>
        {
            ArityRange("fscanf", args, 2, 3, line, col);
            FileStream stream = Stream("fscanf", args, line, col);
            string format = Str("fscanf", args, 1, line, col);
            int limit = args.Count == 3 ? Count("fscanf", args, 2, line, col) : int.MaxValue;

            var rest = new byte[stream.Length - stream.Position];
            int read = stream.Read(rest, 0, rest.Length);
            return Scan(Encoding.UTF8.GetString(rest, 0, read), format, limit, line, col);
        });

        Define("textscan", (args, line, col) =>
        {
            ArityRange("textscan", args, 2, 2, line, col);
            FileStream stream = Stream("textscan", args, line, col);
            string format = Str("textscan", args, 1, line, col);

            // textscan hands back one cell per conversion in the format; with JGraph's flat scan
            // that is one cell holding everything the format read.
            var rest = new byte[stream.Length - stream.Position];
            int read = stream.Read(rest, 0, rest.Length);
            return JgsValue.Cell([Scan(Encoding.UTF8.GetString(rest, 0, read), format, int.MaxValue, line, col)]);
        });
    }

    /// <summary>Reads one line of UTF-8 text from a stream, byte by byte so the position stays exact.</summary>
    private static string? ReadLineFrom(FileStream stream, bool keepTerminator)
    {
        if (stream.Position >= stream.Length)
        {
            return null;
        }

        var bytes = new List<byte>();
        int next;
        while ((next = stream.ReadByte()) >= 0)
        {
            bytes.Add((byte)next);
            if (next == '\n')
            {
                break;
            }
        }

        string text = Encoding.UTF8.GetString(bytes.ToArray());
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

        Define("type", (args, line, col) =>
        {
            Arity("type", args, 1, line, col);
            string path = host.Resolve(Str("type", args, 0, line, col));
            if (!File.Exists(path))
            {
                throw new JgsRuntimeException(line, col, $"type: there is no file '{path}'.");
            }

            host.print(File.ReadAllText(path));
            return JgsValue.Null;
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
                PumpEvents();
            }

            if (!limitRate || Environment.TickCount64 - _lastRenderFlush >= RenderFlushInterval)
            {
                _lastRenderFlush = Environment.TickCount64;
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
        Define("jsonencode", (args, line, col) =>
        {
            ArityRange("jsonencode", args, 1, 3, line, col);
            var json = new StringBuilder();
            WriteJson(json, args[0], line, col);
            return JgsValue.Str(json.ToString());
        });

        Define("jsondecode", (args, line, col) =>
        {
            Arity("jsondecode", args, 1, line, col);
            try
            {
                using JsonDocument document = JsonDocument.Parse(Str("jsondecode", args, 0, line, col));
                return ReadJson(document.RootElement);
            }
            catch (JsonException ex)
            {
                throw new JgsRuntimeException(line, col, $"jsondecode: {ex.Message}");
            }
        });
    }

    /// <summary>Writes one script value as JSON.</summary>
    private static void WriteJson(StringBuilder json, JgsValue value, int line, int col)
    {
        switch (value.Type)
        {
            case JgsType.Number:
                json.Append(double.IsFinite(value.AsNumber)
                    ? value.AsNumber.ToString("R", CultureInfo.InvariantCulture)
                    : "null"); // JSON has no way to write Inf or NaN, and null is what MATLAB writes
                return;

            case JgsType.Bool:
                json.Append(value.AsNumber != 0 ? "true" : "false");
                return;

            case JgsType.String:
                json.Append(JsonSerializer.Serialize(value.AsString));
                return;

            case JgsType.Array or JgsType.Cell:
                json.Append('[');
                JgsValue[] elements = value.Type == JgsType.Cell ? value.AsCell : value.BoxedElements();
                for (int i = 0; i < elements.Length; i++)
                {
                    if (i > 0)
                    {
                        json.Append(',');
                    }

                    WriteJson(json, elements[i], line, col);
                }

                json.Append(']');
                return;

            case JgsType.Struct:
                json.Append('{');
                bool first = true;
                foreach ((string field, JgsValue held) in value.AsStruct)
                {
                    if (!first)
                    {
                        json.Append(',');
                    }

                    first = false;
                    json.Append(JsonSerializer.Serialize(field)).Append(':');
                    WriteJson(json, held, line, col);
                }

                json.Append('}');
                return;

            default:
                throw new JgsRuntimeException(line, col, $"jsonencode cannot write a {value.TypeName}.");
        }
    }

    /// <summary>Reads one JSON element as a script value.</summary>
    private static JgsValue ReadJson(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return JgsValue.Number(element.GetDouble());

            case JsonValueKind.True or JsonValueKind.False:
                return JgsValue.Bool(element.GetBoolean());

            case JsonValueKind.String:
                return JgsValue.Str(element.GetString() ?? string.Empty);

            case JsonValueKind.Array:
                var items = new List<JgsValue>();
                bool allNumeric = true;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    items.Add(ReadJson(item));
                    allNumeric &= items[^1].Type is JgsType.Number or JgsType.Bool;
                }

                // An all-numeric array decodes to a numeric array, as MATLAB does; anything mixed
                // has to be a cell, because a JGS array holds one kind of thing.
                return allNumeric ? JgsValue.Array(items.ToArray()) : JgsValue.Cell(items.ToArray());

            case JsonValueKind.Object:
                var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    fields[property.Name] = ReadJson(property.Value);
                }

                return JgsValue.Struct(fields);

            default:
                return JgsValue.Array([]); // null decodes to an empty value, as MATLAB does
        }
    }
}
