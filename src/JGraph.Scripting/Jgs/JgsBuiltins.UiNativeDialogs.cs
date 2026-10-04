using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The verbs that ask the user through the system's own dialogs (app-building plan, U4):
/// <c>uigetfile</c>, <c>uiputfile</c>, <c>uigetdir</c>, <c>uisetcolor</c> and <c>uisetfont</c>, and
/// the three built on a file dialog — <c>uiopen</c>, <c>uisave</c> and <c>uiload</c>.
/// </summary>
/// <remarks>
/// The dialogs themselves are the host's (<see cref="IScriptNativeDialogs"/>, installed on
/// <see cref="ScriptGraphicsCallbacks.NativeDialogs"/>). Where there is none — a batch run, a
/// headless test — every one of them refuses with R2025b's words for a dialog that would block with
/// nobody to answer it, before it looks at its arguments, as R2025b does.
/// </remarks>
internal static partial class JgsBuiltins
{
    private static readonly ScriptFileFilter AllFiles = new("*.*", "All Files (*.*)");

    private static IScriptNativeDialogs NativeDialogs(int line, int col) =>
        ScriptGraphicsCallbacks.NativeDialogs
        ?? throw new JgsRuntimeException(line, col, NonInteractiveId, NonInteractiveText);

    private static void RegisterNativeDialogBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        void DefineSeveral(string name, int most, Func<IReadOnlyList<JgsValue>, int, int, JgsValue[]> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) => body(args, line, col)[0])
            {
                AutoCallsBare = true,
                KeepsStringArguments = true,
                MultiOutput = (args, wanted, line, col) => wanted > most
                    ? throw new JgsRuntimeException(line, col, "MATLAB:TooManyOutputs", "Too many output arguments.")
                    : body(args, line, col),
            }));

        void DefineQuiet(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body)
            {
                AutoCallsBare = true,
                BindsAnsAsStatement = false,
                KeepsStringArguments = true,
            }));

        DefineSeveral("uigetfile", 3, (args, line, col) => UiFileDialog(host, save: false, args, line, col));
        DefineSeveral("uiputfile", 3, (args, line, col) => UiFileDialog(host, save: true, args, line, col));
        DefineSeveral("uigetdir", 1, (args, line, col) => [UiGetDir(host, args, line, col)]);
        DefineSeveral("uisetcolor", 1, (args, line, col) => [UiSetColor(args, line, col)]);
        DefineSeveral("uisetfont", 1, (args, line, col) => [UiSetFont(args, line, col)]);
        DefineQuiet("uiload", (args, line, col) =>
        {
            if (args.Count > 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
            }

            if (NativeDialogs(line, col).OpenFiles("Select File to Open", [new("*.mat", "MAT-files (*.mat)"), AllFiles], host.CurrentDirectory, false)
                is { } chosen)
            {
                RunInCallersWorkspace(env, "uiload", $"load('{Quoted(chosen.Paths[0])}')", line, col);
            }

            return JgsValue.Null;
        });
        DefineQuiet("uisave", (args, line, col) =>
        {
            IScriptNativeDialogs dialogs = NativeDialogs(line, col);
            List<string> names = args.Count > 0 ? DialogLines(AsChars(args[0]), line, col) : [];
            string start = args.Count > 1 && IsTextScalar(args[1]) ? TextOf(args[1]) : "matlab.mat";
            if (dialogs.SaveFile("Save Workspace Variables", [new("*.mat", "MAT-files (*.mat)"), AllFiles], host.Resolve(start)) is { } chosen)
            {
                string named = string.Concat(names.Select(name => $", '{Quoted(name)}'"));
                RunInCallersWorkspace(env, "uisave", $"save('{Quoted(chosen.Path)}'{named})", line, col);
            }

            return JgsValue.Null;
        });
        DefineQuiet("uiopen", (args, line, col) =>
        {
            IScriptNativeDialogs dialogs = NativeDialogs(line, col);
            string kind = args.Count > 0 && IsTextScalar(args[0]) ? TextOf(args[0]) : string.Empty;
            IReadOnlyList<ScriptFileFilter> filters = kind.ToLowerInvariant() switch
            {
                "load" => [new("*.mat", "MAT-files (*.mat)"), AllFiles],
                "figure" => [new("*.graph;*.fig", "Figures (*.graph, *.fig)"), AllFiles],
                "" or "matlab" => [new("*.m;*.mat;*.graph;*.fig", "MATLAB files"), AllFiles],
                _ when kind.Contains('*') => [new(kind, string.Empty), AllFiles],
                _ => [AllFiles],
            };
            if (dialogs.OpenFiles("Open", filters, host.CurrentDirectory, false) is not { } chosen)
            {
                return JgsValue.Null;
            }

            // What opening means is the file's kind: data is loaded, a figure is shown, and anything
            // else is handed to whatever the system opens it with.
            string path = chosen.Paths[0];
            string statement = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".mat" => $"load('{Quoted(path)}')",
                ".graph" or ".fig" => $"openfig('{Quoted(path)}');",
                _ => $"winopen('{Quoted(path)}')",
            };
            RunInCallersWorkspace(env, "uiopen", statement, line, col);
            return JgsValue.Null;
        });
    }

    private static string Quoted(string text) => text.Replace("'", "''", StringComparison.Ordinal);

    /// <summary>Runs a statement where the calling code's variables are, as <c>eval</c> does.</summary>
    private static void RunInCallersWorkspace(JgsEnvironment env, string verb, string statement, int line, int col)
    {
        if (!env.TryGet("eval", out JgsValue found) || found.AsCallable is not IJgsMultiCallable eval)
        {
            throw new JgsRuntimeException(line, col, $"{verb}: this host cannot run '{statement}'.");
        }

        eval.CallMultiple([JgsValue.Str(statement)], 0, line, col);
    }

    /// <summary>
    /// The type list a script gave a file dialog: one pattern, several separated by semicolons, or a
    /// cell of patterns with an optional second column of descriptions. A name with no wildcard in
    /// it is a file to start on, and its extension the type.
    /// </summary>
    private static (List<ScriptFileFilter> Filters, string? StartName) FileFilters(JgsValue? filter, int line, int col)
    {
        var filters = new List<ScriptFileFilter>();
        string? startName = null;
        if (filter is null || IsEmptyValue(filter))
        {
            return ([AllFiles], null);
        }

        if (filter.Type == JgsType.Cell)
        {
            JgsValue[] cells = filter.AsCell;
            (int rows, int columns) = filter.Rows > 0 && filter.Cols > 0 ? (filter.Rows, filter.Cols) : (cells.Length, 1);
            if (columns != 2)
            {
                rows = cells.Length;
                columns = 1;
            }

            for (int r = 0; r < rows; r++)
            {
                string pattern = TextOf(cells[r]);
                string description = columns == 2 ? TextOf(cells[r + rows]) : string.Empty;
                filters.Add(new ScriptFileFilter(pattern, description));
            }
        }
        else if (IsTextScalar(filter))
        {
            string text = TextOf(filter);
            if (text.Contains('*') || text.Contains('?'))
            {
                filters.Add(new ScriptFileFilter(text, string.Empty));
            }
            else
            {
                startName = text;
                string extension = Path.GetExtension(text);
                if (extension.Length > 0)
                {
                    filters.Add(new ScriptFileFilter("*" + extension, string.Empty));
                }
            }
        }
        else
        {
            throw new JgsRuntimeException(line, col, "MATLAB:uitools:uidialogs:InvalidFilterSpec",
                "The file filter must be a character vector, a string scalar, or a cell array of them.");
        }

        if (!filters.Any(static f => f.Pattern is "*.*" or "*"))
        {
            filters.Add(AllFiles);
        }

        return (filters, startName);
    }

    /// <summary>
    /// <c>[file, location, index] = uigetfile(filter, title, default, 'MultiSelect', mode)</c> and
    /// <c>uiputfile(filter, title, default)</c>. A cancelled dialog answers 0 three times.
    /// </summary>
    private static JgsValue[] UiFileDialog(JGraphScriptGlobals host, bool save, IReadOnlyList<JgsValue> given, int line, int col)
    {
        IScriptNativeDialogs dialogs = NativeDialogs(line, col);
        string verb = save ? "uiputfile" : "uigetfile";
        JgsValue[] args = [.. given.Select(AsChars)];

        // 'MultiSelect', mode may follow the positional three.
        bool multi = false;
        int positional = args.Length;
        for (int i = 0; i < args.Length; i++)
        {
            if (IsTextScalar(args[i]) && TextOf(args[i]).Equals("MultiSelect", StringComparison.OrdinalIgnoreCase))
            {
                if (save || i + 1 >= args.Length || !IsTextScalar(args[i + 1]))
                {
                    throw new JgsRuntimeException(line, col, $"MATLAB:{verb}:InvalidMultiSelect",
                        "MultiSelect takes 'on' or 'off', and only uigetfile has it.");
                }

                multi = TextOf(args[i + 1]).Equals("on", StringComparison.OrdinalIgnoreCase);
                positional = i;
                break;
            }
        }

        if (positional > 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        (List<ScriptFileFilter> filters, string? startName) = FileFilters(positional > 0 ? args[0] : null, line, col);
        string title = positional > 1 && IsTextScalar(args[1]) && TextOf(args[1]).Length > 0
            ? TextOf(args[1])
            : save ? "Select File to Write" : "Select File to Open";
        if (positional > 2 && IsTextScalar(args[2]) && TextOf(args[2]).Length > 0)
        {
            startName = TextOf(args[2]);
        }

        string start = startName is null ? host.CurrentDirectory : host.Resolve(startName);
        IReadOnlyList<string> paths;
        int index;
        if (save)
        {
            if (dialogs.SaveFile(title, filters, start) is not { } written)
            {
                return [JgsValue.Number(0), JgsValue.Number(0), JgsValue.Number(0)];
            }

            paths = [written.Path];
            index = written.FilterIndex;
        }
        else
        {
            if (dialogs.OpenFiles(title, filters, start, multi) is not { Paths.Count: > 0 } opened)
            {
                return [JgsValue.Number(0), JgsValue.Number(0), JgsValue.Number(0)];
            }

            paths = opened.Paths;
            index = opened.FilterIndex;
        }

        string folder = Path.GetDirectoryName(paths[0]) ?? string.Empty;
        if (folder.Length > 0 && !folder.EndsWith(Path.DirectorySeparatorChar))
        {
            folder += Path.DirectorySeparatorChar;
        }

        // One file is its name; several are a row cell of names.
        JgsValue files = paths.Count == 1
            ? JgsValue.Str(Path.GetFileName(paths[0]))
            : JgsValue.Cell([.. paths.Select(static p => JgsValue.Str(Path.GetFileName(p)))]);
        if (paths.Count != 1)
        {
            files.Reshape(1, paths.Count);
        }

        return [files, JgsValue.Str(folder), JgsValue.Number(index)];
    }

    /// <summary><c>uigetdir(start, title)</c>: the folder chosen, or 0.</summary>
    private static JgsValue UiGetDir(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        IScriptNativeDialogs dialogs = NativeDialogs(line, col);
        if (args.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        string start = args.Count > 0 && IsTextScalar(args[0]) && TextOf(args[0]).Length > 0
            ? host.Resolve(TextOf(args[0]))
            : host.CurrentDirectory;
        string title = args.Count > 1 && IsTextScalar(args[1]) ? TextOf(args[1]) : "Select Folder to Open";
        return dialogs.PickFolder(title, start) is { } folder ? JgsValue.Str(folder) : JgsValue.Number(0);
    }

    private static readonly string[] ColorNames = ["Color", "ForegroundColor", "BackgroundColor"];

    /// <summary>
    /// <c>uisetcolor</c>, <c>uisetcolor(rgb)</c>, <c>uisetcolor(h)</c> and each with a title: the
    /// colour chosen. Given an object, the choice is written to its colour; cancelled, the answer is
    /// the colour offered, or 0 when none was.
    /// </summary>
    private static JgsValue UiSetColor(IReadOnlyList<JgsValue> args, int line, int col)
    {
        IScriptNativeDialogs dialogs = NativeDialogs(line, col);
        if (args.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        string title = "Color";
        JgsValue? subject = null;
        foreach (JgsValue argument in args.Select(AsChars))
        {
            // A title is text that is not a colour's name; everything else is the colour or the object.
            if (IsTextScalar(argument) && subject is not null)
            {
                title = TextOf(argument);
            }
            else if (subject is null)
            {
                subject = argument;
            }
            else
            {
                throw new JgsRuntimeException(line, col, "MATLAB:uisetcolor:InvalidParameterList", "Title should be the second arg");
            }
        }

        JgsHandleEntry? owner = null;
        string? property = null;
        (double R, double G, double B)? initial = null;
        if (subject is not null)
        {
            if (subject.Type == JgsType.Number && JgsHandleRegistry.TryGet(subject, out owner))
            {
                property = ColorNames.FirstOrDefault(name => JgsGraphicsProperties.TryFind(owner.Target, name, out _))
                    ?? throw new JgsRuntimeException(line, col, "MATLAB:uisetcolor:InvalidObject",
                        "The object has no color property to set.");
                double[] now = ToDoubles("uisetcolor", JgsGraphicsProperties.Get(owner, property, line, col), line, col);
                initial = now.Length == 3 ? (now[0], now[1], now[2]) : null;
            }
            else if (IsTextScalar(subject) && args.Count == 1 && !LooksLikeColor(subject, line, col))
            {
                title = TextOf(subject);
            }
            else
            {
                Core.Drawing.Color given = OptionColor(subject, line, col, "uisetcolor");
                initial = (given.R / 255.0, given.G / 255.0, given.B / 255.0);
            }
        }

        if (dialogs.PickColor(title, initial) is not { } picked)
        {
            return initial is { } kept ? Vec(kept.R, kept.G, kept.B) : JgsValue.Number(0);
        }

        JgsValue colour = Vec(picked.R, picked.G, picked.B);
        if (owner is not null && property is not null)
        {
            JgsGraphicsProperties.Set(owner, property, colour, line, col);
        }

        return colour;
    }

    private static bool LooksLikeColor(JgsValue value, int line, int col)
    {
        try
        {
            OptionColor(value, line, col, "uisetcolor");
            return true;
        }
        catch (JgsRuntimeException)
        {
            return false;
        }
    }

    private static readonly string[] FontNames = ["FontName", "FontWeight", "FontAngle", "FontUnits", "FontSize"];

    /// <summary>
    /// <c>uisetfont</c>, <c>uisetfont(h)</c>, <c>uisetfont(s)</c> and each with a title: the font
    /// chosen, as a struct of <c>FontName</c>, <c>FontWeight</c>, <c>FontAngle</c>, <c>FontUnits</c>
    /// and <c>FontSize</c>; 0 when cancelled. Given an object, the choice is written to it.
    /// </summary>
    private static JgsValue UiSetFont(IReadOnlyList<JgsValue> args, int line, int col)
    {
        IScriptNativeDialogs dialogs = NativeDialogs(line, col);
        if (args.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        string title = "Font";
        JgsHandleEntry? owner = null;
        ScriptFontChoice? initial = null;
        for (int i = 0; i < args.Count; i++)
        {
            JgsValue argument = AsChars(args[i]);
            if (IsTextScalar(argument))
            {
                title = TextOf(argument);
            }
            else if (i > 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:uisetfont:InvalidParameterList", "Title should be the second arg");
            }
            else if (argument.Type == JgsType.Struct && !argument.IsStructArray)
            {
                Dictionary<string, JgsValue> fields = argument.AsStruct;
                string Word(string name, string otherwise) =>
                    fields.TryGetValue(name, out JgsValue? value) && IsTextScalar(value) ? TextOf(value) : otherwise;
                double size = fields.TryGetValue("FontSize", out JgsValue? points) && points.Type == JgsType.Number ? points.AsNumber : 10;
                initial = new ScriptFontChoice(Word("FontName", "Arial"), size, Word("FontWeight", "normal") == "bold", Word("FontAngle", "normal") == "italic");
            }
            else if (argument.Type == JgsType.Number && JgsHandleRegistry.TryGet(argument, out owner)
                     && FontNames.All(name => JgsGraphicsProperties.TryFind(owner.Target, name, out _)))
            {
                string Word(string name) => TextOf(JgsGraphicsProperties.Get(owner, name, line, col));
                initial = new ScriptFontChoice(
                    Word("FontName"), JgsGraphicsProperties.Get(owner, "FontSize", line, col).AsNumber,
                    Word("FontWeight") == "bold", Word("FontAngle") == "italic");
            }
            else
            {
                throw new JgsRuntimeException(line, col, "MATLAB:uisetfont:InvalidObject",
                    "The first argument is a font structure or an object with font properties.");
            }
        }

        if (dialogs.PickFont(title, initial) is not { } picked)
        {
            return JgsValue.Number(0);
        }

        var font = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["FontName"] = JgsValue.Str(picked.Name),
            ["FontWeight"] = JgsValue.Str(picked.Bold ? "bold" : "normal"),
            ["FontAngle"] = JgsValue.Str(picked.Italic ? "italic" : "normal"),
            ["FontUnits"] = JgsValue.Str("points"),
            ["FontSize"] = JgsValue.Number(picked.SizePoints),
        };
        if (owner is not null)
        {
            foreach (string name in FontNames)
            {
                JgsGraphicsProperties.Set(owner, name, font[name], line, col);
            }
        }

        return JgsValue.Struct(font);
    }
}
