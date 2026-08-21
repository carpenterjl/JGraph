using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The figure's own property surface: the window it lives in, the page it prints on, the keyboard
/// and mouse events it hears, and the colour and transparency maps its axes fall back on. Before
/// M75 a figure answered to twenty-four of MATLAB's sixty-six names; this file is the other
/// forty-two, and after it a figure answers to all of them.
/// </summary>
/// <remarks>
/// A few are answered truthfully rather than implemented — a renderer that is always painters says
/// so and refuses to be told otherwise, rather than accepting a word it would ignore. Each such
/// refusal is a recorded divergence, and every one of them is a property MATLAB itself documents as
/// having no effect in most cases.
/// </remarks>
internal static partial class JgsGraphicsProperties
{
    private static void AddFigureBlock(IDictionary<string, GraphicsProperty> table)
    {
        AddFigureMaps(table);
        AddFigureWindow(table);
        AddFigurePointer(table);
        AddFigurePaper(table);
        AddFigureEvents(table);
        AddFigureTruths(table);
    }

    // --- The maps an axes falls back on ---------------------------------------------------------

    private static void AddFigureMaps(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "Colormap",
            entry => ColorTable((Figure(entry).Colormap ?? Colormap.Parula).Stops),
            (entry, value, line, col) =>
            {
                FigureModel figure = Figure(entry);
                figure.Colormap = ReadColormap("Colormap", value, line, col);

                // An axes that never chose its own reads through to here, but the plots already in
                // it hold working copies seeded when they were made, so they have to be told.
                foreach (AxesModel axes in figure.Axes)
                {
                    if (axes.Colormap is null)
                    {
                        foreach (PlotObject plot in axes.Plots)
                        {
                            plot.AdoptAxesDefaults(axes);
                        }
                    }
                }
            });

        Put(table, "Alphamap",
            entry =>
            {
                IReadOnlyList<double> map = Figure(entry).Alphamap ?? AlphaSampler.DefaultMap;
                return JgsMatrix.FromColumnMajor([.. map], 1, map.Count);
            },
            (entry, value, line, col) => Figure(entry).Alphamap =
                ReadAlphamap("Alphamap", value, line, col));
    }

    // --- The window ------------------------------------------------------------------------------

    private static void AddFigureWindow(IDictionary<string, GraphicsProperty> table)
    {
        // Position and InnerPosition are the same rectangle in MATLAB too: a figure has no
        // decoration of its own between the two, so there is nothing for them to differ by.
        AddFigurePosition(table, "Position");
        AddFigurePosition(table, "InnerPosition");

        // The window's bounds including its title bar and border, when a window is there to ask.
        // Headless, there is no chrome, so the honest answer is the drawable area itself.
        Put(table, "OuterPosition",
            entry =>
            {
                FigureModel figure = Figure(entry);
                return ScriptGraphicsCallbacks.WindowBoundsProvider?.Invoke(figure) is { } outer
                    ? Row(outer.X, outer.Y, outer.Width, outer.Height)
                    : Row(figure.Position.X, figure.Position.Y, figure.Size.Width, figure.Size.Height);
            },
            (entry, value, line, col) =>
            {
                double[] box = Numbers("OuterPosition", value, 4, line, col);
                FigureModel figure = Figure(entry);
                figure.Position = new Point2D(box[0], box[1]);
                figure.Size = new Size2D(System.Math.Max(1, box[2]), System.Math.Max(1, box[3]));
            });

        Put(table, "NumberTitle",
            entry => OnOff(Figure(entry).NumberTitle),
            (entry, value, line, col) => Figure(entry).NumberTitle = ToOnOff("NumberTitle", value, line, col));

        Put(table, "Resize",
            entry => OnOff(Figure(entry).Resizable),
            (entry, value, line, col) => Figure(entry).Resizable = ToOnOff("Resize", value, line, col));

        Put(table, "ToolBar",
            entry => JgsValue.Str(Figure(entry).ToolBar switch
            {
                FigureToolBarMode.Figure => "figure",
                FigureToolBarMode.None => "none",
                _ => "auto",
            }),
            (entry, value, line, col) =>
            {
                string word = JgsBuiltins.StrOf("ToolBar", value, line, col);
                Figure(entry).ToolBar = word.ToLowerInvariant() switch
                {
                    "auto" => FigureToolBarMode.Auto,
                    "figure" => FigureToolBarMode.Figure,
                    "none" => FigureToolBarMode.None,
                    _ => throw new JgsRuntimeException(line, col,
                        $"ToolBar is 'auto', 'figure' or 'none', but got '{word}'."),
                };
            });

        Put(table, "WindowState",
            entry => JgsValue.Str(Figure(entry).WindowState.ToString().ToLowerInvariant()),
            (entry, value, line, col) =>
            {
                string word = JgsBuiltins.StrOf("WindowState", value, line, col);
                Figure(entry).WindowState = word.ToLowerInvariant() switch
                {
                    "normal" => FigureWindowState.Normal,
                    "minimized" => FigureWindowState.Minimized,
                    "maximized" => FigureWindowState.Maximized,
                    "fullscreen" => FigureWindowState.Fullscreen,
                    _ => throw new JgsRuntimeException(line, col,
                        $"WindowState is 'normal', 'minimized', 'maximized' or 'fullscreen', but got '{word}'."),
                };
            });

        Put(table, "FileName",
            entry => JgsValue.Str(Figure(entry).FileName),
            (entry, value, line, col) => Figure(entry).FileName =
                JgsBuiltins.StrOf("FileName", value, line, col));

        Put(table, "InvertHardcopy",
            entry => OnOff(Figure(entry).InvertHardcopy),
            (entry, value, line, col) => Figure(entry).InvertHardcopy =
                ToOnOff("InvertHardcopy", value, line, col));

        Put(table, "GraphicsSmoothing",
            entry => OnOff(Figure(entry).GraphicsSmoothing),
            (entry, value, line, col) => Figure(entry).GraphicsSmoothing =
                ToOnOff("GraphicsSmoothing", value, line, col));

        Put(table, "NextPlot",
            entry => JgsValue.Str(Figure(entry).NextPlot switch
            {
                FigureNextPlot.Replace => "replace",
                FigureNextPlot.ReplaceChildren => "replacechildren",
                FigureNextPlot.New => "new",
                _ => "add",
            }),
            (entry, value, line, col) =>
            {
                string word = JgsBuiltins.StrOf("NextPlot", value, line, col);
                Figure(entry).NextPlot = word.ToLowerInvariant() switch
                {
                    "add" => FigureNextPlot.Add,
                    "replace" => FigureNextPlot.Replace,
                    "replacechildren" => FigureNextPlot.ReplaceChildren,
                    "new" => FigureNextPlot.New,
                    _ => throw new JgsRuntimeException(line, col,
                        $"NextPlot is 'add', 'replace', 'replacechildren' or 'new', but got '{word}'."),
                };
            });
    }

    private static void AddFigurePosition(IDictionary<string, GraphicsProperty> table, string name) =>
        Put(table, name,
            entry =>
            {
                FigureModel figure = Figure(entry);
                return Row(figure.Position.X, figure.Position.Y, figure.Size.Width, figure.Size.Height);
            },
            (entry, value, line, col) =>
            {
                double[] box = Numbers(name, value, 4, line, col);
                FigureModel figure = Figure(entry);
                figure.Position = new Point2D(box[0], box[1]);
                figure.Size = new Size2D(System.Math.Max(1, box[2]), System.Math.Max(1, box[3]));
            });

    // --- The pointer, and where it is -------------------------------------------------------------

    private static void AddFigurePointer(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "Pointer",
            entry => JgsValue.Str(Figure(entry).Pointer.ToString().ToLowerInvariant()),
            (entry, value, line, col) =>
            {
                string word = JgsBuiltins.StrOf("Pointer", value, line, col);
                Figure(entry).Pointer = Enum.TryParse(word, ignoreCase: true, out PointerShape shape)
                    ? shape
                    : throw new JgsRuntimeException(line, col,
                        $"Unknown pointer '{word}'. Known pointers: {string.Join(", ", PointerWords)}.");
            });

        // A custom pointer would be drawn from these; nothing here draws one, so the honest answer
        // is the all-transparent 16 by 16 grid MATLAB starts with and its top-left hot spot.
        Put(table, "PointerShapeCData",
            static entry => JgsMatrix.FromColumnMajor(NanGrid(), 16, 16));
        Put(table, "PointerShapeHotSpot", static entry => Row(1, 1));

        Put(table, "CurrentPoint",
            entry =>
            {
                FigureModel figure = Figure(entry);
                return figure.CurrentPointPx is { } pixel
                    ? Row(pixel.X, figure.Size.Height - pixel.Y)
                    : Row(0, 0);
            });

        Put(table, "CurrentCharacter",
            entry => JgsValue.Str(Figure(entry).CurrentCharacter),
            (entry, value, line, col) => Figure(entry).CurrentCharacter =
                JgsBuiltins.StrOf("CurrentCharacter", value, line, col));

        Put(table, "SelectionType",
            entry => JgsValue.Str(Figure(entry).SelectionType.ToString().ToLowerInvariant()),
            (entry, value, line, col) =>
            {
                string word = JgsBuiltins.StrOf("SelectionType", value, line, col);
                Figure(entry).SelectionType = word.ToLowerInvariant() switch
                {
                    "normal" => SelectionKind.Normal,
                    "extend" => SelectionKind.Extend,
                    "alt" => SelectionKind.Alt,
                    "open" => SelectionKind.Open,
                    _ => throw new JgsRuntimeException(line, col,
                        $"SelectionType is 'normal', 'extend', 'alt' or 'open', but got '{word}'."),
                };
            });

        // The last object clicked, which is what gco answers. It is process-wide here rather than
        // per figure, because one pointer clicks one thing at a time.
        Put(table, "CurrentObject",
            static entry => JgsGraphicsCallbackState.CurrentObject is { BeingDeleted: false } clicked
                ? JgsHandleRegistry.For(clicked)
                : JgsValue.Array([]));
    }

    // --- The page ---------------------------------------------------------------------------------

    private static void AddFigurePaper(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "PaperUnits",
            entry => JgsValue.Str(Figure(entry).PaperUnits.ToString().ToLowerInvariant()),
            (entry, value, line, col) =>
            {
                string word = JgsBuiltins.StrOf("PaperUnits", value, line, col);
                Figure(entry).PaperUnits = word.ToLowerInvariant() switch
                {
                    "inches" => PaperUnitType.Inches,
                    "centimeters" => PaperUnitType.Centimeters,
                    "normalized" => PaperUnitType.Normalized,
                    "points" => PaperUnitType.Points,
                    _ => throw new JgsRuntimeException(line, col,
                        $"PaperUnits is 'inches', 'centimeters', 'normalized' or 'points', but got '{word}'."),
                };
            });

        Put(table, "PaperType",
            entry => JgsValue.Str(Figure(entry).PaperType),
            (entry, value, line, col) =>
            {
                string word = JgsBuiltins.StrOf("PaperType", value, line, col);
                if (PaperSizes.Find(word) is null)
                {
                    throw new JgsRuntimeException(line, col,
                        $"Unknown paper type '{word}'. Known types: {string.Join(", ", PaperSizes.KnownNames)}.");
                }

                Figure(entry).PaperType = word.ToLowerInvariant();
            });

        Put(table, "PaperOrientation",
            entry => JgsValue.Str(Figure(entry).PaperOrientation == PaperOrientationType.Landscape
                ? "landscape"
                : "portrait"),
            (entry, value, line, col) =>
            {
                string word = JgsBuiltins.StrOf("PaperOrientation", value, line, col);
                Figure(entry).PaperOrientation = word.ToLowerInvariant() switch
                {
                    "portrait" => PaperOrientationType.Portrait,
                    "landscape" => PaperOrientationType.Landscape,
                    _ => throw new JgsRuntimeException(line, col,
                        $"PaperOrientation is 'portrait' or 'landscape', but got '{word}'."),
                };
            });

        // Held in inches and reported in whatever units were asked for, so that changing the units
        // changes the numbers without moving the page — which is what changing units means.
        Put(table, "PaperSize",
            entry =>
            {
                FigureModel figure = Figure(entry);
                Size2D inches = figure.EffectivePaperSize();
                double per = PageUnit(figure, inches);
                return Row(inches.Width / per, inches.Height / per);
            },
            (entry, value, line, col) =>
            {
                FigureModel figure = Figure(entry);
                double[] pair = Numbers("PaperSize", value, 2, line, col);
                double per = PageUnit(figure, figure.EffectivePaperSize());
                if (pair[0] * per <= 0 || pair[1] * per <= 0)
                {
                    throw new JgsRuntimeException(line, col, "PaperSize needs a positive width and height.");
                }

                // The stored size is the portrait one, so that turning the page over and back again
                // gives the size that was set rather than its transpose.
                var asked = new Size2D(pair[0] * per, pair[1] * per);
                figure.PaperSize = figure.PaperOrientation == PaperOrientationType.Landscape
                    ? new Size2D(asked.Height, asked.Width)
                    : asked;
            });

        Put(table, "PaperPosition",
            entry =>
            {
                FigureModel figure = Figure(entry);
                Rect2D box = EffectivePaperPosition(figure);
                double per = PageUnit(figure, figure.EffectivePaperSize());
                return Row(box.X / per, box.Y / per, box.Width / per, box.Height / per);
            },
            (entry, value, line, col) =>
            {
                FigureModel figure = Figure(entry);
                double[] box = Numbers("PaperPosition", value, 4, line, col);
                double per = PageUnit(figure, figure.EffectivePaperSize());
                if (box[2] * per <= 0 || box[3] * per <= 0)
                {
                    throw new JgsRuntimeException(line, col, "PaperPosition needs a positive width and height.");
                }

                figure.PaperPosition = new Rect2D(box[0] * per, box[1] * per, box[2] * per, box[3] * per);

                // MATLAB's own rule: saying where on the page the figure goes is saying to stop
                // taking the size off the screen.
                figure.PaperPositionAuto = false;
            });

        Put(table, "PaperPositionMode",
            entry => AutoManual(!Figure(entry).PaperPositionAuto),
            (entry, value, line, col) =>
            {
                FigureModel figure = Figure(entry);
                bool manual = ToAutoManual("PaperPositionMode", value, line, col);
                if (manual && figure.PaperPositionAuto)
                {
                    // Freezing takes the size that would have been used, so that turning the mode to
                    // manual on its own does not move the picture.
                    figure.PaperPosition = EffectivePaperPosition(figure);
                }

                figure.PaperPositionAuto = !manual;
            });
    }

    // --- The events ------------------------------------------------------------------------------

    private static void AddFigureEvents(IDictionary<string, GraphicsProperty> table)
    {
        AddCallbackSlot(table, "KeyPressFcn",
            static entry => entry.KeyPressFcn, static (entry, value) => entry.KeyPressFcn = value);
        AddCallbackSlot(table, "KeyReleaseFcn",
            static entry => entry.KeyReleaseFcn, static (entry, value) => entry.KeyReleaseFcn = value);
        AddCallbackSlot(table, "WindowKeyPressFcn",
            static entry => entry.WindowKeyPressFcn, static (entry, value) => entry.WindowKeyPressFcn = value);
        AddCallbackSlot(table, "WindowKeyReleaseFcn",
            static entry => entry.WindowKeyReleaseFcn, static (entry, value) => entry.WindowKeyReleaseFcn = value);
        AddCallbackSlot(table, "WindowButtonDownFcn",
            static entry => entry.WindowButtonDownFcn, static (entry, value) => entry.WindowButtonDownFcn = value);
        AddCallbackSlot(table, "WindowButtonUpFcn",
            static entry => entry.WindowButtonUpFcn, static (entry, value) => entry.WindowButtonUpFcn = value);
        AddCallbackSlot(table, "WindowButtonMotionFcn",
            static entry => entry.WindowButtonMotionFcn, static (entry, value) => entry.WindowButtonMotionFcn = value);
        AddCallbackSlot(table, "WindowScrollWheelFcn",
            static entry => entry.WindowScrollWheelFcn, static (entry, value) => entry.WindowScrollWheelFcn = value);

        // MATLAB's own note is that ResizeFcn is not recommended and SizeChangedFcn replaces it.
        // Two names over one slot is what that means: whichever is written is what fires, and
        // reading either gives back what was written.
        AddCallbackSlot(table, "ResizeFcn",
            static entry => entry.SizeChangedFcn, static (entry, value) => entry.SizeChangedFcn = value);
    }

    // --- The truths, and the refusals ---------------------------------------------------------------

    private static void AddFigureTruths(IDictionary<string, GraphicsProperty> table)
    {
        // Each of these answers what is actually so and refuses to be told otherwise, rather than
        // accepting a word it would then ignore. A property that lies is worse than one that says no.
        OnlyWord(table, "Units", "pixels", "A figure is measured in pixels");
        OnlyWord(table, "Renderer", "painters", "JGraph draws with painters");
        OnlyWord(table, "RendererMode", "auto", "The renderer is not chosen by hand");
        OnlyWord(table, "WindowStyle", "normal", "Figures are ordinary windows here");
        OnlyWord(table, "MenuBar", "none", "A figure has no menu bar");
        OnlyWord(table, "DockControls", "off", "Figures cannot be docked");
        OnlyWord(table, "IntegerHandle", "on", "Figures are numbered");

        // MATLAB documents figure Clipping as having no effect, and it has none here either. Saying
        // 'on' back is the whole of it.
        OnlyWord(table, "Clipping", "on", "A figure clips its children to itself");
    }

    /// <summary>A property with exactly one true answer, which refuses every other word by name.</summary>
    private static void OnlyWord(
        IDictionary<string, GraphicsProperty> table, string name, string answer, string because) =>
        Put(table, name,
            entry => JgsValue.Str(answer),
            (entry, value, line, col) =>
            {
                string word = JgsBuiltins.StrOf(name, value, line, col);
                if (!word.Equals(answer, StringComparison.OrdinalIgnoreCase))
                {
                    throw new JgsRuntimeException(line, col,
                        $"{because}, so {name} is '{answer}' and cannot be '{word}'.");
                }
            });

    // --- Shared plumbing --------------------------------------------------------------------------

    private static FigureModel Figure(JgsHandleEntry entry) => (FigureModel)entry.Target;

    private static readonly string[] PointerWords =
        [.. Enum.GetNames<PointerShape>().Select(n => n.ToLowerInvariant())];

    private static double[] NanGrid()
    {
        var grid = new double[16 * 16];
        Array.Fill(grid, double.NaN);
        return grid;
    }

    /// <summary>How many inches one paper unit is worth, given the page it is a fraction of.</summary>
    private static double PageUnit(FigureModel figure, Size2D page) =>
        figure.PaperUnits == PaperUnitType.Normalized
            ? System.Math.Max(page.Width, page.Height)
            : PaperSizes.InchesPer(figure.PaperUnits);

    /// <summary>
    /// Where on the page the figure prints, in inches. Automatic means the size it is on screen at
    /// ninety-six pixels to the inch, centred — which is what makes a saved copy the size it looks.
    /// </summary>
    internal static Rect2D EffectivePaperPosition(FigureModel figure)
    {
        if (!figure.PaperPositionAuto)
        {
            return figure.PaperPosition;
        }

        Size2D page = figure.EffectivePaperSize();
        double width = figure.Size.Width / 96.0;
        double height = figure.Size.Height / 96.0;
        return new Rect2D((page.Width - width) / 2, (page.Height - height) / 2, width, height);
    }

    private static Colormap ReadColormap(string what, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.String)
        {
            string name = JgsBuiltins.StrOf(what, value, line, col);
            return Colormap.TryGetByName(name, out Colormap named)
                ? named
                : throw new JgsRuntimeException(line, col,
                    $"Unknown colormap '{name}'. Known colormaps: {string.Join(", ", Colormap.KnownNames)}.");
        }

        return (Colormap)ValueBridge.FromValue(typeof(Colormap), what, value, line, col)!;
    }

    private static double[] ReadAlphamap(string what, JgsValue value, int line, int col)
    {
        double[] map = JgsBuiltins.ToDoubles(what, value, line, col);
        if (map.Length == 0)
        {
            throw new JgsRuntimeException(line, col, $"{what} needs at least one transparency.");
        }

        foreach (double transparency in map)
        {
            if (!double.IsFinite(transparency) || transparency < 0 || transparency > 1)
            {
                throw new JgsRuntimeException(line, col,
                    $"{what} entries are between 0 and 1, but got {transparency}.");
            }
        }

        return map;
    }
}
