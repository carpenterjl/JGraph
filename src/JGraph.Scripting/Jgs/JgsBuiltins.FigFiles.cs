using System.IO;
using JGraph.Api;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// U11 of the app-building plan (ADR 0210): MATLAB's own <c>.fig</c> files and GUIDE apps.
/// <c>openfig</c> and <c>hgload</c> read either kind of figure file - a MATLAB MAT-file, in its
/// R2024a-and-earlier struct form or R2025b's subsystem form, or this build's own <c>.graph</c>
/// document under a <c>.fig</c> name (ADR 0060) - told apart by their first bytes.
/// <c>gui_mainfcn</c> is the GUIDE runtime, written from the contract a GUIDE app's generated code
/// relies on and from R2025b's measured behaviour (probe <c>u11_guide</c>), not from MathWorks'
/// file. <c>guide</c> itself answers R2025b's removal.
/// </summary>
internal static partial class JgsBuiltins
{
    private static readonly string[] OpenFigOptions = ["new", "reuse", "visible", "invisible"];

    private static void RegisterFigFileBuiltins(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        env.Builtins.Register("openfig", JgsValue.Function(new BuiltinFunction("openfig",
            (args, line, col) => OpenFig(interpreter, host, args, line, col))));

        env.Builtins.Register("hgload", JgsValue.Function(new BuiltinFunction("hgload",
            (args, line, col) => HgLoad(interpreter, host, args, 1, line, col)[0])
        {
            MultiOutput = (args, wanted, line, col) => HgLoad(interpreter, host, args, wanted, line, col),
        }));

        env.Builtins.Register("gui_mainfcn", JgsValue.Function(new BuiltinFunction("gui_mainfcn",
            (args, line, col) => GuiMainFcn(interpreter, host, args, 1, line, col) is { Length: > 0 } outputs ? outputs[0] : JgsValue.Null)
        {
            MultiOutput = (args, wanted, line, col) => GuiMainFcn(interpreter, host, args, wanted, line, col),
            KnowsWhenDiscarded = true,
        }));

        // R2025b's guide is a stub that refuses whatever it is given. Its message ends on a link to
        // MATLAB's migration tool, which this build does not have, so the sentence names it plainly.
        env.Builtins.Register("guide", JgsValue.Function(new BuiltinFunction("guide", (_, line, col) =>
            throw new JgsRuntimeException(line, col, "MATLAB:guide:GUIDEHasBeenRemoved",
                "The GUIDE interactive design environment has been removed. You can continue to run and edit "
                + "your GUIDE app from the MATLAB Editor by opening the app code file. To continue developing "
                + "your apps interactively, use the GUIDE to App Designer Migration Tool."))
        { BindsAnsAsStatement = false, AutoCallsBare = true }));
    }

    // --- openfig and hgload ---------------------------------------------------------------------

    private static JgsValue OpenFig(
        Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        string given = StrOf("openfig", args[0], line, col);
        bool reuse = false;
        bool? visible = null;
        for (int i = 1; i < args.Count; i++)
        {
            string word = IsTextScalar(args[i]) ? TextOf(args[i]) : string.Empty;
            switch (word.ToLowerInvariant())
            {
                case "new": reuse = false; break;
                case "reuse": reuse = true; break;
                case "visible": visible = true; break;
                case "invisible": visible = false; break;
                default:
                    throw new JgsRuntimeException(line, col, "MATLAB:openfig:InvalidOption", $"'{word}' is not a recognized option.");
            }
        }

        string path = FigFilePath(host, given, line, col);
        if (reuse && OpenFigureFrom(path) is { } open)
        {
            if (visible is bool shown)
            {
                JgsGraphicsProperties.Set(JgsHandleRegistry.EntryFor(open), "Visible", JgsValue.Str(shown ? "on" : "off"), line, col);
            }

            return JgsHandleRegistry.For(open);
        }

        return JgsHandleRegistry.For(LoadFigureFile(interpreter, host, path, visible, line, col).Figure);
    }

    /// <summary>
    /// <c>[h, old] = hgload(file, props)</c>: the figure, with <paramref name="args"/>' struct of
    /// properties set on it after it opens; <c>old</c> is a cell holding a struct of what they were,
    /// <c>Visible</c> left out, as R2025b answers (a cell holding an empty value when none were given).
    /// </summary>
    private static JgsValue[] HgLoad(
        Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count is 0 or > 3)
        {
            throw new JgsRuntimeException(line, col, args.Count == 0 ? "MATLAB:narginchk:notEnoughInputs" : "MATLAB:narginchk:tooManyInputs",
                args.Count == 0 ? "Not enough input arguments." : "Too many input arguments.");
        }

        string path = FigFilePath(host, StrOf("hgload", args[0], line, col), line, col);
        FigureModel figure = LoadFigureFile(interpreter, host, path, null, line, col).Figure;
        JgsValue old = JgsValue.Cell([JgsValue.Array([])]);
        if (args.Count >= 2 && args[1].Type == JgsType.Struct && !args[1].IsStructArray)
        {
            JgsHandleEntry entry = JgsHandleRegistry.EntryFor(figure);
            var previous = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            foreach ((string name, JgsValue value) in args[1].AsStruct)
            {
                // R2025b leaves Visible out of what it hands back: the figure opened hidden.
                if (!name.Equals("Visible", StringComparison.OrdinalIgnoreCase))
                {
                    previous[name] = JgsGraphicsProperties.Get(entry, name, line, col);
                }

                JgsGraphicsProperties.Set(entry, name, value, line, col);
            }

            old = JgsValue.Cell([JgsValue.Struct(previous)]);
        }

        JgsValue handle = JgsHandleRegistry.For(figure);
        return wanted >= 2 ? [handle, old] : [handle];
    }

    /// <summary>
    /// The file a figure verb names: as given, or with <c>.fig</c> added when it has no extension;
    /// one that does not exist is R2025b's refusal, naming it as it was given.
    /// </summary>
    private static string FigFilePath(JGraphScriptGlobals host, string given, int line, int col)
    {
        string resolved = host.Resolve(given);
        if (!File.Exists(resolved) && !Path.HasExtension(given) && File.Exists(host.Resolve(given + ".fig")))
        {
            resolved = host.Resolve(given + ".fig");
        }

        return File.Exists(resolved)
            ? Path.GetFullPath(resolved)
            : throw new JgsRuntimeException(line, col, "MATLAB:load:couldNotReadFile", $"Unable to find file or directory '{given}'.");
    }

    /// <summary>The first open figure read from <paramref name="path"/>, which <c>'reuse'</c> answers instead of a new one.</summary>
    private static FigureModel? OpenFigureFrom(string path)
    {
        foreach (int number in JG.FigureNumbers)
        {
            if (JG.TryGetFigure(number, out FigureModel figure)
                && string.Equals(figure.FileName, path, StringComparison.OrdinalIgnoreCase))
            {
                return figure;
            }
        }

        return null;
    }

    /// <summary>
    /// Opens a figure file of either kind: a MATLAB MAT-file through <see cref="JgsFigFile"/>, this
    /// build's own document through the host. Answers the figure and whether it was saved visible.
    /// </summary>
    private static (FigureModel Figure, bool SavedVisible) LoadFigureFile(
        Interpreter interpreter, JGraphScriptGlobals host, string path, bool? visible, int line, int col)
    {
        FigureModel figure;
        bool savedVisible;
        if (JgsFigFile.IsMatFile(path))
        {
            FigNode root;
            try
            {
                root = JgsFigFile.Read(path, new MatWorkspaceBinder(interpreter, host, line, col));
            }
            catch (InvalidDataException)
            {
                throw new JgsRuntimeException(line, col, JgsFigFile.InvalidFigId, JgsFigFile.InvalidFigMessage);
            }

            savedVisible = !(root.Property("Visible") is { } saved && IsTextScalar(saved) && TextOf(saved) == "off");
            var skipped = new List<string>();
            figure = JgsFigFile.Build(root, interpreter, visible ?? savedVisible, skipped, line, col);
            figure.FileName = path;
            if (skipped.Count > 0)
            {
                Warn(host, "JGraph:openfig:NotRebuilt",
                    $"'{Path.GetFileName(path)}' holds objects this build does not rebuild yet ({string.Join(", ", skipped.Distinct())}); they were left out.");
            }

            return (figure, savedVisible);
        }

        try
        {
            figure = host.loadfigure(path);
        }
        catch (Exception ex) when (ex is not (JgsException or OperationCanceledException))
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }

        figure.FileName = path;
        savedVisible = figure.Visible;
        if (visible is bool shown)
        {
            JgsGraphicsProperties.Set(JgsHandleRegistry.EntryFor(figure), "Visible", JgsValue.Str(shown ? "on" : "off"), line, col);
        }

        return (figure, savedVisible);
    }

    // --- gui_mainfcn ------------------------------------------------------------------------------

    /// <summary>
    /// The GUIDE runtime. A GUIDE app's main function hands over its <c>gui_State</c> and its own
    /// arguments; from them this either runs one of the app's callbacks or opens the app:
    /// <list type="bullet">
    /// <item>a callback, when the first argument names a function and the second is a graphics
    /// object whose <c>Tag</c> and an underscore begin that name (or the name is a
    /// <c>_CreateFcn</c>) - the app's <c>gui_Callback</c> is called with the rest;</item>
    /// <item>otherwise the app opens: the singleton's open figure, or the <c>.fig</c> beside the
    /// app's file (or what its <c>gui_LayoutFcn</c> makes), invisible; its handles stored by
    /// <c>guidata</c>; the arguments' figure properties set (others ignored, <c>'Visible'</c> kept
    /// for the end); the opening function run with <c>HandleVisibility</c> on; the figure shown if
    /// it was saved visible and not asked to stay hidden; then the output function, asked for
    /// what the caller asked for.</item>
    /// </list>
    /// </summary>
    private static JgsValue[] GuiMainFcn(
        Interpreter interpreter, JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        if (args[0].Type != JgsType.Struct || args[0].IsStructArray)
        {
            throw new JgsRuntimeException(line, col, "gui_mainfcn expects the app's gui_State struct first.");
        }

        IReadOnlyDictionary<string, JgsValue> state = args[0].AsStruct;
        JgsValue[] rest = [.. args.Skip(1)];

        // A callback the figure's objects invoke through the app's main function.
        if (rest.Length >= 2 && IsTextScalar(rest[0]) && Field(state, "gui_Callback") is { Type: JgsType.Function } callback
            && JgsHandleRegistry.TryGet(rest[1], out JgsHandleEntry? source) && IsCallbackName(TextOf(rest[0]), source, line, col))
        {
            return Invoke(callback, rest[1..], wanted, line, col);
        }

        string name = Field(state, "gui_Name") is { } named && IsTextScalar(named) ? TextOf(named) : string.Empty;
        bool singleton = Field(state, "gui_Singleton") is { } single && single.Type is JgsType.Number or JgsType.Bool && single.IsTruthy;

        // The figure: what the layout function makes, the singleton already open, or the .fig.
        FigureModel figure;
        bool savedVisible;
        if (Field(state, "gui_LayoutFcn") is { Type: JgsType.Function } layout)
        {
            JgsValue made = Invoke(layout, [JgsValue.Str(singleton ? "reuse" : "new")], 1, line, col) is { Length: > 0 } outputs
                ? outputs[0]
                : throw new JgsRuntimeException(line, col, $"gui_mainfcn: the layout function of '{name}' made no figure.");
            figure = JgsHandleRegistry.TryGet(made, out JgsHandleEntry? laid) && laid.Target is FigureModel fromLayout
                ? fromLayout
                : throw new JgsRuntimeException(line, col, $"gui_mainfcn: the layout function of '{name}' made no figure.");
            savedVisible = figure.Visible;
        }
        else
        {
            string fig = GuiFigPath(interpreter, host, name);
            if (singleton && File.Exists(fig) && OpenFigureFrom(Path.GetFullPath(fig)) is { } open)
            {
                figure = open;
                savedVisible = figure.Visible;
            }
            else
            {
                string path = FigFilePath(host, File.Exists(fig) ? fig : name + ".fig", line, col);
                (figure, savedVisible) = LoadFigureFile(interpreter, host, path, false, line, col);
            }
        }

        JgsValue handle = JgsHandleRegistry.For(figure);
        JgsHandleEntry entry = JgsHandleRegistry.EntryFor(figure);
        SetQuietly(entry, "Visible", JgsValue.Str("off"), line, col);
        CallNamed(interpreter, "setappdata", [handle, JgsValue.Str("InGUIInitialization"), JgsValue.Number(1)], line, col);

        // The handles: guihandles merged over whatever the figure already holds.
        JgsValue held = CallNamed(interpreter, "guidata", [handle], line, col);
        JgsValue found = CallNamed(interpreter, "guihandles", [handle], line, col);
        var merged = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        if (held.Type == JgsType.Struct && !held.IsStructArray)
        {
            foreach ((string key, JgsValue value) in held.AsStruct)
            {
                merged[key] = value;
            }
        }

        if (found.Type == JgsType.Struct && !found.IsStructArray)
        {
            foreach ((string key, JgsValue value) in found.AsStruct)
            {
                merged[key] = value;
            }
        }

        CallNamed(interpreter, "guidata", [handle, JgsValue.Struct(merged)], line, col);

        // The arguments' figure properties; 'Visible' waits for the end, anything else is ignored.
        bool? visiblePair = null;
        for (int i = 0; i + 1 < rest.Length; i += 2)
        {
            if (!IsTextScalar(rest[i]))
            {
                continue;
            }

            string property = TextOf(rest[i]);
            if (property.Equals("Visible", StringComparison.OrdinalIgnoreCase))
            {
                visiblePair = IsTextScalar(rest[i + 1]) ? TextOf(rest[i + 1]).Equals("on", StringComparison.OrdinalIgnoreCase)
                    : rest[i + 1].IsTruthy;
            }
            else if (JgsGraphicsProperties.TryFind(figure, property, out _))
            {
                SetQuietly(entry, property, rest[i + 1], line, col);
            }
        }

        // The opening function sees the figure with its handle visible, whatever it was saved with.
        JgsValue visibility = JgsGraphicsProperties.Get(entry, "HandleVisibility", line, col);
        SetQuietly(entry, "HandleVisibility", JgsValue.Str("on"), line, col);
        try
        {
            if (Field(state, "gui_OpeningFcn") is { Type: JgsType.Function } opening)
            {
                Invoke(opening, [handle, JgsValue.Array([]), CallNamed(interpreter, "guidata", [handle], line, col), .. rest], 0, line, col);
            }
        }
        finally
        {
            if (IsOpen(figure))
            {
                SetQuietly(entry, "HandleVisibility", visibility, line, col);
            }
        }

        bool alive = IsOpen(figure);
        if (alive)
        {
            if (visiblePair ?? savedVisible)
            {
                SetQuietly(entry, "Visible", JgsValue.Str("on"), line, col);
            }

            CallNamed(interpreter, "rmappdata", [handle, JgsValue.Str("InGUIInitialization")], line, col);
        }

        if (Field(state, "gui_OutputFcn") is not { Type: JgsType.Function } output)
        {
            return [];
        }

        JgsValue data = alive ? CallNamed(interpreter, "guidata", [handle], line, col) : JgsValue.Array([]);
        JgsValue[] answers = Invoke(output, [handle, JgsValue.Array([]), data], wanted, line, col);
        return wanted == 0 ? [] : answers;
    }

    /// <summary>
    /// Whether <paramref name="name"/> is one of the app's callbacks for <paramref name="source"/>:
    /// it contains the object's <c>Tag</c> and an underscore, or it is a <c>CreateFcn</c>.
    /// </summary>
    private static bool IsCallbackName(string name, JgsHandleEntry source, int line, int col)
    {
        JgsValue tag = JgsGraphicsProperties.TryFind(source.Target, "Tag", out _)
            ? JgsGraphicsProperties.Get(source, "Tag", line, col)
            : JgsValue.Str(string.Empty);
        string prefix = (IsTextScalar(tag) ? TextOf(tag) : string.Empty) + "_";
        return name.Contains(prefix, StringComparison.Ordinal) || name.Contains("_CreateFcn", StringComparison.Ordinal);
    }

    /// <summary>The <c>.fig</c> beside the app's file, found as the app's name is found; or the bare name's.</summary>
    private static string GuiFigPath(Interpreter interpreter, JGraphScriptGlobals host, string name)
    {
        if (interpreter.FunctionPath?.Find(name) is { } file && Path.GetDirectoryName(file) is { } folder)
        {
            string beside = Path.Combine(folder, name + ".fig");
            if (File.Exists(beside))
            {
                return beside;
            }
        }

        return host.Resolve(name + ".fig");
    }

    private static JgsValue? Field(IReadOnlyDictionary<string, JgsValue> state, string name) =>
        state.TryGetValue(name, out JgsValue? value) ? value : null;

    private static bool IsOpen(FigureModel figure) => JG.GetFigureNumber(figure) != 0;

    /// <summary>Calls a function value asking for <paramref name="wanted"/> outputs; zero is a statement call.</summary>
    private static JgsValue[] Invoke(JgsValue function, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        IJgsCallable callable = function.AsCallable;
        if (callable is IJgsMultiCallable several)
        {
            return several.CallMultiple(args, wanted, line, col);
        }

        JgsValue single = callable.Call(args, line, col);
        return wanted == 0 ? [] : [single];
    }

    private static JgsValue CallNamed(Interpreter interpreter, string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (!interpreter.Globals.Builtins.TryGet(name, out JgsValue function) || function.Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col, $"gui_mainfcn: '{name}' is not available here.");
        }

        return function.AsCallable.Call(args, line, col);
    }

    private static void SetQuietly(JgsHandleEntry entry, string name, JgsValue value, int line, int col)
    {
        try
        {
            JgsGraphicsProperties.Set(entry, name, value, line, col);
        }
        catch (JgsException)
        {
            // a value the figure refuses is ignored, as gui_mainfcn ignores a name it does not have
        }
    }
}
