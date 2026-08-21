using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Putting a figure on paper: MATLAB's <c>print</c> and <c>saveas</c>, and the page state they are
/// the only consumers of. Until M75 the Paper properties would have described a page nothing ever
/// printed on, which is why they waited for these two verbs rather than landing beside the rest of
/// the figure's names.
/// </summary>
/// <remarks>
/// <c>print</c> means two different things in the two dialects. JGS has always used it for the
/// console — <c>print("x is", x)</c> — and that is what a JGS script's own tests are written
/// against, so the paper verb replaces it only under the MATLAB dialect, where a script writing to
/// the console says <c>fprintf</c> or <c>disp</c> and <c>print</c> can only mean the printer.
/// <c>saveas</c> is a new name in both dialects and so is purely additive.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>A colormap by name, or an error listing the names there are.</summary>
    private static Core.Drawing.Colormap NamedColormap(string name, int line, int col) =>
        Core.Drawing.Colormap.TryGetByName(name, out Core.Drawing.Colormap found)
            ? found
            : throw new JgsRuntimeException(line, col,
                $"Unknown colormap '{name}'. Known colormaps: "
                + $"{string.Join(", ", Core.Drawing.Colormap.KnownNames)}.");

    /// <summary>Hands an axes' colour choice back to the plots that hold working copies of it.</summary>
    private static void ReseedPlots(AxesModel axes)
    {
        foreach (PlotObject plot in axes.Plots)
        {
            plot.AdoptAxesDefaults(axes);
        }
    }

    /// <summary>The device switches <c>print</c> understands, and the extension each one means.</summary>
    private static readonly Dictionary<string, string> PrintDevices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png"] = ".png",
        ["jpeg"] = ".jpg",
        ["jpg"] = ".jpg",
        ["pdf"] = ".pdf",
        ["svg"] = ".svg",
        ["bmp"] = ".bmp",
        ["tiff"] = ".tif",
        ["tif"] = ".tif",
        ["bitmap"] = ".png",
        ["meta"] = ".svg",
    };

    /// <summary>The format words <c>saveas</c> takes as its third argument, and what each writes.</summary>
    private static readonly Dictionary<string, string> SaveAsFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png"] = ".png",
        ["jpeg"] = ".jpg",
        ["jpg"] = ".jpg",
        ["pdf"] = ".pdf",
        ["svg"] = ".svg",
        ["bmp"] = ".bmp",
        ["tiff"] = ".tif",
        ["tif"] = ".tif",
        ["fig"] = ".fig",
        ["graph"] = ".graph",
    };

    private static void RegisterPrinting(JgsEnvironment env, JGraphScriptGlobals host, JgsDialect dialect)
    {
        // saveas is new in both dialects; print replaces the console verb only under MATLAB.
        env.Declare("saveas", JgsValue.Function(new BuiltinFunction(
            "saveas", (args, line, col) => SaveAs(host, args, line, col))));

        if (dialect.IsMatlab)
        {
            env.Declare("print", JgsValue.Function(new BuiltinFunction(
                "print", (args, line, col) => Print(host, args, line, col))));
        }
    }

    /// <summary>
    /// <c>saveas(fig, 'name.png')</c> and <c>saveas(fig, 'name', 'pdf')</c>. A <c>.fig</c> is the
    /// document format rather than a picture, which is what MATLAB's own saveas does with it.
    /// </summary>
    private static JgsValue SaveAs(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("saveas", args, 1, 3, line, col);
        (FigureModel figure, IReadOnlyList<JgsValue> rest) = PeelFigure(args);
        if (rest.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "saveas expects the name of a file to write.");
        }

        string path = StrOf("saveas", rest[0], line, col);
        if (rest.Count > 1)
        {
            string word = StrOf("saveas", rest[1], line, col);
            if (!SaveAsFormats.TryGetValue(word, out string? extension))
            {
                throw new JgsRuntimeException(line, col,
                    $"saveas does not know the format '{word}'. Known formats: "
                    + $"{string.Join(", ", SaveAsFormats.Keys)}.");
            }

            path = Path.ChangeExtension(path, extension);
        }
        else if (!Path.HasExtension(path))
        {
            path += ".fig";
        }

        return WriteFigure(host, "saveas", figure, path, resolution: 96, line, col);
    }

    /// <summary>
    /// <c>print</c>, MATLAB's spelling: a file name, an optional figure, and switches that start
    /// with a minus — <c>-dpng</c> for the format, <c>-r200</c> for the resolution.
    /// </summary>
    private static JgsValue Print(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        (FigureModel figure, IReadOnlyList<JgsValue> rest) = PeelFigure(args);

        string? path = null;
        string? extension = null;
        double resolution = 96;
        foreach (JgsValue argument in rest)
        {
            string text = StrOf("print", argument, line, col);
            if (!text.StartsWith('-'))
            {
                path = text;
                continue;
            }

            string switchText = text[1..];
            if (switchText.StartsWith('d'))
            {
                string device = switchText[1..];
                extension = PrintDevices.TryGetValue(device, out string? found)
                    ? found
                    : throw new JgsRuntimeException(line, col,
                        $"print does not know the device '-d{device}'. Known devices: "
                        + $"{string.Join(", ", PrintDevices.Keys.Select(k => "-d" + k))}.");
            }
            else if (switchText.StartsWith('r'))
            {
                if (!double.TryParse(switchText[1..],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out resolution)
                    || resolution <= 0)
                {
                    throw new JgsRuntimeException(line, col,
                        $"print expects a positive resolution, such as -r300, but got '{text}'.");
                }
            }
            else
            {
                throw new JgsRuntimeException(line, col,
                    $"print does not understand the switch '{text}'. It takes a device such as "
                    + "-dpng and a resolution such as -r300.");
            }
        }

        if (path is null)
        {
            // MATLAB prints to the default printer here. There is none to print to, and quietly
            // doing nothing would look like success, so it is refused by name.
            throw new JgsRuntimeException(line, col,
                "print needs the name of a file to write; printing to a printer is not supported.");
        }

        path = extension is not null
            ? Path.ChangeExtension(path, extension)
            : Path.HasExtension(path) ? path : path + ".png";

        return WriteFigure(host, "print", figure, path, resolution, line, col);
    }

    /// <summary>
    /// Writes one figure at the size the page asks for. An automatic paper position is the size the
    /// figure is on screen, which is what makes an unconfigured <c>print</c> produce what
    /// <c>exportgraphics</c> would; a manual one is the rectangle a script chose, in inches, turned
    /// back into the ninety-six-to-the-inch units everything here is measured in.
    /// </summary>
    private static JgsValue WriteFigure(
        JGraphScriptGlobals host, string verb, FigureModel figure, string path,
        double resolution, int line, int col)
    {
        if (Path.GetExtension(path).Equals(".fig", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).Equals(".graph", StringComparison.OrdinalIgnoreCase))
        {
            return Attempt(() =>
            {
                host.savefigure(path, figure);
                figure.FileName = path;
            }, line, col);
        }

        Size2D? size = null;
        if (!figure.PaperPositionAuto)
        {
            Rect2D page = figure.PaperPosition;
            size = new Size2D(page.Width * 96, page.Height * 96);
        }

        double scale = resolution / 96.0;
        return Attempt(() => host.exportfigure(path, figure, scale, size), line, col);
    }
}
