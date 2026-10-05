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
            env.Builtins.Register(name, JgsValue.Function(
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
            // R2025b's forms and refusals (U4, probes u4_dialogs and u4_messages).
            if (args.Count < 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
            }

            if (args.Count > 2)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
            }

            if (args[0].Type != JgsType.Number || !JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? named)
                || named.Target is not FigureModel figure)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:print:ExportHandleNotValid", "Specified handle is not valid for export.");
            }

            if (!ScriptEventQueue.PumpInstalled || host.FigureFiles is null)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:print:HeadlessFigureUnsupported",
                    "Running using -nodisplay or other startup options that prevent figures from displaying is not supported.");
            }

            string path = host.ResolveForWrite(FilePath("exportapp", args, 1, ".png", line, col));
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".tif" or ".tiff" or ".bmp"))
            {
                throw new JgsRuntimeException(line, col,
                    $"exportapp writes a picture of the window as .png, .jpg, .tif or .bmp, and '{extension}' is none of them — "
                    + "R2025b's .pdf is not written here.");
            }

            // The barrier: the figure is shown and what the script changed is in its window before
            // the window is photographed.
            figure.Visible = true;
            JG.TouchFigure(figure);
            host.ShowTouchedFigures();
            ScriptComponentFrames.Flush(force: true);
            ScriptRenderPump.Flush();
            if (!host.FigureFiles.CaptureWindow(figure, path))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:print:HeadlessFigureUnsupported",
                    "Running using -nodisplay or other startup options that prevent figures from displaying is not supported.");
            }

            return JgsValue.Null;
        });

        // uiaxes moved to JgsBuiltins.UiComponents.cs with the rest of a uifigure's components (U5).
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
