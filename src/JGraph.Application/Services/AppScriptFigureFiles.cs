using System.IO;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Export;
using JGraph.Imaging;
using JGraph.Scripting;
using JGraph.Serialization;

namespace JGraph.Application.Services;

/// <summary>
/// The app's <see cref="IScriptFigureFiles"/>: <c>savefigure</c>/<c>loadfigure</c> ride the versioned
/// <see cref="GraphFormat"/> document format and <c>exportfigure</c> the UI-free
/// <see cref="FigureExporter"/> (format by extension). IO and format exceptions propagate — the
/// script builtins turn them into diagnostics with the script's line/column.
/// </summary>
public sealed class AppScriptFigureFiles : IScriptFigureFiles
{
    /// <inheritdoc />
    public void Save(FigureModel figure, string path) => GraphFormat.Save(figure, path);

    /// <inheritdoc />
    public FigureModel Load(string path) => GraphFormat.Load(path);

    /// <inheritdoc />
    public void Export(FigureModel figure, string path, double scale = 1.0, Size2D? size = null) =>
        FigureExporter.Export(figure, path, new ExportOptions { Scale = scale, Size = size });

    /// <inheritdoc />
    public ImageBuffer Capture(FigureModel figure, double scale)
    {
        (int width, int height, byte[] rgba) =
            FigureExporter.RenderRgba(figure, new ExportOptions { Scale = scale });
        return ImageBuffer.FromRgba(rgba, width, height);
    }

    /// <inheritdoc />
    public bool CopyToClipboard(FigureModel figure, double scale) =>
        JGraph.Controls.FigureClipboard.CopyImage(figure, new ExportOptions { Scale = scale });

    // --- The dialogs and the window capture (M84) --------------------------------------------------
    //
    // The default implementations on the interface answer false, which is what a batch run wants and
    // what the verbs turn into a refusal. These are the overrides for a host that does have a window.

    private readonly Printing.FigurePrintService _printing = new();

    /// <inheritdoc />
    public bool PrintInteractive(FigureModel figure) => _printing.Print(figure);

    /// <inheritdoc />
    public bool PreviewPage(FigureModel figure) => _printing.Preview(figure);

    /// <inheritdoc />
    public bool PageSetup(FigureModel figure) => _printing.PageSetup(figure);

    /// <inheritdoc />
    public bool ExportSetup(FigureModel figure) => _printing.ExportSetup(figure);

    /// <inheritdoc />
    /// <remarks>
    /// The one export that goes through the control rather than the renderer. M80 put the axes toolbar
    /// in JGraph.Controls precisely so that no export could carry it; <c>exportapp</c> is the verb
    /// whose whole point is a picture of the application, so it is the one that has to.
    /// </remarks>
    public bool CaptureWindow(FigureModel figure, string path)
    {
        // The active window, which is the one a script asking for a picture of the application means.
        System.Windows.Window? window = System.Windows.Application.Current?.Windows
            .OfType<System.Windows.Window>()
            .FirstOrDefault(w => w.IsActive);

        if (window is null || window.ActualWidth <= 0 || window.ActualHeight <= 0)
        {
            return false;
        }

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)System.Math.Ceiling(window.ActualWidth),
            (int)System.Math.Ceiling(window.ActualHeight),
            96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using FileStream file = File.Create(path);
        encoder.Save(file);
        return true;
    }
}
