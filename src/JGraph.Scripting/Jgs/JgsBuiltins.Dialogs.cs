using JGraph.Api;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The print and export dialogs, and <c>uiaxes</c> (M84) — the six names that stood on the graphics
/// exclusion list as "app building".
/// </summary>
/// <remarks>
/// <para>
/// The exclusion argument was the one this file's own coverage document makes twice over: <em>an
/// exclusion is a decision, and a decision whose grounds have gone is not a decision any more.</em>
/// The grounds were that these describe an application rather than a figure. But M71 built
/// <c>uicontextmenu</c> and <c>uimenu</c> for the callback seam, M75 made every <c>Paper*</c> property
/// real and said in its own header that they were waiting for something that printed, and M80 put a
/// strip of buttons over an axes. These six describe a figure this build already has.
/// </para>
/// <para>
/// Five of them want a window, and each asks the host for one through
/// <see cref="IScriptFigureFiles"/>. A host with no window answers false and the verb refuses by
/// name, saying which non-interactive verb does the job — which is M60's fourth answer for a verb
/// that wants a window, and what keeps a batch run free of a modal dialog.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the six.</summary>
    internal static void RegisterDialogBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        void DefineSilent(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        // printdlg(fig) prints; printdlg('-setup', fig) is MATLAB's spelling of page setup, and it is
        // the same dialog pagesetupdlg opens.
        DefineSilent("printdlg", (args, line, col) =>
        {
            (FigureModel figure, IReadOnlyList<JgsValue> rest) = PeelFigure(args);
            bool setup = rest.Count > 0
                && StrOf("printdlg", rest[0], line, col).Equals("-setup", StringComparison.OrdinalIgnoreCase);

            // '-setup' may come first, before the figure, which PeelFigure would have walked past.
            if (!setup && args.Count > 0 && args[0].Type == JgsType.String
                && StrOf("printdlg", args[0], line, col).Equals("-setup", StringComparison.OrdinalIgnoreCase))
            {
                setup = true;
                (figure, _) = PeelFigure(args.Skip(1).ToList());
            }

            IScriptFigureFiles? files = host.FigureFiles;
            bool shown = files is not null && (setup ? files.PageSetup(figure) : files.PrintInteractive(figure));
            return NeedsAWindow(shown, "printdlg",
                setup
                    ? "set PaperType, PaperSize, PaperOrientation and PaperPosition directly"
                    : "print(fig, file, '-dpng') writes the same page without one",
                line, col);
        });

        DefineSilent("printpreview", (args, line, col) =>
        {
            (FigureModel figure, _) = PeelFigure(args);
            bool shown = host.FigureFiles?.PreviewPage(figure) == true;
            return NeedsAWindow(shown, "printpreview",
                "exportgraphics or print writes the page a preview would have shown", line, col);
        });

        DefineSilent("pagesetupdlg", (args, line, col) =>
        {
            (FigureModel figure, _) = PeelFigure(args);
            bool shown = host.FigureFiles?.PageSetup(figure) == true;
            return NeedsAWindow(shown, "pagesetupdlg",
                "set PaperType, PaperSize, PaperOrientation and PaperPosition directly", line, col);
        });

        DefineSilent("exportsetupdlg", (args, line, col) =>
        {
            (FigureModel figure, _) = PeelFigure(args);
            bool shown = host.FigureFiles?.ExportSetup(figure) == true;
            return NeedsAWindow(shown, "exportsetupdlg",
                "exportgraphics takes Resolution and BackgroundColor as arguments", line, col);
        });

        // exportapp writes the window rather than the drawing, which is the difference between it and
        // exportgraphics and the reason it is the one verb here with no non-interactive answer at all.
        DefineSilent("exportapp", (args, line, col) =>
        {
            (FigureModel figure, IReadOnlyList<JgsValue> rest) = PeelFigure(args);
            string path = FilePath("exportapp", rest, 0, ".png", line, col);
            bool written = host.FigureFiles?.CaptureWindow(figure, path) == true;
            return NeedsAWindow(written, "exportapp",
                "exportgraphics writes the figure itself, without the window around it", line, col);
        });

        // uiaxes is an axes with the defaults MATLAB's app-building one has. It lives in an ordinary
        // figure, because this build has no uifigure and will not grow one for this.
        // `ax = uiaxes` with no parentheses is the form every app-building script uses, so the bare
        // name has to make the axes rather than hand back the verb that would — the rule bubblesize
        // wrote and nexttile paid for again in M80.
        env.Declare("uiaxes", JgsValue.Function(new BuiltinFunction("uiaxes", (args, line, col) =>
        {
            (FigureModel figure, IReadOnlyList<JgsValue> rest) = PeelFigure(args);
            AxesModel axes = figure.AddAxes();
            JG.MakeCurrent(axes);

            // What a uiaxes is, here: an axes with MATLAB's app-building defaults. The fill behind the
            // whole cell starts at the figure's own colour, so one drawn and left alone looks like the
            // axes it is, and the toolbar a UIAxes shows is showing.
            axes.BackgroundColor = figure.Background;
            axes.Toolbar.Visible = true;

            JgsHandleEntry entry = JgsHandleRegistry.EntryFor(axes);
            var spec = new OptionSpec(
                "uiaxes", [], ["Position", "XLim", "YLim", "ZLim", "Color", "BackgroundColor", "Box", "Tag", "Title"]);
            ParsedArgs parsed = spec.Parse(rest, 0, line, col);
            foreach (string name in new[]
                     { "Position", "XLim", "YLim", "ZLim", "Color", "BackgroundColor", "Box", "Tag", "Title" })
            {
                if (parsed.Named(name) is { } value)
                {
                    JgsGraphicsProperties.Set(entry, name, value, line, col);
                }
            }

            return JgsHandleRegistry.For(axes);
        })
        { AutoCallsBare = true }));
    }

    /// <summary>
    /// The answer a verb gives when the host had no window to show: a refusal that names the verb
    /// which does the same job without one.
    /// </summary>
    private static JgsValue NeedsAWindow(bool shown, string verb, string instead, int line, int col) =>
        shown
            ? JgsValue.Null
            : throw new JgsRuntimeException(line, col,
                $"{verb} opens a window, and this host has none — {instead}.");
}
