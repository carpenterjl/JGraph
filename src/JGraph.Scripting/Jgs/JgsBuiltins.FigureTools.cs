using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Imaging;
using JGraph.Objects.Annotations;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M60 wave A: <c>annotation</c>, which draws on the figure rather than on any axes.
/// <para>
/// Every annotation kind already existed — M4 built them for the editing surface and M26 made them
/// first-class — so this verb is a reader for MATLAB's argument shapes and nothing else. It draws
/// with <see cref="AnnotationSpace.Figure"/>, whose coordinates are normalized to the figure and are
/// therefore the coordinates MATLAB documents, with one difference this file owns: MATLAB measures y
/// upwards from the bottom of the figure and this model measures it downwards from the top.
/// <see cref="JgsGraphicsProperties.Up"/> is the whole of that conversion, and it is applied here on
/// the way in and in the property table on the way back out, so a script never sees the model's
/// origin.
/// </para>
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>The annotation kinds MATLAB documents, in the spellings it accepts.</summary>
    private static readonly string[] AnnotationKinds =
    [
        "rectangle", "ellipse", "textbox", "line", "arrow", "doublearrow", "textarrow",
    ];

    /// <summary>The kinds measured by a box rather than by two ends.</summary>
    private static bool IsBoxedAnnotation(string kind) =>
        kind is "rectangle" or "ellipse" or "textbox";

    private static void RegisterFigureToolBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        void DefineSilent(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        DefineSilent("annotation", Annotation);

        // --- Figures as files -------------------------------------------------------------------
        // MATLAB's four names for two operations. `.fig` is the extension a script writes and the
        // one these accept; what lands in it is this build's own `.graph` document, because a real
        // MAT-file-of-handle-objects is a format for a different program's object model.
        DefineSilent("savefig", (args, line, col) => SaveFigure(host, "savefig", args, line, col));
        DefineSilent("hgsave", (args, line, col) => SaveFigure(host, "hgsave", args, line, col));
        Define("openfig", (args, line, col) => OpenFigure(host, "openfig", args, line, col));
        Define("hgload", (args, line, col) => OpenFigure(host, "hgload", args, line, col));

        // --- Figures as pictures ----------------------------------------------------------------
        DefineSilent("exportgraphics", (args, line, col) => ExportGraphics(host, "exportgraphics", args, line, col));
        DefineSilent("hgexport", (args, line, col) => ExportGraphics(host, "hgexport", args, line, col));
        DefineSilent("copygraphics", (args, line, col) => CopyGraphics(host, args, line, col));
        // `f = getframe` with no parentheses is the form every script uses, so the bare name has to
        // take the picture rather than hand back the verb that would.
        env.Declare("getframe", JgsValue.Function(
            new BuiltinFunction("getframe", (args, line, col) => GetFrame(host, args, line, col))
            { AutoCallsBare = true }));

        // --- The dialogs, the window capture and uiaxes (M84) -------------------------------------
        RegisterDialogBuiltins(env, host);
    }

    /// <summary>The host's figure file services, or an error naming the verb that wanted them.</summary>
    private static IScriptFigureFiles RequireFigureFiles(
        JGraphScriptGlobals host, string verb, int line, int col) =>
        host.FigureFiles
            ?? throw new JgsRuntimeException(line, col, $"{verb} is not supported by this host.");

    /// <summary>
    /// Runs a file operation, turning an IO or format failure into a diagnostic carrying the script's
    /// own position — the same wrapping the three JGS figure-file verbs have had since M19.
    /// </summary>
    private static JgsValue Attempt(Action work, int line, int col)
    {
        try
        {
            work();
        }
        catch (Exception ex) when (ex is not (JgsException or OperationCanceledException))
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }

        return JgsValue.Null;
    }

    /// <summary>
    /// The figure a verb was aimed at, and what is left of its arguments. Aiming at an axes aims at
    /// the figure that holds it: everything in this family works on whole figures, which is a
    /// recorded divergence from MATLAB's <c>exportgraphics(ax, …)</c> cropping to the axes.
    /// </summary>
    private static (FigureModel Figure, IReadOnlyList<JgsValue> Remaining) PeelFigure(IReadOnlyList<JgsValue> args)
    {
        if (args.Count > 0 && args[0].Type != JgsType.String
            && JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? entry))
        {
            GraphObject? walk = entry.Target;
            while (walk is not null and not FigureModel)
            {
                walk = walk.Parent;
            }

            if (walk is FigureModel figure)
            {
                return (figure, args.Skip(1).ToList());
            }
        }

        return (JG.CurrentFigure, args);
    }

    /// <summary>The path a file verb was given, with <paramref name="fallback"/> added if it has no extension.</summary>
    private static string FilePath(
        string verb, IReadOnlyList<JgsValue> args, int index, string fallback, int line, int col)
    {
        if (index >= args.Count)
        {
            throw new JgsRuntimeException(line, col, $"{verb} expects the name of a file to write.");
        }

        string path = StrOf(verb, args[index], line, col);
        return Path.HasExtension(path) ? path : path + fallback;
    }

    private static JgsValue SaveFigure(
        JGraphScriptGlobals host, string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        (FigureModel figure, IReadOnlyList<JgsValue> rest) = PeelFigure(args);

        // hgsave's arguments are the other way round in its oldest form, hgsave(filename), which is
        // the same shape PeelFigure already leaves behind — so there is nothing extra to read.
        string path = FilePath(verb, rest, 0, ".fig", line, col);

        // 'compact' and 'compactv7.3' name a MAT-file variant, and this document format has one
        // spelling, so the words are accepted and change nothing.
        for (int i = 1; i < rest.Count; i++)
        {
            OneOfWord(verb, rest[i], ["compact", "compactv7.3", "-v7.3", "-v7"], line, col);
        }

        // A saved figure knows where it was saved: MATLAB's FileName is set by this and read back
        // by a script that wants to save again over the same file.
        return Attempt(() =>
        {
            host.savefigure(path, figure);
            figure.FileName = path;
        }, line, col);
    }

    private static JgsValue OpenFigure(
        JGraphScriptGlobals host, string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, $"{verb} expects the name of a file to read.");
        }

        string path = FilePath(verb, args, 0, ".fig", line, col);

        // 'new' and 'reuse' decide whether a second call re-opens the same window. Every load here
        // registers a new numbered figure, so 'reuse' is accepted and behaves as 'new' — recorded,
        // because the alternative is to pretend a window exists in a run that has none.
        for (int i = 1; i < args.Count; i++)
        {
            OneOfWord(verb, args[i], ["new", "reuse", "invisible", "visible"], line, col);
        }

        FigureModel figure;
        try
        {
            figure = host.loadfigure(path);
        }
        catch (Exception ex) when (ex is not (JgsException or OperationCanceledException))
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }

        figure.FileName = path;
        return JgsHandleRegistry.For(figure);
    }

    /// <summary>The option names the two picture verbs share, in MATLAB's spellings.</summary>
    private static readonly string[] PictureOptionNames =
        ["Resolution", "ContentType", "BackgroundColor", "Append", "Colorspace"];

    private static JgsValue ExportGraphics(
        JGraphScriptGlobals host, string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        (FigureModel figure, IReadOnlyList<JgsValue> rest) = PeelFigure(args);
        string path = FilePath(verb, rest, 0, ".png", line, col);

        // The option tail is read for its spelling even where it changes nothing, so that a
        // misspelling is answered rather than dropped — the defect class M52 closed for regexprep.
        // The resolution is the one that does change something, and only for a raster format.
        var spec = new OptionSpec(verb, [], PictureOptionNames, StringPositionals: 1);
        ParsedArgs options = spec.Parse(rest, 1, line, col);
        options.Word("ContentType", "auto", "auto", "vector", "image");
        options.Word("Colorspace", "rgb", "rgb", "gray");

        // Read since M52 for its spelling; acted on since M75. A resolution is dots per inch, and a
        // device-independent unit is a ninety-sixth of one, so the ratio is the scale to draw at.
        //
        // The figure's export preset stands in only where the caller said nothing (M84). A preset
        // that overrode an explicit argument would be action at a distance — a script's own
        // 'Resolution' quietly losing to a dialog someone opened once — and that is invisible in the
        // script that suffers it, which is why the default is read from the preset rather than the
        // preset applied over the answer.
        double resolution = options.Scalar("Resolution", figure.ExportSetup.Resolution ?? 96);
        if (!double.IsFinite(resolution) || resolution <= 0)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: Resolution is a positive number of dots per inch, but got {resolution}.");
        }

        JGraph.Core.Drawing.Color? restore = null;
        if (options.Named("BackgroundColor") is { } asked)
        {
            restore = figure.Background;
            figure.Background = OptionColor(asked, line, col, verb);
        }
        else if (figure.ExportSetup.Background is { } preset)
        {
            restore = figure.Background;
            figure.Background = preset;
        }

        try
        {
            return Attempt(() => host.exportfigure(path, figure, resolution / 96.0), line, col);
        }
        finally
        {
            if (restore is { } original)
            {
                figure.Background = original;
            }
        }
    }

    private static JgsValue CopyGraphics(
        JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        (FigureModel figure, IReadOnlyList<JgsValue> rest) = PeelFigure(args);
        var spec = new OptionSpec("copygraphics", [], PictureOptionNames);
        ParsedArgs options = spec.Parse(rest, 0, line, col);
        double resolution = options.Scalar("Resolution", 150);

        // A host with no clipboard says so by answering false, and that is the end of it: a headless
        // run that copies a figure has done everything it can be asked to do.
        RequireFigureFiles(host, "copygraphics", line, col).CopyToClipboard(figure, resolution / 96.0);
        return JgsValue.Null;
    }

    private static JgsValue GetFrame(
        JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        // getframe is one of MATLAB's interruption points: an animation loop capturing frames is
        // exactly the loop a person is most likely to click during.
        PumpEvents();

        (FigureModel figure, IReadOnlyList<JgsValue> rest) = PeelFigure(args);
        if (rest.Count > 0)
        {
            throw new JgsRuntimeException(line, col,
                "getframe takes a figure or axes handle and nothing else; a rectangle to crop to is not read here.");
        }

        using ImageBuffer pixels = RequireFigureFiles(host, "getframe", line, col).Capture(figure, 1.0);
        var frame = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["cdata"] = FrameData(pixels),

            // A true-colour frame carries no colour table, and MATLAB answers with an empty one.
            ["colormap"] = JgsValue.Array([]),
        };

        return JgsValue.Struct(frame);
    }

    /// <summary>
    /// A captured figure as MATLAB's <c>cdata</c>: a height-by-width-by-3 array of <c>uint8</c>.
    /// <para>
    /// It is deliberately a plain array rather than this build's image value. A frame is something a
    /// script does arithmetic on — <c>double(f.cdata)</c> to difference two of them is the whole
    /// point of having <c>getframe</c> at all — and an image value would have needed every numeric
    /// verb to learn about pictures. The array is what MATLAB hands back, and it still goes into
    /// <c>imshow</c> and <c>imwrite</c>, which read an array as readily as an image.
    /// </para>
    /// </summary>
    private static JgsValue FrameData(ImageBuffer pixels)
    {
        int height = pixels.Height;
        int width = pixels.Width;
        var flat = new double[(long)height * width * 3];
        ReadOnlySpan<double> samples = pixels.Pixels;

        // The buffer is row-major and interleaved; an array is column-major with the channels last,
        // so this is a transpose rather than a copy.
        for (int channel = 0; channel < 3; channel++)
        {
            int plane = channel * height * width;
            for (int c = 0; c < width; c++)
            {
                int column = plane + (c * height);
                for (int r = 0; r < height; r++)
                {
                    flat[column + r] = System.Math.Round(samples[(((r * width) + c) * 3) + channel] * 255);
                }
            }
        }

        JgsValue data = JgsMatrix.FromColumnMajorDims(flat, [height, width, 3]);
        data.SetNumericClass(JgsNumericClass.UInt8);
        return data;
    }

    private static JgsValue Annotation(IReadOnlyList<JgsValue> args, int line, int col)
    {
        // annotation(fig, …) names the figure to draw on. A figure's handle is its number, so this
        // is told from annotation('line', …) by the type of the first argument, not by counting.
        IReadOnlyList<JgsValue> rest = args;
        FigureModel figure = JG.CurrentFigure;
        if (args.Count > 0 && args[0].Type != JgsType.String
            && JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? named)
            && named.Target is FigureModel target)
        {
            figure = target;
            rest = args.Skip(1).ToList();
        }

        if (rest.Count == 0)
        {
            throw new JgsRuntimeException(line, col,
                $"annotation expects the kind to draw: {string.Join(", ", AnnotationKinds)}.");
        }

        string kind = OneOfWord("annotation", rest[0], AnnotationKinds, line, col);
        int next = 1;

        // The geometry is a box for the three that have one, and a pair of ends for the four that do
        // not — read as one [x y w h] or as two 2-element vectors. Both are optional, because
        // annotation('arrow') is a documented call that takes MATLAB's default placement.
        Point2D a;
        Point2D b;
        if (IsBoxedAnnotation(kind))
        {
            double[] box = next < rest.Count
                ? Numbers4("annotation", rest[next++], line, col)
                : [0.3, 0.3, 0.4, 0.4];
            a = new Point2D(box[0], JgsGraphicsProperties.Up(box[1] + box[3]));
            b = new Point2D(box[0] + box[2], JgsGraphicsProperties.Up(box[1]));
        }
        else if (next + 1 < rest.Count && rest[next].Type != JgsType.String
                 && rest[next + 1].Type != JgsType.String)
        {
            double[] xs = Numbers2("annotation", rest[next++], line, col);
            double[] ys = Numbers2("annotation", rest[next++], line, col);
            a = new Point2D(xs[0], JgsGraphicsProperties.Up(ys[0]));
            b = new Point2D(xs[1], JgsGraphicsProperties.Up(ys[1]));
        }
        else
        {
            a = new Point2D(0.3, JgsGraphicsProperties.Up(0.3));
            b = new Point2D(0.7, JgsGraphicsProperties.Up(0.7));
        }

        AnnotationObject annotation = NewAnnotation(kind, a, b);
        annotation.Space = AnnotationSpace.Figure;
        figure.Annotations.Add(annotation);

        // The option tail goes through the same property table get and set use, so every documented
        // property of the object works here by having been written once, and a misspelling is
        // answered with the near spellings rather than being dropped.
        JgsValue handle = JgsHandleRegistry.For(annotation);
        JgsHandleEntry entry = JgsHandleRegistry.Require(handle, line, col);
        if ((rest.Count - next) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col,
                "annotation: the properties after the position come in name/value pairs.");
        }

        for (int i = next; i < rest.Count; i += 2)
        {
            JgsGraphicsProperties.Set(
                entry, StrOf("annotation", rest[i], line, col), rest[i + 1], line, col);
        }

        return handle;
    }

    private static AnnotationObject NewAnnotation(string kind, Point2D a, Point2D b) => kind switch
    {
        "rectangle" => new RectangleAnnotation(a.X, a.Y, b.X, b.Y),
        "ellipse" => new EllipseAnnotation(a.X, a.Y, b.X, b.Y),
        "textbox" => new TextAnnotation(a.X, a.Y, string.Empty) { Box = Rect2D.FromCorners(a, b) },

        // The four arrow kinds are one object told apart by two properties, which is also how
        // get(h, 'Type') reads them back.
        "line" => new ArrowAnnotation(a.X, a.Y, b.X, b.Y) { ShowHead = false },
        "doublearrow" => new ArrowAnnotation(a.X, a.Y, b.X, b.Y) { ShowTailHead = true },
        "textarrow" => new ArrowAnnotation(a.X, a.Y, b.X, b.Y) { Text = " " },
        _ => new ArrowAnnotation(a.X, a.Y, b.X, b.Y),
    };

    /// <summary>A four-element position vector.</summary>
    private static double[] Numbers4(string verb, JgsValue value, int line, int col) =>
        NumbersOfLength(verb, value, 4, "[x y w h]", line, col);

    /// <summary>A two-element pair of ends.</summary>
    private static double[] Numbers2(string verb, JgsValue value, int line, int col) =>
        NumbersOfLength(verb, value, 2, "a pair", line, col);

    private static double[] NumbersOfLength(
        string verb, JgsValue value, int count, string shape, int line, int col)
    {
        double[] numbers = ToDoubles(verb, value, line, col);
        if (numbers.Length != count)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: this position is {shape}, so it needs {count} numbers, not {numbers.Length}.");
        }

        return numbers;
    }

    /// <summary>
    /// A string that is one of a documented set, matched without regard to case and refused by name.
    /// </summary>
    private static string OneOfWord(
        string verb, JgsValue value, IReadOnlyList<string> allowed, int line, int col)
    {
        string word = StrOf(verb, value, line, col);
        foreach (string candidate in allowed)
        {
            if (candidate.Equals(word, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        throw new JgsRuntimeException(line, col,
            $"{verb}: '{word}' is not one of {string.Join(", ", allowed)}.");
    }

    /// <summary>
    /// The text of a string-valued property. A cell of strings is joined with newlines, which is how
    /// MATLAB spells a multi-line label.
    /// </summary>
    internal static string AnnotationString(string name, JgsValue value, int line, int col) =>
        value.Type == JgsType.Cell
            ? string.Join("\n", value.AsCell.Select(item => StrOf(name, item, line, col)))
            : StrOf(name, value, line, col);
}
